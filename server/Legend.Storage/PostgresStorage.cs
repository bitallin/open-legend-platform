using Npgsql;

namespace Legend.Storage;

public sealed class PostgresStorage : ILegendStorage
{
    private readonly string _connectionString;
    private readonly ISessionStore _sessions;
    private readonly PostgresAccountStore _accounts;
    private readonly PostgresCharacterStore _characters;
    private readonly PostgresGuildStore _guilds;
    private readonly PostgresTradeStore _trades;
    private readonly PostgresFriendStore _friends;
    private readonly PostgresSiegeStore _siege;

    public PostgresStorage(string connectionString, ISessionStore sessions)
    {
        _connectionString = connectionString;
        _sessions = sessions;
        _accounts = new PostgresAccountStore(_connectionString);
        _characters = new PostgresCharacterStore(_connectionString);
        _guilds = new PostgresGuildStore(_connectionString);
        _trades = new PostgresTradeStore(_connectionString);
        _friends = new PostgresFriendStore(_connectionString);
        _siege = new PostgresSiegeStore(_connectionString);
    }

    public IAccountStore Accounts => _accounts;
    public ICharacterStore Characters => _characters;
    public ISessionStore Sessions => _sessions;
    public IGuildStore Guilds => _guilds;
    public ITradeStore Trades => _trades;
    public IFriendStore Friends => _friends;
    public ISiegeStore Siege => _siege;
    public string BackendName => "PostgreSQL";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _accounts.EnsureSchemaAsync(ct);
        await _characters.EnsureSchemaAsync(ct);
        await _guilds.EnsureSchemaAsync(ct);
        await _trades.EnsureSchemaAsync(ct);
        await _friends.EnsureSchemaAsync(ct);
        await _siege.EnsureSchemaAsync(ct);
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
              exp INTEGER NOT NULL DEFAULT 0,
              map_id TEXT NOT NULL,
              x INTEGER NOT NULL,
              y INTEGER NOT NULL,
              hp INTEGER NOT NULL,
              mp INTEGER NOT NULL,
              max_hp INTEGER NOT NULL,
              max_mp INTEGER NOT NULL,
              inventory_json JSONB NOT NULL DEFAULT '[]'::jsonb,
              gold INTEGER NOT NULL DEFAULT 100,
              quest_json JSONB NOT NULL DEFAULT '[]'::jsonb,
              equipment_json JSONB NOT NULL DEFAULT '[]'::jsonb,
              guild_id TEXT NOT NULL DEFAULT '',
              skills_json JSONB NOT NULL DEFAULT '[]'::jsonb,
              created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_characters_name_lower ON characters (LOWER(name));
            CREATE INDEX IF NOT EXISTS idx_characters_account ON characters(account_id);
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS gold INTEGER NOT NULL DEFAULT 100;
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS quest_json JSONB NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS equipment_json JSONB NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS exp INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS guild_id TEXT NOT NULL DEFAULT '';
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS skills_json JSONB NOT NULL DEFAULT '[]'::jsonb;
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(string accountId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT character_id, account_id, name, class_id, level, exp, map_id, x, y, hp, mp, max_hp, max_mp, inventory_json::text, gold, quest_json::text, equipment_json::text, COALESCE(guild_id,''), COALESCE(skills_json::text,'[]'), created_at, updated_at FROM characters WHERE account_id=@a ORDER BY created_at", conn);
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
        if (string.IsNullOrEmpty(character.QuestJson))
            character.QuestJson = QuestJson.Empty;
        if (string.IsNullOrEmpty(character.EquipmentJson))
            character.EquipmentJson = EquipmentJson.Empty;
        if (string.IsNullOrEmpty(character.SkillsJson))
            character.SkillsJson = "[]";
        if (character.Gold < 0)
            character.Gold = QuestJson.StartingGold;
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
                INSERT INTO characters(character_id, account_id, name, class_id, level, exp, map_id, x, y, hp, mp, max_hp, max_mp, inventory_json, gold, quest_json, equipment_json, guild_id, skills_json, created_at, updated_at)
                VALUES(@cid,@aid,@n,@cl,@lv,@exp,@m,@x,@y,@hp,@mp,@mhp,@mmp,@inv::jsonb,@gold,@qj::jsonb,@eqj::jsonb,@gid,@sj::jsonb,@c,@u)
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
            "SELECT character_id, account_id, name, class_id, level, exp, map_id, x, y, hp, mp, max_hp, max_mp, inventory_json::text, gold, quest_json::text, equipment_json::text, COALESCE(guild_id,''), COALESCE(skills_json::text,'[]'), created_at, updated_at FROM characters WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("id", characterId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return Read(reader);
    }

