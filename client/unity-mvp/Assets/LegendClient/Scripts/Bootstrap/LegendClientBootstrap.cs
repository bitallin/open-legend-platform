#if UNITY_2019_1_OR_NEWER
using Legend.Client.Audio;
using Legend.Client.Combat;
using Legend.Client.Config;
using Legend.Client.Input;
using Legend.Client.Session;
using Legend.Client.UI;
using Legend.Client.World;
using Legend.ClientCore;
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.Client.Bootstrap
{
    /// <summary>
    /// Drop on any GameObject in an empty scene (or use Editor menu to create Boot scene).
    /// Awake builds camera / tiled ground / session / uGUI HUD / audio — no prefab dependency.
    /// Accepts launcher args: --host --port --realm-id [--user --pass].
    /// </summary>
    public sealed class LegendClientBootstrap : MonoBehaviour
    {
        [SerializeField] private LegendClientConfig? configAsset;

        private LegendClientConfig _config = null!;
        private LegendSessionService _session = null!;
        private LegendWorldPresenter _world = null!;
        private LegendCombatController _combat = null!;
        private LegendHudController _hud = null!;
        private LegendMvpInput _input = null!;
        private LegendAudioService _audio = null!;
        private float _nextMoveAt;
        private bool _networkWired;
        private bool _hasMoveTarget;
        private int _moveTargetX;
        private int _moveTargetY;

        private void Awake()
        {
            _config = configAsset != null ? configAsset : ScriptableObject.CreateInstance<LegendClientConfig>();
            ApplyCommandLineArgs(_config);
            EnsureCamera();
            _input = new LegendMvpInput();
            _session = new LegendSessionService();
            _session.Configure(_config.Host, _config.Port, _config.Platform, _config.RealmId);

            _audio = gameObject.AddComponent<LegendAudioService>();
            _audio.Ensure();
            _audio.PlayLoginBgm();

            _world = gameObject.AddComponent<LegendWorldPresenter>();
            _world.Bind(_session, _config);

            _combat = gameObject.AddComponent<LegendCombatController>();
            _combat.Bind(_session, _world, _config);
            _combat.BindAudio(_audio);

            _hud = gameObject.AddComponent<LegendHudController>();
            _hud.Bind(_session, _config, _combat, _world, _input, _audio);
            _combat.BindHud(_hud);

            _session.StateChanged += OnSessionStateChanged;
        }

        private void Update()
        {
            _session.Pump();
            _input.Tick();

            if (_session.Fsm.Phase != SessionPhase.InWorld)
                return;

            if (!_networkWired && _session.Client != null)
            {
                _world.WireNetwork();
                _combat.WireNetwork();
                _networkWired = true;
            }

            _world.TickCamera();

            // Hotkeys that HUD also handles (bag/chat/esc) — combat/move here
            if (_input.TabCyclePressed)
            {
                var next = _world.CycleAttackable(1);
                if (!string.IsNullOrEmpty(next))
                    _world.Select(next);
            }

            if (_input.WantWorldClick)
                HandleWorldClick(UnityEngine.Input.mousePosition);

            if (_input.AttackPressed || _hud.ConsumeAttackUiPressed())
            {
                _audio.PlayAttack();
                _combat.TryAttackSelectedOrNearest();
            }

            if (_input.PickupPressed || _hud.ConsumePickupUiPressed())
                TryPickupSelectedOrNearest();

            if (Time.time < _nextMoveAt) return;

            // WASD / stick overrides click-to-move
            _input.GetMoveIntent(out int dx, out int dy);
            if (dx != 0 || dy != 0)
            {
                _hasMoveTarget = false;
                _world.HideClickMarker();
                TryStep(dx, dy);
                return;
            }

            if (_hasMoveTarget)
                StepTowardClickTarget();
        }

        private void HandleWorldClick(Vector3 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out var hit, 200f))
            {
                var view = hit.collider.GetComponentInParent<LegendEntityView>();
                if (view != null && !view.IsSelf)
                {
                    _hasMoveTarget = false;
                    _world.HideClickMarker();
                    _combat.TrySelectFromScreen(screenPos);
                    return;
                }
            }

            if (_world.ScreenToGrid(screenPos, out int gx, out int gy))
            {
                _moveTargetX = gx;
                _moveTargetY = gy;
                _hasMoveTarget = true;
                _world.ShowClickMarker(gx, gy);
            }
        }

        private void StepTowardClickTarget()
        {
            var self = _session.Client?.SelfEntity;
            if (self == null)
            {
                _hasMoveTarget = false;
                return;
            }
            int dx = _moveTargetX - self.X;
            int dy = _moveTargetY - self.Y;
            if (dx == 0 && dy == 0)
            {
                _hasMoveTarget = false;
                _world.HideClickMarker();
                return;
            }
            int stepX = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
            int stepY = dy == 0 ? 0 : (dy > 0 ? 1 : -1);
            // Prefer axis with larger remaining distance (simple Mir-like pathing)
            if (Mathf.Abs(dx) >= Mathf.Abs(dy) && stepX != 0 && stepY != 0)
            {
                // Try diagonal first when both nonzero
            }
            else if (Mathf.Abs(dx) > Mathf.Abs(dy))
                stepY = 0;
            else if (Mathf.Abs(dy) > Mathf.Abs(dx))
                stepX = 0;

            TryStep(stepX, stepY);
        }

        private void TryStep(int dx, int dy)
        {
            var self = _session.Client?.SelfEntity;
            if (self == null) return;
            if (dx == 0 && dy == 0) return;
            int toX = self.X + dx;
            int toY = self.Y + dy;
            int dir = LegendEntityView.DirFromDelta(dx, dy);
            _nextMoveAt = Time.time + _config.MoveCooldownSeconds;
            _ = MoveAsync(toX, toY, dir);
        }

        private void TryPickupSelectedOrNearest()
        {
            var sel = _world.SelectedEntityId;
            if (!string.IsNullOrEmpty(sel))
            {
                var v = _world.Get(sel);
                if (v != null && v.Kind == EntityKind.EntityLoot)
                {
                    _audio.PlayPickup();
                    _ = _session.PickupAsync(sel);
                    return;
                }
            }
            var nearest = _world.FindNearestLoot();
            if (!string.IsNullOrEmpty(nearest))
            {
                _world.Select(nearest);
                _audio.PlayPickup();
                _ = _session.PickupAsync(nearest!);
                return;
            }
            _hud.ShowToast("附近没有可拾取物（点选金色掉落）", new Color(0.35f, 0.3f, 0.1f, 0.9f));
        }

        private async System.Threading.Tasks.Task MoveAsync(int x, int y, int dir)
        {
            var sync = await _session.TryMoveAsync(x, y, dir);
            if (sync != null && sync.Accepted)
            {
                var view = _world.SelfView;
                view?.SetDirection(dir);
                view?.LerpToGrid(sync.ToX, sync.ToY, _config.CellSize);
            }
            else if (_hasMoveTarget)
            {
                // Blocked — stop click path
                _hasMoveTarget = false;
                _world.HideClickMarker();
            }
        }

        private void OnSessionStateChanged()
        {
            if (_session.Fsm.Phase == SessionPhase.InWorld && _session.Client != null && !_networkWired)
            {
                _world.WireNetwork();
                _combat.WireNetwork();
                _networkWired = true;
                _audio.PlayWorldBgm();
            }
            if (_session.Fsm.Phase is SessionPhase.Login or SessionPhase.CharSelect)
                _audio.PlayLoginBgm();
            if (_session.Fsm.Phase is SessionPhase.Disconnected or SessionPhase.Login or SessionPhase.Error or SessionPhase.Reconnecting)
            {
                _networkWired = false;
                _hasMoveTarget = false;
            }
            if (_session.Fsm.Phase == SessionPhase.CharSelect)
                _world.ClearAll();
        }

        private void OnDestroy()
        {
            _session.DisposeClient();
        }

        /// <summary>Parse --host / --port / --realm-id / --user / --pass from process args (launcher).</summary>
        public static void ApplyCommandLineArgs(LegendClientConfig config)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string? next = i + 1 < args.Length ? args[i + 1] : null;
                if ((a == "--host" || a == "-h") && next != null) { config.Host = next; i++; }
                else if ((a == "--port" || a == "-p") && next != null && int.TryParse(next, out int port)) { config.Port = port; i++; }
                else if ((a == "--realm-id" || a == "--realm") && next != null) { config.RealmId = next; i++; }
                else if ((a == "--user" || a == "--username") && next != null) { config.DefaultUsername = next; i++; }
                else if ((a == "--pass" || a == "--password") && next != null) { config.DefaultPassword = next; i++; }
            }
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null)
            {
                Camera.main.transform.position = new Vector3(20f, 14f, 8f);
                Camera.main.transform.LookAt(new Vector3(20f, 0f, 20f));
                Camera.main.backgroundColor = new Color(0.05f, 0.07f, 0.09f);
                return;
            }
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.09f);
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(20f, 14f, 8f);
            camGo.transform.LookAt(new Vector3(20f, 0f, 20f));
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.intensity = 1.05f;
        }
    }
}
#endif
