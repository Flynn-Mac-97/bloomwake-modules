using System.Collections;
using UnityEngine;
using Flynn.Modules.FXLab;
using Flynn.World;

namespace Flynn.Modules.ItemFeel
{
    /// <summary>
    /// Gives a real <see cref="WorldItem"/> the drop / rest / pickup life the FX lab dialed in.
    ///
    /// The lab's ItemDropFX, ItemIdleFX and ItemPickupFX each spawn their OWN dummy sprite and
    /// throw it away — they are self-contained demos, so they cannot drive an item that has to
    /// survive and end up in the inventory. This reads the SAME dialed blocks (drop / idle /
    /// pickup on the tuning asset) and applies them to the item itself, so re-tuning in the lab
    /// re-tunes the game. Only the motion code is duplicated; every value stays single-source.
    ///
    /// Runtime keeps ownership of the collect: <see cref="DroppedItemMagnet"/> is held disabled
    /// through the drop and rest phases and switched on when the pickup flight arrives, so the
    /// inventory add and the currency routing stay exactly where they were.
    /// </summary>
    [RequireComponent(typeof(WorldItem))]
    [DisallowMultipleComponent]
    public class WorldItemFeel : MonoBehaviour
    {
        [Tooltip("How close the player must come before the item flies to them. The magnet's own " +
                 "radius still gates the actual collect, so keep this at or under it.")]
        [SerializeField] private float _pickupRadius = 1.6f;
        [Tooltip("Shortest time on the ground before it can be picked up - stops a drop being " +
                 "vacuumed mid-hop when you harvest at point-blank range.")]
        [SerializeField] private float _minRestSeconds = 0.35f;
        [Tooltip("Scales the item's own prefab scale for its whole life on the ground. Loose " +
                 "debris reads smaller than the thing it came off - 1 = the prefab's own size.")]
        [Range(0.2f, 1.5f)] [SerializeField] private float _groundScale = 0.7f;
        [Tooltip("Longest random hold before this item starts its pickup flight. A felled tree " +
                 "drops several at once; without a stagger they all fly and land on the SAME " +
                 "frame, which reads as one loud clipped noise instead of bap-bap-bap.")]
        [Range(0f, 0.5f)] [SerializeField] private float _pickupStagger = 0.14f;

        private WorldItem _item;
        private DroppedItemMagnet _magnet;
        private Rigidbody2D _rb;
        private SpriteRenderer _sr;
        private SpriteRenderer _shadow, _glow;
        private Transform _player;
        private Vector3 _ground;
        private Vector3 _baseScale;
        private float _phase;

        private FXLabTuning Tuning =>
            FXMomentPlayer.Current != null ? FXMomentPlayer.Current.tuning : null;

        private void Awake()
        {
            _item = GetComponent<WorldItem>();
            _sr = GetComponent<SpriteRenderer>();
            _baseScale = transform.localScale * _groundScale;
            transform.localScale = _baseScale;
            _phase = Random.value * Mathf.PI * 2f;

            // we script every bit of the motion; a dynamic body would fight the transform writes
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null) _rb.bodyType = RigidbodyType2D.Kinematic;

            // the magnet collects on proximity the instant it is enabled - hold it until the
            // pickup flight has actually landed
            _magnet = GetComponent<DroppedItemMagnet>();
            if (_magnet != null) _magnet.enabled = false;
        }

        private void Start()
        {
            var services = FXServices.Get();
            _player = services != null ? services.player : null;
            _ground = transform.position;
            StartCoroutine(Life());
        }

