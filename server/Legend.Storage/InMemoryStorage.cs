using System.Collections.Concurrent;

namespace Legend.Storage;

public sealed class InMemoryStorage : ILegendStorage
{
    private readonly InMemoryAccountStore _accounts = new();
    private readonly InMemoryCharacterStore _characters = new();
    private readonly InMemorySessionStore _sessions = new();
    private readonly InMemoryGuildStore _guilds = new();
    private readonly InMemoryTradeStore _trades = new();

    public IAccountStore Accounts => _accounts;
    public ICharacterStore Characters => _characters;
    public ISessionStore Sessions => _sessions;
    public IGuildStore Guilds => _guilds;
    public ITradeStore Trades => _trades;
    public string BackendName => "InMemory";

    public Task InitializeAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class InMemoryAccountStore : IAccountStore
{
    private readonly ConcurrentDictionary<string, AccountRecord> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _usernameToId = new(StringComparer.OrdinalIgnoreCase);

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<(bool Ok, string? Error, AccountRecord? Account)> RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        username = username.Trim();
        if (username.Length < 3 || username.Length > 32)
            return Task.FromResult<(bool, string?, AccountRecord?)>((false, "用户名长度须为 3–32", null));
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            return Task.FromResult<(bool, string?, AccountRecord?)>((false, "密码至少 6 位", null));
        if (_usernameToId.ContainsKey(username))
            return Task.FromResult<(bool, string?, AccountRecord?)>((false, "用户名已存在", null));

        var account = new AccountRecord
        {
            AccountId = Guid.NewGuid().ToString("N"),
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (!_usernameToId.TryAdd(username, account.AccountId))
            return Task.FromResult<(bool, string?, AccountRecord?)>((false, "用户名已存在", null));
        _byId[account.AccountId] = account;
        return Task.FromResult<(bool, string?, AccountRecord?)>((true, null, account));
    }

    public Task<(bool Ok, string? Error, AccountRecord? Account)> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (!_usernameToId.TryGetValue(username.Trim(), out string? id) || !_byId.TryGetValue(id, out var account))
            return Task.FromResult<(bool, string?, AccountRecord?)>((false, "用户名或密码错误", null));
        if (!PasswordHasher.Verify(password, account.PasswordHash))
            return Task.FromResult<(bool, string?, AccountRecord?)>((false, "用户名或密码错误", null));
        return Task.FromResult<(bool, string?, AccountRecord?)>((true, null, account));
    }

    public Task<AccountRecord?> GetByIdAsync(string accountId, CancellationToken ct = default)
    {
        _byId.TryGetValue(accountId, out var a);
        return Task.FromResult(a);
    }
}

internal sealed class InMemoryCharacterStore : ICharacterStore
{
    private readonly ConcurrentDictionary<string, CharacterRecord> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _names = new(StringComparer.OrdinalIgnoreCase);

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(string accountId, CancellationToken ct = default)
    {
        var list = _byId.Values.Where(c => c.AccountId == accountId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.Clone())
            .ToList();
        return Task.FromResult<IReadOnlyList<CharacterRecord>>(list);
    }

    public Task<(bool Ok, string? Error, CharacterRecord? Character)> CreateAsync(CharacterRecord character, int maxPerAccount, CancellationToken ct = default)
    {
        character.Name = character.Name.Trim();
        if (character.Name.Length < 2 || character.Name.Length > 16)
            return Task.FromResult<(bool, string?, CharacterRecord?)>((false, "角色名长度须为 2–16", null));
        if (_names.ContainsKey(character.Name))
            return Task.FromResult<(bool, string?, CharacterRecord?)>((false, "角色名已存在", null));

        int count = _byId.Values.Count(c => c.AccountId == character.AccountId);
        if (count >= maxPerAccount)
            return Task.FromResult<(bool, string?, CharacterRecord?)>((false, $"每账号最多 {maxPerAccount} 个角色", null));

        if (string.IsNullOrEmpty(character.CharacterId))
            character.CharacterId = Guid.NewGuid().ToString("N");
        character.CreatedAt = DateTimeOffset.UtcNow;
        character.UpdatedAt = character.CreatedAt;
        if (string.IsNullOrEmpty(character.InventoryJson))
            character.InventoryJson = InventoryJson.Empty;
        if (string.IsNullOrEmpty(character.QuestJson))
            character.QuestJson = QuestJson.Empty;
        if (string.IsNullOrEmpty(character.EquipmentJson))
            character.EquipmentJson = EquipmentJson.Empty;
        if (character.Gold < 0)
            character.Gold = QuestJson.StartingGold;

        if (!_names.TryAdd(character.Name, 0))
            return Task.FromResult<(bool, string?, CharacterRecord?)>((false, "角色名已存在", null));
        _byId[character.CharacterId] = character.Clone();
        return Task.FromResult<(bool, string?, CharacterRecord?)>((true, null, character.Clone()));
    }

