using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Shader radial-wipe swing arc (Cyanilux-style on a flat quad): the Flynn/FXLab/ArcWipe
    /// shader reveals a wobble-edged ring band as _Progress sweeps, with a fading tail and
    /// bright head. All shape params pushed per-play via MaterialPropertyBlock, so tuning-asset
    /// edits apply live. Lives on its own pivot; builds its quad at runtime.
    /// </summary>
    public class ArcWipeFX : MonoBehaviour
    {
        static readonly int Progress = Shader.PropertyToID("_Progress");
        static readonly int Fade = Shader.PropertyToID("_Fade");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int SpanDeg = Shader.PropertyToID("_SpanDeg");
        static readonly int TailDeg = Shader.PropertyToID("_TailDeg");
        static readonly int InnerR = Shader.PropertyToID("_InnerR");
        static readonly int OuterR = Shader.PropertyToID("_OuterR");
        static readonly int EdgeSoft = Shader.PropertyToID("_EdgeSoft");
        static readonly int Wobble = Shader.PropertyToID("_Wobble");
        static readonly int Seed = Shader.PropertyToID("_Seed");

        public FXLabTuning tuning;
        [Tooltip("Material using Flynn/FXLab/ArcWipe (scene builder assigns the shared asset).")]
        public Material wipeMaterial;

        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        Tween _tween;

        public void Play(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
            PlayAtAngle(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, null, null);
        }

        /// <summary>
        /// Swing toward a single 0-360 facing angle, rigged on the character.
        ///
        /// The arc is CENTRED on the pivot's origin and parented to it, so rotating Z sweeps the
        /// whole band around the character rather than swinging a quad that happens to sit
        /// nearby - which is what made the old three-direction setup need a different offset per
        /// direction. One angle now covers the full circle, and because the effect rides the
        /// pivot it tracks the character while it plays.
        /// </summary>
        /// <param name="facingDeg">Where the character is swinging. 0 = +X, counter-clockwise.</param>
        /// <param name="pivot">Character transform to ride. Null = stay where this object is.</param>
        public void PlayAtAngle(float facingDeg, Transform pivot, SwingRigSettings rig)
        {
            if (tuning == null || wipeMaterial == null) return;
            var s = tuning.arcWipe;
            rig = rig ?? tuning.swingRig;
            EnsureQuad();
            if (_tween.isAlive) _tween.Stop();

            if (pivot != null && transform.parent != pivot)
                transform.SetParent(pivot, worldPositionStays: false);
            if (pivot != null) transform.localPosition = Vector3.zero;   // origin ON the character

            FXAudio.Play(s.sfx, transform.position);

            transform.localRotation = Quaternion.Euler(0f, 0f, facingDeg + rig.angleOffsetDeg);
            // offset lives on the QUAD, inside the rotated frame, so it swings round with the arc
            _renderer.transform.localPosition = rig.pivotOffset;
            _renderer.sortingOrder = SortOrder(pivot, rig);
            _renderer.transform.localScale = Vector3.one * s.worldSize;
            _renderer.enabled = true;
            float seed = Random.value * 20f;

            // extend past progress=1 so the tail finishes fading after the head lands
            float total = s.duration * 1.35f;
            _tween = Tween.Custom(0f, 1f, total, onValueChange: t =>
            {
                if (_renderer == null) return;
                float p = Mathf.Clamp01(t * 1.35f);
                float fade = t > 0.74f ? 1f - (t - 0.74f) / 0.26f : 1f;
                _renderer.GetPropertyBlock(_mpb);
                _mpb.SetFloat(Progress, p);
                _mpb.SetFloat(Fade, fade);
                _mpb.SetColor(ColorId, s.color);
                _mpb.SetFloat(SpanDeg, s.spanDegrees);
                _mpb.SetFloat(TailDeg, s.tailDegrees);
                _mpb.SetFloat(InnerR, s.innerRadius);
                _mpb.SetFloat(OuterR, s.outerRadius);
                _mpb.SetFloat(EdgeSoft, s.edgeSoft);
                _mpb.SetFloat(Wobble, s.wobble);
                _mpb.SetFloat(Seed, seed);
                _renderer.SetPropertyBlock(_mpb);
            }, ease: Ease.Linear).OnComplete(() => { if (_renderer != null) _renderer.enabled = false; });
        }

        /// <summary>Tuck the arc under the character when the rig asks for it - resolved per
        /// play, so it stays correct as the character's own Y-sort order changes.</summary>
        static int SortOrder(Transform pivot, SwingRigSettings rig)
        {
            if (!rig.behindPlayer || pivot == null) return rig.sortingOrder;
            var psr = pivot.GetComponentInChildren<SpriteRenderer>();
            return (psr != null ? psr.sortingOrder : 0) - 1;
        }

        void EnsureQuad()
        {
            if (_renderer != null) return;
            _mpb = new MaterialPropertyBlock();

            var go = new GameObject("WipeQuad");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = BuildQuad();
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = wipeMaterial;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sortingOrder = 50;
            _renderer.enabled = false;
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "ArcWipeQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f)
            };
            m.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            return m;
        }

        void OnDisable()
        {
            if (_tween.isAlive) _tween.Stop();
            if (_renderer != null) _renderer.enabled = false;
        }
    }
}
