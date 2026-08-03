using System;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// What THIS object's moments look like. Put one on a prefab and it answers the router's
    /// question "you were hit — which recipe is that for you?" with a name from the tuning asset.
    ///
    /// WHY THIS EXISTS: without it every caller has to know the material of everything it can
    /// touch, so the player rig ends up wired straight to PlayHitWood and a stone node sounds like
    /// a tree. The knowledge belongs on the object, next to the art it describes, where whoever
    /// authors the prefab can change it without touching a scene or a line of code.
    ///
    /// Keys are free-form strings, so a new moment ("complete", "refuse", "ready") needs a new
    /// binding row and a recipe — never a new field here.
    /// </summary>
    [DisallowMultipleComponent]
    public class FXTag : MonoBehaviour
    {
        [Serializable]
        public struct Binding
        {
            [Tooltip("Moment key the caller asks for: hit, complete, ...")]
            public string key;
            [Tooltip("Recipe name on the FXLabTuning asset (Hit_Wood, Hit_Stone, Repair, ...).")]
            public string recipe;
        }

        [Tooltip("This object's moment → recipe table. Unlisted keys fall back to the router's default.")]
        public Binding[] bindings = { new Binding { key = "hit", recipe = "HitWood" } };

        /// <summary>
        /// UnityEvent-friendly self-dispatch. A prefab cannot serialise a reference to the scene's
        /// router, but it can reference its OWN tag — so a node's onDeath can fire
        /// <c>FXTag.Play("complete")</c> and the moment still routes through the scene rig.
        /// Silent when no router is present.
        /// </summary>
        public void Play(string key)
        {
            var router = FXRouter.Current;
            if (router != null) router.Play(key, transform);
        }

        /// <summary>Recipe name for a moment key, or null when this object does not answer for it.</summary>
        public string Resolve(string key)
        {
            if (bindings == null || string.IsNullOrEmpty(key)) return null;
            foreach (var b in bindings)
                if (string.Equals(b.key, key, StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrEmpty(b.recipe) ? null : b.recipe;
            return null;
        }
    }
}
