using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace Flynn.Environment
{
    /// <summary>
    /// Fill driver for the WaterStack template: puts a runtime Flynn/WaterFill
    /// material on the SpriteShapeRenderer's fill slot, bakes a distance-to-shore
    /// mask from the shape's own spline (shared ContourFalloff baker — same tech as
    /// the island's edge darken), and pushes <see cref="WaterVisualProfile"/> values
    /// every update (edit mode included) so the profile tunes live. The distance
    /// mask drives every shore-aware layer: shallow→deep gradient, foam band,
    /// lapping ripple, and the soft edge blend onto the land below.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteShapeRenderer))]
    public class WaterSurface : MonoBehaviour
    {
        private const string ShaderName = "Flynn/WaterFill";
        private const string RimShaderName = "Flynn/WaterRim";

        [Tooltip("Look values. Swap asset = restyle every surface using it.")]
        public WaterVisualProfile profile;

        private SpriteShapeRenderer _renderer;
        private SpriteShapeController _controller;
        private TilemapToSpriteShape _contourSource;
        private Material _material;
        private Material _rimMaterial;
        private Texture2D _falloffTex;
        private Vector4 _falloffRect;
        private float _bakedWidth = -1f;
        private int _bakedSplineHash;

        private static readonly int WaterTexId = Shader.PropertyToID("_WaterTex");
        private static readonly int WaterTexScaleId = Shader.PropertyToID("_WaterTexScale");
        private static readonly int WaterScrollId = Shader.PropertyToID("_WaterScroll");
        private static readonly int LayerAnimId = Shader.PropertyToID("_LayerAnim");
        private static readonly int BodyColorId = Shader.PropertyToID("_BodyColor");
        private static readonly int ShoreColorId = Shader.PropertyToID("_ShoreColor");
        private static readonly int ShoreWidthId = Shader.PropertyToID("_ShoreWidth");
        private static readonly int RingSpacingId = Shader.PropertyToID("_RingSpacing");
        private static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
        private static readonly int RingSpeedId = Shader.PropertyToID("_RingSpeed");
        private static readonly int RingStrengthId = Shader.PropertyToID("_RingStrength");
        private static readonly int RingWobbleId = Shader.PropertyToID("_RingWobble");
        private static readonly int UnderTexId = Shader.PropertyToID("_UnderTex");
        private static readonly int UnderTexScaleId = Shader.PropertyToID("_UnderTexScale");
        private static readonly int UnderTintId = Shader.PropertyToID("_UnderTint");
        private static readonly int FloorVisId = Shader.PropertyToID("_FloorVis");
        private static readonly int BankColorId = Shader.PropertyToID("_BankColor");
        private static readonly int BankHeightId = Shader.PropertyToID("_BankHeight");
        private static readonly int BankOffsetYId = Shader.PropertyToID("_BankOffsetY");
        private static readonly int BankSideInsetId = Shader.PropertyToID("_BankSideInset");
        private static readonly int BankStrengthId = Shader.PropertyToID("_BankStrength");
        private static readonly int EdgeBlendId = Shader.PropertyToID("_EdgeBlend");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int FalloffTexId = Shader.PropertyToID("_FalloffTex");
        private static readonly int FalloffRectId = Shader.PropertyToID("_FalloffRect");
        private static readonly int FalloffWidthId = Shader.PropertyToID("_FalloffWorldWidth");
        private static readonly int RimTintId = Shader.PropertyToID("_Tint");
        private static readonly int RimOuterFadeId = Shader.PropertyToID("_OuterFade");
        private static readonly int RimWetBandId = Shader.PropertyToID("_WetBand");
        private static readonly int RimWetColorId = Shader.PropertyToID("_WetColor");
        private static readonly int RimSwayAmpId = Shader.PropertyToID("_SwayAmp");
        private static readonly int RimSwaySpeedId = Shader.PropertyToID("_SwaySpeed");
        private static readonly int RimSwayScaleId = Shader.PropertyToID("_SwayScale");

        private void OnEnable()
        {
            _renderer = GetComponent<SpriteShapeRenderer>();
            _controller = GetComponent<SpriteShapeController>();
            _contourSource = GetComponent<TilemapToSpriteShape>();
            EnsureMaterial();
            if (_contourSource != null) _contourSource.ContourChanged += OnContourChanged;
            _bakedSplineHash = 0; // force rebake on next update
            // Push immediately — [ExecuteAlways] Update does not tick on an idle
            // editor, so a freshly instantiated stack would sit on material
            // defaults (gray fill) until something else caused a player-loop tick.
            Update();
        }

        private void OnDisable()
        {
            if (_contourSource != null) _contourSource.ContourChanged -= OnContourChanged;
            DestroyHelper(_material);
            _material = null;
            DestroyHelper(_rimMaterial);
            _rimMaterial = null;
            DestroyHelper(_falloffTex);
            _falloffTex = null;
        }

        // Full push, not just a flag: in edit mode Update() may never tick again
        // on an idle editor, so react synchronously when the contour changes.
        private void OnContourChanged()
        {
            _bakedSplineHash = 0;
            Update();
        }

        private float _appliedRimScale = -1f;

        private void Update()
        {
            if (_renderer == null || profile == null) return;
            // Contour knobs live on the profile (no-op unless changed) — spawned
            // stacks stay consistent with the asset instead of instance tweaks
            // that DontSave rebuilds throw away.
            if (_contourSource != null) _contourSource.ApplyContourSettings(profile.contour);
            EnsureMaterial();
            if (_material == null) return;

            bool splineChanged = BakeIfStale();
            ApplyRimScale(splineChanged);

            // Universal scale: every world-unit knob is authored in player/tile
            // units and multiplied here — one slider rescales the whole water.
            float u = Mathf.Max(0.01f, profile.unitScale);

            if (profile.waterTexture != null)
                _material.SetTexture(WaterTexId, profile.waterTexture);
            _material.SetFloat(WaterTexScaleId, Mathf.Max(0.01f, profile.textureScale) * u);
            _material.SetVector(WaterScrollId, profile.scrollSpeed * u);
            _material.SetFloat(LayerAnimId, profile.layerAnim);
            _material.SetColor(BodyColorId, profile.bodyColor);
            _material.SetColor(ShoreColorId, profile.shoreColor);
            _material.SetFloat(ShoreWidthId, profile.shoreWidth * u);
            _material.SetFloat(RingSpacingId, Mathf.Max(0.01f, profile.ringSpacing) * u);
            _material.SetFloat(RingWidthId, Mathf.Max(0.005f, profile.ringWidth) * u);
            _material.SetFloat(RingSpeedId, profile.ringSpeed);
            _material.SetFloat(RingStrengthId, profile.ringStrength);
            _material.SetFloat(RingWobbleId, profile.ringWobble * u);
            if (profile.underwaterTexture != null)
                _material.SetTexture(UnderTexId, profile.underwaterTexture);
            _material.SetFloat(UnderTexScaleId, Mathf.Max(0.01f, profile.underwaterScale) * u);
            _material.SetColor(UnderTintId, profile.underwaterTint);
            _material.SetFloat(FloorVisId, profile.floorVisibility);
            _material.SetColor(BankColorId, profile.bankColor);
            _material.SetFloat(BankHeightId, profile.bankHeight * u);
            _material.SetFloat(BankOffsetYId, profile.bankOffsetY * u);
            _material.SetFloat(BankSideInsetId, profile.bankSideInset * u);
            _material.SetFloat(BankStrengthId, profile.bankStrength);
            _material.SetFloat(EdgeBlendId, profile.edgeBlend * u);
            _material.SetFloat(AlphaId, profile.alpha);

            if (_rimMaterial != null)
            {
                _rimMaterial.SetColor(RimTintId, profile.rimTint);
                _rimMaterial.SetFloat(RimOuterFadeId, profile.rimOuterFade);
                _rimMaterial.SetFloat(RimWetBandId, profile.rimWetBand);
                _rimMaterial.SetColor(RimWetColorId, profile.rimWetColor);
                _rimMaterial.SetFloat(RimSwayAmpId, profile.rimSway * u);
                _rimMaterial.SetFloat(RimSwaySpeedId, profile.rimSwaySpeed);
                _rimMaterial.SetFloat(RimSwayScaleId, 1.5f * u);
            }
            if (_falloffTex != null)
            {
                _material.SetTexture(FalloffTexId, _falloffTex);
                _material.SetVector(FalloffRectId, _falloffRect);
                _material.SetFloat(FalloffWidthId, _bakedWidth);
            }
        }

        /// <summary>
        /// Rebake the distance-to-shore mask when the spline or the required bake
        /// width changed. Spline sampled with a few bezier steps per segment —
        /// the mask is a soft gradient, control-point accuracy is plenty.
        /// Returns true when the spline changed (bake reran).
        /// </summary>
        private bool BakeIfStale()
        {
            if (_controller == null) return false;
            var spline = _controller.spline;
            int n = spline.GetPointCount();
            if (n < 3) return false;

            float width = profile.RequiredFalloffWidth;
            int hash = n;
            for (int i = 0; i < n; i++)
                hash = (hash * 397) ^ spline.GetPosition(i).GetHashCode();

            if (hash == _bakedSplineHash && Mathf.Approximately(width, _bakedWidth)) return false;
            _bakedSplineHash = hash;
            _bakedWidth = width;

            const int steps = 4;
            var pts = new List<Vector2>(n * steps);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 p0 = spline.GetPosition(i);
                Vector3 p3 = spline.GetPosition(j);
                Vector3 p1 = p0 + spline.GetRightTangent(i);
                Vector3 p2 = p3 + spline.GetLeftTangent(j);
                for (int s = 0; s < steps; s++)
                {
                    float t = (float)s / steps;
                    float u = 1f - t;
                    Vector3 p = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
                    pts.Add(new Vector2(p.x, p.y));
                }
            }

            // Resolution keyed to the THINNEST shore feature (shore line / edge
            // blend), not the widest band — thin lines stay crisp.
            float minFeature = Mathf.Max(0.02f, Mathf.Min(
                profile.shoreWidth > 0f ? profile.shoreWidth : width,
                profile.edgeBlend > 0f ? profile.edgeBlend : width));
            ContourFalloff.Bake(pts, width, ref _falloffTex, out _falloffRect, minFeature / 6f);
            return true;
        }

        /// <summary>
        /// Trim thickness via native spline height (edge-sprite scale without PPU).
        /// The contour tracer rebuilds the spline with default heights on every
        /// regen, so reapply whenever the spline changed or the knob moved.
        /// </summary>
        private void ApplyRimScale(bool splineChanged)
        {
            if (_controller == null) return;
            float s = Mathf.Clamp(profile.rimScale * Mathf.Max(0.01f, profile.unitScale), 0.1f, 4f);
            if (!splineChanged && Mathf.Approximately(s, _appliedRimScale)) return;
            _appliedRimScale = s;
            var spline = _controller.spline;
            int n = spline.GetPointCount();
            for (int i = 0; i < n; i++)
                spline.SetHeight(i, s);
            _controller.RefreshSpriteShape();
        }

        // Runtime materials only (island convention — no .mat assets to manage);
        // fill = materials[0], edge sprites (grass trim rim) = materials[1].
        private void EnsureMaterial()
        {
            if (_material != null && _rimMaterial != null) return;

            if (_material == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[WaterSurface] Shader '{ShaderName}' not found.", this);
                    return;
                }
                _material = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            if (_rimMaterial == null)
            {
                var rimShader = Shader.Find(RimShaderName);
                if (rimShader != null)
                    _rimMaterial = new Material(rimShader) { hideFlags = HideFlags.DontSave };
            }

            var mats = _renderer.sharedMaterials;
            if (mats.Length < 2) mats = new Material[2];
            mats[0] = _material;
            mats[1] = _rimMaterial != null ? _rimMaterial : mats[1];
            _renderer.sharedMaterials = mats;
        }

        private static void DestroyHelper(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