    public async Task<CharacterRecord?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT character_id, account_id, name, class_id, level, exp, map_id, x, y, hp, mp, max_hp, max_mp, inventory_json::text, gold, quest_json::text, equipment_json::text, COALESCE(guild_id,''), COALESCE(skills_json::text,'[]'), created_at, updated_at FROM characters WHERE LOWER(name)=LOWER(@n) LIMIT 1", conn);
        cmd.Parameters.AddWithValue("n", name.Trim());
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

    public async Task SaveInventoryAsync(string characterId, string inventoryJson, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE characters SET inventory_json=@inv::jsonb, updated_at=NOW() WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("inv", string.IsNullOrWhiteSpace(inventoryJson) ? "[]" : inventoryJson);
        cmd.Parameters.AddWithValue("id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveGoldAsync(string characterId, int gold, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE characters SET gold=@g, updated_at=NOW() WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("g", Math.Max(0, gold));
        cmd.Parameters.AddWithValue("id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveQuestsAsync(string characterId, string questJson, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE characters SET quest_json=@qj::jsonb, updated_at=NOW() WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("qj", string.IsNullOrWhiteSpace(questJson) ? "[]" : questJson);
        cmd.Parameters.AddWithValue("id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveEquipmentAsync(string characterId, string equipmentJson, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE characters SET equipment_json=@eqj::jsonb, updated_at=NOW() WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("eqj", string.IsNullOrWhiteSpace(equipmentJson) ? "[]" : equipmentJson);
        cmd.Parameters.AddWithValue("id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveGuildIdAsync(string characterId, string? guildId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE characters SET guild_id=@g, updated_at=NOW() WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("g", guildId ?? "");
        cmd.Parameters.AddWithValue("id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveSkillsAsync(string characterId, string skillsJson, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE characters SET skills_json=@sj::jsonb, updated_at=NOW() WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("sj", string.IsNullOrWhiteSpace(skillsJson) ? "[]" : skillsJson);
        cmd.Parameters.AddWithValue("id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveLevelExpAsync(string characterId, int level, int exp, int maxHp, int maxMp, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE characters SET level=@lv, exp=@exp, max_hp=@mhp, max_mp=@mmp, updated_at=NOW() WHERE character_id=@id", conn);
        cmd.Parameters.AddWithValue("lv", Math.Max(1, level));
        cmd.Parameters.AddWithValue("exp", Math.Max(0, exp));
        cmd.Parameters.AddWithValue("mhp", Math.Max(1, maxHp));
        cmd.Parameters.AddWithValue("mmp", Math.Max(0, maxMp));
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
            Exp = reader.GetInt32(5),
            MapId = reader.GetString(6),
            X = reader.GetInt32(7),
            Y = reader.GetInt32(8),
            Hp = reader.GetInt32(9),
            Mp = reader.GetInt32(10),
            MaxHp = reader.GetInt32(11),
            MaxMp = reader.GetInt32(12),
            InventoryJson = reader.GetString(13),
            Gold = reader.GetInt32(14),
            QuestJson = reader.GetString(15),
            EquipmentJson = reader.GetString(16),
            GuildId = reader.IsDBNull(17) ? "" : reader.GetString(17),
            SkillsJson = reader.IsDBNull(18) || string.IsNullOrEmpty(reader.GetString(18)) ? "[]" : reader.GetString(18),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(19),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(20)
        };

    private static void Bind(NpgsqlCommand cmd, CharacterRecord c)
    {
        cmd.Parameters.AddWithValue("cid", c.CharacterId);
        cmd.Parameters.AddWithValue("aid", c.AccountId);
        cmd.Parameters.AddWithValue("n", c.Name);
        cmd.Parameters.AddWithValue("cl", c.ClassId);
        cmd.Parameters.AddWithValue("lv", c.Level);
        cmd.Parameters.AddWithValue("exp", c.Exp);
        cmd.Parameters.AddWithValue("m", c.MapId);
        cmd.Parameters.AddWithValue("x", c.X);
        cmd.Parameters.AddWithValue("y", c.Y);
        cmd.Parameters.AddWithValue("hp", c.Hp);
        cmd.Parameters.AddWithValue("mp", c.Mp);
        cmd.Parameters.AddWithValue("mhp", c.MaxHp);
        cmd.Parameters.AddWithValue("mmp", c.MaxMp);
        cmd.Parameters.AddWithValue("inv", c.InventoryJson);
        cmd.Parameters.AddWithValue("gold", c.Gold);
        cmd.Parameters.AddWithValue("qj", c.QuestJson);
        cmd.Parameters.AddWithValue("eqj", c.EquipmentJson);
        cmd.Parameters.AddWithValue("gid", c.GuildId ?? "");
        cmd.Parameters.AddWithValue("sj", string.IsNullOrWhiteSpace(c.SkillsJson) ? "[]" : c.SkillsJson);
        cmd.Parameters.AddWithValue("c", c.CreatedAt);
        cmd.Parameters.AddWithValue("u", c.UpdatedAt);
    }
}
