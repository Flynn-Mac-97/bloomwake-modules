using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// The target-side recipe hub: put one on any sprite/object, assign the tuning
    /// asset, fire combos at it - Play(recipe) from code or PlayByName("HitWood") from
    /// a UnityEvent. This object is the context target and position; target-side blocks
    /// (flash/squash/fade) find their components here, world blocks route through the
    /// auto-provisioned FXServices. Zero scene wiring beyond the tuning asset.
    /// </summary>
    public class VFXPresenter : MonoBehaviour
    {
        [Tooltip("Recipe source for PlayByName. Swap the asset to restyle every combo at once.")]
        public FXLabTuning tuning;

        /// <summary>UnityEvent-friendly: fires the named recipe from the tuning asset.</summary>
        public void PlayByName(string recipeName)
        {
            if (tuning == null) return;
            Play(tuning.FindRecipe(recipeName));
        }

        public void Play(FXRecipe recipe) => Play(recipe, Vector2.right);

        public void Play(FXRecipe recipe, Vector2 dir)
        {
            if (recipe == null) return;
            FXRecipeRunner.Run(recipe, new FXContext
            {
                target = transform,
                pos = transform.position,
                dir = dir,
                host = this,
                services = FXServices.Get()
            });
        }
    }
}