        private IEnumerator Life()
        {
            var tuning = Tuning;
            if (tuning == null) { Release(); yield break; }   // no rig in the scene: behave as before

            yield return Drop(tuning.drop);
            BuildGroundLayers(tuning.idle);

            // Key-pickup items (the wrench) are collected by the player's own pickup controller,
            // not by proximity - flying one into the player would leave it stuck to them forever.
            // They get the drop and the ground life, and simply wait.
            var def = _item != null ? _item.Item : null;
            if (def != null && !def.AutoCollects)
            {
                while (true) { Bob(tuning.idle); yield return null; }
            }

            yield return Rest(tuning.idle);
            yield return FlyToPlayer(tuning.pickup);
            Release();
        }

        // ── drop: hop AWAY from the player, land in dust, settle ────────────────────────

        private IEnumerator Drop(DropSettings s)
        {
            if (s == null) yield break;

            Vector2 away = _player != null
                ? (Vector2)(transform.position - _player.position)
                : Random.insideUnitCircle;
            if (away.sqrMagnitude < 0.001f) away = Vector2.right;
            away = Quaternion.Euler(0f, 0f, Random.Range(-s.spreadDegrees, s.spreadDegrees))
                * away.normalized;

            Vector3 from = transform.position;
            Vector3 land = from + (Vector3)(away * (s.hopDistance * Random.Range(0.65f, 1.35f)))
                + (Vector3)(Random.insideUnitCircle * 0.08f);

            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(0.05f, s.hopDuration));
                transform.position = Vector3.Lerp(from, land, t)
                    + Vector3.up * (Mathf.Sin(t * Mathf.PI) * s.hopHeight);
                yield return null;
            }

            _ground = land;
            FXAudio.Play(s.sfx, land);
            var puffer = FXServices.Get() != null ? FXServices.Get().puffer : null;
            if (puffer != null) puffer.PlayAt(land, s.landPuff, transform);

            float h = s.hopHeight;
            for (int i = 0; i < s.bounces; i++)
            {
                h *= 0.35f;
                float dur = Mathf.Max(0.02f, s.hopDuration * 0.4f);
                float bt = 0f;
                while (bt < 1f)
                {
                    bt = Mathf.Min(1f, bt + Time.deltaTime / dur);
                    float lift = Mathf.Sin(bt * Mathf.PI);
                    transform.position = land + Vector3.up * (lift * h);
                    float squash = lift * 0.25f;   // stretch mid-air, squash on touch-down
                    transform.localScale = new Vector3(
                        _baseScale.x * (1f - squash * 0.5f), _baseScale.y * (1f + squash), _baseScale.z);
                    yield return null;
                }
            }
            transform.localScale = _baseScale;
        }

        // ── rest: bob over a breathing shadow until the player comes near ───────────────

        /// <summary>The shadow is what makes a bobbing item read as ON the ground rather than
        /// floating. Both layers sort against the item itself, never at a fixed order.</summary>
        private void BuildGroundLayers(IdleSettings s)
        {
            if (s == null) return;
            int layer = _sr != null ? _sr.sortingLayerID : 0;
            int order = _sr != null ? _sr.sortingOrder : 0;

            var shadowGo = new GameObject("DropShadow");
            shadowGo.transform.position = _ground;
            _shadow = shadowGo.AddComponent<SpriteRenderer>();
            _shadow.sprite = FXSprites.SoftCircle;
            _shadow.color = new Color(0f, 0f, 0f, s.shadowAlpha);
            _shadow.sortingLayerID = layer;
            _shadow.sortingOrder = order - 1;

            if (!s.glow) return;
            var glowGo = new GameObject("DropGlow");
            glowGo.transform.SetParent(transform, false);
            glowGo.transform.localScale = Vector3.one * s.glowScale;
            _glow = glowGo.AddComponent<SpriteRenderer>();
            _glow.sprite = FXSprites.SoftCircle;
            _glow.sortingLayerID = layer;
            _glow.sortingOrder = order - 1;
        }

        private IEnumerator Rest(IdleSettings s)
        {
            float rested = 0f;
            float hold = -1f;   // >= 0 once the player is in range: this item's turn to leave
            while (true)
            {
                rested += Time.deltaTime;
                if (s != null) Bob(s);

                bool inRange = _player != null
                    && Vector3.Distance(transform.position, _player.position) <= _pickupRadius;

                if (hold < 0f && rested >= _minRestSeconds && inRange)
                    hold = Random.Range(0f, _pickupStagger);
                if (hold >= 0f)
                {
                    hold -= Time.deltaTime;
                    if (hold <= 0f) yield break;
                }

                yield return null;
            }
        }

        private void Bob(IdleSettings s)
        {
            float twoPi = Mathf.PI * 2f;
            float period = Mathf.Max(0.05f, s.bobPeriod);
            float w = Mathf.Sin(Time.time / period * twoPi + _phase);   // -1..1
            float rise = (w + 1f) * 0.5f;                               //  0..1

            transform.position = _ground + Vector3.up
                * (s.hoverHeight + w * s.bobAmplitude * Flynn.Feel.VibeTokens.MotionScale);
            transform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Sin(Time.time / period * twoPi * 0.7f + _phase) * s.swayDegrees);

            if (_shadow != null)
            {
                float shrink = 1f - s.shadowBreathe * rise;
                _shadow.transform.localScale =
                    new Vector3(s.shadowSize * shrink, s.shadowSize * s.shadowSquish * shrink, 1f);
                _shadow.color = new Color(0f, 0f, 0f,
                    s.shadowAlpha * (1f - 0.5f * s.shadowBreathe * rise));
            }

            if (_glow != null)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(
                    Time.time / Mathf.Max(0.05f, s.glowPulsePeriod) * twoPi + _phase);
                _glow.color = new Color(s.glowColor.r, s.glowColor.g, s.glowColor.b, s.glowAlpha * pulse);
            }
        }

        // ── pickup: anticipation, magnet flight, arrival ack on the player ─────────────

        private IEnumerator FlyToPlayer(PickupSettings s)
        {
            if (s == null || _player == null) yield break;

            Vector3 from = transform.position;
            float at = 0f;
            while (at < 1f)
            {
                at = Mathf.Min(1f, at + Time.deltaTime / 0.1f);
                transform.position = from + Vector3.up * (Mathf.Sin(at * Mathf.PI) * s.anticipation);
                yield return null;
            }

            from = transform.position;
            if (_shadow != null) Destroy(_shadow.gameObject);

            float t = 0f;
            while (t < 1f && _player != null)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(0.05f, s.flyDuration));
                float k = Mathf.Clamp01(s.flyEase != null && s.flyEase.length > 0
                    ? s.flyEase.Evaluate(t) : t);
                Vector3 target = _player.position + Vector3.up * 0.1f;
                transform.position = Vector3.Lerp(from, target, k)
                    + Vector3.up * (Mathf.Sin(k * Mathf.PI) * s.arcHeight);
                transform.localScale = _baseScale * (1f - 0.5f * k);
                yield return null;
            }
            if (_player == null) yield break;

            FXAudio.Play(s.sfx, _player.position);
            var squash = _player.GetComponentInChildren<SquashFX>();
            if (squash != null) squash.Pop(s.receiverPopScale, s.receiverPopDuration);
            var sparkler = FXServices.Get() != null ? FXServices.Get().sparkler : null;
            if (sparkler != null) sparkler.PlayAt(_player.position + Vector3.up * 0.25f, s.absorb);
        }

        /// <summary>Hand the item back to Runtime: the magnet now sees it sitting on the player
        /// and does the inventory add (or the currency routing) exactly as it always did.</summary>
        private void Release()
        {
            if (_glow != null) Destroy(_glow.gameObject);
            if (_shadow != null) Destroy(_shadow.gameObject);
            transform.localScale = _baseScale;
            if (_magnet != null) _magnet.enabled = true;
        }

        private void OnDestroy()
        {
            if (_shadow != null) Destroy(_shadow.gameObject);
        }
    }
}
