using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Legend.ClientCore;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Playable.Data;
using Legend.Playable.Audio;
using Legend.Playable.Input;
using Legend.Playable.Session;
using Legend.Playable.World;

namespace Legend.Playable.Ui;

public sealed partial class GameWindow : Window
{
    private readonly PlayableSession _session = new();
    private readonly PlayableAudio _audio = new();
    private readonly WorldCanvas _canvas = new();
    private readonly DispatcherTimer _timer;
    private readonly HashSet<Key> _held = new();
    private readonly LaunchOptions _options;

    private readonly Border _login;
    private readonly Border _chars;
    private readonly Grid _hud;
    private readonly Border _bag;
    private readonly Border _reconnect;
    private readonly TextBox _user;
    private readonly TextBox _pass;
    private readonly TextBox _host;
    private readonly TextBox _port;
    private readonly TextBlock _loginStatus;
    private readonly StackPanel _charList;
    private readonly TextBox _newName;
    private readonly ComboBox _classBox;
    private readonly ComboBox _genderBox;
    private readonly TextBlock _charStatus;
    private readonly TextBlock _hpText;
    private readonly TextBlock _mpText;
    private readonly ProgressBar _hpBar;
    private readonly ProgressBar _mpBar;
    private readonly ProgressBar _expBar;
    private readonly Border _death;
    private readonly TextBlock _vitals;
    private readonly TextBlock _target;
    private readonly TextBlock _hint;
    private readonly TextBlock _chatLog;
    private readonly TextBox _chatInput;
    private readonly ScrollViewer _chatScroll;
    private readonly WrapPanel _bagItems;
    private readonly TextBlock _bagGold;
    private readonly TextBlock _reconnectText;
    private readonly TextBlock _bossText = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly ProgressBar _bossHp = new() { Height = 8, Minimum = 0, Maximum = 1 };
    private readonly Border _bossHost;
    private readonly ClassPortrait _preview = new() { Width = 88, Height = 112 };
    private DateTime _nextBossPoll = DateTime.MinValue;
    private bool _bossBusy;
    private string _heardMap = "";

    private bool _bagOpen;
    private bool _moveBusy;
    private bool _pendingAttack;
    private bool _pendingPickup;
    private bool _pendingTalk;
    private string? _pendingId;
    private bool? _lastSafe;
    private int _charCount = -1;
    private string _bagFp = "";
    private int _chatCount = -1;
    private DateTime _nextMove = DateTime.MinValue;
    private DateTime _nextAttack = DateTime.MinValue;
    private DateTime _lastTick = DateTime.UtcNow;
    private bool _closed;

    public GameWindow(LaunchOptions options)
    {
        _options = options;
        _session.Configure(options);
        PhasePRules.EnsureLoaded(typeof(GameWindow).Assembly.GetManifestResourceStream("configs.profanity.txt"));
        Title = "Legend 客户端";
        Width = 1280;
        Height = 760;
        MinWidth = 960;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#120E0C"));

        _user = Field(string.IsNullOrWhiteSpace(options.Username) ? "" : options.Username, "账号");
        _pass = Field(options.Password, "密码");
        _pass.PasswordChar = '●';
        _host = Field(options.Host, "主机");
        _port = Field(options.Port.ToString(), "端口");
        _loginStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#F0D8A8") };
        _login = BuildLogin();

        _charList = new StackPanel { Spacing = 6 };
        _newName = Field("", "角色名");
        _classBox = new ComboBox { MinWidth = 140 };
        foreach (var choice in ClassChoices)
            _classBox.Items.Add(choice);
        _classBox.SelectedIndex = 0;
        _genderBox = new ComboBox { MinWidth = 140 };
        _genderBox.Items.Add("男");
        _genderBox.Items.Add("女");
        _genderBox.SelectedIndex = 0;
        _charStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#F0D8A8") };
        _chars = BuildChars();
        _bossHost = BuildBossHost();

        _hpText = Label("HP");
        _mpText = Label("MP");
        _hpBar = Bar("#E01818");
        _mpBar = Bar("#1C6FE0");
        _expBar = Bar("#E0B030");
        if (MirChrome.HpTrack != null)
            _hpBar.Background = MirChrome.HpTrack;
        if (MirChrome.MpTrack != null)
            _mpBar.Background = MirChrome.MpTrack;
        _vitals = Label("");
        _target = Label("目标：无");
        _hint = Label("WASD 走 · Shift 跑 · 左键走/打/捡 · 右键跑/看 · F1–F8 技能 · F9 包 · F10 人物 · F11 技能 · Tab 地图 · Ctrl+H 模式 · Alt+X 选角");
        _hint.Opacity = 0.75;
        _hint.FontSize = 11;
        _chatLog = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brushes.White };
        _chatScroll = new ScrollViewer { Content = _chatLog, Height = 92 };
        _chatInput = Field("", "说点什么…");
        _hud = BuildHud();
        AttachPhaseL(_hud);

        _bagItems = new WrapPanel { Width = 200 };
        _bagGold = Label("");
        _bag = BuildBag();

