using Microsoft.Data.Sqlite;

namespace Legend.Storage;

public sealed class SqliteStorage : ILegendStorage
{
    private readonly string _connectionString;
    private readonly SqliteAccountStore _accounts;
    private readonly SqliteCharacterStore _characters;
    private readonly SqliteGuildStore _guilds;
    private readonly SqliteTradeStore _trades;
    private readonly SqliteFriendStore _friends;
    private readonly SqliteSiegeStore _siege;
    private readonly SqliteMailStore _mails;
    private readonly InMemorySessionStore _sessions = new();

    public SqliteStorage(string databasePath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        _accounts = new SqliteAccountStore(_connectionString);
        _characters = new SqliteCharacterStore(_connectionString);
        _guilds = new SqliteGuildStore(_connectionString);
        _trades = new SqliteTradeStore(_connectionString);
        _friends = new SqliteFriendStore(_connectionString);
        _siege = new SqliteSiegeStore(_connectionString);
        _mails = new SqliteMailStore(_connectionString);
    }

    public IAccountStore Accounts => _accounts;
    public ICharacterStore Characters => _characters;
    public ISessionStore Sessions => _sessions;
    public IGuildStore Guilds => _guilds;
    public ITradeStore Trades => _trades;
    public IFriendStore Friends => _friends;
    public ISiegeStore Siege => _siege;
    public IMailStore Mails => _mails;
    public string BackendName => "Sqlite";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _accounts.EnsureSchemaAsync(ct);
        await _characters.EnsureSchemaAsync(ct);
        await _guilds.EnsureSchemaAsync(ct);
        await _trades.EnsureSchemaAsync(ct);
        await _friends.EnsureSchemaAsync(ct);
        await _siege.EnsureSchemaAsync(ct);
        await _mails.EnsureSchemaAsync(ct);
        await _sessions.EnsureReadyAsync(ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class SqliteAccountStore : IAccountStore
{
    private readonly string _cs;
    public SqliteAccountStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS accounts (
              account_id TEXT PRIMARY KEY,
              username TEXT NOT NULL COLLATE NOCASE UNIQUE,
              password_hash TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            """;
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
            await using var conn = new SqliteConnection(_cs);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO accounts(account_id, username, password_hash, created_at) VALUES($id,$u,$h,$c)";
            cmd.Parameters.AddWithValue("$id", account.AccountId);
            cmd.Parameters.AddWithValue("$u", account.Username);
            cmd.Parameters.AddWithValue("$h", account.PasswordHash);
            cmd.Parameters.AddWithValue("$c", account.CreatedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
            return (true, null, account);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return (false, "用户名已存在", null);
        }
    }

    public async Task<(bool Ok, string? Error, AccountRecord? Account)> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT account_id, username, password_hash, created_at FROM accounts WHERE username = $u COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$u", username.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return (false, "用户名或密码错误", null);

        var account = new AccountRecord
        {
            AccountId = reader.GetString(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(3))
        };
        if (!PasswordHasher.Verify(password, account.PasswordHash))
            return (false, "用户名或密码错误", null);
        return (true, null, account);
    }

    public async Task<AccountRecord?> GetByIdAsync(string accountId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT account_id, username, password_hash, created_at FROM accounts WHERE account_id = $id";
        cmd.Parameters.AddWithValue("$id", accountId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new AccountRecord
        {
            AccountId = reader.GetString(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(3))
        };
    }
}

internal sealed class SqliteCharacterStore : ICharacterStore
{
    private readonly string _cs;
    public SqliteCharacterStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS characters (
              character_id TEXT PRIMARY KEY,
              account_id TEXT NOT NULL,
              name TEXT NOT NULL COLLATE NOCASE UNIQUE,
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
              inventory_json TEXT NOT NULL,
              gold INTEGER NOT NULL DEFAULT 100,
              quest_json TEXT NOT NULL DEFAULT '[]',
              equipment_json TEXT NOT NULL DEFAULT '[]',
              guild_id TEXT NOT NULL DEFAULT '',
              skills_json TEXT NOT NULL DEFAULT '[]',
              dungeon_progress_json TEXT NOT NULL DEFAULT '{}',
              appearance_json TEXT NOT NULL DEFAULT '{}',
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_characters_account ON characters(account_id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        // Migrate older DBs created before M3
        foreach (string alter in new[]
                 {
                     "ALTER TABLE characters ADD COLUMN gold INTEGER NOT NULL DEFAULT 100",
                     "ALTER TABLE characters ADD COLUMN quest_json TEXT NOT NULL DEFAULT '[]'",
                     "ALTER TABLE characters ADD COLUMN equipment_json TEXT NOT NULL DEFAULT '[]'",
                     "ALTER TABLE characters ADD COLUMN exp INTEGER NOT NULL DEFAULT 0",
                     "ALTER TABLE characters ADD COLUMN guild_id TEXT NOT NULL DEFAULT ''",
                     "ALTER TABLE characters ADD COLUMN skills_json TEXT NOT NULL DEFAULT '[]'",
                     "ALTER TABLE characters ADD COLUMN dungeon_progress_json TEXT NOT NULL DEFAULT '{}'",
                     "ALTER TABLE characters ADD COLUMN appearance_json TEXT NOT NULL DEFAULT '{}'"
                 })
        {
            try
            {
                await using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = alter;
                await alterCmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqliteException)
            {
                // column already exists
            }
        }
    }

    public async Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(string accountId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM characters WHERE account_id = $a ORDER BY created_at";
        cmd.Parameters.AddWithValue("$a", accountId);
        var list = new List<CharacterRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadCharacter(reader));
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
        if (string.IsNullOrEmpty(character.DungeonProgressJson))
            character.DungeonProgressJson = "{}";
        if (string.IsNullOrEmpty(character.AppearanceJson))
            character.AppearanceJson = AppearanceJson.Empty;
        if (character.Gold < 0)
            character.Gold = QuestJson.StartingGold;
        character.CreatedAt = DateTimeOffset.UtcNow;
        character.UpdatedAt = character.CreatedAt;

        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);

        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM characters WHERE account_id = $a";
            countCmd.Parameters.AddWithValue("$a", character.AccountId);
            long count = (long)(await countCmd.ExecuteScalarAsync(ct) ?? 0L);
            if (count >= maxPerAccount)
                return (false, $"每账号最多 {maxPerAccount} 个角色", null);
        }

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO characters(character_id, account_id, name, class_id, level, exp, map_id, x, y, hp, mp, max_hp, max_mp, inventory_json, gold, quest_json, equipment_json, guild_id, skills_json, dungeon_progress_json, appearance_json, created_at, updated_at)
                VALUES($cid,$aid,$n,$cl,$lv,$exp,$m,$x,$y,$hp,$mp,$mhp,$mmp,$inv,$gold,$qj,$eqj,$gid,$sj,$dp,$ap,$c,$u)
                """;
            BindCharacter(cmd, character);
            await cmd.ExecuteNonQueryAsync(ct);
            return (true, null, character.Clone());
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return (false, "角色名已存在", null);
        }
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(string accountId, string characterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM characters WHERE character_id = $cid AND account_id = $aid";
        cmd.Parameters.AddWithValue("$cid", characterId);
        cmd.Parameters.AddWithValue("$aid", accountId);
        int n = await cmd.ExecuteNonQueryAsync(ct);
        return n > 0 ? (true, null) : (false, "角色不存在");
    }

    public async Task<CharacterRecord?> GetByIdAsync(string characterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM characters WHERE character_id = $id";
        cmd.Parameters.AddWithValue("$id", characterId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return ReadCharacter(reader);
    }

    public async Task<CharacterRecord?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM characters WHERE LOWER(name) = LOWER($n) LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return ReadCharacter(reader);
    }

    public async Task SavePositionAsync(string characterId, string mapId, int x, int y, int hp, int mp, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET map_id=$m, x=$x, y=$y, hp=$hp, mp=$mp, updated_at=$u WHERE character_id=$id";
        cmd.Parameters.AddWithValue("$m", mapId);
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$y", y);
        cmd.Parameters.AddWithValue("$hp", hp);
        cmd.Parameters.AddWithValue("$mp", mp);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveInventoryAsync(string characterId, string inventoryJson, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET inventory_json=$inv, updated_at=$u WHERE character_id=$id";
        cmd.Parameters.AddWithValue("$inv", string.IsNullOrWhiteSpace(inventoryJson) ? InventoryJson.Empty : inventoryJson);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveGoldAsync(string characterId, int gold, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET gold=$g, updated_at=$u WHERE character_id=$id";
        cmd.Parameters.AddWithValue("$g", Math.Max(0, gold));
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveQuestsAsync(string characterId, string questJson, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET quest_json=$qj, updated_at=$u WHERE character_id=$id";
        cmd.Parameters.AddWithValue("$qj", string.IsNullOrWhiteSpace(questJson) ? QuestJson.Empty : questJson);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveEquipmentAsync(string characterId, string equipmentJson, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET equipment_json=$eqj, updated_at=$u WHERE character_id=$id";
        cmd.Parameters.AddWithValue("$eqj", string.IsNullOrWhiteSpace(equipmentJson) ? EquipmentJson.Empty : equipmentJson);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveLevelExpAsync(string characterId, int level, int exp, int maxHp, int maxMp, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET level=$lv, exp=$exp, max_hp=$mhp, max_mp=$mmp, updated_at=$u WHERE character_id=$id";
        cmd.Parameters.AddWithValue("$lv", Math.Max(1, level));
        cmd.Parameters.AddWithValue("$exp", Math.Max(0, exp));
        cmd.Parameters.AddWithValue("$mhp", Math.Max(1, maxHp));
        cmd.Parameters.AddWithValue("$mmp", Math.Max(0, maxMp));
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveSkillsAsync(string characterId, string skillsJson, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET skills_json=$sj, updated_at=$u WHERE character_id=$id";
        cmd.Parameters.AddWithValue("$sj", string.IsNullOrWhiteSpace(skillsJson) ? "[]" : skillsJson);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveDungeonProgressAsync(string characterId, string dungeonProgressJson, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET dungeon_progress_json=$dp, updated_at=$u WHERE character_id=$id";
        cmd.Parameters.AddWithValue("$dp", string.IsNullOrWhiteSpace(dungeonProgressJson) ? "{}" : dungeonProgressJson);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveAppearanceAsync(string characterId, string appearanceJson, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET appearance_json=$ap, updated_at=$u WHERE character_id=$id";
        cmd.Parameters.AddWithValue("$ap", string.IsNullOrWhiteSpace(appearanceJson) ? AppearanceJson.Empty : appearanceJson);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static CharacterRecord ReadCharacter(SqliteDataReader reader)
        => new()
        {
            CharacterId = reader.GetString(reader.GetOrdinal("character_id")),
            AccountId = reader.GetString(reader.GetOrdinal("account_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            ClassId = reader.GetString(reader.GetOrdinal("class_id")),
            Level = reader.GetInt32(reader.GetOrdinal("level")),
            Exp = SafeGetInt(reader, "exp"),
            MapId = reader.GetString(reader.GetOrdinal("map_id")),
            X = reader.GetInt32(reader.GetOrdinal("x")),
            Y = reader.GetInt32(reader.GetOrdinal("y")),
            Hp = reader.GetInt32(reader.GetOrdinal("hp")),
            Mp = reader.GetInt32(reader.GetOrdinal("mp")),
            MaxHp = reader.GetInt32(reader.GetOrdinal("max_hp")),
            MaxMp = reader.GetInt32(reader.GetOrdinal("max_mp")),
            InventoryJson = reader.GetString(reader.GetOrdinal("inventory_json")),
            Gold = reader.GetInt32(reader.GetOrdinal("gold")),
            QuestJson = reader.GetString(reader.GetOrdinal("quest_json")),
            EquipmentJson = reader.GetString(reader.GetOrdinal("equipment_json")),
            GuildId = SafeGetString(reader, "guild_id"),
            SkillsJson = string.IsNullOrEmpty(SafeGetString(reader, "skills_json")) ? "[]" : SafeGetString(reader, "skills_json"),
            DungeonProgressJson = string.IsNullOrEmpty(SafeGetString(reader, "dungeon_progress_json")) ? "{}" : SafeGetString(reader, "dungeon_progress_json"),
            AppearanceJson = string.IsNullOrEmpty(SafeGetString(reader, "appearance_json")) ? AppearanceJson.Empty : SafeGetString(reader, "appearance_json"),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
        };

    private static void BindCharacter(SqliteCommand cmd, CharacterRecord c)
    {
        cmd.Parameters.AddWithValue("$cid", c.CharacterId);
        cmd.Parameters.AddWithValue("$aid", c.AccountId);
        cmd.Parameters.AddWithValue("$n", c.Name);
        cmd.Parameters.AddWithValue("$cl", c.ClassId);
        cmd.Parameters.AddWithValue("$lv", c.Level);
        cmd.Parameters.AddWithValue("$exp", c.Exp);
        cmd.Parameters.AddWithValue("$m", c.MapId);
        cmd.Parameters.AddWithValue("$x", c.X);
        cmd.Parameters.AddWithValue("$y", c.Y);
        cmd.Parameters.AddWithValue("$hp", c.Hp);
        cmd.Parameters.AddWithValue("$mp", c.Mp);
        cmd.Parameters.AddWithValue("$mhp", c.MaxHp);
        cmd.Parameters.AddWithValue("$mmp", c.MaxMp);
        cmd.Parameters.AddWithValue("$inv", c.InventoryJson);
        cmd.Parameters.AddWithValue("$gold", c.Gold);
        cmd.Parameters.AddWithValue("$qj", c.QuestJson);
        cmd.Parameters.AddWithValue("$eqj", c.EquipmentJson);
        cmd.Parameters.AddWithValue("$gid", c.GuildId ?? "");
        cmd.Parameters.AddWithValue("$sj", string.IsNullOrWhiteSpace(c.SkillsJson) ? "[]" : c.SkillsJson);
        cmd.Parameters.AddWithValue("$dp", string.IsNullOrWhiteSpace(c.DungeonProgressJson) ? "{}" : c.DungeonProgressJson);
        cmd.Parameters.AddWithValue("$ap", string.IsNullOrWhiteSpace(c.AppearanceJson) ? AppearanceJson.Empty : c.AppearanceJson);
        cmd.Parameters.AddWithValue("$c", c.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$u", c.UpdatedAt.ToString("O"));
    }

    public async Task SaveGuildIdAsync(string characterId, string? guildId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET guild_id = $g, updated_at = $u WHERE character_id = $id";
        cmd.Parameters.AddWithValue("$g", guildId ?? "");
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string SafeGetString(SqliteDataReader reader, string column)
    {
        try
        {
            int ord = reader.GetOrdinal(column);
            return reader.IsDBNull(ord) ? "" : reader.GetString(ord);
        }
        catch (IndexOutOfRangeException)
        {
            return "";
        }
    }

    private static int SafeGetInt(SqliteDataReader reader, string column)
    {
        try
        {
            int ord = reader.GetOrdinal(column);
            return reader.IsDBNull(ord) ? 0 : reader.GetInt32(ord);
        }
        catch (IndexOutOfRangeException)
        {
            return 0;
        }
    }
}
