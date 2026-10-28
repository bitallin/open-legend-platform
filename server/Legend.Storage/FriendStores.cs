using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Legend.Storage;

internal sealed class InMemoryFriendStore : IFriendStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, FriendRecord>> _byOwner = new(StringComparer.Ordinal);

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<FriendRecord>> ListByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        if (!_byOwner.TryGetValue(ownerCharacterId, out var map))
            return Task.FromResult<IReadOnlyList<FriendRecord>>(Array.Empty<FriendRecord>());
        var list = map.Values.OrderBy(x => x.CreatedAt).Select(x => x.Clone()).ToList();
        return Task.FromResult<IReadOnlyList<FriendRecord>>(list);
    }

    public Task<int> CountByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        if (!_byOwner.TryGetValue(ownerCharacterId, out var map))
            return Task.FromResult(0);
        return Task.FromResult(map.Count);
    }

    public Task<bool> IsFriendAsync(string ownerCharacterId, string friendCharacterId, CancellationToken ct = default)
    {
        if (!_byOwner.TryGetValue(ownerCharacterId, out var map))
            return Task.FromResult(false);
        return Task.FromResult(map.ContainsKey(friendCharacterId));
    }

    public Task<(bool Ok, string? Error, FriendRecord? Friend)> AddAsync(string ownerCharacterId, string friendCharacterId, string friendName, CancellationToken ct = default)
    {
        var map = _byOwner.GetOrAdd(ownerCharacterId, _ => new ConcurrentDictionary<string, FriendRecord>(StringComparer.Ordinal));
        var rec = new FriendRecord
        {
            OwnerCharacterId = ownerCharacterId,
            FriendCharacterId = friendCharacterId,
            FriendName = friendName,
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (!map.TryAdd(friendCharacterId, rec))
            return Task.FromResult<(bool, string?, FriendRecord?)>((false, "已在好友列表中", null));
        return Task.FromResult<(bool, string?, FriendRecord?)>((true, null, rec.Clone()));
    }

    public Task<(bool Ok, string? Error)> RemoveAsync(string ownerCharacterId, string friendCharacterId, CancellationToken ct = default)
    {
        if (!_byOwner.TryGetValue(ownerCharacterId, out var map) || !map.TryRemove(friendCharacterId, out _))
            return Task.FromResult<(bool, string?)>((false, "好友不存在"));
        return Task.FromResult<(bool, string?)>((true, null));
    }
}

