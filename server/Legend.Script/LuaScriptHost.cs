using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using MoonSharp.Interpreter;

namespace Legend.Script;

public enum ScriptPermissionLevel
{
    Read = 1,
    GrantItem = 2,
    Admin = 3
}

/// <summary>
/// Whitelist-only API exposed into the MoonSharp Lua sandbox.
/// Uses 100% original API naming (LegendApi.*), never copying any closed-source engine command names.
/// </summary>
[MoonSharpUserData]
public sealed class LegendLuaBridge
{
    private readonly ScriptPermissionLevel _permissionLevel;
    private readonly List<ServerNotice> _pendingNotices = new();

    public LegendLuaBridge(ScriptPermissionLevel permissionLevel)
    {
        _permissionLevel = permissionLevel;
    }

    [MoonSharpHidden]
    public IReadOnlyList<ServerNotice> PendingNotices => _pendingNotices;

    [MoonSharpHidden]
    public void ClearPendingNotices() => _pendingNotices.Clear();

    public void send_welcome(string title, string content)
    {
        _pendingNotices.Add(new ServerNotice
        {
            NoticeType = "LUA_HOOK",
            Title = title,
            Content = content
        });
    }

    public bool grant_starter_item(string itemId, int count)
    {
        if (_permissionLevel < ScriptPermissionLevel.GrantItem)
        {
            throw new ScriptRuntimeException("Permission denied: script lacks GrantItem permission.");
        }

        if (_pendingNotices.Count > 0)
        {
            var last = _pendingNotices[^1];
            last.GrantedItemId = itemId;
            last.GrantedItemCount = count;
        }
        else
        {
            _pendingNotices.Add(new ServerNotice
            {
                NoticeType = "LUA_HOOK",
                Title = "Starter Gift",
                Content = $"Granted {itemId} x{count}",
                GrantedItemId = itemId,
                GrantedItemCount = count
            });
        }
        return true;
    }

    public void log_info(string message)
    {
        StructuredLogger.Info("LuaSandbox", message);
    }
}

public sealed class LuaScriptHost
{
    private readonly IEventBus _eventBus;
    private readonly ScriptPermissionLevel _permissionLevel;
    private readonly object _luaLock = new();
    private MoonSharp.Interpreter.Script _luaVm;
    private LegendLuaBridge _bridge;

    static LuaScriptHost()
    {
        UserData.RegisterType<LegendLuaBridge>();
    }

    public LuaScriptHost(IEventBus eventBus, ScriptPermissionLevel permissionLevel = ScriptPermissionLevel.GrantItem)
    {
        _eventBus = eventBus;
        _permissionLevel = permissionLevel;
        _bridge = new LegendLuaBridge(permissionLevel);
        _luaVm = CreateHardSandboxVm(_bridge);

        _eventBus.Subscribe<PlayerEnteredWorldEvent>(OnPlayerEnteredWorldAsync);
    }

    private static MoonSharp.Interpreter.Script CreateHardSandboxVm(LegendLuaBridge bridge)
    {
        // Preset_HardSandbox strips IO, OS, debug, loadfile, dofile to prevent arbitrary file/system access
        var vm = new MoonSharp.Interpreter.Script(CoreModules.Preset_HardSandbox);
        vm.Globals["legend_api"] = bridge;
        return vm;
    }

    public void LoadScripts(IReadOnlyDictionary<string, string> scripts)
    {
        lock (_luaLock)
        {
            _bridge = new LegendLuaBridge(_permissionLevel);
            _luaVm = CreateHardSandboxVm(_bridge);

            foreach (var (name, code) in scripts)
            {
                _luaVm.DoString(code, null, name);
                StructuredLogger.Info("LuaScriptHost", $"Loaded Lua script into hard sandbox: {name}");
            }
        }
    }

    public void ReloadScriptContent(string scriptName, string luaCode)
    {
        lock (_luaLock)
        {
            _luaVm.DoString(luaCode, null, scriptName);
            StructuredLogger.Info("LuaScriptHost", $"Hot-reloaded Lua script: {scriptName}");
        }
    }

    public DynValue ExecuteRawInSandbox(string luaCode)
    {
        lock (_luaLock)
        {
            return _luaVm.DoString(luaCode);
        }
    }

    private async Task OnPlayerEnteredWorldAsync(PlayerEnteredWorldEvent evt, CancellationToken ct)
    {
        List<ServerNotice> noticesToDeliver;
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_player_enter");
            if (fn.Type != DataType.Function)
                return;

            _bridge.ClearPendingNotices();

            var playerTable = new Table(_luaVm)
            {
                ["entity_id"] = evt.EntityId,
                ["name"] = evt.CharacterName,
                ["class_id"] = evt.ClassId,
                ["map_id"] = evt.MapId,
                ["x"] = evt.X,
                ["y"] = evt.Y
            };

            _luaVm.Call(fn, playerTable);
            noticesToDeliver = _bridge.PendingNotices.ToList();
            _bridge.ClearPendingNotices();
        }

        foreach (var notice in noticesToDeliver)
        {
            await evt.Session.SendAsync(MsgId.ServerNotice, 0, notice, ct);
        }
    }
}
