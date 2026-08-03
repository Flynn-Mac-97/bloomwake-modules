using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Rising icon aura: plus signs / hearts / leaves drift up off a tended thing and fade.
    /// The "that did something good" read, borrowed from the healing-number vocabulary but
    /// kept cozy - no numbers, no pop-and-snap, just a soft column of icons leaving.
    ///
    /// Two details do most of the work. Icons EMIT over a window instead of bursting, so it
    /// reads as ongoing care rather than an impact; and each one sways on a random phase as it
    /// climbs, so a repeat never looks like the same stamp twice. Spawner: one instance serves
    /// the scene, fire at any position with any settings block.
    /// </summary>
    public class FloatAuraFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        public void Play()
        {
            if (tuning != null) PlayAt(transform.position, tuning.floatAura);
        }

        public void PlayAt(Vector3 pos, FloatAuraSettings s)
        {
            if (s == null) return;
            FXAudio.Play(s.sfx, pos);

            for (int i = 0; i < s.count; i++)
            {
                float wait = s.emitOver <= 0f ? 0f : s.emitOver * (i / Mathf.Max(1f, s.count - 1f));
                if (wait <= 0f) SpawnIcon(pos, s);
                else
                {
                    var at = pos;   // capture: the emitter may have moved by the time this fires
                    Tween.Delay(wait, () => { if (this != null) SpawnIcon(at, s); });
                }
            }
        }

        void SpawnIcon(Vector3 pos, FloatAuraSettings s)
        {
            var go = new GameObject("AuraIcon");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s.sprites != null && s.sprites.Length > 0
                ? s.sprites[Random.Range(0, s.sprites.Length)]
                : FXSprites.SoftCircle;
            if (sr.sprite == null) sr.sprite = FXSprites.SoftCircle;   // tolerate empty array slots
            sr.color = s.colors != null && s.colors.Length > 0
                ? s.colors[Random.Range(0, s.colors.Length)]
                : Color.white;
            sr.sortingOrder = s.sortingOrder;

            Vector3 start = pos + new Vector3(Random.Range(-s.spread, s.spread) * 0.5f, 0f, 0f);
            go.transform.position = start;

            Color baseCol = sr.color;
            float size = s.size * Random.Range(0.85f, 1.15f);
            float tilt = Random.Range(-s.tiltDegrees, s.tiltDegrees);
            float swayPhase = Random.value * Mathf.PI * 2f;
            float swaySign = Random.value < 0.5f ? -1f : 1f;
            float life = s.riseDuration * Random.Range(0.9f, 1.1f);

            Tween.Custom(0f, 1f, life, onValueChange: t =>
            {
                if (go == null) return;

                // ease-out climb: quick off the mark, slowing as it fades - a released thing,
                // not a thrown one
                float climb = 1f - (1f - t) * (1f - t);
                float sway = Mathf.Sin(swayPhase + t * s.swayHz * Mathf.PI * 2f) * s.swayAmp * swaySign;
                go.transform.position = start + new Vector3(sway, s.riseHeight * climb, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

                float grow = s.scaleIn > 0.0001f ? Mathf.Clamp01(t / s.scaleIn) : 1f;
                grow = 1f - Mathf.Pow(1f - grow, 3f);   // ease out, so the pop lands soft
                go.transform.localScale = Vector3.one * (size * grow);

                float a = t > s.fadeStart
                    ? 1f - (t - s.fadeStart) / Mathf.Max(0.0001f, 1f - s.fadeStart) : 1f;
                sr.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * a);
            }, ease: Ease.Linear).OnComplete(() => { if (go != null) Destroy(go); });
        }
    }
}
