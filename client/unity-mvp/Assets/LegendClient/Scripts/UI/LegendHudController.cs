#if UNITY_2019_1_OR_NEWER
using System.Collections.Generic;
using Legend.Client.Audio;
using System.Text;
using Legend.Client.Combat;
using Legend.Client.Config;
using Legend.Client.Input;
using Legend.Client.Session;
using Legend.Client.World;
using Legend.ClientCore;
using Legend.Contracts.Proto;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Legend.Client.UI
{
    /// <summary>
    /// Phase G+K uGUI shell: Login / CharSelect / HUD (HP/MP/minimap) / Bag / Chat / Joystick(mobile) / Attack / Reconnect + PC hotkeys.
    /// Built entirely at runtime (no Canvas prefabs).
    /// </summary>
    public sealed class LegendHudController : MonoBehaviour
    {
        private LegendSessionService _session = null!;
        private LegendClientConfig _config = null!;
        private LegendCombatController _combat = null!;
        private LegendWorldPresenter _world = null!;
        private LegendMvpInput _input = null!;
        private LegendToastOverlay _toast = null!;
        private LegendVirtualJoystickUi _joystick = null!;
        private LegendAudioService? _audio;

        private Canvas _canvas = null!;
        private GameObject _loginRoot = null!;
        private GameObject _charRoot = null!;
        private GameObject _hudRoot = null!;
        private GameObject _reconnectRoot = null!;

        private InputField _hostField = null!;
        private InputField _portField = null!;
        private InputField _userField = null!;
        private InputField _passField = null!;
        private Text _loginStatus = null!;

        private Transform _charListContent = null!;
        private InputField _newNameField = null!;
        private InputField _newClassField = null!;
        private Text _charStatus = null!;
        private readonly List<GameObject> _charRows = new List<GameObject>();

        private Text _hpText = null!;
        private Image _hpFill = null!;
        private Image _mpFill = null!;
        private Text _mpText = null!;
        private Text _minimapText = null!;
        private Text _pcHint = null!;
        private Text _vitalsText = null!;
        private Text _targetText = null!;
        private Text _safeBadge = null!;
        private GameObject _bagPanel = null!;
        private Text _bagText = null!;
        private Text _chatLogText = null!;
        private InputField _chatInput = null!;
        private int _chatChannel; // 0 world, 1 nearby
        private Button _chWorld = null!;
        private Button _chNearby = null!;
        private Text _statusBar = null!;

        private bool _bagOpen = true;
        private bool _attackUiPressed;
        private bool _pickupUiPressed;
        private bool? _lastSafeZone;
        private bool _wasDead;
        private ClientUiMode _lastMode = ClientUiMode.Hidden;
        private int _lastCharCount = -1;
        private int _lastChatCount = -1;
        private string _lastBagFingerprint = "";

        public void Bind(
            LegendSessionService session,
            LegendClientConfig config,
            LegendCombatController combat,
            LegendWorldPresenter world,
            LegendMvpInput input,
            LegendAudioService? audio = null)
        {
            _session = session;
            _config = config;
            _combat = combat;
            _world = world;
            _input = input;
            _audio = audio;
            BuildUi();
            _session.StateChanged += RefreshFromSession;
            RefreshFromSession();
        }

        public bool ConsumeAttackUiPressed()
        {
            if (!_attackUiPressed) return false;
            _attackUiPressed = false;
            return true;
        }

        public bool ConsumePickupUiPressed()
        {
            if (!_pickupUiPressed) return false;
            _pickupUiPressed = false;
            return true;
        }

        public LegendVirtualJoystickUi? Joystick => _joystick;

        public void ShowToast(string msg, Color? tint = null, float seconds = 2.2f) => _toast?.Show(msg, tint, seconds);

        private void Update()
        {
            if (_session == null) return;
            var mode = ClientUiModeResolver.FromPhase(_session.Fsm.Phase);
            if (mode != _lastMode)
            {
                ApplyMode(mode);
                _lastMode = mode;
            }

            if (mode == ClientUiMode.InWorld)
            {
                TickInWorldHud();
                HandlePcHotkeys();
            }

            // Feed uGUI stick into shared input (mobile only — hidden on PC)
            if (_joystick != null && _joystick.Active)
                _input.SetExternalStick(_joystick.AxisX, _joystick.AxisY);
            else
                _input.ClearExternalStick();
        }

        private void HandlePcHotkeys()
        {
            if (_input.ToggleBagPressed)
            {
                _bagOpen = !_bagOpen;
                if (_bagPanel != null) _bagPanel.SetActive(_bagOpen);
                _audio?.PlayUiClick();
            }
            if (_input.EscapePressed)
            {
                if (_bagOpen)
                {
                    _bagOpen = false;
                    if (_bagPanel != null) _bagPanel.SetActive(false);
                    _audio?.PlayUiClick();
                }
                else if (_chatInput != null && _chatInput.isFocused)
                {
                    _chatInput.DeactivateInputField();
                }
            }
            if (_input.ChatFocusPressed && _chatInput != null)
            {
                if (_chatInput.isFocused)
                    SendChat();
                else
                {
                    _chatInput.ActivateInputField();
                    _chatInput.Select();
                }
            }
        }

        private void BuildUi()
        {
            LegendUiBuilder.EnsureEventSystem();
            _canvas = LegendUiBuilder.CreateOverlayCanvas("LegendUgUiCanvas", 200);
            _canvas.transform.SetParent(transform, false);

            _statusBar = LegendUiBuilder.Text(_canvas.transform, "StatusBar", "Legend", 14,
                TextAnchor.UpperLeft, new Color(0.85f, 0.9f, 1f));
            var sbRt = _statusBar.GetComponent<RectTransform>();
            LegendUiBuilder.SetAnchored(sbRt, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -14), new Vector2(-24, 28));
            sbRt.offsetMin = new Vector2(12, -28);
            sbRt.offsetMax = new Vector2(-12, -4);

            BuildLogin();
            BuildCharSelect();
            BuildHud();
            BuildReconnect();

            _toast = gameObject.AddComponent<LegendToastOverlay>();
            _toast.Build(_canvas.transform);
        }

        private void BuildLogin()
        {
            // Full-screen dim backdrop (classic launcher feel)
            var backdrop = LegendUiBuilder.Panel(_canvas.transform, "LoginBackdrop",
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.04f, 0.05f, 0.08f, 0.92f));
            var banner = LegendUiBuilder.Text(backdrop, "Banner", "LEGEND · 自研传奇试玩", 28,
                TextAnchor.UpperCenter, new Color(0.95f, 0.82f, 0.35f), true);
            var bannerRt = banner.GetComponent<RectTransform>();
            LegendUiBuilder.SetAnchored(bannerRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0, -48), new Vector2(640, 40));

            var panel = LegendUiBuilder.Panel(backdrop, "LoginPanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                new Color(0.09f, 0.10f, 0.14f, 0.96f));
            LegendUiBuilder.SetAnchored(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(440, 380));
            _loginRoot = backdrop.gameObject;

            LegendUiBuilder.Text(panel, "Title", "登录 / 注册", 26, TextAnchor.UpperCenter, Color.white, true)
                .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -18);

            PlaceLabeledField(panel, "Host", "主机", _config.Host, false, new Vector2(0, 90), out _hostField);
            PlaceLabeledField(panel, "Port", "端口", _config.Port.ToString(), false, new Vector2(0, 40), out _portField);
            PlaceLabeledField(panel, "User", "账号", _config.DefaultUsername, false, new Vector2(0, -10), out _userField);
            PlaceLabeledField(panel, "Pass", "密码", _config.DefaultPassword, true, new Vector2(0, -60), out _passField);

            var btnRt = LegendUiBuilder.Panel(panel, "LoginBtnWrap",
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(btnRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 70), new Vector2(280, 48));
            var loginBtn = LegendUiBuilder.Button(btnRt, "LoginBtn", "连接并登录",
                new Color(0.15f, 0.55f, 0.35f, 0.95f));
            loginBtn.onClick.AddListener(() =>
            {
                _audio?.PlayUiClick();
                ApplyHostPortFromFields();
                _ = _session.LoginOrRegisterAsync(_userField.text, _passField.text);
            });

            _loginStatus = LegendUiBuilder.Text(panel, "LoginStatus", "首次登录自动注册", 14,
                TextAnchor.LowerCenter, new Color(0.7f, 0.75f, 0.8f));
            var ls = _loginStatus.GetComponent<RectTransform>();
            LegendUiBuilder.SetAnchored(ls, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 22), new Vector2(-20, 36));
        }

        private void BuildCharSelect()
        {
            var panel = LegendUiBuilder.Panel(_canvas.transform, "CharPanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                new Color(0.07f, 0.08f, 0.11f, 0.94f));
            LegendUiBuilder.SetAnchored(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(460, 480));
            _charRoot = panel.gameObject;

            LegendUiBuilder.Text(panel, "Title", "选择 / 创建角色", 24, TextAnchor.UpperCenter, Color.white, true);

            var listBg = LegendUiBuilder.Panel(panel, "CharListBg",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero,
                new Color(0.1f, 0.11f, 0.14f, 0.95f));
            LegendUiBuilder.SetAnchored(listBg, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0, -160), new Vector2(420, 220));
            _charListContent = listBg;

            PlaceLabeledField(panel, "NewName", "名字", _config.DefaultCharName, false, new Vector2(0, -140), out _newNameField);
            PlaceLabeledField(panel, "NewClass", "职业", _config.DefaultClassId, false, new Vector2(0, -190), out _newClassField);

            var createWrap = LegendUiBuilder.Panel(panel, "CreateWrap",
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(createWrap, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 70), new Vector2(280, 44));
            var createBtn = LegendUiBuilder.Button(createWrap, "CreateBtn", "创建角色",
                new Color(0.35f, 0.4f, 0.75f, 0.95f));
            createBtn.onClick.AddListener(() =>
                _ = _session.CreateCharAsync(_newNameField.text, _newClassField.text));

            _charStatus = LegendUiBuilder.Text(panel, "CharStatus", "", 14,
                TextAnchor.LowerCenter, new Color(0.75f, 0.8f, 0.85f));
            var cs = _charStatus.GetComponent<RectTransform>();
            LegendUiBuilder.SetAnchored(cs, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 22), new Vector2(-20, 36));
        }

        private void BuildHud()
        {
            _hudRoot = new GameObject("HudRoot", typeof(RectTransform));
            _hudRoot.transform.SetParent(_canvas.transform, false);
            LegendUiBuilder.Stretch(_hudRoot.GetComponent<RectTransform>());

            // Vitals card (HP + MP + level)
            var vitals = LegendUiBuilder.Panel(_hudRoot.transform, "Vitals",
                new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero,
                new Color(0.06f, 0.07f, 0.1f, 0.9f));
            LegendUiBuilder.SetAnchored(vitals, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(175, -108), new Vector2(330, 150));

            var hpBg = LegendUiBuilder.Panel(vitals, "HpBg",
                new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                new Color(0.15f, 0.15f, 0.18f, 1f));
            LegendUiBuilder.SetAnchored(hpBg, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -28), new Vector2(-20, 16));
            hpBg.offsetMin = new Vector2(10, -34);
            hpBg.offsetMax = new Vector2(-10, -18);
            _hpFill = LegendUiBuilder.Image(hpBg, "HpFill", new Color(0.25f, 0.85f, 0.35f, 1f));
            var fillRt = _hpFill.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0, 0);
            fillRt.anchorMax = new Vector2(1, 1);
            fillRt.pivot = new Vector2(0, 0.5f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _hpText = LegendUiBuilder.Text(hpBg, "HpText", "HP 0/0", 13, TextAnchor.MiddleCenter, Color.white);

            var mpBg = LegendUiBuilder.Panel(vitals, "MpBg",
                new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                new Color(0.12f, 0.14f, 0.22f, 1f));
            LegendUiBuilder.SetAnchored(mpBg, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -50), new Vector2(-20, 14));
            mpBg.offsetMin = new Vector2(10, -56);
            mpBg.offsetMax = new Vector2(-10, -40);
            _mpFill = LegendUiBuilder.Image(mpBg, "MpFill", new Color(0.25f, 0.45f, 0.95f, 1f));
            var mpFillRt = _mpFill.GetComponent<RectTransform>();
            mpFillRt.anchorMin = new Vector2(0, 0);
            mpFillRt.anchorMax = new Vector2(1, 1);
            mpFillRt.pivot = new Vector2(0, 0.5f);
            mpFillRt.offsetMin = Vector2.zero;
            mpFillRt.offsetMax = Vector2.zero;
            _mpText = LegendUiBuilder.Text(mpBg, "MpText", "MP —", 12, TextAnchor.MiddleCenter, Color.white);

            _vitalsText = LegendUiBuilder.Text(vitals, "VitalsText", "Lv 1  Gold 0", 14,
                TextAnchor.UpperLeft, new Color(0.9f, 0.92f, 0.95f));
            var vt = _vitalsText.GetComponent<RectTransform>();
            LegendUiBuilder.SetAnchored(vt, new Vector2(0, 0), new Vector2(1, 1),
                Vector2.zero, Vector2.zero);
            vt.offsetMin = new Vector2(12, 8);
            vt.offsetMax = new Vector2(-12, -62);

            // Minimap placeholder (top-right)
            var mini = LegendUiBuilder.Panel(_hudRoot.transform, "Minimap",
                new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.08f, 0.06f, 0.88f));
            LegendUiBuilder.SetAnchored(mini, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-95, -95), new Vector2(150, 150));
            LegendUiBuilder.Text(mini, "MiniTitle", "小地图", 12, TextAnchor.UpperCenter,
                new Color(0.75f, 0.85f, 0.7f), true);
            _minimapText = LegendUiBuilder.Text(mini, "MiniBody", "(x,y)", 16, TextAnchor.MiddleCenter,
                new Color(0.55f, 0.95f, 0.55f), true);

            _pcHint = LegendUiBuilder.Text(_hudRoot.transform, "PcHint",
                "WASD移动 · 左键点地/选怪 · 右键/空格/J攻击 · Tab切目标 · B背包 · Enter聊天 · Z拾取",
                12, TextAnchor.LowerCenter, new Color(0.75f, 0.78f, 0.82f));
            var ph = _pcHint.GetComponent<RectTransform>();
            LegendUiBuilder.SetAnchored(ph, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 14), new Vector2(900, 22));

            _targetText = LegendUiBuilder.Text(vitals, "Target", "Target: (none)", 13,
                TextAnchor.LowerLeft, new Color(1f, 0.85f, 0.4f));
            var tt = _targetText.GetComponent<RectTransform>();
            LegendUiBuilder.SetAnchored(tt, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 14), new Vector2(-16, 24));

            _safeBadge = LegendUiBuilder.Text(_hudRoot.transform, "SafeBadge", "", 16,
                TextAnchor.MiddleCenter, new Color(0.4f, 0.95f, 0.7f), true);
            var sb = _safeBadge.GetComponent<RectTransform>();
            LegendUiBuilder.SetAnchored(sb, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0, -48), new Vector2(220, 32));

            // Bag toggle + panel
            var bagBtnWrap = LegendUiBuilder.Panel(_hudRoot.transform, "BagBtnWrap",
                new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(bagBtnWrap, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-200, -50), new Vector2(100, 36));
            var bagBtn = LegendUiBuilder.Button(bagBtnWrap, "BagBtn", "背包",
                new Color(0.25f, 0.28f, 0.35f, 0.95f));
            bagBtn.onClick.AddListener(() =>
            {
                _audio?.PlayUiClick();
                _bagOpen = !_bagOpen;
                if (_bagPanel != null) _bagPanel.SetActive(_bagOpen);
            });

            var bag = LegendUiBuilder.Panel(_hudRoot.transform, "BagPanel",
                new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                new Color(0.06f, 0.07f, 0.1f, 0.92f));
            LegendUiBuilder.SetAnchored(bag, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-280, -220), new Vector2(240, 220));
            _bagPanel = bag.gameObject;
            LegendUiBuilder.Text(bag, "BagTitle", "背包", 18, TextAnchor.UpperCenter, Color.white, true);
            _bagText = LegendUiBuilder.Text(bag, "BagBody", "(empty)", 14, TextAnchor.UpperLeft,
                new Color(0.9f, 0.92f, 0.95f));
            var bt = _bagText.GetComponent<RectTransform>();
            bt.offsetMin = new Vector2(12, 12);
            bt.offsetMax = new Vector2(-12, -36);

            // Chat
            var chat = LegendUiBuilder.Panel(_hudRoot.transform, "ChatPanel",
                new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.06f, 0.08f, 0.9f));
            LegendUiBuilder.SetAnchored(chat, new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(230, 120), new Vector2(440, 200));

            var chRow = LegendUiBuilder.Panel(chat, "Channels",
                new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(chRow, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -22), new Vector2(-12, 32));
            var wWrap = LegendUiBuilder.Panel(chRow, "W", new Vector2(0, 0), new Vector2(0.5f, 1),
                new Vector2(4, 2), new Vector2(-2, -2));
            _chWorld = LegendUiBuilder.Button(wWrap, "World", "世界", new Color(0.2f, 0.45f, 0.7f, 0.95f));
            var nWrap = LegendUiBuilder.Panel(chRow, "N", new Vector2(0.5f, 0), new Vector2(1, 1),
                new Vector2(2, 2), new Vector2(-4, -2));
            _chNearby = LegendUiBuilder.Button(nWrap, "Nearby", "附近", new Color(0.25f, 0.3f, 0.35f, 0.95f));
            _chWorld.onClick.AddListener(() => SetChatChannel(0));
            _chNearby.onClick.AddListener(() => SetChatChannel(1));

            var logArea = LegendUiBuilder.Panel(chat, "Log",
                new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                new Color(0.08f, 0.09f, 0.11f, 0.85f));
            logArea.offsetMin = new Vector2(8, 44);
            logArea.offsetMax = new Vector2(-8, -40);
            _chatLogText = LegendUiBuilder.Text(logArea, "Lines", "", 13, TextAnchor.UpperLeft,
                new Color(0.88f, 0.9f, 0.93f));

            var inputWrap = LegendUiBuilder.Panel(chat, "InputWrap",
                new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(inputWrap, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 22), new Vector2(-12, 36));
            inputWrap.offsetMin = new Vector2(8, 6);
            inputWrap.offsetMax = new Vector2(-70, 38);
            _chatInput = LegendUiBuilder.Input(inputWrap, "ChatInput", "输入聊天…");

            var sendWrap = LegendUiBuilder.Panel(chat, "SendWrap",
                new Vector2(1, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(sendWrap, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-38, 22), new Vector2(60, 36));
            var sendBtn = LegendUiBuilder.Button(sendWrap, "Send", "发送",
                new Color(0.2f, 0.5f, 0.35f, 0.95f));
            sendBtn.onClick.AddListener(SendChat);

            // Attack + Pickup
            var atkWrap = LegendUiBuilder.Panel(_hudRoot.transform, "AttackWrap",
                new Vector2(1, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(atkWrap, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-90, 140), new Vector2(110, 110));
            var atk = LegendUiBuilder.Button(atkWrap, "Attack", "攻击\n(Space)",
                new Color(0.75f, 0.22f, 0.18f, 0.92f));
            atk.onClick.AddListener(() => _attackUiPressed = true);

            var pickWrap = LegendUiBuilder.Panel(_hudRoot.transform, "PickupWrap",
                new Vector2(1, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(pickWrap, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-90, 40), new Vector2(110, 70));
            var pick = LegendUiBuilder.Button(pickWrap, "Pickup", "拾取",
                new Color(0.75f, 0.55f, 0.15f, 0.92f));
            pick.onClick.AddListener(() => _pickupUiPressed = true);

            _joystick = _hudRoot.AddComponent<LegendVirtualJoystickUi>();
            _joystick.Build(_hudRoot.GetComponent<RectTransform>());
            // Phase K: hide large virtual stick on PC Standalone
            if (LegendMvpInput.IsPcStandalone)
                _joystick.SetVisible(false);

            SetChatChannel(0);
        }

        private void BuildReconnect()
        {
            var panel = LegendUiBuilder.Panel(_canvas.transform, "ReconnectPanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                new Color(0.08f, 0.06f, 0.06f, 0.95f));
            LegendUiBuilder.SetAnchored(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(360, 200));
            _reconnectRoot = panel.gameObject;
            LegendUiBuilder.Text(panel, "Title", "与服务器断开", 22, TextAnchor.UpperCenter, Color.white, true);

            var r1 = LegendUiBuilder.Panel(panel, "R1", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(r1, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 10), new Vector2(240, 44));
            var reconnectBtn = LegendUiBuilder.Button(r1, "Reconnect", "重新连接",
                new Color(0.2f, 0.55f, 0.35f, 0.95f));
            reconnectBtn.onClick.AddListener(() => _ = _session.ReconnectAsync());

            var r2 = LegendUiBuilder.Panel(panel, "R2", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(r2, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -50), new Vector2(240, 44));
            var backBtn = LegendUiBuilder.Button(r2, "BackLogin", "返回登录",
                new Color(0.35f, 0.35f, 0.4f, 0.95f));
            backBtn.onClick.AddListener(() =>
            {
                _session.DisposeClient();
                _session.Fsm.Reset();
                _world.ClearAll();
                _lastSafeZone = null;
                _wasDead = false;
            });
        }

        private static void PlaceLabeledField(RectTransform parent, string name, string label, string value,
            bool password, Vector2 centerOffset, out InputField field)
        {
            var row = LegendUiBuilder.Panel(parent, name + "Row",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            LegendUiBuilder.SetAnchored(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                centerOffset, new Vector2(360, 40));
            var lab = LegendUiBuilder.Text(row, "L", label, 16, TextAnchor.MiddleLeft, new Color(0.75f, 0.8f, 0.85f));
            var labRt = lab.GetComponent<RectTransform>();
            LegendUiBuilder.SetAnchored(labRt, new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(40, 0), new Vector2(70, 0));
            var fieldWrap = LegendUiBuilder.Panel(row, "F", new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(80, 2), new Vector2(-8, -2));
            field = LegendUiBuilder.Input(fieldWrap, name, label, password);
            field.text = value;
        }

        private void ApplyMode(ClientUiMode mode)
        {
            if (_loginRoot != null) _loginRoot.SetActive(mode == ClientUiMode.Login);
            if (_charRoot != null) _charRoot.SetActive(mode == ClientUiMode.CharSelect);
            if (_hudRoot != null) _hudRoot.SetActive(mode == ClientUiMode.InWorld);
            if (_reconnectRoot != null) _reconnectRoot.SetActive(mode == ClientUiMode.Reconnecting);
            if (mode == ClientUiMode.CharSelect)
                RebuildCharList();
        }

        private void RefreshFromSession()
        {
            if (_statusBar != null)
            {
                var err = string.IsNullOrEmpty(_session.Fsm.ErrorMessage)
                    ? ""
                    : "  |  " + _session.Fsm.ErrorMessage;
                _statusBar.text = $"Legend 可玩PC  |  {_session.Fsm.Phase}  |  {_session.Fsm.StatusMessage}{err}";
            }
            if (_loginStatus != null)
            {
                _loginStatus.text = string.IsNullOrEmpty(_session.Fsm.ErrorMessage)
                    ? _session.Fsm.StatusMessage
                    : _session.Fsm.ErrorMessage;
                _loginStatus.color = string.IsNullOrEmpty(_session.Fsm.ErrorMessage)
                    ? new Color(0.7f, 0.75f, 0.8f)
                    : new Color(1f, 0.45f, 0.4f);
            }
            if (_charStatus != null)
                _charStatus.text = _session.Fsm.StatusMessage;
            if (_session.Fsm.Phase is SessionPhase.CharSelect or SessionPhase.EnteringWorld)
                RebuildCharList(force: true);
            RefreshChatLog();
            RefreshBag();
        }

        private void TickInWorldHud()
        {
            var client = _session.Client;
            var self = client?.SelfEntity;
            int hp = self?.Hp ?? 0;
            int maxHp = self != null && self.MaxHp > 0 ? self.MaxHp : Mathf.Max(1, client?.EffectiveMaxHp ?? 1);
            float ratio = Mathf.Clamp01((float)hp / maxHp);
            if (_hpFill != null)
            {
                _hpFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
                _hpFill.color = ratio > 0.4f ? new Color(0.25f, 0.85f, 0.35f) :
                    ratio > 0.15f ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.9f, 0.25f, 0.2f);
            }
            if (_hpText != null) _hpText.text = $"HP {hp}/{maxHp}";

            // MP: EntitySnapshot has no mp; show placeholder bar full until protocol HUD expands
            // EntitySnapshot has no Mp; show full blue bar as mana frame until client exposes EffectiveMaxMp
            int maxMpEst = Mathf.Max(50, (client?.EffectiveMaxHp ?? 100) / 2);
            if (_mpFill != null)
                _mpFill.rectTransform.anchorMax = new Vector2(1f, 1f);
            if (_mpText != null) _mpText.text = $"MP ?/{maxMpEst}";

            int lv = client?.Level ?? 1;
            int gold = client?.Gold ?? _session.Inventory.Gold;
            string map = self?.MapId ?? "?";
            string pos = self != null ? $"({self.X},{self.Y})" : "";
            if (_vitalsText != null)
                _vitalsText.text = $"Lv {lv}   Gold {gold}\nMap {map} {pos}\nATK {client?.EffectiveAtk ?? 0}  DEF {client?.EffectiveDef ?? 0}";
            if (_minimapText != null)
                _minimapText.text = self != null ? $"{self.X}\n{self.Y}" : "?";

            var sel = _world.SelectedEntityId;
            if (_targetText != null)
            {
                if (string.IsNullOrEmpty(sel))
                    _targetText.text = "目标: (无 — 点击怪物 / 攻击)";
                else
                {
                    var v = _world.Get(sel);
                    _targetText.text = v != null
                        ? $"目标: {v.DisplayName}  HP {v.Hp}/{v.MaxHp}"
                        : "目标: " + sel;
                }
            }

            bool inSafe = self?.InSafeZone ?? false;
            if (_safeBadge != null)
            {
                _safeBadge.text = inSafe ? "◆ 安全区" : "";
                _safeBadge.color = new Color(0.4f, 0.95f, 0.7f);
            }
            if (_lastSafeZone.HasValue && _lastSafeZone.Value != inSafe)
            {
                if (inSafe)
                    _toast.Show("进入安全区 — 无法攻击", new Color(0.1f, 0.35f, 0.25f, 0.92f));
                else
                    _toast.Show("离开安全区 — 注意危险", new Color(0.4f, 0.2f, 0.1f, 0.92f));
            }
            _lastSafeZone = inSafe;

            bool dead = self != null && maxHp > 0 && hp <= 0;
            if (dead && !_wasDead)
                _toast.Show("你已死亡", new Color(0.45f, 0.08f, 0.08f, 0.95f), 3.5f);
            _wasDead = dead;

            RefreshChatLog();
            RefreshBag();
        }

        private void RebuildCharList(bool force = false)
        {
            if (_charListContent == null) return;
            int count = _session.Characters.Count;
            if (!force && count == _lastCharCount) return;
            _lastCharCount = count;
            foreach (var go in _charRows)
                if (go != null) Destroy(go);
            _charRows.Clear();

            float y = -8f;
            foreach (var c in _session.Characters)
            {
                var row = LegendUiBuilder.Panel(_charListContent, "Char_" + c.CharacterId,
                    new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
                LegendUiBuilder.SetAnchored(row, new Vector2(0, 1), new Vector2(1, 1),
                    new Vector2(0, y - 18), new Vector2(-12, 36));
                row.offsetMin = new Vector2(8, y - 36);
                row.offsetMax = new Vector2(-8, y);
                var label = $"{c.Name}  Lv{c.Level}  {c.ClassId}  @ {c.MapId}";
                var btn = LegendUiBuilder.Button(row, "Enter", label, new Color(0.18f, 0.32f, 0.48f, 0.95f));
                var captured = c;
                btn.onClick.AddListener(() => _ = _session.SelectAndEnterAsync(captured));
                _charRows.Add(row.gameObject);
                y -= 42f;
            }
            if (count == 0 && _charStatus != null)
                _charStatus.text = "暂无角色 — 请先创建";
        }

        private void RefreshChatLog()
        {
            if (_chatLogText == null) return;
            int n = _session.ChatLog.Lines.Count;
            if (n == _lastChatCount) return;
            _lastChatCount = n;
            var sb = new StringBuilder();
            foreach (var line in _session.ChatLog.Lines)
                sb.AppendLine(line);
            _chatLogText.text = sb.ToString();
        }

        private void RefreshBag()
        {
            if (_bagText == null) return;
            var sb = new StringBuilder();
            sb.AppendLine($"金币: {_session.Inventory.Gold}");
            if (_session.Inventory.Items.Count == 0)
                sb.AppendLine("(空)");
            else
                foreach (var it in _session.Inventory.Items)
                    sb.AppendLine($"{it.ItemId}  x{it.Count}");
            var fp = sb.ToString();
            if (fp == _lastBagFingerprint) return;
            _lastBagFingerprint = fp;
            _bagText.text = fp;
        }

        private void SetChatChannel(int ch)
        {
            _chatChannel = ch;
            if (_chWorld != null)
                _chWorld.GetComponent<Image>().color = ch == 0
                    ? new Color(0.2f, 0.45f, 0.7f, 0.95f)
                    : new Color(0.25f, 0.3f, 0.35f, 0.95f);
            if (_chNearby != null)
                _chNearby.GetComponent<Image>().color = ch == 1
                    ? new Color(0.2f, 0.45f, 0.7f, 0.95f)
                    : new Color(0.25f, 0.3f, 0.35f, 0.95f);
        }

        private void SendChat()
        {
            if (_chatInput == null || string.IsNullOrWhiteSpace(_chatInput.text)) return;
            _audio?.PlayUiClick();
            var channel = _chatChannel == 0 ? ChatChannel.ChatWorld : ChatChannel.ChatNearby;
            _ = _session.SendChatAsync(channel, _chatInput.text);
            _chatInput.text = "";
            _chatInput.DeactivateInputField();
        }

        private void ApplyHostPortFromFields()
        {
            if (_hostField != null) _config.Host = _hostField.text.Trim();
            if (_portField != null && int.TryParse(_portField.text.Trim(), out var p))
                _config.Port = p;
            _session.Configure(_config.Host, _config.Port, _config.Platform, _config.RealmId);
        }

        private void OnDestroy()
        {
            if (_session != null)
                _session.StateChanged -= RefreshFromSession;
        }
    }
}
#endif
