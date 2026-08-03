using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// A dummy prop that receives effects. Click it to make it the focus target;
    /// the button board fires everything at the current focus. The per-object effect
    /// components live beside this on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class FXTarget : MonoBehaviour
    {
        public FlashFX Flash => _flash != null ? _flash : (_flash = GetComponent<FlashFX>());
        public SquashFX Squash => _squash != null ? _squash : (_squash = GetComponent<SquashFX>());
        public PuffBurstFX Puff => _puff != null ? _puff : (_puff = GetComponent<PuffBurstFX>());
        public RingFX Ring => _ring != null ? _ring : (_ring = GetComponent<RingFX>());
        public SparkleFX Sparkle => _sparkle != null ? _sparkle : (_sparkle = GetComponent<SparkleFX>());

        FlashFX _flash;
        SquashFX _squash;
        PuffBurstFX _puff;
        RingFX _ring;
        SparkleFX _sparkle;
    }
}
