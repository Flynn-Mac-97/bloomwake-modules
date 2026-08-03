using System.Collections.Generic;
using UnityEngine;
using Flynn.Environment;

namespace Flynn.Modules.ResourceNodes
{
    /// <summary>
    /// Grows the map's resource layer: reads every <c>Resource</c> entry the level loader parsed
    /// out of the map JSON and plants the matching kind at that spot, then grows gathered ones
    /// back after a while.
    ///
    /// DESIGN — renewal, not extraction. A node that is gone forever turns a cozy island into a
    /// resource to be stripped, and quietly teaches the player to hoard and to feel bad about
    /// gathering. Every kind carries a <c>regrowSeconds</c>, so the island always returns to full.
    /// The player's choice becomes "what do I feel like tending today", never "have I ruined it".
    ///
    /// WHERE THE NODES LIVE: a scene-root container, never under the level-loader prefab. Objects
    /// parented into a prefab instance serialise as prefab additions and go stale; the loader
    /// learned that the hard way and this follows the same rule.
    ///
    /// Hook <see cref="JsonMapLoader.onRegenerated"/> → <see cref="Rebuild"/> and it maintains
    /// itself, exactly like GrassMaskBinder.Bind does for the scatter masks.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResourceNodeSpawner : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The level loader whose map JSON carries the resource layer. Auto-found in this object's parents/children when empty.")]
        [SerializeField] private JsonMapLoader loader;
        [Tooltip("Tilemap marker source. When set it REPLACES the JSON loader as the placement " +
                 "list — paint ObjectMarkerTiles on the marker tilemap instead of exporting map JSON.")]
        [SerializeField] private TilemapMarkerLayer markerSource;
        [Tooltip("JSON resource type id → kind. Ids missing from the catalog are reported, not silently skipped.")]
        [SerializeField] private ResourceCatalogSO catalog;
        [Tooltip("Fallback body used only for kinds whose Prefab is empty. Normally every kind carries " +
                 "its own authored variant and this stays as a safety net.")]
        [SerializeField] private GameObject nodePrefab;

        [Header("Output")]
        [Tooltip("Scene-root object the nodes are parented to. Created on demand, kept OUTSIDE any prefab.")]
        [SerializeField] private string generatedRootName = "ResourceNodes_Generated";
        [Tooltip("Deterministic layout seed — same seed + same map = same island, every reload.")]
        [SerializeField] private int seed = 4242;

        [Header("Renewal")]
        [Tooltip("Off = gathered nodes stay gone for the session. On = they grow back per kind.")]
        [SerializeField] private bool regrowEnabled = true;

        [Header("Lifecycle")]
        [Tooltip("Rebuild the whole node layer from the map JSON on Awake. OFF = whatever is already " +
                 "saved under the generated root IS the level — nodes you move, delete or add by hand " +
                 "in the scene survive Play. Regenerate deliberately with the Rebuild context menu.")]
        [SerializeField] private bool rebuildOnAwake = true;

        private Transform _root;
        private bool _rebuilding;

        /// <summary>
        /// World-rule hook: a regrow-ready slot replants only where this returns true. Scene rules
        /// install it (GameCore's lit-ground rule); null means regrow anywhere, so the module keeps
        /// working standalone. A blocked slot waits and re-asks — never cancels — so ground that
        /// later qualifies (light arrives, blight cleared) still comes back.
        /// </summary>
        public static System.Func<Vector3, bool> RegrowGate;

        /// <summary>One map placement and whatever is currently standing on it.</summary>
        private class Slot
        {
            public JsonMapLoader.LayerItem item;
            public ResourceKindSO kind;
            public HarvestableNode live;
            public float regrowAt;   // unscaled play time; 0 = nothing pending
            public int index;        // placement order — names nodes readably, ids are not unique
        }

        private readonly List<Slot> _slots = new List<Slot>();

