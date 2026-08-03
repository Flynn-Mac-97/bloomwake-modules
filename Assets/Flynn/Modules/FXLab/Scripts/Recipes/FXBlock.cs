using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Everything a block needs to play: the reacting object (may be null for world-only
    /// combos), the world position and direction of the moment, a coroutine host for
    /// delayed steps, and the shared scene services (spawners / camera / clock).
    /// </summary>
    public class FXContext
    {
        public Transform target;
        public Vector3 pos;
        public Vector2 dir = Vector2.right;
        public MonoBehaviour host;
        public FXServices services;
        [Tooltip("Where nested recipes are looked up by name - set by whoever starts the firing.")]
        public FXLabTuning tuning;
        /// <summary>How many recipes deep this firing is. Guards a recipe that includes itself.</summary>
        public int depth;

        /// <summary>The material colour this firing carries (the node's own tint). Blocks
        /// with useMomentTint on recolour themselves from it, so ONE dialed recipe serves
        /// every material - the caller supplies the colour, not a duplicated recipe.</summary>
        public bool hasTint;
        public Color tint = Color.white;

        /// <summary>Where the character is swinging, 0-360 (0 = +X). The seam for hooking a real
        /// character up later: set this and the swing points there. Unset = the tuning dial.</summary>
        public bool hasSwingAngle;
        public float swingAngleDeg;
    }

    /// <summary>
    /// One modular effect step. Concrete blocks wrap a primitive (flash, squash, puff...)
    /// or run their own micro-behaviour (fade). A composed moment (HitWood, Repair) is
    /// just an ordered list of these - see FXRecipe. Blocks are defensive: missing
    /// target components or unassigned services skip silently, never throw.
    /// </summary>
    [Serializable]
    public abstract class FXBlock
    {
        public bool enabled = true;
        [Tooltip("Seconds after the recipe fires before this block plays. 0 = same frame, in list order.")]
        public float delay = 0f;

        public abstract void Play(FXContext ctx);
    }

    /// <summary>
    /// A block that can run the PRIMITIVE's dialed settings instead of its own copy.
    ///
    /// Adding a layer to a recipe should reuse what you already dialed in the fire-list, not
    /// hand you a blank slate to re-tune - so linked is the default. The link is live: re-dial
    /// the Puff primitive and every recipe using it changes. "Make custom" copies the current
    /// values into the block and detaches, for the one-off that needs to differ.
    /// </summary>
    public interface IPrimitiveLinked
    {
        bool UsePrimitive { get; set; }
        /// <summary>Fire-list name of the primitive this mirrors - shown in the tuner.</summary>
        string PrimitiveName { get; }
        /// <summary>Detach: copy the primitive's current values into this block's own settings.</summary>
        void CopyFromPrimitive(FXLabTuning tuning);
    }

    /// <summary>A named combo of blocks - a composed moment as pure data. New moment =
    /// new list entry on the tuning asset, zero conductor code. swingOpens = the tool
    /// arc fires first and the blocks land after contactDelay (hit grammar); off =
    /// blocks fire immediately (repair/item grammar).</summary>
    [Serializable]
    public class FXRecipe
    {
        public string name = "Recipe";
        [Tooltip("The swing tool opens this recipe: arc + onSwing fire, blocks land after contactDelay.")]
        public bool swingOpens = false;
        public ArcVariant arc = ArcVariant.Sheet;
        public float contactDelay = 0.14f;
        [Tooltip("Take the material colour from the target's own sprite tint - one recipe, every material.")]
        public bool tintFromTarget = false;
        [Tooltip("Lab only: stand-in art worn by the focus prop while this recipe is selected.")]
        public PreviewSlot preview = new PreviewSlot();
        [SerializeReference] public List<FXBlock> blocks = new List<FXBlock>();

        /// <summary>
        /// A genuinely independent copy - every block is a NEW object, so tuning the copy leaves
        /// the original alone. Use this to start a recipe from an existing one (ContactStone from
        /// ContactWood); duplicating the list entry in the inspector instead gives you two names
        /// pointing at one set of blocks.
        /// </summary>
        public FXRecipe DeepCopy(string newName = null)
        {
            var copy = new FXRecipe
            {
                name = string.IsNullOrEmpty(newName) ? name + " copy" : newName,
                swingOpens = swingOpens,
                arc = arc,
                contactDelay = contactDelay,
                tintFromTarget = tintFromTarget,
                preview = FXCopy.Deep(preview) ?? new PreviewSlot(),
                blocks = new List<FXBlock>()
            };
            if (blocks != null)
                foreach (var b in blocks)
                    copy.blocks.Add(FXCopy.DeepBlock(b));
            return copy;
        }
    }

    /// <summary>
    /// Value-copying for `[SerializeReference]` data.
    ///
    /// Duplicating a recipe (or a block) the obvious way copies the REFERENCE, not the object -
    /// Unity serialises one managed instance and points both entries at it, so editing either
    /// edits both. A JSON round-trip is the cheap way to get genuinely separate objects, arrays
    /// included; UnityEngine.Object fields (sprites, clips, SoundSOs) survive because JsonUtility
    /// stores them by instance id. Not for per-frame use - it allocates.
    /// </summary>
    public static class FXCopy
    {
        public static T Deep<T>(T source) where T : class =>
            source == null ? null : JsonUtility.FromJson<T>(JsonUtility.ToJson(source));

        /// <summary>Deep copy that keeps the block's concrete type (JsonUtility needs it
        /// explicitly - the static type here is only the FXBlock base).</summary>
        public static FXBlock DeepBlock(FXBlock source) =>
            source == null
                ? null
                : JsonUtility.FromJson(JsonUtility.ToJson(source), source.GetType()) as FXBlock;
    }

    /// <summary>Plays a recipe: zero-delay blocks fire immediately in list order,
    /// delayed blocks are scheduled on the context host.</summary>
    public static class FXRecipeRunner
    {
        public static void Run(FXRecipe recipe, FXContext ctx)
        {
            if (recipe == null || recipe.blocks == null || ctx == null) return;

            // an explicit tint from the caller wins; otherwise the target's own sprite colour
            if (!ctx.hasTint && recipe.tintFromTarget && ctx.target != null)
            {
                var sr = ctx.target.GetComponent<SpriteRenderer>();
                if (sr != null) { ctx.tint = sr.color; ctx.hasTint = true; }
            }

            foreach (var block in recipe.blocks)
            {
                if (block == null || !block.enabled) continue;
                if (block.delay > 0f && ctx.host != null)
                    ctx.host.StartCoroutine(Delayed(block, ctx));
                else
                    block.Play(ctx);
            }
        }

        static IEnumerator Delayed(FXBlock block, FXContext ctx)
        {
            yield return new WaitForSeconds(block.delay);
            block.Play(ctx);
        }
    }
}
