using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// LevelLoad lab helper: paints a controllable island blob onto the tilemap so the
    /// ground-gen stack has a confined, repeatable shape to chew on. Right-click the
    /// component header -> "Paint Island" / "Clear Island" (works in edit mode; the
    /// [ExecuteAlways] stack regenerates live). Also paints on Play if the map is
    /// empty, so the lab scene never opens dead. Dial radii/wobble/seed, repaint,
    /// tweak the visual stack - the controlled environment the module tunes in.
    /// </summary>
    public class LabIslandPainter : MonoBehaviour
    {
        [Tooltip("Tilemap to paint. The same one TilemapToSpriteShape reads.")]
        public Tilemap tilemap;
        [Tooltip("Tile painted into cells - use the ground tile the demo scene paints with.")]
        public TileBase tile;

        [Header("Shape (cells)")]
        [Range(2, 40)] public int radiusX = 10;
        [Range(2, 40)] public int radiusY = 8;
        [Tooltip("Coastline wobble - fraction of the radius eaten/grown by noise.")]
        [Range(0f, 0.6f)] public float wobble = 0.25f;
        [Tooltip("Noise feature size in cells - small = choppy coast, big = lazy bays.")]
        public float noiseScale = 6f;
        public int seed = 7;

        void Awake()
        {
            if (Application.isPlaying && tilemap != null && tilemap.GetUsedTilesCount() == 0)
                Paint();
        }

        [ContextMenu("Paint Island")]
        public void Paint()
        {
            if (tilemap == null || tile == null) return;
            Clear();
            float ox = seed * 13.37f, oy = seed * 7.13f;
            for (int y = -radiusY; y <= radiusY; y++)
            {
                for (int x = -radiusX; x <= radiusX; x++)
                {
                    float nx = x / (float)radiusX;
                    float ny = y / (float)radiusY;
                    float n = Mathf.PerlinNoise(ox + x / noiseScale, oy + y / noiseScale);
                    float edge = 1f + (n - 0.5f) * 2f * wobble;
                    if (nx * nx + ny * ny <= edge)
                        tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        [ContextMenu("Clear Island")]
        public void Clear()
        {
            if (tilemap == null) return;
            tilemap.ClearAllTiles();
        }
    }
}
