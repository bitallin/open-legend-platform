#if UNITY_2019_1_OR_NEWER
using Legend.UnitySdk;
using UnityEngine;

namespace Legend.Client.Input
{
    /// <summary>PC WASD + left-side virtual stick (touch / mouse drag).</summary>
    public sealed class LegendMvpInput
    {
        private readonly KeyboardWasdInput _kb = new KeyboardWasdInput();
        private readonly VirtualJoystickInput _stick = new VirtualJoystickInput();
        private Vector2 _stickCenter;
        private bool _stickActive;
        private int _stickFingerId = -1;

        public LegendMvpInput()
        {
            _kb.W = () => UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow);
            _kb.A = () => UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow);
            _kb.S = () => UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow);
            _kb.D = () => UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow);
        }

        public void Tick()
        {
            UpdateTouchStick();
            UpdateMouseStickFallback();
        }

        public void GetMoveIntent(out int dx, out int dy)
        {
            _kb.GetMoveIntent(out dx, out dy);
            if (_stickActive)
                _stick.GetMoveIntent(out dx, out dy);
        }

        public bool AttackPressed =>
            UnityEngine.Input.GetKeyDown(KeyCode.Space) ||
            UnityEngine.Input.GetKeyDown(KeyCode.J) ||
            UnityEngine.Input.GetMouseButtonDown(1);

        public bool WantSelectClick => UnityEngine.Input.GetMouseButtonDown(0);

        public Vector2 StickCenterScreen => _stickCenter;
        public bool StickActive => _stickActive;
        public float StickAxisX => _stick.AxisX;
        public float StickAxisY => _stick.AxisY;

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
            // Hold left mouse in left half to emulate stick (handy for Game view without touch).
            if (UnityEngine.Input.GetMouseButtonDown(0) && UnityEngine.Input.mousePosition.x < Screen.width * 0.28f
                && UnityEngine.Input.mousePosition.y < Screen.height * 0.45f)
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
