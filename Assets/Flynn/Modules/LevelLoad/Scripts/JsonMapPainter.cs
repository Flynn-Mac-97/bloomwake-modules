using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// Paints a map-painter JSON export (David.MapJsonData) onto the ground tilemap so
    /// the island visual stack (TilemapToSpriteShape -> IslandSkirt / grass / foliage)
    /// renders the designer's painted level. Rectangular-grid on purpose: the shader
    /// island reads better on the rect grid than on the iso diamond, so painter cells
    /// map straight to rect cells (painter y-down flipped to world y-up, centered at
    /// origin). Every ground id is flattened into the footprint at z=0 — elevated
    /// tiles sit ON land, so their footprint is land. Elevation / mud / layer data
    /// (resources, npcs, largeSprites) stays in the JSON for the later layered pass.
    /// Right-click the component header -> "Paint Map" / "Clear Map" (edit mode works;
    /// the [ExecuteAlways] stack regenerates live). Also paints on Play if the map is
    /// empty, so the scene never opens dead.
    /// </summary>
    public class JsonMapPainter : MonoBehaviour
    {
        [Tooltip("Map-painter JSON export (Assets/David/map_painter_david/data/maps/*.json).")]
        public TextAsset mapJson;
        [Tooltip("Tilemap to paint. The same one TilemapToSpriteShape reads.")]
        public Tilemap tilemap;
        [Tooltip("Tile painted into cells - use the ground tile the demo scene paints with.")]
        public TileBase tile;

        [Header("Scale")]
        [Tooltip("Cells painted per JSON tile (NxN block). 2 matches the world scale the " +
                 "designer painted at (1 painter tile ~ 1x0.5 world units on the lab grid).")]
        [Range(1, 4)] public int cellsPerTile = 2;

        void Awake()
        {
            if (Application.isPlaying && tilemap != null && tilemap.GetUsedTilesCount() == 0)
                Paint();
        }

        [ContextMenu("Paint Map")]
        public void Paint()
        {
            if (tilemap == null || tile == null || mapJson == null)
            {
                Debug.LogError("[JsonMapPainter] Assign mapJson, tilemap and tile first.", this);
                return;
            }

            var map = JsonUtility.FromJson<David.MapJsonData>(mapJson.text);
            if (map == null || map.islands == null || map.islands.Count == 0)
            {
                Debug.LogError("[JsonMapPainter] Failed to parse JSON or no islands found.", this);
                return;
            }

            // Bounds in painter coords (y-down) across all islands.
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            int total = 0;
            foreach (var island in map.islands)
            {
                if (island.tiles == null) continue;
                foreach (var t in island.tiles)
                {
                    if (t.x < minX) minX = t.x;
                    if (t.x > maxX) maxX = t.x;
                    if (t.y < minY) minY = t.y;
                    if (t.y > maxY) maxY = t.y;
                    total++;
                }
            }
            if (total == 0)
            {
                Debug.LogWarning("[JsonMapPainter] Map has no tiles.", this);
                return;
            }

            Clear();

            int n = Mathf.Max(1, cellsPerTile);
            int offX = -((maxX - minX + 1) * n) / 2;
            int offY = -((maxY - minY + 1) * n) / 2;
            foreach (var island in map.islands)
            {
                if (island.tiles == null) continue;
                foreach (var t in island.tiles)
                {
                    int bx = (t.x - minX) * n + offX;
                    int by = (maxY - t.y) * n + offY; // painter y-down -> world y-up
                    for (int j = 0; j < n; j++)
                        for (int i = 0; i < n; i++)
                            tilemap.SetTile(new Vector3Int(bx + i, by + j, 0), tile);
                }
            }

            Debug.Log($"[JsonMapPainter] Painted {total} tiles ({map.islands.Count} island(s)) " +
                      $"from '{mapJson.name}' at {n}x{n} cells/tile.", this);
        }

        [ContextMenu("Clear Map")]
        public void Clear()
        {
            if (tilemap == null) return;
            tilemap.ClearAllTiles();
        }
    }
}
