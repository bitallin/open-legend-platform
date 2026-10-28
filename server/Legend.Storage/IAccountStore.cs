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
    Task SaveLevelExpAsync(string characterId, int level, int exp, int maxHp, int maxMp, CancellationToken ct = default);
    Task SaveGuildIdAsync(string characterId, string? guildId, CancellationToken ct = default);
    Task SaveSkillsAsync(string characterId, string skillsJson, CancellationToken ct = default);
    Task<CharacterRecord?> GetByNameAsync(string name, CancellationToken ct = default);
}

public interface IGuildStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<GuildRecord?> GetByIdAsync(string guildId, CancellationToken ct = default);
    Task<GuildRecord?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<(bool Ok, string? Error, GuildRecord? Guild)> CreateAsync(string name, string leaderCharacterId, CancellationToken ct = default);
    Task<(bool Ok, string? Error, GuildRecord? Guild)> AddMemberAsync(string guildId, string characterId, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> RemoveMemberAsync(string guildId, string characterId, CancellationToken ct = default);
    Task DeleteAsync(string guildId, CancellationToken ct = default);
}

public interface IFriendStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FriendRecord>> ListByOwnerAsync(string ownerCharacterId, CancellationToken ct = default);
    Task<int> CountByOwnerAsync(string ownerCharacterId, CancellationToken ct = default);
    Task<bool> IsFriendAsync(string ownerCharacterId, string friendCharacterId, CancellationToken ct = default);
    Task<(bool Ok, string? Error, FriendRecord? Friend)> AddAsync(string ownerCharacterId, string friendCharacterId, string friendName, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> RemoveAsync(string ownerCharacterId, string friendCharacterId, CancellationToken ct = default);
}

public interface ITradeStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<TradeListingRecord?> GetByIdAsync(string listingId, CancellationToken ct = default);
    Task<IReadOnlyList<TradeListingRecord>> ListActiveAsync(string? itemIdFilter = null, CancellationToken ct = default);
    Task<int> CountActiveBySellerAsync(string sellerCharacterId, CancellationToken ct = default);
    Task<TradeListingRecord> CreateAsync(TradeListingRecord listing, CancellationToken ct = default);
    Task<(bool Ok, string? Error, TradeListingRecord? Listing)> TryClaimSoldAsync(string listingId, string buyerCharacterId, string buyerName, CancellationToken ct = default);
    Task<(bool Ok, string? Error, TradeListingRecord? Listing)> TryCancelAsync(string listingId, string sellerCharacterId, CancellationToken ct = default);
}



public interface ISiegeStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<SiegeStateRecord?> GetAsync(string castleId, CancellationToken ct = default);
    Task UpsertAsync(SiegeStateRecord record, CancellationToken ct = default);
}

public interface IMailStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MailRecord>> ListByOwnerAsync(string ownerCharacterId, CancellationToken ct = default);
    Task<int> CountByOwnerAsync(string ownerCharacterId, CancellationToken ct = default);
    Task<MailRecord?> GetByIdAsync(string mailId, CancellationToken ct = default);
    Task<MailRecord> InsertAsync(MailRecord mail, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> MarkReadAsync(string mailId, string ownerCharacterId, CancellationToken ct = default);
    /// <summary>Atomically mark claimed if not yet claimed. Returns the mail snapshot before claim.</summary>
    Task<(bool Ok, string? Error, MailRecord? Mail)> TryClaimAsync(string mailId, string ownerCharacterId, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> DeleteAsync(string mailId, string ownerCharacterId, bool allowUnclaimedAttachment, CancellationToken ct = default);
    /// <summary>Delete oldest claimed mails with no remaining attachments until under max, or return false if still full.</summary>
    Task<int> EvictOldestClaimedEmptyAsync(string ownerCharacterId, int keepUnder, CancellationToken ct = default);
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
    IGuildStore Guilds { get; }
    ITradeStore Trades { get; }
    IFriendStore Friends { get; }
    ISiegeStore Siege { get; }
    IMailStore Mails { get; }
    string BackendName { get; }
    Task InitializeAsync(CancellationToken ct = default);
}
