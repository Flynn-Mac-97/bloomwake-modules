using Flynn.Contracts;
using UnityEngine;
using UnityEngine.Events;

namespace Flynn.Modules.PlayerRig
{
    /// <summary>
    /// Self-contained player mover. Legacy WASD/arrows, crisp accel/decel via MoveTowards
    /// (no SmoothDamp tail = little float, quick stop). Holds a debug <see cref="MoveState"/>
    /// that other scripts can read/set through <see cref="State"/>; each state reshapes how
    /// input becomes motion. Movement only — animation reads this via ICharacterMover, swing
    /// lives on its own components.
    /// </summary>
    public class PlayerMover : MonoBehaviour, ICharacterMover, ILiftProvider
    {
        public enum MoveState { Normal, Sliding, Blown, RunInPlace }

        [Header("State — debug dropdown")]
        [Tooltip("Live movement state. Set from other scripts via the State property; changing " +
                 "it here in play mode also fires StateChanged.")]
        [SerializeField] MoveState state = MoveState.Normal;

        [Header("Base movement")]
        [Tooltip("Top speed, world units/sec.")]
        public float maxSpeed = 1.6f;
        [Tooltip("Ramp-up, units/sec². High = snappy start.")]
        public float acceleration = 24f;
        [Tooltip("Ramp-down when no input, units/sec². Keep > acceleration for a crisp, " +
                 "slide-free stop.")]
        public float deceleration = 40f;

        [Header("Sliding — low grip, momentum carries")]
        [Tooltip("How much input steers on ice (0 = no control, 1 = normal).")]
        [Range(0f, 1f)] public float slideControl = 0.3f;
        [Tooltip("Ramp-down while sliding, units/sec². Low = long glide.")]
        public float slideDeceleration = 3f;
        [Tooltip("Debug: 'Slide Kick' button adds this much speed along facing so you can watch the glide.")]
        public float slideKickSpeed = 3f;
        [Tooltip("Subtle ice lean, max degrees. 2D-iso: keep small — full spins read wrong.")]
        public float slideTiltMax = 12f;

        [Header("Blown — shoved along a direction")]
        public Vector2 blowDirection = Vector2.right;
        [Tooltip("Constant push speed, units/sec.")]
        public float blowForce = 1.4f;
        [Tooltip("How much input fights the wind (0 = helpless, 1 = full control).")]
        [Range(0f, 1f)] public float blowControl = 0.35f;
        [Tooltip("Sprite tumble while blown, degrees. 2D-iso: keep subtle, no full spins.")]
        public float blowTiltAmplitude = 8f;
        [Tooltip("Tumble wobble speed.")]
        public float blowTiltSpeed = 6f;

        [Header("Run bounce — toony hop while walking")]
        [Tooltip("Hop height, world units.")]
        public float bounceHeight = 0.12f;
        [Tooltip("Steps per second — the bounce cadence. Crank up for a livelier toon hop.")]
        public float bounceStepsPerSecond = 2.5f;
        [Tooltip("Squash & stretch strength: squashes on landing, stretches at the peak. 0 = off.")]
        [Range(0f, 0.6f)] public float bounceSquash = 0.18f;
        [Tooltip("Arc shape: <1 hangs at the top (cartoony), 1 = round, >1 = pointy.")]
        [Range(0.4f, 1.5f)] public float bounceHang = 0.7f;
        [Tooltip("Slide the hop's timing relative to the step, 0..1 of a full bounce cycle. " +
                 "Use RunInPlace state to nudge this until the landing lands on the foot-contact frame.")]
        [Range(0f, 1f)] public float bouncePhaseOffset = 0f;
        [Tooltip("Fires once per hop at ground contact — wire the footstep sfx here.")]
        public UnityEvent onFootstep = new UnityEvent();

        [Header("Jump — Space, one big scaled-up bounce")]
        [Tooltip("Key that fires a one-shot jump.")]
        public KeyCode jumpKey = KeyCode.Space;
        [Tooltip("Peak height, world units — same arc as the run bounce, scaled up.")]
        public float jumpHeight = 0.6f;
        [Tooltip("Airtime, seconds (takeoff to land).")]
        public float jumpDuration = 0.5f;
        [Tooltip("Squash on takeoff/land, stretch at the peak. 0 = off.")]
        [Range(0f, 0.6f)] public float jumpSquash = 0.22f;
        [Tooltip("Arc shape: <1 hangs at the top, 1 = round, >1 = pointy.")]
        [Range(0.4f, 1.5f)] public float jumpHang = 0.9f;
        [Tooltip("Fires on takeoff — wire jump sfx / dust FX here.")]
        public UnityEvent onJump = new UnityEvent();

        [Header("Sprite (only used when no external animator)")]
        [Tooltip("Visual child to flip. Falls back to this transform's SpriteRenderer.")]
        public Transform visual;
        public Sprite spriteFront, spriteBack, spriteLeft;

        /// <summary>Code-side hook when the movement state changes.</summary>
        public event System.Action<MoveState> StateChanged;

