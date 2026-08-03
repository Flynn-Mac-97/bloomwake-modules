using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Flynn.Contracts;

namespace Flynn.Modules.PlayerRig
{
    /// Bridges the new player's swing moment to the IHittable seam: wire the rig's
    /// onSwing UnityEvent → DoSwingHit(). Generous radius, no aim requirement (cozy).
    ///
    /// When a TileHoverCursor is assigned it takes priority: the swing strikes the node the
    /// player is actually pointing at, and only that one. Without it the swing keeps the old
    /// behaviour and hits everything in the radius — fine on its own, but it makes "I aimed at
    /// THAT tree" a lie whenever two nodes stand close together.
    public class SwingHarvestHitter : MonoBehaviour
    {
        [SerializeField] private float _hitRadius = 0.8f;
        [SerializeField] private int _damage = 1;

        [Tooltip("Optional. When set, a swing hits the cursor's locked target instead of " +
                 "everything in range — as long as that target is within Hit Radius.")]
        [SerializeField] private TileHoverCursor _cursor;

        [Tooltip("ON = a swing only ever strikes the node you are hovering (Stardew rule). OFF = a " +
                 "swing with nothing aimed at falls back to hitting everything inside Hit Radius, " +
                 "which quietly fells three trees when you meant one.")]
        [SerializeField] private bool _requireAimedTarget = true;

        [Header("Events")]
        [Tooltip("Fires per hittable actually struck, with the hit direction — wire impact FX / sfx / score here.")]
        public UnityEvent<Vector2> onHit = new UnityEvent<Vector2>();
        [Tooltip("Fires per hittable actually struck, with WHAT was struck — needed by effects " +
                 "that land on the target rather than on the player (flash, squash, puff).")]
        public UnityEvent<Transform> onHitTarget = new UnityEvent<Transform>();
        [Tooltip("Fires once when a swing connects with at least one hittable (whiffs don't fire).")]
        public UnityEvent onConnected = new UnityEvent();

        private static readonly Collider2D[] _hits = new Collider2D[16];

        public void DoSwingHit()
        {
            if (TryHitAimedTarget()) return;
            if (_requireAimedTarget) return;   // nothing aimed at = a clean whiff, never a wide sweep

            int n = Physics2D.OverlapCircleNonAlloc(transform.position, _hitRadius, _hits);
            var seen = new HashSet<IHittable>();
            int struck = 0;
            for (int i = 0; i < n; i++)
            {
                var hittable = _hits[i].GetComponentInParent<IHittable>();
                if (hittable == null || !seen.Add(hittable)) continue;
                var mb = hittable as MonoBehaviour;
                Vector2 dir = mb != null
                    ? ((Vector2)mb.transform.position - (Vector2)transform.position).normalized
                    : Vector2.right;
                hittable.ApplyHit(_damage, dir);
                onHit.Invoke(dir);
                if (mb != null) onHitTarget.Invoke(mb.transform);
                struck++;
            }
            if (struck > 0) onConnected.Invoke();
        }

        /// <summary>
        /// Strike exactly what the hover cursor locked onto. Returns false when there is no cursor,
        /// no locked target, or the target is out of reach — in which case the caller falls back to
        /// the radius sweep, so an un-aimed swing still connects with whatever you are standing in.
        /// </summary>
        private bool TryHitAimedTarget()
        {
            var target = _cursor != null ? _cursor.LockedTarget : null;
            if (target == null) return false;

            var hittable = target.GetComponentInParent<IHittable>();
            if (hittable == null) return false;

            Vector2 offset = (Vector2)target.position - (Vector2)transform.position;
            if (offset.sqrMagnitude > _hitRadius * _hitRadius) return false;

            Vector2 dir = offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector2.right;
            hittable.ApplyHit(_damage, dir);
            onHit.Invoke(dir);
            onHitTarget.Invoke((hittable as MonoBehaviour)?.transform ?? target);
            onConnected.Invoke();
            return true;
        }
    }
}
