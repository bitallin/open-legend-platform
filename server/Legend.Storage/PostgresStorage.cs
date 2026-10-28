using Npgsql;

namespace Legend.Storage;

public sealed class PostgresStorage : ILegendStorage
{
    private readonly string _connectionString;
    private readonly ISessionStore _sessions;
    private readonly PostgresAccountStore _accounts;
    private readonly PostgresCharacterStore _characters;

    public PostgresStorage(string connectionString, ISessionStore sessions)
    {
        _connectionString = connectionString;
        _sessions = sessions;
        _accounts = new PostgresAccountStore(_connectionString);
        _characters = new PostgresCharacterStore(_connectionString);
    }

    public IAccountStore Accounts => _accounts;
    public ICharacterStore Characters => _characters;
    public ISessionStore Sessions => _sessions;
    public string BackendName => "PostgreSQL";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _accounts.EnsureSchemaAsync(ct);
        await _characters.EnsureSchemaAsync(ct);
        await _sessions.EnsureReadyAsync(ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class PostgresAccountStore : IAccountStore
{
    private readonly string _cs;
    public PostgresAccountStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS accounts (
              account_id TEXT PRIMARY KEY,
              username TEXT NOT NULL,
              password_hash TEXT NOT NULL,
              created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_accounts_username_lower ON accounts (LOWER(username));
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(bool Ok, string? Error, AccountRecord? Account)> RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        username = username.Trim();
        if (username.Length < 3 || username.Length > 32)
            return (false, "用户名长度须为 3–32", null);
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            return (false, "密码至少 6 位", null);

        var account = new AccountRecord
        {
            AccountId = Guid.NewGuid().ToString("N"),
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await using var conn = new NpgsqlConnection(_cs);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO accounts(account_id, username, password_hash, created_at) VALUES(@id,@u,@h,@c)", conn);
            cmd.Parameters.AddWithValue("id", account.AccountId);
            cmd.Parameters.AddWithValue("u", account.Username);
            cmd.Parameters.AddWithValue("h", account.PasswordHash);
            cmd.Parameters.AddWithValue("c", account.CreatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
            return (true, null, account);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return (false, "用户名已存在", null);
        }
    }

    public async Task<(bool Ok, string? Error, AccountRecord? Account)> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT account_id, username, password_hash, created_at FROM accounts WHERE LOWER(username)=LOWER(@u)", conn);
        cmd.Parameters.AddWithValue("u", username.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return (false, "用户名或密码错误", null);

        var account = new AccountRecord
        {
            AccountId = reader.GetString(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(3)
        };
        if (!PasswordHasher.Verify(password, account.PasswordHash))
            return (false, "用户名或密码错误", null);
        return (true, null, account);
    }

    public async Task<AccountRecord?> GetByIdAsync(string accountId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT account_id, username, password_hash, created_at FROM accounts WHERE account_id=@id", conn);
        cmd.Parameters.AddWithValue("id", accountId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new AccountRecord
        {
            AccountId = reader.GetString(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(3)
        };
    }
}

internal sealed class PostgresCharacterStore : ICharacterStore
{
    private readonly string _cs;
    public PostgresCharacterStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS characters (
              character_id TEXT PRIMARY KEY,
              account_id TEXT NOT NULL,
              name TEXT NOT NULL,
              class_id TEXT NOT NULL,
              level INTEGER NOT NULL,
              map_id TEXT NOT NULL,
              x INTEGER NOT NULL,
              y INTEGER NOT NULL,
              hp INTEGER NOT NULL,
              mp INTEGER NOT NULL,
              max_hp INTEGER NOT NULL,
              max_mp INTEGER NOT NULL,
              inventory_json JSONB NOT NULL DEFAULT '[]'::jsonb,
              created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_characters_name_lower ON characters (LOWER(name));
            CREATE INDEX IF NOT EXISTS idx_characters_account ON characters(account_id);
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(string accountId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT character_id, account_id, name, class_id, level, map_id, x, y, hp, mp, max_hp, max_mp, inventory_json::text, created_at, updated_at FROM characters WHERE account_id=@a ORDER BY created_at", conn);
        cmd.Parameters.AddWithValue("a", accountId);
        var list = new List<CharacterRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Read(reader));
        return list;
    }

    public async Task<(bool Ok, string? Error, CharacterRecord? Character)> CreateAsync(CharacterRecord character, int maxPerAccount, CancellationToken ct = default)
    {
        character.Name = character.Name.Trim();
        if (character.Name.Length < 2 || character.Name.Length > 16)
            return (false, "角色名长度须为 2–16", null);
        if (string.IsNullOrEmpty(character.CharacterId))
            character.CharacterId = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(character.InventoryJson))
            character.InventoryJson = InventoryJson.Empty;
        character.CreatedAt = DateTimeOffset.UtcNow;
        character.UpdatedAt = character.CreatedAt;

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);

        await using (var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM characters WHERE account_id=@a", conn))
        {
            countCmd.Parameters.AddWithValue("a", character.AccountId);
            long count = (long)(await countCmd.ExecuteScalarAsync(ct) ?? 0L);
            if (count >= maxPerAccount)
                return (false, $"每账号最多 {maxPerAccount} 个角色", null);
        }

        try
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO characters(character_id, account_id, name, class_id, level, map_id, x, y, hp, mp, max_hp, max_mp, inventory_json, created_at, updated_at)
                VALUES(@cid,@aid,@n,@cl,@lv,@m,@x,@y,@hp,@mp,@mhp,@mmp,@inv::jsonb,@c,@u)
                """, conn);
            Bind(cmd, character);
            await cmd.ExecuteNonQueryAsync(ct);
            return (true, null, character.Clone());
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return (false, "角色名已存在", null);
        }
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(string accountId, string characterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM characters WHERE character_id=@cid AND account_id=@aid", conn);
        cmd.Parameters.AddWithValue("cid", characterId);
        cmd.Parameters.AddWithValue("aid", accountId);
        int n = await cmd.ExecuteNonQueryAsync(ct);
        return n > 0 ? (true, null) : (false, "角色不存在");
    }

    public async Task<CharacterRecord?> GetByIdAsync(string characterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT character_id, account_id, name, class_id, level, map_id, x, y, hp, mp, max_hp, max_mp, inventory_json::text, created_at, updated_at FROM characters WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("id", characterId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return Read(reader);
    }

    public async Task SavePositionAsync(string characterId, string mapId, int x, int y, int hp, int mp, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE characters SET map_id=@m, x=@x, y=@y, hp=@hp, mp=@mp, updated_at=NOW() WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("m", mapId);
        cmd.Parameters.AddWithValue("x", x);
        cmd.Parameters.AddWithValue("y", y);
        cmd.Parameters.AddWithValue("hp", hp);
        cmd.Parameters.AddWithValue("mp", mp);
        cmd.Parameters.AddWithValue("id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static CharacterRecord Read(NpgsqlDataReader reader)
        => new()
        {
            CharacterId = reader.GetString(0),
            AccountId = reader.GetString(1),
            Name = reader.GetString(2),
            ClassId = reader.GetString(3),
            Level = reader.GetInt32(4),
            MapId = reader.GetString(5),
            X = reader.GetInt32(6),
            Y = reader.GetInt32(7),
            Hp = reader.GetInt32(8),
            Mp = reader.GetInt32(9),
            MaxHp = reader.GetInt32(10),
            MaxMp = reader.GetInt32(11),
            InventoryJson = reader.GetString(12),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(13),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(14)
        };

    private static void Bind(NpgsqlCommand cmd, CharacterRecord c)
    {
        cmd.Parameters.AddWithValue("cid", c.CharacterId);
        cmd.Parameters.AddWithValue("aid", c.AccountId);
        cmd.Parameters.AddWithValue("n", c.Name);
        cmd.Parameters.AddWithValue("cl", c.ClassId);
        cmd.Parameters.AddWithValue("lv", c.Level);
        cmd.Parameters.AddWithValue("m", c.MapId);
        cmd.Parameters.AddWithValue("x", c.X);
        cmd.Parameters.AddWithValue("y", c.Y);
        cmd.Parameters.AddWithValue("hp", c.Hp);
        cmd.Parameters.AddWithValue("mp", c.Mp);
        cmd.Parameters.AddWithValue("mhp", c.MaxHp);
        cmd.Parameters.AddWithValue("mmp", c.MaxMp);
        cmd.Parameters.AddWithValue("inv", c.InventoryJson);
        cmd.Parameters.AddWithValue("c", c.CreatedAt);
        cmd.Parameters.AddWithValue("u", c.UpdatedAt);
    }
}
