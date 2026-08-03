using System;
using UnityEngine;
using UnityEngine.U2D;

namespace Flynn.Environment
{
    /// <summary>
    /// One asset = one island look. Every visual knob of the floating-island
    /// stack (contour styling, cliff skirt, top-fill surface, grass fringe)
    /// lives here as raw values. IslandSkirt builds runtime materials from
    /// the shader references and pushes these values every frame — swapping
    /// profile assets restyles an island live, and no per-profile material
    /// assets exist. Field initializers are the built-in defaults, so a
    /// fresh (or missing) profile renders the stock look.
    /// </summary>
    [CreateAssetMenu(menuName = "Flynn/Environment/Island Visual Profile", fileName = "IslandVisualProfile")]
    public class IslandVisualProfile : ScriptableObject
    {
        // -------------------------------------------------------------------
        // Setting groups (mirror the old IslandSkirt inspector headers)
        // -------------------------------------------------------------------

        [Serializable]
        public class ContourSettings
        {
            [Tooltip("EdgeMidpoints = smooth contour through tile edge midpoints.\nTileCorners = blocky contour through tile corner vertices.")]
            public BoundaryMode boundaryMode = BoundaryMode.EdgeMidpoints;
            [Tooltip("Tangent mode for spline control points.")]
            public ShapeTangentMode tangentMode = ShapeTangentMode.Continuous;
            [Tooltip("Auto-inset vertices to compensate for bezier bulge on curved tangents. 1 = curve lands on the grid boundary, 0 = no inset.")]
            [Range(0f, 3f)] public float inset = 1f;
            [Tooltip("Tangent length as a fraction of the neighbour distance. 0.33 = Unity default, 0 = sharp corners, 1 = very curvy.")]
            [Range(0f, 1f)] public float tangentLength = 1f / 3f;
        }

