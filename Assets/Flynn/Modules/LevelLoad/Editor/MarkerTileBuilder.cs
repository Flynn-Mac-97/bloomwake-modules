using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// Builds the paintable object-marker set from the placeholder sprites in
    /// <see cref="SpriteFolder"/>: fixes each sprite's import settings, creates/updates one
    /// <see cref="ObjectMarkerTile"/> per sprite (metadata parsed from the file name,
    /// "Marker_&lt;Kind&gt;_&lt;typeId&gt;_&lt;Name&gt;.png"), and rebuilds the MarkerPalette
    /// prefab. Re-run whenever sprites are added or regenerated — tile assets keep their GUIDs,
    /// so painted maps and palette references survive art swaps.
    /// </summary>
    public static class MarkerTileBuilder
    {
        private const string SpriteFolder = "Assets/Flynn/Modules/LevelLoad/Tiles/Markers";
        // ONE palette for the whole authoring surface — later slices (ground, platforms) add their
        // rows here instead of shipping palettes per category.
        private const string PalettePath = "Assets/Flynn/Modules/LevelLoad/Tiles/IslandAuthoringPalette.prefab";
        // 64x32 px at PPU 128 = one iso cell as painted in GameCore: authoring tilemaps carry the
        // rig's 0.5 world-scale normalization, so a cell is 0.5 x 0.25 world.
        private const float PixelsPerUnit = 128f;

        [MenuItem("Flynn/Tilemap/Build Object Marker Tiles + Palette")]
        public static void Build()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D Marker_", new[] { SpriteFolder });
            var tiles = new List<ObjectMarkerTile>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string baseName = System.IO.Path.GetFileNameWithoutExtension(path);

                // Marker_<Kind>_<typeId>_<Name...>
                var parts = baseName.Split('_');
                if (parts.Length < 4 || parts[0] != "Marker"
                    || !System.Enum.TryParse(parts[1], out JsonMapLoader.LayerKind kind)
                    || !int.TryParse(parts[2], out int typeId))
                {
                    Debug.LogWarning($"[MarkerTileBuilder] '{baseName}' does not match " +
                                     "Marker_<Kind>_<typeId>_<Name> — skipped.");
                    continue;
                }
                string typeName = string.Join("_", parts.Skip(3));

                FixImporter(path);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    Debug.LogWarning($"[MarkerTileBuilder] '{path}' imported without a sprite — skipped.");
                    continue;
                }

                string tilePath = $"{SpriteFolder}/{baseName}.asset";
                var tile = AssetDatabase.LoadAssetAtPath<ObjectMarkerTile>(tilePath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<ObjectMarkerTile>();
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.None;
                tile.kind = kind;
                tile.typeId = typeId;
                tile.typeName = typeName;
                EditorUtility.SetDirty(tile);
                tiles.Add(tile);
            }

            var maskTiles = BuildMaskTiles();

            AssetDatabase.SaveAssets();
            if (tiles.Count == 0 && maskTiles.Count == 0)
            {
                Debug.LogWarning($"[MarkerTileBuilder] No Marker_*/Mask_* sprites found in {SpriteFolder}.");
                return;
            }

            BuildPalette(tiles.OrderBy(t => t.kind).ThenBy(t => t.typeId).Cast<TileBase>().ToList(),
                         maskTiles);
            Debug.Log($"[MarkerTileBuilder] Built {tiles.Count} marker + {maskTiles.Count} mask " +
                      $"tile(s) + palette at {PalettePath}.");
        }

        /// <summary>Plain ground-mask tiles ("Mask_&lt;Name&gt;.png") for the PaintedStyleLayer
        /// tilemaps — palette row 1, painted onto GroundMask/PlatformMask/... targets.</summary>
        private static List<TileBase> BuildMaskTiles()
        {
            var result = new List<TileBase>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D Mask_", new[] { SpriteFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string baseName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!baseName.StartsWith("Mask_")) continue;

                FixImporter(path);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;

                string tilePath = $"{SpriteFolder}/{baseName}.asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
                result.Add(tile);
            }
            return result.OrderBy(t => t.name).ToList();
        }

        private static void FixImporter(string path)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return;
            bool dirty = ti.textureType != TextureImporterType.Sprite
                         || ti.spriteImportMode != SpriteImportMode.Single
                         || !Mathf.Approximately(ti.spritePixelsPerUnit, PixelsPerUnit)
                         || ti.filterMode != FilterMode.Point
                         || ti.textureCompression != TextureImporterCompression.Uncompressed
                         || ti.mipmapEnabled;
            if (!dirty) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }

        /// <summary>Palette prefab + GridPalette sub-asset, rebuilt in place. Row 0 = object
        /// markers, row 1 = ground masks — ONE palette for the whole authoring surface.</summary>
        private static void BuildPalette(List<TileBase> markerRow, List<TileBase> maskRow)
        {
            var root = new GameObject("IslandAuthoringPalette", typeof(Grid));
            var grid = root.GetComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var layer = new GameObject("Layer1", typeof(Tilemap), typeof(TilemapRenderer));
            layer.transform.SetParent(root.transform, false);
            var map = layer.GetComponent<Tilemap>();
            for (int i = 0; i < markerRow.Count; i++)
                map.SetTile(new Vector3Int(i, 0, 0), markerRow[i]);
            for (int i = 0; i < maskRow.Count; i++)
                map.SetTile(new Vector3Int(i, 1, 0), maskRow[i]);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PalettePath);
            Object.DestroyImmediate(root);

            // The GridPalette sub-asset is what makes the prefab show up in the Tile Palette window.
            var existing = AssetDatabase.LoadAllAssetsAtPath(PalettePath).OfType<GridPalette>().FirstOrDefault();
            if (existing == null)
            {
                var settings = ScriptableObject.CreateInstance<GridPalette>();
                settings.name = "Palette Settings";
                settings.cellSizing = GridPalette.CellSizing.Automatic;
                AssetDatabase.AddObjectToAsset(settings, prefab);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(PalettePath);
            }
        }
    }
}
