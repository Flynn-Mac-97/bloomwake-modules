using UnityEngine;

namespace Flynn.Environment
{
    /// <summary>
    /// One authored tile aesthetic: the SpriteShape visual identity of a ground type
    /// (mostly shader-driven). STRUCTURE = a stack template prefab (which components
    /// exist — LandStack has skirt/grass, WaterStack is bare SpriteShape); VALUES = an
    /// optional profile pushed into the instance (IslandVisualProfile -> IslandSkirt,
    /// WaterVisualProfile -> WaterSurface). Designed in the StyleLab swatch scene;
    /// consumed by mapping entries once proven.
    /// </summary>
    [CreateAssetMenu(fileName = "TileAesthetic", menuName = "Flynn/LevelLoad/Tile Aesthetic")]
    public class TileAestheticSO : ScriptableObject
    {
        [TextArea(2, 4), Tooltip("What this aesthetic is for — designer notes only.")]
        public string note;
        [Tooltip("Visual stack template prefab (structure).")]
        public GameObject stackTemplate;
        [Tooltip("Profile pushed into the instantiated stack (values). Empty = template's own.")]
        public ScriptableObject profile;
        [Tooltip("Optional SpriteShape asset override (fill texture / edge sprites) — lets " +
                 "one template prefab serve many patch types (mud, ice, sand...). " +
                 "Empty = the template's own shape.")]
        public UnityEngine.U2D.SpriteShape spriteShapeOverride;

        /// <summary>Push this aesthetic's profile + shape into an instantiated stack.</summary>
        public void ApplyTo(GameObject stackInstance)
        {
            if (stackInstance == null) return;
            if (spriteShapeOverride != null)
                foreach (var c in stackInstance.GetComponentsInChildren<UnityEngine.U2D.SpriteShapeController>(true))
                    c.spriteShape = spriteShapeOverride;
            if (profile is IslandVisualProfile islandProfile)
                foreach (var sk in stackInstance.GetComponentsInChildren<IslandSkirt>(true))
                    sk.ProfileAsset = islandProfile;
            else if (profile is WaterVisualProfile waterProfile)
                foreach (var ws in stackInstance.GetComponentsInChildren<WaterSurface>(true))
                    ws.profile = waterProfile;
        }
    }
}
