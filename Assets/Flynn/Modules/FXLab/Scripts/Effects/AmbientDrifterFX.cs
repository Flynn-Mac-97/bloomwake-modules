using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// The "always alive" ambience floor in one component: falling leaves, fireflies,
    /// pollen motes, steam curls - each is just a settings block (drift direction,
    /// wobble, flicker). Looping: StartLoop spawns and recycles a small mote pool
    /// around a center until StopLoop. One instance per ambience region.
    /// </summary>
    public class AmbientDrifterFX : MonoBehaviour
    {
        [Tooltip("Standalone tuning source for the parameterless Toggle (lab uses the selected variant).")]
        public FXLabTuning tuning;

        class Mote
        {
            public Transform tr;
            public SpriteRenderer sr;
            public Vector3 spawn;
            public float phase, age, life, size, baseAlpha;
        }

        Mote[] _motes;
        AmbientDrifterSettings _s;
        Vector3 _center;
        float _t;

        public bool IsRunning => _motes != null;

        public void ToggleAt(Vector3 center, AmbientDrifterSettings s)
        {
            if (IsRunning) StopLoop();
            else StartLoop(center, s);
        }

        public void StartLoop(Vector3 center, AmbientDrifterSettings s)
        {
            if (s == null) return;
            StopLoop();
            _s = s;
            _center = center;
            _motes = new Mote[Mathf.Max(1, s.count)];
            for (int i = 0; i < _motes.Length; i++)
            {
                _motes[i] = Spawn();
                // stagger ages so the loop doesn't breathe in sync
                _motes[i].age = Random.value * _motes[i].life;
            }
        }

        public void StopLoop()
        {
            if (_motes == null) return;
            foreach (var m in _motes)
                if (m != null && m.tr != null) Destroy(m.tr.gameObject);
            _motes = null;
        }

        void OnDisable() => StopLoop();

        Mote Spawn()
        {
            var m = new Mote();
            var go = new GameObject("Drift");
            go.transform.SetParent(transform, false);
            m.sr = go.AddComponent<SpriteRenderer>();

            Sprite sprite = null;
            if (_s.sprites != null && _s.sprites.Length > 0)
                sprite = _s.sprites[Random.Range(0, _s.sprites.Length)];
            m.sr.sprite = sprite != null ? sprite : FXSprites.SoftCircle;
            m.sr.sortingOrder = _s.sortingOrder;

            var c = _s.colors != null && _s.colors.Length > 0
                ? _s.colors[Random.Range(0, _s.colors.Length)] : Color.white;
            m.baseAlpha = c.a;
            m.sr.color = c;

            m.size = _s.size * (1f + Random.Range(-_s.sizeJitter, _s.sizeJitter));
            m.life = Mathf.Max(0.5f, _s.life * Random.Range(0.7f, 1.3f));
            m.phase = Random.value * Mathf.PI * 2f;
            m.spawn = _center + new Vector3(
                Random.Range(-_s.region.x, _s.region.x) * 0.5f,
                Random.Range(-_s.region.y, _s.region.y) * 0.5f, 0f);
            m.tr = go.transform;
            m.tr.position = m.spawn;
            m.tr.localScale = Vector3.one * m.size;
            m.age = 0f;
            return m;
        }

        void Respawn(Mote m)
        {
            m.spawn = _center + new Vector3(
                Random.Range(-_s.region.x, _s.region.x) * 0.5f,
                Random.Range(-_s.region.y, _s.region.y) * 0.5f, 0f);
            m.age = 0f;
            m.phase = Random.value * Mathf.PI * 2f;
            m.life = Mathf.Max(0.5f, _s.life * Random.Range(0.7f, 1.3f));
        }

        void Update()
        {
            if (_motes == null || _s == null) return;
            _t += Time.deltaTime;

            foreach (var m in _motes)
            {
                if (m == null || m.tr == null) continue;
                m.age += Time.deltaTime;
                if (m.age >= m.life) Respawn(m);

                float wob = Mathf.Sin(m.phase + _t * _s.wobbleHz * Mathf.PI * 2f) * _s.wobbleAmp;
                m.tr.position = m.spawn + (Vector3)(_s.drift * m.age) + new Vector3(wob, 0f, 0f);

                // fade in the first 15% / out the last 20% of life; flicker on top
                float n = m.age / m.life;
                float edge = Mathf.Min(Mathf.InverseLerp(0f, 0.15f, n), Mathf.InverseLerp(1f, 0.8f, n));
                float flick = 1f - _s.flickerAmp *
                    (0.5f + 0.5f * Mathf.Sin(m.phase * 3f + _t * _s.flickerHz * Mathf.PI * 2f));
                var c = m.sr.color;
                c.a = m.baseAlpha * edge * flick;
                m.sr.color = c;
            }
        }
    }
}
