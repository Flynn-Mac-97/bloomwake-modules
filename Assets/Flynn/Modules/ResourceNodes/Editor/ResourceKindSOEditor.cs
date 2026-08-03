using UnityEditor;
using UnityEngine;

namespace Flynn.Modules.ResourceNodes
{
    /// <summary>
    /// Inspector for <see cref="ResourceKindSO"/>: live-rebuilds all spawned nodes when any
    /// property changes, so tweaking targetHeight / scaleJitter / etc. is visible in the
    /// scene view without entering play mode.
    /// </summary>
    [CustomEditor(typeof(ResourceKindSO))]
    public class ResourceKindSOEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                RebuildAllSpawners();
            }
        }

        private static void RebuildAllSpawners()
        {
            var spawners = Object.FindObjectsOfType<ResourceNodeSpawner>(true);
            foreach (var spawner in spawners)
            {
                try { spawner.Rebuild(); }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[ResourceKindSO] Live rebuild failed on '{spawner.name}': {e.Message}", spawner);
                }
            }
        }
    }
}
