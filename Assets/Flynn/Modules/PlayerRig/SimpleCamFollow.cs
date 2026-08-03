using UnityEngine;

namespace Flynn.Modules.PlayerRig
{
    /// Minimal smooth camera follow for the composed scene — keeps Z, lerps XY.
    /// Cinemachine stays out of the MVP glue; swap later without touching modules.
    public class SimpleCamFollow : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float _smooth = 5f;

        private void LateUpdate()
        {
            if (_target == null) return;
            var pos = transform.position;
            var goal = new Vector3(_target.position.x, _target.position.y, pos.z);
            transform.position = Vector3.Lerp(pos, goal, 1f - Mathf.Exp(-_smooth * Time.deltaTime));
        }
    }
}
