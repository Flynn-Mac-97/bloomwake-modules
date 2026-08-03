using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// The inverse of a puff: pieces start out on a ring and are drawn INWARD, spiralling and
    /// shrinking as they arrive. Absorption language - a thing taking something in rather than
    /// throwing it off. Cogs and nuts pulling into a machine being repaired, motes soaking into
    /// watered soil.
    ///
    /// Three things make it satisfying rather than merely inward. The radius closes on an
    /// EASE-IN curve, so pieces hang out there and then snap home - being pulled, not falling.
    /// They sweep a few degrees around the centre on the way (the spiral), which reads as
    /// orbit-and-capture. And they arrive STAGGERED, so you hear/see a run of arrivals instead
    /// of one thud. Spawner: one instance serves the scene.
    /// </summary>
    public class ConvergeFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        public void Play()
        {
            if (tuning != null) PlayAt(transform.position, tuning.converge);
        }

        public void PlayAt(Vector3 center, ConvergeSettings s)
        {
            if (s == null) return;
            FXAudio.Play(s.sfx, center);

            for (int i = 0; i < s.count; i++)
            {
                // even spacing around the ring, then jittered so it never looks stamped
                float baseAngle = (360f / Mathf.Max(1, s.count)) * i;
                float angle = baseAngle + Random.Range(-s.angleJitter, s.angleJitter);
                float radius = Mathf.Max(0.01f, s.startRadius + Random.Range(-s.radiusJitter, s.radiusJitter));
                float wait = s.stagger * i;

                if (wait <= 0f) SpawnPiece(center, angle, radius, s);
                else
                {
                    var at = center;   // capture: the target may have moved by the time this fires
                    Tween.Delay(wait, () => { if (this != null) SpawnPiece(at, angle, radius, s); });
                }
            }
        }

        void SpawnPiece(Vector3 center, float angleDeg, float radius, ConvergeSettings s)
        {
            var go = new GameObject("ConvergePiece");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s.sprites != null && s.sprites.Length > 0
                ? s.sprites[Random.Range(0, s.sprites.Length)]
                : FXSprites.SoftCircle;
            if (sr.sprite == null) sr.sprite = FXSprites.SoftCircle;   // tolerate empty array slots
            sr.color = s.colors != null && s.colors.Length > 0
                ? s.colors[Random.Range(0, s.colors.Length)]
                : Color.white;
            sr.sortingOrder = s.sortingOrder;
            sr.flipX = Random.value < 0.5f;   // free variety on real art

            Color baseCol = sr.color;
            float size = s.size * Random.Range(0.85f, 1.15f);
            float startAng = Random.Range(0f, 360f);   // self-rotation start
            float spinDir = Random.value < 0.5f ? -1f : 1f;
            float life = Mathf.Max(0.05f, s.duration) * Random.Range(0.92f, 1.08f);

            go.transform.position = center + Polar(angleDeg, radius);
            go.transform.localScale = Vector3.one * size;

            Tween.Custom(0f, 1f, life, onValueChange: t =>
            {
                if (go == null) return;

                // curve drives how far in it has come; ease-in = hangs, then snaps home
                float k = Mathf.Clamp01(s.pull.Evaluate(t));
                float r = Mathf.Lerp(radius, 0f, k);
                float a = angleDeg + s.swirlDegrees * k;
                go.transform.position = center + Polar(a, r);
                go.transform.localRotation =
                    Quaternion.Euler(0f, 0f, startAng + s.spinDegrees * k * spinDir);
                go.transform.localScale = Vector3.one * (size * Mathf.Lerp(1f, s.endScale, k));

                float alpha = t > s.fadeStart
                    ? 1f - (t - s.fadeStart) / Mathf.Max(0.0001f, 1f - s.fadeStart) : 1f;
                sr.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * alpha);
            }, ease: Ease.Linear).OnComplete(() => { if (go != null) Destroy(go); });
        }

        static Vector3 Polar(float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, 0f);
        }
    }
}
