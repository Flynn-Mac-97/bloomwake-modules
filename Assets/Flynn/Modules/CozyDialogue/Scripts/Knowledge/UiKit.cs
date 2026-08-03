using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Flynn.Feel
{
    /// <summary>
    /// Tiny runtime-UI helpers shared by the knowledge panels (module-local, not a component).
    /// Everything token-tinted here so panels can't drift off-spec.
    /// </summary>
    public static class UiKit
    {
        public static Canvas NewOverlayCanvas(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920f, 1080f);
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image NewPanel(string name, Transform parent, Sprite sprite, Color color)
        {
            var img = NewRect(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI NewText(string name, Transform parent, float size,
            Color color, TextAlignmentOptions align)
        {
            var tmp = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static void Stretch(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        /// <summary>UGUI clicks need an EventSystem — Codely forgets; code guarantees.</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        /// <summary>Corrupted-text glyph noise, stable per input length.</summary>
        public static string Scramble(string text)
        {
            const string glyphs = "▓▒░#%&≈≠∆";
            var chars = text.ToCharArray();
            var rng = new System.Random(text.Length * 7919);
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsWhiteSpace(chars[i]))
                    chars[i] = glyphs[rng.Next(glyphs.Length)];
            return new string(chars);
        }
    }
}
