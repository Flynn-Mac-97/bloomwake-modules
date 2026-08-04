using PrimeTween;
using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>
    /// Ghost-icon flight: arced tween between two canvas-local points (pickup fly-in,
    /// drop fly-out, slot-to-slot moves). Destroys the ghost when done.
    /// </summary>
    public static class HUDFly
    {
        public static void Arc(RectTransform ghost, Vector2 fromLocal, Vector2 toLocal,
            float arcHeight, float duration, bool shrinkOut, System.Action onArrive)
        {
            if (ghost == null) return;
            Vector2 mid = (fromLocal + toLocal) * 0.5f
                          + Vector2.up * (arcHeight * VibeTokens.MotionScale);
            ghost.anchoredPosition = fromLocal;

            Tween.Custom(0f, 1f, duration, ease: Ease.OutQuad, onValueChange: t =>
            {
                if (ghost == null) return;
                Vector2 a = Vector2.LerpUnclamped(fromLocal, mid, t);
                Vector2 b = Vector2.LerpUnclamped(mid, toLocal, t);
                ghost.anchoredPosition = Vector2.LerpUnclamped(a, b, t);
            }).OnComplete(() =>
            {
                if (ghost == null) { onArrive?.Invoke(); return; }
                if (shrinkOut)
                {
                    Tween.Custom(ghost.localScale.x, 0f, 0.18f, ease: Ease.InQuad,
                        onValueChange: s => { if (ghost != null) ghost.localScale = Vector3.one * s; })
                    .OnComplete(() =>
                    {
                        if (ghost != null) Object.Destroy(ghost.gameObject);
                        onArrive?.Invoke();
                    });
                }
                else
                {
                    Object.Destroy(ghost.gameObject);
                    onArrive?.Invoke();
                }
            });
        }
    }
}
