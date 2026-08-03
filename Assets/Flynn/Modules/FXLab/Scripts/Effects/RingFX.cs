using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Soft expanding pulse ring — one sprite scaling out while fading. Gentle
    /// "the world felt that" acknowledgment; the Repair moment uses it as its aura.
    /// Spawns a standalone one-shot per play so overlapping pulses don't fight.
    /// </summary>
    public class RingFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        public void Play()
        {
            if (tuning != null) PlayAt(transform.position, tuning.ring);
        }

        public void PlayAt(Vector3 pos, RingSettings s)
        {
            if (s == null) return;
            FXAudio.Play(s.sfx, pos);

            var go = new GameObject("PulseRing");
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = FXSprites.Ring;
            sr.sortingOrder = 35;

            Tween.Custom(0f, 1f, s.duration, onValueChange: t =>
            {
                if (go == null) return;
                float r = Mathf.Lerp(s.startRadius, s.endRadius, t);
                go.transform.localScale = Vector3.one * (r * 2f);   // ring sprite is ~1 unit across
                float a = Mathf.Clamp01(s.fade.Evaluate(t)) * s.color.a;
                sr.color = new Color(s.color.r, s.color.g, s.color.b, a);
            }, ease: Ease.Linear).OnComplete(() => { if (go != null) Destroy(go); });
        }
    }
}
