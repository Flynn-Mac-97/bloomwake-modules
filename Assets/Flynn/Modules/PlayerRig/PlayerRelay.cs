using UnityEngine;
using UnityEngine.Events;
using Flynn.Core;
using Flynn.Events;

namespace Flynn.Modules.PlayerRig
{
    /// Seam component on the player root: surfaces bus moments as inspector-wirable
    /// UnityEvents so FX / DemoFlow glue on without code refs. The heavy player stack
    /// (movement, 16-dir anim, swing chain) stays the proven Runtime prefab.
    public class PlayerRelay : MonoBehaviour
    {
        [Tooltip("Fired when a tool swing starts (FXLab moment glue).")]
        public UnityEvent onSwing = new UnityEvent();

        private bool _subscribed;

        private void OnEnable()
        {
            TrySubscribe();
        }

        // Bus may spawn after us in scene order — retry once Start runs.
        private void Start()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (GameEventBus.Instance != null)
                GameEventBus.Instance.Unsubscribe<ToolSwingStarted>(OnSwingEvt);
            _subscribed = false;
        }

        private void TrySubscribe()
        {
            if (_subscribed || GameEventBus.Instance == null) return;
            GameEventBus.Instance.Subscribe<ToolSwingStarted>(OnSwingEvt);
            _subscribed = true;
        }

        private void OnSwingEvt(ToolSwingStarted evt) => onSwing.Invoke();
    }
}
