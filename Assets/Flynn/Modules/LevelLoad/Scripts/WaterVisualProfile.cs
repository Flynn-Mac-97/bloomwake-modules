using UnityEngine;

namespace Flynn.Environment
{
    /// <summary>
    /// Look knobs for a water SpriteShape surface — deliberately FLAT 2D (Stardew /
    /// pixel-art-water formula): one body color, a crisp shore line, thin ripple
    /// rings marching toward the shore, sparse sparkle dashes, and the iso back-bank
    /// wall. WaterSurface pushes these into a runtime material every update, so
    /// tuning the asset restyles live.
    /// </summary>
    [CreateAssetMenu(fileName = "WaterVisualProfile", menuName = "Flynn/LevelLoad/Water Visual Profile")]
    public class WaterVisualProfile : ScriptableObject
    {
        [Header("Universal scale")]
        [Tooltip("Player/tile reference size in world units — 1 painter tile ~ player " +
                 "width. EVERY world-unit knob below multiplies by this, so the whole " +
                 "water reads at player scale and rescales with one slider.")]
        public float unitScale = 1f;

        [Header("Contour")]
        [Tooltip("Boundary trace settings WaterSurface pushes into the stack's " +
                 "TilemapToSpriteShape — SO-owned so every spawned water surface is " +
                 "consistent and nothing resets on play. Defaults = the tuned " +
                 "WaterStack bake.")]
        public IslandVisualProfile.ContourSettings contour = new IslandVisualProfile.ContourSettings
        {
            boundaryMode = BoundaryMode.TileCorners,
            tangentMode = UnityEngine.U2D.ShapeTangentMode.Continuous,
            inset = 1.61f,
            tangentLength = 0.11f,
        };

        [Header("Body — pixel texture")]
        [Tooltip("Seamless pixel-art water tile (point-filtered, repeat wrap). " +
                 "The texture IS the water's character; the shader only layers on top.")]
        public Texture2D waterTexture;
        [Tooltip("World units one texture tile covers.")]
        public float textureScale = 1.0f;
        [Tooltip("Tile drift, world units/second. Keep tiny — calm water.")]
        public Vector2 scrollSpeed = new Vector2(0.010f, 0.004f);
        [Range(0f, 1f), Tooltip("Subtle top animation: a second offset copy of the tile " +
                 "drifts against the first. 0 = static texture.")]
        public float layerAnim = 0.5f;
        [Tooltip("Tint multiplier over the texture. White = the texture's own colors.")]
        public Color bodyColor = Color.white;
        [Range(0f, 1f), Tooltip("Overall opacity — land shows through below 1.")]
        public float alpha = 0.95f;
        [Tooltip("World width over which the water fades onto the land. 0 = hard edge.")]
        public float edgeBlend = 0.06f;

        [Header("Shore line + ripple rings")]
        [Tooltip("Color of the shore line and the ripple rings (usually near-white).")]
        public Color shoreColor = new Color(0.88f, 0.97f, 0.95f, 1f);
        [Tooltip("World width of the solid line at the waterline. 0 = off.")]
        public float shoreWidth = 0.06f;
        [Tooltip("World distance between ripple rings marching toward the shore.")]
        public float ringSpacing = 0.35f;
        [Tooltip("World thickness of each ring line.")]
        public float ringWidth = 0.08f;
        [Tooltip("Ring march speed (cycles/second). 0 = static rings.")]
        public float ringSpeed = 0.15f;
        [Range(0f, 1f), Tooltip("Ring opacity. 0 = no rings.")]
        public float ringStrength = 0.55f;
        [Tooltip("World-space wobble of the rings — hand-drawn feel, not concentric circles.")]
        public float ringWobble = 0.08f;

        [Header("Underwater ground (floor + wall)")]
        [Tooltip("Seamless tile for the ground under the water — rock, sand, mud. " +
                 "Shows through near the shore and on the back-bank wall.")]
        public Texture2D underwaterTexture;
        [Tooltip("World units one underwater tile covers.")]
        public float underwaterScale = 1f;
        [Tooltip("Tint over the underwater texture (usually darkened / cooled).")]
        public Color underwaterTint = new Color(0.62f, 0.66f, 0.72f, 1f);
        [Range(0f, 1f), Tooltip("How clearly the floor shows through the water — uniform " +
                 "across the whole body. 0 = pure water color, 1 = full rock.")]
        public float floorVisibility = 0.35f;

        [Header("Back bank wall (iso depression)")]
        [Tooltip("Wall tint multiplied over the underwater texture — darker = deeper shadow. " +
                 "The tilted orthographic camera shows ONLY the far (top) bank's wall.")]
        public Color bankColor = new Color(0.55f, 0.5f, 0.48f, 1f);
        [Tooltip("Wall height in world units — THE depth read: slide taller = deeper water. " +
                 "Also washes out the floor visibility as it grows.")]
        public float bankHeight = 0.22f;
        [Tooltip("Vertical gap below the shoreline before the wall band starts.")]
        public float bankOffsetY = 0f;
        [Tooltip("Cut this many world units off the band's left/right ends (its width " +
                 "dial — pulls the shadow away from the side corners).")]
        public float bankSideInset = 0f;
        [Range(0f, 1f), Tooltip("Wall opacity over the water body.")]
        public float bankStrength = 0.9f;

        [Header("Rim (grass trim edge sprites)")]
        [Range(0.1f, 4f), Tooltip("Trim band thickness multiplier — native SpriteShape " +
                 "spline height, scales the edge sprite without touching its PPU. " +
                 "Also multiplied by unitScale (clamped to SpriteShape's 0.1-4 range).")]
        public float rimScale = 1f;
        [Tooltip("Tint over the trim sprite.")]
        public Color rimTint = Color.white;
        [Range(0f, 1f), Tooltip("Alpha feather on the LAND side, as a fraction of the " +
                 "trim band height — melts the rim into the surrounding ground. 0 = off.")]
        public float rimOuterFade = 0.2f;
        [Range(0f, 1f), Tooltip("Darkening band on the WATERLINE side (wet grass). 0 = off.")]
        public float rimWetBand = 0.3f;
        [Tooltip("Wet multiply color (rgb) and strength (a).")]
        public Color rimWetColor = new Color(0.55f, 0.75f, 0.8f, 0.7f);
        [Tooltip("Breeze sway of the waterline side, world units (× unitScale). 0 = still.")]
        public float rimSway = 0.02f;
        [Tooltip("Sway speed, cycles/second-ish.")]
        public float rimSwaySpeed = 1.2f;

        /// <summary>Distance the edge bake must cover for every distance-driven layer (world units, unitScale applied).</summary>
        public float RequiredFalloffWidth =>
            Mathf.Max(0.05f, Mathf.Max(Mathf.Max(bankHeight, ringSpacing * 3.5f),
                Mathf.Max(shoreWidth, edgeBlend))) * Mathf.Max(0.01f, unitScale);
    }
}
