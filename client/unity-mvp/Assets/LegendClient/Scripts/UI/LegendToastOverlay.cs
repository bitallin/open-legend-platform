#if UNITY_2019_1_OR_NEWER
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Legend.Client.UI
{
    /// <summary>Center-screen toast for death / safe-zone / notices.</summary>
    public sealed class LegendToastOverlay : MonoBehaviour
    {
        private Text _label = null!;
        private Image _bg = null!;
        private Coroutine? _hideCo;

        public void Build(Transform canvasRoot)
        {
            var panel = LegendUiBuilder.Panel(canvasRoot, "Toast",
                new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.06f, 0.08f, 0.88f));
            LegendUiBuilder.SetAnchored(panel, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f),
                Vector2.zero, new Vector2(520, 64));
            _bg = panel.GetComponent<Image>();
            _label = LegendUiBuilder.Text(panel, "Msg", "", 22, TextAnchor.MiddleCenter, Color.white, bold: true);
            panel.gameObject.SetActive(false);
        }

        public void Show(string message, Color? tint = null, float seconds = 2.2f)
        {
            if (_label == null) return;
            _label.text = message;
            if (_bg != null)
                _bg.color = tint ?? new Color(0.05f, 0.06f, 0.08f, 0.88f);
            _label.transform.parent.gameObject.SetActive(true);
            if (_hideCo != null) StopCoroutine(_hideCo);
            _hideCo = StartCoroutine(HideAfter(seconds));
        }

        private IEnumerator HideAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (_label != null)
                _label.transform.parent.gameObject.SetActive(false);
            _hideCo = null;
        }
    }
}
#endif
