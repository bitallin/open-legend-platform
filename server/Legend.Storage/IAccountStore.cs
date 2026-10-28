namespace Legend.Storage;

public interface IAccountStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<(bool Ok, string? Error, AccountRecord? Account)> RegisterAsync(string username, string password, CancellationToken ct = default);
    Task<(bool Ok, string? Error, AccountRecord? Account)> AuthenticateAsync(string username, string password, CancellationToken ct = default);
    Task<AccountRecord?> GetByIdAsync(string accountId, CancellationToken ct = default);
}

public interface ICharacterStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(string accountId, CancellationToken ct = default);
    Task<(bool Ok, string? Error, CharacterRecord? Character)> CreateAsync(CharacterRecord character, int maxPerAccount, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> DeleteAsync(string accountId, string characterId, CancellationToken ct = default);
    Task<CharacterRecord?> GetByIdAsync(string characterId, CancellationToken ct = default);
    Task SavePositionAsync(string characterId, string mapId, int x, int y, int hp, int mp, CancellationToken ct = default);
    Task SaveInventoryAsync(string characterId, string inventoryJson, CancellationToken ct = default);
    Task SaveGoldAsync(string characterId, int gold, CancellationToken ct = default);
    Task SaveQuestsAsync(string characterId, string questJson, CancellationToken ct = default);
    Task SaveEquipmentAsync(string characterId, string equipmentJson, CancellationToken ct = default);
}


public interface ISessionStore
{
    Task EnsureReadyAsync(CancellationToken ct = default);
    Task<string> CreateSessionAsync(string accountId, string username, TimeSpan ttl, CancellationToken ct = default);
    Task<SessionRecord?> GetSessionAsync(string token, CancellationToken ct = default);
    Task BindCharacterAsync(string token, string characterId, CancellationToken ct = default);
    Task SetOnlineAsync(string accountId, bool online, CancellationToken ct = default);
    Task<bool> IsOnlineAsync(string accountId, CancellationToken ct = default);
    Task RevokeAsync(string token, CancellationToken ct = default);
}

public interface ILegendStorage : IAsyncDisposable
{
    IAccountStore Accounts { get; }
    ICharacterStore Characters { get; }
    ISessionStore Sessions { get; }
    string BackendName { get; }
    Task InitializeAsync(CancellationToken ct = default);
}
