using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Legend.Storage;

internal static class GuildMembersJson
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serialize(IReadOnlyList<string> ids)
        => JsonSerializer.Serialize(ids, Opts);

    public static List<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, Opts) ?? new();
        }
        catch
        {
            return new();
        }
    }
}

public static class GuildRanksJson
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serialize(IReadOnlyDictionary<string, string> ranks)
        => JsonSerializer.Serialize(ranks, Opts);

    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new(StringComparer.Ordinal);
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json, Opts);
            return d is null
                ? new(StringComparer.Ordinal)
                : new Dictionary<string, string>(d, StringComparer.Ordinal);
        }
        catch
        {
            return new(StringComparer.Ordinal);
        }
    }

    public static void RemoveMember(ref string ranksJson, string characterId)
    {
        var map = Parse(ranksJson);
        if (map.Remove(characterId))
            ranksJson = Serialize(map);
    }
}

internal sealed class InMemoryGuildStore : IGuildStore
{
    private readonly ConcurrentDictionary<string, GuildRecord> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _nameToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<GuildWarehouseAuditRecord>> _audits = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<GuildRecord?> GetByIdAsync(string guildId, CancellationToken ct = default)
        => Task.FromResult(_byId.TryGetValue(guildId, out var g) ? g.Clone() : null);

    public Task<GuildRecord?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (!_nameToId.TryGetValue(name.Trim(), out var id))
            return Task.FromResult<GuildRecord?>(null);
        return GetByIdAsync(id, ct);
    }

    public Task<(bool Ok, string? Error, GuildRecord? Guild)> CreateAsync(string name, string leaderCharacterId, CancellationToken ct = default)
    {
        name = name.Trim();
        lock (_gate)
        {
            if (_nameToId.ContainsKey(name))
                return Task.FromResult<(bool, string?, GuildRecord?)>((false, "行会名已存在", null));
            var g = new GuildRecord
            {
                GuildId = Guid.NewGuid().ToString("N"),
                Name = name,
                LeaderCharacterId = leaderCharacterId,
                MemberCharacterIds = new List<string> { leaderCharacterId },
                WarehouseJson = "[]",
                RanksJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            };
            if (!_nameToId.TryAdd(name, g.GuildId))
                return Task.FromResult<(bool, string?, GuildRecord?)>((false, "行会名已存在", null));
            _byId[g.GuildId] = g;
            return Task.FromResult<(bool, string?, GuildRecord?)>((true, null, g.Clone()));
        }
    }

    public Task SaveWarehouseAsync(string guildId, string warehouseJson, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(guildId, out var g))
                g.WarehouseJson = string.IsNullOrWhiteSpace(warehouseJson) ? "[]" : warehouseJson;
        }
        return Task.CompletedTask;
    }

    public Task SaveRanksAsync(string guildId, string ranksJson, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(guildId, out var g))
                g.RanksJson = string.IsNullOrWhiteSpace(ranksJson) ? "{}" : ranksJson;
        }
        return Task.CompletedTask;
    }

    public Task AppendWarehouseAuditAsync(GuildWarehouseAuditRecord entry, CancellationToken ct = default)
    {
        var list = _audits.GetOrAdd(entry.GuildId, _ => new List<GuildWarehouseAuditRecord>());
        lock (list)
        {
            list.Add(entry.Clone());
            const int keep = 500;
            if (list.Count > keep)
                list.RemoveRange(0, list.Count - keep);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GuildWarehouseAuditRecord>> ListWarehouseAuditAsync(string guildId, int limit, CancellationToken ct = default)
    {
        if (!_audits.TryGetValue(guildId, out var list))
            return Task.FromResult<IReadOnlyList<GuildWarehouseAuditRecord>>(Array.Empty<GuildWarehouseAuditRecord>());
        lock (list)
        {
            int take = Math.Max(1, limit);
            var slice = list.Count <= take
                ? list.Select(a => a.Clone()).Reverse().ToList()
                : list.Skip(Math.Max(0, list.Count - take)).Select(a => a.Clone()).Reverse().ToList();
            return Task.FromResult<IReadOnlyList<GuildWarehouseAuditRecord>>(slice);
        }
    }

    public Task<(bool Ok, string? Error, GuildRecord? Guild)> AddMemberAsync(string guildId, string characterId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(guildId, out var g))
                return Task.FromResult<(bool, string?, GuildRecord?)>((false, "行会不存在", null));
            if (g.MemberCharacterIds.Contains(characterId, StringComparer.Ordinal))
                return Task.FromResult<(bool, string?, GuildRecord?)>((true, null, g.Clone()));
            g.MemberCharacterIds.Add(characterId);
            return Task.FromResult<(bool, string?, GuildRecord?)>((true, null, g.Clone()));
        }
    }

    public Task<(bool Ok, string? Error)> RemoveMemberAsync(string guildId, string characterId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(guildId, out var g))
                return Task.FromResult<(bool, string?)>((false, "行会不存在"));
            g.MemberCharacterIds.RemoveAll(id => string.Equals(id, characterId, StringComparison.Ordinal));
            var ranks = g.RanksJson;
            GuildRanksJson.RemoveMember(ref ranks, characterId);
            g.RanksJson = ranks;
            if (g.MemberCharacterIds.Count == 0)
            {
                _byId.TryRemove(guildId, out _);
                _nameToId.TryRemove(g.Name, out _);
                _audits.TryRemove(guildId, out _);
            }
            else if (string.Equals(g.LeaderCharacterId, characterId, StringComparison.Ordinal))
            {
                g.LeaderCharacterId = g.MemberCharacterIds[0];
                ranks = g.RanksJson;
                GuildRanksJson.RemoveMember(ref ranks, g.LeaderCharacterId);
                g.RanksJson = ranks;
            }
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    public Task DeleteAsync(string guildId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_byId.TryRemove(guildId, out var g))
                _nameToId.TryRemove(g.Name, out _);
            _audits.TryRemove(guildId, out _);
        }
        return Task.CompletedTask;
    }
}

