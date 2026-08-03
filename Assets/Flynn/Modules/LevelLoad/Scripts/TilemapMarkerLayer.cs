using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// Tilemap-authored replacement for the map-JSON "layers" block: scans its marker tilemap for
    /// <see cref="ObjectMarkerTile"/> cells and serves them as LayerItems — the same spawn seam
    /// <see cref="JsonMapLoader.BuildLayerItems"/> provides, so consumers (ResourceNodeSpawner)
    /// swap sources without changing shape. Paint a marker, rebuild the spawner, done.
    ///
    /// The painted cell's Z (Tile Palette "Z Position") = elevation level, exactly like the grass
    /// cards: world Y lifts by z * <see cref="heightStep"/> so items sit on their terrace. Keep
    /// this tilemap on a Grid whose settings mirror the level grid (iso, 1 x 0.5) or cell centers
    /// will not land on the island.
    /// </summary>
    [DisallowMultipleComponent]
    public class TilemapMarkerLayer : MonoBehaviour
    {
        [Tooltip("Tilemap the markers are painted on. Auto-found on this object/children when empty.")]
        [SerializeField] private Tilemap markerMap;
        [Tooltip("World Y raise per elevation level — keep equal to JsonMapLoader.heightStep.")]
        [SerializeField] private float heightStep = 0.25f;
        [Tooltip("Markers are authoring UI: their renderer is disabled while playing.")]
        [SerializeField] private bool hideWhilePlaying = true;

        /// <summary>The tilemap markers are painted on — editor tooling (live rebuild) keys on this.</summary>
        public Tilemap MarkerMap => markerMap != null ? markerMap : GetComponentInChildren<Tilemap>(true);

        private void Awake()
        {
            if (!hideWhilePlaying || !Application.isPlaying) return;
            var map = markerMap != null ? markerMap : GetComponentInChildren<Tilemap>(true);
            var renderer = map != null ? map.GetComponent<TilemapRenderer>() : null;
            if (renderer != null) renderer.enabled = false;
        }

        /// <summary>
        /// Every painted marker, mapped to world space. Same contract as
        /// <see cref="JsonMapLoader.BuildLayerItems"/> — ids are cell-derived and unique by
        /// construction (the JSON painter's duplicate-id bug cannot happen here).
        /// </summary>
        public List<JsonMapLoader.LayerItem> BuildLayerItems()
        {
            var items = new List<JsonMapLoader.LayerItem>();
            if (markerMap == null) markerMap = GetComponentInChildren<Tilemap>(true);
            if (markerMap == null)
            {
                Debug.LogError("[TilemapMarkerLayer] No marker Tilemap assigned or found.", this);
                return items;
            }

            markerMap.CompressBounds();
            foreach (var cell in markerMap.cellBounds.allPositionsWithin)
            {
                var marker = markerMap.GetTile<ObjectMarkerTile>(cell);
                if (marker == null) continue;

                Vector3 p = markerMap.GetCellCenterWorld(cell);
                p.y += cell.z * heightStep; // painted Z = elevation level, like the grass cards
                p.z = 0f;

                items.Add(new JsonMapLoader.LayerItem
                {
                    kind = marker.kind,
                    id = $"marker_{cell.x}_{cell.y}_{cell.z}",
                    typeId = marker.typeId,
                    typeName = string.IsNullOrEmpty(marker.typeName) ? marker.name : marker.typeName,
                    world = p,
                    level = cell.z,
                });
            }
            return items;
        }
    }
}
