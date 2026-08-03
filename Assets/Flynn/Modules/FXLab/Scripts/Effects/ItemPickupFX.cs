using System.Collections;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Juicy pickup: the item does a tiny anticipation hop, then magnet-arcs to the
    /// receiver (chasing them live), and on arrival the receiver squash-pops while gold
    /// absorb motes rise off them. Anticipation -> accelerate -> ack: the whole grammar
    /// of a satisfying collect in one moment.
    /// </summary>
    public class ItemPickupFX : MonoBehaviour
    {
        public FXLabTuning tuning;
        [Tooltip("Shared sparkle spawner on the board — used for the absorb motes.")]
        public SparkleFX sparkler;

        public void Play(Vector3 from, Transform receiver, SquashFX receiverSquash)
        {
            if (tuning == null) return;
            Play(from, receiver, receiverSquash, tuning.pickup);
        }

        /// <summary>Pickup with an explicit settings block.</summary>
        public void Play(Vector3 from, Transform receiver, SquashFX receiverSquash, PickupSettings s)
        {
            if (s == null || receiver == null) return;
            StartCoroutine(Pickup(s, from, receiver, receiverSquash));
        }

        IEnumerator Pickup(PickupSettings s, Vector3 from, Transform receiver, SquashFX receiverSquash)
        {
            var go = new GameObject("PickupItem");
            go.transform.position = from;
            go.transform.localScale = Vector3.one * s.itemSize;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s.itemSprite != null ? s.itemSprite : FXSprites.Square;
            sr.color = s.itemTint;
            sr.sortingOrder = 30;

            // anticipation: little hop up before the pull
            float at = 0f;
            while (at < 1f && go != null)
            {
                at = Mathf.Min(1f, at + Time.deltaTime / 0.1f);
                go.transform.position = from + Vector3.up * (Mathf.Sin(at * Mathf.PI) * s.anticipation);
                yield return null;
            }
            if (go == null) yield break;

            // magnet flight: chase the live receiver position, arc sideways, shrink in
            float t = 0f;
            while (t < 1f && go != null && receiver != null)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(0.05f, s.flyDuration));
                float k = Mathf.Clamp01(s.flyEase.Evaluate(t));
                Vector3 target = receiver.position + Vector3.up * 0.1f;
                go.transform.position = Vector3.Lerp(from, target, k)
                    + Vector3.up * (Mathf.Sin(k * Mathf.PI) * s.arcHeight);
                go.transform.localScale = Vector3.one * (s.itemSize * (1f - 0.5f * k));
                yield return null;
            }
            if (go != null) Destroy(go);
            if (receiver == null) yield break;

            // arrival ack: sound + gentle receive swell + absorb motes
            FXAudio.Play(s.sfx, receiver.position);
            if (receiverSquash != null) receiverSquash.Pop(s.receiverPopScale, s.receiverPopDuration);
            if (sparkler != null) sparkler.PlayAt(receiver.position + Vector3.up * 0.25f, s.absorb);
        }
    }
}