internal sealed class SqliteFriendStore : IFriendStore
{
    private readonly string _cs;
    public SqliteFriendStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS friends (
              owner_character_id TEXT NOT NULL,
              friend_character_id TEXT NOT NULL,
              friend_name TEXT NOT NULL,
              created_at TEXT NOT NULL,
              PRIMARY KEY (owner_character_id, friend_character_id)
            );
            CREATE INDEX IF NOT EXISTS idx_friends_owner ON friends(owner_character_id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<FriendRecord>> ListByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT owner_character_id, friend_character_id, friend_name, created_at
            FROM friends WHERE owner_character_id = $o ORDER BY created_at
            """;
        cmd.Parameters.AddWithValue("$o", ownerCharacterId);
        var list = new List<FriendRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Read(reader));
        return list;
    }

    public async Task<int> CountByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM friends WHERE owner_character_id = $o";
        cmd.Parameters.AddWithValue("$o", ownerCharacterId);
        var n = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(n);
    }

    public async Task<bool> IsFriendAsync(string ownerCharacterId, string friendCharacterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM friends WHERE owner_character_id = $o AND friend_character_id = $f LIMIT 1";
        cmd.Parameters.AddWithValue("$o", ownerCharacterId);
        cmd.Parameters.AddWithValue("$f", friendCharacterId);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is not null && r is not DBNull;
    }

    public async Task<(bool Ok, string? Error, FriendRecord? Friend)> AddAsync(string ownerCharacterId, string friendCharacterId, string friendName, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var now = DateTimeOffset.UtcNow;
        cmd.CommandText = """
            INSERT OR IGNORE INTO friends (owner_character_id, friend_character_id, friend_name, created_at)
            VALUES ($o, $f, $n, $c)
            """;
        cmd.Parameters.AddWithValue("$o", ownerCharacterId);
        cmd.Parameters.AddWithValue("$f", friendCharacterId);
        cmd.Parameters.AddWithValue("$n", friendName);
        cmd.Parameters.AddWithValue("$c", now.ToString("O"));
        int rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 0)
            return (false, "已在好友列表中", null);
        return (true, null, new FriendRecord
        {
            OwnerCharacterId = ownerCharacterId,
            FriendCharacterId = friendCharacterId,
            FriendName = friendName,
            CreatedAt = now
        });
    }

    public async Task<(bool Ok, string? Error)> RemoveAsync(string ownerCharacterId, string friendCharacterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM friends WHERE owner_character_id = $o AND friend_character_id = $f";
        cmd.Parameters.AddWithValue("$o", ownerCharacterId);
        cmd.Parameters.AddWithValue("$f", friendCharacterId);
        int rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0 ? (true, null) : (false, "好友不存在");
    }

    private static FriendRecord Read(SqliteDataReader reader) => new()
    {
        OwnerCharacterId = reader.GetString(0),
        FriendCharacterId = reader.GetString(1),
        FriendName = reader.GetString(2),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(3))
    };
}

internal sealed class PostgresFriendStore : IFriendStore
{
    private readonly string _cs;
    public PostgresFriendStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS friends (
              owner_character_id TEXT NOT NULL,
              friend_character_id TEXT NOT NULL,
              friend_name TEXT NOT NULL,
              created_at TIMESTAMPTZ NOT NULL,
              PRIMARY KEY (owner_character_id, friend_character_id)
            );
            CREATE INDEX IF NOT EXISTS idx_friends_owner ON friends(owner_character_id);
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<FriendRecord>> ListByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT owner_character_id, friend_character_id, friend_name, created_at
            FROM friends WHERE owner_character_id=@o ORDER BY created_at
            """, conn);
        cmd.Parameters.AddWithValue("o", ownerCharacterId);
        var list = new List<FriendRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new FriendRecord
            {
                OwnerCharacterId = reader.GetString(0),
                FriendCharacterId = reader.GetString(1),
                FriendName = reader.GetString(2),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(3)
            });
        }
        return list;
    }

    public async Task<int> CountByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM friends WHERE owner_character_id=@o", conn);
        cmd.Parameters.AddWithValue("o", ownerCharacterId);
        var n = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(n);
    }

    public async Task<bool> IsFriendAsync(string ownerCharacterId, string friendCharacterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM friends WHERE owner_character_id=@o AND friend_character_id=@f LIMIT 1", conn);
        cmd.Parameters.AddWithValue("o", ownerCharacterId);
        cmd.Parameters.AddWithValue("f", friendCharacterId);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is not null && r is not DBNull;
    }

    public async Task<(bool Ok, string? Error, FriendRecord? Friend)> AddAsync(string ownerCharacterId, string friendCharacterId, string friendName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        var now = DateTimeOffset.UtcNow;
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO friends (owner_character_id, friend_character_id, friend_name, created_at)
            VALUES (@o, @f, @n, @c)
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("o", ownerCharacterId);
        cmd.Parameters.AddWithValue("f", friendCharacterId);
        cmd.Parameters.AddWithValue("n", friendName);
        cmd.Parameters.AddWithValue("c", now);
        int rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 0)
            return (false, "已在好友列表中", null);
        return (true, null, new FriendRecord
        {
            OwnerCharacterId = ownerCharacterId,
            FriendCharacterId = friendCharacterId,
            FriendName = friendName,
            CreatedAt = now
        });
    }

    public async Task<(bool Ok, string? Error)> RemoveAsync(string ownerCharacterId, string friendCharacterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM friends WHERE owner_character_id=@o AND friend_character_id=@f", conn);
        cmd.Parameters.AddWithValue("o", ownerCharacterId);
        cmd.Parameters.AddWithValue("f", friendCharacterId);
        int rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0 ? (true, null) : (false, "好友不存在");
    }
}
