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
    private string _shopTab = "buy";
    private string _settingsTab = "volume";
    private string _selectedShopItem = "";

    private void AttachPhaseL(Grid hud)
    {
        var skills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (int i = 0; i < 8; i++)
        {
            int slot = i;
            var button = new Button
            {
                Width = 48,
                Height = 40,
                FontSize = 10,
                Background = MirChrome.Slot ?? Brush("#1A120C"),
                Foreground = Brush("#F6E6C0"),
                BorderThickness = new Avalonia.Thickness(0)
            };
            button.Click += (_, _) => CastSlot(slot);
            _skillButtons[i] = button;
            skills.Children.Add(button);
        }
        var potions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (int i = 0; i < 6; i++)
        {
            int slot = i;
            var button = new Button
            {
                Width = 42,
                Height = 28,
                FontSize = 10,
                Background = MirChrome.Slot ?? Brush("#1A120C"),
                Foreground = Brush("#F6E6C0"),
                BorderThickness = new Avalonia.Thickness(0)
            };
            button.Click += async (_, _) => await DrinkSlot(slot);
            _potionButtons[i] = button;
            potions.Children.Add(button);
        }
        var systems = new WrapPanel { MaxWidth = 200 };
        void AddSys(string text, Action action) => systems.Children.Add(Sys(text, action));
        AddSys("背包", () => { _bagOpen = !_bagOpen; _audio.PlayOpen(); });
        AddSys("人物", () => ShowPanel("char"));
        AddSys("技能", () => ShowPanel("skills"));
        AddSys("任务", () => ShowPanel("quest"));
        AddSys("组队", () => ShowPanel("party"));
        AddSys("行会", () => ShowPanel("guild"));
        AddSys("好友", () => ShowPanel("friends"));
        AddSys("摆摊", () => ShowPanel("stall"));
        AddSys("外观", () => ShowPanel("looks"));
        AddSys("地图", () => ShowPanel("map"));
        AddSys("世界", () => ShowPanel("world"));
        AddSys("交易", () => ShowPanel("trade"));
        AddSys("攻城", () => { ShowPanel("siege"); _ = Safe(async () => { await _session.SiegeStatusAsync(); }); });
        AddSys("设置", () => ShowPanel("settings"));

        var dock = new StackPanel { Spacing = 4, Width = 210 };
        dock.Children.Add(_modeLabel);
        dock.Children.Add(skills);
        dock.Children.Add(potions);
        dock.Children.Add(systems);
        dock.Children.Add(_expLabel);

        var br = new Grid { Width = 210, Height = 250 };
        if (MirChrome.BrCluster != null)
            br.Children.Add(new Image { Source = MirChrome.BrCluster, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
        br.Children.Add(new Border
        {
            Child = dock,
            Padding = new Avalonia.Thickness(8, 8, 8, 8),
            VerticalAlignment = VerticalAlignment.Bottom
        });
        var bar = new Border
        {
            Child = br,
            Margin = new Avalonia.Thickness(0, 0, 6, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        hud.Children.Add(bar);

        MirChrome.StyleField(_whisperName);
        var channels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        channels.Children.Add(Sys("喊话", () => _chatChannel = ChatChannel.ChatWorld));
        channels.Children.Add(Sys("私聊", () => _chatChannel = ChatChannel.ChatWhisper));
        channels.Children.Add(Sys("组队", () => _chatChannel = ChatChannel.ChatParty));
        channels.Children.Add(Sys("行会", () => _chatChannel = ChatChannel.ChatGuild));
        channels.Children.Add(_whisperName);

        _panelBody.Children.Add(_panelTitle);
        var panelScroll = new ScrollViewer { Content = _panelBody, MaxHeight = 500, Width = 460 };
        _panelHost = MirChrome.Framed(panelScroll, 480, null, new Avalonia.Thickness(10, 12, 10, 10));
        _panelHost.HorizontalAlignment = HorizontalAlignment.Center;
        _panelHost.VerticalAlignment = VerticalAlignment.Center;
        _panelHost.Margin = new Avalonia.Thickness(0, 36, 0, 80);
        _panelHost.IsVisible = false;
        hud.Children.Add(_panelHost);
        DragDrop.SetAllowDrop(_panelHost, true);
        _panelHost.AddHandler(DragDrop.DropEvent, OnItemDropped);
        hud.Children.Add(channels);
        channels.HorizontalAlignment = HorizontalAlignment.Left;
        channels.VerticalAlignment = VerticalAlignment.Bottom;
        channels.Margin = new Avalonia.Thickness(190, 0, 0, 150);
    }

    private Button Sys(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Avalonia.Thickness(8, 2), FontSize = 12 };
        MirChrome.StyleSys(button);
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
            if (id == "char")
                _bagOpen = true;
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
        string fp = _openPanel + "|" + _shopTab + "|" + _settingsTab + "|" + _selectedShopItem + "|" + _session.Inventory.Gold + "|" + _session.Quests.Count + "|" + _session.Skills.Count + "|" + _session.Storage.Count + "|" + _session.Party.Count + "|" + _session.PendingPartyId + "|" + _session.TradeId + "|" + (trade?.SelfLocked == true) + (trade?.PeerLocked == true) + trade?.PeerGold + "|" + _session.SiegeNote + "|" + _session.SocialRevision + "|" + _session.Friends.Count + "|" + _session.Market.Count;
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
                _panelBody.Children.Add(Block(_session.GuildName.Length == 0 ? "行会：无" : $"行会 {_session.GuildName} {_session.GuildRank}"));
                // Paperdoll + surround equipment slots (OL-03), not a green text dump.
                var doll = new ClassPortrait
                {
                    Width = 96,
                    Height = 120,
                    ClassId = string.IsNullOrEmpty(_session.ClassId) ? "warrior" : _session.ClassId,
                    Gender = "m"
                };
                var leftSlots = new StackPanel { Spacing = 4, Width = 88 };
                var rightSlots = new StackPanel { Spacing = 4, Width = 88 };
                string[] leftOrder = { "helmet", "necklace", "armor", "belt", "boots" };
                string[] rightOrder = { "weapon", "bracelet", "bracelet2", "ring", "ring2", "torch" };
                void AddEquipSlot(StackPanel host, string slot)
                {
                    var eq = _session.Equipment.FirstOrDefault(e => string.Equals(e.Slot, slot, StringComparison.OrdinalIgnoreCase));
                    string label = eq == null || string.IsNullOrEmpty(eq.ItemId)
                        ? SlotLabel(slot)
                        : (eq.Identified ? ContentCatalog.ItemName(eq.ItemId) : "未鉴定");
                    var cell = new Border
                    {
                        Width = 84,
                        Height = 40,
                        Background = MirChrome.Slot ?? Brush("#22180F"),
                        BorderBrush = Brush("#8A6A38"),
                        BorderThickness = new Avalonia.Thickness(1),
                        Child = new TextBlock
                        {
                            Text = label,
                            FontSize = 11,
                            Foreground = Brush("#F0D8A8"),
                            TextWrapping = TextWrapping.NoWrap,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Avalonia.Thickness(4, 0)
                        }
                    };
                    if (eq != null && !eq.Identified && !string.IsNullOrEmpty(eq.ItemId))
                    {
                        string id = eq.ItemId;
                        cell.PointerPressed += async (_, _) => Toast(await _session.IdentifyAsync(id) ?? "已鉴定");
                    }
                    host.Children.Add(cell);
                }
                foreach (var s in leftOrder) AddEquipSlot(leftSlots, s);
                foreach (var s in rightOrder) AddEquipSlot(rightSlots, s);
                var surround = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
                surround.Children.Add(leftSlots);
                surround.Children.Add(doll);
                surround.Children.Add(rightSlots);
                _panelBody.Children.Add(surround);
                var enhance = new Button { Content = "强化武器", HorizontalAlignment = HorizontalAlignment.Stretch };
                MirChrome.StyleNormal(enhance);
                enhance.Click += async (_, _) => Toast(await _session.EnhanceAsync("weapon") ?? "已尝试强化");
                _panelBody.Children.Add(enhance);
                foreach (var item in _session.Inventory.Items)
                {
                    var id = item.ItemId;
                    if (!LooksEquipable(id))
                        continue;
                    var button = new Button { Content = $"装备 {ContentCatalog.ItemName(id)}", HorizontalAlignment = HorizontalAlignment.Stretch };
                    MirChrome.StyleNormal(button);
                    button.Click += async (_, _) => Toast(await _session.EquipAsync(id, GuessSlot(id)) ?? "已装备");
                    _panelBody.Children.Add(button);
                }
                break;
            case "skills":
            {
                _panelTitle.Text = "技能";
                _panelBody.Children.Add(Block("职业技能 · F1–F8 快捷栏 · 双击行可尝试学习"));
                int iconN = 1;
                foreach (var def in ContentCatalog.Skills.Where(s => s.Supports(_session.ClassId)))
                {
                    var known = _session.Skills.FirstOrDefault(s => s.SkillId == def.SkillId);
                    int barIndex = -1;
                    for (int i = 0; i < _skillSlots.Count; i++)
                        if (_skillSlots[i] == def.SkillId) { barIndex = i; break; }
                    string hotkey = barIndex >= 0 ? $"F{barIndex + 1}" : "—";
                    int level = known?.Level ?? (def.RequiresLearn ? 0 : 1);
                    bool knownOk = known != null || !def.RequiresLearn;
                    int expCur = knownOk ? Math.Min(3000, 400 + level * 500) : 0;
                    int expMax = 3000;
                    var icon = MirChrome.ItemIcon(((iconN++ - 1) % 16) + 1);
                    var left = new Grid { Width = 40, Height = 40 };
                    if (MirChrome.HotbarSlot != null)
                        left.Children.Add(new Image { Source = MirChrome.HotbarSlot, Stretch = Stretch.Fill });
                    if (icon != null)
                        left.Children.Add(new Image { Source = icon, Width = 28, Height = 28, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    var mid = new StackPanel { Spacing = 2, Width = 280 };
                    mid.Children.Add(new TextBlock
                    {
                        Text = $"{def.DisplayName}    {hotkey}",
                        FontSize = 13,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brush("#F6E6C0")
                    });
                    mid.Children.Add(new TextBlock
                    {
                        Text = knownOk ? $"Lv{level}  MP{def.MpCost}  Exp {expCur}/{expMax}" : $"未学  MP{def.MpCost}",
                        FontSize = 11,
                        Foreground = Brush("#C8B48A")
                    });
                    mid.Children.Add(MirChrome.ThinBar("#E0B030", expCur, expMax));
                    var rowInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    rowInner.Children.Add(left);
                    rowInner.Children.Add(mid);
                    bool selected = barIndex == 0;
                    var row = MirChrome.ListRow(rowInner, selected);
                    string skillId = def.SkillId;
                    string display = def.DisplayName;
                    row.PointerPressed += async (_, ev) =>
                    {
                        if (ev.ClickCount < 2) return;
                        if (known != null || !def.RequiresLearn)
                        {
                            Toast($"{display} 已在技能栏");
                            return;
                        }
                        var bookItem = _session.Inventory.Items.FirstOrDefault(i =>
                            i.ItemId.Contains(skillId, StringComparison.OrdinalIgnoreCase)
                            || (i.ItemId.Contains("skillbook", StringComparison.OrdinalIgnoreCase)
                                && ContentCatalog.ItemName(i.ItemId).Contains(display)));
                        if (string.IsNullOrEmpty(bookItem.ItemId))
                        {
                            Toast("背包里没有这本书");
                            return;
                        }
                        Toast(await _session.LearnAsync(bookItem.ItemId, skillId) ?? "学会了");
                    };
                    _panelBody.Children.Add(row);
                }
                var hotbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
                for (int i = 0; i < 8; i++)
                {
                    var def = ContentCatalog.FindSkill(_skillSlots[i]);
                    var cell = MirChrome.SlotCell(new TextBlock
                    {
                        Text = $"F{i + 1}\n{(def?.DisplayName ?? "空")[..Math.Min(2, (def?.DisplayName ?? "空").Length)]}",
                        FontSize = 9,
                        Foreground = Brush("#F0D8A8"),
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, 44);
                    hotbar.Children.Add(cell);
                }
                _panelBody.Children.Add(hotbar);
                break;
            }
            case "shop":
            {
                var talk = _session.LastTalk;
                string npcName = talk?.NpcName ?? "商人";
                string tabLabel = _shopTab switch { "sell" => "卖出", "repair" => "修理", "special" => "特殊", _ => "购买" };
                _panelTitle.Text = $"{npcName} · {tabLabel}";
                var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 6) };
                foreach (var (id, label) in new[] { ("buy", "购买"), ("sell", "卖出"), ("repair", "修理"), ("special", "特殊") })
                {
                    string tabId = id;
                    var tab = MirChrome.MakeTab(label, _shopTab == tabId);
                    tab.Click += (_, _) => { _shopTab = tabId; _panelFp = ""; RefreshPhaseL(); };
                    tabs.Children.Add(tab);
                }
                _panelBody.Children.Add(tabs);

                if (_shopTab == "buy")
                {
                    if (talk == null || talk.ShopOffers.Count == 0)
                    {
                        _panelBody.Children.Add(Block("这个人没有货品"));
                        break;
                    }
                    if (string.IsNullOrEmpty(_selectedShopItem) || talk.ShopOffers.All(o => o.ItemId != _selectedShopItem))
                        _selectedShopItem = talk.ShopOffers[0].ItemId;

                    var dual = new Grid { ColumnDefinitions = new ColumnDefinitions("*,140") };
                    var list = new StackPanel { Spacing = 2 };
                    int iconN = 1;
                    foreach (var offer in talk.ShopOffers)
                    {
                        string id = offer.ItemId;
                        bool sel = id == _selectedShopItem;
                        var icon = MirChrome.ItemIcon(((iconN++ - 1) % 16) + 1);
                        var rowInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                        rowInner.Children.Add(new Image { Source = icon, Width = 28, Height = 28, Stretch = Stretch.Uniform });
                        var texts = new StackPanel { Spacing = 1 };
                        texts.Children.Add(new TextBlock { Text = ContentCatalog.ItemName(id), FontSize = 12, Foreground = Brush("#F6E6C0") });
                        texts.Children.Add(new TextBlock { Text = $"{offer.PriceGold} 金", FontSize = 11, Foreground = Brush("#E8C872") });
                        rowInner.Children.Add(texts);
                        var row = MirChrome.ListRow(rowInner, sel);
                        row.PointerPressed += (_, _) => { _selectedShopItem = id; _panelFp = ""; RefreshPhaseL(); };
                        list.Children.Add(row);
                    }
                    Grid.SetColumn(list, 0);
                    dual.Children.Add(list);

                    var selectedOffer = talk.ShopOffers.First(o => o.ItemId == _selectedShopItem);
                    var detail = new StackPanel { Spacing = 6, Margin = new Avalonia.Thickness(8, 0, 0, 0) };
                    var preview = MirChrome.SlotCell(new Image
                    {
                        Source = MirChrome.ItemIcon(Math.Abs(selectedOffer.ItemId.GetHashCode() % 16) + 1),
                        Width = 36, Height = 36, Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }, 56);
                    detail.Children.Add(preview);
                    detail.Children.Add(new TextBlock { Text = ContentCatalog.ItemName(selectedOffer.ItemId), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush("#F6E6C0"), TextWrapping = TextWrapping.Wrap });
                    detail.Children.Add(Block("库存 充足"));
                    detail.Children.Add(Block("数量 x1"));
                    detail.Children.Add(new TextBlock { Text = $"合计 {selectedOffer.PriceGold} 金", FontSize = 13, Foreground = Brush("#E8C872") });
                    var buy = new Button { Content = "购买", HorizontalAlignment = HorizontalAlignment.Stretch };
                    MirChrome.StylePrimary(buy);
                    string buyId = selectedOffer.ItemId;
                    buy.Click += async (_, _) => Toast(await _session.ShopBuyAsync(buyId) ?? "已购买");
                    detail.Children.Add(buy);
                    var back = new Button { Content = "返回", HorizontalAlignment = HorizontalAlignment.Stretch };
                    MirChrome.StyleNormal(back);
                    back.Click += (_, _) => ShowPanel("");
                    detail.Children.Add(back);
                    Grid.SetColumn(detail, 1);
                    dual.Children.Add(detail);
                    _panelBody.Children.Add(dual);
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(talk.Dialogue) ? "年轻人，货真价实，随便挑。" : talk.Dialogue,
                        FontSize = 11, Foreground = Brush("#C8B48A"), TextWrapping = TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 8, 0, 2)
                    });
                    _panelBody.Children.Add(new TextBlock { Text = $"携带金币  {_session.Inventory.Gold:N0}", FontSize = 12, Foreground = Brush("#E8C872") });
                }
                else if (_shopTab == "sell")
                {
                    if (_session.Inventory.Items.Count == 0)
                        _panelBody.Children.Add(Block("背包是空的"));
                    int iconN = 1;
                    foreach (var item in _session.Inventory.Items)
                    {
                        string id = item.ItemId;
                        int price = Math.Max(1, offerPrice(id));
                        var icon = MirChrome.ItemIcon(((iconN++ - 1) % 16) + 1);
                        var rowInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                        rowInner.Children.Add(new Image { Source = icon, Width = 28, Height = 28, Stretch = Stretch.Uniform });
                        var texts = new StackPanel { Spacing = 1 };
                        texts.Children.Add(new TextBlock { Text = $"{ContentCatalog.ItemName(id)} x{item.Count}", FontSize = 12, Foreground = Brush("#F6E6C0") });
                        texts.Children.Add(new TextBlock { Text = $"回收约 {price} 金", FontSize = 11, Foreground = Brush("#E8C872") });
                        rowInner.Children.Add(texts);
                        var sellBtn = new Button { Content = "卖出", MinWidth = 56 };
                        MirChrome.StyleNormal(sellBtn);
                        sellBtn.Click += async (_, _) => Toast(await _session.ShopSellAsync(id) ?? "已出售");
                        rowInner.Children.Add(sellBtn);
                        _panelBody.Children.Add(MirChrome.ListRow(rowInner));
                    }
                    _panelBody.Children.Add(new TextBlock { Text = $"携带金币  {_session.Inventory.Gold:N0}", FontSize = 12, Foreground = Brush("#E8C872"), Margin = new Avalonia.Thickness(0, 6, 0, 0) });
                }
                else if (_shopTab == "repair")
                {
                    _panelBody.Children.Add(Block("修理全身装备，按耐久损耗收费。"));
                    var repair = new Button { Content = "修理全身", HorizontalAlignment = HorizontalAlignment.Stretch };
                    MirChrome.StylePrimary(repair);
                    repair.Click += async (_, _) => Toast(await _session.RepairAsync() ?? "已修理");
                    _panelBody.Children.Add(repair);
                }
                else
                {
                    _panelBody.Children.Add(Block("特殊服务：个人仓库、行会仓入口。"));
                    var store = new Button { Content = "打开个人仓库", HorizontalAlignment = HorizontalAlignment.Stretch };
                    MirChrome.StylePrimary(store);
                    store.Click += async (_, _) =>
                    {
                        Toast(await _session.OpenStorageAsync() ?? "仓库");
                        ShowPanel("storage");
                    };
                    _panelBody.Children.Add(store);
                }
                break;
            }
            case "storage":
            {
                _panelTitle.Text = "仓库";
                var dual = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                var left = new StackPanel { Spacing = 3, Margin = new Avalonia.Thickness(0, 0, 6, 0) };
                left.Children.Add(new TextBlock { Text = "仓库格", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                if (_session.Storage.Count == 0)
                    left.Children.Add(Block("仓库是空的"));
                int iconN = 1;
                foreach (var item in _session.Storage)
                {
                    string id = item.ItemId;
                    var icon = MirChrome.ItemIcon(((iconN++ - 1) % 16) + 1);
                    var rowInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    rowInner.Children.Add(new Image { Source = icon, Width = 28, Height = 28, Stretch = Stretch.Uniform });
                    rowInner.Children.Add(new TextBlock { Text = $"{ContentCatalog.ItemName(id)} x{item.Count}", FontSize = 12, Foreground = Brush("#F6E6C0"), VerticalAlignment = VerticalAlignment.Center, Width = 120 });
                    var button = new Button { Content = "取出", MinWidth = 48 };
                    MirChrome.StyleNormal(button);
                    button.Click += async (_, _) => Toast(await _session.StorageWithdrawAsync(id) ?? "已取出");
                    rowInner.Children.Add(button);
                    left.Children.Add(MirChrome.ListRow(rowInner));
                }
                var right = new StackPanel { Spacing = 3, Margin = new Avalonia.Thickness(6, 0, 0, 0) };
                right.Children.Add(new TextBlock { Text = "背包", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                if (_session.Inventory.Items.Count == 0)
                    right.Children.Add(Block("背包是空的"));
                foreach (var item in _session.Inventory.Items)
                {
                    string id = item.ItemId;
                    var icon = MirChrome.ItemIcon(((iconN++ - 1) % 16) + 1);
                    var rowInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    rowInner.Children.Add(new Image { Source = icon, Width = 28, Height = 28, Stretch = Stretch.Uniform });
                    rowInner.Children.Add(new TextBlock { Text = ContentCatalog.ItemName(id), FontSize = 12, Foreground = Brush("#F6E6C0"), VerticalAlignment = VerticalAlignment.Center, Width = 120 });
                    var button = new Button { Content = "存入", MinWidth = 48 };
                    MirChrome.StyleNormal(button);
                    button.Click += async (_, _) => Toast(await _session.StorageDepositAsync(id) ?? "已存入");
                    rowInner.Children.Add(button);
                    right.Children.Add(MirChrome.ListRow(rowInner));
                }
                Grid.SetColumn(left, 0);
                Grid.SetColumn(right, 1);
                dual.Children.Add(left);
                dual.Children.Add(right);
                _panelBody.Children.Add(dual);
                _panelBody.Children.Add(new TextBlock { Text = $"携带金币  {_session.Inventory.Gold:N0}", FontSize = 12, Foreground = Brush("#E8C872"), Margin = new Avalonia.Thickness(0, 8, 0, 0) });
                break;
            }
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
            {
                _panelTitle.Text = "行会";
                string gtitle = _session.GuildName.Length == 0 ? "尚未加入行会" : $"{_session.GuildName}  ·  {RankLabel(_session.GuildRank)}";
                _panelBody.Children.Add(new TextBlock { Text = gtitle, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "同图组队击杀有额外经验。拜师徒弟 +10%，在线师父获 20%。行会频道可发公告或宣战。",
                    FontSize = 11, Foreground = Brush("#C8B48A"), TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 2, 0, 6)
                });
                bool leader = string.Equals(_session.GuildRank, "leader", StringComparison.OrdinalIgnoreCase);
                if (_session.GuildInfo != null)
                {
                    _panelBody.Children.Add(new TextBlock { Text = "成员列表", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                    foreach (var member in _session.GuildInfo.Members)
                    {
                        var rowInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                        rowInner.Children.Add(new TextBlock
                        {
                            Text = $"{member.Name}",
                            FontSize = 12, Foreground = Brush("#F6E6C0"), Width = 120,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        rowInner.Children.Add(new TextBlock
                        {
                            Text = RankLabel(member.Rank),
                            FontSize = 11, Foreground = Brush("#E8C872"), Width = 56,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        if (leader && !string.Equals(member.CharacterId, _session.CharacterId, StringComparison.Ordinal))
                        {
                            string id = member.CharacterId;
                            var rb1 = RankButton("副会", id, "vice_leader");
                            var rb2 = RankButton("仓管", id, "warehouse_admin");
                            var rb3 = RankButton("成员", id, "member");
                            MirChrome.StyleSys(rb1); MirChrome.StyleSys(rb2); MirChrome.StyleSys(rb3);
                            rowInner.Children.Add(rb1); rowInner.Children.Add(rb2); rowInner.Children.Add(rb3);
                        }
                        _panelBody.Children.Add(MirChrome.ListRow(rowInner));
                    }
                }
                var gname = new TextBox { Watermark = "行会名", Margin = new Avalonia.Thickness(0, 8, 0, 4) };
                MirChrome.StyleField(gname);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var create = new Button { Content = "创建", MinWidth = 72 };
                MirChrome.StylePrimary(create);
                create.Click += async (_, _) => Toast(await _session.CreateGuildAsync(gname.Text ?? "") ?? "已创建");
                var join = new Button { Content = "加入", MinWidth = 72 };
                MirChrome.StyleNormal(join);
                join.Click += async (_, _) => Toast(await _session.JoinGuildAsync(gname.Text ?? "") ?? "已加入");
                var gleave = new Button { Content = "退出", MinWidth = 72 };
                MirChrome.StyleDanger(gleave);
                gleave.Click += async (_, _) => Toast(await _session.LeaveGuildAsync() ?? "已退出");
                actions.Children.Add(create); actions.Children.Add(join); actions.Children.Add(gleave);
                _panelBody.Children.Add(gname);
                _panelBody.Children.Add(actions);
                break;
            }
            case "map":
            {
                _panelTitle.Text = "大地图";
                var map = _session.World.Map;
                var self = _session.World.Self;
                _panelBody.Children.Add(new TextBlock
                {
                    Text = $"{map.MapName}  ·  {map.Width}×{map.Height}",
                    FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872")
                });
                _panelBody.Children.Add(new TextBlock
                {
                    Text = self == null ? "坐标 —" : $"你在 ({self.GridX},{self.GridY})",
                    FontSize = 12, Foreground = Brush("#F6E6C0"), Margin = new Avalonia.Thickness(0, 2, 0, 6)
                });
                // schematic map board
                var board = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#AA140E0A")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#8A6A38")),
                    BorderThickness = new Avalonia.Thickness(1),
                    Height = 180,
                    Margin = new Avalonia.Thickness(0, 0, 0, 8),
                    Child = new Canvas { ClipToBounds = true }
                };
                var canvas = (Canvas)board.Child!;
                double scaleX = 420.0 / Math.Max(1, map.Width);
                double scaleY = 160.0 / Math.Max(1, map.Height);
                foreach (var portal in map.Portals)
                {
                    var dot = new Border
                    {
                        Width = 8, Height = 8,
                        Background = new SolidColorBrush(Color.Parse("#60A0E8")),
                        CornerRadius = new Avalonia.CornerRadius(4)
                    };
                    Canvas.SetLeft(dot, Math.Clamp(portal.X * scaleX, 4, 410));
                    Canvas.SetTop(dot, Math.Clamp(portal.Y * scaleY, 4, 150));
                    canvas.Children.Add(dot);
                    var lab = new TextBlock { Text = portal.Id, FontSize = 9, Foreground = Brush("#A8C8E8") };
                    Canvas.SetLeft(lab, Math.Clamp(portal.X * scaleX + 10, 4, 380));
                    Canvas.SetTop(lab, Math.Clamp(portal.Y * scaleY - 2, 4, 150));
                    canvas.Children.Add(lab);
                }
                if (self != null)
                {
                    var me = new Border
                    {
                        Width = 10, Height = 10,
                        Background = new SolidColorBrush(Color.Parse("#E8C872")),
                        CornerRadius = new Avalonia.CornerRadius(5),
                        BorderBrush = Brushes.White, BorderThickness = new Avalonia.Thickness(1)
                    };
                    Canvas.SetLeft(me, Math.Clamp(self.GridX * scaleX, 4, 410));
                    Canvas.SetTop(me, Math.Clamp(self.GridY * scaleY, 4, 150));
                    canvas.Children.Add(me);
                }
                _panelBody.Children.Add(board);
                foreach (var portal in map.Portals)
                    _panelBody.Children.Add(MirChrome.ListRow(new TextBlock
                    {
                        Text = $"传送点  {portal.Id}  ({portal.X},{portal.Y})",
                        FontSize = 11, Foreground = Brush("#F6E6C0")
                    }));
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "路线：曙光镇外 → 沼窟外廊 → 骨脊小径",
                    FontSize = 11, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 6, 0, 0)
                });
                break;
            }
            case "talk":
                _panelTitle.Text = _session.LastTalk?.NpcName ?? "对话";
                _panelBody.Children.Add(Block(_session.LastTalk?.Dialogue ?? ""));
                break;
            case "trade":
            {
                _panelTitle.Text = "交易";
                if (_session.TradeId.Length == 0)
                {
                    var peer = new TextBox { Watermark = "对方角色名", Text = _session.TradePeer };
                    MirChrome.StyleField(peer);
                    var startBtn = new Button { Content = "发起交易", HorizontalAlignment = HorizontalAlignment.Stretch };
                    MirChrome.StylePrimary(startBtn);
                    startBtn.Click += async (_, _) => Toast(await _session.TradeStartAsync(peer.Text ?? "") ?? "已发起，等待对方");
                    _panelBody.Children.Add(Block("面对面交易：输入对方角色名发起。"));
                    _panelBody.Children.Add(peer);
                    _panelBody.Children.Add(startBtn);
                    break;
                }

                var st = _session.TradeState;
                string peerName = st?.PeerName ?? _session.TradePeer;
                _panelBody.Children.Add(new TextBlock
                {
                    Text = $"对方：{peerName}    单号 {_session.TradeId}",
                    FontSize = 12, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                });

                var dual = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                // peer side
                var peerCol = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 6, 0) };
                peerCol.Children.Add(new TextBlock { Text = "对方物品", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                var peerGrid = new WrapPanel { Width = 210 };
                var peerItems = st == null ? new System.Collections.Generic.List<TradeStack>() : st.PeerItems.ToList();
                for (int i = 0; i < 20; i++)
                {
                    Control? inner = null;
                    if (i < peerItems.Count)
                    {
                        var it = peerItems[i];
                        inner = new StackPanel
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                new Image { Source = MirChrome.ItemIcon((Math.Abs(it.ItemId.GetHashCode()) % 16) + 1), Width = 24, Height = 24, Stretch = Stretch.Uniform },
                                new TextBlock { Text = "x" + it.Count, FontSize = 9, Foreground = Brush("#F0D8A8"), HorizontalAlignment = HorizontalAlignment.Center }
                            }
                        };
                    }
                    peerGrid.Children.Add(MirChrome.SlotCell(inner, 36));
                }
                peerCol.Children.Add(peerGrid);
                peerCol.Children.Add(new TextBlock
                {
                    Text = st?.PeerLocked == true ? "对方已锁定 · 只读" : "对方未锁定",
                    FontSize = 11, Foreground = Brush(st?.PeerLocked == true ? "#60A0E8" : "#C8B48A")
                });
                peerCol.Children.Add(new TextBlock { Text = $"金币  {st?.PeerGold ?? 0:N0}", FontSize = 12, Foreground = Brush("#E8C872") });
                Grid.SetColumn(peerCol, 0);
                dual.Children.Add(peerCol);

                // self side
                var selfCol = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(6, 0, 0, 0) };
                selfCol.Children.Add(new TextBlock { Text = "自己物品", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                var selfGrid = new WrapPanel { Width = 210 };
                var selfItems = st == null ? new System.Collections.Generic.List<TradeStack>() : st.SelfItems.ToList();
                for (int i = 0; i < 20; i++)
                {
                    Control? inner = null;
                    if (i < selfItems.Count)
                    {
                        var it = selfItems[i];
                        inner = new StackPanel
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                new Image { Source = MirChrome.ItemIcon((Math.Abs(it.ItemId.GetHashCode()) % 16) + 1), Width = 24, Height = 24, Stretch = Stretch.Uniform },
                                new TextBlock { Text = "x" + it.Count, FontSize = 9, Foreground = Brush("#F0D8A8"), HorizontalAlignment = HorizontalAlignment.Center }
                            }
                        };
                    }
                    selfGrid.Children.Add(MirChrome.SlotCell(inner, 36));
                }
                selfCol.Children.Add(selfGrid);
                selfCol.Children.Add(new TextBlock
                {
                    Text = st?.SelfLocked == true ? "已锁定 · 不可改" : "未锁定",
                    FontSize = 11, Foreground = Brush(st?.SelfLocked == true ? "#70D070" : "#C8B48A")
                });
                var offerGold = new TextBox { Watermark = "金币", Text = (st?.SelfGold ?? 0).ToString(), Width = 120 };
                MirChrome.StyleField(offerGold);
                selfCol.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "金币", FontSize = 12, Foreground = Brush("#E8C872"), VerticalAlignment = VerticalAlignment.Center },
                        offerGold
                    }
                });
                Grid.SetColumn(selfCol, 1);
                dual.Children.Add(selfCol);
                _panelBody.Children.Add(dual);

                _panelBody.Children.Add(new TextBlock
                {
                    Text = "点背包格子放入，再点一次撤下",
                    FontSize = 11, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 8, 0, 4)
                });
                var bagRow = new WrapPanel();
                foreach (var item in _session.Inventory.Items)
                {
                    string id = item.ItemId;
                    bool offered = st?.SelfItems.Any(s => s.ItemId == id) == true;
                    var slot = new Button
                    {
                        Content = (offered ? "撤 " : "+ ") + ContentCatalog.ItemName(id),
                        FontSize = 11, Margin = new Avalonia.Thickness(0, 0, 4, 4)
                    };
                    MirChrome.StyleSys(slot);
                    slot.Click += async (_, _) =>
                    {
                        int.TryParse(offerGold.Text, out int gold);
                        int count = offered ? 0 : 1;
                        Toast(await _session.TradePutAsync(id, count, gold) ?? (offered ? "已撤下" : "已放入"));
                    };
                    bagRow.Children.Add(slot);
                }
                _panelBody.Children.Add(bagRow);

                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
                var yes = new Button { Content = "接受", MinWidth = 72 };
                MirChrome.StyleNormal(yes);
                yes.Click += async (_, _) => Toast(await _session.TradeRespondAsync(true) ?? "已接受");
                var lockBtn = new Button { Content = st?.SelfLocked == true ? "已锁定" : "锁定", MinWidth = 72 };
                MirChrome.StylePrimary(lockBtn);
                lockBtn.IsEnabled = st?.SelfLocked != true;
                lockBtn.Click += async (_, _) => Toast(await _session.TradeLockAsync(true) ?? "已锁定");
                var cancel = new Button { Content = "取消", MinWidth = 72 };
                MirChrome.StyleDanger(cancel);
                cancel.Click += async (_, _) => await _session.TradeCancelAsync();
                var no = new Button { Content = "拒绝", MinWidth = 72 };
                MirChrome.StyleDanger(no);
                no.Click += async (_, _) => Toast(await _session.TradeRespondAsync(false) ?? "已拒绝");
                actions.Children.Add(yes);
                actions.Children.Add(lockBtn);
                actions.Children.Add(cancel);
                actions.Children.Add(no);
                _panelBody.Children.Add(actions);
                bool both = st?.SelfLocked == true && st?.PeerLocked == true;
                _panelBody.Children.Add(new TextBlock
                {
                    Text = both ? "双方已锁定，可确认成交；Esc 取消" : "锁定后对方可见；双方锁定后可成交",
                    FontSize = 11, Foreground = Brush(both ? "#70D070" : "#C8B48A"),
                    Margin = new Avalonia.Thickness(0, 6, 0, 0)
                });
                break;
            }

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
            {
                _panelTitle.Text = "设置";
                var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
                foreach (var (id, label) in new[] { ("volume", "音量"), ("graphics", "画质"), ("keys", "键位"), ("assist", "辅助") })
                {
                    string tabId = id;
                    var tab = MirChrome.MakeTab(label, _settingsTab == tabId);
                    tab.Click += (_, _) => { _settingsTab = tabId; _panelFp = ""; RefreshPhaseL(); };
                    tabs.Children.Add(tab);
                }
                _panelBody.Children.Add(tabs);

                if (_settingsTab == "volume")
                {
                    _panelBody.Children.Add(SliderRow("主音量", _audio.Master, v => _audio.Master = (float)v));
                    _panelBody.Children.Add(SliderRow("音乐", _audio.BgmVolume, v => _audio.BgmVolume = (float)v));
                    _panelBody.Children.Add(SliderRow("音效", _audio.SfxVolume, v => _audio.SfxVolume = (float)v));
                    _panelBody.Children.Add(SliderRow("界面", _audio.UiVolume, v => _audio.UiVolume = (float)v));
                    var mute = new CheckBox { Content = "静音", IsChecked = _audio.Muted, Foreground = Brush("#F6E6C0") };
                    mute.IsCheckedChanged += (_, _) => _audio.Muted = mute.IsChecked == true;
                    _panelBody.Children.Add(mute);
                }
                else if (_settingsTab == "graphics")
                {
                    _panelBody.Children.Add(Block("分辨率跟随窗口。阴影 / 天气特效占位，当前版本固定标准画质。"));
                    _panelBody.Children.Add(Block("帧率目标：显示器刷新率。"));
                }
                else if (_settingsTab == "keys")
                {
                    _panelBody.Children.Add(Block("WASD 移动 · 鼠标右键跑步"));
                    _panelBody.Children.Add(Block("F1–F8 技能 · 1–6 喝药 · Tab 大地图"));
                    _panelBody.Children.Add(Block("` 切目标 · F9 背包 · F10 人物 · F11 技能"));
                    _panelBody.Children.Add(Block("Ctrl+H 攻击模式 · Alt+X 小退选角 · Esc 关窗"));
                    _panelBody.Children.Add(Block("聊天会按本地脏词表替换。"));
                }
                else
                {
                    _panelBody.Children.Add(new TextBlock { Text = "扩展入口", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                    foreach (var (label, on) in new[] { ("商城", true), ("内挂", false), ("称号", true), ("排行榜", true), ("邮件", false), ("活动", true) })
                    {
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        row.Children.Add(new CheckBox { IsChecked = on, IsEnabled = false, Content = label, Foreground = Brush("#F6E6C0"), Width = 120 });
                        row.Children.Add(new TextBlock
                        {
                            Text = on ? "开" : "关",
                            FontSize = 12,
                            Foreground = Brush(on ? "#70D070" : "#E07070"),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        _panelBody.Children.Add(MirChrome.ListRow(row));
                    }
                }

                var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 12, 0, 0) };
                var apply = new Button { Content = "应用", MinWidth = 64 };
                MirChrome.StyleNormal(apply);
                apply.Click += (_, _) => Toast("设置已应用");
                var def = new Button { Content = "默认", MinWidth = 64 };
                MirChrome.StyleNormal(def);
                def.Click += (_, _) =>
                {
                    _audio.Master = 0.8f; _audio.BgmVolume = 0.55f; _audio.SfxVolume = 0.8f; _audio.UiVolume = 0.7f; _audio.Muted = false;
                    _panelFp = ""; RefreshPhaseL();
                };
                var close = new Button { Content = "关闭", MinWidth = 64 };
                MirChrome.StyleNormal(close);
                close.Click += (_, _) => ShowPanel("");
                var small = new Button { Content = "小退 Alt+X", MinWidth = 88 };
                MirChrome.StyleDanger(small);
                small.Click += (_, _) =>
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
                };
                var logout = new Button { Content = "退出游戏", MinWidth = 88 };
                MirChrome.StyleDanger(logout);
                logout.Click += (_, _) =>
                {
                    _session.ReturnToLogin();
                    _audio.PlayLoginBgm();
                };
                footer.Children.Add(apply); footer.Children.Add(def); footer.Children.Add(close);
                footer.Children.Add(small); footer.Children.Add(logout);
                _panelBody.Children.Add(footer);
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "小退=回角色 · 大退=退出登录 · Esc 关闭 · O/F12 打开设置",
                    FontSize = 10, Foreground = Brush("#E07070"), Margin = new Avalonia.Thickness(0, 6, 0, 0)
                });
                break;
            }

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
