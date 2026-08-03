using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// Live-builder loop for the island coastline collider: whenever a PaintedStyleLayer finishes
    /// regenerating (its "*_cells_z*" maps change), rebuild every PlatformBorderBuilder after a
    /// debounce. The debounce (longer than PaintedStyleLayer's own 0.3 s) guarantees the cell maps
    /// are final before tracing.
    /// </summary>
    [InitializeOnLoad]
    public static class BorderLiveRebuild
    {
        private const double DebounceSeconds = 0.6;
        private static double _at;
        private static bool _pending;

        static BorderLiveRebuild()
        {
            Tilemap.tilemapTileChanged += OnTileChanged;
            EditorApplication.update += OnUpdate;
        }

        private static void OnTileChanged(Tilemap map, Tilemap.SyncTile[] tiles)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!map.name.Contains("_cells_z")) return;
            _at = EditorApplication.timeSinceStartup + DebounceSeconds;
            _pending = true;
        }

        private static void OnUpdate()
        {
            if (!_pending || EditorApplication.timeSinceStartup < _at) return;
            _pending = false;
            foreach (var b in Object.FindObjectsOfType<PlatformBorderBuilder>(false))
                b.BuildBorders();
        }
    }
}
