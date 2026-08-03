using UnityEngine;

namespace Flynn.Modules.PlayerRig
{
    /// <summary>
    /// Frame-animation data for the player: named clips of sprite frames exported from
    /// After Effects ("run_front (7).png" → clip id "run_front"). PlayerRig-owned fork of
    /// the critter anim set so the player rig carries its own animation with no cross-module dep.
    /// </summary>
    [CreateAssetMenu(menuName = "Flynn/PlayerRig/Anim Set", fileName = "PlayerAnims")]
    public class PlayerAnimSet : ScriptableObject
    {
        [System.Serializable]
        public class Clip
        {
            [Tooltip("Export prefix, e.g. run_front_45.")]
            public string id;
            [Tooltip("Playback frames/sec — tune by feel.")]
            public float fps = 18f;
            public bool loop = true;
            public Sprite[] frames;
        }

        public Clip[] clips;

        public Clip Get(string id)
        {
            if (clips == null) return null;
            foreach (var c in clips)
                if (c != null && c.id == id) return c;
            return null;
        }
    }
}
