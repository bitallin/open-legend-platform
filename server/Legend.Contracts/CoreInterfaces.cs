using Google.Protobuf;

namespace Legend.Contracts;

public interface IEventBus
{
    void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class;
    void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class;
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class;
}

public interface IPlugin
{
    string PluginId { get; }
    string Name { get; }
    string Version { get; }
    bool IsEnabled { get; }
    Task OnLoadAsync(IEventBus eventBus, CancellationToken ct = default);
    Task OnEnableAsync(CancellationToken ct = default);
    Task OnDisableAsync(CancellationToken ct = default);
    Task OnUnloadAsync(CancellationToken ct = default);
}

public interface IAntiCheatProbe
{
    string ProbeName { get; }
    bool ValidateMove(string playerId, int fromX, int fromY, int toX, int toY, out string reason);
    /// <summary>
    /// Attack / skill interval probe. Default rejects when <paramref name="nowUnixMs"/> is before
    /// <paramref name="readyAtUnixMs"/> (readyAt 0 means never cast — always allow).
    /// </summary>
    bool ValidateAttackInterval(string playerId, string skillId, long nowUnixMs, long readyAtUnixMs, out string reason);
}

public interface IClientSession
{
    string ConnectionId { get; }
    bool IsHandshaked { get; set; }
    string? AccountId { get; set; }
    string? SessionToken { get; set; }
    string? EntityId { get; set; }
    string? CharacterId { get; set; }
    string? CharacterName { get; set; }
    string? ClassId { get; set; }
    Task SendAsync(MsgId msgId, uint seq, IMessage message, CancellationToken ct = default);
    /// <summary>Request the underlying transport to close (best-effort kick).</summary>
    void RequestClose();
}

public interface IGameModule
{
    string ModuleName { get; }
    Task InitializeAsync(CancellationToken ct = default);
    bool CanHandle(MsgId msgId);
    Task HandlePacketAsync(IClientSession session, PacketFrame frame, CancellationToken ct = default);
    Task OnSessionDisconnectedAsync(IClientSession session, CancellationToken ct = default);
}

public sealed class VersionPackManifest
{
    public string VersionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = "1.9.0";
    public string RealmId { get; set; } = "realm-1";
    public string RealmName { get; set; } = "经典一区（自研原生）";
    public string DefaultMapFile { get; set; } = "maps/novice_plain.json";
    public List<string> MapFiles { get; set; } = new();
    public string ClassesConfigFile { get; set; } = "configs/classes.json";
    public string SkillsConfigFile { get; set; } = "";
    public string MonstersConfigFile { get; set; } = "";
    public string LootTablesConfigFile { get; set; } = "";
    public string NpcsConfigFile { get; set; } = "";
    public string ShopsConfigFile { get; set; } = "";
    public string QuestsConfigFile { get; set; } = "";
    public string ItemsConfigFile { get; set; } = "";
    public string LevelsConfigFile { get; set; } = "";
    public string GuildConfigFile { get; set; } = "";
    public string TradeConfigFile { get; set; } = "";
    public string PartyConfigFile { get; set; } = "";
    public string BuffsConfigFile { get; set; } = "";
    public string SiegeConfigFile { get; set; } = "";
    public string MailConfigFile { get; set; } = "";
    public string DungeonsConfigFile { get; set; } = "";
    public string WorldBossConfigFile { get; set; } = "";
    public string MountsConfigFile { get; set; } = "";
    public string FashionsConfigFile { get; set; } = "";
    public string PetsConfigFile { get; set; } = "";
    public string CurrenciesConfigFile { get; set; } = "";
    public string RecycleConfigFile { get; set; } = "";
    public string TalentsConfigFile { get; set; } = "";
    public string InternalConfigFile { get; set; } = "";
    public List<string> ScriptFiles { get; set; } = new();
    public int MaxCharactersPerAccount { get; set; } = 3;
}

public sealed class SafeZoneRect
{
    public string Name { get; set; } = "safe";
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }

    public bool Contains(int x, int y)
        => x >= Math.Min(X1, X2) && x <= Math.Max(X1, X2)
        && y >= Math.Min(Y1, Y2) && y <= Math.Max(Y1, Y2);
}

public sealed class PortalConfig
{
    public string PortalId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public string TargetMapId { get; set; } = string.Empty;
    public int TargetX { get; set; }
    public int TargetY { get; set; }
}

