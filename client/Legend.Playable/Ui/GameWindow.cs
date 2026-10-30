using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Legend.ClientCore;
using Legend.Contracts.Proto;
using Legend.Playable.Audio;
using Legend.Playable.Session;
using Legend.Playable.World;

namespace Legend.Playable.Ui;

public sealed class GameWindow : Window
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
    private readonly TextBlock _charStatus;
    private readonly TextBlock _hpText;
    private readonly TextBlock _mpText;
    private readonly ProgressBar _hpBar;
    private readonly ProgressBar _mpBar;
    private readonly TextBlock _vitals;
    private readonly TextBlock _target;
    private readonly TextBlock _hint;
    private readonly TextBlock _chatLog;
    private readonly TextBox _chatInput;
    private readonly ScrollViewer _chatScroll;
    private readonly StackPanel _bagItems;
    private readonly TextBlock _bagGold;
    private readonly TextBlock _reconnectText;

    private bool _bagOpen;
    private bool _chatNearby;
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
        _charStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#F0D8A8") };
        _chars = BuildChars();

        _hpText = Label("HP");
        _mpText = Label("MP");
        _hpBar = Bar("#C44747");
        _mpBar = Bar("#3D6FBF");
        _vitals = Label("");
        _target = Label("目标：无");
        _hint = Label("WASD 移动 · 左键点地/选中 · 右键/Space/J 攻击 · Tab 目标 · B 背包 · Enter 聊天 · Z 拾取");
        _hint.Opacity = 0.75;
        _hint.FontSize = 11;
        _chatLog = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brushes.White };
        _chatScroll = new ScrollViewer { Content = _chatLog, Height = 92 };
        _chatInput = Field("", "说点什么…");
        _hud = BuildHud();

        _bagItems = new StackPanel { Spacing = 4 };
        _bagGold = Label("");
        _bag = BuildBag();

        _reconnectText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        _reconnect = BuildReconnect();

        _canvas.World = _session.World;
        _canvas.WorldPressed += (point, right) =>
        {
            _canvas.Focus();
            OnWorld(point, right);
        };

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(_hud);
        root.Children.Add(_bag);
        root.Children.Add(_chars);
        root.Children.Add(_login);
        root.Children.Add(_reconnect);
        Content = root;

        _session.EnteredWorld += () => _audio.PlayWorldBgm();
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
            if (options.UiSmoke)
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
        _canvas.ToastAge += dt;
        if (_session.Fsm.Phase == SessionPhase.InWorld)
        {
            TickMove();
            TrackSafeZone();
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
        _nextMove = DateTime.UtcNow.AddSeconds(PlayableTuning.MoveCooldownSec);
        try
        {
            var sync = await _session.MoveAsync(x, y, dir);
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

    private void OnWorld(Avalonia.Point point, bool right)
    {
        if (_session.Fsm.Phase != SessionPhase.InWorld)
            return;
        var self = _session.World.Self;
        if (self == null)
            return;
        string? hit = _canvas.HitTest(point);
        if (right)
        {
            if (hit != null)
                _session.World.SelectedId = hit;
            _ = Attack(SelectedMonsterOrNearest());
            return;
        }

        if (hit != null)
        {
            var actor = _session.World.Get(hit);
            _session.World.SelectedId = hit;
            if (actor == null || actor.IsSelf)
                return;
            int dist = GridMath.Chebyshev(self.GridX, self.GridY, actor.GridX, actor.GridY);
            if (actor.Kind == EntityKind.EntityLoot)
            {
                BeginApproach(hit, attack: false, dist, PlayableTuning.PickupRange);
                return;
            }
            if (actor.Kind == EntityKind.EntityMonster)
            {
                BeginApproach(hit, attack: true, dist, PlayableTuning.AttackRange);
                return;
            }
            if (actor.Kind == EntityKind.EntityNpc)
            {
                if (dist <= PlayableTuning.TalkRange)
                    _ = Talk(hit);
                else
                {
                    ClearApproach();
                    _pendingTalk = true;
                    _pendingId = hit;
                    _session.World.SetGoal(actor.GridX, actor.GridY);
                    Toast("靠近 " + actor.Name);
                }
            }
            return;
        }

        if (_canvas.TryScreenToGrid(point, out int gx, out int gy))
        {
            ClearApproach();
            _session.World.SetGoal(gx, gy);
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

    private async Task Attack(string? targetId)
    {
        if (string.IsNullOrEmpty(targetId))
        {
            Toast("附近没有可攻击目标");
            return;
        }
        var self = _session.World.Self;
        if (self != null && self.InSafeZone)
        {
            Toast("安全区内无法攻击");
            return;
        }
        if (DateTime.UtcNow < _nextAttack)
            return;
        _nextAttack = DateTime.UtcNow.AddSeconds(PlayableTuning.AttackCooldownSec);
        _audio.PlayAttack();
        try
        {
            var resp = await _session.AttackAsync(targetId);
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
            if (resp != null && resp.Success && !string.IsNullOrWhiteSpace(resp.Dialogue))
                Toast(resp.Dialogue);
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
        if (_session.World.Self != null && cs.TargetEntityId == _session.World.Self.EntityId && cs.TargetDied)
            Toast("你已死亡（服务器将复活到出生点）");
    }

    private void TrackSafeZone()
    {
        bool safe = _session.World.Self?.InSafeZone ?? false;
        if (_lastSafe == safe)
            return;
        _lastSafe = safe;
        Toast(safe ? "进入安全区" : "离开安全区");
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

        if (mode != ClientUiMode.InWorld)
            return;

        var self = _session.World.Self;
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
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += async (_, _) =>
            {
                _audio.PlayUiClick();
                try { await _session.EnterAsync(captured); }
                catch (Exception ex) { _charStatus.Text = ex.Message; }
            };
            _charList.Children.Add(button);
        }
    }

    private void RebuildBag()
    {
        _bagItems.Children.Clear();
        _bagGold.Text = $"金币 {_session.Inventory.Gold}";
        if (_session.Inventory.Items.Count == 0)
        {
            _bagItems.Children.Add(new TextBlock { Text = "背包是空的", Foreground = Brushes.White });
            return;
        }
        foreach (var item in _session.Inventory.Items)
        {
            var id = item.ItemId;
            var button = new Button
            {
                Content = $"{item.ItemId}  x{item.Count}   （点击使用）",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += async (_, _) =>
            {
                _audio.PlayUiClick();
                try
                {
                    string? err = await _session.UseItemAsync(id);
                    Toast(err ?? $"使用 {id}");
                }
                catch (Exception ex)
                {
                    Toast(ex.Message);
                }
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
        switch (e.Key)
        {
            case Key.Space:
            case Key.J:
                _ = Attack(SelectedMonsterOrNearest());
                e.Handled = true;
                break;
            case Key.Tab:
                var next = _session.World.CycleMonster(_session.World.SelectedId);
                _session.World.SelectedId = next;
                if (next != null)
                    Toast("目标 " + (_session.World.Get(next)?.Name ?? next));
                e.Handled = true;
                break;
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
        var channel = _chatNearby ? ChatChannel.ChatNearby : ChatChannel.ChatWorld;
        _ = SendChat(channel, text);
    }

    private async Task SendChat(ChatChannel channel, string text)
    {
        try { await _session.SendChatAsync(channel, text); }
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
        var title = new TextBlock
        {
            Text = "LEGEND",
            FontSize = 42,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#E8C872"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var sub = new TextBlock
        {
            Text = "自研客户端 · 协议 1.21.0 · 无 Unity",
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("#D8C7A2"),
            Margin = new Avalonia.Thickness(0, 0, 0, 12)
        };
        var button = new Button { Content = "登录并进入选角", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += async (_, _) =>
        {
            try { await DoLogin(); }
            catch (Exception ex) { _loginStatus.Text = ex.Message; }
        };
        var panel = new StackPanel
        {
            Spacing = 8,
            Width = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(title);
        panel.Children.Add(sub);
        panel.Children.Add(Labeled("主机", _host));
        panel.Children.Add(Labeled("端口", _port));
        panel.Children.Add(new TextBlock { Text = "区服 " + _options.RealmId, Foreground = Brush("#C8B48A") });
        panel.Children.Add(Labeled("账号", _user));
        panel.Children.Add(Labeled("密码", _pass));
        panel.Children.Add(button);
        panel.Children.Add(_loginStatus);
        panel.Children.Add(new TextBlock
        {
            Text = "公网试玩 150.158.164.90:7100   realm-classic-01",
            FontSize = 12,
            Foreground = Brush("#B7A78A"),
            TextWrapping = TextWrapping.Wrap
        });
        return Card(panel, "#F014100E");
    }

    private Border BuildChars()
    {
        var create = new Button { Content = "创建角色", HorizontalAlignment = HorizontalAlignment.Stretch };
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
            try { await _session.CreateCharacterAsync(name, classId); }
            catch (Exception ex) { _charStatus.Text = ex.Message; }
            _charCount = -1;
        };
        var panel = new StackPanel { Spacing = 8, Width = 460, Margin = new Avalonia.Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "选择角色", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
        panel.Children.Add(new ScrollViewer { Content = _charList, MaxHeight = 240 });
        panel.Children.Add(_newName);
        panel.Children.Add(_classBox);
        panel.Children.Add(create);
        panel.Children.Add(_charStatus);
        var wrap = Card(panel, "#F014100E");
        wrap.HorizontalAlignment = HorizontalAlignment.Center;
        wrap.VerticalAlignment = VerticalAlignment.Center;
        wrap.Padding = new Avalonia.Thickness(0);
        return wrap;
    }

    private Grid BuildHud()
    {
        var bars = new StackPanel { Spacing = 4, Width = 280 };
        bars.Children.Add(_hpText);
        bars.Children.Add(_hpBar);
        bars.Children.Add(_mpText);
        bars.Children.Add(_mpBar);
        bars.Children.Add(_vitals);
        var topLeft = new Border
        {
            Background = Brush("#AA1A140F"),
            Padding = new Avalonia.Thickness(10),
            Margin = new Avalonia.Thickness(12),
            Child = bars,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var topRight = new Border
        {
            Background = Brush("#AA1A140F"),
            Padding = new Avalonia.Thickness(10),
            Margin = new Avalonia.Thickness(12),
            Child = _target,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        var worldBtn = Small("世界");
        var nearBtn = Small("附近");
        worldBtn.Click += (_, _) => { _chatNearby = false; _audio.PlayUiClick(); worldBtn.FontWeight = FontWeight.Bold; nearBtn.FontWeight = FontWeight.Normal; };
        nearBtn.Click += (_, _) => { _chatNearby = true; _audio.PlayUiClick(); nearBtn.FontWeight = FontWeight.Bold; worldBtn.FontWeight = FontWeight.Normal; };
        worldBtn.FontWeight = FontWeight.Bold;
        var channel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        channel.Children.Add(worldBtn);
        channel.Children.Add(nearBtn);
        var chat = new StackPanel { Spacing = 4, Width = 440 };
        chat.Children.Add(channel);
        chat.Children.Add(_chatScroll);
        chat.Children.Add(_chatInput);
        chat.Children.Add(_hint);
        var bottom = new Border
        {
            Background = Brush("#CC16110E"),
            Padding = new Avalonia.Thickness(8),
            Margin = new Avalonia.Thickness(12, 0, 0, 12),
            Child = chat,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var grid = new Grid();
        grid.Children.Add(topLeft);
        grid.Children.Add(topRight);
        grid.Children.Add(bottom);
        return grid;
    }

    private Border BuildBag()
    {
        var stack = new StackPanel { Spacing = 6, Width = 240 };
        stack.Children.Add(new TextBlock { Text = "背包", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brush("#E8C872") });
        stack.Children.Add(_bagGold);
        stack.Children.Add(new ScrollViewer { Content = _bagItems, MaxHeight = 320 });
        var border = new Border
        {
            Background = Brush("#EE1C1612"),
            BorderBrush = Brush("#E8C872"),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(10),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 16, 80)
        };
        return border;
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
        var card = Card(stack, "#F01A1210");
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        return card;
    }

    private static readonly ClassChoice[] ClassChoices =
    {
        new("warrior", "武者"),
        new("mage", "术士"),
        new("taoist", "灵师")
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
        Height = 12,
        Foreground = Brush(hex)
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
