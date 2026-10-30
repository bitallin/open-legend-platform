using Legend.ClientCore;
using Legend.Contracts.Proto;
using Legend.Playable.Net;
using Legend.Playable.World;

namespace Legend.Playable.Session;

public sealed class PlayableSession : IDisposable
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
    public IReadOnlyList<CharacterSummary> Characters => _characters;
    public PlayableNetClient Net => _net;

    public event Action? EnteredWorld;
    public event Action<CombatSync>? CombatSeen;
    public event Action? PickedUp;
    public event Action? Disconnected;

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

    public async Task CreateCharacterAsync(string name, string classId)
    {
        if (Fsm.Phase != SessionPhase.CharSelect)
            return;
        try
        {
            await _net.CreateCharAsync(name.Trim(), classId);
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

    public async Task<AttackResp?> AttackAsync(string targetId)
    {
        if (Fsm.Phase != SessionPhase.InWorld)
            return null;
        var resp = await _net.RequestAttackAsync(targetId, PlayableTuning.BasicSkillId);
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
        if (resp.Success)
        {
            string who = string.IsNullOrWhiteSpace(resp.NpcName) ? "NPC" : resp.NpcName;
            if (!string.IsNullOrWhiteSpace(resp.Dialogue))
                Chat.Add($"[{who}] {resp.Dialogue}");
            if (resp.ShopOffers.Count > 0)
            {
                var offers = string.Join("，", resp.ShopOffers.Select(o => $"{o.ItemId} {o.PriceGold}金"));
                Chat.Add($"[商店] {offers}（购买界面为 P1）");
            }
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

    public async Task SendChatAsync(ChatChannel channel, string content)
    {
        if (Fsm.Phase != SessionPhase.InWorld)
            return;
        await _net.SendChatAsync(channel, content.Trim());
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
            }
            Chat.Add($"[系统] 升级到 {lu.NewLevel}");
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
