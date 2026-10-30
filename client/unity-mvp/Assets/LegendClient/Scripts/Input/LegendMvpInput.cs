#if UNITY_2019_1_OR_NEWER
using Legend.UnitySdk;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Legend.Client.Input
{
    /// <summary>
    /// PC: WASD + left-click ground move / select + RMB/Space/J attack.
    /// Mobile: left virtual stick. Standalone disables mouse-stick fallback so LMB is click-to-move.
    /// </summary>
    public sealed class LegendMvpInput
    {
        private readonly KeyboardWasdInput _kb = new KeyboardWasdInput();
        private readonly VirtualJoystickInput _stick = new VirtualJoystickInput();
        private Vector2 _stickCenter;
        private bool _stickActive;
        private int _stickFingerId = -1;
        private bool _externalStick;
        private float _extX;
        private float _extY;

        public LegendMvpInput()
        {
            _kb.W = () => UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow);
            _kb.A = () => UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow);
            _kb.S = () => UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow);
            _kb.D = () => UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow);
        }

        public static bool IsPcStandalone =>
#if UNITY_STANDALONE || UNITY_EDITOR
            !Application.isMobilePlatform;
#else
            false;
#endif

        public void SetExternalStick(float axisX, float axisY)
        {
            _externalStick = true;
            _extX = axisX;
            _extY = axisY;
        }

        public void ClearExternalStick() => _externalStick = false;

        public void Tick()
        {
            if (_externalStick) return;
            UpdateTouchStick();
            // PC: do NOT steal left mouse for stick — reserved for click-to-move / select
            if (!IsPcStandalone)
                UpdateMouseStickFallback();
        }

        public void GetMoveIntent(out int dx, out int dy)
        {
            _kb.GetMoveIntent(out dx, out dy);
            if (_externalStick)
            {
                const float dead = 0.35f;
                int sx = _extX > dead ? 1 : _extX < -dead ? -1 : 0;
                int sy = _extY > dead ? 1 : _extY < -dead ? -1 : 0;
                if (sx != 0 || sy != 0)
                {
                    dx = sx;
                    dy = sy;
                }
                return;
            }
            if (_stickActive)
                _stick.GetMoveIntent(out dx, out dy);
        }

        public bool AttackPressed =>
            UnityEngine.Input.GetKeyDown(KeyCode.Space) ||
            UnityEngine.Input.GetKeyDown(KeyCode.J) ||
            UnityEngine.Input.GetMouseButtonDown(1);

        public bool PickupPressed =>
            UnityEngine.Input.GetKeyDown(KeyCode.Z) ||
            UnityEngine.Input.GetKeyDown(KeyCode.G);

        public bool TabCyclePressed => UnityEngine.Input.GetKeyDown(KeyCode.Tab);

        public bool ToggleBagPressed => UnityEngine.Input.GetKeyDown(KeyCode.B);

        public bool EscapePressed => UnityEngine.Input.GetKeyDown(KeyCode.Escape);

        public bool ChatFocusPressed => UnityEngine.Input.GetKeyDown(KeyCode.Return) ||
                                        UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter);

        public bool WantWorldClick
        {
            get
            {
                if (!UnityEngine.Input.GetMouseButtonDown(0)) return false;
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return false;
                return true;
            }
        }

        /// <summary>Legacy alias.</summary>
        public bool WantSelectClick => WantWorldClick;

        public Vector2 StickCenterScreen => _stickCenter;
        public bool StickActive => _stickActive || _externalStick;
        public float StickAxisX => _externalStick ? _extX : _stick.AxisX;
        public float StickAxisY => _externalStick ? _extY : _stick.AxisY;

        private void UpdateTouchStick()
        {
            if (UnityEngine.Input.touchCount == 0)
            {
                if (_stickFingerId >= 0)
                    ClearStick();
                return;
            }

            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                var t = UnityEngine.Input.GetTouch(i);
                if (t.phase == TouchPhase.Began && t.position.x < Screen.width * 0.42f && !_stickActive)
                {
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId))
                        continue;
                    _stickActive = true;
                    _stickFingerId = t.fingerId;
                    _stickCenter = t.position;
                }
                if (_stickActive && t.fingerId == _stickFingerId)
                {
                    if (t.phase is TouchPhase.Moved or TouchPhase.Stationary)
                    {
                        Vector2 delta = t.position - _stickCenter;
                        _stick.AxisX = Mathf.Clamp(delta.x / 80f, -1f, 1f);
                        _stick.AxisY = Mathf.Clamp(delta.y / 80f, -1f, 1f);
                    }
                    if (t.phase is TouchPhase.Ended or TouchPhase.Canceled)
                        ClearStick();
                }
            }
        }

        private void UpdateMouseStickFallback()
        {
            if (UnityEngine.Input.touchCount > 0) return;
            if (UnityEngine.Input.GetMouseButtonDown(0) && UnityEngine.Input.mousePosition.x < Screen.width * 0.28f
                && UnityEngine.Input.mousePosition.y < Screen.height * 0.45f
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                _stickActive = true;
                _stickCenter = UnityEngine.Input.mousePosition;
            }
            if (_stickActive && UnityEngine.Input.GetMouseButton(0) && _stickFingerId < 0)
            {
                Vector2 delta = (Vector2)UnityEngine.Input.mousePosition - _stickCenter;
                _stick.AxisX = Mathf.Clamp(delta.x / 80f, -1f, 1f);
                _stick.AxisY = Mathf.Clamp(delta.y / 80f, -1f, 1f);
            }
            if (_stickActive && UnityEngine.Input.GetMouseButtonUp(0) && _stickFingerId < 0)
                ClearStick();
        }

        private void ClearStick()
        {
            _stickActive = false;
            _stickFingerId = -1;
            _stick.AxisX = 0;
            _stick.AxisY = 0;
        }
    }
}
#endif
