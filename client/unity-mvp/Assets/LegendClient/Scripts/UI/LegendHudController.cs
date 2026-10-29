#if UNITY_2019_1_OR_NEWER
using Legend.Client.Combat;
using Legend.Client.Config;
using Legend.Client.Input;
using Legend.Client.Session;
using Legend.Client.World;
using Legend.ClientCore;
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.Client.UI
{
    /// <summary>
    /// IMGUI HUD: login / char select / in-world (HP·Lv·Gold, bag, chat, attack, reconnect).
    /// Works without Canvas assets — Play Mode ready from an empty scene + Bootstrap.
    /// </summary>
    public sealed class LegendHudController : MonoBehaviour
    {
        private LegendSessionService _session = null!;
        private LegendClientConfig _config = null!;
        private LegendCombatController _combat = null!;
        private LegendWorldPresenter _world = null!;
        private LegendMvpInput _input = null!;

        private string _user = "";
        private string _pass = "";
        private string _newName = "";
        private string _newClass = "warrior";
        private string _chatInput = "";
        private int _chatChannel; // 0 world, 1 nearby
        private bool _bagOpen = true;
        private Vector2 _chatScroll;
        private bool _attackUiPressed;

        public void Bind(
            LegendSessionService session,
            LegendClientConfig config,
            LegendCombatController combat,
            LegendWorldPresenter world,
            LegendMvpInput input)
        {
            _session = session;
            _config = config;
            _combat = combat;
            _world = world;
            _input = input;
            _user = config.DefaultUsername;
            _pass = config.DefaultPassword;
            _newName = config.DefaultCharName;
            _newClass = config.DefaultClassId;
        }

        public bool ConsumeAttackUiPressed()
        {
            if (!_attackUiPressed) return false;
            _attackUiPressed = false;
            return true;
        }

        private void OnGUI()
        {
            if (_session == null) return;
            DrawStatusBar();
            switch (_session.Fsm.Phase)
            {
                case SessionPhase.Disconnected:
                case SessionPhase.Login:
                case SessionPhase.Connecting:
                case SessionPhase.Error:
                    DrawLogin();
                    break;
                case SessionPhase.CharSelect:
                case SessionPhase.EnteringWorld:
                    DrawCharSelect();
                    break;
                case SessionPhase.InWorld:
                    DrawInWorldHud();
                    DrawVirtualStickGizmo();
                    break;
                case SessionPhase.Reconnecting:
                    DrawReconnect();
                    break;
            }
        }

        private void DrawStatusBar()
        {
            GUILayout.BeginArea(new Rect(8, 8, Screen.width - 16, 28));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Legend MVP  |  {_session.Fsm.Phase}  |  {_session.Fsm.StatusMessage}");
            if (!string.IsNullOrEmpty(_session.Fsm.ErrorMessage))
                GUILayout.Label("<color=red>" + _session.Fsm.ErrorMessage + "</color>");
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawLogin()
        {
            GUILayout.BeginArea(new Rect(12, 48, 360, 280), GUI.skin.box);
            GUILayout.Label("Login / Register");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Host", GUILayout.Width(60));
            _config.Host = GUILayout.TextField(_config.Host);
            GUILayout.Label("Port", GUILayout.Width(40));
            var portStr = GUILayout.TextField(_config.Port.ToString(), GUILayout.Width(70));
            if (int.TryParse(portStr, out var p)) _config.Port = p;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("User", GUILayout.Width(60));
            _user = GUILayout.TextField(_user);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pass", GUILayout.Width(60));
            _pass = GUILayout.PasswordField(_pass, '*');
            GUILayout.EndHorizontal();
            _session.Configure(_config.Host, _config.Port, _config.Platform, _config.RealmId);
            GUI.enabled = _session.Fsm.Phase != SessionPhase.Connecting;
            if (GUILayout.Button("Connect / Login", GUILayout.Height(36)))
                _ = _session.LoginOrRegisterAsync(_user, _pass);
            GUI.enabled = true;
            GUILayout.Label("First login auto-registers the account.");
            GUILayout.EndArea();
        }

        private void DrawCharSelect()
        {
            GUILayout.BeginArea(new Rect(12, 48, 400, 420), GUI.skin.box);
            GUILayout.Label("Characters");
            foreach (var c in _session.Characters)
            {
                if (GUILayout.Button($"{c.Name}  Lv{c.Level}  {c.ClassId}  @ {c.MapId} ({c.X},{c.Y})", GUILayout.Height(28)))
                    _ = _session.SelectAndEnterAsync(c);
            }
            GUILayout.Space(10);
            GUILayout.Label("Create");
            _newName = GUILayout.TextField(_newName);
            _newClass = GUILayout.TextField(_newClass);
            if (GUILayout.Button("Create Character"))
                _ = _session.CreateCharAsync(_newName, _newClass);
            GUILayout.EndArea();
        }

        private void DrawReconnect()
        {
            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 160, Screen.height * 0.4f, 320, 120), GUI.skin.box);
            GUILayout.Label("Disconnected from server");
            if (GUILayout.Button("Reconnect", GUILayout.Height(40)))
                _ = _session.ReconnectAsync();
            if (GUILayout.Button("Back to Login"))
            {
                _session.DisposeClient();
                _session.Fsm.Reset();
                _world.ClearAll();
            }
            GUILayout.EndArea();
        }

        private void DrawInWorldHud()
        {
            var client = _session.Client;
            var self = client?.SelfEntity;
            int hp = self?.Hp ?? 0;
            int maxHp = self?.MaxHp > 0 ? self.MaxHp : Mathf.Max(1, client?.EffectiveMaxHp ?? 1);
            int lv = client?.Level ?? 1;
            int gold = client?.Gold ?? _session.Inventory.Gold;
            string map = self?.MapId ?? "?";
            string pos = self != null ? $"({self.X},{self.Y})" : "";

            // Top-left vitals
            GUILayout.BeginArea(new Rect(12, 44, 320, 90), GUI.skin.box);
            GUILayout.Label($"HP {hp}/{maxHp}   Lv {lv}   Gold {gold}");
            GUILayout.Label($"Map {map} {pos}  ATK {client?.EffectiveAtk ?? 0}  DEF {client?.EffectiveDef ?? 0}");
            var sel = _world.SelectedEntityId;
            GUILayout.Label(string.IsNullOrEmpty(sel) ? "Target: (none — click enemy / Attack)" : "Target: " + sel);
            GUILayout.EndArea();

            // Attack button (right)
            if (GUI.Button(new Rect(Screen.width - 120, Screen.height * 0.55f, 100, 100), "ATTACK\n(Space)"))
                _attackUiPressed = true;

            // Bag toggle + panel
            if (GUI.Button(new Rect(Screen.width - 100, 44, 88, 28), _bagOpen ? "Bag ▲" : "Bag ▼"))
                _bagOpen = !_bagOpen;
            if (_bagOpen)
            {
                GUILayout.BeginArea(new Rect(Screen.width - 240, 76, 228, 220), GUI.skin.box);
                GUILayout.Label("Inventory");
                if (_session.Inventory.Items.Count == 0)
                    GUILayout.Label("(empty)");
                foreach (var it in _session.Inventory.Items)
                    GUILayout.Label($"{it.ItemId}  x{it.Count}");
                GUILayout.EndArea();
            }

            // Chat
            GUILayout.BeginArea(new Rect(12, Screen.height - 200, 420, 188), GUI.skin.box);
            GUILayout.BeginHorizontal();
            _chatChannel = GUILayout.Toolbar(_chatChannel, new[] { "World", "Nearby" });
            GUILayout.EndHorizontal();
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(110));
            foreach (var line in _session.ChatLog.Lines)
                GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            _chatInput = GUILayout.TextField(_chatInput);
            if (GUILayout.Button("Send", GUILayout.Width(60)) || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == ""))
            {
                if (!string.IsNullOrWhiteSpace(_chatInput))
                {
                    var ch = _chatChannel == 0 ? ChatChannel.ChatWorld : ChatChannel.ChatNearby;
                    _ = _session.SendChatAsync(ch, _chatInput);
                    _chatInput = "";
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawVirtualStickGizmo()
        {
            if (_input == null || !_input.StickActive) return;
            var c = _input.StickCenterScreen;
            // IMGUI y is top-down
            float guiY = Screen.height - c.y;
            var center = new Vector2(c.x, guiY);
            DrawCircle(center, 70f, new Color(1f, 1f, 1f, 0.15f));
            var knob = center + new Vector2(_input.StickAxisX, -_input.StickAxisY) * 50f;
            DrawCircle(knob, 24f, new Color(1f, 1f, 1f, 0.45f));
        }

        private static void DrawCircle(Vector2 center, float radius, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
#endif
