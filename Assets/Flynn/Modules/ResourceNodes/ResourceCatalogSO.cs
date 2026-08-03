using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Modules.ResourceNodes
{
    /// <summary>
    /// The map's resource-type table: JSON type id → the kind that grows there. One asset to
    /// assign on the spawner, so swapping the whole island's flora is a single reference change.
    ///
    /// Ids with no entry are skipped and reported once, loudly — a silently unspawned resource
    /// type reads as "the map is empty there", which is indistinguishable from a design choice.
    /// </summary>
    [CreateAssetMenu(menuName = "Flynn/Resource Nodes/Catalog", fileName = "ResourceCatalog")]
    public class ResourceCatalogSO : ScriptableObject
    {
        [Tooltip("One entry per resource type id used by the map painter.")]
        public ResourceKindSO[] kinds = System.Array.Empty<ResourceKindSO>();

        private Dictionary<int, ResourceKindSO> _byId;

        public ResourceKindSO Find(int jsonTypeId)
        {
            if (_byId == null || _byId.Count != CountValid())
            {
                _byId = new Dictionary<int, ResourceKindSO>();
                foreach (var k in kinds)
                    if (k != null) _byId[k.jsonTypeId] = k;
            }
            return _byId.TryGetValue(jsonTypeId, out var kind) ? kind : null;
        }

        private int CountValid()
        {
            int n = 0;
            foreach (var k in kinds) if (k != null) n++;
            return n;
        }

        /// <summary>Drop the lookup cache — the editor edits `kinds` behind our back.</summary>
        private void OnValidate() => _byId = null;
    }
}
