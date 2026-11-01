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
    private string _friendsTab = "friends";
    private string _stallTab = "browse";
    private string _stallQuery = "";
    private string _stallCat = "all";
    private string _mallTab = "hot";
    private string _looksTab = "pets";
    private string _guildTab = "members";
    private string _selectedGuildMemberId = "";
    private string _mailTab = "inbox";
    private string _assistTab = "fight";
    private bool _assistArmed;
    private bool _assistNearFirst;
    private bool _assistSkillLoop;
    private bool _assistComboReadyOnly;
    private bool _assistQualityBlue;
    private bool _assistStopFullBag = true;
    private bool _assistPauseOnPvp = true;
    private bool _assistStopInSafe = true;
    private bool _assistNoAttackGuard = true;
    private string _selectedMailId = "";
    private Border _assistFloat = null!;
    private TextBlock _assistBadge = null!;
    private Border _confirmHost = null!;
    private TextBlock _confirmTitle = null!;
    private TextBlock _confirmBody = null!;
    private Button _confirmOk = null!;
    private Button _confirmCancel = null!;
    private Action? _confirmAction;
    private Border _partyInviteHost = null!;
    private TextBlock _partyInviteName = null!;
    private TextBlock _partyInviteMeta = null!;
    private TextBlock _partyInviteLine = null!;
    private TextBlock _assistBackLink = null!;
    private readonly List<MailEntry> _mailTrash = new();
    private string _ctxName = "";
    private Border _questTracker = null!;
    private StackPanel _questTrackerBody = null!;
    private Border _zoneBanner = null!;
    private TextBlock _zoneBannerText = null!;
    private Border _siegeBanner = null!;
    private TextBlock _siegeBannerText = null!;
    private Border _ctxHost = null!;
    private StackPanel _ctxBody = null!;
    private StackPanel _chatChannels = null!;

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
        // Compact Mir-style dock: primary row + secondary wrap. No baked BR art under the grid
        // (old br-cluster.png baked buttons/text and caused density collapse with live controls).
        var primary = new WrapPanel { MaxWidth = 204 };
        void AddPri(string text, Action action)
        {
            var b = Sys(text, action);
            b.Width = 46; b.Height = 24; b.FontSize = 11;
            primary.Children.Add(b);
        }
        AddPri("背包", () => { _bagOpen = !_bagOpen; _audio.PlayOpen(); });
        AddPri("人物", () => ShowPanel("char"));
        AddPri("技能", () => ShowPanel("skills"));
        AddPri("任务", () => ShowPanel("quest"));
        AddPri("组队", () => ShowPanel("party"));
        AddPri("行会", () => ShowPanel("guild"));
        AddPri("地图", () => ShowPanel("map"));
        AddPri("设置", () => ShowPanel("settings"));

        var secondary = new WrapPanel { MaxWidth = 204 };
        void AddSec(string text, Action action)
        {
            var b = Sys(text, action);
            b.Width = 46; b.Height = 22; b.FontSize = 10; b.Opacity = 0.92;
            secondary.Children.Add(b);
        }
        AddSec("好友", () => ShowPanel("friends"));
        AddSec("摆摊", () => ShowPanel("stall"));
        AddSec("商城", () => ShowPanel("mall"));
        AddSec("外观", () => ShowPanel("looks"));
        AddSec("英雄", () => ShowPanel("hero"));
        AddSec("邮件", () => ShowPanel("mail"));
        AddSec("内挂", () => ShowPanel("assist"));
        AddSec("世界", () => ShowPanel("world"));
        AddSec("交易", () => ShowPanel("trade"));
        AddSec("攻城", () => { ShowPanel("siege"); _ = Safe(async () => { await _session.SiegeStatusAsync(); }); });
        AddSec("名色", () => ShowPanel("names"));

        _modeLabel.FontSize = 12;
        _modeLabel.Foreground = Brush("#F0D8A8");
        _expLabel.FontSize = 11;
        _expLabel.Foreground = Brush("#C8B48A");

        var dock = new StackPanel { Spacing = 3, Width = 208 };
        dock.Children.Add(_modeLabel);
        dock.Children.Add(skills);
        dock.Children.Add(potions);
        dock.Children.Add(primary);
        dock.Children.Add(secondary);
        dock.Children.Add(_expLabel);

        var br = new Border
        {
            Width = 220,
            Background = Brush("#CC16110E"),
            BorderBrush = Brush("#C8A050"),
            BorderThickness = new Avalonia.Thickness(1.5),
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(8, 8, 8, 8),
            Child = dock,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var bar = new Border
        {
            Child = br,
            Margin = new Avalonia.Thickness(0, 0, 8, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        hud.Children.Add(bar);

        MirChrome.StyleField(_whisperName);
        _chatChannels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var channels = _chatChannels;
        channels.Children.Add(Sys("喊话", () => _chatChannel = ChatChannel.ChatWorld));
        channels.Children.Add(Sys("私聊", () => _chatChannel = ChatChannel.ChatWhisper));
        channels.Children.Add(Sys("组队", () => _chatChannel = ChatChannel.ChatParty));
        channels.Children.Add(Sys("行会", () => _chatChannel = ChatChannel.ChatGuild));
        channels.Children.Add(_whisperName);

        _panelBody.Children.Add(_panelTitle);
        var panelScroll = new ScrollViewer { Content = _panelBody, MaxHeight = 560, Width = 580 };
        _panelHost = MirChrome.Framed(panelScroll, 600, null, new Avalonia.Thickness(12, 12, 12, 12));
        _panelHost.HorizontalAlignment = HorizontalAlignment.Center;
        _panelHost.VerticalAlignment = VerticalAlignment.Center;
        _panelHost.Margin = new Avalonia.Thickness(0, 36, 0, 80);
        _panelHost.IsVisible = false;
        _panelHost.ZIndex = 25; // above chat 「私聊对象」 row (Z=2)
        hud.Children.Add(_panelHost);

        // OL-16-N: split 「挂」badge + 「点回内挂设置」 link (two controls)
        _assistBadge = new TextBlock
        {
            Text = "",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#70D0A8"),
            Margin = new Avalonia.Thickness(0, 2, 4, 0)
        };
        _assistBackLink = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = Brush("#C8B48A"),
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Avalonia.Thickness(0, 2, 0, 0)
        };
        _assistBackLink.PointerPressed += (_, _) => ShowPanel("assist");
        dock.Children.Insert(Math.Min(3, dock.Children.Count), _assistBadge);
        dock.Children.Insert(Math.Min(4, dock.Children.Count), _assistBackLink);

        var assistFloatBody = new StackPanel { Spacing = 8 };
        // OL-16-N preview: titlechip 「扩展」 on stone-gold chrome header
        var assistHead = new Grid { Margin = new Avalonia.Thickness(0, 0, 0, 2) };
        assistHead.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
        assistHead.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        assistHead.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
        var extChip = new Button { Content = "扩展", MinWidth = 44, MinHeight = 22, FontSize = 11, Padding = new Avalonia.Thickness(6, 1) };
        MirChrome.StyleSys(extChip);
        extChip.Click += (_, _) => ShowPanel("assist");
        Grid.SetColumn(extChip, 0);
        assistHead.Children.Add(extChip);
        var hangTitle = new TextBlock
        {
            Text = "挂机中", FontSize = 14, FontWeight = FontWeight.Bold,
            Foreground = Brush("#E8C872"), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(hangTitle, 1);
        assistHead.Children.Add(hangTitle);
        assistFloatBody.Children.Add(assistHead);
        assistFloatBody.Children.Add(new TextBlock
        {
            Text = "自动战斗运行中，请遵守本服公告",
            FontSize = 12, Foreground = Brush("#E8C872"), TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        assistFloatBody.Children.Add(new TextBlock
        {
            Text = "手动点商铺 / 交易 / 死亡 → 暂停",
            FontSize = 11, Foreground = Brush("#F0D060"), TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var assistFloatBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        var stopHang = new Button { Content = "停止挂机", MinWidth = 96 };
        MirChrome.StyleDanger(stopHang);
        stopHang.Click += (_, _) =>
        {
            _session.StopAssist("ui");
            Toast("内挂已停止");
            _panelFp = "";
            RefreshPhaseL();
        };
        var openAssist = new Button { Content = "打开设置", MinWidth = 96 };
        MirChrome.StyleNormal(openAssist);
        openAssist.Click += (_, _) => ShowPanel("assist");
        assistFloatBtns.Children.Add(stopHang);
        assistFloatBtns.Children.Add(openAssist);
        assistFloatBody.Children.Add(assistFloatBtns);
        assistFloatBody.Children.Add(new TextBlock
        {
            Text = "HUD 最小化 · 禁霓虹大条", FontSize = 10, Foreground = Brush("#A09070"),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        // OL-16-N: stone-gold Framed thick window (not flat program Border)
        _assistFloat = MirChrome.Framed(assistFloatBody, 300, null, new Avalonia.Thickness(14, 12, 14, 12));
        _assistFloat.HorizontalAlignment = HorizontalAlignment.Center;
        _assistFloat.VerticalAlignment = VerticalAlignment.Center;
        _assistFloat.Margin = new Avalonia.Thickness(0, 0, 0, 40);
        _assistFloat.IsVisible = false;
        _assistFloat.ZIndex = 30;
        hud.Children.Add(_assistFloat);

        _confirmTitle = new TextBlock
        {
            FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _confirmBody = new TextBlock
        {
            FontSize = 12, Foreground = Brush("#F6E6C0"), TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 6, 0, 10)
        };
        _confirmOk = new Button { Content = "确定", MinWidth = 88 };
        MirChrome.StyleDanger(_confirmOk);
        _confirmCancel = new Button { Content = "取消", MinWidth = 88 };
        MirChrome.StyleNormal(_confirmCancel);
        _confirmOk.Click += (_, _) =>
        {
            var act = _confirmAction;
            HideConfirm();
            act?.Invoke();
        };
        _confirmCancel.Click += (_, _) => HideConfirm();
        var confirmBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        confirmBtns.Children.Add(_confirmOk);
        confirmBtns.Children.Add(_confirmCancel);
        var confirmInner = new StackPanel { Spacing = 4, Width = 300 };
        confirmInner.Children.Add(_confirmTitle);
        confirmInner.Children.Add(_confirmBody);
        confirmInner.Children.Add(confirmBtns);
        _confirmHost = new Border
        {
            Background = Brush("#F01A1210"),
            BorderBrush = Brush("#A04030"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(16, 12),
            Child = confirmInner,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            ZIndex = 40
        };
        hud.Children.Add(_confirmHost);

        // OL-07-D: centered independent party-invite modal (not nested in party panel)
        _partyInviteName = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush("#F6E6C0")
        };
        _partyInviteMeta = new TextBlock
        {
            FontSize = 11, Foreground = Brush("#70D070"), Margin = new Avalonia.Thickness(0, 2, 0, 0)
        };
        _partyInviteLine = new TextBlock
        {
            FontSize = 12, Foreground = Brush("#F6E6C0"), Margin = new Avalonia.Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        var inviteWho = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        inviteWho.Children.Add(new Border
        {
            Width = 48, Height = 48, Background = Brush("#2A1C14"),
            BorderBrush = Brush("#8A6A38"), BorderThickness = new Avalonia.Thickness(1)
        });
        var inviteWhoCol = new StackPanel { Spacing = 0 };
        inviteWhoCol.Children.Add(_partyInviteName);
        inviteWhoCol.Children.Add(_partyInviteMeta);
        inviteWho.Children.Add(inviteWhoCol);
        var inviteBody = new StackPanel { Spacing = 8, Width = 280 };
        inviteBody.Children.Add(new TextBlock
        {
            Text = "组队邀请", FontSize = 14, FontWeight = FontWeight.Bold,
            Foreground = Brush("#E8C872"), HorizontalAlignment = HorizontalAlignment.Center
        });
        inviteBody.Children.Add(inviteWho);
        inviteBody.Children.Add(_partyInviteLine);
        inviteBody.Children.Add(new TextBlock
        {
            Text = "入队后，可在右上角切换攻击模式，避免自动反击。",
            FontSize = 11, Foreground = Brush("#E8C872"), TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        });
        inviteBody.Children.Add(new TextBlock
        {
            Text = "⚠ 超距将自动失效，请尽快处理。",
            FontSize = 11, Foreground = Brush("#F0A060"), TextWrapping = TextWrapping.Wrap
        });
        var inviteBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        var inviteYes = new Button { Content = "同意", MinWidth = 96 };
        MirChrome.StylePrimary(inviteYes);
        inviteYes.Click += async (_, _) =>
        {
            Toast(await _session.AcceptPartyAsync(true) ?? "已入队");
            _panelFp = "";
            RefreshPhaseL();
        };
        var inviteNo = new Button { Content = "拒绝", MinWidth = 96 };
        MirChrome.StyleDanger(inviteNo);
        inviteNo.Click += async (_, _) =>
        {
            Toast(await _session.AcceptPartyAsync(false) ?? "已拒绝");
            _panelFp = "";
            RefreshPhaseL();
        };
        inviteBtns.Children.Add(inviteYes);
        inviteBtns.Children.Add(inviteNo);
        inviteBody.Children.Add(inviteBtns);
        _partyInviteHost = MirChrome.Framed(inviteBody, 300, null, new Avalonia.Thickness(14, 12, 14, 12));
        _partyInviteHost.HorizontalAlignment = HorizontalAlignment.Center;
        _partyInviteHost.VerticalAlignment = VerticalAlignment.Center;
        _partyInviteHost.Margin = new Avalonia.Thickness(0, 0, 0, 60);
        _partyInviteHost.IsVisible = false;
        _partyInviteHost.ZIndex = 35;
        hud.Children.Add(_partyInviteHost);

        DragDrop.SetAllowDrop(_panelHost, true);
        _panelHost.AddHandler(DragDrop.DropEvent, OnItemDropped);

        _questTrackerBody = new StackPanel { Spacing = 3 };
        _questTracker = new Border
        {
            Background = Brush("#CC1C1612"),
            BorderBrush = Brush("#8A6A38"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(8, 6),
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(12, 48, 0, 0),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "任务追踪", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") },
                    _questTrackerBody
                }
            },
            IsVisible = false
        };
        hud.Children.Add(_questTracker);

        _zoneBannerText = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush("#F6E6C0"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _zoneBanner = new Border
        {
            Background = Brush("#CC2A1C10"),
            BorderBrush = Brush("#E8C872"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(16, 6),
            Child = _zoneBannerText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 36, 0, 0),
            IsVisible = false
        };
        hud.Children.Add(_zoneBanner);

        _siegeBannerText = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush("#FDD835"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _siegeBanner = new Border
        {
            Background = Brush("#CC1A1008"),
            BorderBrush = Brush("#C08030"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(18, 6),
            Child = _siegeBannerText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            IsVisible = false
        };
        hud.Children.Add(_siegeBanner);

        _ctxBody = new StackPanel { Spacing = 2 };
        _ctxHost = new Border
        {
            Background = Brush("#EE1C1612"),
            BorderBrush = Brush("#E8C872"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(6, 6),
            Width = 160,
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "玩家菜单", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872"), HorizontalAlignment = HorizontalAlignment.Center },
                    _ctxBody
                }
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(180, 0, 0, 40),
            IsVisible = false
        };
        hud.Children.Add(_ctxHost);

        channels.ZIndex = 2; // below center modals / panelHost — 私聊对象 must not cover bottom buttons
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
            HidePlayerContext();
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
            else if (id == "mail")
                _ = Safe(() => _session.RefreshMailsAsync());
        }
    }

    private void ClosePhaseLPanels()
    {
        _openPanel = "";
        _mouseRun = false;
        HidePlayerContext();
    }

    private void RefreshPhaseL()
    {
        EnsureBarForClass();
        RefreshQuestTracker();
        RefreshSiegeBanner();
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
        bool assistRun = _session.Assist.Running;
        if (_assistBadge != null)
        {
            _assistBadge.Text = assistRun ? "挂" : "";
            _assistBadge.IsVisible = assistRun;
        }
        if (_assistBackLink != null)
        {
            _assistBackLink.Text = assistRun ? "点回内挂设置" : "";
            _assistBackLink.IsVisible = assistRun;
        }
        if (_assistFloat != null)
        {
            // OL-16-N only when chrome is clear — hide under any panel / party-invite modal
            bool invitePending = _session.PendingPartyId.Length > 0;
            _assistFloat.IsVisible = assistRun
                && _openPanel.Length == 0
                && !invitePending
                && _session.Fsm.Phase == SessionPhase.InWorld;
        }
        if (_partyInviteHost != null)
        {
            bool pending = _session.PendingPartyId.Length > 0 && _session.Fsm.Phase == SessionPhase.InWorld;
            if (pending)
            {
                string who = string.IsNullOrWhiteSpace(_session.PendingInviter) ? "未知玩家" : _session.PendingInviter;
                _partyInviteName.Text = who;
                var actor = _session.World.Actors.Values.FirstOrDefault(a =>
                    string.Equals(a.Name, who, StringComparison.OrdinalIgnoreCase));
                // protocol invite has no Lv/class — local placeholder for OL-07-D chrome
                int lv = 30 + Math.Abs(who.GetHashCode() % 40);
                string job = actor != null && !string.IsNullOrWhiteSpace(actor.ClassId)
                    ? ClassLabel(actor.ClassId)
                    : ClassLabel(_session.ClassId);
                _partyInviteMeta.Text = $"Lv.{lv} {job} · 队长";
                int partyN = Math.Max(1, _session.Party.Count);
                _partyInviteLine.Text = $"邀请你加入队伍（当前 {partyN}/5）";
            }
            _partyInviteHost.IsVisible = pending;
        }
        _panelHost.IsVisible = _openPanel.Length > 0 && _session.Fsm.Phase == SessionPhase.InWorld;
        if (_chatChannels != null)
        {
            // Keep channel chips usable on clear HUD; hide under center modal/panel so 私聊对象 cannot cover底钮
            bool centerChrome = _panelHost.IsVisible
                || (_assistFloat?.IsVisible == true)
                || (_partyInviteHost?.IsVisible == true)
                || (_confirmHost?.IsVisible == true);
            _chatChannels.IsVisible = !centerChrome;
        }
        if (!_panelHost.IsVisible && _confirmHost != null)
            HideConfirm();
        // party invite modal is independent of panel host — do not early-return solely on panel
        if (!_panelHost.IsVisible)
            return;
        var trade = _session.TradeState;
        string fp = _openPanel + "|" + _shopTab + "|" + _settingsTab + "|" + _friendsTab + "|" + _stallTab + "|" + _stallQuery + "|" + _stallCat + "|" + _mallTab + "|" + _guildTab + "|" + _selectedGuildMemberId + "|" + _looksTab + "|" + _mailTab + "|" + _assistTab + "|" + _assistArmed + "|" + _assistNearFirst + "|" + _assistSkillLoop + "|" + _assistComboReadyOnly + "|" + _assistQualityBlue + "|" + _assistStopFullBag + "|" + _assistPauseOnPvp + "|" + _assistStopInSafe + "|" + _assistNoAttackGuard + "|" + _selectedMailId + "|" + _session.Mails.Count + "|" + _mailTrash.Count + "|" + (assistRun ? 1 : 0) + "|" + _selectedShopItem + "|" + _session.Inventory.Gold + "|" + _session.Quests.Count + "|" + _session.Skills.Count + "|" + _session.Storage.Count + "|" + _session.Party.Count + "|" + _session.PendingPartyId + "|" + _session.TradeId + "|" + (trade?.SelfLocked == true) + (trade?.PeerLocked == true) + trade?.PeerGold + "|" + _session.SiegeNote + "|" + _session.SocialRevision + "|" + _session.Friends.Count + "|" + _session.Market.Count;
        if (fp == _panelFp)
            return;
        _panelFp = fp;
        RebuildPanel();
    }


    private static string FormatMailWhen(long unixMs)
    {
        if (unixMs <= 0) return "";
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime();
        var today = DateTime.Today;
        if (dt.Date == today) return "今天 " + dt.ToString("HH:mm");
        if (dt.Date == today.AddDays(-1)) return "昨天 " + dt.ToString("HH:mm");
        return dt.ToString("MM-dd");
    }

    private Border MailAttachSlot(string label, bool claimed, int icon = 1, bool bound = false)
    {
        var stack = new StackPanel { Spacing = 2, Width = 76 };
        var bmp = MirChrome.ItemIcon(icon);
        if (bmp != null)
            stack.Children.Add(new Image { Source = bmp, Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Center });
        else
            stack.Children.Add(new Border
            {
                Width = 36, Height = 36, Background = Brush("#2A1C14"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 10,
            Foreground = Brush(claimed ? "#9E9E9E" : "#F6E6C0"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        // OL-17-BF: 绑/非绑 Caption on attach cells
        stack.Children.Add(new TextBlock
        {
            Text = bound ? "绑" : "非绑",
            FontSize = 9,
            Foreground = Brush(bound ? "#C070F0" : "#A09070"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        return new Border
        {
            Background = Brush("#4414100C"),
            BorderBrush = Brush(bound ? "#8050A0" : "#6A5030"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(4),
            Margin = new Avalonia.Thickness(0, 0, 6, 4),
            Child = stack
        };
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
                _panelBody.Children.Add(Block("职业技能列表 · 双击未学技能行可尝试学习"));
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
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "快捷栏",
                    FontSize = 11, Foreground = Brush("#A09070"),
                    Margin = new Avalonia.Thickness(0, 10, 0, 2)
                });
                var hotbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 0) };
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
            {
                _panelTitle.Text = "任务";
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "进行中 / 可交付 · 追踪显示在左上角",
                    FontSize = 11, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                });
                if (_session.Quests.Count == 0 && (_session.LastTalk?.Quests.Count ?? 0) == 0)
                    _panelBody.Children.Add(MirChrome.ListRow(Block("还没有任务。去找镇上的阿福。")));
                foreach (var quest in _session.Quests)
                {
                    string badge = quest.Status == QuestStatus.QuestReady ? "可交付" :
                        quest.Status == QuestStatus.QuestActive ? "进行中" : quest.Status.ToString();
                    string badgeColor = quest.Status == QuestStatus.QuestReady ? "#70D070" : "#E8C872";
                    var row = new StackPanel { Spacing = 2 };
                    var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    head.Children.Add(new TextBlock { Text = quest.Title, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush("#F6E6C0"), Width = 220 });
                    head.Children.Add(new TextBlock { Text = badge, FontSize = 11, Foreground = Brush(badgeColor), VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(head);
                    row.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(quest.Description) ? $"{quest.Progress}/{quest.TargetCount}" : quest.Description,
                        FontSize = 11, Foreground = Brush("#C8B48A"), TextWrapping = TextWrapping.Wrap
                    });
                    row.Children.Add(MirChrome.ThinBar("#C08030", quest.Progress, Math.Max(1, quest.TargetCount)));
                    if (quest.Status == QuestStatus.QuestReady)
                    {
                        string id = quest.QuestId;
                        var turn = new Button { Content = "交付", MinWidth = 64, HorizontalAlignment = HorizontalAlignment.Left };
                        MirChrome.StylePrimary(turn);
                        turn.Click += async (_, _) => Toast(await _session.TurnInQuestAsync(id) ?? "已交付");
                        row.Children.Add(turn);
                    }
                    _panelBody.Children.Add(MirChrome.ListRow(row));
                }
                if (_session.LastTalk != null)
                {
                    foreach (var quest in _session.LastTalk.Quests)
                    {
                        string id = quest.QuestId;
                        if (quest.Status == QuestStatus.QuestNone)
                        {
                            var button = new Button { Content = $"接取 {quest.Title}", HorizontalAlignment = HorizontalAlignment.Stretch };
                            MirChrome.StylePrimary(button);
                            button.Click += async (_, _) => Toast(await _session.AcceptQuestAsync(id) ?? "已接取");
                            _panelBody.Children.Add(button);
                        }
                        else if (quest.Status == QuestStatus.QuestReady && !_session.Quests.Any(q => q.QuestId == id))
                        {
                            var button = new Button { Content = $"交付 {quest.Title}", HorizontalAlignment = HorizontalAlignment.Stretch };
                            MirChrome.StylePrimary(button);
                            button.Click += async (_, _) => Toast(await _session.TurnInQuestAsync(id) ?? "已交付");
                            _panelBody.Children.Add(button);
                        }
                    }
                }
                break;
            }

            case "party":
            {
                _panelTitle.Text = "组队";
                _panelBody.Children.Add(new TextBlock
                {
                    Text = $"Alt+W · 成员 {_session.Party.Count}/8 · 同图击杀分享经验 · Ctrl+H 切攻击模式",
                    FontSize = 11, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                });
                var partyHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(4, 0, 0, 2) };
                foreach (var (lab, w) in new[] { ("角色", 140.0), ("等级", 48.0), ("状态", 40.0), ("地图", 120.0) })
                    partyHead.Children.Add(new TextBlock { Text = lab, FontSize = 10, Foreground = Brush("#A09070"), Width = w });
                _panelBody.Children.Add(partyHead);
                // OL-07-D invite is independent centered modal (_partyInviteHost), not nested here
                if (_session.PendingPartyId.Length > 0)
                {
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "有待处理的组队邀请（见中置模态）",
                        FontSize = 11, Foreground = Brush("#F0D060"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                    });
                }
                _panelBody.Children.Add(new TextBlock { Text = "成员", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                if (_session.Party.Count == 0)
                    _panelBody.Children.Add(MirChrome.ListRow(Block("当前没有队伍。")));
                foreach (var member in _session.Party)
                {
                    var actor = _session.World.Actors.Values.FirstOrDefault(a =>
                        string.Equals(a.Name, member.Name, StringComparison.OrdinalIgnoreCase));
                    double hp = actor?.Hp ?? (member.Online ? 1 : 0);
                    double maxHp = actor?.MaxHp ?? 1;
                    var col = new StackPanel { Spacing = 2 };
                    var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    head.Children.Add(new TextBlock
                    {
                        Text = (member.IsLeader ? "★ " : "") + member.Name,
                        FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush("#F6E6C0"), Width = 140
                    });
                    head.Children.Add(new TextBlock { Text = $"Lv{member.Level}", FontSize = 11, Foreground = Brush("#E8C872"), Width = 48, VerticalAlignment = VerticalAlignment.Center });
                    head.Children.Add(new TextBlock
                    {
                        Text = member.Online ? "在线" : "离线",
                        FontSize = 11, Foreground = Brush(member.Online ? "#70D070" : "#9E9E9E"),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    col.Children.Add(head);
                    col.Children.Add(MirChrome.ThinBar("#C04040", hp, maxHp));
                    col.Children.Add(new TextBlock { Text = member.MapId, FontSize = 10, Foreground = Brush("#A09070") });
                    _panelBody.Children.Add(MirChrome.ListRow(col));
                }
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "附近可邀请", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872"),
                    Margin = new Avalonia.Thickness(0, 8, 0, 4)
                });
                var nearby = _session.World.Actors.Values
                    .Where(a => !a.IsSelf && a.Kind == EntityKind.EntityPlayer)
                    .Take(8)
                    .ToList();
                if (nearby.Count == 0)
                    _panelBody.Children.Add(MirChrome.ListRow(Block("附近没有其他玩家。")));
                foreach (var actor in nearby)
                {
                    string nm = actor.Name;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{nm}  HP {actor.Hp}/{actor.MaxHp}",
                        Foreground = Brush("#F6E6C0"), Width = 220, VerticalAlignment = VerticalAlignment.Center
                    });
                    var inv = new Button { Content = "邀请", MinWidth = 64 };
                    MirChrome.StylePrimary(inv);
                    inv.Click += async (_, _) => Toast(await _session.InvitePartyAsync(nm) ?? "已邀请");
                    row.Children.Add(inv);
                    _panelBody.Children.Add(MirChrome.ListRow(row));
                }
                var nameBox = new TextBox { Watermark = "对方角色名", Margin = new Avalonia.Thickness(0, 8, 0, 4) };
                MirChrome.StyleField(nameBox);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var invite = new Button { Content = "邀请", MinWidth = 72 };
                MirChrome.StylePrimary(invite);
                invite.Click += async (_, _) => Toast(await _session.InvitePartyAsync(nameBox.Text ?? "") ?? "已邀请");
                var leave = new Button { Content = "离队", MinWidth = 72 };
                MirChrome.StyleDanger(leave);
                leave.Click += async (_, _) => Toast(await _session.LeavePartyAsync() ?? "已离队");
                actions.Children.Add(invite); actions.Children.Add(leave);
                _panelBody.Children.Add(nameBox);
                _panelBody.Children.Add(actions);
                break;
            }

            case "guild":
            {
                // OL-09-D 行会成员管理密度
                _panelTitle.Text = "行会";
                string gtitle = _session.GuildName.Length == 0 ? "尚未加入行会" : $"{_session.GuildName}  ·  {RankLabel(_session.GuildRank)}";
                _panelBody.Children.Add(new TextBlock { Text = gtitle, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                var gTabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 4, 0, 6) };
                foreach (var (id, label) in new[] { ("members", "成员"), ("apps", "申请"), ("vault", "仓库") })
                {
                    string tabId = id;
                    var tab = MirChrome.MakeTab(label, _guildTab == tabId);
                    tab.Click += (_, _) => { _guildTab = tabId; _panelFp = ""; RefreshPhaseL(); };
                    gTabs.Children.Add(tab);
                }
                _panelBody.Children.Add(gTabs);
                bool canManage = string.Equals(_session.GuildRank, "leader", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(_session.GuildRank, "vice_leader", StringComparison.OrdinalIgnoreCase);
                if (_guildTab == "apps")
                {
                    _panelBody.Children.Add(MirChrome.ListRow(Block("申请列表占位（未升协议）。可由会长在聊天约定后手动审批。")));
                }
                else if (_guildTab == "vault")
                {
                    _panelBody.Children.Add(MirChrome.ListRow(Block("行会仓库占位。仓管职位可任命；本刀不升协议。")));
                }
                else if (_session.GuildInfo != null)
                {
                    if (!string.IsNullOrWhiteSpace(_session.GuildInfo.Notice))
                        _panelBody.Children.Add(new TextBlock
                        {
                            Text = "公告：" + _session.GuildInfo.Notice,
                            FontSize = 11, Foreground = Brush("#F0D060"), TextWrapping = TextWrapping.Wrap,
                            Margin = new Avalonia.Thickness(0, 0, 0, 6)
                        });
                    var split = new Grid { ColumnDefinitions = new ColumnDefinitions("260,12,*"), MinHeight = 220 };
                    var left = new StackPanel { Spacing = 2 };
                    var right = new StackPanel { Spacing = 4 };
                    Grid.SetColumn(left, 0); Grid.SetColumn(right, 2);
                    split.Children.Add(left); split.Children.Add(right);
                    int gCount = _session.GuildInfo.Members.Count;
                    const int gCap = 200; // local capacity chrome; protocol has no MaxMembers on GuildInfoResp
                    left.Children.Add(new TextBlock
                    {
                        Text = $"成员列表（管理） {gCount}/{gCap}",
                        FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872"),
                        Margin = new Avalonia.Thickness(0, 0, 0, 4)
                    });
                    if (gCount >= 198)
                    {
                        left.Children.Add(new Border
                        {
                            Background = Brush("#55201010"), BorderBrush = Brush("#A04030"),
                            BorderThickness = new Avalonia.Thickness(1), Padding = new Avalonia.Thickness(8, 4),
                            Margin = new Avalonia.Thickness(0, 0, 0, 6),
                            Child = new TextBlock
                            {
                                Text = $"人数 {gCount}/{gCap} · 近满员，踢出前请确认挂名离线",
                                FontSize = 11, Foreground = Brush("#F0A060"), TextWrapping = TextWrapping.Wrap
                            }
                        });
                    }
                    var gHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(4, 0, 0, 2) };
                    foreach (var (lab, w) in new[] { ("角色", 90.0), ("等级", 40.0), ("职位", 56.0), ("状态", 40.0) })
                        gHead.Children.Add(new TextBlock { Text = lab, FontSize = 10, Foreground = Brush("#A09070"), Width = w });
                    left.Children.Add(gHead);
                    if (string.IsNullOrEmpty(_selectedGuildMemberId) && _session.GuildInfo.Members.Count > 0)
                        _selectedGuildMemberId = _session.GuildInfo.Members[0].CharacterId;
                    var selected = _session.GuildInfo.Members.FirstOrDefault(m => m.CharacterId == _selectedGuildMemberId)
                        ?? _session.GuildInfo.Members.FirstOrDefault();
                    var memberList = new StackPanel { Spacing = 2 };
                    foreach (var member in _session.GuildInfo.Members)
                    {
                        bool isSelf = string.Equals(member.CharacterId, _session.CharacterId, StringComparison.Ordinal);
                        bool sel = selected != null && member.CharacterId == selected.CharacterId;
                        var rowInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                        rowInner.Children.Add(new TextBlock
                        {
                            Text = (isSelf ? "◆ " : "") + member.Name,
                            FontSize = 12, Foreground = Brush(isSelf ? "#E8C872" : "#F6E6C0"), Width = 90,
                            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
                        });
                        // protocol GuildMemberInfo has no Level — local placeholder
                        int mLv = 20 + Math.Abs(member.CharacterId.GetHashCode() % 60);
                        rowInner.Children.Add(new TextBlock
                        {
                            Text = $"Lv{mLv}", FontSize = 11, Foreground = Brush("#C8B48A"), Width = 40,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        rowInner.Children.Add(new TextBlock
                        {
                            Text = RankLabel(member.Rank), FontSize = 11, Foreground = Brush("#E8C872"), Width = 56,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        var worldOnline = _session.World.Actors.Values.Any(a =>
                            string.Equals(a.Name, member.Name, StringComparison.OrdinalIgnoreCase));
                        // no Online field on protocol — world sight or alternating local placeholder
                        bool online = isSelf || worldOnline || (Math.Abs(member.CharacterId.GetHashCode()) % 3 != 0);
                        rowInner.Children.Add(new TextBlock
                        {
                            Text = online ? "在线" : "离线", FontSize = 10,
                            Foreground = Brush(online ? "#70D070" : "#9E9E9E"), Width = 40,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        string mid = member.CharacterId;
                        var row = MirChrome.ListRow(rowInner, sel);
                        row.PointerPressed += (_, _) => { _selectedGuildMemberId = mid; _panelFp = ""; RefreshPhaseL(); };
                        memberList.Children.Add(row);
                    }
                    left.Children.Add(new ScrollViewer
                    {
                        Content = memberList,
                        MaxHeight = 280,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                    });
                    if (selected == null)
                        right.Children.Add(Block("点选左侧成员查看管理项。"));
                    else
                    {
                        right.Children.Add(new TextBlock
                        {
                            Text = selected.Name, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brush("#F6E6C0")
                        });
                        int selLv = 20 + Math.Abs(selected.CharacterId.GetHashCode() % 60);
                        int contrib = 1000 + Math.Abs(selected.Name.GetHashCode() % 9000);
                        string joinDay = $"2026-{(Math.Abs(selected.CharacterId.GetHashCode() % 9) + 1):00}-{(Math.Abs(selected.Name.GetHashCode() % 27) + 1):00}";
                        right.Children.Add(new TextBlock
                        {
                            Text = $"Lv.{selLv} · {RankLabel(selected.Rank)}",
                            FontSize = 11, Foreground = Brush("#E8C872"), Margin = new Avalonia.Thickness(0, 2, 0, 2)
                        });
                        right.Children.Add(new TextBlock
                        {
                            Text = $"贡献 {contrib:N0}（本地示意） · 入会 {joinDay}",
                            FontSize = 11, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 0, 0, 8),
                            TextWrapping = TextWrapping.Wrap
                        });
                        right.Children.Add(new TextBlock
                        {
                            Text = "职位与管理（会长/副会长）", FontSize = 12, FontWeight = FontWeight.Bold,
                            Foreground = Brush("#E8C872"), Margin = new Avalonia.Thickness(0, 0, 0, 4)
                        });
                        var mgr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        string sid = selected.CharacterId;
                        bool selfSel = string.Equals(sid, _session.CharacterId, StringComparison.Ordinal);
                        var promote = new Button { Content = "升职", MinWidth = 72, IsEnabled = canManage && !selfSel };
                        MirChrome.StylePrimary(promote);
                        promote.Click += async (_, _) =>
                        {
                            string next = selected.Rank switch
                            {
                                "member" => "warehouse_admin",
                                "warehouse_admin" => "vice_leader",
                                _ => "vice_leader"
                            };
                            Toast(await _session.SetGuildRankAsync(sid, next) ?? "已升职");
                            _panelFp = "";
                        };
                        var demote = new Button { Content = "降职", MinWidth = 72, IsEnabled = canManage && !selfSel };
                        MirChrome.StyleNormal(demote);
                        demote.Click += async (_, _) =>
                        {
                            string next = selected.Rank switch
                            {
                                "vice_leader" => "warehouse_admin",
                                "warehouse_admin" => "member",
                                _ => "member"
                            };
                            Toast(await _session.SetGuildRankAsync(sid, next) ?? "已降职");
                            _panelFp = "";
                        };
                        var kick = new Button { Content = "踢出", MinWidth = 72, IsEnabled = canManage && !selfSel };
                        MirChrome.StyleDanger(kick);
                        string kickName = selected.Name;
                        kick.Click += (_, _) => ShowConfirm(
                            "踢出成员",
                            $"确认将「{kickName}」踢出行会？\n踢出需二次确认；满员时优先清理挂名离线。（试玩服未升踢出协议，确认后仅本地提示）",
                            () => Toast($"已确认踢出「{kickName}」· 服务端踢出未接线（协议外）"));
                        mgr.Children.Add(promote); mgr.Children.Add(demote); mgr.Children.Add(kick);
                        right.Children.Add(new Border
                        {
                            Background = Brush("#5514100C"), BorderBrush = Brush("#5A4030"),
                            BorderThickness = new Avalonia.Thickness(1), Padding = new Avalonia.Thickness(8, 6),
                            Child = new StackPanel
                            {
                                Spacing = 6,
                                Children =
                                {
                                    mgr,
                                    new TextBlock
                                    {
                                        Text = "踢出需二次确认，退员时优先清挂名离线。",
                                        FontSize = 10, Foreground = Brush("#A09070"), TextWrapping = TextWrapping.Wrap
                                    }
                                }
                            }
                        });
                    }
                    _panelBody.Children.Add(split);
                }
                else
                {
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "同图组队击杀有额外经验。创建或加入行会后可管理成员。",
                        FontSize = 11, Foreground = Brush("#C8B48A"), TextWrapping = TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 2, 0, 6)
                    });
                }
                var gname = new TextBox { Watermark = "行会名 / 邀请角色名", Margin = new Avalonia.Thickness(0, 10, 0, 4) };
                MirChrome.StyleField(gname);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var create = new Button { Content = "创建", MinWidth = 64 };
                MirChrome.StylePrimary(create);
                create.Click += async (_, _) => Toast(await _session.CreateGuildAsync(gname.Text ?? "") ?? "已创建");
                var join = new Button { Content = "加入", MinWidth = 64 };
                MirChrome.StyleNormal(join);
                join.Click += async (_, _) => Toast(await _session.JoinGuildAsync(gname.Text ?? "") ?? "已加入");
                var invite = new Button { Content = "邀请", MinWidth = 64 };
                MirChrome.StyleNormal(invite);
                invite.Click += (_, _) => Toast("行会邀请占位 · 可先用组队/好友联系");
                var apps = new Button { Content = "申请列表", MinWidth = 80 };
                MirChrome.StyleSys(apps);
                apps.Click += (_, _) => { _guildTab = "apps"; _panelFp = ""; RefreshPhaseL(); };
                var gleave = new Button { Content = "退出", MinWidth = 64 };
                MirChrome.StyleDanger(gleave);
                gleave.Click += async (_, _) => Toast(await _session.LeaveGuildAsync() ?? "已退出");
                actions.Children.Add(create); actions.Children.Add(join); actions.Children.Add(invite);
                actions.Children.Add(apps); actions.Children.Add(gleave);
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
            {
                _panelTitle.Text = "攻城信息";
                string note = string.IsNullOrWhiteSpace(_session.SiegeNote) ? "打开面板时会向服务器要状态。" : _session.SiegeNote;
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "沙巴克战场 · 顶栏同步显示剩余时间",
                    FontSize = 11, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                });
                foreach (var (k, v) in new[]
                {
                    ("阶段", note.Contains("进行") || note.Contains("攻") ? "进行中" : "待命"),
                    ("说明", note),
                    ("城门", "完好（示意）"),
                    ("旗帜", "幽影林 (20,20)"),
                    ("入口", "沙门关城墙挡住直路，城门格传送进皇宫区")
                })
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(new TextBlock { Text = k, FontSize = 12, Foreground = Brush("#E8C872"), Width = 56 });
                    row.Children.Add(new TextBlock { Text = v, FontSize = 12, Foreground = Brush("#F6E6C0"), TextWrapping = TextWrapping.Wrap, Width = 340 });
                    _panelBody.Children.Add(MirChrome.ListRow(row));
                }
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
                var refreshSiege = new Button { Content = "刷新状态", MinWidth = 88 };
                MirChrome.StyleNormal(refreshSiege);
                refreshSiege.Click += async (_, _) => Toast(await _session.SiegeStatusAsync() ?? "已刷新");
                var declare = new Button { Content = "行会宣战", MinWidth = 88 };
                MirChrome.StylePrimary(declare);
                declare.Click += async (_, _) => Toast(await _session.SiegeDeclareAsync() ?? "已宣战");
                var capture = new Button { Content = "占领旗帜", MinWidth = 88 };
                MirChrome.StyleDanger(capture);
                capture.Click += async (_, _) => Toast(await _session.SiegeCaptureAsync() ?? "已占领");
                var guild = new Button { Content = "行会面板", MinWidth = 88 };
                MirChrome.StyleSys(guild);
                guild.Click += (_, _) => ShowPanel("guild");
                var map = new Button { Content = "大地图", MinWidth = 72 };
                MirChrome.StyleSys(map);
                map.Click += (_, _) => ShowPanel("map");
                actions.Children.Add(refreshSiege); actions.Children.Add(declare); actions.Children.Add(capture);
                actions.Children.Add(guild); actions.Children.Add(map);
                _panelBody.Children.Add(actions);
                break;
            }

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
                    _panelBody.Children.Add(Block("` 切目标 · F9 背包 · F10 人物 · F11 技能 · O/F12 设置"));
                    _panelBody.Children.Add(Block("Alt+W 组队 · Alt+M 邮件 · Ctrl+E 英雄 · Ctrl+S 合击 · Ctrl+H 攻击模式"));
                    _panelBody.Children.Add(Block("Alt+X 小退 · Esc 关窗/菜单 · O/F12 设置"));
                    _panelBody.Children.Add(Block("聊天会按本地脏词表替换。"));
                }
                else
                {
                    _panelBody.Children.Add(new TextBlock { Text = "扩展入口", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                    foreach (var (label, on) in new[] { ("商城", true), ("内挂", true), ("称号", true), ("排行榜", true), ("邮件", true), ("活动", true) })
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
                    var openAssist = new Button { Content = "打开内挂面板", MinWidth = 120, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
                    MirChrome.StylePrimary(openAssist);
                    openAssist.Click += (_, _) => ShowPanel("assist");
                    _panelBody.Children.Add(openAssist);
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "内挂默认关闭，需在面板确认风险后启动。禁挂图 / 禁拾取图由服务端标志强制停。",
                        FontSize = 10, Foreground = Brush("#A09070"), TextWrapping = TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 6, 0, 0)
                    });
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
            {
                _panelTitle.Text = "好友";
                var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
                foreach (var (id, label) in new[] { ("friends", "好友"), ("black", "黑名单"), ("mentor", "师徒") })
                {
                    string tabId = id;
                    var tab = MirChrome.MakeTab(label, _friendsTab == tabId);
                    tab.Click += (_, _) => { _friendsTab = tabId; _panelFp = ""; RefreshPhaseL(); };
                    tabs.Children.Add(tab);
                }
                _panelBody.Children.Add(tabs);
                if (_friendsTab == "friends")
                {
                    var fname = new TextBox { Watermark = "角色名" };
                    MirChrome.StyleField(fname);
                    var add = new Button { Content = "添加好友", MinWidth = 88 };
                    MirChrome.StylePrimary(add);
                    add.Click += async (_, _) => Toast(await _session.AddFriendAsync(fname.Text ?? "") ?? "已添加");
                    var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    addRow.Children.Add(fname); addRow.Children.Add(add);
                    _panelBody.Children.Add(addRow);
                    int onlineN = _session.Friends.Count(f => f.Online);
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = $"好友 {_session.Friends.Count}  ·  在线 {onlineN}",
                        FontSize = 11, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 4, 0, 4)
                    });
                    var fHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(4, 0, 0, 2) };
                    foreach (var (lab, w) in new[] { ("角色", 120.0), ("状态", 40.0), ("操作", 160.0) })
                        fHead.Children.Add(new TextBlock { Text = lab, FontSize = 10, Foreground = Brush("#A09070"), Width = w });
                    _panelBody.Children.Add(fHead);
                    if (_session.Friends.Count == 0)
                        _panelBody.Children.Add(MirChrome.ListRow(Block("还没有好友。")));
                    foreach (var friend in _session.Friends.OrderByDescending(f => f.Online).ThenBy(f => f.Name))
                    {
                        string id = friend.CharacterId;
                        string nm = friend.Name;
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                        row.Children.Add(new TextBlock
                        {
                            Text = nm, FontSize = 13, FontWeight = friend.Online ? FontWeight.Bold : FontWeight.Normal,
                            Foreground = Brush(friend.Online ? "#F6E6C0" : "#A09070"), Width = 120,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        row.Children.Add(new TextBlock
                        {
                            Text = friend.Online ? "在线" : "离线",
                            FontSize = 11, Foreground = Brush(friend.Online ? "#70D070" : "#9E9E9E"), Width = 40,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        var whisper = new Button { Content = "私聊", MinWidth = 48 };
                        MirChrome.StyleSys(whisper);
                        whisper.Click += (_, _) => { _whisperName.Text = nm; _chatChannel = ChatChannel.ChatWhisper; Toast("私聊对象 " + nm); };
                        var party = new Button { Content = "组队", MinWidth = 48 };
                        MirChrome.StyleSys(party);
                        party.Click += async (_, _) => Toast(await _session.InvitePartyAsync(nm) ?? "已邀请");
                        var remove = new Button { Content = "删除", MinWidth = 48 };
                        MirChrome.StyleDanger(remove);
                        remove.Click += async (_, _) => Toast(await _session.RemoveFriendAsync(id) ?? "已删除");
                        row.Children.Add(whisper); row.Children.Add(party); row.Children.Add(remove);
                        _panelBody.Children.Add(MirChrome.ListRow(row));
                    }
                }
                else if (_friendsTab == "black")
                {
                    _panelBody.Children.Add(MirChrome.ListRow(Block("黑名单占位（本地示意）。右键玩家菜单可拉黑。")));
                    _panelBody.Children.Add(new TextBlock { Text = "拉黑后屏蔽私聊与附近喊话（客户端过滤占位）。", FontSize = 11, Foreground = Brush("#C8B48A"), TextWrapping = TextWrapping.Wrap });
                }
                else
                {
                    _panelBody.Children.Add(MirChrome.ListRow(Block("师徒占位：拜师徒弟 +10% 经验，在线师父获 20%。")));
                    _panelBody.Children.Add(new TextBlock { Text = "协议未接线前仅展示说明，不升协议。", FontSize = 11, Foreground = Brush("#C8B48A") });
                }
                break;
            }

            case "stall":
            {
                _panelTitle.Text = "交易行 · 寄售";
                var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 6) };
                // OL-08-J: three tabs + bottom「上架物品」(list is sub-view, not a 4th tab)
                foreach (var (id, label) in new[] { ("browse", "浏览"), ("mine", "我的"), ("log", "记录") })
                {
                    string tabId = id;
                    var tab = MirChrome.MakeTab(label, _stallTab == tabId);
                    tab.Click += (_, _) => { _stallTab = tabId; _panelFp = ""; RefreshPhaseL(); };
                    tabs.Children.Add(tab);
                }
                _panelBody.Children.Add(tabs);
                if (_stallTab == "list")
                {
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "上架物品（底栏入口 · 非独立 Tab）",
                        FontSize = 11, Foreground = Brush("#F0D060"), Margin = new Avalonia.Thickness(0, 0, 0, 4)
                    });
                }
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "浏览成交无手续费 · 上架扣 1%（成交结算）· 仅安全区可上架 · 单价 1–100000",
                    FontSize = 11, Foreground = Brush("#C8B48A"), TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 0, 0, 6)
                });
                if (_stallTab == "log")
                {
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "成交记录 · 本地示意假数据（未升协议，刷新不落库）",
                        FontSize = 11, Foreground = Brush("#F0D060"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                    });
                    var logHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(4, 0, 0, 2) };
                    foreach (var (lab, w) in new[] { ("时间", 90.0), ("物品", 140.0), ("单价", 80.0), ("对方", 90.0), ("结果", 56.0) })
                        logHead.Children.Add(new TextBlock { Text = lab, FontSize = 10, Foreground = Brush("#A09070"), Width = w });
                    _panelBody.Children.Add(logHead);
                    foreach (var (when, item, price, peer, result) in new[]
                    {
                        ("今天 09:12", "金创药(中)×20", "2,000", "刀客阿强", "售出"),
                        ("昨天 21:40", "铁剑", "8,500", "雪落无声", "购入"),
                        ("昨天 18:05", "魔法药(中)×10", "1,600", "龙战天下", "售出")
                    })
                    {
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                        row.Children.Add(new TextBlock { Text = when, FontSize = 11, Foreground = Brush("#C8B48A"), Width = 90, VerticalAlignment = VerticalAlignment.Center });
                        row.Children.Add(new TextBlock { Text = item, FontSize = 12, Foreground = Brush("#F6E6C0"), Width = 140, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                        row.Children.Add(new TextBlock { Text = price, FontSize = 11, Foreground = Brush("#E8C872"), Width = 80, VerticalAlignment = VerticalAlignment.Center });
                        row.Children.Add(new TextBlock { Text = peer, FontSize = 11, Foreground = Brush("#C8B48A"), Width = 90, VerticalAlignment = VerticalAlignment.Center });
                        row.Children.Add(new TextBlock { Text = result, FontSize = 11, Foreground = Brush("#70D070"), Width = 56, VerticalAlignment = VerticalAlignment.Center });
                        _panelBody.Children.Add(MirChrome.ListRow(row));
                    }
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "空态时显示「暂无成交记录」；上表为示意，不代表真实寄售流水。",
                        FontSize = 10, Foreground = Brush("#A09070"), Margin = new Avalonia.Thickness(0, 8, 0, 0)
                    });
                    break;
                }
                if (_stallTab is "browse" or "mine")
                {
                    var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(0, 0, 0, 4) };
                    var qBox = new TextBox { Watermark = "搜索物品名", Text = _stallQuery, Width = 140 };
                    MirChrome.StyleField(qBox);
                    var search = new Button { Content = "搜索", MinWidth = 56 };
                    MirChrome.StylePrimary(search);
                    search.Click += (_, _) => { _stallQuery = qBox.Text ?? ""; _panelFp = ""; RefreshPhaseL(); };
                    var reset = new Button { Content = "重置", MinWidth = 56 };
                    MirChrome.StyleNormal(reset);
                    reset.Click += (_, _) => { _stallQuery = ""; _stallCat = "all"; _panelFp = ""; RefreshPhaseL(); };
                    searchRow.Children.Add(qBox); searchRow.Children.Add(search); searchRow.Children.Add(reset);
                    foreach (var (cid, lab) in new[] { ("all", "全部"), ("weapon", "武器"), ("armor", "防具"), ("acc", "饰品"), ("mat", "材料") })
                    {
                        string cat = cid;
                        var b = new Button { Content = lab, MinWidth = 44 };
                        if (_stallCat == cat) MirChrome.StylePrimary(b); else MirChrome.StyleSys(b);
                        b.Click += (_, _) => { _stallCat = cat; _panelFp = ""; RefreshPhaseL(); };
                        searchRow.Children.Add(b);
                    }
                    _panelBody.Children.Add(searchRow);
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = $"筛选：{StallCatLabel(_stallCat)} ·「{(string.IsNullOrWhiteSpace(_stallQuery) ? "*" : _stallQuery)}」· 上架扣1%（成交结算）",
                        FontSize = 10, Foreground = Brush("#F0D060"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                    });
                }
                if (_stallTab == "list")
                {
                    var priceBox = new TextBox { Watermark = "单价", Text = "10", Width = 100 };
                    var countBox = new TextBox { Watermark = "数量", Text = "1", Width = 80 };
                    MirChrome.StyleField(priceBox); MirChrome.StyleField(countBox);
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(priceBox); row.Children.Add(countBox);
                    _panelBody.Children.Add(row);
                    foreach (var item in _session.Inventory.Items)
                    {
                        string id = item.ItemId;
                        var list = new Button
                        {
                            Content = "上架 " + ContentCatalog.ItemName(id) + " x" + item.Count,
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        MirChrome.StyleNormal(list);
                        list.Click += async (_, _) =>
                        {
                            int.TryParse(priceBox.Text, out int price);
                            int.TryParse(countBox.Text, out int count);
                            Toast(await _session.StallListAsync(id, Math.Max(1, count), Math.Max(1, price)) ?? "已上架");
                        };
                        _panelBody.Children.Add(list);
                    }
                }
                else
                {
                    bool mineOnly = _stallTab == "mine";
                    int shown = 0;
                    var sHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(4, 0, 0, 2) };
                    foreach (var (lab, w) in new[] { ("物品", 160.0), ("单价", 64.0), ("卖家", 64.0), ("剩余", 56.0), ("", 64.0) })
                        sHead.Children.Add(new TextBlock { Text = lab, FontSize = 10, Foreground = Brush("#A09070"), Width = w });
                    _panelBody.Children.Add(sHead);
                    foreach (var listing in _session.Market)
                    {
                        bool mine = string.Equals(listing.SellerCharacterId, _session.CharacterId, StringComparison.Ordinal);
                        if (mineOnly && !mine) continue;
                        if (!mineOnly && mine) continue; // browse shows others; mine shows mine. If browse and empty others, show all?
                        if (!string.IsNullOrWhiteSpace(_stallQuery)
                            && ContentCatalog.ItemName(listing.ItemId).IndexOf(_stallQuery, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        shown++;
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                        row.Children.Add(new TextBlock
                        {
                            Text = $"{ContentCatalog.ItemName(listing.ItemId)}×{listing.Count}",
                            FontSize = 12, Foreground = Brush("#F6E6C0"), Width = 160,
                            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
                        });
                        row.Children.Add(new TextBlock
                        {
                            Text = $"{listing.UnitPriceGold:N0}金", FontSize = 11, Foreground = Brush("#E8C872"), Width = 64,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        row.Children.Add(new TextBlock
                        {
                            Text = listing.SellerName, FontSize = 11, Foreground = Brush("#C8B48A"), Width = 64,
                            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
                        });
                        // remaining: local chrome from created_unix_ms (48h shelf); protocol has no expire field
                        long ageMs = listing.CreatedUnixMs > 0
                            ? Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - listing.CreatedUnixMs)
                            : 0;
                        int remainH = Math.Max(0, 48 - (int)(ageMs / 3_600_000));
                        row.Children.Add(new TextBlock
                        {
                            Text = remainH > 0 ? $"{remainH}时" : "将过期",
                            FontSize = 11, Foreground = Brush(remainH > 6 ? "#70D070" : "#F0A060"), Width = 56,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        string listingId = listing.ListingId;
                        var act = new Button { Content = mine ? "下架" : "购买", MinWidth = 64 };
                        if (mine) MirChrome.StyleDanger(act); else MirChrome.StylePrimary(act);
                        act.Click += async (_, _) =>
                        {
                            Toast(mine
                                ? await _session.StallCancelAsync(listingId) ?? "已下架"
                                : await _session.StallBuyAsync(listingId) ?? "已购买");
                        };
                        row.Children.Add(act);
                        _panelBody.Children.Add(MirChrome.ListRow(row));
                    }
                    if (shown == 0 && _stallTab == "browse")
                    {
                        // fallback: show all market
                        foreach (var listing in _session.Market)
                        {
                            shown++;
                            bool mine = string.Equals(listing.SellerCharacterId, _session.CharacterId, StringComparison.Ordinal);
                            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                            row.Children.Add(new TextBlock
                            {
                                Text = $"{ContentCatalog.ItemName(listing.ItemId)}×{listing.Count}",
                                FontSize = 12, Foreground = Brush("#F6E6C0"), Width = 160, VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            });
                            row.Children.Add(new TextBlock
                            {
                                Text = $"{listing.UnitPriceGold:N0}金",
                                FontSize = 11, Foreground = Brush("#E8C872"), Width = 64, VerticalAlignment = VerticalAlignment.Center
                            });
                            row.Children.Add(new TextBlock
                            {
                                Text = listing.SellerName, FontSize = 11, Foreground = Brush("#C8B48A"), Width = 64,
                                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
                            });
                            long ageMs2 = listing.CreatedUnixMs > 0
                                ? Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - listing.CreatedUnixMs)
                                : 0;
                            int remainH2 = Math.Max(0, 48 - (int)(ageMs2 / 3_600_000));
                            row.Children.Add(new TextBlock
                            {
                                Text = remainH2 > 0 ? $"{remainH2}时" : "将过期",
                                FontSize = 11, Foreground = Brush(remainH2 > 6 ? "#70D070" : "#F0A060"), Width = 56,
                                VerticalAlignment = VerticalAlignment.Center
                            });
                            string listingId = listing.ListingId;
                            var act = new Button { Content = mine ? "下架" : "购买", MinWidth = 64 };
                            if (mine) MirChrome.StyleDanger(act); else MirChrome.StylePrimary(act);
                            act.Click += async (_, _) =>
                            {
                                Toast(mine
                                    ? await _session.StallCancelAsync(listingId) ?? "已下架"
                                    : await _session.StallBuyAsync(listingId) ?? "已购买");
                            };
                            row.Children.Add(act);
                            _panelBody.Children.Add(MirChrome.ListRow(row));
                        }
                    }
                    if (shown == 0)
                    {
                        // OL-08-J 寄售无结果
                        var empty = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 16, 0, 8) };
                        empty.Children.Add(new TextBlock
                        {
                            Text = _stallTab == "mine" ? "你还没有寄售。" : "暂无符合条件的寄售",
                            FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush("#F6E6C0"),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                        empty.Children.Add(new TextBlock
                        {
                            Text = "试试清空筛选，或自己上架一件武器",
                            FontSize = 11, Foreground = Brush("#A09070"),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                        var emptyBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
                        var clear = new Button { Content = "清空筛选", MinWidth = 96 };
                        MirChrome.StyleNormal(clear);
                        clear.Click += (_, _) => { _stallQuery = ""; _stallCat = "all"; _panelFp = ""; RefreshPhaseL(); };
                        var goList = new Button { Content = "去上架", MinWidth = 96 };
                        MirChrome.StylePrimary(goList);
                        goList.Click += (_, _) => { _stallTab = "list"; _panelFp = ""; RefreshPhaseL(); };
                        emptyBtns.Children.Add(clear); emptyBtns.Children.Add(goList);
                        empty.Children.Add(emptyBtns);
                        _panelBody.Children.Add(empty);
                    }
                    // OL-08-J: keep hint above foot buttons — never side-by-side (was overlapping)
                    if (shown == 0)
                    {
                        _panelBody.Children.Add(new TextBlock
                        {
                            Text = "无匹配结果可清空筛选，或自行上架",
                            FontSize = 10, Foreground = Brush("#A09070"),
                            Margin = new Avalonia.Thickness(0, 10, 0, 2),
                            HorizontalAlignment = HorizontalAlignment.Left
                        });
                    }
                    var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
                    var listBtn = new Button { Content = "上架物品", MinWidth = 88 };
                    MirChrome.StylePrimary(listBtn);
                    listBtn.Click += (_, _) => { _stallTab = "list"; _panelFp = ""; RefreshPhaseL(); };
                    var refresh = new Button { Content = "刷新", MinWidth = 72 };
                    MirChrome.StyleNormal(refresh);
                    refresh.Click += async (_, _) => { await _session.RefreshMarketAsync(); _panelFp = ""; RefreshPhaseL(); };
                    foot.Children.Add(listBtn); foot.Children.Add(refresh);
                    _panelBody.Children.Add(foot);
                }
                break;
            }

            case "looks":
            {
                _panelTitle.Text = "外观 / 宝宝";
                var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 6) };
                foreach (var (id, label) in new[] { ("pets", "宝宝"), ("mounts", "坐骑"), ("fashions", "时装") })
                {
                    string tabId = id;
                    var tab = MirChrome.MakeTab(label, _looksTab == tabId);
                    tab.Click += (_, _) => { _looksTab = tabId; _panelFp = ""; RefreshPhaseL(); };
                    tabs.Children.Add(tab);
                }
                _panelBody.Children.Add(tabs);
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "商店可买缰绳、法袍礼盒和幼狼契约。解锁后在这里召唤或穿上。",
                    FontSize = 11, Foreground = Brush("#C8B48A"), TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 0, 0, 6)
                });
                if (_looksTab == "mounts" && _session.Mounts != null)
                {
                    foreach (var mount in _session.Mounts.Mounts)
                        AddLookRow(mount.DisplayName, mount.Owned, mount.Active, mount.MountId, "召唤", "下马",
                            () => _session.SummonMountAsync(mount.MountId),
                            () => _session.DismissMountAsync());
                }
                else if (_looksTab == "fashions" && _session.Fashions != null)
                {
                    foreach (var fashion in _session.Fashions.Fashions)
                        AddLookRow(fashion.DisplayName, fashion.Owned, fashion.Equipped, fashion.FashionId, "穿上", "脱下",
                            () => _session.EquipFashionAsync(fashion.FashionId),
                            () => _session.EquipFashionAsync(""));
                }
                else if (_session.Pets != null)
                {
                    foreach (var pet in _session.Pets.Pets)
                        AddLookRow(pet.DisplayName + $" 助战{pet.AssistDamagePercent}%", pet.Owned, pet.Active, pet.PetId, "召唤", "收回",
                            () => _session.SummonPetAsync(pet.PetId),
                            () => _session.DismissPetAsync());
                }
                else
                    _panelBody.Children.Add(MirChrome.ListRow(Block("外观数据加载中… 重新打开面板刷新。")));
                break;
            }

            case "mall":
            {
                // OL-17-B 赞助/礼包密度
                _panelTitle.Text = "商城";
                var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 6) };
                foreach (var (id, label) in new[] { ("hot", "热销"), ("consumable", "消耗"), ("fashion", "时装"), ("material", "材料"), ("pack", "礼包"), ("pay", "充值") })
                {
                    string tabId = id;
                    var tab = MirChrome.MakeTab(label, _mallTab == tabId);
                    tab.Click += (_, _) => { _mallTab = tabId; _panelFp = ""; RefreshPhaseL(); };
                    tabs.Children.Add(tab);
                }
                _panelBody.Children.Add(tabs);
                var currency = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Avalonia.Thickness(0, 0, 0, 6) };
                currency.Children.Add(new TextBlock { Text = $"金 {_session.Inventory.Gold:N0}", FontSize = 12, Foreground = Brush("#E8C872") });
                currency.Children.Add(new TextBlock { Text = "元宝 1,280（示意）", FontSize = 12, Foreground = Brush("#70B0F0") });
                currency.Children.Add(new TextBlock { Text = "绑 80（示意）", FontSize = 12, Foreground = Brush("#C070F0") });
                _panelBody.Children.Add(currency);
                if (_mallTab == "pay")
                {
                    _panelBody.Children.Add(new Border
                    {
                        Background = Brush("#551A1008"), BorderBrush = Brush("#6A4030"),
                        BorderThickness = new Avalonia.Thickness(1), Padding = new Avalonia.Thickness(8, 6),
                        Margin = new Avalonia.Thickness(0, 0, 0, 8),
                        Child = new TextBlock
                        {
                            Text = "充值通道未开通 · 试玩服不接线真实支付 · 档位仅供浏览",
                            FontSize = 11, Foreground = Brush("#F0D060"), TextWrapping = TextWrapping.Wrap
                        }
                    });
                    foreach (var (title, price) in new[] { ("60 元宝", "¥6"), ("300 元宝", "¥30"), ("980 元宝", "¥98"), ("1980 元宝", "¥198") })
                    {
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                        row.Children.Add(new TextBlock { Text = title, FontSize = 13, Foreground = Brush("#F6E6C0"), Width = 120, VerticalAlignment = VerticalAlignment.Center });
                        row.Children.Add(new TextBlock { Text = price, FontSize = 12, Foreground = Brush("#70B0F0"), Width = 64, VerticalAlignment = VerticalAlignment.Center });
                        var pay = new Button { Content = "暂不可购", MinWidth = 88, IsEnabled = false };
                        MirChrome.StyleSys(pay);
                        row.Children.Add(pay);
                        row.Children.Add(new TextBlock
                        {
                            Text = "支付未接线", FontSize = 10, Foreground = Brush("#A09070"),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        _panelBody.Children.Add(MirChrome.ListRow(row));
                    }
                    break;
                }
                if (_mallTab == "pack")
                {
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "赞助档位 · 限购按角色 · 绑定规则见 Caption · 禁止叠加折扣倍",
                        FontSize = 11, Foreground = Brush("#F0D060"), Margin = new Avalonia.Thickness(0, 0, 0, 8)
                    });
                    var packs = MallPacks();
                    var wrap = new WrapPanel { Width = 460 };
                    foreach (var p in packs)
                    {
                        bool sel = _selectedShopItem == p.id;
                        var card = new StackPanel { Spacing = 3, Width = 200 };
                        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        head.Children.Add(new TextBlock
                        {
                            Text = p.name, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush("#F6E6C0")
                        });
                        head.Children.Add(new TextBlock
                        {
                            Text = $"限购 {p.bought}/{p.limit}", FontSize = 10, Foreground = Brush("#A09070"),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        card.Children.Add(head);
                        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                        int iconN = 1 + Math.Abs(p.id.GetHashCode() % 16);
                        foreach (var ic in p.icons)
                        {
                            var cellInner = new Grid { Width = 32, Height = 32 };
                            var bmp = MirChrome.ItemIcon(((iconN++ - 1) % 16) + 1);
                            if (bmp != null)
                                cellInner.Children.Add(new Image { Source = bmp, Width = 28, Height = 28, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                            else
                                cellInner.Children.Add(new TextBlock
                                {
                                    Text = ic, FontSize = 9, Foreground = Brush("#F6E6C0"),
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                                });
                            icons.Children.Add(new Border
                            {
                                Width = 34, Height = 34, Background = Brush("#2A1C14"),
                                BorderBrush = Brush("#8050A0"), BorderThickness = new Avalonia.Thickness(1),
                                Child = cellInner
                            });
                        }
                        card.Children.Add(icons);
                        card.Children.Add(new TextBlock
                        {
                            Text = p.loot, FontSize = 10, Foreground = Brush("#C8B48A"), TextWrapping = TextWrapping.Wrap
                        });
                        // OL-17-B: purple-frame bind Caption
                        card.Children.Add(new Border
                        {
                            Background = Brush("#33181028"), BorderBrush = Brush("#8050A0"),
                            BorderThickness = new Avalonia.Thickness(1), Padding = new Avalonia.Thickness(4, 2),
                            Margin = new Avalonia.Thickness(0, 2, 0, 0),
                            Child = new TextBlock
                            {
                                Text = p.bind, FontSize = 10, Foreground = Brush("#C070F0"), TextWrapping = TextWrapping.Wrap
                            }
                        });
                        var buyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 4, 0, 0) };
                        buyRow.Children.Add(new TextBlock
                        {
                            Text = p.price, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872"),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        string pid = p.id;
                        var pick = new Button { Content = "选购", MinWidth = 64, IsEnabled = !sel };
                        if (sel) MirChrome.StyleSys(pick); else MirChrome.StyleNormal(pick);
                        pick.Click += (_, _) => { _selectedShopItem = pid; _panelFp = ""; RefreshPhaseL(); };
                        var buyNow = new Button
                        {
                            Content = "通道关闭", MinWidth = 72, IsEnabled = false,
                            Opacity = 0.55, Background = Brush("#3314100C"), Foreground = Brush("#9E9E9E"),
                            BorderBrush = Brush("#5A4030"), BorderThickness = new Avalonia.Thickness(1)
                        };
                        ToolTip.SetTip(buyNow, "试玩服商城不接线支付 · 元宝购买关闭");
                        buyRow.Children.Add(pick);
                        buyRow.Children.Add(buyNow);
                        if (sel)
                            buyRow.Children.Add(new TextBlock
                            {
                                Text = "已选 · 支付关闭", FontSize = 10, Foreground = Brush("#A09070"),
                                VerticalAlignment = VerticalAlignment.Center
                            });
                        card.Children.Add(buyRow);
                        var border = MirChrome.ListRow(card, sel);
                        border.Width = 214;
                        border.Margin = new Avalonia.Thickness(0, 0, 6, 6);
                        if (sel)
                        {
                            border.BorderBrush = Brush("#8050A0");
                            border.BorderThickness = new Avalonia.Thickness(1);
                        }
                        border.PointerPressed += (_, _) => { _selectedShopItem = pid; _panelFp = ""; RefreshPhaseL(); };
                        wrap.Children.Add(border);
                    }
                    _panelBody.Children.Add(wrap);
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "选中档位后可预览绑定规则 · 购买通道关闭（试玩服）· 充值见「充值」Tab",
                        FontSize = 10, Foreground = Brush("#A09070"), Margin = new Avalonia.Thickness(0, 4, 0, 6)
                    });
                }
                else
                {
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "热销角标 · 限时标签示意 · 元宝购买通道关闭（试玩服）",
                        FontSize = 10, Foreground = Brush("#A09070"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                    });
                    var offers = MallOffers(_mallTab);
                    var grid = new WrapPanel { Width = 440 };
                    string? selected = _selectedShopItem;
                    foreach (var (id, name, price, tag) in offers)
                    {
                        bool sel = selected == id;
                        var cell = new StackPanel { Spacing = 2, Width = 100 };
                        cell.Children.Add(MirChrome.SlotCell(new TextBlock
                        {
                            Text = Short(name), FontSize = 9, Foreground = Brush("#F6E6C0"),
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                        }, 40));
                        cell.Children.Add(new TextBlock { Text = name, FontSize = 11, Foreground = Brush("#F6E6C0"), TextWrapping = TextWrapping.Wrap });
                        cell.Children.Add(new TextBlock { Text = price, FontSize = 10, Foreground = Brush("#E8C872") });
                        if (tag.Length > 0)
                            cell.Children.Add(new TextBlock { Text = tag, FontSize = 10, Foreground = Brush("#F0D060") });
                        string itemId = id;
                        var border = MirChrome.ListRow(cell, sel);
                        border.Width = 108;
                        border.PointerPressed += (_, _) => { _selectedShopItem = itemId; _panelFp = ""; RefreshPhaseL(); };
                        grid.Children.Add(border);
                    }
                    _panelBody.Children.Add(grid);
                }
                bool hasSel = !string.IsNullOrEmpty(_selectedShopItem) && _selectedShopItem.StartsWith("mall:", StringComparison.Ordinal);
                var buyFoot = new Button
                {
                    Content = "购买选中 · 通道关闭", MinWidth = 140, IsEnabled = false,
                    Margin = new Avalonia.Thickness(0, 4, 0, 0),
                    Opacity = 0.55, Background = Brush("#3314100C"), Foreground = Brush("#9E9E9E"),
                    BorderBrush = Brush("#5A4030"), BorderThickness = new Avalonia.Thickness(1)
                };
                ToolTip.SetTip(buyFoot, "试玩服不接线元宝支付 · 按钮保持禁用");
                var recharge = new Button { Content = "充值", MinWidth = 72 };
                MirChrome.StyleNormal(recharge);
                recharge.Click += (_, _) => { _mallTab = "pay"; _panelFp = ""; RefreshPhaseL(); };
                var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 4, 0, 0) };
                foot.Children.Add(buyFoot); foot.Children.Add(recharge);
                foot.Children.Add(new TextBlock
                {
                    Text = hasSel ? "已选商品 · 支付通道关闭" : "点选商品预览 · 支付通道关闭",
                    FontSize = 10, Foreground = Brush("#A09070"), VerticalAlignment = VerticalAlignment.Center
                });
                _panelBody.Children.Add(foot);
                break;
            }
            case "hero":
            {
                _panelTitle.Text = "英雄";
                var pet = _session.Pets?.Pets.FirstOrDefault(p => p.Active) ?? _session.Pets?.Pets.FirstOrDefault(p => p.Owned);
                string heroName = pet?.DisplayName ?? "战将 · 战士";
                bool outNow = pet?.Active == true;
                _panelBody.Children.Add(new TextBlock { Text = heroName, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
                _panelBody.Children.Add(new TextBlock
                {
                    Text = outNow ? "状态：跟随 · 合击就绪" : "状态：休息中",
                    FontSize = 12, Foreground = Brush("#F6E6C0"), Margin = new Avalonia.Thickness(0, 2, 0, 8)
                });
                int hp = _session.World.Self?.Hp ?? 0;
                int maxHp = Math.Max(1, _session.World.Self?.MaxHp ?? 1);
                _panelBody.Children.Add(new TextBlock { Text = $"HP  {Math.Max(1, hp * 9 / 10)}/{Math.Max(1, maxHp * 9 / 10)}（示意）", FontSize = 12, Foreground = Brush("#F6A0A0") });
                _panelBody.Children.Add(MirChrome.ThinBar("#C060A0", hp * 0.9, maxHp * 0.9));
                _panelBody.Children.Add(new TextBlock { Text = $"MP  {_session.Mp * 2 / 3}/{Math.Max(1, _session.MaxMp * 2 / 3)}（示意）", FontSize = 12, Foreground = Brush("#70D0F0"), Margin = new Avalonia.Thickness(0, 6, 0, 0) });
                _panelBody.Children.Add(MirChrome.ThinBar("#40A0C0", _session.Mp * 2 / 3.0, Math.Max(1, _session.MaxMp * 2 / 3.0)));
                _panelBody.Children.Add(new TextBlock
                {
                    Text = $"攻击 {_session.Atk}-{_session.Atk + 30}   防御 {_session.Def}-{_session.Def + 15}",
                    FontSize = 12, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 8, 0, 8)
                });
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var summon = new Button { Content = outNow ? "休息" : "召唤", MinWidth = 72 };
                MirChrome.StylePrimary(summon);
                summon.Click += async (_, _) =>
                {
                    if (pet == null) { Toast("先在外观解锁宝宝/英雄契约"); return; }
                    Toast(outNow
                        ? await _session.DismissPetAsync() ?? "已休息"
                        : await _session.SummonPetAsync(pet.PetId) ?? "已召唤");
                    await _session.RefreshLooksAsync();
                    _panelFp = "";
                };
                bool comboReady = outNow && !string.IsNullOrEmpty(_session.World.SelectedId);
                var combo = new Button { Content = comboReady ? "合击 Ctrl+S" : "合击", MinWidth = 96 };
                if (comboReady) MirChrome.StylePrimary(combo); else MirChrome.StyleNormal(combo);
                combo.Click += (_, _) => TryHeroCombo();
                var bag = new Button { Content = "包裹", MinWidth = 72 };
                MirChrome.StyleSys(bag);
                bag.Click += (_, _) => { _bagOpen = true; Toast("英雄包裹占位"); };
                var looks = new Button { Content = "宝宝", MinWidth = 72 };
                MirChrome.StyleSys(looks);
                looks.Click += (_, _) => { _looksTab = "pets"; ShowPanel("looks"); };
                actions.Children.Add(summon); actions.Children.Add(combo); actions.Children.Add(bag); actions.Children.Add(looks);
                _panelBody.Children.Add(actions);
                _panelBody.Children.Add(new TextBlock
                {
                    Text = comboReady
                        ? "合击就绪：对当前选中目标施放 hero_combo（现有 Attack · 无新 MsgId）"
                        : (outNow ? "先点选怪物/目标，再合击（Ctrl+S）" : "先召唤英雄，再选目标合击"),
                    FontSize = 11, Foreground = Brush(comboReady ? "#70D070" : "#E8C872"),
                    Margin = new Avalonia.Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap
                });
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "Ctrl+E 面板 · Ctrl+S 合击 · 护卫/锁定为示意键位",
                    FontSize = 10, Foreground = Brush("#A09070"), Margin = new Avalonia.Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
                });
                break;
            }

            case "mail":
            {
                // Authority: OL-17-BF (list+read+attach). Entry chrome OL-07-H. Never OL-14-*.
                _panelTitle.Text = "邮件";
                var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 6) };
                foreach (var (id, label) in new[] { ("inbox", "收件"), ("sent", "发件"), ("trash", "已删") })
                {
                    string tabId = id;
                    var tab = MirChrome.MakeTab(label, _mailTab == tabId);
                    tab.Click += (_, _) => { _mailTab = tabId; _panelFp = ""; RefreshPhaseL(); };
                    tabs.Children.Add(tab);
                }
                _panelBody.Children.Add(tabs);
                int cap = _session.MailCapacity > 0 ? _session.MailCapacity : 100;
                int unread = _session.Mails.Count(m => !m.IsRead);
                _panelBody.Children.Add(new TextBlock
                {
                    Text = $"邮箱 {_session.Mails.Count}/{cap}  ·  未读 {unread}  ·  附件负重计入人物负重  ·  Alt+M",
                    FontSize = 11, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                });

                if (_mailTab == "sent")
                {
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = "发件箱 · 撰写新邮件（发出后对方收件可见）",
                        FontSize = 11, Foreground = Brush("#A09070"), Margin = new Avalonia.Thickness(0, 0, 0, 6)
                    });
                    var toBox = new TextBox { Watermark = "收件人角色名" };
                    var subBox = new TextBox { Watermark = "标题" };
                    var bodyBox = new TextBox { Watermark = "正文", AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap };
                    var goldBox = new TextBox { Watermark = "附件金币（可选）", Text = "0", Width = 140 };
                    MirChrome.StyleField(toBox); MirChrome.StyleField(subBox); MirChrome.StyleField(bodyBox); MirChrome.StyleField(goldBox);
                    _panelBody.Children.Add(toBox);
                    _panelBody.Children.Add(subBox);
                    _panelBody.Children.Add(bodyBox);
                    _panelBody.Children.Add(goldBox);
                    var sendRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
                    var send = new Button { Content = "发送", MinWidth = 88 };
                    MirChrome.StylePrimary(send);
                    send.Click += async (_, _) =>
                    {
                        int.TryParse(goldBox.Text, out int g);
                        Toast(await SafeMsg(() => _session.SendMailAsync(toBox.Text ?? "", subBox.Text ?? "", bodyBox.Text ?? "", Math.Max(0, g))) ?? "已发送");
                        _mailTab = "inbox";
                        _panelFp = "";
                    };
                    var back = new Button { Content = "返回收件", MinWidth = 88 };
                    MirChrome.StyleNormal(back);
                    back.Click += (_, _) => { _mailTab = "inbox"; _panelFp = ""; RefreshPhaseL(); };
                    sendRow.Children.Add(send); sendRow.Children.Add(back);
                    _panelBody.Children.Add(sendRow);
                    break;
                }

                if (_mailTab == "trash")
                {
                    if (_mailTrash.Count == 0)
                        _panelBody.Children.Add(MirChrome.ListRow(Block("已删箱为空（本地会话缓存，未升协议）。")));
                    foreach (var mail in _mailTrash)
                    {
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        row.Children.Add(new TextBlock
                        {
                            Text = $"{mail.FromName}：{(string.IsNullOrWhiteSpace(mail.Subject) ? "（无标题）" : mail.Subject)}",
                            FontSize = 12, Foreground = Brush("#9E9E9E"), Width = 360,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                        _panelBody.Children.Add(MirChrome.ListRow(row));
                    }
                    break;
                }

                var split = new Grid { ColumnDefinitions = new ColumnDefinitions("240,12,*"), MinHeight = 280 };
                var leftCol = new StackPanel { Spacing = 2 };
                var rightCol = new StackPanel { Spacing = 4 };
                Grid.SetColumn(leftCol, 0);
                Grid.SetColumn(rightCol, 2);
                split.Children.Add(leftCol);
                split.Children.Add(rightCol);

                if (_session.Mails.Count == 0)
                    leftCol.Children.Add(MirChrome.ListRow(Block("信箱空空如也。")));
                foreach (var mail in _session.Mails)
                {
                    string mid = mail.MailId;
                    bool sel = mid == _selectedMailId || (_session.SelectedMail?.MailId == mid);
                    var rowInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                    rowInner.Children.Add(new TextBlock
                    {
                        Text = mail.IsRead ? "○" : "●",
                        FontSize = 12,
                        Foreground = Brush(mail.IsRead ? "#6A6050" : "#E05050"),
                        Width = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    var col = new StackPanel { Spacing = 1, Width = 150 };
                    string subj = string.IsNullOrWhiteSpace(mail.Subject) ? "（无标题）" : mail.Subject;
                    col.Children.Add(new TextBlock
                    {
                        Text = $"{mail.FromName}：{subj}",
                        FontSize = 12,
                        FontWeight = mail.IsRead ? FontWeight.Normal : FontWeight.Bold,
                        Foreground = Brush("#F6E6C0"),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    string attachHint = "";
                    if (mail.HasUnclaimedAttachment)
                    {
                        var bits = new List<string>();
                        if (mail.Attachments?.Gold > 0) bits.Add($"{mail.Attachments.Gold:N0}金");
                        foreach (var it in mail.Attachments?.Items ?? Enumerable.Empty<MailAttachmentItem>())
                            bits.Add($"{ContentCatalog.ItemName(it.ItemId)}×{Math.Max(1, it.Count)}");
                        attachHint = bits.Count > 0 ? string.Join("、", bits) : "有附件";
                    }
                    if (!string.IsNullOrEmpty(attachHint))
                    {
                        col.Children.Add(new TextBlock
                        {
                            Text = attachHint,
                            FontSize = 10,
                            Foreground = Brush("#F0D060"),
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                    }
                    rowInner.Children.Add(col);
                    // OL-17-BF: independent date column on the right
                    rowInner.Children.Add(new TextBlock
                    {
                        Text = FormatMailWhen(mail.CreatedAtUnixMs),
                        FontSize = 10, Foreground = Brush("#A09070"), Width = 56,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right
                    });
                    var row = MirChrome.ListRow(rowInner, sel);
                    row.PointerPressed += async (_, __) =>
                    {
                        _selectedMailId = mid;
                        await Safe(() => _session.ReadMailAsync(mid));
                        _panelFp = "";
                        RefreshPhaseL();
                    };
                    leftCol.Children.Add(row);
                }

                var listFoot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
                var allRead = new Button { Content = "全部已读", MinWidth = 88 };
                MirChrome.StylePrimary(allRead);
                allRead.Click += async (_, _) =>
                {
                    foreach (var m in _session.Mails.Where(x => !x.IsRead).ToList())
                        await Safe(() => _session.ReadMailAsync(m.MailId));
                    Toast("已全部标为已读");
                    _panelFp = "";
                    RefreshPhaseL();
                };
                var clearRead = new Button { Content = "清空已读", MinWidth = 88 };
                MirChrome.StyleNormal(clearRead);
                clearRead.Click += async (_, _) =>
                {
                    int n = 0;
                    foreach (var m in _session.Mails.Where(x => x.IsRead && !x.HasUnclaimedAttachment).ToList())
                    {
                        _mailTrash.Insert(0, m);
                        await Safe(() => _session.DeleteMailAsync(m.MailId));
                        n++;
                    }
                    Toast(n > 0 ? $"已清空 {n} 封已读" : "没有可清空的已读信");
                    _panelFp = "";
                    RefreshPhaseL();
                };
                listFoot.Children.Add(allRead);
                listFoot.Children.Add(clearRead);
                leftCol.Children.Add(listFoot);

                var detail = _session.SelectedMail ?? _session.Mails.FirstOrDefault(m => m.MailId == _selectedMailId);
                if (detail == null)
                {
                    rightCol.Children.Add(new TextBlock
                    {
                        Text = "点左侧信件阅读。入口：枢纽→邮件 / 功能簇红点。",
                        FontSize = 12, Foreground = Brush("#A09070"), TextWrapping = TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 24, 0, 0)
                    });
                }
                else
                {
                    string dSubj = string.IsNullOrWhiteSpace(detail.Subject) ? "（无标题）" : detail.Subject;
                    rightCol.Children.Add(new TextBlock
                    {
                        Text = dSubj, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872"),
                        TextWrapping = TextWrapping.Wrap
                    });
                    rightCol.Children.Add(new TextBlock
                    {
                        Text = $"发件：{detail.FromName}  ·  {FormatMailWhen(detail.CreatedAtUnixMs)}  ·  {(detail.IsRead ? "已读" : "未读")}",
                        FontSize = 11, Foreground = Brush("#C8B48A"), Margin = new Avalonia.Thickness(0, 2, 0, 6)
                    });
                    rightCol.Children.Add(new Border
                    {
                        Background = Brush("#4414100C"),
                        BorderBrush = Brush("#5A4030"),
                        BorderThickness = new Avalonia.Thickness(1),
                        Padding = new Avalonia.Thickness(8, 6),
                        Child = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(detail.Body) ? "（无正文）" : detail.Body,
                            FontSize = 12, Foreground = Brush("#F6E6C0"), TextWrapping = TextWrapping.Wrap,
                            MinHeight = 72
                        }
                    });

                    var attachBox = new WrapPanel { Margin = new Avalonia.Thickness(0, 8, 0, 4) };
                    bool anyAttach = false;
                    if (detail.Attachments != null)
                    {
                        if (detail.Attachments.Gold > 0)
                        {
                            anyAttach = true;
                            attachBox.Children.Add(MailAttachSlot($"{detail.Attachments.Gold:N0} 金", detail.Claimed, 1, bound: false));
                        }
                        int icon = 1;
                        foreach (var it in detail.Attachments.Items)
                        {
                            anyAttach = true;
                            // local bind heuristic: potions/scrolls bound; others unbound (protocol has no Bound flag)
                            bool bound = it.ItemId.Contains("potion", StringComparison.OrdinalIgnoreCase)
                                || it.ItemId.Contains("scroll", StringComparison.OrdinalIgnoreCase)
                                || it.ItemId.Contains("bind", StringComparison.OrdinalIgnoreCase);
                            attachBox.Children.Add(MailAttachSlot(
                                $"{ContentCatalog.ItemName(it.ItemId)} ×{Math.Max(1, it.Count)}",
                                detail.Claimed,
                                icon++,
                                bound));
                        }
                    }
                    if (anyAttach)
                    {
                        rightCol.Children.Add(new TextBlock
                        {
                            Text = "附件", FontSize = 11, Foreground = Brush("#E8C872"),
                            Margin = new Avalonia.Thickness(0, 4, 0, 2)
                        });
                        rightCol.Children.Add(new Border
                        {
                            Background = Brush("#6614100C"),
                            BorderBrush = Brush("#5A4030"),
                            BorderThickness = new Avalonia.Thickness(1),
                            Padding = new Avalonia.Thickness(6),
                            Child = attachBox
                        });
                    }

                    string mailId = detail.MailId;
                    var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 10, 0, 0) };
                    var claim = new Button { Content = "提取附件", MinWidth = 96, IsEnabled = detail.HasUnclaimedAttachment };
                    MirChrome.StylePrimary(claim);
                    claim.Click += async (_, _) => { Toast(await SafeMsg(() => _session.ClaimMailAsync(mailId)) ?? "已提取"); _panelFp = ""; };
                    var del = new Button { Content = "删除", MinWidth = 72 };
                    MirChrome.StyleDanger(del);
                    del.Click += async (_, _) =>
                    {
                        _mailTrash.Insert(0, detail);
                        Toast(await SafeMsg(() => _session.DeleteMailAsync(mailId)) ?? "已删除");
                        _selectedMailId = "";
                        _panelFp = "";
                    };
                    var reply = new Button { Content = "回复", MinWidth = 72, IsEnabled = false };
                    MirChrome.StyleNormal(reply);
                    acts.Children.Add(claim); acts.Children.Add(del); acts.Children.Add(reply);
                    rightCol.Children.Add(acts);
                    rightCol.Children.Add(new TextBlock
                    {
                        Text = "负重充足可一键提取",
                        FontSize = 10, Foreground = Brush("#A09070"),
                        Margin = new Avalonia.Thickness(0, 4, 0, 0)
                    });
                    rightCol.Children.Add(new TextBlock
                    {
                        Text = "入口：枢纽 → 邮件 / 功能簇红点",
                        FontSize = 10, Foreground = Brush("#A09070"), Margin = new Avalonia.Thickness(0, 8, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                _panelBody.Children.Add(split);
                break;
            }

            case "assist":
            {
                // Authority: OL-16-K deep tabs + risk gate. Running chrome OL-16-N. Never OL-14-*.
                _panelTitle.Text = "内挂设置";
                var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 0, 0, 4) };
                head.Children.Add(new TextBlock
                {
                    Text = "扩展 · Feature 默认开", FontSize = 10, Foreground = Brush("#70D0A8"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                head.Children.Add(new TextBlock
                {
                    Text = "F12", FontSize = 10, Foreground = Brush("#A09070"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                _panelBody.Children.Add(head);

                var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 6) };
                foreach (var (id, label) in new[] { ("fight", "打怪"), ("cast", "施法"), ("pickup", "拾取"), ("protect", "保护"), ("about", "关于") })
                {
                    string tabId = id;
                    var tab = MirChrome.MakeTab(label, _assistTab == tabId);
                    tab.Click += (_, _) => { _assistTab = tabId; _panelFp = ""; RefreshPhaseL(); };
                    tabs.Children.Add(tab);
                }
                _panelBody.Children.Add(tabs);

                var cfg = _session.Assist.Settings;
                bool running = _session.Assist.Running;
                cfg.Pickup.Enabled = cfg.Pickup.Enabled; // keep
                // Sync guard default into settings when toggled on
                bool hasGuard = cfg.DoNotAttackEntityKinds.Any(k => string.Equals(k, "guard", StringComparison.OrdinalIgnoreCase));
                if (_assistNoAttackGuard && !hasGuard)
                    cfg.DoNotAttackEntityKinds.Add("guard");
                if (!_assistNoAttackGuard && hasGuard)
                    cfg.DoNotAttackEntityKinds.RemoveAll(k => string.Equals(k, "guard", StringComparison.OrdinalIgnoreCase));

                _panelBody.Children.Add(new Border
                {
                    Background = Brush("#661A1008"),
                    BorderBrush = Brush("#6A3020"),
                    BorderThickness = new Avalonia.Thickness(1),
                    Padding = new Avalonia.Thickness(8, 4),
                    Margin = new Avalonia.Thickness(0, 0, 0, 8),
                    Child = new TextBlock
                    {
                        Text = running
                            ? "当前：挂机中　·　规则可配　·　禁止霓虹自动战斗条"
                            : "当前：未挂机　·　规则可配，启动须风险门闩　·　禁止霓虹自动战斗条",
                        FontSize = 11, Foreground = Brush(running ? "#70D070" : "#E8C872"), TextWrapping = TextWrapping.Wrap
                    }
                });

                void AddCheck(StackPanel host, string label, bool on, bool showOffTag, Action<bool> set)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(0, 2, 0, 2) };
                    var cb = new CheckBox
                    {
                        IsChecked = on,
                        Content = showOffTag && !on ? $"{label}（关）" : label,
                        Foreground = Brush("#F6E6C0"),
                        FontSize = 12
                    };
                    cb.IsCheckedChanged += (_, _) =>
                    {
                        bool v = cb.IsChecked == true;
                        if (v == on) return;
                        set(v);
                        _panelFp = "";
                        RefreshPhaseL();
                    };
                    row.Children.Add(cb);
                    host.Children.Add(row);
                }

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,12,*"),
                    RowDefinitions = new RowDefinitions("Auto,8,Auto")
                };
                Border Sec(string title, Action<StackPanel> fill)
                {
                    var body = new StackPanel { Spacing = 2 };
                    body.Children.Add(new TextBlock
                    {
                        Text = title, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872"),
                        Margin = new Avalonia.Thickness(0, 0, 0, 4)
                    });
                    fill(body);
                    return new Border
                    {
                        Background = Brush("#5514100C"),
                        BorderBrush = Brush("#5A4030"),
                        BorderThickness = new Avalonia.Thickness(1),
                        Padding = new Avalonia.Thickness(8, 6),
                        Child = body
                    };
                }

                var fight = Sec("打怪", p =>
                {
                    AddCheck(p, "自动打怪", running, true, v =>
                    {
                        if (v)
                        {
                            if (!_assistArmed) { Toast("先勾选「我已阅读风险」"); return; }
                            if (!_session.TryStartAssist(out string reason))
                                Toast(string.IsNullOrEmpty(reason) ? "启动失败" : ("启动失败：" + reason));
                            else Toast("内挂已启动");
                        }
                        else
                        {
                            _session.StopAssist("ui");
                            Toast("内挂已停止");
                        }
                    });
                    AddCheck(p, "优先近距怪", _assistNearFirst, true, v =>
                    {
                        _assistNearFirst = v;
                        cfg.SearchRangeCells = v ? 4 : 8;
                    });
                    AddCheck(p, "不攻击守卫", _assistNoAttackGuard, false, v => _assistNoAttackGuard = v);
                });
                var cast = Sec("施法", p =>
                {
                    AddCheck(p, "技能循环", _assistSkillLoop, true, v =>
                    {
                        _assistSkillLoop = v;
                        EnsureAssistLocalTables(cfg);
                        foreach (var s in cfg.Skills) s.Enabled = v;
                    });
                    AddCheck(p, "合击就绪才放", _assistComboReadyOnly, true, v => _assistComboReadyOnly = v);
                });
                var pick = Sec("拾取", p =>
                {
                    AddCheck(p, "自动拾取", cfg.Pickup.Enabled, true, v => cfg.Pickup.Enabled = v);
                    AddCheck(p, "品质≥蓝", _assistQualityBlue, true, v => _assistQualityBlue = v);
                    AddCheck(p, "满包停止捡", _assistStopFullBag, false, v => _assistStopFullBag = v);
                });
                var prot = Sec("保护", p =>
                {
                    bool hpOn = cfg.HpPotions.Any(x => x.HpPercent > 0 && x.HpPercent <= 40.5);
                    AddCheck(p, "HP≤40% 喝药", hpOn, true, v =>
                    {
                        if (v)
                        {
                            cfg.HpPotions.Clear();
                            cfg.MpPotions.Clear();
                            foreach (var pot in AssistPotionCatalog())
                            {
                                if (pot.HpPercent > 0) cfg.HpPotions.Add(pot);
                                else cfg.MpPotions.Add(pot);
                            }
                        }
                        else
                        {
                            cfg.HpPotions.Clear();
                            cfg.MpPotions.Clear();
                        }
                    });
                    AddCheck(p, "被玩家攻击暂停", _assistPauseOnPvp, false, v => _assistPauseOnPvp = v);
                    AddCheck(p, "安全区停打怪", _assistStopInSafe, false, v => _assistStopInSafe = v);
                });

                if (_assistTab is "fight" or "cast" or "pickup" or "protect")
                {
                    Grid.SetColumn(fight, 0); Grid.SetRow(fight, 0);
                    Grid.SetColumn(cast, 2); Grid.SetRow(cast, 0);
                    Grid.SetColumn(pick, 0); Grid.SetRow(pick, 2);
                    Grid.SetColumn(prot, 2); Grid.SetRow(prot, 2);
                    grid.Children.Add(fight); grid.Children.Add(cast); grid.Children.Add(pick); grid.Children.Add(prot);
                    _panelBody.Children.Add(grid);
                    EnsureAssistLocalTables(cfg);
                    string tip = _assistTab switch
                    {
                        "fight" => $"寻敌 {cfg.SearchRangeCells} 格 · 拉怪上限 {cfg.MaxPullCount} · 间隔 {cfg.GlobalCastGapMs}ms",
                        "cast" => $"技能表 {cfg.Skills.Count} 条 · 合击走既有 hero_combo（Ctrl+S）",
                        "pickup" => $"范围 {cfg.Pickup.PickupRangeCells} · 延迟 {cfg.Pickup.DelayMs}ms · 禁拾取图强制停",
                        _ => $"紧急回城 HP≤{cfg.EmergencyScrollHpPercent:0}% · 禁挂图强制停"
                    };
                    _panelBody.Children.Add(new TextBlock
                    {
                        Text = tip, FontSize = 11, Foreground = Brush("#A09070"),
                        Margin = new Avalonia.Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap
                    });
                    if (_assistTab == "cast")
                    {
                        _panelBody.Children.Add(new TextBlock
                        {
                            Text = "技能表（本地配置 · 可开关）", FontSize = 12, FontWeight = FontWeight.Bold,
                            Foreground = Brush("#E8C872"), Margin = new Avalonia.Thickness(0, 2, 0, 4)
                        });
                        var skHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(4, 0, 0, 2) };
                        foreach (var (lab, w) in new[] { ("启用", 40.0), ("技能", 120.0), ("优先", 40.0), ("MP≥", 48.0), ("说明", 160.0) })
                            skHead.Children.Add(new TextBlock { Text = lab, FontSize = 10, Foreground = Brush("#A09070"), Width = w });
                        _panelBody.Children.Add(skHead);
                        foreach (var sk in cfg.Skills.OrderBy(s => s.Priority))
                        {
                            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                            var on = sk.Enabled;
                            var cb = new CheckBox { IsChecked = on, Width = 40 };
                            string sid = sk.SkillId;
                            cb.IsCheckedChanged += (_, _) =>
                            {
                                bool v = cb.IsChecked == true;
                                var hit = cfg.Skills.FirstOrDefault(x => x.SkillId == sid);
                                if (hit != null) hit.Enabled = v;
                                _assistSkillLoop = cfg.Skills.Any(x => x.Enabled);
                                _panelFp = "";
                                RefreshPhaseL();
                            };
                            row.Children.Add(cb);
                            string nm = ContentCatalog.FindSkill(sid)?.DisplayName ?? sid;
                            row.Children.Add(new TextBlock { Text = nm, FontSize = 12, Foreground = Brush("#F6E6C0"), Width = 120, VerticalAlignment = VerticalAlignment.Center });
                            row.Children.Add(new TextBlock { Text = sk.Priority.ToString(), FontSize = 11, Foreground = Brush("#E8C872"), Width = 40, VerticalAlignment = VerticalAlignment.Center });
                            row.Children.Add(new TextBlock { Text = $"{sk.MinMpPercent:0}%", FontSize = 11, Foreground = Brush("#70D0F0"), Width = 48, VerticalAlignment = VerticalAlignment.Center });
                            string note = sid == "hero_combo" ? "合击 · Ctrl+S" : (sk.MinMobCount > 1 ? $"怪≥{sk.MinMobCount}" : "单体");
                            row.Children.Add(new TextBlock { Text = note, FontSize = 10, Foreground = Brush("#A09070"), Width = 160, VerticalAlignment = VerticalAlignment.Center });
                            _panelBody.Children.Add(MirChrome.ListRow(row));
                        }
                    }
                    else if (_assistTab == "protect")
                    {
                        _panelBody.Children.Add(new TextBlock
                        {
                            Text = "药表（本地配置 · 阈值示意）", FontSize = 12, FontWeight = FontWeight.Bold,
                            Foreground = Brush("#E8C872"), Margin = new Avalonia.Thickness(0, 2, 0, 4)
                        });
                        var potHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(4, 0, 0, 2) };
                        foreach (var (lab, w) in new[] { ("类型", 40.0), ("物品", 140.0), ("阈值", 72.0), ("优先", 40.0), ("间隔", 64.0) })
                            potHead.Children.Add(new TextBlock { Text = lab, FontSize = 10, Foreground = Brush("#A09070"), Width = w });
                        _panelBody.Children.Add(potHead);
                        void AddPotRow(string kind, AssistPotionSetting pot)
                        {
                            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                            row.Children.Add(new TextBlock { Text = kind, FontSize = 11, Foreground = Brush("#E8C872"), Width = 40, VerticalAlignment = VerticalAlignment.Center });
                            row.Children.Add(new TextBlock
                            {
                                Text = ContentCatalog.ItemName(pot.ItemId), FontSize = 12, Foreground = Brush("#F6E6C0"), Width = 140,
                                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
                            });
                            string thr = kind == "HP" ? $"HP≤{pot.HpPercent:0}%" : $"MP≤{pot.MpPercent:0}%";
                            row.Children.Add(new TextBlock { Text = thr, FontSize = 11, Foreground = Brush("#F0A060"), Width = 72, VerticalAlignment = VerticalAlignment.Center });
                            row.Children.Add(new TextBlock { Text = pot.Priority.ToString(), FontSize = 11, Foreground = Brush("#C8B48A"), Width = 40, VerticalAlignment = VerticalAlignment.Center });
                            row.Children.Add(new TextBlock { Text = $"{pot.MinIntervalMs}ms", FontSize = 11, Foreground = Brush("#A09070"), Width = 64, VerticalAlignment = VerticalAlignment.Center });
                            _panelBody.Children.Add(MirChrome.ListRow(row));
                        }
                        if (cfg.HpPotions.Count == 0 && cfg.MpPotions.Count == 0)
                        {
                            _panelBody.Children.Add(new TextBlock
                            {
                                Text = "建议档（未启用 · 勾选「HP≤40% 喝药」写入本地药表）",
                                FontSize = 10, Foreground = Brush("#A09070"), Margin = new Avalonia.Thickness(0, 0, 0, 4)
                            });
                            foreach (var pot in AssistPotionCatalog())
                                AddPotRow(pot.HpPercent > 0 ? "HP" : "MP", pot);
                        }
                        else
                        {
                            foreach (var pot in cfg.HpPotions.OrderByDescending(p => p.Priority))
                                AddPotRow("HP", pot);
                            foreach (var pot in cfg.MpPotions.OrderByDescending(p => p.Priority))
                                AddPotRow("MP", pot);
                        }
                    }
                }
                else
                {
                    _panelBody.Children.Add(Block("内挂为客户端本地 FSM；挂机中见 HUD 小「挂中」标与克制浮层。"));
                    _panelBody.Children.Add(Block($"手动移动停挂 {YesNo(cfg.StopOnManualMove)} · 换图停 {YesNo(cfg.StopOnMapChange)} · 死亡停 {YesNo(cfg.StopOnDeath)}"));
                    _panelBody.Children.Add(Block("规则可配 · 启动须风险门闩 · 禁止霓虹自动战斗条"));
                }

                // OL-16-K: keep risk gate clear of four-zone bottom edge
                _panelBody.Children.Add(new Border
                {
                    Background = Brush("#55201010"),
                    BorderBrush = Brush("#A04030"),
                    BorderThickness = new Avalonia.Thickness(1),
                    Padding = new Avalonia.Thickness(8, 6),
                    Margin = new Avalonia.Thickness(0, 18, 0, 10),
                    Child = new TextBlock
                    {
                        Text = "⚠ 风险：自动战斗可能违反部分区服规则；请遵守本服公告。",
                        FontSize = 11, Foreground = Brush("#F0A060"), TextWrapping = TextWrapping.Wrap
                    }
                });

                var riskRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Avalonia.Thickness(0, 2, 0, 8) };
                var riskCb = new CheckBox
                {
                    IsChecked = _assistArmed,
                    Content = "我已阅读风险",
                    Foreground = Brush("#F6E6C0"),
                    FontSize = 12
                };
                riskCb.IsCheckedChanged += (_, _) =>
                {
                    bool v = riskCb.IsChecked == true;
                    if (v == _assistArmed) return;
                    _assistArmed = v;
                    if (!_assistArmed && running)
                    {
                        _session.StopAssist("risk_off");
                        Toast("取消确认并已停止");
                    }
                    _panelFp = "";
                    RefreshPhaseL();
                };
                riskRow.Children.Add(riskCb);
                var start = new Button
                {
                    Content = running ? "挂机中" : "开启挂机",
                    MinWidth = 100,
                    IsEnabled = !running && _assistArmed
                };
                MirChrome.StylePrimary(start);
                start.Click += (_, _) =>
                {
                    if (!_assistArmed) { Toast("先勾选「我已阅读风险」"); return; }
                    if (_session.TryStartAssist(out string reason))
                        Toast("内挂已启动");
                    else
                        Toast(string.IsNullOrEmpty(reason) ? "启动失败" : ("启动失败：" + reason));
                    _panelFp = "";
                    RefreshPhaseL();
                };
                var stop = new Button { Content = "停止挂机", MinWidth = 88, IsEnabled = running };
                MirChrome.StyleDanger(stop);
                stop.Click += (_, _) =>
                {
                    _session.StopAssist("ui");
                    Toast("内挂已停止");
                    _panelFp = "";
                    RefreshPhaseL();
                };
                riskRow.Children.Add(start);
                riskRow.Children.Add(stop);
                _panelBody.Children.Add(riskRow);
                break;
            }

            case "names":
            {
                _panelTitle.Text = "名色阶对照";
                string self = _session.NameColor switch
                {
                    "red" => "红名",
                    "yellow" => "黄名",
                    "gray" or "grey" => "灰名",
                    _ => "白名"
                };
                _panelBody.Children.Add(new TextBlock
                {
                    Text = $"当前：{self}  ·  世界名板已按色显示",
                    FontSize = 12, Foreground = Brush("#E8C872"), Margin = new Avalonia.Thickness(0, 0, 0, 8)
                });
                foreach (var (label, hex, tip) in new[]
                {
                    ("白名", "#FFFFFF", "正常"),
                    ("灰名", "#9E9E9E", "临时 PK"),
                    ("黄名", "#FDD835", "有 PK 值"),
                    ("红名", "#FF1744", "高 PK · 进城高风险 · 守卫敌对")
                })
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                    row.Children.Add(new TextBlock { Text = "■", FontSize = 16, Foreground = Brush(hex), Width = 24 });
                    row.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush(hex), Width = 48 });
                    row.Children.Add(new TextBlock { Text = tip, FontSize = 12, Foreground = Brush("#F6E6C0"), VerticalAlignment = VerticalAlignment.Center });
                    _panelBody.Children.Add(MirChrome.ListRow(row));
                }
                _panelBody.Children.Add(new TextBlock
                {
                    Text = "杀白名→灰；累计 PK→黄→红。红名掉落率上升。宝宝名色勿与玩家混淆。",
                    FontSize = 11, Foreground = Brush("#C8B48A"), TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 8, 0, 0)
                });
                break;
            }

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
        if (e.Key is Key.O or Key.F12)
        {
            ShowPanel("settings");
            e.Handled = true;
            return true;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.W)
        {
            ShowPanel("party");
            e.Handled = true;
            return true;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.E)
        {
            ShowPanel("hero");
            e.Handled = true;
            return true;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S)
        {
            TryHeroCombo();
            e.Handled = true;
            return true;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.M)
        {
            ShowPanel("mail");
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Escape && _ctxHost.IsVisible)
        {
            HidePlayerContext();
            e.Handled = true;
            return true;
        }
        return false;
    }

    private static string YesNo(bool v) => v ? "是" : "否";

    private async Task<string?> SafeMsg(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    private void TryHeroCombo()
    {
        var pet = _session.Pets?.Pets.FirstOrDefault(p => p.Active);
        if (pet == null)
        {
            Toast("先召唤英雄/宝宝");
            return;
        }
        string target = _session.World.SelectedId ?? "";
        if (string.IsNullOrEmpty(target))
            target = SelectedMonsterOrNearest() ?? "";
        if (string.IsNullOrEmpty(target))
        {
            Toast("先点选合击目标");
            return;
        }
        _ = Attack(target, "hero_combo");
        Toast("合击 hero_combo");
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
            HidePlayerContext();
            Toast("没有可查看的目标");
            return;
        }
        string title = string.IsNullOrWhiteSpace(actor.Title) ? "" : actor.Title + " ";
        Toast($"{title}{actor.Name}  {actor.Kind}  HP {actor.Hp}/{actor.MaxHp}");
        if (actor.Kind == EntityKind.EntityPlayer && !actor.IsSelf)
            ShowPlayerContext(actor.Name);
        else
            HidePlayerContext();
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
        string stateColor = active ? "#70D070" : owned ? "#E8C872" : "#9E9E9E";
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = label, FontSize = 12, Foreground = Brush("#F6E6C0"), Width = 220,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        });
        row.Children.Add(new TextBlock
        {
            Text = state, FontSize = 11, Foreground = Brush(stateColor), Width = 56,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (owned && !active)
        {
            var button = new Button { Content = onText, MinWidth = 64 };
            MirChrome.StylePrimary(button);
            button.Click += async (_, _) => Toast(await on() ?? "完成");
            row.Children.Add(button);
        }
        else if (active)
        {
            var button = new Button { Content = offText, MinWidth = 64 };
            MirChrome.StyleDanger(button);
            button.Click += async (_, _) => Toast(await off() ?? "完成");
            row.Children.Add(button);
        }
        else
        {
            string itemId = ContentCatalog.UnlockItemFor(appearanceId);
            bool inBag = itemId.Length > 0 && _session.Inventory.Items.Any(i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
            if (inBag)
            {
                var unlock = new Button { Content = "解锁", MinWidth = 64 };
                MirChrome.StyleNormal(unlock);
                unlock.Click += async (_, _) =>
                {
                    Toast(await _session.UseItemAsync(itemId) ?? "已解锁");
                    await _session.RefreshLooksAsync();
                };
                row.Children.Add(unlock);
            }
        }
        _panelBody.Children.Add(MirChrome.ListRow(row));
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

    private void RefreshQuestTracker()
    {
        if (_questTrackerBody == null) return;
        _questTrackerBody.Children.Clear();
        bool inWorld = _session.Fsm.Phase == SessionPhase.InWorld;
        var quests = _session.Quests.Take(5).ToList();
        _questTracker.IsVisible = inWorld && quests.Count > 0;
        foreach (var quest in quests)
        {
            string badge = quest.Status == QuestStatus.QuestReady ? "可交付" : $"{quest.Progress}/{quest.TargetCount}";
            var line = new TextBlock
            {
                Text = $"· {quest.Title}  {badge}",
                FontSize = 11,
                Foreground = Brush(quest.Status == QuestStatus.QuestReady ? "#70D070" : "#F6E6C0"),
                TextWrapping = TextWrapping.Wrap
            };
            _questTrackerBody.Children.Add(line);
        }
    }

    private void RefreshSiegeBanner()
    {
        if (_siegeBanner == null) return;
        bool show = _session.Fsm.Phase == SessionPhase.InWorld && !string.IsNullOrWhiteSpace(_session.SiegeNote);
        _siegeBanner.IsVisible = show;
        if (show)
            _siegeBannerText.Text = "⚔ " + _session.SiegeNote;
    }

    private void UpdateZoneBanner(string text, bool safe)
    {
        if (_zoneBanner == null) return;
        _zoneBannerText.Text = text;
        _zoneBannerText.Foreground = Brush(safe ? "#70D070" : "#FDD835");
        _zoneBanner.IsVisible = _session.Fsm.Phase == SessionPhase.InWorld;
    }

    private void ShowPlayerContext(string name)
    {
        _ctxName = name;
        _ctxBody.Children.Clear();
        void Item(string label, Action action, bool danger = false)
        {
            var btn = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
            if (danger) MirChrome.StyleDanger(btn); else MirChrome.StyleSys(btn);
            btn.Click += (_, _) => { action(); HidePlayerContext(); };
            _ctxBody.Children.Add(btn);
        }
        Item("查看装备", () => Toast($"查看 {name} 装备（示意）"));
        Item("私聊", () => { _whisperName.Text = name; _chatChannel = ChatChannel.ChatWhisper; Toast("私聊 " + name); });
        Item("加好友", () => _ = Safe(async () => Toast(await _session.AddFriendAsync(name) ?? "已添加")));
        Item("交易", () => { ShowPanel("trade"); _ = Safe(async () => Toast(await _session.TradeStartAsync(name) ?? "已发起交易")); });
        Item("组队邀请", () => _ = Safe(async () => Toast(await _session.InvitePartyAsync(name) ?? "已邀请")));
        Item("行会邀请", () => Toast("行会邀请占位：请对方加入你的行会名"));
        Item("拉黑", () => { _friendsTab = "black"; Toast("已记入黑名单占位：" + name); }, danger: true);
        Item("挑战", () => Toast("挑战暂未开放"));
        Item("复制名称", () =>
        {
            _ = Safe(async () =>
            {
                var clip = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clip != null) await clip.SetTextAsync(name);
                Toast("已复制 " + name);
            });
        });
        _ctxHost.IsVisible = true;
    }

    private void HidePlayerContext()
    {
        if (_ctxHost != null)
            _ctxHost.IsVisible = false;
        _ctxName = "";
    }

    private static (string id, string name, string price, string tag)[] MallOffers(string tab) => tab switch
    {
        "consumable" => new[]
        {
            ("mall:teleport", "传送石", "30 元宝", "惠"),
            ("mall:random", "随机传送卷 x10", "50 元宝", ""),
            ("mall:repair", "修复油 x5", "45 元宝", ""),
            ("mall:exp", "双倍经验符(2h)", "80 元宝", "")
        },
        "fashion" => new[]
        {
            ("mall:robe", "时装·青衫", "380 元宝", "绑"),
            ("mall:fashion_box", "法袍礼盒", "200 元宝", "")
        },
        "material" => new[]
        {
            ("mall:ore", "精铁矿 x20", "60 元宝", ""),
            ("mall:scroll", "武器炼制符", "200 元宝", "")
        },
        "pack" => new[]
        {
            ("mall:pack_novice", "新手礼包", "60 元宝", "惠"),
            ("mall:pack_value", "超值礼包", "300 元宝", "惠"),
            ("mall:pack_supreme", "至尊礼包", "980 元宝", "绑"),
            ("mall:pack_month", "月卡赞助", "1,280 元宝", "绑")
        },
        _ => new[]
        {
            ("mall:teleport", "传送石", "30 元宝", "惠"),
            ("mall:random", "随机传送卷 x10", "50 元宝", ""),
            ("mall:pearl", "聚灵珠(大)", "120 元宝", "绑"),
            ("mall:scroll", "武器炼制符", "200 元宝", ""),
            ("mall:robe", "时装·青衫", "380 元宝", "绑"),
            ("mall:exp", "双倍经验符(2h)", "80 元宝", ""),
            ("mall:storage", "仓库扩容+8", "150 元宝", "绑"),
            ("mall:repair", "修复油 x5", "45 元宝", "")
        }
    };

    private static (string id, string name, string price, string loot, string bind, string[] icons, int bought, int limit)[] MallPacks() =>
        new[]
        {
            ("mall:pack_novice", "新手礼包", "60 元宝", "金创×20 · 回城×5 · 经验符", "绑 · 全创项绑定", new[] { "药", "卷", "符" }, 0, 1),
            ("mall:pack_value", "超值礼包", "300 元宝", "传送石 · 聚灵珠 · 双倍符", "绑 · 道具绑 · 元宝不绑", new[] { "石", "珠", "倍" }, 2, 5),
            ("mall:pack_supreme", "至尊礼包", "980 元宝", "时装·青衫 · 炼制符 · 仓扩", "绑 · 时装绑 · 材料可交易", new[] { "装", "符", "仓" }, 0, 2),
            ("mall:pack_month", "月卡赞助", "1,280 元宝", "银元×30/日 · 30天", "绑 · 每日领银元 · 到期停", new[] { "银", "日", "月" }, 0, 1)
        };

    private static string StallCatLabel(string cat) => cat switch
    {
        "weapon" => "武器",
        "armor" => "防具",
        "acc" => "饰品",
        "mat" => "材料",
        _ => "全部"
    };

    private void ShowConfirm(string title, string body, Action onOk)
    {
        _confirmTitle.Text = title;
        _confirmBody.Text = body;
        _confirmAction = onOk;
        _confirmHost.IsVisible = true;
    }

    private void HideConfirm()
    {
        _confirmHost.IsVisible = false;
        _confirmAction = null;
    }

    private void EnsureAssistLocalTables(ClientAssistSettings cfg)
    {
        if (cfg.Skills.Count == 0)
        {
            foreach (var (id, pri, mp, mobs) in new (string, int, double, int)[]
            {
                ("basic_slash", 1, 0, 1),
                ("power_strike", 2, 8, 1),
                ("half_moon", 3, 12, 2),
                ("hero_combo", 4, 15, 1)
            })
            {
                cfg.Skills.Add(new AssistSkillSetting
                {
                    SkillId = id,
                    Enabled = _assistSkillLoop,
                    Priority = pri,
                    MinMpPercent = mp,
                    MinMobCount = mobs
                });
            }
        }
    }

    private static AssistPotionSetting[] AssistPotionCatalog() =>
        new[]
        {
            new AssistPotionSetting { ItemId = "item_hp_potion", HpPercent = 40, Priority = 2, MinIntervalMs = 800 },
            new AssistPotionSetting { ItemId = "item_dried_meat", HpPercent = 55, Priority = 1, MinIntervalMs = 1200 },
            new AssistPotionSetting { ItemId = "item_mp_potion", MpPercent = 35, Priority = 1, MinIntervalMs = 900 }
        };

}