        [Header("Events")]
        [Tooltip("Fires when the movement state changes — wire feedback / FX / other systems here.")]
        public UnityEvent<MoveState> onStateChanged = new UnityEvent<MoveState>();

        /// <summary>Live movement state. Assign to switch behaviour; raises the state events.</summary>
        public MoveState State
        {
            get => state;
            set { if (state == value) return; state = value; _lastState = value; RaiseState(value); }
        }

        void RaiseState(MoveState s)
        {
            StateChanged?.Invoke(s);
            onStateChanged.Invoke(s);
        }

        // ICharacterMover
        public Vector2 Facing { get; private set; } = Vector2.down;
        // RunInPlace reports a fake speed so the animator plays the run cycle even though we don't move.
        public float CurrentSpeed => state == MoveState.RunInPlace ? maxSpeed : _velocity.magnitude;
        public bool ExternalSpriteDriver { get; set; }
        public bool SuppressLocomotion => state == MoveState.Blown;

        // ILiftProvider — how far the visual is currently lifted off the ground (hop + jump), world units.
        public float Lift { get; private set; }

        /// <summary>Debug: shove the character along its facing so Sliding's glide is visible.</summary>
        [ContextMenu("Sliding: Kick Forward")]
        public void SlideKick() => _velocity += Facing * slideKickSpeed;

        [Header("Collision")]
        [Tooltip("Layers that block the player (assign the platform-border layer).")]
        [SerializeField] LayerMask blockers;
        [Tooltip("Player collision radius in world units for the movement cast.")]
        [SerializeField] float collideRadius = 0.4f;
        [Tooltip("Small gap kept between the player and the wall.")]
        [SerializeField] float collideSkin = 0.02f;
        [Tooltip("Cast origin offset from the pivot down to the feet — collision is sensed here.")]
        [SerializeField] Vector2 footOffset = Vector2.zero;

        SpriteRenderer _renderer;
        Vector2 _velocity;
        MoveState _lastState;
        bool _rotated;
        float _slideTilt;
        float _bouncePhase;
        int _stepIndex;
        bool _jumping;
        float _jumpT;
        float _visualBaseY;
        Vector3 _visualBaseScale = Vector3.one;

        void Awake()
        {
            _renderer = visual != null ? visual.GetComponent<SpriteRenderer>()
                                       : GetComponent<SpriteRenderer>();
            if (visual != null) { _visualBaseY = visual.localPosition.y; _visualBaseScale = visual.localScale; }
            _lastState = state;
        }

        // Cast-and-clamp the intended move against blocker layers; slide along the hit edge instead
        // of stopping dead so movement stays smooth. No Rigidbody2D — pure scene query on the
        // transform mover. Two passes resolve sliding into a corner.
        Vector2 CollideAndSlide(Vector2 start, Vector2 delta)
        {
            Vector2 pos = start;
            for (int i = 0; i < 3; i++)
            {
                float dist = delta.magnitude;
                if (dist < 1e-5f) break;
                Vector2 dir = delta / dist;
                var hit = Physics2D.CircleCast(pos, collideRadius, dir, dist, blockers);
                if (hit.collider == null) { pos += delta; break; }

                // Already touching this edge and heading away from it (wedged in a corner) — don't
                // clamp us in place; let the escape through.
                if (hit.distance <= collideSkin && Vector2.Dot(dir, hit.normal) > 0f) { pos += delta; break; }

                float allowed = Mathf.Max(0f, hit.distance - collideSkin);
                pos += dir * allowed;
                Vector2 leftover = delta - dir * allowed;
                delta = leftover - Vector2.Dot(leftover, hit.normal) * hit.normal;   // tangent slide
            }
            return pos - start;
        }

