#if UNITY_2019_1_OR_NEWER
using System.Collections.Generic;
using System.Threading.Tasks;
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.UnitySdk
{
    /// <summary>
    /// Drop onto an empty GameObject. IMGUI login/select-char flow + WASD / virtual stick move.
    /// Attach LegendBlockedCellsGizmo helper for debug blocking visualization.
    /// </summary>
    public sealed class LegendWorldDemoBehaviour : MonoBehaviour
    {
        public string Host = "127.0.0.1";
        public int Port = 7100;
        public string Username = "unity_player";
        public string Password = "pass123";
        public string NewCharName = "旅人";
        public string NewCharClass = "warrior";
        public string Platform = "PC";
        public float CellSize = 1f;
        public float MoveCooldownSeconds = 0.12f;

        private LegendUnityNetworkClient? _client;
        private readonly Dictionary<string, GameObject> _entities = new();
        private enum UiPhase { Login, CharSelect, InWorld, Error }
        private UiPhase _phase = UiPhase.Login;
        private string _status = "Idle";
        private string _error = "";
        private List<CharacterSummary> _chars = new();
        private float _nextMoveAt;
        private ILegendMoveInput? _input;
        private VirtualJoystickInput _joystick = new();
        private Vector2 _stickCenter;
        private bool _stickActive;
        public List<Vector2Int> DebugBlockedCells = new();
        public List<RectInt> DebugSafeZones = new();

        private void Awake()
        {
            var kb = new KeyboardWasdInput
            {
                W = () => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow),
                A = () => Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow),
                S = () => Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow),
                D = () => Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)
            };
            _input = kb;
        }

        private void Update()
        {
            _client?.PumpMainThreadEvents();
            if (_phase != UiPhase.InWorld || _client?.SelfEntity == null || _input == null)
                return;

            UpdateTouchStick();
            if (Time.time < _nextMoveAt)
                return;

            _input.GetMoveIntent(out int dx, out int dy);
            if (_stickActive)
                _joystick.GetMoveIntent(out dx, out dy);

            if (dx == 0 && dy == 0)
                return;

            int toX = _client.SelfEntity.X + dx;
            int toY = _client.SelfEntity.Y + dy;
            int dir = dy > 0 ? 0 : dy < 0 ? 4 : dx > 0 ? 2 : 6;
            _nextMoveAt = Time.time + MoveCooldownSeconds;
            _ = RequestMove(toX, toY, dir);
        }

        private async Task RequestMove(int x, int y, int dir)
        {
            try
            {
                var sync = await _client!.RequestMoveAsync(x, y, dir);
                if (sync.Accepted && _entities.TryGetValue(sync.EntityId, out var go))
                    go.transform.position = new Vector3(sync.ToX * CellSize, 0, sync.ToY * CellSize);
            }
            catch (System.Exception ex)
            {
                _status = "Move error: " + ex.Message;
            }
        }

        private void UpdateTouchStick()
        {
            if (Input.touchCount == 0)
            {
                _stickActive = false;
                _joystick.AxisX = 0;
                _joystick.AxisY = 0;
                return;
            }
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began && t.position.x < Screen.width * 0.45f)
            {
                _stickActive = true;
                _stickCenter = t.position;
            }
            if (_stickActive && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
            {
                Vector2 delta = t.position - _stickCenter;
                _joystick.AxisX = Mathf.Clamp(delta.x / 80f, -1f, 1f);
                _joystick.AxisY = Mathf.Clamp(delta.y / 80f, -1f, 1f);
            }
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                _stickActive = false;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 360, 520), GUI.skin.box);
            GUILayout.Label("Legend M1 Demo — " + _phase);
            GUILayout.Label(_status);
            if (!string.IsNullOrEmpty(_error))
                GUILayout.Label(_error);

            switch (_phase)
            {
                case UiPhase.Login:
                    Host = GUILayout.TextField(Host);
                    Username = GUILayout.TextField(Username);
                    Password = GUILayout.PasswordField(Password, '*');
                    if (GUILayout.Button("Connect / Login"))
                        _ = DoLogin();
                    break;
                case UiPhase.CharSelect:
                    GUILayout.Label("Characters:");
                    foreach (var c in _chars)
                    {
                        if (GUILayout.Button($"{c.Name} Lv{c.Level} {c.ClassId} @ {c.MapId} ({c.X},{c.Y})"))
                            _ = DoSelect(c.CharacterId);
                    }
                    GUILayout.Space(8);
                    NewCharName = GUILayout.TextField(NewCharName);
                    NewCharClass = GUILayout.TextField(NewCharClass);
                    if (GUILayout.Button("Create Char"))
                        _ = DoCreate();
                    break;
                case UiPhase.InWorld:
                    var self = _client?.SelfEntity;
                    if (self != null)
                        GUILayout.Label($"Pos ({self.X},{self.Y}) map={self.MapId} safe={self.InSafeZone}");
                    GUILayout.Label("WASD / arrows / left-screen virtual stick");
                    break;
            }
            GUILayout.EndArea();
        }

        private async Task DoLogin()
        {
            try
            {
                _error = "";
                _status = "Connecting...";
                _client?.Dispose();
                _client = new LegendUnityNetworkClient();
                WireEvents(_client);
                await _client.ConnectAsync(Host, Port);
                await _client.HandshakeAsync();
                await _client.RegisterAsync(Username, Password, Platform);
                await _client.LoginAsync(Username, Password, Platform);
                _chars = new List<CharacterSummary>(await _client.CharListAsync());
                _phase = UiPhase.CharSelect;
                _status = "Select or create a character";
            }
            catch (System.Exception ex)
            {
                _phase = UiPhase.Error;
                _error = ex.Message;
            }
        }

        private async Task DoCreate()
        {
            try
            {
                var c = await _client!.CreateCharAsync(NewCharName, NewCharClass);
                _chars = new List<CharacterSummary>(await _client.CharListAsync());
                _status = "Created " + c.Name;
            }
            catch (System.Exception ex) { _error = ex.Message; }
        }

        private async Task DoSelect(string characterId)
        {
            try
            {
                await _client!.SelectCharAsync(characterId);
                await _client.EnterWorldAsync("realm-classic-01", characterId);
                _phase = UiPhase.InWorld;
                _status = "In world";
            }
            catch (System.Exception ex) { _error = ex.Message; }
        }

        private void WireEvents(LegendUnityNetworkClient client)
        {
            client.OnSelfEnteredWorld += e =>
            {
                EnsureEntity(e);
                Camera.main.transform.position = new Vector3(e.X * CellSize, 12f, e.Y * CellSize - 10f);
            };
            client.OnEntityEnterAoi += EnsureEntity;
            client.OnEntityLeaveAoi += (id, _) =>
            {
                if (_entities.TryGetValue(id, out var go))
                {
                    Destroy(go);
                    _entities.Remove(id);
                }
            };
            client.OnMoveSync += sync =>
            {
                if (_entities.TryGetValue(sync.EntityId, out var go) && sync.Accepted)
                    go.transform.position = new Vector3(sync.ToX * CellSize, 0, sync.ToY * CellSize);
            };
            client.OnMapChange += mc =>
            {
                _status = $"Portal {mc.PortalId}: {mc.FromMapId} -> {mc.ToMapId}";
                if (_client?.SelfEntity != null && _entities.TryGetValue(mc.EntityId, out var go))
                    go.transform.position = new Vector3(mc.ToX * CellSize, 0, mc.ToY * CellSize);
            };
            client.OnServerNotice += n => _status = $"[{n.NoticeType}] {n.Title}: {n.Content}";
        }

        private void EnsureEntity(EntitySnapshot e)
        {
            if (_entities.ContainsKey(e.EntityId))
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = e.Name;
            go.transform.position = new Vector3(e.X * CellSize, 0, e.Y * CellSize);
            _entities[e.EntityId] = go;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            foreach (var c in DebugBlockedCells)
                Gizmos.DrawWireCube(new Vector3(c.x * CellSize, 0.5f, c.y * CellSize), Vector3.one * CellSize * 0.9f);
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.35f);
            foreach (var z in DebugSafeZones)
            {
                var center = new Vector3((z.xMin + z.xMax) * 0.5f * CellSize, 0.1f, (z.yMin + z.yMax) * 0.5f * CellSize);
                var size = new Vector3((z.width + 1) * CellSize, 0.05f, (z.height + 1) * CellSize);
                Gizmos.DrawCube(center, size);
            }
        }

        private void OnDestroy() => _client?.Dispose();
    }
}
#endif