internal sealed class SqliteGuildStore : IGuildStore
{
    private readonly string _cs;
    public SqliteGuildStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS guilds (
              guild_id TEXT PRIMARY KEY,
              name TEXT NOT NULL COLLATE NOCASE UNIQUE,
              leader_character_id TEXT NOT NULL,
              members_json TEXT NOT NULL,
              warehouse_json TEXT NOT NULL DEFAULT '[]',
              ranks_json TEXT NOT NULL DEFAULT '{}',
              created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS guild_warehouse_audit (
              audit_id TEXT PRIMARY KEY,
              guild_id TEXT NOT NULL,
              actor_character_id TEXT NOT NULL,
              actor_name TEXT NOT NULL,
              action TEXT NOT NULL,
              item_id TEXT NOT NULL DEFAULT '',
              count INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_gwa_guild_created ON guild_warehouse_audit(guild_id, created_at);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
        cmd.CommandText = "PRAGMA table_info(guilds)";
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                cols.Add(reader.GetString(1));
        }
        if (!cols.Contains("warehouse_json"))
        {
            cmd.CommandText = "ALTER TABLE guilds ADD COLUMN warehouse_json TEXT NOT NULL DEFAULT '[]'";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if (!cols.Contains("ranks_json"))
        {
            cmd.CommandText = "ALTER TABLE guilds ADD COLUMN ranks_json TEXT NOT NULL DEFAULT '{}'";
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<GuildRecord?> GetByIdAsync(string guildId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT guild_id, name, leader_character_id, members_json, warehouse_json, ranks_json, created_at FROM guilds WHERE guild_id = $id";
        cmd.Parameters.AddWithValue("$id", guildId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task<GuildRecord?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT guild_id, name, leader_character_id, members_json, warehouse_json, ranks_json, created_at FROM guilds WHERE name = $n COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$n", name.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task<(bool Ok, string? Error, GuildRecord? Guild)> CreateAsync(string name, string leaderCharacterId, CancellationToken ct = default)
    {
        name = name.Trim();
        var g = new GuildRecord
        {
            GuildId = Guid.NewGuid().ToString("N"),
            Name = name,
            LeaderCharacterId = leaderCharacterId,
            MemberCharacterIds = new List<string> { leaderCharacterId },
            WarehouseJson = "[]",
            RanksJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        try
        {
            await using var conn = new SqliteConnection(_cs);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO guilds(guild_id, name, leader_character_id, members_json, warehouse_json, ranks_json, created_at)
                VALUES($id, $n, $l, $m, $w, $r, $c)
                """;
            cmd.Parameters.AddWithValue("$id", g.GuildId);
            cmd.Parameters.AddWithValue("$n", g.Name);
            cmd.Parameters.AddWithValue("$l", g.LeaderCharacterId);
            cmd.Parameters.AddWithValue("$m", GuildMembersJson.Serialize(g.MemberCharacterIds));
            cmd.Parameters.AddWithValue("$w", g.WarehouseJson);
            cmd.Parameters.AddWithValue("$r", g.RanksJson);
            cmd.Parameters.AddWithValue("$c", g.CreatedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
            return (true, null, g);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return (false, "行会名已存在", null);
        }
    }

    public async Task<(bool Ok, string? Error, GuildRecord? Guild)> AddMemberAsync(string guildId, string characterId, CancellationToken ct = default)
    {
        var g = await GetByIdAsync(guildId, ct);
        if (g is null) return (false, "行会不存在", null);
        if (!g.MemberCharacterIds.Contains(characterId, StringComparer.Ordinal))
            g.MemberCharacterIds.Add(characterId);
        await PersistMembersAsync(g, ct);
        return (true, null, g);
    }

    public async Task<(bool Ok, string? Error)> RemoveMemberAsync(string guildId, string characterId, CancellationToken ct = default)
    {
        var g = await GetByIdAsync(guildId, ct);
        if (g is null) return (false, "行会不存在");
        g.MemberCharacterIds.RemoveAll(id => string.Equals(id, characterId, StringComparison.Ordinal));
        var ranks = g.RanksJson;
        GuildRanksJson.RemoveMember(ref ranks, characterId);
        g.RanksJson = ranks;
        if (g.MemberCharacterIds.Count == 0)
        {
            await DeleteAsync(guildId, ct);
            return (true, null);
        }
        if (string.Equals(g.LeaderCharacterId, characterId, StringComparison.Ordinal))
        {
            g.LeaderCharacterId = g.MemberCharacterIds[0];
            ranks = g.RanksJson;
            GuildRanksJson.RemoveMember(ref ranks, g.LeaderCharacterId);
            g.RanksJson = ranks;
        }
        await PersistMembersAsync(g, ct);
        await SaveRanksAsync(guildId, g.RanksJson, ct);
        return (true, null);
    }

    public async Task DeleteAsync(string guildId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM guild_warehouse_audit WHERE guild_id = $id; DELETE FROM guilds WHERE guild_id = $id";
        cmd.Parameters.AddWithValue("$id", guildId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveWarehouseAsync(string guildId, string warehouseJson, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE guilds SET warehouse_json = $w WHERE guild_id = $id";
        cmd.Parameters.AddWithValue("$w", string.IsNullOrWhiteSpace(warehouseJson) ? "[]" : warehouseJson);
        cmd.Parameters.AddWithValue("$id", guildId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveRanksAsync(string guildId, string ranksJson, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE guilds SET ranks_json = $r WHERE guild_id = $id";
        cmd.Parameters.AddWithValue("$r", string.IsNullOrWhiteSpace(ranksJson) ? "{}" : ranksJson);
        cmd.Parameters.AddWithValue("$id", guildId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AppendWarehouseAuditAsync(GuildWarehouseAuditRecord entry, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO guild_warehouse_audit(audit_id, guild_id, actor_character_id, actor_name, action, item_id, count, created_at)
            VALUES($id, $g, $a, $n, $act, $i, $c, $t)
            """;
        cmd.Parameters.AddWithValue("$id", entry.AuditId);
        cmd.Parameters.AddWithValue("$g", entry.GuildId);
        cmd.Parameters.AddWithValue("$a", entry.ActorCharacterId);
        cmd.Parameters.AddWithValue("$n", entry.ActorName);
        cmd.Parameters.AddWithValue("$act", entry.Action);
        cmd.Parameters.AddWithValue("$i", entry.ItemId ?? "");
        cmd.Parameters.AddWithValue("$c", entry.Count);
        cmd.Parameters.AddWithValue("$t", entry.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<GuildWarehouseAuditRecord>> ListWarehouseAuditAsync(string guildId, int limit, CancellationToken ct = default)
    {
        int take = Math.Max(1, limit);
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT audit_id, guild_id, actor_character_id, actor_name, action, item_id, count, created_at
            FROM guild_warehouse_audit
            WHERE guild_id = $g
            ORDER BY created_at DESC
            LIMIT $lim
            """;
        cmd.Parameters.AddWithValue("$g", guildId);
        cmd.Parameters.AddWithValue("$lim", take);
        var list = new List<GuildWarehouseAuditRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new GuildWarehouseAuditRecord
            {
                AuditId = reader.GetString(0),
                GuildId = reader.GetString(1),
                ActorCharacterId = reader.GetString(2),
                ActorName = reader.GetString(3),
                Action = reader.GetString(4),
                ItemId = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Count = reader.GetInt32(6),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(7))
            });
        }
        return list;
    }

    private async Task PersistMembersAsync(GuildRecord g, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE guilds SET leader_character_id = $l, members_json = $m WHERE guild_id = $id";
        cmd.Parameters.AddWithValue("$l", g.LeaderCharacterId);
        cmd.Parameters.AddWithValue("$m", GuildMembersJson.Serialize(g.MemberCharacterIds));
        cmd.Parameters.AddWithValue("$id", g.GuildId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static GuildRecord Read(SqliteDataReader reader) => new()
    {
        GuildId = reader.GetString(0),
        Name = reader.GetString(1),
        LeaderCharacterId = reader.GetString(2),
        MemberCharacterIds = GuildMembersJson.Parse(reader.GetString(3)),
        WarehouseJson = reader.IsDBNull(4) || string.IsNullOrWhiteSpace(reader.GetString(4)) ? "[]" : reader.GetString(4),
        RanksJson = reader.FieldCount > 6 && !reader.IsDBNull(5) && !string.IsNullOrWhiteSpace(reader.GetString(5))
            ? reader.GetString(5) : "{}",
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.FieldCount > 6 ? 6 : 5))
    };
}

internal sealed class PostgresGuildStore : IGuildStore
{
    private readonly string _cs;
    public PostgresGuildStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS guilds (
              guild_id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              leader_character_id TEXT NOT NULL,
              members_json TEXT NOT NULL,
              warehouse_json TEXT NOT NULL DEFAULT '[]',
              ranks_json TEXT NOT NULL DEFAULT '{}',
              created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_guilds_name_lower ON guilds (LOWER(name));
            ALTER TABLE guilds ADD COLUMN IF NOT EXISTS warehouse_json TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE guilds ADD COLUMN IF NOT EXISTS ranks_json TEXT NOT NULL DEFAULT '{}';
            CREATE TABLE IF NOT EXISTS guild_warehouse_audit (
              audit_id TEXT PRIMARY KEY,
              guild_id TEXT NOT NULL,
              actor_character_id TEXT NOT NULL,
              actor_name TEXT NOT NULL,
              action TEXT NOT NULL,
              item_id TEXT NOT NULL DEFAULT '',
              count INTEGER NOT NULL DEFAULT 0,
              created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_gwa_guild_created ON guild_warehouse_audit(guild_id, created_at DESC);
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<GuildRecord?> GetByIdAsync(string guildId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT guild_id, name, leader_character_id, members_json, warehouse_json, ranks_json, created_at FROM guilds WHERE guild_id = @id", conn);
        cmd.Parameters.AddWithValue("id", guildId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task<GuildRecord?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT guild_id, name, leader_character_id, members_json, warehouse_json, ranks_json, created_at FROM guilds WHERE LOWER(name) = LOWER(@n)", conn);
        cmd.Parameters.AddWithValue("n", name.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task<(bool Ok, string? Error, GuildRecord? Guild)> CreateAsync(string name, string leaderCharacterId, CancellationToken ct = default)
    {
        name = name.Trim();
        var g = new GuildRecord
        {
            GuildId = Guid.NewGuid().ToString("N"),
            Name = name,
            LeaderCharacterId = leaderCharacterId,
            MemberCharacterIds = new List<string> { leaderCharacterId },
            WarehouseJson = "[]",
            RanksJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        try
        {
            await using var conn = new NpgsqlConnection(_cs);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO guilds(guild_id, name, leader_character_id, members_json, warehouse_json, ranks_json, created_at)
                VALUES(@id, @n, @l, @m, @w, @r, @c)
                """, conn);
            cmd.Parameters.AddWithValue("id", g.GuildId);
            cmd.Parameters.AddWithValue("n", g.Name);
            cmd.Parameters.AddWithValue("l", g.LeaderCharacterId);
            cmd.Parameters.AddWithValue("m", GuildMembersJson.Serialize(g.MemberCharacterIds));
            cmd.Parameters.AddWithValue("w", g.WarehouseJson);
            cmd.Parameters.AddWithValue("r", g.RanksJson);
            cmd.Parameters.AddWithValue("c", g.CreatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
            return (true, null, g);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return (false, "行会名已存在", null);
        }
    }

    public async Task<(bool Ok, string? Error, GuildRecord? Guild)> AddMemberAsync(string guildId, string characterId, CancellationToken ct = default)
    {
        var g = await GetByIdAsync(guildId, ct);
        if (g is null) return (false, "行会不存在", null);
        if (!g.MemberCharacterIds.Contains(characterId, StringComparer.Ordinal))
            g.MemberCharacterIds.Add(characterId);
        await PersistMembersAsync(g, ct);
        return (true, null, g);
    }

    public async Task<(bool Ok, string? Error)> RemoveMemberAsync(string guildId, string characterId, CancellationToken ct = default)
    {
        var g = await GetByIdAsync(guildId, ct);
        if (g is null) return (false, "行会不存在");
        g.MemberCharacterIds.RemoveAll(id => string.Equals(id, characterId, StringComparison.Ordinal));
        var ranks = g.RanksJson;
        GuildRanksJson.RemoveMember(ref ranks, characterId);
        g.RanksJson = ranks;
        if (g.MemberCharacterIds.Count == 0)
        {
            await DeleteAsync(guildId, ct);
            return (true, null);
        }
        if (string.Equals(g.LeaderCharacterId, characterId, StringComparison.Ordinal))
        {
            g.LeaderCharacterId = g.MemberCharacterIds[0];
            ranks = g.RanksJson;
            GuildRanksJson.RemoveMember(ref ranks, g.LeaderCharacterId);
            g.RanksJson = ranks;
        }
        await PersistMembersAsync(g, ct);
        await SaveRanksAsync(guildId, g.RanksJson, ct);
        return (true, null);
    }

    public async Task DeleteAsync(string guildId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM guild_warehouse_audit WHERE guild_id = @id;
            DELETE FROM guilds WHERE guild_id = @id;
            """, conn);
        cmd.Parameters.AddWithValue("id", guildId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveWarehouseAsync(string guildId, string warehouseJson, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE guilds SET warehouse_json = @w WHERE guild_id = @id", conn);
        cmd.Parameters.AddWithValue("w", string.IsNullOrWhiteSpace(warehouseJson) ? "[]" : warehouseJson);
        cmd.Parameters.AddWithValue("id", guildId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveRanksAsync(string guildId, string ranksJson, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE guilds SET ranks_json = @r WHERE guild_id = @id", conn);
        cmd.Parameters.AddWithValue("r", string.IsNullOrWhiteSpace(ranksJson) ? "{}" : ranksJson);
        cmd.Parameters.AddWithValue("id", guildId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AppendWarehouseAuditAsync(GuildWarehouseAuditRecord entry, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO guild_warehouse_audit(audit_id, guild_id, actor_character_id, actor_name, action, item_id, count, created_at)
            VALUES(@id, @g, @a, @n, @act, @i, @c, @t)
            """, conn);
        cmd.Parameters.AddWithValue("id", entry.AuditId);
        cmd.Parameters.AddWithValue("g", entry.GuildId);
        cmd.Parameters.AddWithValue("a", entry.ActorCharacterId);
        cmd.Parameters.AddWithValue("n", entry.ActorName);
        cmd.Parameters.AddWithValue("act", entry.Action);
        cmd.Parameters.AddWithValue("i", entry.ItemId ?? "");
        cmd.Parameters.AddWithValue("c", entry.Count);
        cmd.Parameters.AddWithValue("t", entry.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<GuildWarehouseAuditRecord>> ListWarehouseAuditAsync(string guildId, int limit, CancellationToken ct = default)
    {
        int take = Math.Max(1, limit);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT audit_id, guild_id, actor_character_id, actor_name, action, item_id, count, created_at
            FROM guild_warehouse_audit
            WHERE guild_id = @g
            ORDER BY created_at DESC
            LIMIT @lim
            """, conn);
        cmd.Parameters.AddWithValue("g", guildId);
        cmd.Parameters.AddWithValue("lim", take);
        var list = new List<GuildWarehouseAuditRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new GuildWarehouseAuditRecord
            {
                AuditId = reader.GetString(0),
                GuildId = reader.GetString(1),
                ActorCharacterId = reader.GetString(2),
                ActorName = reader.GetString(3),
                Action = reader.GetString(4),
                ItemId = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Count = reader.GetInt32(6),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(7)
            });
        }
        return list;
    }

    private async Task PersistMembersAsync(GuildRecord g, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE guilds SET leader_character_id = @l, members_json = @m WHERE guild_id = @id", conn);
        cmd.Parameters.AddWithValue("l", g.LeaderCharacterId);
        cmd.Parameters.AddWithValue("m", GuildMembersJson.Serialize(g.MemberCharacterIds));
        cmd.Parameters.AddWithValue("id", g.GuildId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static GuildRecord Read(NpgsqlDataReader reader) => new()
    {
        GuildId = reader.GetString(0),
        Name = reader.GetString(1),
        LeaderCharacterId = reader.GetString(2),
        MemberCharacterIds = GuildMembersJson.Parse(reader.GetString(3)),
        WarehouseJson = reader.IsDBNull(4) || string.IsNullOrWhiteSpace(reader.GetString(4)) ? "[]" : reader.GetString(4),
        RanksJson = reader.IsDBNull(5) || string.IsNullOrWhiteSpace(reader.GetString(5)) ? "{}" : reader.GetString(5),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(6)
    };
}