        void Update()
        {
            // inspector dropdown change during play -> fire events once
            if (state != _lastState) { _lastState = state; RaiseState(state); }

            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();
            bool hasInput = input.sqrMagnitude > 0.01f;

            if (Input.GetKeyDown(jumpKey) && !_jumping && state != MoveState.Blown)
            {
                _jumping = true;
                _jumpT = 0f;
                onJump.Invoke();
            }

            Vector2 target;
            float rate;
            switch (state)
            {
                case MoveState.Sliding:
                    target = input * (maxSpeed * slideControl);
                    rate = hasInput ? acceleration * slideControl : slideDeceleration;
                    break;
                case MoveState.Blown:
                    target = input * (maxSpeed * blowControl) + blowDirection.normalized * blowForce;
                    rate = acceleration;
                    break;
                case MoveState.RunInPlace:
                    target = Vector2.zero;   // preview: run cycle + bounce, but stay put
                    rate = deceleration;
                    break;
                default: // Normal
                    target = input * maxSpeed;
                    rate = hasInput ? acceleration : deceleration;
                    break;
            }

            _velocity = Vector2.MoveTowards(_velocity, target, rate * Time.deltaTime);
            Vector2 move = _velocity * Time.deltaTime;
            if (blockers.value != 0) move = CollideAndSlide((Vector2)transform.position + footOffset, move);
            transform.position += (Vector3)move;

            if (hasInput)
            {
                Facing = input.normalized;
                if (_renderer != null && !ExternalSpriteDriver) UpdateSprite(input);
            }

            // Blown faces where the wind pushes it, so the animator swaps to that direction's pose.
            if (state == MoveState.Blown && blowDirection.sqrMagnitude > 0.0001f)
                Facing = blowDirection.normalized;

            // State-driven sprite rotation (reset once when back to a still state).
            if (visual != null)
            {
                if (state == MoveState.Sliding)
                {
                    // subtle lean into the slide (bounded, smoothed) — no full rotation
                    float leanTarget = -Mathf.Clamp(_velocity.x / Mathf.Max(0.01f, maxSpeed), -1f, 1f) * slideTiltMax;
                    _slideTilt = Mathf.Lerp(_slideTilt, leanTarget, 1f - Mathf.Exp(-8f * Time.deltaTime));
                    visual.localRotation = Quaternion.Euler(0f, 0f, _slideTilt);
                    _rotated = true;
                }
                else if (state == MoveState.Blown)
                {
                    float t = Time.time * blowTiltSpeed;
                    float ang = (Mathf.Sin(t) + 0.4f * Mathf.Sin(t * 2.3f)) * blowTiltAmplitude;
                    visual.localRotation = Quaternion.Euler(0f, 0f, ang);
                    _rotated = true;
                }
                else if (_rotated)
                {
                    visual.localRotation = Quaternion.identity;
                    _slideTilt = 0f;
                    _rotated = false;
                }

                // Toony run bounce: crisp ballistic hop + squash/stretch, only while walking (Normal).
                // Applied DIRECTLY (no smoothing) so the cadence + pop actually read; eases back on stop.
                float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
                var lp = visual.localPosition;
                bool bouncing = (state == MoveState.Normal || state == MoveState.RunInPlace)
                                && CurrentSpeed > 0.05f;
                if (_jumping)
                {
                    // One big scaled-up bounce arc: same shape as the run hop, longer + taller.
                    _jumpT += Time.deltaTime / Mathf.Max(0.05f, jumpDuration);
                    if (_jumpT >= 1f) { _jumping = false; _jumpT = 0f; }
                    float arc = _jumping ? Mathf.Pow(Mathf.Sin(_jumpT * Mathf.PI), jumpHang) : 0f;
                    lp.y = _visualBaseY + arc * jumpHeight;
                    float js = Mathf.Lerp(-jumpSquash, jumpSquash, arc);
                    visual.localScale = new Vector3(_visualBaseScale.x * (1f - js * 0.6f),
                                                    _visualBaseScale.y * (1f + js),
                                                    _visualBaseScale.z);
                    _bouncePhase = 0f;   // land clean, no leftover run-hop phase
                }
                else if (bouncing)
                {
                    _bouncePhase += bounceStepsPerSecond * Time.deltaTime;
                    float ph = _bouncePhase + bouncePhaseOffset;
                    int si = Mathf.FloorToInt(ph);
                    if (si != _stepIndex) { _stepIndex = si; onFootstep.Invoke(); }  // ground contact
                    float p = ph - Mathf.Floor(ph);                              // 0..1 within a step
                    float arc = Mathf.Pow(Mathf.Sin(p * Mathf.PI), bounceHang);   // hang-y top = toony
                    lp.y = _visualBaseY + arc * bounceHeight;
                    float stretch = Mathf.Lerp(-bounceSquash, bounceSquash, arc); // squash on ground, stretch at peak
                    visual.localScale = new Vector3(_visualBaseScale.x * (1f - stretch * 0.6f),
                                                    _visualBaseScale.y * (1f + stretch),
                                                    _visualBaseScale.z);
                }
                else
                {
                    _bouncePhase = 0f;
                    _stepIndex = Mathf.FloorToInt(bouncePhaseOffset);  // arm next contact, no step on start
                    lp.y = Mathf.Lerp(lp.y, _visualBaseY, k);
                    visual.localScale = Vector3.Lerp(visual.localScale, _visualBaseScale, k);
                }
                visual.localPosition = lp;
                Lift = lp.y - _visualBaseY;   // publish for ground FX (blob shadow)
            }
        }

        // Visualize the collision probe (foot point + cast radius) so footOffset/collideRadius can be
        // tweaked by eye in the Scene view. Green = the CircleCast origin used in CollideAndSlide.
        void OnDrawGizmosSelected()
        {
            Vector3 foot = transform.position + (Vector3)footOffset;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(foot, collideRadius);
            Gizmos.DrawLine(foot + Vector3.left * 0.06f, foot + Vector3.right * 0.06f);
            Gizmos.DrawLine(foot + Vector3.down * 0.06f, foot + Vector3.up * 0.06f);
        }

        void UpdateSprite(Vector2 input)
        {
            if (Mathf.Abs(input.x) > Mathf.Abs(input.y))
            {
                _renderer.sprite = spriteLeft;
                _renderer.flipX = input.x > 0f;
            }
            else
            {
                _renderer.flipX = false;
                _renderer.sprite = input.y > 0f ? spriteBack : spriteFront;
            }
        }
    }
}
