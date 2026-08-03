using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Rising twinkle motes — completion kindness (Success/Milestone tier), not spectacle.
    /// Fires at any position with any settings block; used as an ingredient by the Repair
    /// and Pickup moments as well as standalone.
    /// </summary>
    public class SparkleFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        public void Play()
        {
            if (tuning != null) PlayAt(transform.position, tuning.sparkle);
        }

        public void PlayAt(Vector3 pos, SparkleSettings s)
        {
            if (s == null) return;
            FXAudio.Play(s.sfx, pos);
            for (int i = 0; i < s.count; i++)
                SpawnMote(pos, s, i);
        }

        void SpawnMote(Vector3 pos, SparkleSettings s, int index)
        {
            var go = new GameObject("Sparkle");
            go.transform.position = pos
                + (Vector3)(Random.insideUnitCircle * 0.15f) + Vector3.up * 0.05f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = FXSprites.SoftCircle;
            sr.color = s.color;
            sr.sortingOrder = 45;

            Vector3 start = go.transform.position;
            float wanderPhase = Random.value * Mathf.PI * 2f;
            float phase = index * 0.7f + Random.value;
            float life = s.life * Random.Range(0.8f, 1.2f);
            float size = s.size * Random.Range(0.7f, 1.3f);

            Tween.Custom(0f, 1f, life, onValueChange: t =>
            {
                if (go == null) return;
                float sec = t * life;
                go.transform.position = start
                    + Vector3.up * (s.riseSpeed * sec)
                    + Vector3.right * (Mathf.Sin(sec * 3f + wanderPhase) * 0.03f);
                float twinkle = 0.55f + 0.45f * Mathf.Sin((sec * s.twinkleHz + phase) * Mathf.PI * 2f);
                float fade = 1f - t * t;
                sr.color = new Color(s.color.r, s.color.g, s.color.b, s.color.a * twinkle * fade);
                go.transform.localScale = Vector3.one * (size * (0.8f + 0.2f * twinkle));
            }, ease: Ease.Linear).OnComplete(() => { if (go != null) Destroy(go); });
        }
    }
}
