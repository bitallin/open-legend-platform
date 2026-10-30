using Legend.ClientCore;
using Legend.Contracts.Proto;
using Legend.Playable.Net;
using Legend.Playable.World;

namespace Legend.Playable.Session;

public sealed partial class PlayableSession : IDisposable
{
    private PlayableNetClient _net = new();
    private bool _suppressDisconnect;
    private readonly List<CharacterSummary> _characters = new();

    public LegendSessionFsm Fsm { get; } = new();
    public WorldState World { get; } = new();
    public InventoryViewModel Inventory { get; } = new();
    public ChatLogBuffer Chat { get; } = new(40);
    public string Host { get; private set; } = LaunchOptions.DefaultHost;
    public int Port { get; private set; } = LaunchOptions.DefaultPort;
    public string RealmId { get; private set; } = LaunchOptions.DefaultRealmId;
    public string LastUsername { get; private set; } = "";
    public string LastPassword { get; private set; } = "";
    public int Level { get; private set; } = 1;
    public int Exp { get; private set; }
    public int ExpToNext { get; private set; }
    public int Mp { get; private set; }
    public int MaxMp { get; private set; } = 1;
    public int Atk { get; private set; }
    public int Def { get; private set; }
    public string ClassId { get; private set; } = "warrior";
    public string GuildName { get; private set; } = "";
    public string GuildRank { get; private set; } = "";
    public string PartyId { get; private set; } = "";
    public TalkResp? LastTalk { get; private set; }
    public string PendingPartyId { get; private set; } = "";
    public string PendingInviter { get; private set; } = "";
    public IReadOnlyList<CharacterSummary> Characters => _characters;
    public PlayableNetClient Net => _net;

    public event Action? EnteredWorld;
    public event Action<CombatSync>? CombatSeen;
    public event Action? PickedUp;
    public event Action? Disconnected;
    public event Action? MetaChanged;
    public event Action? LeveledUp;

    public void Configure(LaunchOptions options)
    {
        Host = options.Host;
        Port = options.Port;
        RealmId = options.RealmId;
        if (!string.IsNullOrWhiteSpace(options.Username))
            LastUsername = options.Username;
        if (!string.IsNullOrEmpty(options.Password))
            LastPassword = options.Password;
    }

    public void Pump() => _net.Pump();

    public async Task LoginAsync(string username, string password)
    {
        LastUsername = username.Trim();
        LastPassword = password;
        if (!Fsm.BeginConnect(Host, Port))
            return;
        try
        {
            ReplaceNet();
            await _net.ConnectAsync(Host, Port);
            await _net.HandshakeAsync();
            await _net.RegisterAsync(LastUsername, LastPassword);
            await _net.LoginAsync(LastUsername, LastPassword);
            await RefreshCharactersAsync();
            Fsm.MarkAuthenticated();
        }
        catch (Exception ex)
        {
            Fsm.Fail(ex.Message);
        }
    }

    public async Task RefreshCharactersAsync()
    {
        var list = await _net.CharListAsync();
        _characters.Clear();
        _characters.AddRange(list);
    }

    public async Task DeleteCharacterAsync(string characterId)
    {
        await _net.DeleteCharAsync(characterId);
        await RefreshCharactersAsync();
    }

    public async Task CreateCharacterAsync(string name, string classId, string gender = "m")
    {
        if (Fsm.Phase != SessionPhase.CharSelect)
            return;
        try
        {
            await _net.CreateCharAsync(name.Trim(), classId, gender);
            await RefreshCharactersAsync();
            Fsm.SetStatus("已创建 " + name.Trim());
        }
        catch (Exception ex)
        {
            Fsm.SetStatus(ex.Message);
            throw;
        }
    }

