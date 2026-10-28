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
    public string ProtocolVersion { get; set; } = "1.3.0";
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

public sealed record GridCoord(int X, int Y);

public sealed class ClassConfigItem
{
    public string ClassId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int BaseHp { get; set; } = 100;
    public int BaseMp { get; set; } = 50;
    public int BaseAtk { get; set; } = 15;
}


public sealed class SkillConfigItem
{
    public string SkillId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int MpCost { get; set; }
    public int CooldownMs { get; set; } = 600;
    public int Range { get; set; } = 1;
    public double DamageMultiplier { get; set; } = 1.0;
    public List<string> AllowedClasses { get; set; } = new();
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
}

public sealed class QuestProgressState
{
    public string QuestId { get; set; } = string.Empty;
    /// <summary>active | ready | completed</summary>
    public string Status { get; set; } = "active";
    public int Progress { get; set; }
}

public sealed class LoadedVersionPack
{
    public required VersionPackManifest Manifest { get; init; }
    public required string BaseDirectory { get; init; }
    public required MapConfig Map { get; init; }
    public required IReadOnlyDictionary<string, MapConfig> Maps { get; init; }
    public required IReadOnlyDictionary<string, ClassConfigItem> Classes { get; init; }
    public required IReadOnlyDictionary<string, SkillConfigItem> Skills { get; init; }
    public required IReadOnlyDictionary<string, MonsterTemplate> Monsters { get; init; }
    public required IReadOnlyList<MonsterSpawnPoint> MonsterSpawns { get; init; }
    public required IReadOnlyDictionary<string, LootTableConfig> LootTables { get; init; }
    public required IReadOnlyDictionary<string, NpcTemplate> Npcs { get; init; }
    public required IReadOnlyDictionary<string, ShopConfig> Shops { get; init; }
    public required IReadOnlyDictionary<string, QuestTemplate> Quests { get; init; }
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