    public Task<(bool Ok, string? Error)> DeleteAsync(string accountId, string characterId, CancellationToken ct = default)
    {
        if (!_byId.TryGetValue(characterId, out var c) || c.AccountId != accountId)
            return Task.FromResult<(bool, string?)>((false, "角色不存在"));
        _byId.TryRemove(characterId, out _);
        _names.TryRemove(c.Name, out _);
        return Task.FromResult<(bool, string?)>((true, null));
    }

    public Task<CharacterRecord?> GetByIdAsync(string characterId, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(characterId, out var c))
            return Task.FromResult<CharacterRecord?>(c.Clone());
        return Task.FromResult<CharacterRecord?>(null);
    }

    public Task SavePositionAsync(string characterId, string mapId, int x, int y, int hp, int mp, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(characterId, out var c))
        {
            c.MapId = mapId;
            c.X = x;
            c.Y = y;
            c.Hp = hp;
            c.Mp = mp;
            c.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task SaveInventoryAsync(string characterId, string inventoryJson, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(characterId, out var c))
        {
            c.InventoryJson = string.IsNullOrWhiteSpace(inventoryJson) ? InventoryJson.Empty : inventoryJson;
            c.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task SaveGoldAsync(string characterId, int gold, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(characterId, out var c))
        {
            c.Gold = Math.Max(0, gold);
            c.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task SaveQuestsAsync(string characterId, string questJson, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(characterId, out var c))
        {
            c.QuestJson = string.IsNullOrWhiteSpace(questJson) ? QuestJson.Empty : questJson;
            c.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task SaveEquipmentAsync(string characterId, string equipmentJson, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(characterId, out var c))
        {
            c.EquipmentJson = string.IsNullOrWhiteSpace(equipmentJson) ? EquipmentJson.Empty : equipmentJson;
            c.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task SaveLevelExpAsync(string characterId, int level, int exp, int maxHp, int maxMp, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(characterId, out var c))
        {
            c.Level = Math.Max(1, level);
            c.Exp = Math.Max(0, exp);
            c.MaxHp = Math.Max(1, maxHp);
            c.MaxMp = Math.Max(0, maxMp);
            c.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task SaveGuildIdAsync(string characterId, string? guildId, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(characterId, out var c))
        {
            c.GuildId = guildId ?? string.Empty;
            c.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _online = new(StringComparer.Ordinal);

    public Task EnsureReadyAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<string> CreateSessionAsync(string accountId, string username, TimeSpan ttl, CancellationToken ct = default)
    {
        string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        _sessions[token] = new SessionRecord
        {
            Token = token,
            AccountId = accountId,
            Username = username,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
        };
        _online[accountId] = true;
        return Task.FromResult(token);
    }

    public Task<SessionRecord?> GetSessionAsync(string token, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(token, out var s))
            return Task.FromResult<SessionRecord?>(null);
        if (s.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return Task.FromResult<SessionRecord?>(null);
        }
        return Task.FromResult<SessionRecord?>(s);
    }

    public Task BindCharacterAsync(string token, string characterId, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(token, out var s))
            s.CharacterId = characterId;
        return Task.CompletedTask;
    }

    public Task SetOnlineAsync(string accountId, bool online, CancellationToken ct = default)
    {
        _online[accountId] = online;
        return Task.CompletedTask;
    }

    public Task<bool> IsOnlineAsync(string accountId, CancellationToken ct = default)
        => Task.FromResult(_online.TryGetValue(accountId, out bool v) && v);

    public Task RevokeAsync(string token, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(token, out var s))
            _online[s.AccountId] = false;
        return Task.CompletedTask;
    }
}