    public async Task EnterAsync(CharacterSummary character)
    {
        if (Fsm.Phase != SessionPhase.CharSelect)
            return;
        Fsm.BeginEnterWorld(character.Name);
        try
        {
            await _net.SelectCharAsync(character.CharacterId);
            await _net.EnterWorldAsync(RealmId, character.CharacterId);
            _net.Pump();
            ApplyEnterVitals(character);
            SyncInventory();
            ClassId = string.IsNullOrWhiteSpace(character.ClassId) ? ClassId : character.ClassId;
            GuildName = _net.GuildName;
            GuildRank = _net.GuildRank;
            CopyMeta();
            if (World.Self != null)
                World.Self.Title = Legend.Contracts.PhasePRules.Title(Level, GuildRank);
            var map = _net.SelfEntity?.MapId ?? character.MapId;
            Fsm.MarkInWorld(string.IsNullOrEmpty(map) ? "?" : map);
            Chat.Add($"[系统] 进入 {World.Map.MapName}");
        }
        catch (Exception ex)
        {
            Fsm.AbortEnterWorld(ex.Message);
            throw;
        }
    }

    public async Task<MoveSync?> MoveAsync(int toX, int toY, int direction)
    {
        if (Fsm.Phase != SessionPhase.InWorld)
            return null;
        var sync = await _net.RequestMoveAsync(toX, toY, direction);
        World.ApplyMove(sync);
        return sync;
    }

    public async Task<AttackResp?> AttackAsync(string targetId, string? skillId = null, bool force = false)
    {
        if (Fsm.Phase != SessionPhase.InWorld)
            return null;
        string skill = string.IsNullOrWhiteSpace(skillId) ? PlayableTuning.BasicSkillId : skillId;
        var resp = await _net.RequestAttackAsync(targetId, skill, force);
        if (resp.Success || resp.RemainingMp > 0 || (resp.Message ?? "").Contains("MP", StringComparison.OrdinalIgnoreCase))
            Mp = Math.Max(0, resp.RemainingMp);
        return resp;
    }

    public async Task<PickupResp?> PickupAsync(string lootId)
    {
        if (Fsm.Phase != SessionPhase.InWorld)
            return null;
        var resp = await _net.RequestPickupAsync(lootId);
        if (resp.Success)
        {
            SyncInventory();
            PickedUp?.Invoke();
        }
        return resp;
    }

    public async Task<TalkResp?> TalkAsync(string npcId)
    {
        if (Fsm.Phase != SessionPhase.InWorld)
            return null;
        var resp = await _net.RequestTalkAsync(npcId);
        _npcEntityId = npcId;
        if (resp.Success)
        {
            string who = string.IsNullOrWhiteSpace(resp.NpcName) ? "NPC" : resp.NpcName;
            if (!string.IsNullOrWhiteSpace(resp.Dialogue))
                Chat.Add($"[{who}] {resp.Dialogue}");
            LastTalk = resp;
            MetaChanged?.Invoke();
        }
        else if (!string.IsNullOrWhiteSpace(resp.Message))
        {
            Chat.Add("[NPC] " + resp.Message);
        }
        return resp;
    }

