#if UNITY_2019_1_OR_NEWER
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Legend.Client.UI
{
    /// <summary>Fixed bottom-left uGUI stick; hidden on PC Standalone (Phase K).</summary>
    public sealed class LegendVirtualJoystickUi : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private RectTransform _root = null!;
        private RectTransform _knob = null!;
        private float _radius = 70f;
        private int _pointerId = int.MinValue;
        private bool _visible = true;

        public float AxisX { get; private set; }
        public float AxisY { get; private set; }
        public bool Active => _visible && _pointerId != int.MinValue;

        public void Build(RectTransform parent)
        {
            _root = LegendUiBuilder.Panel(parent, "VirtualJoystick",
                new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, Vector2.zero,
                new Color(1f, 1f, 1f, 0.18f));
            LegendUiBuilder.SetAnchored(_root, new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(110, 110), new Vector2(160, 160));
            var bg = _root.GetComponent<Image>();
            bg.raycastTarget = true;

            _knob = LegendUiBuilder.Panel(_root, "Knob",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                new Color(1f, 1f, 1f, 0.55f));
            LegendUiBuilder.SetAnchored(_knob, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(64, 64));
            _knob.GetComponent<Image>().raycastTarget = false;
            _radius = 56f;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_root != null)
                _root.gameObject.SetActive(visible);
            if (!visible)
            {
                _pointerId = int.MinValue;
                AxisX = 0f;
                AxisY = 0f;
                if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_visible) return;
            if (_pointerId != int.MinValue) return;
            _pointerId = eventData.pointerId;
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_visible) return;
            if (eventData.pointerId != _pointerId) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _root, eventData.position, eventData.pressEventCamera, out var local);
            var clamped = Vector2.ClampMagnitude(local, _radius);
            _knob.anchoredPosition = clamped;
            AxisX = Mathf.Clamp(clamped.x / _radius, -1f, 1f);
            AxisY = Mathf.Clamp(clamped.y / _radius, -1f, 1f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            _pointerId = int.MinValue;
            _knob.anchoredPosition = Vector2.zero;
            AxisX = 0f;
            AxisY = 0f;
        }

        public void GetMoveIntent(out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            const float dead = 0.35f;
            if (AxisX > dead) dx = 1;
            else if (AxisX < -dead) dx = -1;
            if (AxisY > dead) dy = 1;
            else if (AxisY < -dead) dy = -1;
        }
    }
}
#endif
