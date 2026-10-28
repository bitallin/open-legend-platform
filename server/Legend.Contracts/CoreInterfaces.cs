using Google.Protobuf;

namespace Legend.Contracts;

/// <summary>
/// Domain Event Bus interface for decoupled communication between Login, Game, Script, Pay, and Plugins.
/// </summary>
public interface IEventBus
{
    void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class;
    void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class;
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class;
}

/// <summary>
/// Pluggable lifecycle interface (Load -> Enable -> Disable -> Unload)
/// </summary>
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

/// <summary>
/// Pluggable Anti-Cheat Probe interface
/// </summary>
public interface IAntiCheatProbe
{
    string ProbeName { get; }
    bool ValidateMove(string playerId, int fromX, int fromY, int toX, int toY, out string reason);
}

/// <summary>
/// Client session abstraction exposed to GameModules
/// </summary>
public interface IClientSession
{
    string ConnectionId { get; }
    bool IsHandshaked { get; set; }
    string? AccountId { get; set; }
    string? SessionToken { get; set; }
    string? EntityId { get; set; }
    string? CharacterName { get; set; }
    string? ClassId { get; set; }

    Task SendAsync(MsgId msgId, uint seq, IMessage message, CancellationToken ct = default);
}

/// <summary>
/// Modular server subsystem (Login, Game, etc.)
/// </summary>
public interface IGameModule
{
    string ModuleName { get; }
    Task InitializeAsync(CancellationToken ct = default);
    bool CanHandle(MsgId msgId);
    Task HandlePacketAsync(IClientSession session, PacketFrame frame, CancellationToken ct = default);
    Task OnSessionDisconnectedAsync(IClientSession session, CancellationToken ct = default);
}

/// <summary>
/// Version Pack Manifest & Data Models
/// </summary>
public sealed class VersionPackManifest
{
    public string VersionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = "1.0.0";
    public string RealmId { get; set; } = "realm-1";
    public string RealmName { get; set; } = "经典一区（自研原生）";
    public string DefaultMapFile { get; set; } = "maps/novice_plain.json";
    public string ClassesConfigFile { get; set; } = "configs/classes.json";
    public List<string> ScriptFiles { get; set; } = new();
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

public sealed class LoadedVersionPack
{
    public required VersionPackManifest Manifest { get; init; }
    public required string BaseDirectory { get; init; }
    public required MapConfig Map { get; init; }
    public required IReadOnlyDictionary<string, ClassConfigItem> Classes { get; init; }
    public required IReadOnlyDictionary<string, string> LuaScripts { get; init; }
    public required string PackHash { get; init; }
}

public interface IVersionPackLoader
{
    LoadedVersionPack LoadFromManifest(string manifestPath);
}

// Domain Events
public sealed record PlayerLoggedInEvent(string ConnectionId, string AccountId, string Username, string Platform);
public sealed record PlayerEnteredWorldEvent(IClientSession Session, string EntityId, string CharacterName, string ClassId, string MapId, int X, int Y);
public sealed record PlayerMovedEvent(string EntityId, string CharacterName, string MapId, int FromX, int FromY, int ToX, int ToY);
public sealed record ZoneLoadedEvent(string VersionId, string RealmId, string PackHash);
