using Flynn.Contracts;
using UnityEngine;
using UnityEngine.Events;

namespace Flynn.Modules.PlayerRig
{
    /// <summary>
    /// Player frame animator: picks a run or idle clip from the mover's facing + speed and
    /// steps sprite frames by time. Export set has front / back / front-45 directions only,
    /// so 45° (flipped) covers sides and down-diagonals and back (flipped) covers up-diagonals.
    /// When the mover reports SuppressLocomotion (e.g. Blown) it loops an idle instead of running.
    /// Swing clips play as one-shots via <see cref="PlaySwing"/> (F to test); onSwing fires for
    /// scene-level hookup (e.g. the harvest hitter). PlayerRig-owned, no cross-module dependency.
    /// </summary>
    public class PlayerAnimator : MonoBehaviour
    {
        [Tooltip("Mover component (must implement ICharacterMover). Auto-finds one in parents if empty.")]
        [SerializeField] MonoBehaviour moverSource;
        ICharacterMover mover;
        [Tooltip("Renderer to drive (auto: first SpriteRenderer in children).")]
        public SpriteRenderer target;
        public PlayerAnimSet set;

        [Tooltip("Below this speed (units/sec) the character is idle.")]
        public float moveThreshold = 0.15f;
        [Tooltip("The *_45 clips are drawn facing LEFT — untick if the art faces right.")]
        public bool fortyFiveFacesLeft = true;
        [Tooltip("Back clips only when facing.y exceeds this (movement mostly upward). " +
                 "Up-diagonals below it use the front-45 clips instead. Normalized facing: " +
                 "pure up = 1, up-diagonal ≈ 0.71.")]
        [Range(0.4f, 0.95f)] public float backThreshold = 0.75f;
        [Tooltip("Mouse button that swings toward the cursor (-1 to disable).")]
        public int swingMouseButton = 0;
        [Tooltip("One-shot swing test key, aims along movement facing (None to disable).")]
        public KeyCode swingTestKey = KeyCode.F;

        /// <summary>True while a swing one-shot is playing.</summary>
        public bool IsSwinging => _oneShot != null;

        /// <summary>Aim direction of the current (or last) swing, normalized.</summary>
        public Vector2 SwingDir { get; private set; } = Vector2.down;

        /// <summary>Normalized 0-1 progress of the swing one-shot; 0 when not swinging.</summary>
        public float SwingProgress => _oneShot == null ? 0f
            : Mathf.Clamp01(_t / _oneShot.frames.Length);

        [Tooltip("Fires when a swing one-shot starts, with the normalized aim. Scene-level hookup " +
                 "point (e.g. the swing hitter) — no code coupling.")]
        public UnityEvent<Vector2> onSwing = new UnityEvent<Vector2>();

        PlayerAnimSet.Clip _clip;      // current locomotion clip
        PlayerAnimSet.Clip _oneShot;   // swing overrides locomotion until it finishes
        float _t;
        bool _flip;

        void Awake()
        {
            mover = moverSource as ICharacterMover ?? GetComponentInParent<ICharacterMover>();
            if (target == null) target = GetComponentInChildren<SpriteRenderer>();
            if (mover != null) mover.ExternalSpriteDriver = true;
        }

        void OnDisable()
        {
            if (mover != null) mover.ExternalSpriteDriver = false;
        }