        private void Awake()
        {
            if (rebuildOnAwake) { Rebuild(); return; }

            // Hand-authored level: the slot table stays empty, so regrow has nothing to regrow from.
            // Say so once rather than leaving a checkbox that quietly does nothing.
            if (regrowEnabled)
                Debug.Log("[ResourceNodes] Rebuild-on-Awake is off — the nodes saved in the scene are " +
                          "the level. Regrow is inactive: it needs the map-driven slot table that " +
                          "rebuild produces.", this);
        }

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        [ContextMenu("Rebuild Resource Nodes")]
        public void Rebuild()
        {
            if (!ResolveRefs()) return;

            _rebuilding = true;   // our own teardown must not look like a harvest
            try
            {
                var root = EnsureRoot();
                for (int i = root.childCount - 1; i >= 0; i--)
                    DestroySafe(root.GetChild(i).gameObject);
                _slots.Clear();

                var items = markerSource != null ? markerSource.BuildLayerItems()
                                                 : loader.BuildLayerItems();
                int planted = 0;
                var missing = new HashSet<int>();

                foreach (var item in items)
                {
                    if (item.kind != JsonMapLoader.LayerKind.Resource) continue;
                    var kind = catalog.Find(item.typeId);
                    if (kind == null) { missing.Add(item.typeId); continue; }

                    var slot = new Slot { item = item, kind = kind, index = _slots.Count };
                    _slots.Add(slot);
                    if (Plant(slot, instant: true)) planted++;
                }

                foreach (var id in missing)
                    Debug.LogWarning($"[ResourceNodes] Map uses resource type {id} but the catalog has " +
                                     "no kind for it — those placements are empty ground.", this);

                Debug.Log($"[ResourceNodes] Planted {planted} node(s) across {_slots.Count} placement(s) " +
                          $"under '{generatedRootName}'.", this);
            }
            finally { _rebuilding = false; }
        }

        /// <summary>Editor live-builder hook: rebuild only when this spawner is marker-driven,
        /// so painting markers never touches a JSON-driven spawner in another scene.</summary>
        public void RebuildIfMarkerDriven()
        {
            if (markerSource != null) Rebuild();
        }

        [ContextMenu("Clear Resource Nodes")]
        public void Clear()
        {
            _rebuilding = true;
            try
            {
                var root = FindRoot();
                if (root != null) DestroySafe(root.gameObject);
                _root = null;
                _slots.Clear();
            }
            finally { _rebuilding = false; }
        }

        private bool ResolveRefs()
        {
            if (markerSource == null)
            {
                if (loader == null) loader = GetComponentInParent<JsonMapLoader>();
                if (loader == null) loader = GetComponentInChildren<JsonMapLoader>(true);
                if (loader == null) { Debug.LogError("[ResourceNodes] No JsonMapLoader assigned or found (and no Marker Source set).", this); return false; }
            }
            if (catalog == null) { Debug.LogError("[ResourceNodes] No ResourceCatalogSO assigned.", this); return false; }
            // nodePrefab is only a fallback — each kind normally carries its own body.
            return true;
        }

        /// <summary>
        /// Place one node for a slot. The prefab is the design surface: it is instantiated AS AUTHORED
        /// and nothing on it is touched but the position — sprite, scale, colliders, sorting and the
        /// ResourceNode config are all tuned by hand in Prefab Mode. Anything this method changed at
        /// spawn time would be an edit you cannot see in the prefab and cannot fix there either.
        ///
        /// <paramref name="instant"/> skips the grow-in ease — used when the whole island is (re)built,
        /// where 79 things easing up at once reads as a glitch.
        /// </summary>
        private bool Plant(Slot slot, bool instant)
        {
            var kind = slot.kind;

            var body = kind.prefab != null ? kind.prefab : nodePrefab;
            if (body == null)
            {
                Debug.LogError($"[ResourceNodes] Kind '{kind.name}' has no prefab and there is no " +
                               "fallback Node Prefab — nothing to plant.", this);
                return false;
            }

            var go = InstantiateNode(body, EnsureRoot());
            go.name = $"{kind.displayName}_{slot.index}";

            // The only thing the spawner decides: where it stands. Jitter inside the tile so a
            // painted row doesn't read as a fence.
            int hash = PlacementHash(slot.item, seed);
            float jx = (Frac(hash, 1) - 0.5f) * 2f * kind.positionJitter;
            float jy = (Frac(hash, 2) - 0.5f) * 2f * kind.positionJitter * 0.5f; // iso squash on Y
            go.transform.position = slot.item.world + new Vector3(jx, jy, 0f);

            // Same lookup ResourceNode itself uses — the rig hangs the visual off the collider body.
            var visual = go.transform.Find("VisualRoot");
            if (visual == null) visual = go.transform.Find("ResourceCollider/VisualRoot");

            var harvest = visual != null
                ? visual.GetComponent<HarvestableNode>()
                : go.GetComponentInChildren<HarvestableNode>(true);
            if (harvest == null)
            {
                Debug.LogWarning($"[ResourceNodes] Prefab '{body.name}' has no HarvestableNode on its " +
                                 "VisualRoot — it will stand there but cannot be gathered.", go);
            }
            else
            {
                harvest.PlacementId = slot.item.id;
                harvest.CaptureBaseScale();
                harvest.Depleted += OnNodeDepleted;
                slot.live = harvest;
            }

            if (!instant && visual != null && isActiveAndEnabled && Application.isPlaying)
                StartCoroutine(GrowIn(visual, visual.localScale, kind.regrowEaseSeconds));
            return true;
        }

