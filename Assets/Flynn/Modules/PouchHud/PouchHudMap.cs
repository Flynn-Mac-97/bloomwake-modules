using System;
using UnityEngine;
using Flynn.Feel;
using Flynn.Player;

namespace Flynn.Modules.PouchHud
{
    /// <summary>
    /// Translation table between the two item vocabularies that exist in this project and do not
    /// know about each other:
    ///
    ///   <see cref="ItemDefinition"/> (Flynn.Runtime) — what the world actually drops and what
    ///   PlayerInventory stores. Carries stack rules, world prefab, tool type.
    ///
    ///   <see cref="HUDItemDef"/> (Flynn.Modules.HUD) — what the feel-passed PlayerHUD renders.
    ///   Carries icon, VibeSpec role tint, pinned tool slot.
    ///
    /// Keeping them separate is deliberate — the HUD module has no business referencing gameplay
    /// item rules, and stays cullable. This asset is an OVERRIDE table, not a gate: every item
    /// reaches the bar whether or not it has a row, carrying its own name and icon (see
    /// PouchHudRelay.Resolve). Author a row only to supply dedicated inventory art, or a
    /// placeholder tint role for an item that has none yet. Identity always comes from the
    /// ItemDefinition, so a row can never make one item wear another's label.
    /// </summary>
    [CreateAssetMenu(menuName = "Flynn/Pouch HUD Map", fileName = "PouchHudMap")]
    public class PouchHudMap : ScriptableObject
    {
        [Serializable]
        public struct Row
        {
            [Tooltip("The gameplay item that drops into PlayerInventory.")]
            public ItemDefinition item;
            [Tooltip("The HUD icon definition to show for it.")]
            public HUDItemDef hudItem;
        }

        [Tooltip("One row per item whose HUD look should differ from its ItemDefinition. " +
                 "Unmapped items still appear, using the gameplay icon and stack rules.")]
        [SerializeField] private Row[] _rows = Array.Empty<Row>();

        /// <summary>HUD counterpart for a gameplay item, or false when the item isn't mapped.</summary>
        public bool TryGet(ItemDefinition item, out HUDItemDef hudItem)
        {
            hudItem = null;
            if (item == null || _rows == null) return false;

            for (int i = 0; i < _rows.Length; i++)
            {
                if (_rows[i].item != item) continue;
                hudItem = _rows[i].hudItem;
                return hudItem != null;
            }
            return false;
        }
    }
}
