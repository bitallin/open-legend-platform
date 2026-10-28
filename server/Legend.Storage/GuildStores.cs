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

internal sealed class InMemoryGuildStore : IGuildStore
{
    private readonly ConcurrentDictionary<string, GuildRecord> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _nameToId = new(StringComparer.OrdinalIgnoreCase);
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
                CreatedAt = DateTimeOffset.UtcNow
            };
            if (!_nameToId.TryAdd(name, g.GuildId))
                return Task.FromResult<(bool, string?, GuildRecord?)>((false, "行会名已存在", null));
            _byId[g.GuildId] = g;
            return Task.FromResult<(bool, string?, GuildRecord?)>((true, null, g.Clone()));
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
            if (g.MemberCharacterIds.Count == 0)
            {
                _byId.TryRemove(guildId, out _);
                _nameToId.TryRemove(g.Name, out _);
            }
            else if (string.Equals(g.LeaderCharacterId, characterId, StringComparison.Ordinal))
            {
                g.LeaderCharacterId = g.MemberCharacterIds[0];
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
              created_at TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<GuildRecord?> GetByIdAsync(string guildId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT guild_id, name, leader_character_id, members_json, created_at FROM guilds WHERE guild_id = $id";
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
        cmd.CommandText = "SELECT guild_id, name, leader_character_id, members_json, created_at FROM guilds WHERE name = $n COLLATE NOCASE";
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
            CreatedAt = DateTimeOffset.UtcNow
        };
        try
        {
            await using var conn = new SqliteConnection(_cs);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO guilds(guild_id, name, leader_character_id, members_json, created_at)
                VALUES($id, $n, $l, $m, $c)
                """;
            cmd.Parameters.AddWithValue("$id", g.GuildId);
            cmd.Parameters.AddWithValue("$n", g.Name);
            cmd.Parameters.AddWithValue("$l", g.LeaderCharacterId);
            cmd.Parameters.AddWithValue("$m", GuildMembersJson.Serialize(g.MemberCharacterIds));
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
        if (g.MemberCharacterIds.Count == 0)
        {
            await DeleteAsync(guildId, ct);
            return (true, null);
        }
        if (string.Equals(g.LeaderCharacterId, characterId, StringComparison.Ordinal))
            g.LeaderCharacterId = g.MemberCharacterIds[0];
        await PersistMembersAsync(g, ct);
        return (true, null);
    }

    public async Task DeleteAsync(string guildId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM guilds WHERE guild_id = $id";
        cmd.Parameters.AddWithValue("$id", guildId);
        await cmd.ExecuteNonQueryAsync(ct);
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
        CreatedAt = DateTimeOffset.Parse(reader.GetString(4))
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
              created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_guilds_name_lower ON guilds (LOWER(name));
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<GuildRecord?> GetByIdAsync(string guildId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT guild_id, name, leader_character_id, members_json, created_at FROM guilds WHERE guild_id = @id", conn);
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
            "SELECT guild_id, name, leader_character_id, members_json, created_at FROM guilds WHERE LOWER(name) = LOWER(@n)", conn);
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
            CreatedAt = DateTimeOffset.UtcNow
        };
        try
        {
            await using var conn = new NpgsqlConnection(_cs);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO guilds(guild_id, name, leader_character_id, members_json, created_at)
                VALUES(@id, @n, @l, @m, @c)
                """, conn);
            cmd.Parameters.AddWithValue("id", g.GuildId);
            cmd.Parameters.AddWithValue("n", g.Name);
            cmd.Parameters.AddWithValue("l", g.LeaderCharacterId);
            cmd.Parameters.AddWithValue("m", GuildMembersJson.Serialize(g.MemberCharacterIds));
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
        if (g.MemberCharacterIds.Count == 0)
        {
            await DeleteAsync(guildId, ct);
            return (true, null);
        }
        if (string.Equals(g.LeaderCharacterId, characterId, StringComparison.Ordinal))
            g.LeaderCharacterId = g.MemberCharacterIds[0];
        await PersistMembersAsync(g, ct);
        return (true, null);
    }

    public async Task DeleteAsync(string guildId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM guilds WHERE guild_id = @id", conn);
        cmd.Parameters.AddWithValue("id", guildId);
        await cmd.ExecuteNonQueryAsync(ct);
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
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(4)
    };
}
