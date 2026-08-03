using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Leaf/petal/chip debris burst — soft dots thrown up-and-out on little gravity arcs,
    /// shrinking and fading. Cozy contact response, not shrapnel. Fires at any position
    /// with any settings block, so composed moments (wood chips vs metal sparks) reuse the
    /// one spawner on the board. Spawns throwaway sprites; pool later if it graduates hot paths.
    /// </summary>
    public class PuffBurstFX : MonoBehaviour
    {
        public FXLabTuning tuning;

        public void Play()
        {
            if (tuning != null) PlayAt(transform.position, tuning.puff);
        }

        /// <param name="sortLike">The thing this burst happened TO. Motes take its sorting layer
        /// and sit one order above it, so debris reads as coming off the front of the object
        /// instead of behind it. Null = the flat fallback order, which is only correct in a scene
        /// with no depth sorting.</param>
        public void PlayAt(Vector3 pos, PuffSettings s, Transform sortLike = null)
        {
            if (s == null) return;
            FXAudio.Play(s.sfx, pos);

            int layer = 0, order = FallbackOrder;
            var host = sortLike != null ? sortLike.GetComponentInChildren<SpriteRenderer>() : null;
            if (host != null)
            {
                layer = host.sortingLayerID;
                order = host.sortingOrder + 1;
            }

            for (int i = 0; i < s.count; i++)
                SpawnMote(pos, s, layer, order, host != null);
        }

        /// <summary>Order used when the burst has nothing to sort against (lab scenes, world puffs).</summary>
        const int FallbackOrder = 40;

        void SpawnMote(Vector3 pos, PuffSettings s, int sortingLayerID, int sortingOrder, bool hasHost)
        {
            var go = new GameObject("PuffMote");
            go.transform.position = pos + (Vector3)(Random.insideUnitCircle * 0.06f);
            var sr = go.AddComponent<SpriteRenderer>();

            // a sheet turns each mote into its own little dust animation; without one the
            // mote is a single sprite picked from the array (or the placeholder dot)
            var frames = FXSheetFrames.Resolve(s.anim);
            if (frames != null && frames.Length == 0) frames = null;
            sr.sprite = frames != null
                ? frames[0]
                : s.sprites != null && s.sprites.Length > 0
                    ? s.sprites[Random.Range(0, s.sprites.Length)]
                    : FXSprites.SoftCircle;
            if (sr.sprite == null) sr.sprite = FXSprites.SoftCircle;   // tolerate empty array slots
            sr.color = s.colors != null && s.colors.Length > 0
                ? s.colors[Random.Range(0, s.colors.Length)]
                : Color.white;
            if (hasHost) sr.sortingLayerID = sortingLayerID;
            sr.sortingOrder = sortingOrder;
            sr.flipX = Random.value < 0.5f;   // free variety on real art

            // up-biased launch direction
            Vector2 dir = Vector2.Lerp(Random.insideUnitCircle.normalized, Vector2.up, s.upBias * Random.value).normalized;
            Vector2 vel = dir * (s.speed * Random.Range(0.6f, 1.3f));
            Vector3 start = go.transform.position;
            float size = s.size * Random.Range(0.7f, 1.4f);
            Color baseCol = sr.color;
            // tumble: random start angle + spin so chips read as torn-off fragments
            float startAng = Random.Range(0f, 360f);
            float spin = s.spinDegrees * Random.Range(0.4f, 1f) * (Random.value < 0.5f ? -1f : 1f);

            Tween.Custom(0f, 1f, s.life, onValueChange: t =>
            {
                if (go == null) return;
                float sec = t * s.life;
                go.transform.position = start + (Vector3)(vel * sec)
                    + Vector3.up * (0.5f * s.gravity * sec * sec);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, startAng + spin * sec);

                if (frames != null)
                {
                    // fit the anim to the mote's life, or run it at the sheet's own fps and
                    // hold the last frame for whatever life is left
                    int idx = s.animOverLife
                        ? (int)(t * frames.Length)
                        : (int)(sec * s.anim.fps);
                    idx = Mathf.Clamp(idx, 0, frames.Length - 1);
                    if (frames[idx] != null) sr.sprite = frames[idx];
                }

                // Scale from nothing (toon pop) then lose size over life. scaleIn 0 keeps the
                // original debris behaviour: full size on frame one.
                float grow = s.scaleIn > 0.0001f ? Mathf.Clamp01(t / s.scaleIn) : 1f;
                grow = 1f - Mathf.Pow(1f - grow, 3f);   // ease out, so the pop lands soft
                go.transform.localScale = Vector3.one * (size * grow * (1f - t * s.shrink));

                float a = t > s.fadeStart ? 1f - (t - s.fadeStart) / Mathf.Max(0.0001f, 1f - s.fadeStart) : 1f;
                sr.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * a);
            }, ease: Ease.Linear).OnComplete(() => { if (go != null) Destroy(go); });
        }
    }
}
