using UnityEditor;
using UnityEngine;

namespace Flynn.Environment
{
    [CustomEditor(typeof(SpriteShapeStyleLab))]
    public class SpriteShapeStyleLabEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var lab = (SpriteShapeStyleLab)target;

            DrawDefaultInspector();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build Swatches", GUILayout.Height(28)))
                    lab.Build();
                if (GUILayout.Button("Clear", GUILayout.Height(28), GUILayout.Width(80)))
                    lab.Clear();
            }
        }
    }
}
