#if UNITY_2019_1_OR_NEWER
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
    /// Awake builds camera / ground / session / uGUI HUD — no prefab dependency.
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
        private float _nextMoveAt;
        private bool _networkWired;

        private void Awake()
        {
            _config = configAsset != null ? configAsset : ScriptableObject.CreateInstance<LegendClientConfig>();
            EnsureCamera();
            _input = new LegendMvpInput();
            _session = new LegendSessionService();
            _session.Configure(_config.Host, _config.Port, _config.Platform, _config.RealmId);

            _world = gameObject.AddComponent<LegendWorldPresenter>();
            _world.Bind(_session, _config);

            _combat = gameObject.AddComponent<LegendCombatController>();
            _combat.Bind(_session, _world, _config);

            _hud = gameObject.AddComponent<LegendHudController>();
            _hud.Bind(_session, _config, _combat, _world, _input);
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

            // Selection click (skip when pointer is over uGUI)
            if (_input.WantSelectClick)
                _combat.TrySelectFromScreen(UnityEngine.Input.mousePosition);

            if (_input.AttackPressed || _hud.ConsumeAttackUiPressed())
                _combat.TryAttackSelectedOrNearest();

            if (_hud.ConsumePickupUiPressed())
                TryPickupSelectedOrNearest();

            if (Time.time < _nextMoveAt) return;
            _input.GetMoveIntent(out int dx, out int dy);
            if (dx == 0 && dy == 0) return;
            var self = _session.Client?.SelfEntity;
            if (self == null) return;
            int toX = self.X + dx;
            int toY = self.Y + dy;
            int dir = dy > 0 ? 0 : dy < 0 ? 4 : dx > 0 ? 2 : 6;
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
                    _ = _session.PickupAsync(sel);
                    return;
                }
            }
            var nearest = _world.FindNearestLoot();
            if (!string.IsNullOrEmpty(nearest))
            {
                _world.Select(nearest);
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
                view?.LerpToGrid(sync.ToX, sync.ToY, _config.CellSize);
            }
        }

        private void OnSessionStateChanged()
        {
            if (_session.Fsm.Phase == SessionPhase.InWorld && _session.Client != null && !_networkWired)
            {
                _world.WireNetwork();
                _combat.WireNetwork();
                _networkWired = true;
            }
            if (_session.Fsm.Phase is SessionPhase.Disconnected or SessionPhase.Login or SessionPhase.Error or SessionPhase.Reconnecting)
            {
                _networkWired = false;
            }
            if (_session.Fsm.Phase == SessionPhase.CharSelect)
                _world.ClearAll();
        }

        private void OnDestroy()
        {
            _session.DisposeClient();
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null)
            {
                Camera.main.transform.position = new Vector3(20f, 14f, 8f);
                Camera.main.transform.LookAt(new Vector3(20f, 0f, 20f));
                return;
            }
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(20f, 14f, 8f);
            camGo.transform.LookAt(new Vector3(20f, 0f, 20f));
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }
}
#endif
