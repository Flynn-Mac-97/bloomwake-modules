using PrimeTween;
using TMPro;
using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// The spawned bubble: pops in (OutBack), holds, shrinks out (InQuad) — VibeSpec §12/§14.
    /// A new Show() replaces the current message (replace, never stack — Contract §16).
    /// Colors and sorting are set in code from VibeTokens so scene wiring can't get them wrong.
    /// </summary>
    public class EmoteBubble : MonoBehaviour
    {
        [Tooltip("Rounded bubble background sprite (tinted ui-surface in code).")]
        public SpriteRenderer background;
        [Tooltip("The short text / glyph (3D TextMeshPro).")]
        public TextMeshPro label;

        const int SortOrder = 6000;   // above the Y-sort band (5000 ± world)

        Tween _pop, _lifetime, _exit;

        void Awake()
        {
            if (background != null)
            {
                background.color = VibeTokens.UiSurface;
                background.sortingOrder = SortOrder;
            }
            if (label != null)
            {
                var r = label.GetComponent<Renderer>();
                if (r != null) r.sortingOrder = SortOrder + 1;
            }
            gameObject.SetActive(false);
        }

        public void Show(string text, Color textColor, float popIn, float hold, float fadeOut)
        {
            StopAll();
            if (label != null) { label.text = text; label.color = textColor; }

            gameObject.SetActive(true);
            transform.localScale = Vector3.zero;
            _pop = Tween.Scale(transform, Vector3.one, popIn, Ease.OutBack);
            _lifetime = Tween.Delay(popIn + hold, () => Dismiss(fadeOut));
        }

        /// <summary>Shrink out. Never resembles the pop-in (Contract §13).</summary>
        public void Dismiss(float duration)
        {
            _lifetime.Stop();
            _exit = Tween.Scale(transform, Vector3.zero, duration, Ease.InQuad)
                         .OnComplete(() => gameObject.SetActive(false));
        }

        void StopAll()
        {
            _pop.Stop(); _lifetime.Stop(); _exit.Stop();
        }

        void OnDestroy() => StopAll();
    }
}
