using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// StyleLab workbench: one LARGE floating-isle base (the base aesthetic, usually
    /// grass) with every other <see cref="TileAestheticSO"/> painted as a square
    /// overlay ON it — because the real check is how each type matches and blends
    /// against the isle it will live on. The base tilemap stays solid under the
    /// overlays (same rule as the map loader's footprint); overlay stacks sort above
    /// the base. Right-click the component header -> "Build Swatches" /
    /// "Clear Swatches"; also builds on Play if empty. All swatch objects are
    /// DontSave — rebuilt on demand.
    /// </summary>
    public class SpriteShapeStyleLab : MonoBehaviour
    {
        [Tooltip("The isle everything is judged against — usually the grass floating isle.")]
        public TileAestheticSO baseAesthetic;
        [Tooltip("Overlay aesthetics, one square each, laid out ON the base isle.")]
        public List<TileAestheticSO> aesthetics = new List<TileAestheticSO>();
        [Tooltip("Tile painted into swatch cells — same ground tile the loader uses.")]
        public TileBase tile;
        [Tooltip("Scene Grid the swatch tilemaps go under (cellSize (1, 0.5, 0) like the lab).")]
        public Grid grid;

        [Header("Base isle")]
        [Range(4, 32), Tooltip("Base isle width in painter tiles.")]
        public int baseTilesX = 14;
        [Range(4, 32), Tooltip("Base isle height in painter tiles.")]
        public int baseTilesY = 9;

        [Header("Overlay layout")]
        [Range(1, 8), Tooltip("Overlay square side length in painter tiles.")]
        public int tilesPerSwatch = 2;
        [Range(1, 4), Tooltip("Cells per painter tile — keep matched to JsonMapLoader.")]
        public int cellsPerTile = 2;
        [Tooltip("Tilemap child scale — (0.5, 0.5, 1) matches the demo/lab convention.")]
        public Vector3 tilemapScale = new Vector3(0.5f, 0.5f, 1f);
        [Range(0, 6), Tooltip("Base tiles left visible between overlays (the blend gap).")]
        public int gapTiles = 2;
        [Tooltip("Show the raw painted tiles over the aesthetic. Off = pure SpriteShape look.")]
        public bool showRawTiles = false;

        void Awake()
        {
            if (Application.isPlaying && transform.childCount == 0)
                Build();
        }

        [ContextMenu("Build Swatches")]
        public void Build()
        {
            if (grid == null || tile == null)
            {
                Debug.LogError("[SpriteShapeStyleLab] Assign grid and tile first.", this);
                return;
            }

            Clear();

            int n = Mathf.Max(1, cellsPerTile);

            // --- base isle: one large solid rect, everything is judged on it ---
            if (baseAesthetic != null && baseAesthetic.stackTemplate != null)
                BuildSwatch("Base_" + baseAesthetic.name, baseAesthetic,
                    Vector3Int.zero, baseTilesX * n, baseTilesY * n, 0);
            else
                Debug.LogWarning("[SpriteShapeStyleLab] No base aesthetic — overlays float on nothing.", this);

            // --- overlays: squares ON the base, base tiles visible between them ---
            int side = Mathf.Max(1, tilesPerSwatch) * n;
            int step = (tilesPerSwatch + Mathf.Max(0, gapTiles)) * n;
            int margin = Mathf.Max(1, gapTiles) * n;
            int cursorX = margin, cursorY = margin;
            int built = 0;
            for (int i = 0; i < aesthetics.Count; i++)
            {
                var aesthetic = aesthetics[i];
                if (aesthetic == null) continue;
                if (aesthetic.stackTemplate == null)
                {
                    Debug.LogWarning($"[SpriteShapeStyleLab] '{aesthetic.name}' has no stackTemplate — skipped.", aesthetic);
                    continue;
                }

                if (cursorX + side + margin > baseTilesX * n) // wrap to next row
                {
                    cursorX = margin;
                    cursorY += step;
                }
                if (cursorY + side + margin > baseTilesY * n)
                    Debug.LogWarning($"[SpriteShapeStyleLab] Base isle too small for '{aesthetic.name}' — enlarge baseTiles.", this);

                BuildSwatch(aesthetic.name, aesthetic,
                    new Vector3Int(cursorX, cursorY, 0), side, side, 2 + i);
                cursorX += step;
                built++;
            }

            Debug.Log($"[SpriteShapeStyleLab] Built base ({baseTilesX}x{baseTilesY} tiles) + " +
                      $"{built}/{aesthetics.Count} overlay swatches ({tilesPerSwatch}x{tilesPerSwatch}).", this);
        }

        /// <summary>One tilemap + stack instance covering a cell rect at cellOffset.</summary>
        private void BuildSwatch(
            string label, TileAestheticSO aesthetic, Vector3Int cellOffset,
            int cellsW, int cellsH, int sortingBump)
        {
            var tmGo = new GameObject($"StyleSwatch_{label}") { hideFlags = HideFlags.DontSave };
            tmGo.transform.SetParent(grid.transform, false);
            tmGo.transform.localScale = tilemapScale;
            var tm = tmGo.AddComponent<Tilemap>();
            var tr = tmGo.AddComponent<TilemapRenderer>();
            tr.enabled = showRawTiles;
            tr.sortingOrder = sortingBump;

            var stack = Instantiate(aesthetic.stackTemplate, transform);
            stack.name = $"StyleStack_{label}";
            stack.hideFlags = HideFlags.DontSave;
            stack.transform.localPosition = Vector3.zero;

            var t2s = stack.GetComponent<TilemapToSpriteShape>();
            if (t2s != null) t2s.SetSourceTilemap(tm);
            var controller = stack.GetComponent<UnityEngine.U2D.SpriteShapeController>();
            if (controller != null && controller.spriteShapeRenderer != null)
                controller.spriteShapeRenderer.sortingOrder += sortingBump;
            aesthetic.ApplyTo(stack);

            var positions = new Vector3Int[cellsW * cellsH];
            var tiles = new TileBase[cellsW * cellsH];
            for (int y = 0; y < cellsH; y++)
                for (int x = 0; x < cellsW; x++)
                {
                    positions[y * cellsW + x] = cellOffset + new Vector3Int(x, y, 0);
                    tiles[y * cellsW + x] = tile;
                }
            tm.SetTiles(positions, tiles);
            // Trace NOW: the tilemap-changed chain is tick-driven and an idle
            // editor doesn't tick — swatches would sit untraced/gray.
            if (t2s != null) t2s.GenerateBoundary();
        }

        [ContextMenu("Clear Swatches")]
        public void Clear()
        {
            if (grid != null) Sweep(grid.transform, "StyleSwatch_");
            Sweep(transform, "StyleStack_");
        }

        private static void Sweep(Transform parent, string prefix)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var c = parent.GetChild(i);
                if (!c.name.StartsWith(prefix)) continue;
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
        }
    }
}