        [Serializable]
        public class SkirtShapeSettings
        {
            [Tooltip("How far the cliff hangs below the island edge (world units).")]
            [Range(0.1f, 10f)] public float thickness = 1.5f;
            [Tooltip("Cliff profile by depth: X = 0 (top edge) → 1 (bottom), Y = horizontal silhouette offset (× Profile Strength; negative = inward). Keep Y at 0 for X=0 or the seam opens.")]
            public AnimationCurve profile = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, -0.4f));
            [Tooltip("World-unit multiplier for the profile curve.")]
            [Range(0f, 3f)] public float profileStrength = 0.4f;
            [Tooltip("Arc distance (world units) over which bulge directions are averaged. Raise if the bulge pinches or folds at tight corners.")]
            [Range(0.1f, 5f)] public float bulgeSmoothing = 1.5f;
            [Tooltip("Vertical subdivisions of the cliff wall — more = smoother profile curve.")]
            [Range(2, 16)] public int rows = 6;
            [Tooltip("Target sample density along the spline (world units, ~half this between samples).")]
            [Range(0.1f, 2f)] public float spacing = 0.35f;
            [Tooltip("How far the strip top tucks inside the island silhouette (hides the seam).")]
            [Range(0f, 0.5f)] public float topInset = 0.05f;
            [Tooltip("Strip ends taper to nothing over this × Spacing (world units of arc).")]
            [Range(1, 12)] public int taperColumns = 4;
            [Tooltip("Bridge gaps between downward-facing runs up to this long (world units), so blocky staircase silhouettes keep one continuous cliff.")]
            [Range(0f, 5f)] public float bridgeGaps = 1.5f;
        }

        [Serializable]
        public class SkirtShadingSettings
        {
            [Tooltip("Directional modelling: cliff faces angled away from the fake light darken.")]
            [Range(0f, 1f)] public float shadeStrength = 0.35f;
            [Tooltip("Fake light direction in degrees (0 = +X, 90 = up). 160 ≈ upper-left.")]
            [Range(0f, 360f)] public float shadeLightAngle = 160f;
            [Tooltip("Depth layering: walls hanging from higher (further back) edges darken, so stacked cliffs separate.")]
            [Range(0f, 0.6f)] public float layerShade = 0.25f;
            [Tooltip("Crevice AO: darken concave turns of the contour.")]
            [Range(0f, 1f)] public float creviceAO = 0.3f;
        }

        [Serializable]
        public class ErosionSettings
        {
            [Tooltip("Sideways displacement of the bottom edge along the contour normal (world units).")]
            [Range(0f, 2f)] public float noiseStrength = 0.25f;
            [Tooltip("Depth variation as a fraction of thickness.")]
            [Range(0f, 1f)] public float depthJitter = 0.3f;
            [Tooltip("Noise frequency (features per world unit).")]
            [Range(0.05f, 3f)] public float noiseScale = 0.6f;
            public int seed = 42;
        }

        public enum PaintBlend { Normal, Multiply, Screen, Overlay, ColorDodge, SoftLight, Add }

        [Serializable]
        public class PaintLayerSettings
        {
            [Tooltip("Layer colour; alpha = opacity over the layers below.")]
            public Color color = new Color(1f, 1f, 1f, 0f);
            [Tooltip("How this layer's colour combines with the layers below.")]
            public PaintBlend blendMode = PaintBlend.Normal;
            [Tooltip("World size of the noise region blobs.")]
            public float noiseScale = 6f;
            [Range(0f, 1f)] public float noiseThreshold = 0.5f;
            [Range(0.001f, 0.5f)] public float edgeSoftness = 0.08f;
            [Tooltip("Noise offset/seed — vary per layer so regions don't align.")]
            public float noiseOffset = 0f;
            [Tooltip("Which brush sheet the edge uses: 0 or 1.")]
            [Range(0, 1)] public int brushSheet = 0;
            [Tooltip("World size of one brush stamp cell.")]
            public float brushScale = 2.5f;
            [Tooltip("How hard the brush breaks the region edge into strokes.")]
            [Range(0f, 1f)] public float edgeBreak = 0.45f;
            [Tooltip("Brush cell range (min, max index, row-major inclusive).")]
            public Vector2 brushRange = new Vector2(0, 15);
            [Tooltip("Brush sheet grid (cols, rows).")]
            public Vector2 brushGrid = new Vector2(4, 4);
        }

        [Serializable]
        public class FillSettings
        {
            [Tooltip("Base surface tint under everything — the island's grass colour lives here. The SpriteShapeRenderer colour still multiplies on top for scene-level tinting.")]
            public Color baseTint = Color.white;
            [Tooltip("Rounded-edge illusion baked into the island fill: the top darkens toward the silhouette.")]
            public bool falloffEnabled = true;
            [Tooltip("How far the falloff reaches onto the island top (world units).")]
            [Range(0.05f, 3f)] public float falloffWidth = 0.5f;
            [Range(0f, 1f)] public float edgeDarken = 0.3f;
            [Tooltip("Falloff curve shape (< 1 sharpens toward the border).")]
            [Range(0.25f, 4f)] public float falloffPower = 1f;
            public Color edgeTint = Color.white;
            [Tooltip("UV roll: texture compresses at the lip like a surface curving away (world units).")]
            [Range(0f, 1f)] public float rollAmount = 0.15f;
            [Range(0.5f, 4f)] public float rollPower = 1.5f;

            [Tooltip("World-tiled detail layer over the fill.")]
            public Texture detailTexture;
            public float detailScale = 3f;
            [Range(0f, 1f)] public float detailStrength = 1f;
            [Tooltip("Iso foreshortening squash (Y/X) for world-tiled layers.")]
            [Range(0.25f, 1f)] public float detailAspect = 0.5f;
            [Tooltip("Second rotated detail sample weighted by low-freq noise — hides the texture repeat grid.")]
            [Range(0f, 1f)] public float detailAntiTile = 0.6f;

            [Tooltip("Procedural macro tint variation (two-octave value noise) — sells the natural grass plane.")]
            [Range(0f, 1f)] public float varStrength = 0.35f;
            public float varScale = 7f;
            public Color varTintA = new Color(0.87f, 0.93f, 0.78f, 1f);
            public Color varTintB = Color.white;

            [Tooltip("Iso tile grid overlay. Alpha = opacity, 0 = off.")]
            public Color gridColor = new Color(0f, 0f, 0f, 0f);
            [Tooltip("Grid line thickness (world units).")]
            [Range(0.005f, 0.3f)] public float gridThickness = 0.04f;
            [Range(0f, 2f)] public float gridSoftness = 0.5f;
            [Tooltip("Iso cell size in world units (match the Grid component).")]
            public Vector2 gridCell = new Vector2(1f, 0.5f);
            [Tooltip("Grid alignment offset in half cells.")]
            public Vector2 gridOffset = Vector2.zero;
            [Tooltip("Dissolve grid lines toward the border (world units; capped by Falloff Width).")]
            [Range(0f, 2f)] public float gridEdgeFade = 0.4f;

            [Tooltip("Ghibli-style posterized 2D light with brushy band borders.")]
            public bool bandedLight = true;
            [Range(1f, 6f)] public float lightBands = 3f;
            [Range(0.005f, 0.5f)] public float bandSoftness = 0.08f;
            [Range(0f, 1f)] public float bandWobble = 0.25f;
            public float bandWobbleScale = 2f;
            public Color shadowTint = new Color(0.55f, 0.58f, 0.78f, 1f);

            [Header("Paint Layers")]
            [Tooltip("Enable the 4 brush-edged noise colour layers stacked over the base grass.")]
            public bool paintLayersEnabled = false;
            [Tooltip("Brush stamp sheet 0 (white stroke on transparent).")]
            public Texture brushTex1;
            [Tooltip("Brush stamp sheet 1 (white stroke on transparent).")]
            public Texture brushTex2;
            [Tooltip("Four colour layers, composited bottom (0) → top (3) over the base.")]
            public PaintLayerSettings[] paintLayers = DefaultPaintLayers();

            private static PaintLayerSettings[] DefaultPaintLayers() => new[]
            {
                new PaintLayerSettings { color = new Color(0.55f, 0.68f, 0.32f, 0.5f),  noiseScale = 7f,   noiseThreshold = 0.45f, noiseOffset = 0f,  brushSheet = 0, brushScale = 2.5f, edgeBreak = 0.45f },
                new PaintLayerSettings { color = new Color(0.42f, 0.58f, 0.26f, 0.5f),  noiseScale = 5f,   noiseThreshold = 0.55f, noiseOffset = 13f, brushSheet = 1, brushScale = 3.5f, edgeBreak = 0.5f  },
                new PaintLayerSettings { color = new Color(0.82f, 0.87f, 0.47f, 0.35f), noiseScale = 3.5f, noiseThreshold = 0.62f, noiseOffset = 29f, brushSheet = 0, brushScale = 2f,   edgeBreak = 0.4f  },
                new PaintLayerSettings { color = new Color(0.32f, 0.40f, 0.52f, 0.22f), noiseScale = 6f,   noiseThreshold = 0.6f,  noiseOffset = 41f, brushSheet = 1, brushScale = 4f,   edgeBreak = 0.5f  },
            };
        }

        [Serializable]
        public class SkirtSurfaceSettings
        {
            public Color topColor = new Color(0.35f, 0.55f, 0.20f, 1f);
            public Color midColor = new Color(0.45f, 0.35f, 0.22f, 1f);
            public Color bottomColor = new Color(0.25f, 0.22f, 0.18f, 1f);
            [Tooltip("Depth of the grass band at the cliff top (fraction of wall).")]
            [Range(0.02f, 0.9f)] public float grassDepth = 0.15f;
            [Range(0.01f, 0.5f)] public float blendSoftness = 0.1f;
            [Tooltip("Rock detail texture, wrapped in the cliff's (arc, depth) surface space.")]
            public Texture detailTexture;
            public float detailScale = 3f;
            [Range(0f, 1f)] public float detailStrength = 1f;
            [Range(0f, 12f)] public float strataCount = 5f;
            [Range(0f, 0.5f)] public float strataDarken = 0.1f;
            [Range(0f, 2f)] public float strataWarp = 0.5f;
            [Tooltip("Contact shadow just under the island lip.")]
            [Range(0f, 1f)] public float topShadow = 0.35f;
            [Range(0.01f, 0.5f)] public float topShadowDepth = 0.15f;
            [Tooltip("Dissolve toward the bottom (floating in the sky) starts at this fraction of the wall.")]
            [Range(0f, 1f)] public float fadeStart = 0.75f;
        }

        [Serializable]
        public class FringeSettings
        {
            [Tooltip("Blade strip hugging the island lip — roots over the top fill, tips overhang the skirt.")]
            public bool enabled = true;
            [Tooltip("Blade strip texture: white-with-alpha mask, or real coloured grass art (tick Art Coloured).")]
            public Texture2D stripTexture;
            [Tooltip("Strip art is real coloured grass: blades keep the art's colours, alpha alone carves the shape.")]
            public bool artColored = false;
            [Tooltip("How far up the blade (0 root → 1 tip) the island surface colour cross-fades into the art colour.")]
            [Range(0.05f, 1f)] public float artBlend = 0.45f;
            [Tooltip("Recolor coloured art toward the profile's surface colour (art luminance keeps the blade shading). 0 = art's own colours, 1 = fully follows the profile — set high on recoloured variants so blades match the top.")]
            [Range(0f, 1f)] public float artRecolor = 0f;
            [Tooltip("World units per strip-texture repeat along the border (snapped to whole repeats).")]
            [Range(0.25f, 8f)] public float texScale = 0.5f;
            [Tooltip("How far the strip roots inward over the top fill (world units).")]
            [Range(0f, 0.5f)] public float innerOverlap = 0.1f;
            [Tooltip("How far blade tips reach outward past the silhouette (world units).")]
            [Range(0f, 1f)] public float reach = 0.3f;
            [Tooltip("Downward gravity bend of the blade tips (world units).")]
            [Range(0f, 1f)] public float droop = 0.25f;
            [Tooltip("How much upward blade reach on up-facing (far) edges is folded flat. 0 = floats past the silhouette, 1 = fully flattened onto the rim.")]
            [Range(0f, 1f)] public float upFold = 0.6f;
            [Tooltip("Cross-section subdivisions — more rows = smoother droop curve.")]
            [Range(1, 6)] public int rows = 3;
            [Tooltip("Arc window for smoothing offset directions (kills corner folds).")]
            [Range(0.1f, 3f)] public float smoothing = 0.75f;
            [Tooltip("Strip texture V at the rooted inner edge (0.5 = the art's solid middle).")]
            [Range(0f, 1f)] public float vInner = 0.5f;
            [Tooltip("Strip texture V at the blade tips (1/0 = the art's blade edges).")]
            [Range(0f, 1f)] public float vOuter = 1f;
            [Tooltip("Extra tint over the surface colour. White = match the top exactly.")]
            public Color tint = Color.white;
            [Tooltip("Darken toward the blade tips — separates the overhang from the top surface.")]
            [Range(0f, 0.6f)] public float tipDarken = 0.15f;
            [Tooltip("Sorting order offset relative to the source renderer (must be above the fill).")]
            public int orderOffset = 1;
        }

        [Serializable]
        public class RenderingSettings
        {
            [Tooltip("If empty, the skirt/fringe copy the source renderer's sorting layer.")]
            public string sortingLayer = "";
            [Tooltip("Skirt sorting order offset relative to the source renderer (negative = behind).")]
            public int sortingOrderOffset = -1;
        }

        [Serializable]
        public class SkirtSpriteSettings
        {
            [Tooltip("Rock sprite to tile along the cliff. Must have alpha for the cutout silhouette. Used when Rock Variants is empty.")]
            public Sprite rockSprite;
            [Tooltip("Rock variants (up to 8) randomly picked per tile to break up repetition. Must all share ONE texture/sheet. Empty = just Rock Sprite.")]
            public Sprite[] rockVariants;
            [Tooltip("Tiles per world unit along the contour.")]
            public float tileCountH = 2.0f;

            [Header("Platform Rim (Flynn/PlatformSkirt only)")]
            [Tooltip("Rock tile width as a multiple of the rim's world height. Sizes tiles off the " +
                     "rim instead of a fixed pitch, so a 0.25 and a 0.75 terrace wear the same " +
                     "shaped rock. Ignored by Flynn/IslandSkirtSprite.")]
            public float platformTileAspect = 1.8f;
            [Tooltip("Solid cliff colour behind the rock silhouette — what makes a platform rim " +
                     "gap-proof. Use a MID rock tone, not the darkest: this is what shows wherever " +
                     "the rock alpha thins, and too dark reads as a black band.")]
            public Color baseFillColor = new Color(0.42f, 0.38f, 0.33f, 1f);
            [Tooltip("How much of the rock sprite's BOTTOM to crop off the rim. A platform rim " +
                     "meets the ground, so it must not show the eroded bottom silhouette drawn " +
                     "for an island hanging in sky.")]
            [Range(0f, 0.6f)] public float platformBottomCrop = 0.12f;
            [Tooltip("Neighbour tiles overlap by this fraction of a tile — wider = more layered rock.")]
            [Range(0f, 0.8f)] public float overlap = 0.3f;
            [Tooltip("How dark the top tile's cast shadow falls on the tile behind it.")]
            [Range(0f, 1f)] public float overlapShade = 0.3f;
            [Tooltip("Mirror alternate tiles to hide repetition. 0 = off, 1 = full mirror.")]
            [Range(0f, 1f)] public float mirrorTiles = 1.0f;
            [Tooltip("Per-tile vertical jitter — breaks the bottom silhouette.")]
            [Range(0f, 0.3f)] public float bottomJitter = 0.12f;
            [Tooltip("Per-tile horizontal jitter — breaks the side silhouette.")]
            [Range(0f, 0.2f)] public float sideJitter = 0.08f;
            [Tooltip("Smoothstep width of the depth-order flip between overlapping tiles.")]
            [Range(0.01f, 0.5f)] public float layerBlendSoft = 0.15f;
            [Tooltip("Contact-shadow reach: how far into the over-tile body the seam shadow bites.")]
            [Range(0f, 0.5f)] public float shadowDist = 0.12f;
            [Tooltip("Alpha cutoff threshold for the cutout silhouette.")]
            [Range(0.01f, 0.5f)] public float alphaCutoff = 0.1f;
            [Tooltip("Depth of the grass colour band at the cliff top.")]
            [Range(0.02f, 0.5f)] public float grassDepth = 0.1f;
            public Color grassColor = new Color(0.35f, 0.55f, 0.20f, 1f);
            [Range(0.01f, 0.2f)] public float grassBlendSoft = 0.05f;

            [Header("Back Layer")]
            [Tooltip("Fill the gaps behind the front tiles with a second, darker rock layer.")]
            public bool backLayerEnable = true;
            [Range(-1f, 1f)] public float backOffsetH = 0.5f;
            [Range(-1f, 1f)] public float backOffsetV = 0.15f;
            [Tooltip("How much darker the back layer reads (fake depth).")]
            [Range(0f, 1f)] public float backShade = 0.45f;
            [Range(0.5f, 2f)] public float backScale = 1.1f;

            [Header("Shingle Planes")]
            [Tooltip("Use overlapping full-sprite quads (shingles) instead of the tiling shader. The sprite silhouette makes the cliff; the plane in front casts a gradient shadow on the one behind — no UV cutoff.")]
            public bool useShinglePlanes = false;
            [Tooltip("World width of each shingle plane along the contour.")]
            public float shingleWidth = 1.0f;
            [Tooltip("World height each shingle hangs down.")]
            public float shingleHeight = 1.0f;
            [Tooltip("Under-edge shadow strength on each shingle (covered by the plane in front).")]
            [Range(0f, 1f)] public float shingleShadowStrength = 0.6f;
            [Tooltip("u where the shadow begins (0 = exposed edge).")]
            [Range(0f, 1f)] public float shingleShadowStart = 0.0f;
            [Tooltip("How far the shadow ramps to full — set near (1 - overlap) so full-dark lands at the overlap seam.")]
            [Range(0.02f, 1f)] public float shingleShadowSoft = 0.5f;

            [Tooltip("Global tint over the sprite colours.")]
            public Color tint = Color.white;
        }

        [Serializable]
        public class DecalSettings
        {
            [Tooltip("Scatter decal cards (vines/bushes) along the skirt to break its silhouette.")]
            public bool enabled = false;
            [Tooltip("Decal variants — must share ONE sheet, randomly picked per card. Prototype: reuse the rock sheet.")]
            public Sprite[] decalSprites;
            [Tooltip("Cards per world unit of down-facing contour.")]
            [Range(0f, 8f)] public float density = 2f;
            public float sizeMin = 0.25f;
            public float sizeMax = 0.6f;
            [Tooltip("Vertical spread of card anchors down the skirt band (world).")]
            public float bandHeight = 1.0f;
            [Tooltip("How far cards poke outward past the contour edge — breaks the side silhouette (world).")]
            public float pokeOut = 0.15f;
            [Tooltip("Deterministic scatter seed.")]
            public int seed = 0;
            [Tooltip("Draw order relative to the skirt (positive = in front).")]
            public int orderOffset = 1;
        }

        // -------------------------------------------------------------------
        // Fields
        // -------------------------------------------------------------------

        public ContourSettings contour = new ContourSettings();
        public SkirtShapeSettings skirtShape = new SkirtShapeSettings();
        public SkirtShadingSettings skirtShading = new SkirtShadingSettings();
        public ErosionSettings erosion = new ErosionSettings();
        public FillSettings fill = new FillSettings();
        public SkirtSurfaceSettings skirtSurface = new SkirtSurfaceSettings();
        public SkirtSpriteSettings skirtSprite = new SkirtSpriteSettings();
        public FringeSettings fringe = new FringeSettings();
        public DecalSettings decals = new DecalSettings();
        public RenderingSettings rendering = new RenderingSettings();

        [Header("Shaders (referenced so they ship in builds)")]
        public Shader fillShader;
        public Shader skirtShader;
        public Shader fringeShader;

        /// <summary>Bumped on inspector edits — consumers regen when it moves.</summary>
        [NonSerialized] public int Version;

        private void OnValidate() => Version++;

        // -------------------------------------------------------------------
        // Material push (property names live here and nowhere else)
        // -------------------------------------------------------------------

        private static readonly int EdgeDarkenId = Shader.PropertyToID("_EdgeDarken");
        private static readonly int FalloffPowerId = Shader.PropertyToID("_FalloffPower");
        private static readonly int EdgeTintId = Shader.PropertyToID("_EdgeTint");
        private static readonly int RollAmountId = Shader.PropertyToID("_RollAmount");
        private static readonly int RollPowerId = Shader.PropertyToID("_RollPower");
        private static readonly int DetailTexId = Shader.PropertyToID("_DetailTex");
        private static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
        private static readonly int DetailStrengthId = Shader.PropertyToID("_DetailStrength");
        private static readonly int DetailAspectId = Shader.PropertyToID("_DetailAspect");
        private static readonly int DetailAntiTileId = Shader.PropertyToID("_DetailAntiTile");
        private static readonly int VarStrengthId = Shader.PropertyToID("_VarStrength");
        private static readonly int VarScaleId = Shader.PropertyToID("_VarScale");
        private static readonly int VarTintAId = Shader.PropertyToID("_VarTintA");
        private static readonly int VarTintBId = Shader.PropertyToID("_VarTintB");
        private static readonly int GridColorId = Shader.PropertyToID("_GridColor");
        private static readonly int GridThicknessId = Shader.PropertyToID("_GridThickness");
        private static readonly int GridSoftnessId = Shader.PropertyToID("_GridSoftness");
        private static readonly int GridCellId = Shader.PropertyToID("_GridCell");
        private static readonly int GridOffsetId = Shader.PropertyToID("_GridOffset");
        private static readonly int GridEdgeFadeId = Shader.PropertyToID("_GridEdgeFade");
        private static readonly int PaintLayersId = Shader.PropertyToID("_PaintLayers");
        private static readonly int BrushTex1Id = Shader.PropertyToID("_BrushTex1");
        private static readonly int BrushTex2Id = Shader.PropertyToID("_BrushTex2");
        private static readonly int LayerColorId = Shader.PropertyToID("_LayerColor");
        private static readonly int LayerNoiseId = Shader.PropertyToID("_LayerNoise");
        private static readonly int LayerBrushId = Shader.PropertyToID("_LayerBrush");
        private static readonly int LayerRangeId = Shader.PropertyToID("_LayerRange");
        private static readonly Vector4[] _layerCol = new Vector4[4];
        private static readonly Vector4[] _layerNoise = new Vector4[4];
        private static readonly Vector4[] _layerBrush = new Vector4[4];
        private static readonly Vector4[] _layerRange = new Vector4[4];
        private static readonly int BandedLightId = Shader.PropertyToID("_BandedLight");
        private static readonly int LightBandsId = Shader.PropertyToID("_LightBands");
        private static readonly int BandSoftnessId = Shader.PropertyToID("_BandSoftness");
        private static readonly int BandWobbleId = Shader.PropertyToID("_BandWobble");
        private static readonly int BandWobbleScaleId = Shader.PropertyToID("_BandWobbleScale");
        private static readonly int ShadowTintId = Shader.PropertyToID("_ShadowTint");
        private static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        private static readonly int MidColorId = Shader.PropertyToID("_MidColor");
        private static readonly int BottomColorId = Shader.PropertyToID("_BottomColor");
        private static readonly int GrassDepthId = Shader.PropertyToID("_GrassDepth");
        private static readonly int BlendSoftnessId = Shader.PropertyToID("_BlendSoftness");
        private static readonly int StrataCountId = Shader.PropertyToID("_StrataCount");
        private static readonly int StrataDarkenId = Shader.PropertyToID("_StrataDarken");
        private static readonly int StrataWarpId = Shader.PropertyToID("_StrataWarp");
        private static readonly int TopShadowId = Shader.PropertyToID("_TopShadow");
        private static readonly int TopShadowDepthId = Shader.PropertyToID("_TopShadowDepth");
        private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ArtColoredId = Shader.PropertyToID("_ArtColored");
        private static readonly int ArtBlendReachId = Shader.PropertyToID("_ArtBlendReach");
        private static readonly int ArtRecolorId = Shader.PropertyToID("_ArtRecolor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Skirt sprite shader property IDs
        private static readonly int TileCountHId = Shader.PropertyToID("_TileCountH");
        private static readonly int TileAspectId = Shader.PropertyToID("_TileAspect");
        private static readonly int BaseFillId = Shader.PropertyToID("_BaseFill");
        private static readonly int BottomCropId = Shader.PropertyToID("_BottomCrop");
        private static readonly int OverlapId = Shader.PropertyToID("_Overlap");
        private static readonly int OverlapShadeId = Shader.PropertyToID("_OverlapShade");
        private static readonly int MirrorTilesId = Shader.PropertyToID("_MirrorTiles");
        private static readonly int BottomJitterId = Shader.PropertyToID("_BottomJitter");
        private static readonly int SideJitterId = Shader.PropertyToID("_SideJitter");
        private static readonly int LayerBlendSoftId = Shader.PropertyToID("_LayerBlendSoft");
        private static readonly int ShadowDistId = Shader.PropertyToID("_ShadowDist");
        private static readonly int AlphaCutoffId = Shader.PropertyToID("_AlphaCutoff");
        private static readonly int BackLayerEnableId = Shader.PropertyToID("_BackLayerEnable");
        private static readonly int BackOffsetHId = Shader.PropertyToID("_BackOffsetH");
        private static readonly int BackOffsetVId = Shader.PropertyToID("_BackOffsetV");
        private static readonly int BackShadeId = Shader.PropertyToID("_BackShade");
        private static readonly int BackScaleId = Shader.PropertyToID("_BackScale");
        private static readonly int ShadowStrengthId = Shader.PropertyToID("_ShadowStrength");
        private static readonly int ShadowStartId = Shader.PropertyToID("_ShadowStart");
        private static readonly int ShadowSoftId = Shader.PropertyToID("_ShadowSoft");
        private static readonly int GrassColorId = Shader.PropertyToID("_GrassColor");
        private static readonly int GrassBlendSoftId = Shader.PropertyToID("_GrassBlendSoft");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int MainTexSTId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int RockRectsId = Shader.PropertyToID("_RockRects");
        private static readonly int RockCountId = Shader.PropertyToID("_RockCount");
        private static readonly Vector4[] _rockRects = new Vector4[8];

        public void ApplyToFillMaterial(Material m)
        {
            m.SetColor(ColorId, fill.baseTint);
            m.SetFloat(EdgeDarkenId, fill.falloffEnabled ? fill.edgeDarken : 0f);
            m.SetFloat(FalloffPowerId, fill.falloffPower);
            m.SetColor(EdgeTintId, fill.edgeTint);
            m.SetFloat(RollAmountId, fill.falloffEnabled ? fill.rollAmount : 0f);
            m.SetFloat(RollPowerId, fill.rollPower);
            m.SetTexture(DetailTexId, fill.detailTexture);
            m.SetFloat(DetailScaleId, fill.detailScale);
            m.SetFloat(DetailStrengthId, fill.detailStrength);
            m.SetFloat(DetailAspectId, fill.detailAspect);
            m.SetFloat(DetailAntiTileId, fill.detailAntiTile);
            m.SetFloat(VarStrengthId, fill.varStrength);
            m.SetFloat(VarScaleId, fill.varScale);
            m.SetColor(VarTintAId, fill.varTintA);
            m.SetColor(VarTintBId, fill.varTintB);
            m.SetColor(GridColorId, fill.gridColor);
            m.SetFloat(GridThicknessId, fill.gridThickness);
            m.SetFloat(GridSoftnessId, fill.gridSoftness);
            m.SetVector(GridCellId, new Vector4(fill.gridCell.x, fill.gridCell.y, 0f, 0f));
            m.SetVector(GridOffsetId, new Vector4(fill.gridOffset.x, fill.gridOffset.y, 0f, 0f));
            m.SetFloat(GridEdgeFadeId, fill.gridEdgeFade);
            m.SetFloat(PaintLayersId, fill.paintLayersEnabled ? 1f : 0f);
            if (fill.brushTex1 != null) m.SetTexture(BrushTex1Id, fill.brushTex1);
            if (fill.brushTex2 != null) m.SetTexture(BrushTex2Id, fill.brushTex2);
            ApplyPaintLayers(m);
            ApplyLightBanding(m);
        }

        // Pack the 4 paint layers into the shader's float4 arrays.
        private void ApplyPaintLayers(Material m)
        {
            var layers = fill.paintLayers;
            for (int i = 0; i < 4; i++)
            {
                var L = (layers != null && i < layers.Length) ? layers[i] : null;
                if (L == null)
                {
                    _layerCol[i] = Vector4.zero;
                    _layerNoise[i] = new Vector4(6f, 0.5f, 0.08f, 0f);
                    _layerBrush[i] = new Vector4(0f, 2.5f, 0.45f, 0f);
                    _layerRange[i] = new Vector4(0f, 15f, 4f, 4f);
                    continue;
                }
                _layerCol[i] = (Vector4)L.color;
                _layerNoise[i] = new Vector4(L.noiseScale, L.noiseThreshold, L.edgeSoftness, L.noiseOffset);
                _layerBrush[i] = new Vector4(L.brushSheet, L.brushScale, L.edgeBreak, (float)L.blendMode);
                _layerRange[i] = new Vector4(L.brushRange.x, L.brushRange.y, L.brushGrid.x, L.brushGrid.y);
            }
            m.SetVectorArray(LayerColorId, _layerCol);
            m.SetVectorArray(LayerNoiseId, _layerNoise);
            m.SetVectorArray(LayerBrushId, _layerBrush);
            m.SetVectorArray(LayerRangeId, _layerRange);
        }

        public void ApplyToSkirtMaterial(Material m)
        {
            // _TopColor is pushed per-renderer (× island tint) by IslandSkirt.
            m.SetColor(TopColorId, skirtSurface.topColor);
            m.SetColor(MidColorId, skirtSurface.midColor);
            m.SetColor(BottomColorId, skirtSurface.bottomColor);
            m.SetFloat(GrassDepthId, skirtSurface.grassDepth);
            m.SetFloat(BlendSoftnessId, skirtSurface.blendSoftness);
            m.SetTexture(DetailTexId, skirtSurface.detailTexture);
            m.SetFloat(DetailScaleId, skirtSurface.detailScale);
            m.SetFloat(DetailStrengthId, skirtSurface.detailStrength);
            m.SetFloat(StrataCountId, skirtSurface.strataCount);
            m.SetFloat(StrataDarkenId, skirtSurface.strataDarken);
            m.SetFloat(StrataWarpId, skirtSurface.strataWarp);
            m.SetFloat(TopShadowId, skirtSurface.topShadow);
            m.SetFloat(TopShadowDepthId, skirtSurface.topShadowDepth);
            m.SetFloat(FadeStartId, skirtSurface.fadeStart);
        }

        public void ApplyToShingleMaterial(Material m)
        {
            var s = skirtSprite;
            if (s.rockSprite != null && s.rockSprite.texture != null)
            {
                var tex = s.rockSprite.texture;
                var rect = s.rockSprite.textureRect;
                m.SetTexture(MainTexId, tex);
                m.SetVector(MainTexSTId, new Vector4(
                    rect.width / tex.width, rect.height / tex.height,
                    rect.x / tex.width, rect.y / tex.height));
            }
            m.SetColor(TintId, s.tint);
            m.SetFloat(AlphaCutoffId, s.alphaCutoff);
            m.SetFloat(ShadowStrengthId, s.shingleShadowStrength);
            m.SetFloat(ShadowStartId, s.shingleShadowStart);
            m.SetFloat(ShadowSoftId, s.shingleShadowSoft);
        }

        public void ApplyToSkirtSpriteMaterial(Material m)
        {
            var s = skirtSprite;

            // Gather up to 8 rock variants sharing one texture (per-tile random
            // pick in the shader). Falls back to the single rockSprite.
            for (int i = 0; i < _rockRects.Length; i++) _rockRects[i] = new Vector4(0f, 0f, 1f, 1f);
            Texture atlas = null;
            int count = 0;
            if (s.rockVariants != null)
            {
                foreach (var sp in s.rockVariants)
                {
                    if (sp == null || sp.texture == null) continue;
                    if (atlas == null) atlas = sp.texture;
                    if (sp.texture != atlas || count >= _rockRects.Length) continue;
                    var r = sp.textureRect;
                    _rockRects[count++] = new Vector4(
                        r.x / atlas.width, r.y / atlas.height,
                        r.width / atlas.width, r.height / atlas.height);
                }
            }
            if (count == 0 && s.rockSprite != null && s.rockSprite.texture != null)
            {
                atlas = s.rockSprite.texture;
                var r = s.rockSprite.textureRect;
                _rockRects[0] = new Vector4(
                    r.x / atlas.width, r.y / atlas.height,
                    r.width / atlas.width, r.height / atlas.height);
                count = 1;
            }
            if (atlas != null) m.SetTexture(MainTexId, atlas);
            m.SetVectorArray(RockRectsId, _rockRects);
            m.SetFloat(RockCountId, Mathf.Max(count, 1));

            m.SetFloat(TileCountHId, s.tileCountH);
            m.SetFloat(TileAspectId, s.platformTileAspect);
            m.SetColor(BaseFillId, s.baseFillColor);
            m.SetFloat(BottomCropId, s.platformBottomCrop);
            m.SetFloat(OverlapId, s.overlap);
            m.SetFloat(OverlapShadeId, s.overlapShade);
            m.SetFloat(MirrorTilesId, s.mirrorTiles);
            m.SetFloat(BottomJitterId, s.bottomJitter);
            m.SetFloat(SideJitterId, s.sideJitter);
            m.SetFloat(LayerBlendSoftId, s.layerBlendSoft);
            m.SetFloat(ShadowDistId, s.shadowDist);
            m.SetFloat(AlphaCutoffId, s.alphaCutoff);
            m.SetFloat(GrassDepthId, s.grassDepth);
            m.SetColor(GrassColorId, s.grassColor);
            m.SetFloat(GrassBlendSoftId, s.grassBlendSoft);
            m.SetFloat(BackLayerEnableId, s.backLayerEnable ? 1f : 0f);
            m.SetFloat(BackOffsetHId, s.backOffsetH);
            m.SetFloat(BackOffsetVId, s.backOffsetV);
            m.SetFloat(BackShadeId, s.backShade);
            m.SetFloat(BackScaleId, s.backScale);
            m.SetColor(TintId, s.tint);
        }

        public void ApplyToFringeMaterial(Material m)
        {
            // _FillTex/_FillScale/_Color and the falloff bake are live
            // per-island state, pushed by IslandSkirt.
            m.SetTexture(MainTexId, fringe.stripTexture);
            m.SetFloat(ArtColoredId, fringe.artColored ? 1f : 0f);
            m.SetFloat(ArtBlendReachId, fringe.artBlend);
            m.SetFloat(ArtRecolorId, fringe.artRecolor);
            m.SetFloat(EdgeDarkenId, fill.falloffEnabled ? fill.edgeDarken : 0f);
            m.SetFloat(FalloffPowerId, fill.falloffPower);
            m.SetColor(EdgeTintId, fill.edgeTint);
            m.SetTexture(DetailTexId, fill.detailTexture);
            m.SetFloat(DetailScaleId, fill.detailScale);
            m.SetFloat(DetailStrengthId, fill.detailStrength);
            m.SetFloat(DetailAspectId, fill.detailAspect);
            m.SetFloat(DetailAntiTileId, fill.detailAntiTile);
            m.SetFloat(VarStrengthId, fill.varStrength);
            m.SetFloat(VarScaleId, fill.varScale);
            m.SetColor(VarTintAId, fill.varTintA);
            m.SetColor(VarTintBId, fill.varTintB);
            ApplyLightBanding(m);
        }

        private void ApplyLightBanding(Material m)
        {
            m.SetFloat(BandedLightId, fill.bandedLight ? 1f : 0f);
            m.SetFloat(LightBandsId, fill.lightBands);
            m.SetFloat(BandSoftnessId, fill.bandSoftness);
            m.SetFloat(BandWobbleId, fill.bandWobble);
            m.SetFloat(BandWobbleScaleId, fill.bandWobbleScale);
            m.SetColor(ShadowTintId, fill.shadowTint);
        }
    }
}