        void Update()
        {
            if (swingMouseButton >= 0 && Input.GetMouseButtonDown(swingMouseButton))
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector2 aim = cam.ScreenToWorldPoint(Input.mousePosition) - transform.position;
                    PlaySwing(aim);
                }
            }
            if (swingTestKey != KeyCode.None && Input.GetKeyDown(swingTestKey)) PlaySwing();
        }

        // after the mover moves/faces this frame
        void LateUpdate()
        {
            if (set == null || target == null || mover == null) return;

            if (_oneShot != null)
            {
                _t += Time.deltaTime * Mathf.Max(1f, _oneShot.fps);
                int i = (int)_t;
                if (i >= _oneShot.frames.Length) { _oneShot = null; _t = 0f; }
                else { Show(_oneShot, i); return; }
            }

            // Blown / no-locomotion states: ignore run clips, loop an idle instead.
            if (mover.SuppressLocomotion)
            {
                PickIdleClip(mover.Facing);
                if (_clip == null) _clip = set.Get("front_idle") ?? set.Get("run_front");
                if (_clip == null || _clip.frames == null || _clip.frames.Length == 0) return;
                _t += Time.deltaTime * Mathf.Max(1f, _clip.fps);
                Show(_clip, (int)_t % _clip.frames.Length);   // always loop
                return;
            }

            bool moving = mover.CurrentSpeed > moveThreshold;
            var prev = _clip;
            bool animate = moving;
            if (moving) PickRunClip(mover.Facing);
            else animate = PickIdleClip(mover.Facing);   // false = no idle art, freeze run pose
            if (_clip == null) _clip = set.Get("run_front");
            if (_clip == null || _clip.frames == null || _clip.frames.Length == 0) return;
            if (_clip != prev) _t = 0f;

            if (animate)
            {
                _t += Time.deltaTime * Mathf.Max(1f, _clip.fps);
                Show(_clip, _clip.loop ? (int)_t % _clip.frames.Length
                                       : Mathf.Min((int)_t, _clip.frames.Length - 1));
            }
            else
            {
                _t = 0f;                 // idle fallback: rest on the cycle's contact pose
                Show(_clip, 0);
            }
        }

        /// <summary>Swing once along movement facing, then return to locomotion.</summary>
        public void PlaySwing()
        {
            if (mover != null) PlaySwing(mover.Facing);
        }

        /// <summary>Swing once toward an arbitrary direction (e.g. the cursor).</summary>
        public void PlaySwing(Vector2 dir)
        {
            if (set == null || _oneShot != null || dir.sqrMagnitude < 0.0001f) return;
            var f = dir.normalized;
            PlayerAnimSet.Clip clip;
            if (f.y > backThreshold) { clip = set.Get("swing_back"); _flip = f.x > 0.05f; }
            else if (Mathf.Abs(f.x) < 0.35f) { clip = set.Get("swing_front"); _flip = false; }
            else { clip = set.Get("swing_45_front"); _flip = FlipFor45(f.x); }
            if (clip == null || clip.frames == null || clip.frames.Length == 0) return;
            _oneShot = clip;
            SwingDir = f;
            _t = 0f;
            onSwing.Invoke(f);
        }

        void PickRunClip(Vector2 f)
        {
            if (f.y > backThreshold)
            {
                _clip = set.Get("run_back");
                _flip = f.x > 0.05f;     // mirror for slight up-drift, neutral for pure up
            }
            else if (Mathf.Abs(f.x) < 0.35f)
            {
                _clip = set.Get("run_front");
                _flip = false;
            }
            else
            {
                _clip = set.Get("run_front_45");
                _flip = FlipFor45(f.x);
            }
        }

        // Same direction carve-up as the run clips; returns false when the idle clip
        // isn't in the set yet so the caller can fall back to the frozen run pose.
        bool PickIdleClip(Vector2 f)
        {
            PlayerAnimSet.Clip c;
            bool flip;
            if (f.y > backThreshold) { c = set.Get("back_idle"); flip = f.x > 0.05f; }
            else if (Mathf.Abs(f.x) < 0.35f) { c = set.Get("front_idle"); flip = false; }
            else { c = set.Get("45_idle"); flip = FlipFor45(f.x); }
            if (c == null || c.frames == null || c.frames.Length == 0) return false;
            _clip = c;
            _flip = flip;
            return true;
        }

        bool FlipFor45(float x) => fortyFiveFacesLeft ? x > 0f : x < 0f;

        void Show(PlayerAnimSet.Clip clip, int frame)
        {
            target.sprite = clip.frames[Mathf.Clamp(frame, 0, clip.frames.Length - 1)];
            target.flipX = _flip;
        }
    }
}
