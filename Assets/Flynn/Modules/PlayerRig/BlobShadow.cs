using Flynn.Contracts;
using UnityEngine;

namespace Flynn.Modules.PlayerRig
{
    /// <summary>
    /// Ground blob shadow that shrinks + fades as the character lifts off the ground (hop / jump),
    /// selling the height. Reads an <see cref="ILiftProvider"/> (e.g. PlayerMover) — no concrete
    /// coupling. Put this on the shadow object (or set the refs); the shadow itself must NOT be a
    /// child of the hopping visual, so it stays planted while the visual rises.
    /// </summary>
    public class BlobShadow : MonoBehaviour
    {
        [Tooltip("Lift source (must implement ILiftProvider). Auto-finds one in parents if empty.")]
        [SerializeField] MonoBehaviour liftSource;
        ILiftProvider lift;

        [Tooltip("Shadow transform to scale. Falls back to this object.")]
        public Transform shadow;
        [Tooltip("Shadow renderer to fade. Falls back to a SpriteRenderer on the shadow.")]
        public SpriteRenderer shadowRenderer;

        [Tooltip("Lift (world units) at which the shadow reaches its smallest/faintest — set to your jumpHeight.")]
        public float maxLift = 0.6f;
        [Tooltip("Shadow scale at max lift, as a fraction of its grounded size.")]
        [Range(0.05f, 1f)] public float minScale = 0.55f;
        [Tooltip("Shadow alpha at max lift, as a fraction of its grounded alpha.")]
        [Range(0f, 1f)] public float minAlpha = 0.35f;
        [Tooltip("Shape the falloff: <1 drops off fast near the ground, >1 stays big longer.")]
        [Range(0.4f, 2f)] public float curve = 1f;

        Vector3 _baseScale;
        float _baseAlpha;

        void Awake()
        {
            lift = liftSource as ILiftProvider ?? GetComponentInParent<ILiftProvider>();
            if (shadow == null) shadow = transform;
            if (shadowRenderer == null) shadowRenderer = shadow.GetComponent<SpriteRenderer>();
            _baseScale = shadow.localScale;
            _baseAlpha = shadowRenderer != null ? shadowRenderer.color.a : 1f;
        }

        void LateUpdate()
        {
            if (lift == null) return;

            float t = Mathf.Pow(Mathf.Clamp01(lift.Lift / Mathf.Max(0.001f, maxLift)), curve);

            shadow.localScale = _baseScale * Mathf.Lerp(1f, minScale, t);

            if (shadowRenderer != null)
            {
                var c = shadowRenderer.color;
                c.a = _baseAlpha * Mathf.Lerp(1f, minAlpha, t);
                shadowRenderer.color = c;
            }
        }
    }
}
