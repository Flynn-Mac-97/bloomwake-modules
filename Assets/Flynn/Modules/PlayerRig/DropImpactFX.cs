using UnityEngine;
using Flynn.Core;
using Flynn.Events;
using Flynn.Modules.FXLab;

namespace Flynn.Modules.PlayerRig
{
    /// Every spawned drop gets the FXLab item-drop moment (puff + idle sparkle) at its
    /// spawn point. Bus-fed so any module's drops get the treatment for free.
    public class DropImpactFX : MonoBehaviour
    {
        [SerializeField] private ItemDropFX _dropFX;

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
                GameEventBus.Instance.Unsubscribe<ResourceDropSpawned>(OnDrop);
            _subscribed = false;
        }

        private void TrySubscribe()
        {
            if (_subscribed || GameEventBus.Instance == null) return;
            GameEventBus.Instance.Subscribe<ResourceDropSpawned>(OnDrop);
            _subscribed = true;
        }

        private void OnDrop(ResourceDropSpawned evt)
        {
            if (_dropFX == null) return;
            var away = Random.insideUnitCircle.normalized;
            _dropFX.Play(evt.Position, away);
        }
    }
}
