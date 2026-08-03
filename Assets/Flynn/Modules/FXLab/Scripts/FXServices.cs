using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Shared scene services recipe blocks route through: world spawners (puff, ring,
    /// sparkle, sheet anim) plus the two globals (camera nudge, hitstop). Find-or-create:
    /// the first consumer provisions everything, so a VFXPresenter needs zero scene
    /// wiring. Existing scene globals are reused, never duplicated (two HitStopFX would
    /// fight over Time.timeScale; CamNudgeFX must sit on the camera). Module-local
    /// convenience for the lab and its consumers - not a game singleton.
    /// </summary>
    public class FXServices : MonoBehaviour
    {
        public PuffBurstFX puffer;
        public RingFX ringer;
        public SparkleFX sparkler;
        public SheetAnimFX sheet;
        public SheetAnimFX sheetSlash;
        public ArcWipeFX arcWipe;
        public ArcRibbonFX arcRibbon;
        public DropletSprayFX droplets;
        public EmoteBubbleFX emote;
        public FloatAuraFX floatAura;
        public ConvergeFX converge;
        public CamNudgeFX camNudge;
        public HitStopFX hitStop;

        [Header("Scene actors - found, never created (they belong to a character)")]
        [Tooltip("Whoever swings the tool: lunge body language + behind-player arc sorting.")]
        public SwingLungeFX lunge;
        public Transform player;

        /// <summary>Raised when a recipe's swing opens - the character rig's cue to play its
        /// swing animation. Code-only (no inspector), set by whoever owns the character.</summary>
        public System.Action<Vector2> onSwing;

        static FXServices _live;

        public static FXServices Get()
        {
            if (_live == null)
            {
                _live = FindObjectOfType<FXServices>();
                if (_live == null)
                    _live = new GameObject("FXServices").AddComponent<FXServices>();
            }
            _live.EnsureParts();
            return _live;
        }

        void Awake()
        {
            if (_live == null) _live = this;
            EnsureParts();
        }

        void OnDestroy()
        {
            if (_live == this) _live = null;
        }

        void EnsureParts()
        {
            if (puffer == null) puffer = Need<PuffBurstFX>();
            if (ringer == null) ringer = Need<RingFX>();
            if (sparkler == null) sparkler = Need<SparkleFX>();
            if (sheet == null) sheet = Need<SheetAnimFX>();
            if (droplets == null) droplets = Need<DropletSprayFX>();
            if (emote == null) emote = Need<EmoteBubbleFX>();
            if (floatAura == null) floatAura = Need<FloatAuraFX>();
            if (converge == null) converge = Need<ConvergeFX>();
            // the slash needs its OWN sheet spawner: it is aimed and re-sorted per facing,
            // while `sheet` fires flat impact bursts
            if (sheetSlash == null) sheetSlash = gameObject.AddComponent<SheetAnimFX>();
            if (arcWipe == null) arcWipe = Need<ArcWipeFX>();
            // safe to auto-provision: ArcRibbonFX rigs a CHILD onto the character and never
            // reparents or scales its own transform, so this shared object stays put
            if (arcRibbon == null) arcRibbon = Need<ArcRibbonFX>();
            // character-owned: take whatever the scene already has, never spawn one
            if (lunge == null) lunge = FindObjectOfType<SwingLungeFX>();
            if (player == null && lunge != null) player = lunge.transform;
            if (hitStop == null)
            {
                hitStop = FindObjectOfType<HitStopFX>();
                if (hitStop == null) hitStop = Need<HitStopFX>();
            }
            if (camNudge == null)
            {
                camNudge = FindObjectOfType<CamNudgeFX>();
                if (camNudge == null && Camera.main != null)
                    camNudge = Camera.main.gameObject.AddComponent<CamNudgeFX>();
            }
        }

        T Need<T>() where T : Component
        {
            var c = GetComponent<T>();
            return c != null ? c : gameObject.AddComponent<T>();
        }
    }
}
