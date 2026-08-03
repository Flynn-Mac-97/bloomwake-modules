using System;
using UnityEngine;
using Flynn.Contracts;
using Flynn.Core;
using Flynn.Events;
using Flynn.Resources;

namespace Flynn.Modules.ResourceNodes
{
    /// <summary>
    /// Makes a spawned resource node strikeable, and tells the spawner when it has been gathered
    /// so the island can grow it back.
    ///
    /// WHY IT LIVES ON THE VISUAL, NOT THE ROOT: <c>SwingHarvestHitter</c> reports the struck
    /// object as <c>(hittable as MonoBehaviour).transform</c>, and the FXLab contact path looks up
    /// FlashFX/SquashFX with a plain <c>GetComponent</c> on exactly that object. Putting IHittable
    /// on a root with no SpriteRenderer is why target-side FX silently do nothing. Sitting on the
    /// sprite object means the swing, the hover cursor, the outline and the FX all agree on one
    /// GameObject.
    ///
    /// Hits are published on <see cref="GameEventBus"/> when one exists, because ResourceNode's own
    /// wobble + flash only run off that event. With no bus in the scene the node would take damage
    /// in complete silence, so the fallback path reproduces the wobble and the flash locally —
    /// the strike must always be felt, bus or no bus.
    /// </summary>
    [DisallowMultipleComponent]
    public class HarvestableNode : MonoBehaviour, IHittable
    {
        private static readonly int FlashAmountID = Shader.PropertyToID("_FlashAmount");

        /// <summary>Raised once when this node has been fully gathered and is about to disappear.
        /// Module-local by design — the spawner subscribes at spawn time; no bus, no singleton.</summary>
        public event Action<HarvestableNode> Depleted;

        /// <summary>Inspector twin of <see cref="Depleted"/>, for the gathered moment. A prefab
        /// cannot hold a reference to the scene's FX rig, so wire this to the node's OWN
        /// FXTag.Play("complete") and the moment routes itself.</summary>
        public UnityEngine.Events.UnityEvent onDepleted = new UnityEngine.Events.UnityEvent();

        private ResourceNode _node;
        private SpriteRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private Vector3 _baseScale;
        private bool _baseScaleCaptured;
        private Coroutine _feelRoutine;

        /// <summary>Set by the spawner so a struck node can be traced back to its map placement.</summary>
        public string PlacementId { get; set; }

        private void Awake()
        {
            _node = GetComponentInParent<ResourceNode>();
            _renderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>Spawner calls this once the final scale is applied, so the hit squash returns
        /// to the right size instead of whatever the prefab shipped with.</summary>
        public void CaptureBaseScale()
        {
            _baseScale = transform.localScale;
            _baseScaleCaptured = true;
        }

        public void ApplyHit(int damage, Vector2 hitDirection)
        {
            if (_node == null) return;

            var bus = GameEventBus.Instance;
            if (bus != null)
            {
                // ResourceNode handles wobble + flash + damage off this event.
                bus.Publish(new ResourceHit(_node, hitDirection, damage));
                return;
            }

            // No bus: reproduce the feel ourselves, then apply the damage directly.
            _node.AddVisualTorque(-hitDirection.x * 30f / Mathf.Max(1f, _node.Weight));
            if (!_baseScaleCaptured) CaptureBaseScale();
            if (_feelRoutine != null) StopCoroutine(_feelRoutine);
            if (isActiveAndEnabled) _feelRoutine = StartCoroutine(HitFeel());
            _node.Hit(damage);
        }

        /// <summary>Squash + white flash on contact. VibeSpec §8 "Action" band: ~0.2s, small punch.</summary>
        private System.Collections.IEnumerator HitFeel()
        {
            const float duration = 0.18f;
            const float punch = 0.16f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                // Squash wide-and-low, then ease back — reads as "took the hit", not "is breaking".
                float e = Mathf.Sin(p * Mathf.PI);
                transform.localScale = new Vector3(
                    _baseScale.x * (1f + punch * e),
                    _baseScale.y * (1f - punch * e),
                    _baseScale.z);
                SetFlash(1f - p);
                yield return null;
            }
            transform.localScale = _baseScale;
            SetFlash(0f);
            _feelRoutine = null;
        }

        private void SetFlash(float amount)
        {
            if (_renderer == null) return;
            _mpb ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmountID, amount);
            _renderer.SetPropertyBlock(_mpb);
        }

        private void OnDestroy()
        {
            // ResourceNode destroys the whole node at zero health, so "destroyed" IS "gathered" —
            // except during scene teardown and during the spawner's own rebuild, which would
            // otherwise schedule a regrow for something that was never harvested.
            if (!Application.isPlaying) return;
            if (!gameObject.scene.isLoaded) return;
            Depleted?.Invoke(this);
            onDepleted.Invoke();
        }
    }
}
