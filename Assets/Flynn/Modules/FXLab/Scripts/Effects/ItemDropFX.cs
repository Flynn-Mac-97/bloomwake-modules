using System.Collections;
using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Juicy item drop: a dummy item pops out of the source on a parabola hop away from
    /// the player, lands with a dust puff and shrinking settle bounces, rests, then fades
    /// (lab cleanup — in the real game the item stays and hands over to the pickup flow).
    /// </summary>
    public class ItemDropFX : MonoBehaviour
    {
        public FXLabTuning tuning;
        [Tooltip("Shared puff spawner on the board — used for the landing dust.")]
        public PuffBurstFX puffer;
        [Tooltip("Idle spawner on the board — the settled item hands off to the bob loop.")]
        public ItemIdleFX idleFX;

        public void Play(Vector3 from, Vector2 dirAway)
        {
            if (tuning == null) return;
            Play(from, dirAway, tuning.drop);
        }

        /// <summary>Drop with an explicit settings block.</summary>
        public void Play(Vector3 from, Vector2 dirAway, DropSettings s)
        {
            if (s == null) return;
            StartCoroutine(Drop(s, from, dirAway));
        }

        IEnumerator Drop(DropSettings s, Vector3 from, Vector2 dirAway)
        {
            if (dirAway.sqrMagnitude < 0.001f) dirAway = Vector2.right;
            // fan the away direction so consecutive drops scatter instead of stacking
            float fan = Random.Range(-s.spreadDegrees, s.spreadDegrees);
            dirAway = Quaternion.Euler(0f, 0f, fan) * dirAway.normalized;

            var go = new GameObject("DropItem");
            go.transform.position = from;
            go.transform.localScale = Vector3.one * s.itemSize;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s.itemSprite != null ? s.itemSprite : FXSprites.Square;
            sr.color = s.itemTint;
            sr.sortingOrder = 30;

            Vector3 land = from + (Vector3)(dirAway * (s.hopDistance * Random.Range(0.65f, 1.35f)))
                + (Vector3)(Random.insideUnitCircle * 0.08f);

            // main hop: straight lerp + parabola lift
            float t = 0f;
            while (t < 1f && go != null)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(0.05f, s.hopDuration));
                go.transform.position = Vector3.Lerp(from, land, t)
                    + Vector3.up * (Mathf.Sin(t * Mathf.PI) * s.hopHeight);
                yield return null;
            }
            if (go == null) yield break;

            // landing: sound + dust + settle bounces, each smaller
            FXAudio.Play(s.sfx, land);
            if (puffer != null) puffer.PlayAt(land, s.landPuff);

            float h = s.hopHeight;
            for (int i = 0; i < s.bounces && go != null; i++)
            {
                h *= 0.35f;
                float dur = s.hopDuration * 0.4f;
                float bt = 0f;
                while (bt < 1f && go != null)
                {
                    bt = Mathf.Min(1f, bt + Time.deltaTime / dur);
                    go.transform.position = land + Vector3.up * (Mathf.Sin(bt * Mathf.PI) * h);
                    // squash on touch-down, stretch mid-air
                    float sq = Mathf.Sin(bt * Mathf.PI) * 0.25f;
                    go.transform.localScale = new Vector3(
                        s.itemSize * (1f - sq * 0.5f), s.itemSize * (1f + sq), 1f);
                    yield return null;
                }
            }
            if (go == null) yield break;
            go.transform.localScale = Vector3.one * s.itemSize;

            // the real game flow: the settled item lifts into the idle bob
            if (s.restToIdle && idleFX != null)
            {
                idleFX.Spawn(land);
                Destroy(go);
                yield break;
            }

            // fallback: rest, then fade out
            yield return new WaitForSeconds(s.restSeconds);
            if (go == null) yield break;
            Tween.Custom(0f, 1f, 0.25f, onValueChange: ft =>
            {
                if (sr == null) return;
                sr.color = new Color(s.itemTint.r, s.itemTint.g, s.itemTint.b, 1f - ft);
            }, ease: Ease.Linear).OnComplete(() => { if (go != null) Destroy(go); });
        }
    }
}
