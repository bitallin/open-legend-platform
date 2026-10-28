using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Legend.Storage;

internal sealed class InMemorySiegeStore : ISiegeStore
{
    private readonly ConcurrentDictionary<string, SiegeStateRecord> _byCastle = new(StringComparer.OrdinalIgnoreCase);

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<SiegeStateRecord?> GetAsync(string castleId, CancellationToken ct = default)
    {
        if (_byCastle.TryGetValue(castleId, out var r))
            return Task.FromResult<SiegeStateRecord?>(r.Clone());
        return Task.FromResult<SiegeStateRecord?>(null);
    }

    public Task UpsertAsync(SiegeStateRecord record, CancellationToken ct = default)
    {
        var copy = record.Clone();
        copy.UpdatedAt = DateTimeOffset.UtcNow;
        _byCastle[copy.CastleId] = copy;
        return Task.CompletedTask;
    }
}

internal sealed class SqliteSiegeStore : ISiegeStore
{
    private readonly string _cs;
    public SqliteSiegeStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS siege_state (
              castle_id TEXT PRIMARY KEY,
              owner_guild_id TEXT NOT NULL DEFAULT '',
              owner_guild_name TEXT NOT NULL DEFAULT '',
              declared_guilds_json TEXT NOT NULL DEFAULT '[]',
              updated_at TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SiegeStateRecord?> GetAsync(string castleId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT castle_id, owner_guild_id, owner_guild_name, declared_guilds_json, updated_at
            FROM siege_state WHERE castle_id = $id LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$id", castleId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        return new SiegeStateRecord
        {
            CastleId = r.GetString(0),
            OwnerGuildId = r.GetString(1),
            OwnerGuildName = r.GetString(2),
            DeclaredGuildsJson = r.GetString(3),
            UpdatedAt = DateTimeOffset.Parse(r.GetString(4))
        };
    }

    public async Task UpsertAsync(SiegeStateRecord record, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO siege_state (castle_id, owner_guild_id, owner_guild_name, declared_guilds_json, updated_at)
            VALUES ($id, $oid, $oname, $decl, $u)
            ON CONFLICT(castle_id) DO UPDATE SET
              owner_guild_id = excluded.owner_guild_id,
              owner_guild_name = excluded.owner_guild_name,
              declared_guilds_json = excluded.declared_guilds_json,
              updated_at = excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("$id", record.CastleId);
        cmd.Parameters.AddWithValue("$oid", record.OwnerGuildId ?? string.Empty);
        cmd.Parameters.AddWithValue("$oname", record.OwnerGuildName ?? string.Empty);
        cmd.Parameters.AddWithValue("$decl", string.IsNullOrWhiteSpace(record.DeclaredGuildsJson) ? "[]" : record.DeclaredGuildsJson);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

internal sealed class PostgresSiegeStore : ISiegeStore
{
    private readonly string _cs;
    public PostgresSiegeStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS siege_state (
              castle_id VARCHAR(64) PRIMARY KEY,
              owner_guild_id VARCHAR(64) NOT NULL DEFAULT '',
              owner_guild_name VARCHAR(64) NOT NULL DEFAULT '',
              declared_guilds_json TEXT NOT NULL DEFAULT '[]',
              updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SiegeStateRecord?> GetAsync(string castleId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT castle_id, owner_guild_id, owner_guild_name, declared_guilds_json, updated_at
            FROM siege_state WHERE castle_id = @id LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("id", castleId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        return new SiegeStateRecord
        {
            CastleId = r.GetString(0),
            OwnerGuildId = r.GetString(1),
            OwnerGuildName = r.GetString(2),
            DeclaredGuildsJson = r.GetString(3),
            UpdatedAt = r.GetFieldValue<DateTimeOffset>(4)
        };
    }

    public async Task UpsertAsync(SiegeStateRecord record, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO siege_state (castle_id, owner_guild_id, owner_guild_name, declared_guilds_json, updated_at)
            VALUES (@id, @oid, @oname, @decl, NOW())
            ON CONFLICT(castle_id) DO UPDATE SET
              owner_guild_id = EXCLUDED.owner_guild_id,
              owner_guild_name = EXCLUDED.owner_guild_name,
              declared_guilds_json = EXCLUDED.declared_guilds_json,
              updated_at = NOW();
            """, conn);
        cmd.Parameters.AddWithValue("id", record.CastleId);
        cmd.Parameters.AddWithValue("oid", record.OwnerGuildId ?? string.Empty);
        cmd.Parameters.AddWithValue("oname", record.OwnerGuildName ?? string.Empty);
        cmd.Parameters.AddWithValue("decl", string.IsNullOrWhiteSpace(record.DeclaredGuildsJson) ? "[]" : record.DeclaredGuildsJson);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
