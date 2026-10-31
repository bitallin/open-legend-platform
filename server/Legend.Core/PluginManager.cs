using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Legend.Contracts;

namespace Legend.Core;

public sealed class PluginManager
{
    private readonly IEventBus _eventBus;
    private readonly ConcurrentDictionary<string, IPlugin> _plugins = new();

    public PluginManager(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public IReadOnlyCollection<IPlugin> Plugins => _plugins.Values.ToList();

    public async Task RegisterAndEnableAsync(IPlugin plugin, CancellationToken ct = default)
    {
        if (!_plugins.TryAdd(plugin.PluginId, plugin))
            throw new InvalidOperationException($"Plugin '{plugin.PluginId}' is already registered.");

        await plugin.OnLoadAsync(_eventBus, ct);
        await plugin.OnEnableAsync(ct);
        StructuredLogger.Info("PluginManager", $"Plugin loaded and enabled: {plugin.Name} ({plugin.PluginId} v{plugin.Version})");
    }

    public async Task DisablePluginAsync(string pluginId, CancellationToken ct = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var plugin))
            throw new KeyNotFoundException($"Plugin '{pluginId}' not found.");

        if (plugin.IsEnabled)
        {
            await plugin.OnDisableAsync(ct);
            StructuredLogger.Info("PluginManager", $"Plugin disabled: {plugin.PluginId}");
        }
    }

    public async Task EnablePluginAsync(string pluginId, CancellationToken ct = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var plugin))
            throw new KeyNotFoundException($"Plugin '{pluginId}' not found.");

        if (!plugin.IsEnabled)
        {
            await plugin.OnEnableAsync(ct);
            StructuredLogger.Info("PluginManager", $"Plugin enabled: {plugin.PluginId}");
        }
    }

    public async Task UnloadPluginAsync(string pluginId, CancellationToken ct = default)
    {
        if (!_plugins.TryRemove(pluginId, out var plugin))
            throw new KeyNotFoundException($"Plugin '{pluginId}' not found.");

        if (plugin.IsEnabled)
        {
            await plugin.OnDisableAsync(ct);
        }
        await plugin.OnUnloadAsync(ct);
        StructuredLogger.Info("PluginManager", $"Plugin unloaded: {plugin.PluginId}");
    }

    public async Task<IReadOnlyList<IPlugin>> LoadFromAssemblyPathAsync(string assemblyPath, CancellationToken ct = default)
    {
        var alc = new AssemblyLoadContext($"Plugin_{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: true);
        Assembly asm = alc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        var loaded = new List<IPlugin>();

        foreach (var type in asm.GetExportedTypes())
        {
            if (typeof(IPlugin).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
            {
                if (Activator.CreateInstance(type) is IPlugin plugin)
                {
                    await RegisterAndEnableAsync(plugin, ct);
                    loaded.Add(plugin);
                }
            }
        }
        return loaded;
    }
}

/// <summary>
/// Built-in sample plugin demonstrating hot-swappable audit & telemetry on Domain Event Bus
/// </summary>
public sealed class TelemetryAuditPlugin : IPlugin
{
    private IEventBus? _eventBus;
    private int _loginCount;
    private int _moveCount;

    public string PluginId => "legend.plugin.telemetry-audit";
    public string Name => "Telemetry & Audit Trail Plugin";
    public string Version => "1.0.0";
    public bool IsEnabled { get; private set; }

    public int LoginCount => _loginCount;
    public int MoveCount => _moveCount;

    public Task OnLoadAsync(IEventBus eventBus, CancellationToken ct = default)
    {
        _eventBus = eventBus;
        return Task.CompletedTask;
    }

    public Task OnEnableAsync(CancellationToken ct = default)
    {
        if (_eventBus != null && !IsEnabled)
        {
            _eventBus.Subscribe<PlayerLoggedInEvent>(OnPlayerLoggedInAsync);
            _eventBus.Subscribe<PlayerMovedEvent>(OnPlayerMovedAsync);
            IsEnabled = true;
        }
        return Task.CompletedTask;
    }

    public Task OnDisableAsync(CancellationToken ct = default)
    {
        if (_eventBus != null && IsEnabled)
        {
            _eventBus.Unsubscribe<PlayerLoggedInEvent>(OnPlayerLoggedInAsync);
            _eventBus.Unsubscribe<PlayerMovedEvent>(OnPlayerMovedAsync);
            IsEnabled = false;
        }
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync(CancellationToken ct = default)
    {
        _eventBus = null;
        return Task.CompletedTask;
    }

    private Task OnPlayerLoggedInAsync(PlayerLoggedInEvent evt, CancellationToken ct)
    {
        if (IsEnabled)
        {
            Interlocked.Increment(ref _loginCount);
        }
        return Task.CompletedTask;
    }

    private Task OnPlayerMovedAsync(PlayerMovedEvent evt, CancellationToken ct)
    {
        if (IsEnabled)
        {
            Interlocked.Increment(ref _moveCount);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Default pluggable anti-cheat speed/distance probe
/// </summary>
public sealed class DefaultAntiCheatProbe : IAntiCheatProbe
{
    private readonly int _maxStepDistance;

    public DefaultAntiCheatProbe(int maxStepDistance = 2)
    {
        _maxStepDistance = maxStepDistance;
    }

    public string ProbeName => "DefaultGridSpeedProbe";

    public bool ValidateMove(string playerId, int fromX, int fromY, int toX, int toY, out string reason)
    {
        int dx = Math.Abs(toX - fromX);
        int dy = Math.Abs(toY - fromY);
        int chebyshev = Math.Max(dx, dy);

        if (chebyshev > _maxStepDistance)
        {
            reason = $"Move step {chebyshev} exceeds max allowed distance {_maxStepDistance}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool ValidateAttackInterval(string playerId, string skillId, long nowUnixMs, long readyAtUnixMs, out string reason)
    {
        if (readyAtUnixMs > 0 && nowUnixMs < readyAtUnixMs)
        {
            reason = "技能冷却中";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
