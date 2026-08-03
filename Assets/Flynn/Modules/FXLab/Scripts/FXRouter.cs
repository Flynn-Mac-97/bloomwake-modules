using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// One scene-level entry point for "something happened to that object — show it".
    ///
    /// Callers hand over a moment KEY and the object it happened to; the router asks the object's
    /// <see cref="FXTag"/> which recipe that key means for it and plays it through the scene's
    /// <see cref="FXMomentPlayer"/>. So a swing wires to PlayHit ONCE and a wooden node, a stone
    /// node and an NPC each answer for themselves — the caller never learns what anything is made of.
    ///
    /// Everything degrades quietly: no tag → the fallback recipe; no recipe by that name → one
    /// warning, not a null-reference; no rig in the scene → nothing plays and the game runs.
    /// </summary>
    [DisallowMultipleComponent]
    public class FXRouter : MonoBehaviour
    {
        static FXRouter _live;

        /// <summary>The scene's router for callers that cannot hold an inspector ref (prefabs).
        /// Null when no router is present, so null-check every call site.</summary>
        public static FXRouter Current => _live;

        [Tooltip("Recipe source. Leave empty to use whatever the FX Moment Player is tuned with.")]
        [SerializeField] private FXLabTuning _tuning;
        [Tooltip("The rig that actually plays moments. Leave empty to use the scene's FXMomentPlayer.")]
        [SerializeField] private FXMomentPlayer _moments;
        [Tooltip("Played when the struck object has no FXTag, or no binding for the asked key.")]
        [SerializeField] private string _fallbackRecipe = "HitWood";

        private Vector2 _aim = Vector2.right;
        private bool _aimSet;
        private bool _warnedNoRig;

        void OnEnable() { if (_live == null) _live = this; }
        void OnDisable() { if (_live == this) _live = null; }

        /// <summary>
        /// UnityEvent&lt;Vector2&gt; hook. The direction of the moment about to land — wire the
        /// hitter's onHit here. A UnityEvent carries one argument, so direction and target arrive
        /// as two calls; the hitter fires direction first, which is why this is remembered.
        /// </summary>
        public void SetAim(Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return;
            _aim = dir.normalized;
            _aimSet = true;
        }

        /// <summary>UnityEvent&lt;Transform&gt; hook: the object was struck.</summary>
        public void PlayHit(Transform target) => Play("hit", target);

        /// <summary>UnityEvent&lt;Transform&gt; hook: the object finished / was gathered.</summary>
        public void PlayComplete(Transform target) => Play("complete", target);

        /// <summary>Play whatever <paramref name="target"/> says this moment key looks like.</summary>
        public void Play(string key, Transform target)
        {
            var tag = target != null ? target.GetComponentInParent<FXTag>() : null;
            var named = tag != null ? tag.Resolve(key) : null;
            PlayNamed(string.IsNullOrEmpty(named) ? _fallbackRecipe : named, target);
        }

        /// <summary>Play a recipe by name, bypassing the object's own answer.</summary>
        public void PlayNamed(string recipeName, Transform target)
        {
            var player = _moments != null ? _moments : FXMomentPlayer.Current;
            var tuning = _tuning != null ? _tuning : (player != null ? player.tuning : null);
            if (player == null || tuning == null)
            {
                if (!_warnedNoRig)
                {
                    _warnedNoRig = true;
                    Debug.LogWarning("[FXRouter] No FXMomentPlayer or tuning asset in the scene — " +
                                     "moments are silent. Add the FXLab rig.", this);
                }
                return;
            }

            var recipe = tuning.FindRecipe(recipeName);
            if (recipe == null)
            {
                Debug.LogWarning($"[FXRouter] No recipe named '{recipeName}' on '{tuning.name}'.", this);
                return;
            }
            // A real aim also answers "where is the character swinging" - without it a recipe's
            // swing arc falls back to the lab's dial and points wherever that was left.
            float? swingDeg = _aimSet
                ? Mathf.Atan2(_aim.y, _aim.x) * Mathf.Rad2Deg
                : (float?)null;
            player.PlayRecipeMoment(recipe, target, _aim, swingDeg: swingDeg);
        }
    }
}
