using UnityEngine;
using UnityEngine.Tilemaps;

namespace Flynn.Environment
{
    /// <summary>
    /// Marks a tilemap renderer as authoring UI: visible in the editor (so painted masks and
    /// markers can be seen and edited), disabled the moment the game runs.
    /// </summary>
    [RequireComponent(typeof(TilemapRenderer))]
    [DisallowMultipleComponent]
    public class EditOnlyTilemapRenderer : MonoBehaviour
    {
        private void Awake()
        {
            if (Application.isPlaying) GetComponent<TilemapRenderer>().enabled = false;
        }
    }
}