    public async Task<string?> UseItemAsync(string itemId)
    {
        if (Fsm.Phase != SessionPhase.InWorld)
            return "未在游戏中";
        var resp = await _net.RequestUseItemAsync(itemId, 1);
        if (resp.Success && World.Self != null)
        {
            World.Self.Hp = resp.Hp;
            if (resp.MaxHp > 0)
                World.Self.MaxHp = resp.MaxHp;
            SyncInventory();
        }
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "无法使用" : resp.Message);
    }

    public async Task<bool> SendChatAsync(ChatChannel channel, string content, string? targetName = null)
    {
        if (Fsm.Phase != SessionPhase.InWorld)
            return false;
        string raw = content.Trim();
        string masked = Legend.Contracts.PhasePRules.MaskProfanity(raw);
        await _net.SendChatAsync(channel, masked, targetName);
        return !string.Equals(raw, masked, StringComparison.Ordinal);
    }

    public async Task<string?> ShopBuyAsync(string itemId, int count = 1)
    {
        if (LastTalk == null)
            return "先和商人说话";
        var resp = await _net.ShopBuyAsync(_npcEntityId, itemId, count);
        if (resp.Success)
        {
            SyncInventory();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "购买失败" : resp.Message;
    }

    public string NpcEntityId() => _npcEntityId;

    private string _npcEntityId = "";

    public async Task<string?> ShopSellAsync(string itemId, int count = 1)
    {
        if (string.IsNullOrEmpty(_npcEntityId))
            return "先和商人说话";
        var resp = await _net.ShopSellAsync(_npcEntityId, itemId, count);
        if (resp.Success)
        {
            SyncInventory();
            Chat.Add($"[商店] 卖出 {itemId}，获得 {resp.GoldGained} 金");
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "出售失败" : resp.Message;
    }

    public async Task<string?> RepairAsync()
    {
        if (string.IsNullOrEmpty(_npcEntityId))
            return "先和修理的人说话";
        var resp = await _net.RepairAsync(_npcEntityId, "");
        if (resp.Success)
        {
            SyncInventory();
            Atk = _net.EffectiveAtk;
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "修理失败" : resp.Message;
    }

    public async Task<string?> OpenStorageAsync()
    {
        if (string.IsNullOrEmpty(_npcEntityId))
            return "先和仓库管理员说话";
        var resp = await _net.StorageListAsync(_npcEntityId);
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "打不开仓库" : resp.Message);
    }

    public async Task<string?> StorageDepositAsync(string itemId)
    {
        var resp = await _net.StorageDepositAsync(_npcEntityId, itemId, 1);
        if (resp.Success)
        {
            SyncInventory();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "存入失败" : resp.Message;
    }

    public async Task<string?> StorageWithdrawAsync(string itemId)
    {
        var resp = await _net.StorageWithdrawAsync(_npcEntityId, itemId, 1);
        if (resp.Success)
        {
            SyncInventory();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "取出失败" : resp.Message;
    }

    public async Task<string?> AcceptQuestAsync(string questId)
    {
        var resp = await _net.QuestAcceptAsync(_npcEntityId, questId);
            if (resp.Success)
            {
                MetaChanged?.Invoke();
                return null;
            }
            return string.IsNullOrWhiteSpace(resp.Message) ? "接取失败" : resp.Message;
        }

    public async Task<string?> TurnInQuestAsync(string questId)
    {
        var resp = await _net.QuestTurnInAsync(_npcEntityId, questId);
        if (resp.Success)
        {
            SyncInventory();
            MetaChanged?.Invoke();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "交付失败" : resp.Message;
    }

    public async Task<string?> EquipAsync(string itemId, string slot)
    {
        var resp = await _net.EquipAsync(itemId, slot);
        if (resp.Success)
        {
            SyncInventory();
            Atk = resp.EffectiveAtk;
            Def = resp.EffectiveDef;
            if (World.Self != null && resp.EffectiveMaxHp > 0)
                World.Self.MaxHp = resp.EffectiveMaxHp;
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "装备失败" : resp.Message;
    }

    public async Task<string?> LearnAsync(string itemId, string skillId)
    {
        var resp = await _net.LearnSkillAsync(itemId, skillId);
        if (resp.Success)
        {
            SyncInventory();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "学习失败" : resp.Message;
    }

    public async Task<string?> InvitePartyAsync(string name)
    {
        var resp = await _net.PartyInviteAsync(name);
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "邀请失败" : resp.Message);
    }

    public async Task<string?> AcceptPartyAsync(bool accept)
    {
        if (string.IsNullOrEmpty(PendingPartyId))
            return "没有邀请";
        var resp = await _net.PartyAcceptAsync(PendingPartyId, accept);
        if (resp.Success && !accept)
        {
            PendingPartyId = "";
            PendingInviter = "";
        }
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "组队失败" : resp.Message);
    }

    public async Task<string?> LeavePartyAsync()
    {
        var resp = await _net.PartyLeaveAsync();
        if (resp.Success)
        {
            PartyId = "";
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "离队失败" : resp.Message;
    }

    public async Task<string?> CreateGuildAsync(string name)
    {
        var resp = await _net.GuildCreateAsync(name);
        if (resp.Success)
        {
            SyncInventory();
            GuildName = resp.GuildName;
            GuildRank = resp.GuildRank;
            await RefreshGuildAsync();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "创建行会失败" : resp.Message;
    }

    public async Task<string?> JoinGuildAsync(string name)
    {
        var resp = await _net.GuildJoinAsync(name);
        if (resp.Success)
        {
            GuildName = resp.GuildName;
            GuildRank = resp.GuildRank;
            await RefreshGuildAsync();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "加入失败" : resp.Message;
    }

    public async Task<string?> LeaveGuildAsync()
    {
        var resp = await _net.GuildLeaveAsync();
        if (resp.Success)
        {
            GuildName = "";
            GuildRank = "";
            GuildInfo = null;
            SocialRevision++;
            if (World.Self != null)
                World.Self.Title = Legend.Contracts.PhasePRules.Title(Level, GuildRank);
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "退出失败" : resp.Message;
    }

    public async Task<GuildInfoResp?> RefreshGuildAsync()
    {
        var resp = await _net.GuildInfoAsync();
        if (resp.Success)
        {
            GuildName = resp.GuildName;
            GuildInfo = resp;
            var me = resp.Members.FirstOrDefault(m => string.Equals(m.CharacterId, CharacterId, StringComparison.Ordinal));
            if (me != null && !string.IsNullOrWhiteSpace(me.Rank))
                GuildRank = me.Rank;
            if (World.Self != null)
                World.Self.Title = Legend.Contracts.PhasePRules.Title(Level, GuildRank);
        }
        SocialRevision++;
        return resp;
    }

    public void ReturnToLogin()
    {
        _suppressDisconnect = true;
        try { _net.Dispose(); }
        catch { /* already closed */ }
        _suppressDisconnect = false;
        LastTalk = null;
        _npcEntityId = "";
        Fsm.Reset();
    }

    public async Task ReconnectAsync()
    {
        Fsm.Reset();
        if (string.IsNullOrWhiteSpace(LastUsername))
        {
            Fsm.Fail("没有保存的账号");
            return;
        }
        await LoginAsync(LastUsername, LastPassword);
    }

    public void Dispose()
    {
        _suppressDisconnect = true;
        _net.Dispose();
    }

    private void ApplyEnterVitals(CharacterSummary character)
    {
        Level = _net.Level > 0 ? _net.Level : Math.Max(1, character.Level);
        Exp = _net.Exp;
        ExpToNext = _net.ExpToNext;
        Atk = _net.EffectiveAtk;
        Def = _net.EffectiveDef;
        MaxMp = _net.EffectiveMaxMp > 0 ? _net.EffectiveMaxMp : Math.Max(1, character.MaxMp);
        int fromChar = character.Mp > 0 ? character.Mp : character.MaxMp;
        Mp = fromChar > 0 ? Math.Min(fromChar, MaxMp) : MaxMp;
        if (World.Self != null && _net.EffectiveMaxHp > 0)
            World.Self.MaxHp = Math.Max(World.Self.MaxHp, _net.EffectiveMaxHp);
    }

    public IReadOnlyList<QuestProgress> Quests => _net.Quests;
    public IReadOnlyList<SkillState> Skills => _net.Skills;
    public IReadOnlyList<EquippedSlot> Equipment => _net.Equipment;
    public IReadOnlyList<InventoryItem> Storage => _net.Storage;
    public IReadOnlyList<PartyMemberInfo> Party => _net.PartyMembers;

    private void CopyMeta()
    {
        if (!string.IsNullOrEmpty(_net.GuildName))
            GuildName = _net.GuildName;
        GuildRank = string.IsNullOrEmpty(_net.GuildRank) ? GuildRank : _net.GuildRank;
        PartyId = _net.PartyId;
    }

    private void SyncInventory()
    {
        Inventory.Replace(_net.Inventory.Select(i => (i.ItemId, i.Count)), _net.Gold);
    }

    private void ReplaceNet()
    {
        _suppressDisconnect = true;
        try { _net.Dispose(); }
        finally { _suppressDisconnect = false; }
        _net = new PlayableNetClient();
        Wire(_net);
    }

    private void Wire(PlayableNetClient net)
    {
        net.OnSelfEnteredWorld += self =>
        {
            World.ClearActors(keepSelf: false);
            World.SetMap(self.MapId);
            World.Upsert(self, isSelf: true);
            EnteredWorld?.Invoke();
        };
        net.OnEntityEnter += snap => World.Upsert(snap, isSelf: false);
        net.OnEntitySpawn += snap => World.Upsert(snap, isSelf: false);
        net.OnEntityLeave += World.Remove;
        net.OnEntityDespawn += World.Remove;
        net.OnMoveSync += World.ApplyMove;
        net.OnMapChange += change =>
        {
            if (net.SelfEntity != null && change.EntityId == net.SelfEntity.EntityId)
            {
                World.SetMap(change.ToMapId);
                World.ClearActors(keepSelf: true);
                if (World.Self != null)
                {
                    World.Self.GridX = change.ToX;
                    World.Self.GridY = change.ToY;
                    World.Self.DrawX = change.ToX;
                    World.Self.DrawY = change.ToY;
                    World.Self.InSafeZone = change.InSafeZone;
                }
                foreach (var other in change.NearbyEntities)
                    World.Upsert(other, isSelf: false);
                World.AddCast(change.ToX, change.ToY, "mage", "portal");
                Chat.Add($"[系统] 传送到 {World.Map.MapName} ({change.ToX},{change.ToY})");
            }
            else if (World.Get(change.EntityId) is { } actor)
            {
                actor.GridX = change.ToX;
                actor.GridY = change.ToY;
                actor.DrawX = change.ToX;
                actor.DrawY = change.ToY;
            }
        };
        net.OnCombatSync += cs =>
        {
            World.ApplyCombat(cs);
            if (World.Self != null && cs.TargetEntityId == World.Self.EntityId)
            {
                World.Self.Hp = cs.TargetHp;
                if (cs.TargetMaxHp > 0)
                    World.Self.MaxHp = cs.TargetMaxHp;
            }
            CombatSeen?.Invoke(cs);
            if (cs.TargetDied)
            {
                var name = World.Get(cs.TargetEntityId)?.Name ?? cs.TargetEntityId;
                if (World.Self != null && cs.TargetEntityId == World.Self.EntityId)
                    Chat.Add("[系统] 你已死亡，服务器会把你复活到出生点");
                else
                    Chat.Add($"[战斗] 击杀 {name}");
            }
        };
        net.OnLootDrop += loot =>
        {
            if (loot.Loot != null)
            {
                World.Upsert(loot.Loot, isSelf: false);
                Chat.Add($"[掉落] {loot.Loot.Name} ({loot.Loot.X},{loot.Loot.Y})");
            }
        };
        net.OnInventory += inv =>
        {
            Inventory.Replace(inv.Items.Select(i => (i.ItemId, i.Count)), inv.Gold);
        };
        net.OnChat += chat =>
        {
            string channel = chat.Channel switch
            {
                ChatChannel.ChatWorld => "世界",
                ChatChannel.ChatNearby => "附近",
                ChatChannel.ChatGuild => "行会",
                ChatChannel.ChatWhisper => "私聊",
                _ => chat.Channel.ToString()
            };
            Chat.Add($"[{channel}] {chat.SenderName}: {chat.Content}");
        };
        net.OnNotice += notice => Chat.Add($"[{notice.Title}] {notice.Content}");
        net.OnExpGain += exp =>
        {
            Level = exp.Level;
            Exp = exp.TotalExp;
            ExpToNext = exp.ExpToNext;
            Chat.Add($"[经验] +{exp.GainedExp}");
        };
        net.OnMeta += () =>
        {
            CopyMeta();
            MetaChanged?.Invoke();
        };
        net.OnPk += pk =>
        {
            World.SetNameColor(pk.EntityId, pk.NameColor);
            if (World.Self != null && pk.EntityId == World.Self.EntityId)
            {
                AttackModeWire = pk.AttackMode;
                PkKills = pk.PkKills;
                NameColor = pk.NameColor;
            }
            MetaChanged?.Invoke();
        };
        net.OnTradeInvite += invite =>
        {
            TradeId = invite.TradeId ?? "";
            TradePeer = invite.FromName ?? "";
            Chat.Add($"[交易] {TradePeer} 请求交易");
            TradeChanged?.Invoke();
        };
        net.OnTradeState += state =>
        {
            TradeId = state.TradeId ?? TradeId;
            TradeState = state;
            TradeChanged?.Invoke();
        };
        net.OnTradeResult += result =>
        {
            if (result.Success)
                SyncInventory();
            Chat.Add("[交易] " + result.Message);
            if (!result.Success || result.Message.Contains("完成"))
            {
                TradeId = "";
                TradeState = null;
            }
            TradeChanged?.Invoke();
        };
        net.OnGround += ground =>
        {
            World.AddGround(ground.MapId, ground.X, ground.Y, ground.Radius, ground.DurationMs / 1000.0);
        };
        net.OnBossHp += ApplyBossHp;
        net.OnBossSpawn += ApplyBossSpawn;
        net.OnAppearance += ApplyLook;
        net.OnSiege += siege =>
        {
            SiegeChanged?.Invoke(siege.IsActive ? "攻城开始" : "攻城状态 " + siege.Reason);
        };
        net.OnPartyInvite += invite =>
        {
            PendingPartyId = invite.PartyId ?? "";
            PendingInviter = invite.InviterName ?? "";
            Chat.Add($"[组队] {PendingInviter} 邀请你加入队伍");
            MetaChanged?.Invoke();
        };
        net.OnLevelUp += lu =>
        {
            Level = lu.NewLevel;
            Exp = lu.Exp;
            ExpToNext = lu.ExpToNext;
            Atk = lu.EffectiveAtk;
            Def = lu.EffectiveDef;
            if (lu.BaseMaxMp > 0)
                MaxMp = lu.BaseMaxMp;
            Mp = lu.Mp;
            if (World.Self != null)
            {
                World.Self.Hp = lu.Hp;
                if (lu.EffectiveMaxHp > 0)
                    World.Self.MaxHp = lu.EffectiveMaxHp;
                World.Self.Title = Legend.Contracts.PhasePRules.Title(Level, GuildRank);
            }
            Chat.Add($"[系统] 升级到 {lu.NewLevel}");
            LeveledUp?.Invoke();
        };
        net.OnDisconnected += () =>
        {
            if (_suppressDisconnect)
                return;
            if (Fsm.Phase is SessionPhase.InWorld or SessionPhase.EnteringWorld or SessionPhase.CharSelect)
            {
                Fsm.MarkDisconnected(expectReconnect: true);
                Disconnected?.Invoke();
            }
        };
    }
}

public static class PlayableTuning
{
    public const string BasicSkillId = "basic_slash";
    public const int AttackRange = 2;
    public const int PickupRange = 2;
    public const int TalkRange = 3;
    public const double MoveCooldownSec = 0.16;
    public const double AttackCooldownSec = 0.45;
    public const double CellPixels = 36;
}
