using UnityEngine;
using Flynn.Modules.FXLab;

namespace Flynn.Modules.PlayerRig
{
    /// Wire CritterAnimator.onSwing → PlayAtCursor: the FXLab burst lands on the hovered
    /// tile (or locked target), grounding the swing in the world.
    public class SwingGroundImpact : MonoBehaviour
    {
        [SerializeField] private TileHoverCursor _cursor;
        [SerializeField] private SheetAnimFX _burst;

        public void PlayAtCursor()
        {
            if (_cursor == null || _burst == null) return;
            _burst.PlayAt(_cursor.CurrentPoint, Vector2.zero);
        }
    }
}
