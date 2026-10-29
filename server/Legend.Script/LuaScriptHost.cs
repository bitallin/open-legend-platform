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
        _eventBus.Subscribe<MonsterKilledEvent>(OnMonsterKilledAsync);
        _eventBus.Subscribe<LootPickedEvent>(OnLootPickedAsync);
        _eventBus.Subscribe<NpcTalkedEvent>(OnNpcTalkedAsync);
        _eventBus.Subscribe<ShopBoughtEvent>(OnShopBoughtAsync);
        _eventBus.Subscribe<QuestAcceptedEvent>(OnQuestAcceptedAsync);
        _eventBus.Subscribe<QuestCompletedEvent>(OnQuestCompletedAsync);
        _eventBus.Subscribe<ItemEquippedEvent>(OnItemEquippedAsync);
        _eventBus.Subscribe<EquipmentEnhancedEvent>(OnEquipmentEnhancedAsync);
        _eventBus.Subscribe<PlayerLeveledUpEvent>(OnPlayerLeveledUpAsync);
        _eventBus.Subscribe<ItemUsedEvent>(OnItemUsedAsync);
        _eventBus.Subscribe<GuildCreatedEvent>(OnGuildCreatedAsync);
        _eventBus.Subscribe<GuildJoinedEvent>(OnGuildJoinedAsync);
        _eventBus.Subscribe<GuildLeftEvent>(OnGuildLeftAsync);
        _eventBus.Subscribe<StallListedEvent>(OnStallListedAsync);
        _eventBus.Subscribe<StallBoughtEvent>(OnStallBoughtAsync);
        _eventBus.Subscribe<StallCancelledEvent>(OnStallCancelledAsync);
        _eventBus.Subscribe<PartyJoinedEvent>(OnPartyJoinedAsync);
        _eventBus.Subscribe<PartyLeftEvent>(OnPartyLeftAsync);
        _eventBus.Subscribe<FriendAddedEvent>(OnFriendAddedAsync);
        _eventBus.Subscribe<WhisperSentEvent>(OnWhisperSentAsync);
        _eventBus.Subscribe<SkillLearnedEvent>(OnSkillLearnedAsync);
        _eventBus.Subscribe<SkillLevelUpEvent>(OnSkillLevelUpAsync);
        _eventBus.Subscribe<BuffAppliedEvent>(OnBuffAppliedAsync);
        _eventBus.Subscribe<BuffExpiredEvent>(OnBuffExpiredAsync);
        _eventBus.Subscribe<SiegeDeclaredEvent>(OnSiegeDeclaredAsync);
        _eventBus.Subscribe<SiegeCapturedEvent>(OnSiegeCapturedAsync);
        _eventBus.Subscribe<SiegeEndedEvent>(OnSiegeEndedAsync);
        _eventBus.Subscribe<MailSentEvent>(OnMailSentAsync);
        _eventBus.Subscribe<MailClaimedEvent>(OnMailClaimedAsync);
        _eventBus.Subscribe<DungeonEnteredEvent>(OnDungeonEnteredAsync);
        _eventBus.Subscribe<DungeonCompletedEvent>(OnDungeonCompletedAsync);
        _eventBus.Subscribe<DungeonLeftEvent>(OnDungeonLeftAsync);
        _eventBus.Subscribe<DungeonPartyEnteredEvent>(OnDungeonPartyEnteredAsync);
        _eventBus.Subscribe<WorldBossSpawnedEvent>(OnWorldBossSpawnedAsync);
        _eventBus.Subscribe<WorldBossKilledEvent>(OnWorldBossKilledAsync);
        _eventBus.Subscribe<RareLootBroadcastEvent>(OnRareLootBroadcastAsync);
        _eventBus.Subscribe<MountChangedEvent>(OnMountChangedAsync);
        _eventBus.Subscribe<FashionChangedEvent>(OnFashionChangedAsync);
        _eventBus.Subscribe<PetChangedEvent>(OnPetChangedAsync);
        _eventBus.Subscribe<GuildWarehouseDepositEvent>(OnGuildWarehouseDepositAsync);
        _eventBus.Subscribe<GuildWarehouseWithdrawEvent>(OnGuildWarehouseWithdrawAsync);
        _eventBus.Subscribe<GuildWarehouseClearEvent>(OnGuildWarehouseClearAsync);
    }

    private static MoonSharp.Interpreter.Script CreateHardSandboxVm(LegendLuaBridge bridge)
    {
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
            await evt.Session.SendAsync(MsgId.ServerNotice, 0, notice, ct);
    }

    private async Task OnMonsterKilledAsync(MonsterKilledEvent evt, CancellationToken ct)
    {
        List<ServerNotice> noticesToDeliver;
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_kill");
            if (fn.Type != DataType.Function)
                return;

            _bridge.ClearPendingNotices();
            var ctx = new Table(_luaVm)
            {
                ["killer_entity_id"] = evt.KillerEntityId,
                ["killer_name"] = evt.KillerName,
                ["killer_character_id"] = evt.KillerCharacterId,
                ["victim_entity_id"] = evt.VictimEntityId,
                ["victim_name"] = evt.VictimName,
                ["monster_id"] = evt.MonsterId,
                ["map_id"] = evt.MapId,
                ["x"] = evt.X,
                ["y"] = evt.Y
            };
            _luaVm.Call(fn, ctx);
            noticesToDeliver = _bridge.PendingNotices.ToList();
            _bridge.ClearPendingNotices();
        }

        if (evt.KillerSession != null)
        {
            foreach (var notice in noticesToDeliver)
                await evt.KillerSession.SendAsync(MsgId.ServerNotice, 0, notice, ct);
        }
    }

    private Task OnLootPickedAsync(LootPickedEvent evt, CancellationToken ct)
    {
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_loot");
            if (fn.Type != DataType.Function)
                return Task.CompletedTask;

            _bridge.ClearPendingNotices();
            var ctx = new Table(_luaVm)
            {
                ["picker_name"] = evt.PickerName,
                ["character_id"] = evt.CharacterId,
                ["item_id"] = evt.ItemId,
                ["count"] = evt.Count,
                ["loot_entity_id"] = evt.LootEntityId,
                ["map_id"] = evt.MapId
            };
            _luaVm.Call(fn, ctx);
            _bridge.ClearPendingNotices();
        }
        return Task.CompletedTask;
    }

    private Task OnNpcTalkedAsync(NpcTalkedEvent evt, CancellationToken ct)
    {
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_talk");
            if (fn.Type != DataType.Function)
                return Task.CompletedTask;
            _bridge.ClearPendingNotices();
            var ctx = new Table(_luaVm)
            {
                ["player_name"] = evt.PlayerName,
                ["character_id"] = evt.CharacterId,
                ["npc_id"] = evt.NpcId,
                ["npc_name"] = evt.NpcName,
                ["map_id"] = evt.MapId
            };
            _luaVm.Call(fn, ctx);
            _bridge.ClearPendingNotices();
        }
        return Task.CompletedTask;
    }

    private Task OnShopBoughtAsync(ShopBoughtEvent evt, CancellationToken ct)
    {
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_buy");
            if (fn.Type != DataType.Function)
                return Task.CompletedTask;
            _bridge.ClearPendingNotices();
            var ctx = new Table(_luaVm)
            {
                ["player_name"] = evt.PlayerName,
                ["character_id"] = evt.CharacterId,
                ["npc_id"] = evt.NpcId,
                ["item_id"] = evt.ItemId,
                ["count"] = evt.Count,
                ["gold"] = evt.GoldRemaining,
                ["price_paid"] = evt.PricePaid
            };
            _luaVm.Call(fn, ctx);
            _bridge.ClearPendingNotices();
        }
        return Task.CompletedTask;
    }

    private async Task OnQuestAcceptedAsync(QuestAcceptedEvent evt, CancellationToken ct)
    {
        List<ServerNotice> notices = new();
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_quest_accept");
            if (fn.Type != DataType.Function)
                return;
            _bridge.ClearPendingNotices();
            var ctx = new Table(_luaVm)
            {
                ["player_name"] = evt.PlayerName,
                ["character_id"] = evt.CharacterId,
                ["quest_id"] = evt.QuestId,
                ["quest_title"] = evt.QuestTitle
            };
            _luaVm.Call(fn, ctx);
            notices = _bridge.PendingNotices.ToList();
            _bridge.ClearPendingNotices();
        }
        foreach (var n in notices)
            await evt.Session.SendAsync(MsgId.ServerNotice, 0, n, ct);
    }

    private async Task OnQuestCompletedAsync(QuestCompletedEvent evt, CancellationToken ct)
    {
        List<ServerNotice> notices = new();
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_quest_complete");
            if (fn.Type != DataType.Function)
                return;
            _bridge.ClearPendingNotices();
            var ctx = new Table(_luaVm)
            {
                ["player_name"] = evt.PlayerName,
                ["character_id"] = evt.CharacterId,
                ["quest_id"] = evt.QuestId,
                ["quest_title"] = evt.QuestTitle,
                ["reward_gold"] = evt.RewardGold
            };
            _luaVm.Call(fn, ctx);
            notices = _bridge.PendingNotices.ToList();
            _bridge.ClearPendingNotices();
        }
        foreach (var n in notices)
            await evt.Session.SendAsync(MsgId.ServerNotice, 0, n, ct);
    }

    private async Task OnItemEquippedAsync(ItemEquippedEvent evt, CancellationToken ct)
    {
        List<ServerNotice> notices = new();
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_equip");
            if (fn.Type != DataType.Function)
                return;
            _bridge.ClearPendingNotices();
            var playerTbl = new Table(_luaVm)
            {
                ["name"] = evt.PlayerName,
                ["character_id"] = evt.CharacterId
            };
            var infoTbl = new Table(_luaVm)
            {
                ["slot"] = evt.Slot,
                ["item_id"] = evt.ItemId,
                ["enhance_level"] = evt.EnhanceLevel,
                ["effective_atk"] = evt.EffectiveAtk,
                ["effective_def"] = evt.EffectiveDef,
                ["effective_max_hp"] = evt.EffectiveMaxHp
            };
            _luaVm.Call(fn, playerTbl, infoTbl);
            notices = _bridge.PendingNotices.ToList();
            _bridge.ClearPendingNotices();
        }
        foreach (var n in notices)
            await evt.Session.SendAsync(MsgId.ServerNotice, 0, n, ct);
    }

    private async Task OnEquipmentEnhancedAsync(EquipmentEnhancedEvent evt, CancellationToken ct)
    {
        List<ServerNotice> notices = new();
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_enhance");
            if (fn.Type != DataType.Function)
                return;
            _bridge.ClearPendingNotices();
            var playerTbl = new Table(_luaVm)
            {
                ["name"] = evt.PlayerName,
                ["character_id"] = evt.CharacterId
            };
            var infoTbl = new Table(_luaVm)
            {
                ["slot"] = evt.Slot,
                ["item_id"] = evt.ItemId,
                ["enhance_level"] = evt.EnhanceLevel,
                ["succeeded"] = evt.Succeeded,
                ["cost_gold"] = evt.CostGold,
                ["effective_atk"] = evt.EffectiveAtk
            };
            _luaVm.Call(fn, playerTbl, infoTbl);
            notices = _bridge.PendingNotices.ToList();
            _bridge.ClearPendingNotices();
        }
        foreach (var n in notices)
            await evt.Session.SendAsync(MsgId.ServerNotice, 0, n, ct);
    }

    private async Task OnPlayerLeveledUpAsync(PlayerLeveledUpEvent evt, CancellationToken ct)
    {
        List<ServerNotice> notices = new();
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_level_up");
            if (fn.Type != DataType.Function)
                return;
            _bridge.ClearPendingNotices();
            var playerTbl = new Table(_luaVm)
            {
                ["name"] = evt.PlayerName,
                ["character_id"] = evt.CharacterId
            };
            var infoTbl = new Table(_luaVm)
            {
                ["old_level"] = evt.OldLevel,
                ["new_level"] = evt.NewLevel,
                ["exp"] = evt.Exp,
                ["effective_atk"] = evt.EffectiveAtk,
                ["effective_def"] = evt.EffectiveDef,
                ["effective_max_hp"] = evt.EffectiveMaxHp
            };
            _luaVm.Call(fn, playerTbl, infoTbl);
            notices = _bridge.PendingNotices.ToList();
            _bridge.ClearPendingNotices();
        }
        foreach (var n in notices)
            await evt.Session.SendAsync(MsgId.ServerNotice, 0, n, ct);
    }

    private async Task OnItemUsedAsync(ItemUsedEvent evt, CancellationToken ct)
    {
        List<ServerNotice> notices = new();
        lock (_luaLock)
        {
            DynValue fn = _luaVm.Globals.Get("on_use_item");
            if (fn.Type != DataType.Function)
                return;
            _bridge.ClearPendingNotices();
            var playerTbl = new Table(_luaVm)
            {
                ["name"] = evt.PlayerName,
                ["character_id"] = evt.CharacterId
            };
            var infoTbl = new Table(_luaVm)
            {
                ["item_id"] = evt.ItemId,
                ["heal_hp"] = evt.HealHp,
                ["heal_mp"] = evt.HealMp,
                ["hp"] = evt.Hp,
                ["mp"] = evt.Mp
            };
            _luaVm.Call(fn, playerTbl, infoTbl);
            notices = _bridge.PendingNotices.ToList();
            _bridge.ClearPendingNotices();
        }
        foreach (var n in notices)
            await evt.Session.SendAsync(MsgId.ServerNotice, 0, n, ct);
    }

    private Task OnGuildCreatedAsync(GuildCreatedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_guild_create");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.GuildName);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_guild_create failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnGuildJoinedAsync(GuildJoinedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_guild_join");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.GuildName);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_guild_join failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnGuildLeftAsync(GuildLeftEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_guild_leave");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.GuildName);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_guild_leave failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnStallListedAsync(StallListedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_stall_list");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.SellerName, evt.ItemId, evt.Count, evt.TotalPriceGold);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_stall_list failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnStallBoughtAsync(StallBoughtEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_stall_buy");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.BuyerName, evt.SellerName, evt.ItemId, evt.Count, evt.TotalPriceGold);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_stall_buy failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnStallCancelledAsync(StallCancelledEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_stall_cancel");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.SellerName, evt.ItemId, evt.Count);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_stall_cancel failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnPartyJoinedAsync(PartyJoinedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_party_join");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.PartyId, evt.LeaderName);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_party_join failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnPartyLeftAsync(PartyLeftEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_party_leave");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.PartyId, evt.Reason);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_party_leave failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnFriendAddedAsync(FriendAddedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_friend_add");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.FriendName);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_friend_add failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnWhisperSentAsync(WhisperSentEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_whisper");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.FromName, evt.ToName, evt.Content);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_whisper failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnSkillLearnedAsync(SkillLearnedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_learn_skill");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.SkillId, evt.Level);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_learn_skill failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnSkillLevelUpAsync(SkillLevelUpEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_skill_level_up");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.SkillId, evt.OldLevel, evt.NewLevel);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_skill_level_up failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnBuffAppliedAsync(BuffAppliedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_buff_apply");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.BuffId, evt.DurationMs);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_buff_apply failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnBuffExpiredAsync(BuffExpiredEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_buff_expire");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.BuffId, evt.Reason);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_buff_expire failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }



    private Task OnSiegeDeclaredAsync(SiegeDeclaredEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_siege_declare");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.CastleId, evt.GuildId, evt.GuildName, evt.LeaderName, evt.CostGold);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_siege_declare failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnSiegeCapturedAsync(SiegeCapturedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_siege_capture");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.CastleId, evt.GuildId, evt.GuildName, evt.CapturerName);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_siege_capture failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnSiegeEndedAsync(SiegeEndedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_siege_end");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.CastleId, evt.OwnerGuildId, evt.OwnerGuildName);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_siege_end failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }


    private Task OnMailSentAsync(MailSentEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_mail_send");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.ToName, evt.Subject, evt.AttachGold, evt.FromName);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_mail_send failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnMailClaimedAsync(MailClaimedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_mail_claim");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.MailId, evt.ClaimedGold);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_mail_claim failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnDungeonEnteredAsync(DungeonEnteredEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_dungeon_enter");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.DungeonId, evt.InstanceId);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_dungeon_enter failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnDungeonCompletedAsync(DungeonCompletedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_dungeon_complete");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.DungeonId, evt.RewardGold);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_dungeon_complete failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnDungeonLeftAsync(DungeonLeftEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_dungeon_leave");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.DungeonId, evt.Completed);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_dungeon_leave failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnDungeonPartyEnteredAsync(DungeonPartyEnteredEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_dungeon_party_enter");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.LeaderName, evt.DungeonId, evt.InstanceId, evt.EnteredCount, evt.DeniedCount);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_dungeon_party_enter failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }
    private Task OnWorldBossSpawnedAsync(WorldBossSpawnedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_world_boss_spawn");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.BossId, evt.DisplayName, evt.MapId, evt.X, evt.Y);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_world_boss_spawn failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnWorldBossKilledAsync(WorldBossKilledEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_world_boss_kill");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.KillerName, evt.BossId, evt.TopHateDamage);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_world_boss_kill failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnRareLootBroadcastAsync(RareLootBroadcastEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_rare_loot_broadcast");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.KillerName, evt.BossId, evt.ItemId, evt.ItemCount);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_rare_loot_broadcast failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnMountChangedAsync(MountChangedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_mount");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.CharacterId, evt.MountId, evt.Action, evt.MoveIntervalMs);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_mount failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnFashionChangedAsync(FashionChangedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_fashion_equip");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.CharacterId, evt.FashionId, evt.Equipped, evt.EffectiveAtk);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_fashion_equip failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnPetChangedAsync(PetChangedEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                string fnName = string.Equals(evt.Action, "summon", StringComparison.OrdinalIgnoreCase)
                    ? "on_pet_summon" : "on_pet_dismiss";
                DynValue fn = _luaVm.Globals.Get(fnName);
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                if (string.Equals(evt.Action, "summon", StringComparison.OrdinalIgnoreCase))
                    _luaVm.Call(fn, evt.PlayerName, evt.CharacterId, evt.PetId, evt.PetEntityId);
                else
                    _luaVm.Call(fn, evt.PlayerName, evt.CharacterId, evt.PetId, evt.Reason);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_pet_* failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnGuildWarehouseDepositAsync(GuildWarehouseDepositEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_guild_warehouse_deposit");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.GuildName, evt.ItemId, evt.Count, evt.UsedSlots);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_guild_warehouse_deposit failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnGuildWarehouseWithdrawAsync(GuildWarehouseWithdrawEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_guild_warehouse_withdraw");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.GuildName, evt.ItemId, evt.Count, evt.UsedSlots);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_guild_warehouse_withdraw failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task OnGuildWarehouseClearAsync(GuildWarehouseClearEvent evt, CancellationToken ct)
    {
        try
        {
            lock (_luaLock)
            {
                DynValue fn = _luaVm.Globals.Get("on_guild_warehouse_clear");
                if (fn.Type != DataType.Function)
                    return Task.CompletedTask;
                _bridge.ClearPendingNotices();
                _luaVm.Call(fn, evt.PlayerName, evt.GuildName, evt.ClearedSlots);
                _bridge.ClearPendingNotices();
            }
        }
        catch (Exception ex)
        {
            StructuredLogger.Error("LuaSandbox", $"on_guild_warehouse_clear failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }
}