        _reconnectText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        _reconnect = BuildReconnect();
        var deathBody = new StackPanel { Spacing = 10, Width = 360 };
        deathBody.Children.Add(new TextBlock
        {
            Text = "你已经死亡",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#FF6A6A"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        deathBody.Children.Add(new TextBlock
        {
            Text = "操作已锁定，请选择复活方式",
            FontSize = 13,
            Foreground = Brush("#F6E6C0"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        deathBody.Children.Add(new TextBlock
        {
            Text = "本次死亡可能损失部分经验\n红名或高风险地图：装备掉落概率上升\n掉落摘要：无装备掉落（示意）",
            FontSize = 12,
            Foreground = Brush("#C8B48A"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var deathBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        var reviveTown = new Button { Content = "回城复活", MinWidth = 120 };
        MirChrome.StylePrimary(reviveTown);
        reviveTown.Click += (_, _) => Toast("服务器会把你复活到出生点");
        var waitHere = new Button { Content = "就地等待", MinWidth = 120 };
        MirChrome.StyleNormal(waitHere);
        waitHere.Click += (_, _) => Toast("观战残局中… 等待服务器复活");
        deathBtns.Children.Add(reviveTown);
        deathBtns.Children.Add(waitHere);
        deathBody.Children.Add(deathBtns);
        deathBody.Children.Add(new TextBlock
        {
            Text = "主路径：回城至安全点 · 空格可归回城复活",
            FontSize = 10,
            Foreground = Brush("#E07070"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var deathFrame = MirChrome.Framed(deathBody, 400);
        deathFrame.HorizontalAlignment = HorizontalAlignment.Center;
        deathFrame.VerticalAlignment = VerticalAlignment.Center;
        _death = new Border
        {
            Background = Brush("#CC1A0808"),
            IsVisible = false,
            Child = deathFrame
        };


        _canvas.World = _session.World;
        _canvas.WorldPressed += (point, right, shift, ctrl, alt, clicks) =>
        {
            _canvas.Focus();
            OnWorld(point, right, shift, ctrl, alt, clicks);
        };

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(_hud);
        root.Children.Add(_bag);
        root.Children.Add(_chars);
        root.Children.Add(_login);
        root.Children.Add(_reconnect);
        root.Children.Add(_death);
        Content = root;

        _session.EnteredWorld += () =>
        {
            _audio.PlayForMap(_session.World.Map.MapId, _session.World.Self?.InSafeZone == true);
            _audio.PlayAmbient();
        };
        _session.LeveledUp += () =>
        {
            _audio.PlayLevel();
            var self = _session.World.Self;
            if (self != null)
                _session.World.AddCast(self.DrawX, self.DrawY, "warrior", "level");
        };
        _session.MetaChanged += () => { };
        _session.PickedUp += () => _audio.PlayPickup();
        _session.CombatSeen += OnCombat;
        _session.Disconnected += () =>
        {
            Toast("连接已断开");
            _audio.PlayLoginBgm();
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        Opened += (_, _) =>
        {
            _audio.PlayLoginBgm();
            _timer.Start();
            if (!string.IsNullOrWhiteSpace(options.UiCaptureDir))
            {
                Console.WriteLine("UI_CAPTURE_OPEN " + options.UiCaptureDir);
                RunUiCaptureAsync(options.UiCaptureDir);
            }
            else if (options.UiSmoke)
            {
                Console.WriteLine("UI_SMOKE_OPEN");
                var close = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                close.Tick += (_, _) =>
                {
                    close.Stop();
                    Close();
                };
                close.Start();
            }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _audio.Dispose();
            _session.Dispose();
        };

        ApplyChrome();
    }

    private void Tick()
    {
        if (_closed)
            return;
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastTick).TotalSeconds, 0, 0.1);
        _lastTick = now;
        _session.Pump();
        _session.World.Tick(dt);
        _canvas.AnimTime += dt;
        _canvas.ToastAge += dt;
        if (_session.Fsm.Phase == SessionPhase.InWorld)
        {
            TickMove();
            _session.TickAssist(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            TrackSafeZone();
            MaybePollBoss();
            SyncGear();
            string mapId = _session.World.Map.MapId;
            if (_heardMap.Length > 0 && _heardMap != mapId)
                _audio.PlayPortal();
            _heardMap = mapId;
        }
        ApplyChrome();
        _canvas.InvalidateVisual();
    }

    private void TickMove()
    {
        var self = _session.World.Self;
        if (self == null || _moveBusy || nowReadyMove() == false)
            return;
        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        ReadWasd(out int dx, out int dy);
        if (dx != 0 || dy != 0)
        {
            ClearApproach();
            _session.World.ClearGoal();
            self.Direction = GridMath.DirFromDelta(dx, dy);
            _ = Step(self.GridX + dx, self.GridY + dy, self.Direction, fromKeyboard: true);
            return;
        }

        if ((_pendingAttack || _pendingPickup || _pendingTalk) && _pendingId != null)
        {
            var target = _session.World.Get(_pendingId);
            if (target == null)
            {
                ClearApproach();
            }
            else
            {
                int dist = GridMath.Chebyshev(self.GridX, self.GridY, target.GridX, target.GridY);
                int need = _pendingAttack
                    ? PlayableTuning.AttackRange
                    : _pendingTalk ? PlayableTuning.TalkRange : PlayableTuning.PickupRange;
                if (dist <= need)
                {
                    if (_pendingAttack && DateTime.UtcNow < _nextAttack)
                        return;
                    var id = _pendingId;
                    bool attack = _pendingAttack;
                    bool talk = _pendingTalk;
                    ClearApproach();
                    _session.World.ClearGoal();
                    if (attack)
                        _ = Attack(id);
                    else if (talk)
                        _ = Talk(id);
                    else
                        _ = Pickup(id);
                    return;
                }
                _session.World.SetGoal(target.GridX, target.GridY);
            }
        }

        if (_session.World.TryNextStep(out int tx, out int ty, out int dir))
        {
            self.Direction = dir;
            _ = Step(tx, ty, dir, fromKeyboard: false);
        }

        bool nowReadyMove() => DateTime.UtcNow >= _nextMove;
    }

    private async Task Step(int x, int y, int dir, bool fromKeyboard)
    {
        if (_moveBusy)
            return;
        _moveBusy = true;
        bool mounted = !string.IsNullOrEmpty(_session.World.Self?.MountId);
        int mountMs = _session.World.Self?.MoveIntervalMs ?? 0;
        bool running = _mouseRun || _held.Contains(Key.LeftShift) || _held.Contains(Key.RightShift);
        double wait = mounted && mountMs > 0 ? mountMs / 1000.0 : running ? 0.1 : 0.22;
        _nextMove = DateTime.UtcNow.AddSeconds(wait);
        try
        {
            var sync = await _session.MoveAsync(x, y, dir);
            if (sync != null && sync.Accepted)
            {
                _audio.PlayStep();
                if (_session.World.Self != null && _session.World.Self.PoseLock <= 0)
                {
                    string next = mounted || running ? "run" : "walk";
                    if (_session.World.Self.Pose != next)
                    {
                        _session.World.Self.Pose = next;
                        _session.World.Self.AnimPhase = 0;
                    }
                }
            }
            if (sync != null && !sync.Accepted && !fromKeyboard)
            {
                _session.World.ClearGoal();
                ClearApproach();
                if (!string.IsNullOrWhiteSpace(sync.Reason))
                    Toast(sync.Reason);
            }
        }
        catch (Exception ex)
        {
            Toast(ex.Message);
        }
        finally
        {
            _moveBusy = false;
        }
    }

    private void OnWorld(Avalonia.Point point, bool right, bool shift, bool ctrl, bool alt, int clicks)
    {
        if (_session.Fsm.Phase != SessionPhase.InWorld)
            return;
        var self = _session.World.Self;
        if (self == null)
            return;
        string? hit = _canvas.HitTest(point);
        var actor = hit == null ? null : _session.World.Get(hit);
        string? kind = actor == null ? null : actor.Kind switch
        {
            EntityKind.EntityMonster => "monster",
            EntityKind.EntityLoot => "loot",
            EntityKind.EntityNpc => "npc",
            EntityKind.EntityPlayer => "player",
            _ => "other"
        };
        var action = ClassicPointer.Resolve(right, shift, ctrl, alt, kind, clicks >= 2);
        if (hit != null)
            _session.World.SelectedId = hit;
        switch (action)
        {
            case ClickAction.Examine:
                Examine(actor);
                return;
            case ClickAction.Dig:
                Dig();
                return;
            case ClickAction.Run:
                _mouseRun = true;
                if (actor != null && kind is "monster" or "player")
                {
                    _session.World.SetGoal(actor.GridX, actor.GridY);
                    return;
                }
                if (_canvas.TryScreenToGrid(point, out int rx, out int ry))
                {
                    ClearApproach();
                    _session.World.SetGoal(rx, ry);
                }
                return;
            case ClickAction.ForceAttack:
                _mouseRun = false;
                if (actor != null && kind is "monster" or "player")
                    BeginAttack(actor, force: true);
                else
                    _ = Attack(SelectedMonsterOrNearest(), PlayableTuning.BasicSkillId, force: true);
                return;
            case ClickAction.Pickup:
                _mouseRun = false;
                if (actor != null)
                    BeginApproach(actor.EntityId, attack: false,
                        GridMath.Chebyshev(self.GridX, self.GridY, actor.GridX, actor.GridY),
                        PlayableTuning.PickupRange);
                return;
            case ClickAction.Talk:
                _mouseRun = false;
                if (actor == null)
                    return;
                int talkDist = GridMath.Chebyshev(self.GridX, self.GridY, actor.GridX, actor.GridY);
                if (talkDist <= PlayableTuning.TalkRange)
                    _ = Talk(actor.EntityId);
                else
                {
                    ClearApproach();
                    _pendingTalk = true;
                    _pendingId = actor.EntityId;
                    _session.World.SetGoal(actor.GridX, actor.GridY);
                    Toast("靠近 " + actor.Name);
                }
                return;
            case ClickAction.Attack:
                _mouseRun = false;
                if (actor != null)
                    BeginAttack(actor, force: false);
                return;
            default:
                _mouseRun = false;
                if (_canvas.TryScreenToGrid(point, out int gx, out int gy))
                {
                    ClearApproach();
                    _session.World.SetGoal(gx, gy);
                }
                return;
        }
    }

    private void ClearApproach()
    {
        _pendingAttack = false;
        _pendingPickup = false;
        _pendingTalk = false;
        _pendingId = null;
    }

    private void BeginApproach(string id, bool attack, int dist, int range)
    {
        _session.World.SelectedId = id;
        if (dist <= range)
        {
            if (attack) _ = Attack(id);
            else _ = Pickup(id);
            return;
        }
        ClearApproach();
        _pendingAttack = attack;
        _pendingPickup = !attack;
        _pendingId = id;
        var actor = _session.World.Get(id);
        if (actor != null)
            _session.World.SetGoal(actor.GridX, actor.GridY);
        Toast(attack ? "正在接近目标" : "正在靠近掉落");
    }

    private async Task Attack(string? targetId, string? skillId = null, bool force = false)
    {
        string skill = string.IsNullOrWhiteSpace(skillId) ? PlayableTuning.BasicSkillId : skillId;
        var def = ContentCatalog.FindSkill(skill);
        bool support = def != null && def.Kind is "heal" or "buff" or "summon" or "teleport";
        if (!support && string.IsNullOrEmpty(targetId))
        {
            Toast("附近没有可攻击目标");
            return;
        }
        var self = _session.World.Self;
        if (!support && self != null && self.InSafeZone)
        {
            Toast("安全区内无法攻击");
            return;
        }
        var target = string.IsNullOrEmpty(targetId) ? null : _session.World.Get(targetId);
        if (!support && target != null && target.Kind == EntityKind.EntityPlayer &&
            !AttackModes.CanAttack(_attackMode, false, sameParty: false, sameGuild: false, force))
        {
            Toast($"{AttackModes.Label(_attackMode)}模式不能攻击玩家");
            return;
        }
        if (support)
            targetId = self?.EntityId ?? targetId;
        if (DateTime.UtcNow < _nextAttack)
            return;
        _nextAttack = DateTime.UtcNow.AddSeconds(Math.Max(0.2, (def?.CooldownMs ?? 600) / 1000.0));
        if (support)
        {
            _audio.PlayCast(def?.School);
            self?.PlayPose("cast", 0.48);
            if (self != null && def?.Kind == "buff")
                self.BuffUntil = _session.World.Time + 8;
        }
        else
        {
            _audio.PlayAttack();
            self?.PlayPose("attack", 0.42);
        }
        if (self != null)
        {
            string fx = def?.SkillId switch
            {
                "lightning" or "hell_lightning" or "fast_beam" => "lightning",
                "heal" or "holy_armor" or "ghost_shield" or "soul_charm" or "poison_charm" => "heal",
                "fire_wall" or "burst_flame" or "fire_rain" or "arcane_bolt" => "fire",
                _ => support ? "heal" : "slash"
            };
            _session.World.AddCast(self.DrawX, self.DrawY, def?.School ?? "warrior", fx);
            if (!support && target != null)
            {
                _session.World.AddCast(target.DrawX, target.DrawY, def?.School ?? "warrior", fx);
                if (target.PoseLock < 0.15)
                    target.PlayPose("hit", 0.28);
            }
        }
        if (def != null && def.CastMs > 0)
            await Task.Delay(Math.Min(def.CastMs, 400));
        try
        {
            var resp = await _session.AttackAsync(targetId ?? "", skill, force);
            if (resp != null && !resp.Success && !string.IsNullOrWhiteSpace(resp.Message))
                Toast(resp.Message);
        }
        catch (Exception ex)
        {
            Toast(ex.Message);
        }
    }

    private async Task Pickup(string id)
    {
        _audio.PlayUiClick();
        try
        {
            var resp = await _session.PickupAsync(id);
            if (resp != null && !resp.Success)
                Toast(string.IsNullOrWhiteSpace(resp.Message) ? "无法拾取" : resp.Message);
        }
        catch (Exception ex)
        {
            Toast(ex.Message);
        }
    }

    private async Task Talk(string id)
    {
        _audio.PlayUiClick();
        try
        {
            var resp = await _session.TalkAsync(id);
            if (resp != null && resp.Success)
            {
                if (!string.IsNullOrWhiteSpace(resp.Dialogue))
                    Toast(resp.Dialogue);
                OpenNpcPanel(resp);
            }
            else if (resp != null && !resp.Success)
                Toast(resp.Message);
        }
        catch (Exception ex)
        {
            Toast(ex.Message);
        }
    }

    private string? SelectedMonsterOrNearest()
    {
        var world = _session.World;
        var selected = world.Get(world.SelectedId);
        if (selected != null && selected.Kind == EntityKind.EntityMonster && selected.Hp > 0)
            return selected.EntityId;
        var self = world.Self;
        if (self == null)
            return null;
        var nearest = world.Nearest(EntityKind.EntityMonster, self.GridX, self.GridY, 8, livingOnly: true);
        if (nearest != null)
            world.SelectedId = nearest;
        return nearest;
    }

    private void OnCombat(CombatSync cs)
    {
        if (cs.Result is not CombatResultKind.CombatMiss and not CombatResultKind.CombatHeal)
            _audio.PlayHit();
        var attacker = _session.World.Get(cs.AttackerEntityId);
        if (attacker != null && attacker.Kind == EntityKind.EntityMonster)
            _audio.PlayMonster();
        if (_session.World.Self != null && cs.TargetEntityId == _session.World.Self.EntityId && cs.TargetDied)
        {
            _audio.PlayDeath();
            Toast("你已死亡，服务器会把你复活到出生点");
        }
        else if (cs.TargetDied)
            _audio.PlayMonster();
    }

    private void TrackSafeZone()
    {
        bool safe = _session.World.Self?.InSafeZone ?? false;
        if (_lastSafe == safe)
            return;
        _lastSafe = safe;
        Toast(safe ? "进入安全区" : "离开安全区");
        UpdateZoneBanner(safe ? "◆ 安全区 · 禁止 PK · 可摆摊/存仓" : "◇ 已离开安全区 · 野外可战斗", safe);
        _audio.PlayForMap(_session.World.Map.MapId, safe);
    }

    private void ApplyChrome()
    {
        var mode = ClientUiModeResolver.FromPhase(_session.Fsm.Phase);
        _login.IsVisible = mode == ClientUiMode.Login;
        _chars.IsVisible = mode == ClientUiMode.CharSelect;
        _hud.IsVisible = mode == ClientUiMode.InWorld;
        _bag.IsVisible = mode == ClientUiMode.InWorld && _bagOpen;
        _reconnect.IsVisible = mode == ClientUiMode.Reconnecting;

        if (mode == ClientUiMode.Login)
        {
            string err = _session.Fsm.Phase == SessionPhase.Error ? _session.Fsm.ErrorMessage : _session.Fsm.StatusMessage;
            _loginStatus.Text = string.IsNullOrWhiteSpace(err) ? "输入账号后进入游戏。没有账号会自动注册。" : err;
        }

        if (mode == ClientUiMode.CharSelect)
        {
            _charStatus.Text = _session.Fsm.StatusMessage;
            if (_charCount != _session.Characters.Count)
            {
                _charCount = _session.Characters.Count;
                RebuildChars();
            }
        }
        else
        {
            _charCount = -1;
        }

        if (mode == ClientUiMode.Reconnecting)
            _reconnectText.Text = "与服务器断开。可重新登录后继续。";

        RefreshBossChrome(mode);
        var self = _session.World.Self;
        _death.IsVisible = mode == ClientUiMode.InWorld && (self?.Hp ?? 1) <= 0;
        if (mode != ClientUiMode.InWorld)
            return;
        int hp = self?.Hp ?? 0;
        int maxHp = Math.Max(1, self?.MaxHp ?? 1);
        _hpBar.Maximum = maxHp;
        _hpBar.Value = Math.Clamp(hp, 0, maxHp);
        _hpText.Text = $"HP {hp}/{maxHp}";
        int maxMp = Math.Max(1, _session.MaxMp);
        _mpBar.Maximum = maxMp;
        _mpBar.Value = Math.Clamp(_session.Mp, 0, maxMp);
        _mpText.Text = $"MP {_session.Mp}/{maxMp}";
        string safe = self?.InSafeZone == true ? "安全区" : "野外";
        _vitals.Text = $"Lv{_session.Level}  {_session.World.Map.MapName}  ({self?.GridX ?? 0},{self?.GridY ?? 0})  {safe}  金{_session.Inventory.Gold}  攻{_session.Atk} 防{_session.Def}";
        var selected = _session.World.Get(_session.World.SelectedId);
        _target.Text = selected == null
            ? "目标：无"
            : $"目标：{selected.Name}  HP {selected.Hp}/{selected.MaxHp}";

        int chats = _session.Chat.Lines.Count;
        if (chats != _chatCount)
        {
            _chatCount = chats;
            _chatLog.Text = string.Join(Environment.NewLine, _session.Chat.Lines);
            _chatScroll.ScrollToEnd();
        }

        string fp = string.Join("|", _session.Inventory.Items.Select(i => i.ItemId + ":" + i.Count)) + "#" + _session.Inventory.Gold;
        if (fp != _bagFp)
        {
            _bagFp = fp;
            RebuildBag();
        }
        RefreshPhaseL();
    }

    private Border BuildBossHost()
    {
        var stack = new StackPanel { Spacing = 4, Width = 300 };
        stack.Children.Add(_bossText);
        stack.Children.Add(_bossHp);
        return new Border
        {
            Background = Brush("#CC1A140F"),
            BorderBrush = Brush("#E8C872"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(8),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
            IsVisible = false
        };
    }

    private void RefreshBossChrome(ClientUiMode mode)
    {
        if (mode != ClientUiMode.InWorld)
        {
            _bossHost.IsVisible = false;
            return;
        }
        var boss = _session.Bosses.FirstOrDefault(b => b.Alive) ?? _session.Bosses.FirstOrDefault();
        if (boss == null)
        {
            _bossHost.IsVisible = false;
            return;
        }
        _bossHost.IsVisible = true;
        int max = Math.Max(1, boss.MaxHp);
        _bossHp.Maximum = max;
        _bossHp.Value = Math.Clamp(boss.Hp, 0, max);
        string hate = boss.HateTop.Count == 0
            ? "暂无仇恨"
            : string.Join("  ", boss.HateTop.Take(5).Select(h => $"{h.Rank}.{h.Name} {h.Damage}"));
        string state = boss.Alive ? $"{boss.Hp}/{max}" : "未刷新";
        _bossText.Text = $"{boss.DisplayName}  {state}\n仇恨 {hate}";
    }

    private void SyncGear()
    {
        string weapon = "";
        string armor = "";
        string helmet = "";
        foreach (var eq in _session.Equipment)
        {
            if (string.IsNullOrEmpty(eq.ItemId))
                continue;
            string layer = ContentCatalog.Paperdoll(eq.ItemId);
            if (layer.Length == 0)
                continue;
            if (eq.Slot.Contains("weapon", StringComparison.OrdinalIgnoreCase))
                weapon = layer;
            else if (string.Equals(eq.Slot, "armor", StringComparison.OrdinalIgnoreCase))
                armor = layer;
            else if (string.Equals(eq.Slot, "helmet", StringComparison.OrdinalIgnoreCase))
                helmet = layer;
        }
        if (weapon.Length == 0)
        {
            weapon = (_session.ClassId ?? "").ToLowerInvariant() switch
            {
                "mage" => "wep_staff",
                "taoist" => "wep_blade",
                _ => "wep_sword"
            };
        }
        _canvas.WeaponLayer = weapon;
        _canvas.ArmorLayer = armor;
        _canvas.HelmetLayer = helmet;
        _canvas.FashionOn = !string.IsNullOrEmpty(_session.World.Self?.FashionId);
    }

    private void MaybePollBoss()
    {
        if (_bossBusy || DateTime.UtcNow < _nextBossPoll)
            return;
        _bossBusy = true;
        _nextBossPoll = DateTime.UtcNow.AddSeconds(2);
        _ = PollBoss();

        async Task PollBoss()
        {
            try { await _session.RefreshBossesAsync(); }
            catch { /* status is optional until the socket is ready */ }
            finally { _bossBusy = false; }
        }
    }

    private void RebuildChars()
    {
        _charList.Children.Clear();
        if (_session.Characters.Count == 0)
            _charList.Children.Add(new TextBlock { Text = "还没有角色，先创建一个。", Foreground = Brushes.White });
        foreach (var character in _session.Characters)
        {
            var captured = character;
            var button = new Button
            {
                Content = $"进入  {character.Name}   Lv{character.Level}  {ClassLabel(character.ClassId)}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Width = 280
            };
            button.Click += async (_, _) =>
            {
                _audio.PlayUiClick();
                try { await _session.EnterAsync(captured); }
                catch (Exception ex) { _charStatus.Text = ex.Message; }
            };
            var del = new Button { Content = "删除", Width = 64 };
            del.Click += async (_, _) =>
            {
                try
                {
                    await _session.DeleteCharacterAsync(captured.CharacterId);
                    _charCount = -1;
                }
                catch (Exception ex) { _charStatus.Text = ex.Message; }
            };
            var portrait = new ClassPortrait
            {
                Width = 56,
                Height = 72,
                ClassId = character.ClassId,
                Gender = character.Gender
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(portrait);
            row.Children.Add(button);
            row.Children.Add(del);
            _charList.Children.Add(row);
        }
    }

    private void RebuildBag()
    {
        _bagItems.Children.Clear();
        _bagGold.Text = $"金币 {_session.Inventory.Gold}";
        if (_session.Inventory.Items.Count == 0)
        {
            _bagItems.Children.Add(new TextBlock { Text = "背包是空的", Foreground = Brushes.White, Margin = new Avalonia.Thickness(4) });
            return;
        }
        int iconIndex = 1;
        foreach (var item in _session.Inventory.Items)
        {
            var id = item.ItemId;
            var icon = MirChrome.ItemIconFor(id, ((iconIndex - 1) % 16) + 1);
            iconIndex++;
            var cell = new Grid { Width = 40, Height = 40 };
            if (MirChrome.SlotEmpty != null)
                cell.Children.Add(new Image { Source = MirChrome.SlotEmpty, Width = 40, Height = 40, Stretch = Stretch.Fill });
            if (icon != null)
                cell.Children.Add(new Image
                {
                    Source = icon,
                    Width = 32,
                    Height = 32,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            if (item.Count > 1)
            {
                cell.Children.Add(new TextBlock
                {
                    Text = item.Count.ToString(),
                    FontSize = 10,
                    Foreground = Brush("#F6E6C0"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Avalonia.Thickness(0, 0, 2, 1)
                });
            }
            var button = new Button
            {
                Width = 44,
                Height = 44,
                Padding = new Avalonia.Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
                Content = cell
            };
            ToolTip.SetTip(button, $"{ContentCatalog.ItemName(item.ItemId)} x{item.Count} · 拖到人物装备 · 双击使用");
            button.PointerPressed += async (s, ev) =>
            {
                if (ev.ClickCount >= 2)
                {
                    _audio.PlayUiClick();
                    try { Toast(await _session.UseItemAsync(id) ?? $"使用 {ContentCatalog.ItemName(id)}"); }
                    catch (Exception ex) { Toast(ex.Message); }
                    return;
                }
                var data = new DataObject();
                data.Set("legend-item", id);
                await DragDrop.DoDragDrop(ev, data, DragDropEffects.Copy);
            };
            _bagItems.Children.Add(button);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool typing = FocusManager?.GetFocusedElement() is TextBox;
        if (e.Key is Key.Enter or Key.Return)
        {
            HandleEnter(typing);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            _bagOpen = false;
            ClosePhaseLPanels();
            _canvas.Focus();
            e.Handled = true;
            return;
        }
        if (typing)
        {
            base.OnKeyDown(e);
            return;
        }
        if (!_held.Add(e.Key))
            return;
        if (_session.Fsm.Phase != SessionPhase.InWorld)
            return;
        if (TryPhaseLKey(e))
            return;
        switch (e.Key)
        {
            case Key.Space:
            case Key.J:
                _ = Attack(SelectedMonsterOrNearest(), PlayableTuning.BasicSkillId);
                e.Handled = true;
                break;
            case Key.Oem3:
            {
                var next = _session.World.CycleMonster(_session.World.SelectedId);
                _session.World.SelectedId = next;
                if (next != null)
                    Toast("目标 " + (_session.World.Get(next)?.Name ?? next));
                e.Handled = true;
                break;
            }
            case Key.B:
                _bagOpen = !_bagOpen;
                _audio.PlayUiClick();
                e.Handled = true;
                break;
            case Key.Z:
            case Key.G:
                TryPickupKey();
                e.Handled = true;
                break;
            case Key.W:
            case Key.A:
            case Key.S:
            case Key.D:
            case Key.Up:
            case Key.Down:
            case Key.Left:
            case Key.Right:
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _held.Remove(e.Key);
        base.OnKeyUp(e);
    }

    private void HandleEnter(bool typing)
    {
        if (_session.Fsm.Phase != SessionPhase.InWorld)
            return;
        if (!typing)
        {
            _chatInput.Focus();
            return;
        }
        string text = _chatInput.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            _canvas.Focus();
            return;
        }
        _chatInput.Text = "";
        var route = ClassicChat.Parse(text, _chatChannel, _whisperName.Text ?? "");
        if (route.Error != null)
        {
            Toast(route.Error);
            return;
        }
        if (route.Content.Length == 0)
            return;
        _ = SendChat(route.Channel, route.Content, route.TargetName);
    }

    private async Task SendChat(ChatChannel channel, string text, string? target = null)
    {
        try
        {
            bool masked = await _session.SendChatAsync(channel, text, target);
            if (masked)
                Toast("已按脏词表替换");
        }
        catch (Exception ex) { Toast(ex.Message); }
    }

    private void TryPickupKey()
    {
        var self = _session.World.Self;
        if (self == null)
            return;
        var selected = _session.World.Get(_session.World.SelectedId);
        if (selected != null && selected.Kind == EntityKind.EntityLoot)
        {
            BeginApproach(selected.EntityId, attack: false,
                GridMath.Chebyshev(self.GridX, self.GridY, selected.GridX, selected.GridY),
                PlayableTuning.PickupRange);
            return;
        }
        var nearest = _session.World.Nearest(EntityKind.EntityLoot, self.GridX, self.GridY, 8, livingOnly: false);
        if (nearest == null)
        {
            Toast("附近没有可拾取物");
            return;
        }
        var loot = _session.World.Get(nearest)!;
        BeginApproach(nearest, attack: false,
            GridMath.Chebyshev(self.GridX, self.GridY, loot.GridX, loot.GridY),
            PlayableTuning.PickupRange);
    }

    private async Task DoLogin()
    {
        _audio.PlayUiClick();
        if (!int.TryParse(_port.Text?.Trim(), out int port))
        {
            _loginStatus.Text = "端口不正确";
            return;
        }
        _session.Configure(new LaunchOptions
        {
            Host = string.IsNullOrWhiteSpace(_host.Text) ? _options.Host : _host.Text.Trim(),
            Port = port,
            RealmId = _options.RealmId,
            Username = _user.Text?.Trim() ?? "",
            Password = _pass.Text ?? ""
        });
        if (string.IsNullOrWhiteSpace(_session.LastUsername) && string.IsNullOrWhiteSpace(_user.Text))
        {
            _loginStatus.Text = "请填写账号";
            return;
        }
        _loginStatus.Text = $"正在连接 {_session.Host}:{_session.Port} …";
        await _session.LoginAsync(_user.Text ?? "", _pass.Text ?? "");
        if (_session.Fsm.Phase == SessionPhase.Error)
            _loginStatus.Text = _session.Fsm.ErrorMessage;
    }

    private void Toast(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        _canvas.Toast = message.Trim();
        _canvas.ToastAge = 0;
    }

    private void ReadWasd(out int dx, out int dy)
    {
        dx = 0;
        dy = 0;
        if (_held.Contains(Key.A) || _held.Contains(Key.Left)) dx -= 1;
        if (_held.Contains(Key.D) || _held.Contains(Key.Right)) dx += 1;
        if (_held.Contains(Key.S) || _held.Contains(Key.Down)) dy -= 1;
        if (_held.Contains(Key.W) || _held.Contains(Key.Up)) dy += 1;
    }

    private Border BuildLogin()
    {
        MirChrome.StyleField(_user);
        MirChrome.StyleField(_pass);
        MirChrome.StyleField(_host);
        MirChrome.StyleField(_port);

        var loginBtn = new Button { Content = "登录", HorizontalAlignment = HorizontalAlignment.Stretch };
        MirChrome.StylePrimary(loginBtn);
        loginBtn.Click += async (_, _) =>
        {
            try { await DoLogin(); }
            catch (Exception ex) { _loginStatus.Text = ex.Message; }
        };
        var noticeBtn = new Button { Content = "公告", HorizontalAlignment = HorizontalAlignment.Stretch };
        MirChrome.StyleNormal(noticeBtn);
        var exitBtn = new Button { Content = "退出", HorizontalAlignment = HorizontalAlignment.Stretch };
        MirChrome.StyleDanger(exitBtn);
        exitBtn.Click += (_, _) => Close();

        var loginBody = new StackPanel { Spacing = 8 };
        loginBody.Children.Add(new TextBlock
        {
            Text = "账号登录",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#F6E6C0"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        loginBody.Children.Add(Labeled("账号", _user));
        loginBody.Children.Add(Labeled("密码", _pass));
        loginBody.Children.Add(Labeled("主机", _host));
        loginBody.Children.Add(Labeled("端口", _port));
        loginBody.Children.Add(new TextBlock { Text = "区服 " + _options.RealmId, Foreground = Brush("#C8B48A"), FontSize = 12 });
        var btnRow = new Grid();
        btnRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        btnRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        btnRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        Grid.SetColumn(loginBtn, 0);
        Grid.SetColumn(noticeBtn, 1);
        Grid.SetColumn(exitBtn, 2);
        loginBtn.Margin = new Avalonia.Thickness(0, 0, 4, 0);
        noticeBtn.Margin = new Avalonia.Thickness(2, 0, 2, 0);
        exitBtn.Margin = new Avalonia.Thickness(4, 0, 0, 0);
        btnRow.Children.Add(loginBtn);
        btnRow.Children.Add(noticeBtn);
        btnRow.Children.Add(exitBtn);
        loginBody.Children.Add(btnRow);
        loginBody.Children.Add(_loginStatus);
        loginBody.Children.Add(new TextBlock
        {
            Text = "Enter 登录 · Esc 退出 · 公网 150.158.164.90:7100",
            FontSize = 11,
            Foreground = Brush("#8A7A5A"),
            TextWrapping = TextWrapping.Wrap
        });

        var noticeBody = new StackPanel { Spacing = 6 };
        noticeBody.Children.Add(new TextBlock
        {
            Text = "游戏公告",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#F6E6C0"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        foreach (var line in new[]
                 {
                     "[维护] 每周日 04:00–06:00 例行维护",
                     "[活动] 经验加成进行中",
                     "[更新] 石金壳对齐 OL-01 / OL-02 / OL-03",
                     "[提示] 请使用官方启动器或本客户端",
                     "[安全] 勿泄露账号与验证码",
                     "[版本] 客户端协议 1.21.0"
                 })
        {
            noticeBody.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = 13,
                Foreground = Brush("#E8C872"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var title = new TextBlock
        {
            Text = "开天·传奇",
            FontSize = 40,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#E8C872"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 0, 16)
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        row.Children.Add(MirChrome.Framed(loginBody, 320));
        row.Children.Add(MirChrome.Framed(noticeBody, 360, 320));

        var center = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        center.Children.Add(title);
        center.Children.Add(row);
        center.Children.Add(new TextBlock
        {
            Text = "自研客户端 · 对齐私服石金壳 · 无 Unity",
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("#B7A78A"),
            FontSize = 12,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        });

        var root = new Grid();
        root.Children.Add(new TownBackdrop());
        root.Children.Add(new Border { Background = Brush("#990C0806") });
        root.Children.Add(center);
        return new Border { Child = root, Background = Brush("#120E0C") };
    }

    private Border BuildChars()
    {
        MirChrome.StyleField(_newName);
        var create = new Button { Content = "创建角色", HorizontalAlignment = HorizontalAlignment.Stretch };
        MirChrome.StylePrimary(create);
        create.Click += async (_, _) =>
        {
            _audio.PlayUiClick();
            string name = _newName.Text?.Trim() ?? "";
            if (name.Length == 0)
            {
                _charStatus.Text = "请填写角色名";
                return;
            }
            string classId = (_classBox.SelectedItem as ClassChoice)?.Id ?? "warrior";
            string gender = _genderBox.SelectedIndex == 1 ? "f" : "m";
            try { await _session.CreateCharacterAsync(name, classId, gender); }
            catch (Exception ex) { _charStatus.Text = ex.Message; }
            _charCount = -1;
        };
        void SyncPreview()
        {
            _preview.ClassId = (_classBox.SelectedItem as ClassChoice)?.Id ?? "warrior";
            _preview.Gender = _genderBox.SelectedIndex == 1 ? "f" : "m";
            _preview.InvalidateVisual();
        }
        _classBox.SelectionChanged += (_, _) => SyncPreview();
        _genderBox.SelectionChanged += (_, _) => SyncPreview();
        SyncPreview();
        var logout = new Button { Content = "返回登录", HorizontalAlignment = HorizontalAlignment.Stretch };
        MirChrome.StyleNormal(logout);
        logout.Click += (_, _) =>
        {
            _session.ReturnToLogin();
            _audio.PlayLoginBgm();
        };
        var panel = new StackPanel { Spacing = 8, Width = 480 };
        panel.Children.Add(new TextBlock
        {
            Text = "选择角色",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#E8C872"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new ScrollViewer { Content = _charList, MaxHeight = 220 });
        var createRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _preview.Width = 96;
        _preview.Height = 120;
        var form = new StackPanel { Spacing = 6, Width = 280 };
        form.Children.Add(new TextBlock { Text = "创角", Foreground = Brush("#E8C872") });
        form.Children.Add(_newName);
        form.Children.Add(_classBox);
        form.Children.Add(_genderBox);
        form.Children.Add(create);
        form.Children.Add(logout);
        createRow.Children.Add(_preview);
        createRow.Children.Add(form);
        panel.Children.Add(createRow);
        panel.Children.Add(_charStatus);
        var framed = MirChrome.Framed(panel, 520, 520);
        var root = new Grid();
        root.Children.Add(new TownBackdrop());
        root.Children.Add(new Border { Background = Brush("#990C0806") });
        root.Children.Add(framed);
        return new Border { Child = root, Background = Brush("#120E0C") };
    }

    private Grid BuildHud()
    {
        _hpText.FontSize = 12;
        _mpText.FontSize = 12;
        _hpBar.Height = 10;
        _mpBar.Height = 10;
        _expBar.Height = 8;
        MirChrome.StyleField(_chatInput);

        // BL yin-yang orb (OL-02)
        var orbStack = new Grid { Width = 168, Height = 180 };
        if (MirChrome.Orb != null)
            orbStack.Children.Add(new Image { Source = MirChrome.Orb, Stretch = Stretch.Uniform, Width = 168, Height = 180 });
        var orbLabels = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Spacing = 2
        };
        orbLabels.Children.Add(_hpText);
        orbLabels.Children.Add(_mpText);
        orbStack.Children.Add(orbLabels);
        var orbHost = new Border
        {
            Child = orbStack,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Avalonia.Thickness(8, 0, 0, 8)
        };

        // subtle vitals / exp under top strip
        var topStrip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        topStrip.Children.Add(_vitals);
        topStrip.Children.Add(_expBar);
        var topLeft = new Border
        {
            Background = Brush("#AA120E0A"),
            Padding = new Avalonia.Thickness(8, 4),
            Margin = new Avalonia.Thickness(12, 8, 0, 0),
            Child = topStrip,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        // TR: target chip only — radar is drawn by WorldCanvas (hollow chrome would double-cover).
        _target.FontSize = 12;
        _target.Foreground = Brush("#F6E6C0");
        var topRight = new Border
        {
            Background = Brush("#CC120E0A"),
            BorderBrush = Brush("#E8C872"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(8, 4),
            Child = _target,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 8, 160, 0),
            MaxWidth = 220
        };

        var worldBtn = Small("世界");
        var nearBtn = Small("附近");
        MirChrome.StyleSys(worldBtn);
        MirChrome.StyleSys(nearBtn);
        worldBtn.Click += (_, _) => { _chatChannel = ChatChannel.ChatWorld; _audio.PlayUiClick(); worldBtn.FontWeight = FontWeight.Bold; nearBtn.FontWeight = FontWeight.Normal; };
        nearBtn.Click += (_, _) => { _chatChannel = ChatChannel.ChatNearby; _audio.PlayUiClick(); nearBtn.FontWeight = FontWeight.Bold; worldBtn.FontWeight = FontWeight.Normal; };
        worldBtn.FontWeight = FontWeight.Bold;
        var channel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        channel.Children.Add(worldBtn);
        channel.Children.Add(nearBtn);
        _chatScroll.Height = 78;
        _chatLog.Foreground = Brush("#E8DCC0");
        var chat = new StackPanel { Spacing = 3, Width = 440 };
        chat.Children.Add(channel);
        chat.Children.Add(_chatScroll);
        chat.Children.Add(_chatInput);
        chat.Children.Add(_hint);
        var bottom = new Border
        {
            // Solid dark panel — do not use parchment/white baked chat art as fill.
            Background = Brush("#DD14100C"),
            BorderBrush = Brush("#C8A050"),
            BorderThickness = new Avalonia.Thickness(1.5),
            Padding = new Avalonia.Thickness(10, 8, 10, 8),
            Margin = new Avalonia.Thickness(186, 0, 230, 12),
            Child = chat,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Width = 460,
            MaxHeight = 150,
            Opacity = 0.96
        };

        var grid = new Grid();
        grid.Children.Add(topLeft);
        grid.Children.Add(topRight);
        grid.Children.Add(_bossHost);
        grid.Children.Add(bottom);
        grid.Children.Add(orbHost);
        return grid;
    }

    private Border BuildBag()
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = "背包",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#E8C872"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(_bagGold);
        stack.Children.Add(new ScrollViewer { Content = _bagItems, MaxHeight = 340 });
        var art = MirChrome.ArtWindow(
            MirChrome.BagWindow,
            stack,
            width: 260,
            height: 480,
            contentMargin: new Avalonia.Thickness(28, 36, 28, 48));
        art.HorizontalAlignment = HorizontalAlignment.Left;
        art.VerticalAlignment = VerticalAlignment.Center;
        art.Margin = new Avalonia.Thickness(24, 0, 0, 40);
        return art;
    }

    private Border BuildReconnect()
    {
        var again = new Button { Content = "重新登录", HorizontalAlignment = HorizontalAlignment.Stretch };
        again.Click += async (_, _) =>
        {
            _audio.PlayUiClick();
            await _session.ReconnectAsync();
        };
        var back = new Button { Content = "返回登录", HorizontalAlignment = HorizontalAlignment.Stretch };
        back.Click += (_, _) =>
        {
            _session.Fsm.Reset();
            _audio.PlayUiClick();
        };
        var stack = new StackPanel { Spacing = 8, Width = 320 };
        stack.Children.Add(new TextBlock { Text = "连接中断", FontSize = 20, Foreground = Brush("#E8C872") });
        stack.Children.Add(_reconnectText);
        stack.Children.Add(again);
        stack.Children.Add(back);
        MirChrome.StylePrimary(again);
        MirChrome.StyleNormal(back);
        var framed = MirChrome.Framed(stack, 340);
        framed.HorizontalAlignment = HorizontalAlignment.Center;
        framed.VerticalAlignment = VerticalAlignment.Center;
        var root = new Grid();
        root.Children.Add(new Border { Background = Brush("#CC0C0806") });
        root.Children.Add(framed);
        return new Border { Child = root };
    }

    private static readonly ClassChoice[] ClassChoices =
    {
        new("warrior", "武者 · 战"),
        new("mage", "术士 · 法"),
        new("taoist", "灵师 · 道")
    };

    private static string ClassLabel(string id)
        => ClassChoices.FirstOrDefault(c => c.Id == id)?.Label ?? id;

    private static TextBox Field(string text, string watermark) => new()
    {
        Text = text,
        Watermark = watermark
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = 13
    };

    private static ProgressBar Bar(string hex) => new()
    {
        Minimum = 0,
        Maximum = 1,
        Height = 16,
        Foreground = Brush(hex),
        Background = Brush("#140C0A")
    };

    private static Button Small(string text) => new() { Content = text, Padding = new Avalonia.Thickness(10, 4) };

    private static Control Labeled(string title, Control field)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = Brush("#D8C7A2") });
        stack.Children.Add(field);
        return stack;
    }

    private static Border Card(Control child, string hex) => new()
    {
        Background = Brush(hex),
        Child = child,
        Padding = new Avalonia.Thickness(8)
    };

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    private sealed record ClassChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
