using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// A paintable placement marker: one Tile asset per placeable type. Painting it on the
    /// object-marker tilemap IS the authoring act — <see cref="TilemapMarkerLayer"/> turns every
    /// painted cell into a <see cref="JsonMapLoader.LayerItem"/> for the spawn pass. The sprite is
    /// editor-facing placeholder art only; the marker tilemap never renders in Play.
    /// </summary>
    [CreateAssetMenu(menuName = "Flynn/Level/Object Marker Tile", fileName = "Marker_")]
    public class ObjectMarkerTile : Tile
    {
        [Tooltip("Which spawn layer this marker feeds (the resource spawner reads Resource; " +
                 "Npc / LargeSprite consumers hook the same seam later).")]
        public JsonMapLoader.LayerKind kind = JsonMapLoader.LayerKind.Resource;
        [Tooltip("Type id the consuming catalog resolves — matches the old map-painter toolset " +
                 "ids (2000 = Tree, 2001 = Stone, ...).")]
        public int typeId;
        [Tooltip("Display name for logs and generated node names.")]
        public string typeName;
    }
}