public sealed class MapConfig
{
    public string MapId { get; set; } = "novice_plain";
    public string MapName { get; set; } = "新手平原";
    public int Width { get; set; } = 64;
    public int Height { get; set; } = 64;
    public int AoiCellSize { get; set; } = 8;
    public int AoiRadiusCells { get; set; } = 1;
    public int SpawnX { get; set; } = 10;
    public int SpawnY { get; set; } = 10;
    public List<GridCoord> BlockedCells { get; set; } = new();
    /// <summary>Optional compact bitset (row-major, LSB first within each byte), Base64. Overrides BlockedCells when set.</summary>
    public string? BlockedBitsetBase64 { get; set; }
    public List<SafeZoneRect> SafeZones { get; set; } = new();
    public List<PortalConfig> Portals { get; set; } = new();
    /// <summary>Optional map boolean flags (docs/site/04-map-movement.md). Omitted keys are false.</summary>
    public MapFlagsConfig MapFlags { get; set; } = new();

    private bool[]? _blockedCache;

    public bool IsBlocked(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return true;
        EnsureBlockedCache();
        return _blockedCache![y * Width + x];
    }

    public bool IsInSafeZone(int x, int y)
        => SafeZones.Any(z => z.Contains(x, y));

    /// <summary>True when mapFlags.noPk is set. Player-vs-player must be rejected outside safe zones.</summary>
    public bool IsNoPk => MapFlags.NoPk;

    public PortalConfig? FindPortalAt(int x, int y)
        => Portals.FirstOrDefault(p => p.X == x && p.Y == y);

    public void EnsureBlockedCache()
    {
        if (_blockedCache != null)
            return;

        _blockedCache = new bool[Width * Height];
        if (!string.IsNullOrWhiteSpace(BlockedBitsetBase64))
        {
            byte[] bytes = Convert.FromBase64String(BlockedBitsetBase64);
            for (int i = 0; i < _blockedCache.Length; i++)
            {
                int byteIndex = i / 8;
                int bit = i % 8;
                if (byteIndex < bytes.Length)
                    _blockedCache[i] = (bytes[byteIndex] & (1 << bit)) != 0;
            }
        }
        else
        {
            foreach (var c in BlockedCells)
            {
                if (c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height)
                    _blockedCache[c.Y * Width + c.X] = true;
            }
        }
    }

    public string BuildBlockedBitsetBase64()
    {
        EnsureBlockedCache();
        int byteCount = (_blockedCache!.Length + 7) / 8;
        byte[] bytes = new byte[byteCount];
        for (int i = 0; i < _blockedCache.Length; i++)
        {
            if (_blockedCache[i])
                bytes[i / 8] |= (byte)(1 << (i % 8));
        }
        return Convert.ToBase64String(bytes);
    }
}

/// <summary>Map-level boolean flags. Defaults false; schema keys in map.schema.json mapFlags.</summary>
public sealed class MapFlagsConfig
{
    public bool NoPk { get; set; }
    public bool NoMount { get; set; }
    public bool NoSummon { get; set; }
    public bool NoTeleport { get; set; }
    public bool NoTrade { get; set; }
    public bool Instance { get; set; }
    public bool NoHang { get; set; }
    public bool NoAutoPickup { get; set; }
}

public sealed record GridCoord(int X, int Y);

public sealed class ClassConfigItem
{
    public string ClassId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int BaseHp { get; set; } = 100;
    public int BaseMp { get; set; } = 50;
    public int BaseAtk { get; set; } = 15;
    public int BaseDef { get; set; } = 0;
}


public sealed class SkillConfigItem
{
    public string SkillId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int MpCost { get; set; }
    public int CooldownMs { get; set; } = 600;
    public int Range { get; set; } = 1;
    public double DamageMultiplier { get; set; } = 1.0;
    public double DamageMultiplierPerLevel { get; set; }
    public int CooldownMsPerLevel { get; set; }
    public List<string> AllowedClasses { get; set; } = new();
    public bool RequiresLearn { get; set; }
    public int LearnRequiredLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 1;
    public string SkillBookItemId { get; set; } = string.Empty;
    public int SkillExpPerCast { get; set; }
    public int SkillExpPerKill { get; set; }
    public List<int> ExpToNextLevel { get; set; } = new();
    public string ApplyBuffOnCast { get; set; } = string.Empty;
    public string ApplyDebuffOnHit { get; set; } = string.Empty;
    /// <summary>attack (default) | heal | buff | summon | teleport</summary>
    public string Kind { get; set; } = "attack";
    public int HealAmount { get; set; }
    public int HealAmountPerLevel { get; set; }
    public string SummonPetId { get; set; } = string.Empty;
    /// <summary>Folded into cooldown when greater than the skill cooldown.</summary>
    public int CastTimeMs { get; set; }
    /// <summary>self | target. Used when kind is aoe.</summary>
    public string AoeCenter { get; set; } = "";
    public int AoeRadius { get; set; }
    public int ChargeSteps { get; set; }
    public int GroundDurationMs { get; set; }
    public int GroundTickMs { get; set; }
    /// <summary>circle | arc</summary>
    public string AoeShape { get; set; } = "";
}

