using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Legend.ClientCore;
using Legend.Contracts.Proto;
using Legend.Playable.Session;
using Legend.Playable.Data;
using Legend.Playable.Input;
using Legend.Playable.World;

namespace Legend.Playable.Ui;

public sealed partial class GameWindow
{
    private readonly Button[] _skillButtons = new Button[8];
    private readonly Button[] _potionButtons = new Button[6];
    private readonly TextBlock _modeLabel = new() { Foreground = Brushes.White, FontSize = 13 };
    private readonly TextBlock _expLabel = new() { Foreground = Brushes.White, FontSize = 12 };
    private readonly TextBox _whisperName = new() { Watermark = "私聊对象", Width = 100 };
    private Border _panelHost = null!;
    private readonly StackPanel _panelBody = new() { Spacing = 4 };
    private readonly TextBlock _panelTitle = new() { FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") };
    private IReadOnlyList<string> _skillSlots = ContentCatalog.DefaultBar("warrior");
    private string[] _potionSlots = (string[])ContentCatalog.DefaultPotions.Clone();
    private AttackMode _attackMode = AttackMode.Peace;
    private ChatChannel _chatChannel = ChatChannel.ChatNearby;
    private bool _mouseRun;
    private string _openPanel = "";
    private string _panelFp = "";

    private void AttachPhaseL(Grid hud)
    {
        var skills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (int i = 0; i < 8; i++)
        {
            int slot = i;
            var button = new Button
            {
                Width = 72,
                Height = 40,
                FontSize = 11,
                Background = MirChrome.Slot ?? Brush("#1A120C"),
                Foreground = Brush("#F6E6C0"),
                BorderThickness = new Avalonia.Thickness(0)
            };
            button.Click += (_, _) => CastSlot(slot);
            _skillButtons[i] = button;
            skills.Children.Add(button);
        }
        var potions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (int i = 0; i < 6; i++)
        {
            int slot = i;
            var button = new Button
            {
                Width = 64,
                Height = 28,
                FontSize = 11,
                Background = MirChrome.Slot ?? Brush("#1A120C"),
                Foreground = Brush("#F6E6C0"),
                BorderThickness = new Avalonia.Thickness(0)
            };
            button.Click += async (_, _) => await DrinkSlot(slot);
            _potionButtons[i] = button;
            potions.Children.Add(button);
        }
        var systems = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        systems.Children.Add(Sys("背包", () => { _bagOpen = !_bagOpen; _audio.PlayOpen(); }));
        systems.Children.Add(Sys("人物", () => ShowPanel("char")));
        systems.Children.Add(Sys("技能", () => ShowPanel("skills")));
        systems.Children.Add(Sys("任务", () => ShowPanel("quest")));
        systems.Children.Add(Sys("组队", () => ShowPanel("party")));
        systems.Children.Add(Sys("行会", () => ShowPanel("guild")));
        systems.Children.Add(Sys("好友", () => ShowPanel("friends")));
        systems.Children.Add(Sys("摆摊", () => ShowPanel("stall")));
        systems.Children.Add(Sys("外观", () => ShowPanel("looks")));
        systems.Children.Add(Sys("地图", () => ShowPanel("map")));
        systems.Children.Add(Sys("世界", () => ShowPanel("world")));
        systems.Children.Add(Sys("交易", () => ShowPanel("trade")));
        systems.Children.Add(Sys("攻城", () => { ShowPanel("siege"); _ = Safe(async () => { await _session.SiegeStatusAsync(); }); }));
        systems.Children.Add(Sys("设置", () => ShowPanel("settings")));
        var dock = new StackPanel { Spacing = 4 };
        dock.Children.Add(_modeLabel);
        dock.Children.Add(skills);
        dock.Children.Add(potions);
        dock.Children.Add(systems);
        dock.Children.Add(_expLabel);
        var bar = new Border
        {
            Background = MirChrome.BottomBar ?? Brush("#F016100C"),
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(8),
            Margin = new Avalonia.Thickness(0, 0, 12, 12),
            Child = dock,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        hud.Children.Add(bar);

        var channels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        channels.Children.Add(Sys("喊话", () => _chatChannel = ChatChannel.ChatWorld));
        channels.Children.Add(Sys("私聊", () => _chatChannel = ChatChannel.ChatWhisper));
        channels.Children.Add(Sys("组队", () => _chatChannel = ChatChannel.ChatParty));
        channels.Children.Add(Sys("行会", () => _chatChannel = ChatChannel.ChatGuild));
        channels.Children.Add(_whisperName);
        if (_hud.Children.Count > 0 && _hud.Children[^1] != bar)
        {
            /* chat row stays in BuildHud */
        }
        // Insert channel extras into the existing chat stack if we can find it.
        _panelBody.Children.Add(_panelTitle);
        _panelHost = new Border
        {
            Background = MirChrome.BagFrame ?? Brush("#EE1C1612"),
            BorderBrush = Brush("#E8C872"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(10),
            Child = new ScrollViewer { Content = _panelBody, MaxHeight = 460, Width = 320 },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 72, 16, 0),
            IsVisible = false
        };
        hud.Children.Add(_panelHost);
        DragDrop.SetAllowDrop(_panelHost, true);
        _panelHost.AddHandler(DragDrop.DropEvent, OnItemDropped);
        hud.Children.Add(channels);
        channels.HorizontalAlignment = HorizontalAlignment.Left;
        channels.VerticalAlignment = VerticalAlignment.Bottom;
        channels.Margin = new Avalonia.Thickness(470, 0, 0, 148);
    }

    private Button Sys(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Avalonia.Thickness(8, 2), FontSize = 12 };
        button.Click += (_, _) =>
        {
            _audio.PlayUiClick();
            action();
        };
        return button;
    }

    private void ShowPanel(string id)
    {
        if (_openPanel == id)
        {
            _openPanel = "";
            _audio.PlayClose();
        }
        else
        {
            _openPanel = id;
            _panelFp = "";
            _audio.PlayOpen();
            if (id == "guild")
                _ = Safe(() => _session.RefreshGuildAsync());
            else if (id == "friends")
                _ = Safe(() => _session.RefreshFriendsAsync());
            else if (id == "stall")
                _ = Safe(() => _session.RefreshMarketAsync());
            else if (id == "looks")
                _ = Safe(() => _session.RefreshLooksAsync());
        }
    }

    private void ClosePhaseLPanels()
    {
        _openPanel = "";
        _mouseRun = false;
    }

    private void RefreshPhaseL()
    {
        EnsureBarForClass();
        _modeLabel.Text = "攻击模式 " + AttackModes.Label(_attackMode) + "  ·  Ctrl+H";
        int expMax = Math.Max(1, _session.ExpToNext);
        _expBar.Maximum = expMax;
        _expBar.Value = Math.Clamp(_session.Exp, 0, expMax);
        int weight = ContentCatalog.BagWeight(_session.Inventory.Items.Select(i => (i.ItemId, i.Count)));
        int carry = 80 + Math.Max(1, _session.Level) * 5;
        string pk = _session.NameColor switch { "red" => "红名", "yellow" => "黄名", _ => "白名" };
        _expLabel.Text = $"EXP {_session.Exp}/{expMax}   负重 {weight}/{carry}   {pk}";
        for (int i = 0; i < 8; i++)
        {
            var def = ContentCatalog.FindSkill(_skillSlots[i]);
            string name = def?.DisplayName ?? _skillSlots[i];
            bool known = !_session.Skills.Any() || _session.Skills.Any(s => s.SkillId == _skillSlots[i]) || def is { RequiresLearn: false };
            _skillButtons[i].Content = $"F{i + 1} {(known ? name : name + "*")}";
            _skillButtons[i].Opacity = known ? 1 : 0.45;
        }
        for (int i = 0; i < 6; i++)
        {
            int count = _session.Inventory.Items.Where(it => it.ItemId == _potionSlots[i]).Sum(it => it.Count);
            _potionButtons[i].Content = $"{i + 1} {Short(ContentCatalog.ItemName(_potionSlots[i]))} {count}";
        }
        _panelHost.IsVisible = _openPanel.Length > 0 && _session.Fsm.Phase == SessionPhase.InWorld;
        if (!_panelHost.IsVisible)
            return;
        var trade = _session.TradeState;
        string fp = _openPanel + "|" + _session.Inventory.Gold + "|" + _session.Quests.Count + "|" + _session.Skills.Count + "|" + _session.Storage.Count + "|" + _session.Party.Count + "|" + _session.PendingPartyId + "|" + _session.TradeId + "|" + (trade?.SelfLocked == true) + (trade?.PeerLocked == true) + trade?.PeerGold + "|" + _session.SiegeNote + "|" + _session.SocialRevision + "|" + _session.Friends.Count + "|" + _session.Market.Count;
        if (fp == _panelFp)
            return;
        _panelFp = fp;
        RebuildPanel();
    }

    private void EnsureBarForClass()
    {
        var bar = ContentCatalog.DefaultBar(_session.ClassId);
        if (!_skillSlots.SequenceEqual(bar) && _session.Fsm.Phase == SessionPhase.InWorld)
        {
            // Keep the player's bar once chosen; first world entry adopts the class bar.
            if (_skillSlots.SequenceEqual(ContentCatalog.DefaultBar("warrior")) && _session.ClassId != "warrior")
                _skillSlots = bar.ToArray();
        }
    }

    private void RebuildPanel()
    {
        while (_panelBody.Children.Count > 1)
            _panelBody.Children.RemoveAt(1);
        switch (_openPanel)
        {
            case "char":
                _panelTitle.Text = "人物 F10";
                _panelBody.Children.Add(Block($"职业 {_session.ClassId}  等级 {_session.Level}"));
                var enhance = new Button { Content = "强化武器", HorizontalAlignment = HorizontalAlignment.Stretch };
                enhance.Click += async (_, _) => Toast(await _session.EnhanceAsync("weapon") ?? "已尝试强化");
                _panelBody.Children.Add(enhance);
                _panelBody.Children.Add(Block(_session.GuildName.Length == 0 ? "行会：无" : $"行会 {_session.GuildName} {_session.GuildRank}"));
                foreach (string slot in new[] { "weapon", "armor", "helmet", "necklace", "bracelet", "bracelet2", "ring", "ring2", "boots", "belt", "torch" })
                {
                    var eq = _session.Equipment.FirstOrDefault(e => string.Equals(e.Slot, slot, StringComparison.OrdinalIgnoreCase));
                    string line = eq == null || string.IsNullOrEmpty(eq.ItemId)
                        ? $"{SlotLabel(slot)}  —"
                        : $"{SlotLabel(slot)}  {(eq.Identified ? ContentCatalog.ItemName(eq.ItemId) : "未鉴定")}  耐久 {eq.Durability}/{Math.Max(eq.MaxDurability, 1)}";
                    _panelBody.Children.Add(Block(line));
                    if (eq != null && !eq.Identified && !string.IsNullOrEmpty(eq.ItemId))
                    {
                        string id = eq.ItemId;
                        var identify = new Button { Content = "鉴定", HorizontalAlignment = HorizontalAlignment.Stretch };
                        identify.Click += async (_, _) => Toast(await _session.IdentifyAsync(id) ?? "已鉴定");
                        _panelBody.Children.Add(identify);
                    }
                }
                foreach (var item in _session.Inventory.Items)
                {
                    var id = item.ItemId;
                    if (!LooksEquipable(id))
                        continue;
                    var button = new Button { Content = $"装备 {ContentCatalog.ItemName(id)}", HorizontalAlignment = HorizontalAlignment.Stretch };
                    button.Click += async (_, _) => Toast(await _session.EquipAsync(id, GuessSlot(id)) ?? "已装备");
                    _panelBody.Children.Add(button);
                }
                break;
            case "skills":
                _panelTitle.Text = "技能 F11";
                foreach (var def in ContentCatalog.Skills.Where(s => s.Supports(_session.ClassId)))
                {
                    var known = _session.Skills.FirstOrDefault(s => s.SkillId == def.SkillId);
                    string line = known == null
                        ? $"{def.DisplayName}  {(def.RequiresLearn ? "未学" : "可用")}  MP{def.MpCost}"
                        : $"{def.DisplayName}  Lv{known.Level}  MP{def.MpCost}";
                    _panelBody.Children.Add(Block(line));
                    if (def.RequiresLearn && known == null)
                    {
                        string book = "item_skillbook_" + def.SkillId.Replace("summon_bone", "summon_bone");
                        var button = new Button { Content = "使用背包里的技能书", HorizontalAlignment = HorizontalAlignment.Stretch };
                        string skillId = def.SkillId;
                        button.Click += async (_, _) =>
                        {
                            var bookItem = _session.Inventory.Items.FirstOrDefault(i =>
                                i.ItemId.Contains(skillId, StringComparison.OrdinalIgnoreCase)
                                || (i.ItemId.Contains("skillbook", StringComparison.OrdinalIgnoreCase)
                                    && ContentCatalog.ItemName(i.ItemId).Contains(def.DisplayName)));
                            if (string.IsNullOrEmpty(bookItem.ItemId))
                            {
                                Toast("背包里没有这本书");
                                return;
                            }
                            Toast(await _session.LearnAsync(bookItem.ItemId, skillId) ?? "学会了");
                        };
                        _panelBody.Children.Add(button);
                    }
                }
                break;
            case "shop":
                _panelTitle.Text = "商店";
                var talk = _session.LastTalk;
                if (talk == null || talk.ShopOffers.Count == 0)
                {
                    _panelBody.Children.Add(Block("这个人没有货品"));
                    break;
                }
                foreach (var offer in talk.ShopOffers)
                {
                    string id = offer.ItemId;
                    var button = new Button
                    {
                        Content = $"买 {ContentCatalog.ItemName(id)}  {offer.PriceGold}金",
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    button.Click += async (_, _) => Toast(await _session.ShopBuyAsync(id) ?? "已购买");
                    _panelBody.Children.Add(button);
                }
                foreach (var item in _session.Inventory.Items)
                {
                    string id = item.ItemId;
                    var sell = new Button
                    {
                        Content = $"卖 {ContentCatalog.ItemName(id)} x1  回收约 {Math.Max(1, offerPrice(id))}金",
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    sell.Click += async (_, _) => Toast(await _session.ShopSellAsync(id) ?? "已出售");
                    _panelBody.Children.Add(sell);
                }
                var repair = new Button { Content = "修理全身", HorizontalAlignment = HorizontalAlignment.Stretch };
                repair.Click += async (_, _) => Toast(await _session.RepairAsync() ?? "已修理");
                _panelBody.Children.Add(repair);
                var store = new Button { Content = "打开个人仓库", HorizontalAlignment = HorizontalAlignment.Stretch };
                store.Click += async (_, _) =>
                {
                    Toast(await _session.OpenStorageAsync() ?? "仓库");
                    ShowPanel("storage");
                };
                _panelBody.Children.Add(store);
                break;
            case "storage":
                _panelTitle.Text = "仓库";
                if (_session.Storage.Count == 0)
                    _panelBody.Children.Add(Block("仓库是空的"));
                foreach (var item in _session.Storage)
                {
                    string id = item.ItemId;
                    var button = new Button { Content = $"取出 {ContentCatalog.ItemName(id)} x{item.Count}", HorizontalAlignment = HorizontalAlignment.Stretch };
                    button.Click += async (_, _) => Toast(await _session.StorageWithdrawAsync(id) ?? "已取出");
                    _panelBody.Children.Add(button);
                }
                foreach (var item in _session.Inventory.Items)
                {
                    string id = item.ItemId;
                    var button = new Button { Content = $"存入 {ContentCatalog.ItemName(id)}", HorizontalAlignment = HorizontalAlignment.Stretch };
                    button.Click += async (_, _) => Toast(await _session.StorageDepositAsync(id) ?? "已存入");
                    _panelBody.Children.Add(button);
                }
                break;
            case "quest":
                _panelTitle.Text = "任务";
                if (_session.Quests.Count == 0 && (_session.LastTalk?.Quests.Count ?? 0) == 0)
                    _panelBody.Children.Add(Block("还没有任务。去找镇上的阿福。"));
                foreach (var quest in _session.Quests)
                    _panelBody.Children.Add(Block($"{quest.Title}  {quest.Progress}/{quest.TargetCount}  {quest.Status}"));
                if (_session.LastTalk != null)
                {
                    foreach (var quest in _session.LastTalk.Quests)
                    {
                        string id = quest.QuestId;
                        if (quest.Status == QuestStatus.QuestNone)
                        {
                            var button = new Button { Content = $"接取 {quest.Title}", HorizontalAlignment = HorizontalAlignment.Stretch };
                            button.Click += async (_, _) => Toast(await _session.AcceptQuestAsync(id) ?? "已接取");
                            _panelBody.Children.Add(button);
                        }
                        else if (quest.Status == QuestStatus.QuestReady)
                        {
                            var button = new Button { Content = $"交付 {quest.Title}", HorizontalAlignment = HorizontalAlignment.Stretch };
                            button.Click += async (_, _) => Toast(await _session.TurnInQuestAsync(id) ?? "已交付");
                            _panelBody.Children.Add(button);
                        }
                    }
                }
                break;
            case "party":
                _panelTitle.Text = "组队";
                var nameBox = new TextBox { Watermark = "对方角色名" };
                var invite = new Button { Content = "邀请", HorizontalAlignment = HorizontalAlignment.Stretch };
                invite.Click += async (_, _) => Toast(await _session.InvitePartyAsync(nameBox.Text ?? "") ?? "已邀请");
                _panelBody.Children.Add(nameBox);
                _panelBody.Children.Add(invite);
                if (_session.PendingPartyId.Length > 0)
                {
                    var yes = new Button { Content = $"接受 {_session.PendingInviter}", HorizontalAlignment = HorizontalAlignment.Stretch };
                    yes.Click += async (_, _) => Toast(await _session.AcceptPartyAsync(true) ?? "已入队");
                    var no = new Button { Content = "拒绝", HorizontalAlignment = HorizontalAlignment.Stretch };
                    no.Click += async (_, _) => Toast(await _session.AcceptPartyAsync(false) ?? "已拒绝");
                    _panelBody.Children.Add(yes);
                    _panelBody.Children.Add(no);
                }
                if (_session.Party.Count == 0)
                    _panelBody.Children.Add(Block("当前没有队伍。同图组队击杀会分享经验。"));
                foreach (var member in _session.Party)
                    _panelBody.Children.Add(Block($"{member.Name}  Lv{member.Level}  {(member.IsLeader ? "队长" : "队员")}"));
                var leave = new Button { Content = "离开队伍", HorizontalAlignment = HorizontalAlignment.Stretch };
                leave.Click += async (_, _) => Toast(await _session.LeavePartyAsync() ?? "已离队");
                _panelBody.Children.Add(leave);
                break;
            case "guild":
                _panelTitle.Text = "行会";
                _panelBody.Children.Add(Block(_session.GuildName.Length == 0 ? "尚未加入行会" : $"{_session.GuildName}  {RankLabel(_session.GuildRank)}"));
                _panelBody.Children.Add(Block("经验分享：同图组队击杀有额外经验。拜师后徒弟击杀 +10% 经验，在线师父获得 20%。行会频道可发公告或宣战。"));
                bool leader = string.Equals(_session.GuildRank, "leader", StringComparison.OrdinalIgnoreCase);
                if (_session.GuildInfo != null)
                {
                    foreach (var member in _session.GuildInfo.Members)
                    {
                        _panelBody.Children.Add(Block($"{member.Name}  {RankLabel(member.Rank)}"));
                        if (!leader || string.Equals(member.CharacterId, _session.CharacterId, StringComparison.Ordinal))
                            continue;
                        string id = member.CharacterId;
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                        row.Children.Add(RankButton("副会", id, "vice_leader"));
                        row.Children.Add(RankButton("仓管", id, "warehouse_admin"));
                        row.Children.Add(RankButton("成员", id, "member"));
                        _panelBody.Children.Add(row);
                    }
                }
                var gname = new TextBox { Watermark = "行会名" };
                var create = new Button { Content = "创建", HorizontalAlignment = HorizontalAlignment.Stretch };
                create.Click += async (_, _) => Toast(await _session.CreateGuildAsync(gname.Text ?? "") ?? "已创建");
                var join = new Button { Content = "加入", HorizontalAlignment = HorizontalAlignment.Stretch };
                join.Click += async (_, _) => Toast(await _session.JoinGuildAsync(gname.Text ?? "") ?? "已加入");
                var gleave = new Button { Content = "退出", HorizontalAlignment = HorizontalAlignment.Stretch };
                gleave.Click += async (_, _) => Toast(await _session.LeaveGuildAsync() ?? "已退出");
                _panelBody.Children.Add(gname);
                _panelBody.Children.Add(create);
                _panelBody.Children.Add(join);
                _panelBody.Children.Add(gleave);
                break;
            case "map":
                _panelTitle.Text = "大地图";
                var map = _session.World.Map;
                var self = _session.World.Self;
                _panelBody.Children.Add(Block($"{map.MapName}  {map.Width}×{map.Height}"));
                _panelBody.Children.Add(Block(self == null ? "坐标 —" : $"你在 ({self.GridX},{self.GridY})"));
                foreach (var portal in map.Portals)
                    _panelBody.Children.Add(Block($"传送 {portal.Id} ({portal.X},{portal.Y})"));
                _panelBody.Children.Add(Block("路线：曙光镇外 → 沼窟外廊 → 骨脊小径"));
                break;
            case "talk":
                _panelTitle.Text = _session.LastTalk?.NpcName ?? "对话";
                _panelBody.Children.Add(Block(_session.LastTalk?.Dialogue ?? ""));
                break;
            case "trade":
                _panelTitle.Text = "面对面交易";
                var peer = new TextBox { Watermark = "对方角色名", Text = _session.TradePeer };
                var start = new Button { Content = "发起", HorizontalAlignment = HorizontalAlignment.Stretch };
                start.Click += async (_, _) => Toast(await _session.TradeStartAsync(peer.Text ?? "") ?? "已发起，等待对方");
                _panelBody.Children.Add(peer);
                _panelBody.Children.Add(start);
                if (_session.TradeId.Length > 0)
                {
                    _panelBody.Children.Add(Block("单号 " + _session.TradeId));
                    var yes = new Button { Content = "接受", HorizontalAlignment = HorizontalAlignment.Stretch };
                    yes.Click += async (_, _) => Toast(await _session.TradeRespondAsync(true) ?? "已接受");
                    var no = new Button { Content = "拒绝", HorizontalAlignment = HorizontalAlignment.Stretch };
                    no.Click += async (_, _) => Toast(await _session.TradeRespondAsync(false) ?? "已拒绝");
                    _panelBody.Children.Add(yes);
                    _panelBody.Children.Add(no);
                    var offerGold = new TextBox { Watermark = "金币", Text = "0" };
                    _panelBody.Children.Add(Block("点背包格子放入，再点一次撤下"));
                    foreach (var item in _session.Inventory.Items)
                    {
                        string id = item.ItemId;
                        bool offered = _session.TradeState?.SelfItems.Any(s => s.ItemId == id) == true;
                        var slot = new Button
                        {
                            Content = (offered ? "撤下 " : "放入 ") + ContentCatalog.ItemName(id) + " x" + item.Count,
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        slot.Click += async (_, _) =>
                        {
                            int.TryParse(offerGold.Text, out int gold);
                            int count = offered ? 0 : 1;
                            Toast(await _session.TradePutAsync(id, count, gold) ?? (offered ? "已撤下" : "已放入"));
                        };
                        _panelBody.Children.Add(slot);
                    }
                    var lockBtn = new Button { Content = "锁定成交", HorizontalAlignment = HorizontalAlignment.Stretch };
                    lockBtn.Click += async (_, _) => Toast(await _session.TradeLockAsync(true) ?? "已锁定");
                    var cancel = new Button { Content = "取消", HorizontalAlignment = HorizontalAlignment.Stretch };
                    cancel.Click += async (_, _) => await _session.TradeCancelAsync();
                    _panelBody.Children.Add(offerGold);
                    _panelBody.Children.Add(lockBtn);
                    _panelBody.Children.Add(cancel);
                    if (_session.TradeState != null)
                    {
                        var st = _session.TradeState;
                        string mine = st.SelfItems.Count == 0 ? "空" : string.Join("，", st.SelfItems.Select(s => ContentCatalog.ItemName(s.ItemId) + "x" + s.Count));
                        string peerItems = st.PeerItems.Count == 0 ? "空" : string.Join("，", st.PeerItems.Select(s => ContentCatalog.ItemName(s.ItemId) + "x" + s.Count));
                        _panelBody.Children.Add(Block($"我 {mine} 金{st.SelfGold} {(st.SelfLocked ? "已锁" : "")}"));
                        _panelBody.Children.Add(Block($"对方 {st.PeerName} {peerItems} 金{st.PeerGold} {(st.PeerLocked ? "已锁" : "")}"));
                    }
                }
                break;
            case "siege":
                _panelTitle.Text = "沙巴克";
                _panelBody.Children.Add(Block("沙门关城墙挡住直路，城门格传送进皇宫区。旗帜在幽影林 (20,20)。"));
                _panelBody.Children.Add(Block(string.IsNullOrWhiteSpace(_session.SiegeNote) ? "打开面板时会向服务器要状态。" : _session.SiegeNote));
                var refreshSiege = new Button { Content = "刷新状态", HorizontalAlignment = HorizontalAlignment.Stretch };
                refreshSiege.Click += async (_, _) => Toast(await _session.SiegeStatusAsync() ?? "已刷新");
                var declare = new Button { Content = "行会宣战", HorizontalAlignment = HorizontalAlignment.Stretch };
                declare.Click += async (_, _) => Toast(await _session.SiegeDeclareAsync() ?? "已宣战");
                var capture = new Button { Content = "占领旗帜", HorizontalAlignment = HorizontalAlignment.Stretch };
                capture.Click += async (_, _) => Toast(await _session.SiegeCaptureAsync() ?? "已占领");
                _panelBody.Children.Add(refreshSiege);
                _panelBody.Children.Add(declare);
                _panelBody.Children.Add(capture);
                break;
            case "world":
                _panelTitle.Text = "世界地图";
                _panelBody.Children.Add(Block("安全区内点相邻地图传送。当前：" + _session.World.Map.MapName));
                foreach (var (id, label) in WorldNodes())
                {
                    string mapId = id;
                    var go = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
                    go.Click += async (_, _) => Toast(await _session.TravelAsync(mapId) ?? ("到达 " + label));
                    _panelBody.Children.Add(go);
                }
                break;
            case "settings":
                _panelTitle.Text = "设置";
                _panelBody.Children.Add(SliderRow("主音量", _audio.Master, v => _audio.Master = (float)v));
                _panelBody.Children.Add(SliderRow("音乐", _audio.BgmVolume, v => _audio.BgmVolume = (float)v));
                _panelBody.Children.Add(SliderRow("音效", _audio.SfxVolume, v => _audio.SfxVolume = (float)v));
                _panelBody.Children.Add(SliderRow("界面", _audio.UiVolume, v => _audio.UiVolume = (float)v));
                var mute = new CheckBox { Content = "静音", IsChecked = _audio.Muted, Foreground = Brushes.White };
                mute.IsCheckedChanged += (_, _) => _audio.Muted = mute.IsChecked == true;
                _panelBody.Children.Add(mute);
                _panelBody.Children.Add(Block("WASD 保留。F1–F8 技能，1–6 喝药，Tab 地图，` 切目标，Alt+X 回选角。聊天会按本地脏词表替换。"));
                var logout = new Button { Content = "退出到登录", HorizontalAlignment = HorizontalAlignment.Stretch };
                logout.Click += (_, _) =>
                {
                    _session.ReturnToLogin();
                    _audio.PlayLoginBgm();
                };
                _panelBody.Children.Add(logout);
                break;
            case "friends":
                _panelTitle.Text = "好友";
                var fname = new TextBox { Watermark = "角色名" };
                var add = new Button { Content = "添加", HorizontalAlignment = HorizontalAlignment.Stretch };
                add.Click += async (_, _) => Toast(await _session.AddFriendAsync(fname.Text ?? "") ?? "已添加");
                _panelBody.Children.Add(fname);
                _panelBody.Children.Add(add);
                if (_session.Friends.Count == 0)
                    _panelBody.Children.Add(Block("还没有好友。"));
                foreach (var friend in _session.Friends)
                {
                    string id = friend.CharacterId;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{friend.Name}  {(friend.Online ? "在线" : "离线")}",
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        Width = 180
                    });
                    var remove = new Button { Content = "删除" };
                    remove.Click += async (_, _) => Toast(await _session.RemoveFriendAsync(id) ?? "已删除");
                    row.Children.Add(remove);
                    _panelBody.Children.Add(row);
                }
                break;
            case "stall":
                _panelTitle.Text = "摆摊";
                _panelBody.Children.Add(Block("只能在安全区上架。单价 1–100000。"));
                var priceBox = new TextBox { Watermark = "单价", Text = "10" };
                var countBox = new TextBox { Watermark = "数量", Text = "1" };
                _panelBody.Children.Add(priceBox);
                _panelBody.Children.Add(countBox);
                foreach (var item in _session.Inventory.Items)
                {
                    string id = item.ItemId;
                    var list = new Button
                    {
                        Content = "上架 " + ContentCatalog.ItemName(id) + " x" + item.Count,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    list.Click += async (_, _) =>
                    {
                        int.TryParse(priceBox.Text, out int price);
                        int.TryParse(countBox.Text, out int count);
                        Toast(await _session.StallListAsync(id, Math.Max(1, count), Math.Max(1, price)) ?? "已上架");
                    };
                    _panelBody.Children.Add(list);
                }
                foreach (var listing in _session.Market)
                {
                    bool mine = string.Equals(listing.SellerCharacterId, _session.CharacterId, StringComparison.Ordinal);
                    string line = $"{ContentCatalog.ItemName(listing.ItemId)} x{listing.Count}  {listing.UnitPriceGold}金  {listing.SellerName}";
                    _panelBody.Children.Add(Block(line));
                    string listingId = listing.ListingId;
                    var act = new Button
                    {
                        Content = mine ? "下架" : "购买",
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    act.Click += async (_, _) =>
                    {
                        Toast(mine
                            ? await _session.StallCancelAsync(listingId) ?? "已下架"
                            : await _session.StallBuyAsync(listingId) ?? "已购买");
                    };
                    _panelBody.Children.Add(act);
                }
                break;
            case "looks":
                _panelTitle.Text = "外观";
                _panelBody.Children.Add(Block("商店可买缰绳、法袍礼盒和幼狼契约。解锁后在这里召唤或穿上。"));
                if (_session.Mounts != null)
                {
                    foreach (var mount in _session.Mounts.Mounts)
                        AddLookRow(mount.DisplayName, mount.Owned, mount.Active, mount.MountId, "召唤", "下马",
                            () => _session.SummonMountAsync(mount.MountId),
                            () => _session.DismissMountAsync());
                }
                if (_session.Fashions != null)
                {
                    foreach (var fashion in _session.Fashions.Fashions)
                        AddLookRow(fashion.DisplayName, fashion.Owned, fashion.Equipped, fashion.FashionId, "穿上", "脱下",
                            () => _session.EquipFashionAsync(fashion.FashionId),
                            () => _session.EquipFashionAsync(""));
                }
                if (_session.Pets != null)
                {
                    foreach (var pet in _session.Pets.Pets)
                        AddLookRow(pet.DisplayName + $" 助战{pet.AssistDamagePercent}%", pet.Owned, pet.Active, pet.PetId, "召唤", "收回",
                            () => _session.SummonPetAsync(pet.PetId),
                            () => _session.DismissPetAsync());
                }
                break;
        }
    }

    private async void OnItemDropped(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("legend-item"))
            return;
        string? id = e.Data.Get("legend-item") as string;
        if (string.IsNullOrEmpty(id))
            return;
        if (_openPanel == "char")
            Toast(await _session.EquipAsync(id, GuessSlot(id)) ?? "已装备");
        else if (_openPanel == "trade" && _session.TradeId.Length > 0)
            Toast(await _session.TradePutAsync(id, 1, 0) ?? "已放入交易");
        else if (_openPanel == "guild")
            Toast(await _session.GuildDepositAsync(id) ?? "已存入行会仓库");
        e.Handled = true;
    }

    private void OpenNpcPanel(TalkResp resp)
    {
        if (resp.NpcId.Contains("guild", StringComparison.OrdinalIgnoreCase))
            _openPanel = "guild";
        else if (resp.NpcId.Contains("way", StringComparison.OrdinalIgnoreCase))
            _openPanel = "world";
        else if (resp.ShopOffers.Count > 0)
            _openPanel = "shop";
        else if (resp.Quests.Count > 0)
            _openPanel = "quest";
        else
            _openPanel = "talk";
        _panelFp = "";
    }

    private static int offerPrice(string itemId) => itemId.Contains("potion", StringComparison.OrdinalIgnoreCase) ? 5 : 1;

    private static (string Id, string Label)[] WorldNodes() => new[]
    {
        ("novice_plain", "曙光镇外"),
        ("oath_city", "盟誓城"),
        ("sand_gate", "沙门关"),
        ("dark_forest", "幽影林"),
        ("marsh_den", "沼窟外廊"),
        ("bone_path", "骨脊小径"),
        ("sealed_temple", "封碑神殿")
    };

    private bool TryPhaseLKey(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.H)
        {
            _attackMode = AttackModes.Next(_attackMode);
            string wire = _attackMode switch
            {
                AttackMode.GoodEvil => "good_evil",
                AttackMode.Group => "group",
                AttackMode.Guild => "guild",
                AttackMode.All => "all",
                _ => "peace"
            };
            _ = Safe(async () =>
            {
                string? err = await _session.SetAttackModeAsync(wire);
                Toast(err ?? ("攻击模式 " + AttackModes.Label(_attackMode)));
            });
            e.Handled = true;
            return true;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.X)
        {
            _ = Safe(async () =>
            {
                await _session.LeaveToCharSelectAsync();
                _charCount = -1;
                _bagOpen = false;
                ClosePhaseLPanels();
                _audio.PlayLoginBgm();
                Toast("已返回选角");
            });
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Tab)
        {
            ShowPanel("map");
            e.Handled = true;
            return true;
        }
        if (e.Key is >= Key.F1 and <= Key.F8)
        {
            CastSlot(e.Key - Key.F1);
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.F9)
        {
            _bagOpen = !_bagOpen;
            _audio.PlayOpen();
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.F10)
        {
            ShowPanel("char");
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.F11)
        {
            ShowPanel("skills");
            e.Handled = true;
            return true;
        }
        if (e.Key is >= Key.D1 and <= Key.D6)
        {
            _ = DrinkSlot(e.Key - Key.D1);
            e.Handled = true;
            return true;
        }
        return false;
    }

    private void CastSlot(int slot)
    {
        if (slot < 0 || slot >= _skillSlots.Count)
            return;
        string skillId = _skillSlots[slot];
        var def = ContentCatalog.FindSkill(skillId);
        if (def != null && def.Kind is "heal" or "buff" or "summon" or "teleport")
        {
            _ = Attack(_session.World.Self?.EntityId, skillId);
            return;
        }
        _ = Attack(SelectedMonsterOrNearest(), skillId);
    }

    private async Task DrinkSlot(int slot)
    {
        if (slot < 0 || slot >= _potionSlots.Length)
            return;
        string id = _potionSlots[slot];
        try
        {
            Toast(await _session.UseItemAsync(id) ?? $"使用 {ContentCatalog.ItemName(id)}");
        }
        catch (Exception ex)
        {
            Toast(ex.Message);
        }
    }

    private void BeginAttack(ActorView actor, bool force)
    {
        var self = _session.World.Self;
        if (self == null)
            return;
        bool monster = actor.Kind == EntityKind.EntityMonster;
        if (!monster && !AttackModes.CanAttack(_attackMode, false, false, false, force))
        {
            Toast($"{AttackModes.Label(_attackMode)}模式不能攻击玩家");
            return;
        }
        int dist = GridMath.Chebyshev(self.GridX, self.GridY, actor.GridX, actor.GridY);
        if (dist <= PlayableTuning.AttackRange)
            _ = Attack(actor.EntityId, PlayableTuning.BasicSkillId, force);
        else
            BeginApproach(actor.EntityId, attack: true, dist, PlayableTuning.AttackRange);
    }

    private void Examine(ActorView? actor)
    {
        if (actor == null)
        {
            Toast("没有可查看的目标");
            return;
        }
        string title = string.IsNullOrWhiteSpace(actor.Title) ? "" : actor.Title + " ";
        Toast($"{title}{actor.Name}  {actor.Kind}  HP {actor.Hp}/{actor.MaxHp}");
    }

    private void Dig()
    {
        var self = _session.World.Self;
        if (self == null)
            return;
        var nearest = _session.World.Nearest(EntityKind.EntityLoot, self.GridX, self.GridY, 2, livingOnly: false);
        if (nearest == null)
        {
            _ = Safe(async () =>
            {
                string? err = await _session.MineAsync();
                Toast(err ?? "挖到粗矿");
            });
            return;
        }
        var loot = _session.World.Get(nearest)!;
        BeginApproach(nearest, attack: false, GridMath.Chebyshev(self.GridX, self.GridY, loot.GridX, loot.GridY), PlayableTuning.PickupRange);
    }

    private Button RankButton(string label, string characterId, string rank)
    {
        var button = new Button { Content = label, FontSize = 11 };
        button.Click += async (_, _) => Toast(await _session.SetGuildRankAsync(characterId, rank) ?? "已任命");
        return button;
    }

    private static string RankLabel(string? rank) => (rank ?? "") switch
    {
        "leader" => "会长",
        "vice_leader" => "副会长",
        "warehouse_admin" => "仓管",
        "member" => "成员",
        _ => string.IsNullOrWhiteSpace(rank) ? "成员" : rank
    };

    private void AddLookRow(
        string label, bool owned, bool active, string appearanceId,
        string onText, string offText, Func<Task<string?>> on, Func<Task<string?>> off)
    {
        string state = active ? "使用中" : owned ? "已拥有" : "未拥有";
        _panelBody.Children.Add(Block($"{label}  {state}"));
        if (owned && !active)
        {
            var button = new Button { Content = onText, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += async (_, _) => Toast(await on() ?? "完成");
            _panelBody.Children.Add(button);
        }
        else if (active)
        {
            var button = new Button { Content = offText, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += async (_, _) => Toast(await off() ?? "完成");
            _panelBody.Children.Add(button);
        }
        else
        {
            string itemId = ContentCatalog.UnlockItemFor(appearanceId);
            bool inBag = itemId.Length > 0 && _session.Inventory.Items.Any(i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
            if (!inBag)
                return;
            var unlock = new Button { Content = "解锁 " + ContentCatalog.ItemName(itemId), HorizontalAlignment = HorizontalAlignment.Stretch };
            unlock.Click += async (_, _) =>
            {
                Toast(await _session.UseItemAsync(itemId) ?? "已解锁");
                await _session.RefreshLooksAsync();
            };
            _panelBody.Children.Add(unlock);
        }
    }

    private async Task Safe(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) { Toast(ex.Message); }
    }

    private static bool LooksEquipable(string itemId)
        => itemId.Contains("sword", StringComparison.OrdinalIgnoreCase)
           || itemId.Contains("armor", StringComparison.OrdinalIgnoreCase)
           || itemId.Contains("ring", StringComparison.OrdinalIgnoreCase)
           || itemId.Contains("helmet", StringComparison.OrdinalIgnoreCase)
           || itemId.Contains("necklace", StringComparison.OrdinalIgnoreCase)
           || itemId.Contains("bracelet", StringComparison.OrdinalIgnoreCase)
           || itemId.Contains("boots", StringComparison.OrdinalIgnoreCase)
           || itemId.Contains("belt", StringComparison.OrdinalIgnoreCase)
           || itemId.Contains("torch", StringComparison.OrdinalIgnoreCase)
           || itemId.Contains("blade", StringComparison.OrdinalIgnoreCase);

    private static string GuessSlot(string itemId)
    {
        string id = itemId.ToLowerInvariant();
        if (id.Contains("helmet")) return "helmet";
        if (id.Contains("necklace")) return "necklace";
        if (id.Contains("bracelet2") || id.Contains("iron_bracelet")) return "bracelet2";
        if (id.Contains("bracelet")) return "bracelet";
        if (id.Contains("ring2") || id.Contains("silver_ring")) return "ring2";
        if (id.Contains("ring")) return "ring";
        if (id.Contains("boots")) return "boots";
        if (id.Contains("belt")) return "belt";
        if (id.Contains("torch")) return "torch";
        if (id.Contains("armor")) return "armor";
        return "weapon";
    }

    private static string SlotLabel(string slot) => slot switch
    {
        "weapon" => "武器",
        "armor" => "衣服",
        "helmet" => "头盔",
        "necklace" => "项链",
        "bracelet" => "手镯",
        "bracelet2" => "手镯2",
        "ring" => "戒指",
        "ring2" => "戒指2",
        "boots" => "靴子",
        "belt" => "腰带",
        "torch" => "火把",
        _ => slot
    };

    private static string Short(string text) => text.Length <= 4 ? text : text[..4];

    private static TextBlock Block(string text) => new() { Text = text, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };

    private static StackPanel SliderRow(string title, float value, Action<double> set)
    {
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = value, Width = 180 };
        slider.PropertyChanged += (_, ev) =>
        {
            if (ev.Property == Slider.ValueProperty)
                set(slider.Value);
        };
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 12 });
        row.Children.Add(slider);
        return row;
    }
}