        /// <summary>
        /// Keep the prefab link alive. A plain <c>Instantiate</c> in the editor produces a
        /// disconnected clone, so editing the kind's prefab would leave every already-placed node
        /// stale — the whole point of one variant per kind is that fixing the prefab fixes the island.
        /// </summary>
        private static GameObject InstantiateNode(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var linked = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(
                    prefab, parent.gameObject.scene);
                linked.transform.SetParent(parent, false);
                return linked;
            }
#endif
            return Instantiate(prefab, parent);
        }

        private System.Collections.IEnumerator GrowIn(Transform visual, Vector3 target, float seconds)
        {
            float t = 0f;
            while (t < seconds && visual != null)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / seconds);
                // Slight overshoot on the way in — growth that lands softly, not a pop-in.
                float e = 1f - Mathf.Pow(1f - p, 3f);
                float s = Mathf.LerpUnclamped(0f, 1f, e) * (1f + 0.08f * Mathf.Sin(p * Mathf.PI));
                visual.localScale = target * s;
                yield return null;
            }
            if (visual != null) visual.localScale = target;
        }

        // ------------------------------------------------------------------
        // Renewal
        // ------------------------------------------------------------------

        private void OnNodeDepleted(HarvestableNode node)
        {
            if (_rebuilding) return;
            foreach (var slot in _slots)
            {
                if (slot.live != node) continue;
                slot.live = null;
                slot.regrowAt = regrowEnabled && slot.kind.regrowSeconds > 0f
                    ? Time.time + Mathf.Max(0f, slot.kind.regrowSeconds
                        + Random.Range(-slot.kind.regrowJitter, slot.kind.regrowJitter))
                    : 0f;
                return;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || _slots.Count == 0) return;
            float now = Time.time;
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.live != null || slot.regrowAt <= 0f || now < slot.regrowAt) continue;
                if (RegrowGate != null && !RegrowGate(slot.item.world)) { slot.regrowAt = now + 2f; continue; }
                slot.regrowAt = 0f;
                Plant(slot, instant: false);
            }
        }

        // ------------------------------------------------------------------
        // Container
        // ------------------------------------------------------------------

        private Transform EnsureRoot()
        {
            if (FindRoot() != null) return _root;
            var go = new GameObject(generatedRootName);
            if (go.scene != gameObject.scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
            _root = go.transform;
            _root.SetParent(null, false);
            _root.position = Vector3.zero;
            return _root;
        }

        private Transform FindRoot()
        {
            if (_root != null) return _root;
            if (!gameObject.scene.IsValid()) return null;
            foreach (var go in gameObject.scene.GetRootGameObjects())
                if (go.name == generatedRootName) { _root = go.transform; break; }
            return _root;
        }

        private static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        /// <summary>
        /// Stable per-placement hash, so the same map always lays out the same way. Keyed on the
        /// WORLD POSITION, not the JSON id: the map painter exports duplicate ids (every Overgrowth
        /// on the starting map ships as "resource_NaN"), which would give those placements identical
        /// sprite, scale and jitter — a row of visible clones. Position is unique by construction.
        /// </summary>
        private static int PlacementHash(JsonMapLoader.LayerItem item, int seed)
        {
            unchecked
            {
                int h = seed * 486187739;
                h = (h ^ Mathf.RoundToInt(item.world.x * 100f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(item.world.y * 100f)) * 16777619;
                h = (h ^ item.typeId) * 16777619;
                return h;
            }
        }

        private static float Frac(int hash, int channel)
        {
            unchecked
            {
                uint h = (uint)(hash ^ (channel * 374761393));
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
