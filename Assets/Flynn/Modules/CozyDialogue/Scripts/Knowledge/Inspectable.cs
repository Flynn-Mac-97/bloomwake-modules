using UnityEngine;
using UnityEngine.Events;

namespace Flynn.Feel
{
    /// <summary>
    /// Click an object → the repairbot voices a short observation over it. That's all —
    /// no discovery, no event chains; knowledge comes from talking to NPCs.
    /// Needs a Collider2D.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Inspectable : MonoBehaviour
    {
        [TextArea]
        [Tooltip("The bot's self-talk line (VibeSpec §15 voice: short, warm, FIRST PERSON — " +
                 "the repairbot speaking, and it should actually SAY what the thing is: " +
                 "'A moss lantern! I can feel it humming.' not 'Hm, curious object.')")]
        public string observation = "Solid little thing. Older than me, I think.";

        [Tooltip("Cozy Dialogue UI facade (Stage D). When set, shows the repairbot's self-talk tag; replaces onObserve.")]
        public CozyDialogueUI ui;
        [Tooltip("Fallback: wire to an EmoteSpeaker.Say (dynamic string).")]
        public UnityEvent<string> onObserve;
        [Tooltip("Player must be within this world radius to inspect. 0 = no proximity gate.")]
        public float interactRadius = 2.5f;
        [Tooltip("Show the self-talk tag on HOVER (in range) instead of on click.")]
        public bool triggerOnHover = false;

        /// <summary>True once the player has inspected this at least once (drives the hover "seen" badge).</summary>
        public bool Inspected { get; private set; }

        /// <summary>
        /// Fires whenever ANY Inspectable is observed, carrying the one that was.
        /// Exists so a single director can react to inspections without a component on every object
        /// (2026-07-31 — the opening sequence needs tool-state-conditional lines). Static by design:
        /// domain reload stays on in this project, so subscribers do not leak across play sessions.
        /// Fires after the proximity gate, so a refused inspect stays silent.
        /// </summary>
        public static event System.Action<Inspectable> AnyInspected;

        void Observe()
        {
            if (interactRadius > 0f && !PlayerLocator.InRange(transform.position, interactRadius)) return;
            Inspected = true;
            if (ui != null) ui.ShowTag(transform, observation);
            else onObserve?.Invoke(observation);
            AnyInspected?.Invoke(this);
        }

        void OnMouseDown()
        {
            if (!triggerOnHover) Observe();
        }

        void OnMouseEnter()
        {
            if (triggerOnHover) Observe();
        }
    }
}
