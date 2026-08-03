using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Watering-can droplet arc: a cone of stretched droplets thrown along a direction,
    /// pulled down by gravity, fading as they land. Spawner - one instance serves the
    /// scene: PlayAt(origin, direction).
    /// </summary>
    public class DropletSprayFX : MonoBehaviour
    {
        [Tooltip("Standalone tuning source for the short PlayAt overload.")]
        public FXLabTuning tuning;

        public void PlayAt(Vector3 pos, Vector2 dir)
            => PlayAt(pos, dir, tuning != null ? tuning.droplets : null);

        public void PlayAt(Vector3 pos, Vector2 dir, DropletSettings s)
        {
            if (s == null) return;
            FXAudio.Play(s.sfx, pos);
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
            dir.Normalize();

            var root = new GameObject("Droplets");
            root.transform.position = pos;

            for (int i = 0; i < s.count; i++)
            {
                var go = new GameObject("Drop").AddComponent<SpriteRenderer>();
                go.transform.SetParent(root.transform, false);
                go.sprite = FXSprites.SoftCircle;
                go.color = s.color;
                go.sortingOrder = s.sortingOrder;

                float ang = Random.Range(-s.spreadDegrees, s.spreadDegrees) * 0.5f;
                Vector2 v = (Vector2)(Quaternion.Euler(0f, 0f, ang) * dir)
                    * (s.speed * Random.Range(0.7f, 1.3f));
                float life = s.life * Random.Range(0.8f, 1.2f);
                float size = s.size * Random.Range(0.8f, 1.2f);
                var tr = go.transform;
                var sr = go;

                Tween.Custom(0f, 1f, life, onValueChange: t =>
                {
                    if (tr == null) return;
                    float tt = t * life;
                    Vector2 vel = v + Vector2.up * (s.gravity * tt);
                    tr.localPosition = (Vector3)(v * tt + 0.5f * s.gravity * tt * tt * Vector2.up);
                    // stretch along current velocity - reads as falling water
                    tr.rotation = Quaternion.FromToRotation(Vector3.up, vel);
                    tr.localScale = new Vector3(size, size * s.stretch, 1f);
                    var c = s.color;
                    c.a *= Mathf.InverseLerp(1f, 0.7f, t);   // fade the last 30%
                    sr.color = c;
                });
            }
            Destroy(root, s.life * 1.3f + 0.1f);
        }
    }
}
