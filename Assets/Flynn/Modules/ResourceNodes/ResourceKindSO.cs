using UnityEngine;

namespace Flynn.Modules.ResourceNodes
{
    /// <summary>
    /// One harvestable kind, keyed to a resource type id from the map painter's toolset
    /// (2000 Tree, 2001 Stone, 2002 Rock, 2003 Tree stub, 2004 Bush, 2005 Overgrowth).
    ///
    /// AUTHORING CONTRACT — the PREFAB is the design surface, this asset is only the routing.
    /// Sprite, scale, colliders, sorting, wind and the ResourceNode config all live on the kind's
    /// own prefab variant and are tuned there by hand. The spawner instantiates that prefab and
    /// changes nothing on it, so what you see in Prefab Mode is what stands on the island.
    /// </summary>
    [CreateAssetMenu(menuName = "Flynn/Resource Nodes/Kind", fileName = "ResourceKind")]
    public class ResourceKindSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Resource type id from the map JSON toolset. Must match or nothing spawns for it.")]
        public int jsonTypeId;
        [Tooltip("Player-facing name, used for the hover prompt (\"Gather Bush\").")]
        public string displayName = "Resource";
        [Tooltip("Verb shown before the name. Cozy framing: Gather / Clear / Prune — never Destroy.")]
        public string promptVerb = "Gather";

        [Header("Body")]
        [Tooltip("This kind's prefab variant, authored complete: sprite, scale, colliders, config. " +
                 "The spawner instantiates it as a PREFAB INSTANCE and edits nothing but its position.")]
        public GameObject prefab;

        [Header("Placement")]
        [Tooltip("Random world offset inside the tile so a row of nodes doesn't form a visible grid line.")]
        public float positionJitter = 0.18f;

        [Header("Renewal — the cozy pillar")]
        [Tooltip("Seconds before a gathered node grows back. 0 = gone for good (avoid: permanent " +
                 "loss reads as extraction, not stewardship). Overgrowth returns fastest, trees slowest.")]
        [Min(0f)] public float regrowSeconds = 45f;
        [Tooltip("Random +/- spread on regrow time so a cleared patch doesn't pop back in lockstep.")]
        [Min(0f)] public float regrowJitter = 10f;
        [Tooltip("Seconds the returning node spends easing up from nothing to full size.")]
        [Min(0.01f)] public float regrowEaseSeconds = 0.45f;
    }
}
