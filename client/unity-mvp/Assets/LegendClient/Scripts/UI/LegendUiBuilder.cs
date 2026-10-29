#if UNITY_2019_1_OR_NEWER
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Legend.Client.UI
{
    /// <summary>Runtime uGUI builders — no prefabs / Editor assets required.</summary>
    public static class LegendUiBuilder
    {
        private static Sprite? _whiteSprite;

        public static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite != null) return _whiteSprite;
                var tex = Texture2D.whiteTexture;
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                return _whiteSprite;
            }
        }

        public static Canvas CreateOverlayCanvas(string name, int sortingOrder = 100)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        public static RectTransform Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, Color? bg = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            if (bg.HasValue)
            {
                var img = go.AddComponent<Image>();
                img.sprite = WhiteSprite;
                img.color = bg.Value;
                img.raycastTarget = true;
            }
            return rt;
        }

        public static Text Text(Transform parent, string name, string content, int fontSize = 18,
            TextAnchor align = TextAnchor.MiddleLeft, Color? color = null, bool bold = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            Stretch(rt);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (t.font == null)
                t.font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            t.text = content;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color ?? Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (bold) t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            return t;
        }

        public static InputField Input(Transform parent, string name, string placeholder, bool password = false)
        {
            var root = Panel(parent, name, new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                new Color(0.12f, 0.13f, 0.16f, 0.95f));
            var img = root.GetComponent<Image>();
            var field = root.gameObject.AddComponent<InputField>();
            field.targetGraphic = img;

            var text = Text(root, "Text", "", 18, TextAnchor.MiddleLeft, Color.white);
            var textRt = text.GetComponent<RectTransform>();
            textRt.offsetMin = new Vector2(10, 4);
            textRt.offsetMax = new Vector2(-10, -4);
            text.supportRichText = false;

            var ph = Text(root, "Placeholder", placeholder, 18, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.35f));
            var phRt = ph.GetComponent<RectTransform>();
            phRt.offsetMin = new Vector2(10, 4);
            phRt.offsetMax = new Vector2(-10, -4);

            field.textComponent = text;
            field.placeholder = ph;
            if (password) field.contentType = InputField.ContentType.Password;
            return field;
        }

        public static Button Button(Transform parent, string name, string label, Color? bg = null)
        {
            var root = Panel(parent, name, new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                bg ?? new Color(0.18f, 0.45f, 0.85f, 0.95f));
            var btn = root.gameObject.AddComponent<Button>();
            btn.targetGraphic = root.GetComponent<Image>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.6f, 1f, 1f);
            colors.pressedColor = new Color(0.12f, 0.3f, 0.6f, 1f);
            btn.colors = colors;
            Text(root, "Label", label, 20, TextAnchor.MiddleCenter, Color.white, bold: true);
            return btn;
        }

        public static Image Image(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            var img = go.AddComponent<Image>();
            img.sprite = WhiteSprite;
            img.color = color;
            return img;
        }

        public static ScrollRect Scroll(Transform parent, string name, out Text contentText)
        {
            var root = Panel(parent, name, new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                new Color(0.08f, 0.09f, 0.11f, 0.9f));
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = Panel(root, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Panel(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(6, -2000), new Vector2(-6, -6));
            content.pivot = new Vector2(0.5f, 1f);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            contentText = Text(content, "Lines", "", 14, TextAnchor.UpperLeft, new Color(0.9f, 0.92f, 0.95f));
            var trt = contentText.GetComponent<RectTransform>();
            Stretch(trt);
            contentText.alignment = TextAnchor.UpperLeft;
            var le = contentText.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 40;

            scroll.viewport = viewport;
            scroll.content = content;
            return scroll;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void SetAnchored(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }
    }
}
#endif