public sealed class BuffConfigItem
{
    public string BuffId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Kind { get; set; } = "buff";
    public int DurationMs { get; set; }
    public int TickMs { get; set; }
    public int AtkBonusPercent { get; set; }
    public int DefBonusPercent { get; set; }
    public int MoveSlowPercent { get; set; }
    public int DotDamagePerTick { get; set; }
    public string StackRule { get; set; } = "refresh";
    public bool ClearOnDeath { get; set; } = true;
    /// <summary>Flat damage absorbed before HP. Magic shield uses this instead of defense.</summary>
    public int AbsorbAmount { get; set; }
}

public sealed class MonsterTemplate
{
    public string MonsterId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int MaxHp { get; set; } = 50;
    public int Atk { get; set; } = 5;
    public int Def { get; set; } = 0;
    public int AggroRange { get; set; } = 4;
    public int AttackRange { get; set; } = 1;
    public int AttackCooldownMs { get; set; } = 1200;
    public string LootTableId { get; set; } = string.Empty;
    public int RespawnMs { get; set; } = 10000;
    /// <summary>chase | stationary</summary>
    public string Ai { get; set; } = "chase";
    public int ExpReward { get; set; }
}

public sealed class MonsterSpawnPoint
{
    public string MonsterId { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Count { get; set; } = 1;
}

public sealed class MonstersConfigFile
{
    public List<MonsterTemplate> Templates { get; set; } = new();
    public List<MonsterSpawnPoint> Spawns { get; set; } = new();
}

public sealed class LootEntry
{
    public string ItemId { get; set; } = string.Empty;
    public int CountMin { get; set; } = 1;
    public int CountMax { get; set; } = 1;
    public double Chance { get; set; } = 1.0;
}

public sealed class LootTableConfig
{
    public string LootTableId { get; set; } = string.Empty;
    public List<LootEntry> Entries { get; set; } = new();
}

public sealed class InventorySlot
{
    public string ItemId { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool Identified { get; set; } = true;
}


public sealed class NpcTemplate
{
    public string NpcId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public string ShopId { get; set; } = string.Empty;
    public string Dialogue { get; set; } = string.Empty;
    public List<string> QuestIds { get; set; } = new();
    public bool CanRepair { get; set; }
    public bool CanStore { get; set; }
}

public sealed class ShopOfferConfig
{
    public string ItemId { get; set; } = string.Empty;
    public int PriceGold { get; set; }
    /// <summary>-1 = unlimited</summary>
    public int Stock { get; set; } = -1;
}

public sealed class ShopConfig
{
    public string ShopId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<ShopOfferConfig> Offers { get; set; } = new();
}

public sealed class QuestRewardItem
{
    public string ItemId { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

public sealed class QuestTemplate
{
    public string QuestId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AcceptNpcId { get; set; } = string.Empty;
    public string TurnInNpcId { get; set; } = string.Empty;
    /// <summary>kill | gather (M3 uses kill)</summary>
    public string ObjectiveType { get; set; } = "kill";
    public string TargetMonsterId { get; set; } = string.Empty;
    public int TargetCount { get; set; } = 1;
    public int RewardGold { get; set; }
    public List<QuestRewardItem> RewardItems { get; set; } = new();
    /// <summary>Empty = no prerequisite. Otherwise that quest must be completed.</summary>
    public string PrerequisiteQuestId { get; set; } = string.Empty;
    /// <summary>daily | ring | empty</summary>
    public string Repeatable { get; set; } = "";
}

public sealed class QuestProgressState
{
    public string QuestId { get; set; } = string.Empty;
    /// <summary>active | ready | completed</summary>
    public string Status { get; set; } = "active";
    public int Progress { get; set; }
    public string CompletedOn { get; set; } = "";
}

public sealed class ItemTemplate
{
    public string ItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>consumable | material | equipment</summary>
    public string Kind { get; set; } = "consumable";
    /// <summary>weapon | armor | ring | ""</summary>
    public string Slot { get; set; } = string.Empty;
    public int AtkBonus { get; set; }
    public int DefBonus { get; set; }
    public int MaxHpBonus { get; set; }
    public int EnhanceAtkPerLevel { get; set; }
    public int EnhanceDefPerLevel { get; set; }
    public int EnhanceHpPerLevel { get; set; }
    public string RequiredClass { get; set; } = string.Empty;
    public int RequiredLevel { get; set; } = 1;
    /// <summary>Consumable HP restore on use (0 = none).</summary>
    public int HealHp { get; set; }
    /// <summary>Consumable MP restore on use (0 = none).</summary>
    public int HealMp { get; set; }
    /// <summary>Skillbook: skill id taught on use.</summary>
    public string TeachesSkillId { get; set; } = string.Empty;
    /// <summary>Consumable/scroll: buff id applied on use.</summary>
    public string AppliesBuffId { get; set; } = string.Empty;
    /// <summary>M15: mount unlock item → mountId.</summary>
    public string UnlockMountId { get; set; } = string.Empty;
    /// <summary>M15: fashion unlock item → fashionId.</summary>
    public string UnlockFashionId { get; set; } = string.Empty;
    /// <summary>M16: pet unlock item → petId.</summary>
    public string UnlockPetId { get; set; } = string.Empty;
    /// <summary>NPC recycle price. 0 = derive from shop buy price.</summary>
    public int SellPriceGold { get; set; }
    public int Weight { get; set; }
    public bool NeedsIdentify { get; set; }
    /// <summary>When kind is recall, empty means the pack default town spawn.</summary>
    public string RecallMapId { get; set; } = "";
    public string SetId { get; set; } = "";
}

public sealed class LevelEntryConfig
{
    public int Level { get; set; }
    public int ExpToNext { get; set; }
}

public sealed class ClassGrowthConfig
{
    public int AtkPerLevel { get; set; }
    public int DefPerLevel { get; set; }
    public int HpPerLevel { get; set; }
    public int MpPerLevel { get; set; }
}

public sealed class GuildConfig
{
    public int CreateCostGold { get; set; } = 50;
    public int MinNameLength { get; set; } = 2;
    public int MaxNameLength { get; set; } = 12;
    public int MaxMembers { get; set; } = 20;
    public int ChatMaxLength { get; set; } = 80;
    public int ChatCooldownMs { get; set; } = 50;
    /// <summary>Chebyshev distance for Nearby chat (same map).</summary>
    public int NearbyChebyshev { get; set; } = 14;
    /// <summary>Max distinct item stacks in the shared guild warehouse.</summary>
    public int WarehouseCapacitySlots { get; set; } = 40;
    /// <summary>Max stack size per item id in the guild warehouse.</summary>
    public int WarehouseMaxStackPerItem { get; set; } = 999;
    /// <summary>Any member (including leader) may deposit when true.</summary>
    public bool MemberCanDeposit { get; set; } = true;
    /// <summary>Any member (including leader) may withdraw when true.</summary>
    public bool MemberCanWithdraw { get; set; } = true;
    /// <summary>Guild leader may clear (empty) the warehouse when true.</summary>
    public bool LeaderCanClear { get; set; } = true;
    /// <summary>Vice leader may clear the warehouse when true.</summary>
    public bool ViceLeaderCanClear { get; set; } = true;
    /// <summary>Warehouse admin may clear the warehouse when true.</summary>
    public bool WarehouseAdminCanClear { get; set; } = true;
    /// <summary>Ordinary members may clear the warehouse when true (default false).</summary>
    public bool MemberCanClear { get; set; } = false;
    /// <summary>Only the guild leader may appoint/dismiss ranks when true.</summary>
    public bool LeaderCanSetRank { get; set; } = true;
    /// <summary>Max concurrent vice_leader appointments (excluding the leader).</summary>
    public int MaxViceLeaders { get; set; } = 2;
    /// <summary>Max concurrent warehouse_admin appointments.</summary>
    public int MaxWarehouseAdmins { get; set; } = 3;
    /// <summary>Max warehouse audit entries returned by ListAudit (and retention hint).</summary>
    public int AuditLogLimit { get; set; } = 100;
}

public sealed class TradeConfig
{
    public int MaxListingsPerChar { get; set; } = 10;
    public int MinUnitPriceGold { get; set; } = 1;
    public int MaxUnitPriceGold { get; set; } = 100000;
    public int TaxRatePercent { get; set; } = 0;
    public bool RequireSafeZone { get; set; } = true;
}

public sealed class SiegeCaptureZone
{
    public string MapId { get; set; } = "dark_forest";
    public int X1 { get; set; } = 16;
    public int Y1 { get; set; } = 16;
    public int X2 { get; set; } = 24;
    public int Y2 { get; set; } = 24;

    public bool Contains(string mapId, int x, int y)
        => string.Equals(MapId, mapId, StringComparison.OrdinalIgnoreCase)
        && x >= Math.Min(X1, X2) && x <= Math.Max(X1, X2)
        && y >= Math.Min(Y1, Y2) && y <= Math.Max(Y1, Y2);
}

public sealed class SiegeConfig
{
    public string CastleId { get; set; } = "sabuk_castle";
    public string DisplayName { get; set; } = "沙巴克王城";
    public int DeclareCostGold { get; set; } = 50;
    public bool LeaderOnlyDeclare { get; set; } = true;
    public SiegeCaptureZone CaptureZone { get; set; } = new();
    public string OwnerBuffId { get; set; } = "buff_castle_lord";
    public string OwnerTitle { get; set; } = "沙巴克城主行会";
    public int WinnerRewardGold { get; set; } = 100;
    public string ScheduleNote { get; set; } = "";
    public int OpenHour { get; set; } = -1;
    public int CloseHour { get; set; } = -1;
    public string GateMonsterId { get; set; } = "castle_gate";
    public string GatePortalId { get; set; } = "gate";
}


public sealed class DungeonSpawnConfig
{
    public string MonsterId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Count { get; set; } = 1;
}

public sealed class DungeonRewardItemConfig
{
    public string ItemId { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

public sealed class DungeonDefinition
{
    public string DungeonId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TemplateMapId { get; set; } = string.Empty;
    public int MinLevel { get; set; } = 1;
    public int EntryCostGold { get; set; }
    public int DailyLimit { get; set; }
    /// <summary>Seconds between successful enters. 0 = none.</summary>
    public int CooldownSeconds { get; set; }
    /// <summary>Local day boundary hour in Asia/Shanghai (0 = midnight).</summary>
    public int ResetHourLocal { get; set; }
    public int EnterX { get; set; }
    public int EnterY { get; set; }
    public string ExitMapId { get; set; } = "novice_plain";
    public int ExitX { get; set; } = 10;
    public int ExitY { get; set; } = 10;
    /// <summary>kill_all | kill_boss</summary>
    public string Objective { get; set; } = "kill_all";
    public List<DungeonSpawnConfig> Spawns { get; set; } = new();
    public int RewardGold { get; set; }
    public List<DungeonRewardItemConfig> RewardItems { get; set; } = new();
}

public sealed class DungeonsConfigFile
{
    public List<DungeonDefinition> Dungeons { get; set; } = new();
}

public sealed class WorldBossRareDropConfig
{
    public string ItemId { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
    public double Chance { get; set; } = 1.0;
    public bool Broadcast { get; set; } = true;
}

public sealed class WorldBossDefinition
{
    public string BossId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string MonsterId { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int ParticipateRadius { get; set; } = 30;
    public int RespawnSeconds { get; set; } = 120;
    public bool SpawnOnStart { get; set; } = true;
    public int HateTopN { get; set; } = 5;
    /// <summary>top_hate: highest cumulative damage wins; ties → most recent hit among tied.</summary>
    public string KillCreditRule { get; set; } = "top_hate";
    public int RewardGold { get; set; }
    public List<WorldBossRareDropConfig> RareDrops { get; set; } = new();
}


public sealed class MountDefinition
{
    public string MountId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int MoveSpeedBonusPercent { get; set; } = 50;
}

public sealed class MountsConfigFile
{
    public int DefaultMoveIntervalMs { get; set; } = 300;
    public List<MountDefinition> Mounts { get; set; } = new();
}

public sealed class FashionDefinition
{
    public string FashionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ItemId { get; set; } = "";
    /// <summary>Small combat bonus while equipped (M15: ATK only).</summary>
    public int AtkBonus { get; set; }
}

public sealed class FashionsConfigFile
{
    public List<FashionDefinition> Fashions { get; set; } = new();
}

public sealed class PetDefinition
{
    public string PetId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ItemId { get; set; } = "";
    /// <summary>Chebyshev distance beyond which pet steps toward owner each tick.</summary>
    public int FollowDistance { get; set; } = 2;
    /// <summary>Assist hit damage = floor(owner.Atk * percent / 100), min 1.</summary>
    public int AssistDamagePercent { get; set; } = 25;
    public int AssistCooldownMs { get; set; } = 1200;
    public int AssistRange { get; set; } = 3;
}

public sealed class PetsConfigFile
{
    public List<PetDefinition> Pets { get; set; } = new();
}


public sealed class WorldBossConfigFile
{
    public List<WorldBossDefinition> Bosses { get; set; } = new();
}

public sealed class MailConfig
{
    public int MaxMailsPerCharacter { get; set; } = 50;
    public int RetentionDays { get; set; } = 30;
    public int MaxInventorySlots { get; set; } = 40;
    public string SystemSenderName { get; set; } = "系统";
}

public sealed class PartyConfig
{
    public int MaxMembers { get; set; } = 5;
    public bool ExpShareSameMapOnly { get; set; } = true;
    public int ExpBonusPercent { get; set; } = 10;
    public int MaxFriends { get; set; } = 50;
    public bool WhisperRequiresFriend { get; set; } = true;
}

public sealed class LevelsConfigFile
{
    public int MaxLevel { get; set; } = 10;
    public List<LevelEntryConfig> Entries { get; set; } = new();
    public Dictionary<string, ClassGrowthConfig> ClassGrowth { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int DeathGoldPercent { get; set; } = 10;
    public int DeathItemChancePercent { get; set; } = 25;
    public int DeathMaxItems { get; set; } = 1;

    public int GetExpToNext(int level)
    {
        foreach (var e in Entries)
            if (e.Level == level)
                return Math.Max(0, e.ExpToNext);
        return 0;
    }

    public ClassGrowthConfig GetGrowth(string classId)
    {
        foreach (var kv in ClassGrowth)
        {
            if (string.Equals(kv.Key, classId, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return new ClassGrowthConfig { AtkPerLevel = 2, DefPerLevel = 1, HpPerLevel = 15, MpPerLevel = 5 };
    }
}

public sealed class EnhanceRuleConfig
{
    public int TargetLevel { get; set; }
    public int CostGold { get; set; } = 20;
    public double SuccessRate { get; set; } = 1.0;
}

public sealed class ItemsConfigFile
{
    public List<ItemTemplate> Items { get; set; } = new();
    public List<EnhanceRuleConfig> EnhanceRules { get; set; } = new();
}

public sealed class EquippedItemState
{
    public string Slot { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public int EnhanceLevel { get; set; }
    public int Durability { get; set; }
    public int MaxDurability { get; set; }
    public bool Identified { get; set; } = true;
}

/// <summary>One currency declared in configs/currencies.json (docs/site/11).</summary>
public sealed class CurrencyConfigItem
{
    public string CurrencyId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Tradable { get; set; } = true;
    public bool StallAllowed { get; set; } = true;
    public bool AuctionAllowed { get; set; } = true;
    /// <summary>Deduct group: bound currency of the same group is preferred before unbound.</summary>
    public string DeductGroup { get; set; } = "gold";
}

/// <summary>NPC recycle row in configs/recycle.json (docs/site/11). Daily limit is per character per itemId.</summary>
public sealed class RecycleConfigItem
{
    public string ItemId { get; set; } = string.Empty;
    public string CurrencyId { get; set; } = "gold";
    public int Amount { get; set; }
    public int GrantExp { get; set; }
    /// <summary>0 = unlimited. Counts pieces recycled today (Asia/Shanghai day).</summary>
    public int DailyLimit { get; set; }
    public bool DestroyItem { get; set; } = true;
}

/// <summary>One talent effect: whitelist statId or skillModifierId (docs/site/03).</summary>
public sealed class TalentEffectConfig
{
    public string StatId { get; set; } = "";
    public string SkillModifierId { get; set; } = "";
    public int Value { get; set; }
}

public sealed class TalentNodeConfig
{
    public string NodeId { get; set; } = "";
    public List<string> Requires { get; set; } = new();
    public int CostPoints { get; set; } = 1;
    public List<TalentEffectConfig> Effects { get; set; } = new();
}

public sealed class TalentTreeConfig
{
    public string TreeId { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<TalentNodeConfig> Nodes { get; set; } = new();
}

public sealed class TalentResetCostConfig
{
    public string CurrencyId { get; set; } = "gold";
    public int Amount { get; set; }
}

/// <summary>configs/talents.json — trees only; no MsgId handlers in E10.</summary>
public sealed class TalentsConfigFile
{
    public int PointsPerLevel { get; set; } = 1;
    public TalentResetCostConfig ResetCost { get; set; } = new();
    public List<TalentTreeConfig> Trees { get; set; } = new();
}

public sealed class InternalLevelEntry
{
    public int Level { get; set; }
    public int ExpToNext { get; set; }
    public int AtkBonus { get; set; }
    public int DefBonus { get; set; }
    public int MaxHpBonus { get; set; }
    public int MaxMpBonus { get; set; }
    /// <summary>Optional MP tax while internal level is active (percent of max MP reserved).</summary>
    public int MpCostPercent { get; set; }
}

public sealed class MeridianMaterialConfig
{
    public string ItemId { get; set; } = "";
    public int Count { get; set; } = 1;
}

public sealed class MeridianConfigItem
{
    public string MeridianId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int RequiresInternalLevel { get; set; }
    public List<MeridianMaterialConfig> Materials { get; set; } = new();
    public List<TalentEffectConfig> Effects { get; set; } = new();
}

/// <summary>configs/internal.json — second XP pool + optional meridians (docs/site/03).</summary>
public sealed class InternalConfigFile
{
    public int MaxLevel { get; set; } = 1;
    public List<InternalLevelEntry> Entries { get; set; } = new();
    public List<MeridianConfigItem> Meridians { get; set; } = new();
}

public sealed class LoadedVersionPack
{
    public required VersionPackManifest Manifest { get; init; }
    public required string BaseDirectory { get; init; }
    public required MapConfig Map { get; init; }
    public required IReadOnlyDictionary<string, MapConfig> Maps { get; init; }
    public required IReadOnlyDictionary<string, ClassConfigItem> Classes { get; init; }
    public required IReadOnlyDictionary<string, SkillConfigItem> Skills { get; init; }
    public IReadOnlyDictionary<string, BuffConfigItem> Buffs { get; init; } = new Dictionary<string, BuffConfigItem>(StringComparer.OrdinalIgnoreCase);
    public required IReadOnlyDictionary<string, MonsterTemplate> Monsters { get; init; }
    public required IReadOnlyList<MonsterSpawnPoint> MonsterSpawns { get; init; }
    public required IReadOnlyDictionary<string, LootTableConfig> LootTables { get; init; }
    public required IReadOnlyDictionary<string, NpcTemplate> Npcs { get; init; }
    public required IReadOnlyDictionary<string, ShopConfig> Shops { get; init; }
    public required IReadOnlyDictionary<string, QuestTemplate> Quests { get; init; }
    public IReadOnlyDictionary<string, ItemTemplate> Items { get; init; } = new Dictionary<string, ItemTemplate>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<int, EnhanceRuleConfig> EnhanceRules { get; init; } = new Dictionary<int, EnhanceRuleConfig>();
    public LevelsConfigFile Levels { get; init; } = new();
    public GuildConfig Guild { get; init; } = new();
    public TradeConfig Trade { get; init; } = new();
    public PartyConfig Party { get; init; } = new();
    public SiegeConfig Siege { get; init; } = new();
    public MailConfig Mail { get; init; } = new();
    public DungeonsConfigFile Dungeons { get; init; } = new();
    public WorldBossConfigFile WorldBoss { get; init; } = new();
    public MountsConfigFile Mounts { get; init; } = new();
    public FashionsConfigFile Fashions { get; init; } = new();
    public PetsConfigFile Pets { get; init; } = new();
    /// <summary>Enabled currencies. Sample packs usually only declare gold.</summary>
    public IReadOnlyDictionary<string, CurrencyConfigItem> Currencies { get; init; } =
        new Dictionary<string, CurrencyConfigItem>(StringComparer.OrdinalIgnoreCase);
    /// <summary>NPC recycle table keyed by itemId.</summary>
    public IReadOnlyDictionary<string, RecycleConfigItem> Recycle { get; init; } =
        new Dictionary<string, RecycleConfigItem>(StringComparer.OrdinalIgnoreCase);
    public TalentsConfigFile Talents { get; init; } = new();
    public InternalConfigFile Internal { get; init; } = new();
    public required IReadOnlyDictionary<string, string> LuaScripts { get; init; }
    public required string PackHash { get; init; }

    public MapConfig GetMap(string mapId)
        => Maps.TryGetValue(mapId, out var m) ? m : Map;
}

public interface IVersionPackLoader
{
    LoadedVersionPack LoadFromManifest(string manifestPath);
}

public sealed record PlayerLoggedInEvent(string ConnectionId, string AccountId, string Username, string Platform);
public sealed record PlayerEnteredWorldEvent(IClientSession Session, string EntityId, string CharacterName, string ClassId, string MapId, int X, int Y);
public sealed record PlayerMovedEvent(string EntityId, string CharacterName, string MapId, int FromX, int FromY, int ToX, int ToY);
public sealed record ZoneLoadedEvent(string VersionId, string RealmId, string PackHash);
public sealed record MonsterKilledEvent(IClientSession? KillerSession, string KillerEntityId, string KillerName, string KillerCharacterId, string VictimEntityId, string VictimName, string MonsterId, string MapId, int X, int Y);
public sealed record LootPickedEvent(IClientSession Session, string PickerName, string CharacterId, string ItemId, int Count, string LootEntityId, string MapId);
public sealed record NpcTalkedEvent(IClientSession Session, string PlayerName, string CharacterId, string NpcId, string NpcName, string MapId);
public sealed record ShopBoughtEvent(IClientSession Session, string PlayerName, string CharacterId, string NpcId, string ItemId, int Count, int PricePaid, int GoldRemaining);
public sealed record QuestAcceptedEvent(IClientSession Session, string PlayerName, string CharacterId, string QuestId, string QuestTitle);
public sealed record QuestCompletedEvent(IClientSession Session, string PlayerName, string CharacterId, string QuestId, string QuestTitle, int RewardGold);
public sealed record ItemEquippedEvent(IClientSession Session, string PlayerName, string CharacterId, string Slot, string ItemId, int EnhanceLevel, int EffectiveAtk, int EffectiveDef, int EffectiveMaxHp);
public sealed record EquipmentEnhancedEvent(IClientSession Session, string PlayerName, string CharacterId, string Slot, string ItemId, int EnhanceLevel, bool Succeeded, int CostGold, int EffectiveAtk);
public sealed record PlayerLeveledUpEvent(IClientSession Session, string PlayerName, string CharacterId, int OldLevel, int NewLevel, int Exp, int EffectiveAtk, int EffectiveDef, int EffectiveMaxHp);
public sealed record ItemUsedEvent(IClientSession Session, string PlayerName, string CharacterId, string ItemId, int HealHp, int HealMp, int Hp, int Mp);
public sealed record GuildCreatedEvent(IClientSession Session, string PlayerName, string CharacterId, string GuildId, string GuildName);
public sealed record GuildJoinedEvent(IClientSession Session, string PlayerName, string CharacterId, string GuildId, string GuildName);
public sealed record GuildLeftEvent(IClientSession Session, string PlayerName, string CharacterId, string GuildId, string GuildName);

public sealed record StallListedEvent(IClientSession Session, string SellerName, string SellerCharacterId, string ListingId, string ItemId, int Count, int UnitPriceGold, int TotalPriceGold);
public sealed record StallBoughtEvent(IClientSession Session, string BuyerName, string BuyerCharacterId, string SellerName, string SellerCharacterId, string ListingId, string ItemId, int Count, int TotalPriceGold, int NetSellerGold);
public sealed record StallCancelledEvent(IClientSession Session, string SellerName, string SellerCharacterId, string ListingId, string ItemId, int Count);

public sealed record PartyJoinedEvent(IClientSession Session, string PlayerName, string CharacterId, string PartyId, string LeaderName);
public sealed record PartyLeftEvent(IClientSession Session, string PlayerName, string CharacterId, string PartyId, string Reason);
public sealed record FriendAddedEvent(IClientSession Session, string PlayerName, string CharacterId, string FriendCharacterId, string FriendName);
public sealed record WhisperSentEvent(IClientSession Session, string FromName, string FromCharacterId, string ToName, string ToCharacterId, string Content);

public sealed record SkillLearnedEvent(IClientSession Session, string PlayerName, string CharacterId, string SkillId, int Level);
public sealed record SkillLevelUpEvent(IClientSession Session, string PlayerName, string CharacterId, string SkillId, int OldLevel, int NewLevel);
public sealed record BuffAppliedEvent(IClientSession Session, string PlayerName, string CharacterId, string BuffId, int DurationMs);
public sealed record BuffExpiredEvent(IClientSession Session, string PlayerName, string CharacterId, string BuffId, string Reason);
public sealed record SiegeDeclaredEvent(IClientSession Session, string CastleId, string GuildId, string GuildName, string LeaderName, int CostGold);
public sealed record SiegeCapturedEvent(IClientSession Session, string CastleId, string GuildId, string GuildName, string CapturerName);
public sealed record SiegeEndedEvent(string CastleId, string OwnerGuildId, string OwnerGuildName);

public sealed record MailSentEvent(string ToCharacterId, string ToName, string MailId, string FromName, string Subject, int AttachGold);
public sealed record MailClaimedEvent(IClientSession Session, string PlayerName, string CharacterId, string MailId, int ClaimedGold);

public sealed record DungeonEnteredEvent(IClientSession Session, string PlayerName, string CharacterId, string DungeonId, string InstanceId);
public sealed record DungeonCompletedEvent(IClientSession Session, string PlayerName, string CharacterId, string DungeonId, string InstanceId, int RewardGold);
public sealed record DungeonLeftEvent(IClientSession Session, string PlayerName, string CharacterId, string DungeonId, string InstanceId, bool Completed);
public sealed record DungeonPartyEnteredEvent(IClientSession Session, string LeaderName, string LeaderCharacterId, string DungeonId, string InstanceId, int EnteredCount, int DeniedCount);
public sealed record WorldBossSpawnedEvent(string BossId, string DisplayName, string MapId, int X, int Y, string EntityId, string Reason);
public sealed record WorldBossKilledEvent(string BossId, string DisplayName, string KillerCharacterId, string KillerName, long TopHateDamage, string MapId, int X, int Y);
public sealed record MountChangedEvent(string CharacterId, string PlayerName, string MountId, string Action, int MoveIntervalMs);
public sealed record FashionChangedEvent(string CharacterId, string PlayerName, string FashionId, bool Equipped, int EffectiveAtk);
public sealed record RareLootBroadcastEvent(string BossId, string KillerCharacterId, string KillerName, string ItemId, int ItemCount, int Gold, string Content);

public sealed record PetChangedEvent(string CharacterId, string PlayerName, string PetId, string PetEntityId, string Action, string Reason);
public sealed record GuildWarehouseDepositEvent(IClientSession Session, string PlayerName, string CharacterId, string GuildId, string GuildName, string ItemId, int Count, int UsedSlots);
public sealed record GuildWarehouseWithdrawEvent(IClientSession Session, string PlayerName, string CharacterId, string GuildId, string GuildName, string ItemId, int Count, int UsedSlots);
public sealed record GuildWarehouseClearEvent(IClientSession Session, string PlayerName, string CharacterId, string GuildId, string GuildName, int ClearedSlots);
public sealed record GuildRankChangedEvent(IClientSession Session, string ActorName, string ActorCharacterId, string GuildId, string GuildName, string TargetCharacterId, string TargetName, string OldRank, string NewRank);
public sealed record GuildWarehouseAuditEvent(IClientSession Session, string PlayerName, string CharacterId, string GuildId, string GuildName, string Action, string ItemId, int Count, string AuditId);
