using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Flynn.Environment;

namespace Flynn.Modules.ResourceNodes
{
    /// <summary>
    /// The live-builder loop: painting on a marker tilemap rebuilds the node layer in edit mode,
    /// so the map updates while the author paints. Watches <see cref="Tilemap.tilemapTileChanged"/>
    /// for tilemaps that belong to a spawner's Marker Source and debounces one
    /// <see cref="ResourceNodeSpawner.Rebuild"/> per paint stroke (a drag fires the event per cell
    /// — rebuilding 65 prefabs per cell would crawl).
    /// </summary>
    [InitializeOnLoad]
    public static class MarkerLiveRebuild
    {
        private const double DebounceSeconds = 0.3;
        private static double _rebuildAt;
        private static bool _pending;

        static MarkerLiveRebuild()
        {
            Tilemap.tilemapTileChanged += OnTileChanged;
            EditorApplication.update += OnUpdate;
        }

        private static void OnTileChanged(Tilemap map, Tilemap.SyncTile[] tiles)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var layer = map.GetComponentInParent<TilemapMarkerLayer>();
            if (layer == null || layer.MarkerMap != map) return;
            _rebuildAt = EditorApplication.timeSinceStartup + DebounceSeconds;
            _pending = true;
        }

        private static void OnUpdate()
        {
            if (!_pending || EditorApplication.timeSinceStartup < _rebuildAt) return;
            _pending = false;
            foreach (var spawner in Object.FindObjectsOfType<ResourceNodeSpawner>(true))
                spawner.RebuildIfMarkerDriven();
        }
    }
}
