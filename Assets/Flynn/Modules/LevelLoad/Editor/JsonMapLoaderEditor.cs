using UnityEditor;
using UnityEngine;

namespace Flynn.Environment
{
    /// <summary>
    /// Inspector for <see cref="JsonMapLoader"/>: Regenerate button, auto-regen when a
    /// field changes (so swapping the JSON asset reloads instantly), and a read-only
    /// table of the ground types found in the loaded JSON with their resolved mapping.
    /// </summary>
    [CustomEditor(typeof(JsonMapLoader))]
    public class JsonMapLoaderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var loader = (JsonMapLoader)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool fieldChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space();
            bool buttonPressed = GUILayout.Button("Regenerate", GUILayout.Height(28));

            if (buttonPressed || (fieldChanged && loader.autoRegenerate))
                loader.Regenerate();

            var types = loader.LoadedGroundTypes;
            if (types != null && types.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Loaded: {loader.LoadedMapName}", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(true))
                {
                    foreach (var t in types)
                        EditorGUILayout.LabelField(
                            $"  {t.id}  {t.name}",
                            $"{t.tileCount} tiles — {t.mode}");
                }
            }
        }
    }
}
