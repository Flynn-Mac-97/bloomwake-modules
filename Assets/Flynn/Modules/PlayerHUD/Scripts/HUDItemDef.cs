using UnityEngine;

namespace Flynn.Feel
{
    /// <summary>Semantic tint role for placeholder icons — VibeTokens only, never raw colors.</summary>
    public enum HUDItemRole { InteractHero, InteractPassive, Bloom, Magic, CharNpc, GroundAccent }

    /// <summary>
    /// One inventory item definition (SO-first). Placeholder look = rounded square tinted by
    /// token role + initial letter; final-art phase assigns <see cref="icon"/> and the look
    /// rebinds with zero code changes.
    /// </summary>
    [CreateAssetMenu(fileName = "HUDItem", menuName = "Flynn/Feel/HUD Item")]
    public class HUDItemDef : ScriptableObject
    {
        public string displayName = "Item";
        [Tooltip("Placeholder tint (VibeTokens role). Ignored once icon art is assigned.")]
        public HUDItemRole role = HUDItemRole.InteractPassive;
        [Tooltip("Optional final art. Null = tinted rounded square + initial letter.")]
        public Sprite icon;
        [Tooltip("Tools live on the tool bar at a pinned hotkey and never move.")]
        public bool isTool;
        [Tooltip("Tools only: fixed tool-bar slot (0=Q, 1=W, 2=E).")]
        public int pinnedToolSlot;
        [Tooltip("Non-tools stack up to this in one slot.")]
        public int maxStack = 9;

        public Color Tint => role switch
        {
            HUDItemRole.InteractHero    => VibeTokens.InteractHero,
            HUDItemRole.InteractPassive => VibeTokens.InteractPassive,
            HUDItemRole.Bloom           => VibeTokens.Bloom,
            HUDItemRole.Magic           => VibeTokens.Magic,
            HUDItemRole.CharNpc         => VibeTokens.CharNpc,
            HUDItemRole.GroundAccent    => VibeTokens.GroundAccent,
            _                           => VibeTokens.InteractPassive,
        };

        public string Initial => string.IsNullOrEmpty(displayName)
            ? "?" : displayName.Substring(0, 1).ToUpperInvariant();
    }
}
