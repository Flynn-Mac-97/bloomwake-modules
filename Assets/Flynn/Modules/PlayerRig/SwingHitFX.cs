using UnityEngine;
using Flynn.Modules.FXLab;

namespace Flynn.Modules.PlayerRig
{
    /// <summary>
    /// Turns the player's two swing moments into FXLab recipe moments: the swing opening
    /// (<see cref="PlaySwing"/>, wire <see cref="PlayerAnimator.onSwing"/>) and the strike landing
    /// (<see cref="PlayOn"/>, wire <see cref="SwingHarvestHitter.onHitTarget"/>).
    ///
    /// Both go through <see cref="FXRouter"/>, so WHAT plays is answered by an <see cref="FXTag"/> -
    /// the player's own tag names the swing recipe, and each struck node names its material recipe.
    /// That keeps every dialed value in one place (the recipe list the lab edits) instead of a
    /// second typed copy that drifts, and means a new material needs a tag row, not a code branch.
    ///
    /// The rig is reached through <see cref="FXRouter.Current"/> rather than an inspector ref,
    /// because this ships on the PlayerRig PREFAB and a serialised reference to a scene object
    /// would become a per-instance override. Silently does nothing when no rig is in the scene.
    /// </summary>
    public class SwingHitFX : MonoBehaviour
    {
        [Tooltip("FXTag key for the swing opening. The player's own FXTag maps it to a recipe.")]
        [SerializeField] private string _swingKey = "swing";
        [Tooltip("FXTag key for the strike landing. The struck object's FXTag maps it to a recipe.")]
        [SerializeField] private string _hitKey = "hit";

        /// <summary>The swing opens, aimed this way. Plays the player's own swing recipe.</summary>
        public void PlaySwing(Vector2 dir)
        {
            var router = FXRouter.Current;
            if (router == null) return;
            router.SetAim(dir);
            router.Play(_swingKey, transform);
        }

        /// <summary>The swing connected with this. Plays whatever that object says it is made of.</summary>
        public void PlayOn(Transform target)
        {
            var router = FXRouter.Current;
            if (router == null || target == null) return;
            router.SetAim((Vector2)(target.position - transform.position));
            router.Play(_hitKey, target);
        }
    }
}
