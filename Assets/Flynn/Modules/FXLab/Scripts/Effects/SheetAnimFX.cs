using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// One-shot frame animation from a grid sprite sheet (bought VFX packs), sliced at
    /// runtime via Sprite.Create - no importer slicing, no per-frame wiring: drag the
    /// texture into the tuning block, set row + frameCount, done. Frames are cached and
    /// rebuilt automatically when the tuning block changes (live scrubbing safe).
    /// Each Play spawns a throwaway sprite object, so overlapping plays don't cut
    /// each other off.
    /// </summary>
    public class SheetAnimFX : MonoBehaviour
    {
        public enum Block { Slash, Burst }

        public FXLabTuning tuning;
        [Tooltip("Which tuning block this instance reads: sheetSlash or sheetBurst.")]
        public Block block = Block.Slash;

        SheetFXSettings S => block == Block.Slash ? tuning.sheetSlash : tuning.sheetBurst;

        /// <summary>Play at this object's position, aimed along dir (slash variant).</summary>
        public void Play(Vector2 dir) => PlayAt(transform.position, dir);

        /// <summary>Slash with per-facing overrides: extra rotation, flip, position nudge,
        /// and sorting order on top of the block.</summary>
        public void Play(Vector2 dir, float extraAngleDeg, bool flipYExtra,
            Vector2 extraOffset, int sortOrder)
        {
            if (tuning == null) return;
            PlayAt(transform.position, dir, S, extraAngleDeg, flipYExtra, extraOffset, sortOrder);
        }

        /// <summary>Play at an arbitrary world position. dir = Vector2.zero for no rotation (bursts).</summary>
        public void PlayAt(Vector3 pos, Vector2 dir)
        {
            if (tuning == null) return;
            PlayAt(pos, dir, S);
        }

        /// <summary>
        /// Swing toward a single 0-360 facing angle, rigged on the character.
        ///
        /// A pivot object is parented to the character AT ITS ORIGIN and rotated on Z; the slash
        /// art hangs off that pivot at the rig's offset. So the angle orbits the art around the
        /// character instead of spinning it in place, and the frames stay upright relative to the
        /// swing rather than twisting - which is what the old three-direction setup needed
        /// per-direction offsets and flips to fake. The pivot is parented, so a swing that starts
        /// while the character is moving rides along with them.
        /// </summary>
        /// <param name="facingDeg">Where the character is swinging. 0 = +X, counter-clockwise.</param>
        /// <param name="pivot">Character transform to ride. Null = play at this object's position.</param>
        public void PlayAtAngle(float facingDeg, Transform pivot, SheetFXSettings s, SwingRigSettings rig)
        {
            if (s == null || rig == null) return;
            var frames = FXSheetFrames.Resolve(s);
            if (frames == null || frames.Length == 0) return;

            var root = new GameObject("SwingPivot");
            if (pivot != null)
            {
                root.transform.SetParent(pivot, worldPositionStays: false);
                root.transform.localPosition = Vector3.zero;   // origin ON the character
            }
            else root.transform.position = transform.position;
            root.transform.localRotation = Quaternion.Euler(0f, 0f, facingDeg + rig.angleOffsetDeg);

            FXAudio.Play(s.sfx, root.transform.position);

            // inside the rotated frame: +X is "forward along the swing", so forwardOffset pushes
            // the art out to arm's length and it sweeps round with the angle
            var local = new Vector3(s.forwardOffset, s.offsetY, 0f) + (Vector3)rig.pivotOffset;
            // the block's own art-correction angle, inside the rotated frame. It was dropped here
            // (hard-coded 0) while PlayAt applied it, so the panel's slash "angle" slider did
            // nothing on the aimed path.
            Spawn(s, frames, root.transform, local, s.angleOffsetDeg, s.flipY, SortOrder(pivot, rig), root);
        }

        static int SortOrder(Transform pivot, SwingRigSettings rig)
        {
            if (!rig.behindPlayer || pivot == null) return rig.sortingOrder;
            var psr = pivot.GetComponentInChildren<SpriteRenderer>();
            return (psr != null ? psr.sortingOrder : 0) - 1;
        }

        /// <summary>Play with an explicit settings block (composed moments pass per-material
        /// bursts). extraAngleDeg / flipYExtra / extraOffset / sortOrder = per-facing
        /// overrides on top of the block.</summary>
        public void PlayAt(Vector3 pos, Vector2 dir, SheetFXSettings s,
            float extraAngleDeg = 0f, bool flipYExtra = false,
            Vector2 extraOffset = default, int sortOrder = 50)
        {
            if (s == null) return;

            var frames = FXSheetFrames.Resolve(s);
            if (frames == null || frames.Length == 0) return;
            FXAudio.Play(s.sfx, pos);

            bool flipY = s.flipY ^ flipYExtra;
            pos += Vector3.up * s.offsetY + (Vector3)extraOffset;
            float angle = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                pos += (Vector3)(dir.normalized * s.forwardOffset);
                angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + s.angleOffsetDeg + extraAngleDeg;
            }
            Spawn(s, frames, null, pos, angle, flipY, sortOrder, null);
        }

        /// <summary>
        /// Build the renderer stack (main + glow + ghosts) and run the frame animation.
        /// Shared by both entry points: <paramref name="parent"/> null = <paramref name="place"/>
        /// is a world position, otherwise it is a local offset inside the parent's rotated frame.
        /// <paramref name="extraRoot"/> is destroyed alongside the art when the anim ends.
        /// </summary>
        void Spawn(SheetFXSettings s, Sprite[] frames, Transform parent, Vector3 place,
            float zAngle, bool flipY, int sortOrder, GameObject extraRoot)
        {
            var go = new GameObject("SheetAnim_" + block);
            if (parent != null)
            {
                go.transform.SetParent(parent, worldPositionStays: false);
                go.transform.localPosition = place;
                go.transform.localRotation = Quaternion.Euler(0f, 0f, zAngle);
            }
            else
            {
                go.transform.position = place;
                go.transform.rotation = Quaternion.Euler(0f, 0f, zAngle);
            }
            go.transform.localScale = Vector3.one * s.worldSize;
            var sr = go.AddComponent<SpriteRenderer>();
            if (frames[0] != null) sr.sprite = frames[0];
            sr.color = s.tint;
            sr.flipY = flipY;
            sr.sortingOrder = sortOrder;

            // layers: warm glow duplicate underneath + temporal ghost afterimages
            SpriteRenderer glowSr = null;
            if (s.glow)
            {
                var g = new GameObject("Glow");
                g.transform.SetParent(go.transform, false);
                g.transform.localScale = Vector3.one * s.glowScale;
                glowSr = g.AddComponent<SpriteRenderer>();
                glowSr.flipY = flipY;
                glowSr.sortingOrder = sortOrder - 1;
                glowSr.enabled = false;
            }
            SpriteRenderer[] ghosts = null;
            if (s.ghostCount > 0)
            {
                ghosts = new SpriteRenderer[s.ghostCount];
                for (int gi = 0; gi < s.ghostCount; gi++)
                {
                    var g = new GameObject("Ghost" + gi);
                    g.transform.SetParent(go.transform, false);
                    var gsr = g.AddComponent<SpriteRenderer>();
                    gsr.flipY = flipY;
                    gsr.sortingOrder = sortOrder - 2;
                    gsr.enabled = false;
                    ghosts[gi] = gsr;
                }
            }

            float duration = frames.Length / Mathf.Max(1f, s.fps);
            Tween.Custom(0f, 1f, duration, onValueChange: t =>
            {
                if (sr == null) return;
                int idx = Mathf.Min((int)(t * frames.Length), frames.Length - 1);
                if (frames[idx] != null) sr.sprite = frames[idx];   // tolerate empty array slots

                if (glowSr != null && frames[idx] != null)
                {
                    glowSr.sprite = frames[idx];
                    glowSr.color = new Color(s.glowTint.r, s.glowTint.g, s.glowTint.b, s.glowAlpha);
                    glowSr.enabled = true;
                }

                if (ghosts != null)
                {
                    float a = s.ghostTint.a;
                    for (int gi = 0; gi < ghosts.Length; gi++)
                    {
                        int gidx = idx - (gi + 1) * Mathf.Max(1, s.ghostFrameLag);
                        var gsr = ghosts[gi];
                        if (gsr == null) continue;
                        if (gidx < 0 || gidx >= frames.Length || frames[gidx] == null)
                        {
                            gsr.enabled = false;
                        }
                        else
                        {
                            gsr.sprite = frames[gidx];
                            // inherit the main tint so ghosts recolor with the effect
                            gsr.color = new Color(
                                s.ghostTint.r * s.tint.r,
                                s.ghostTint.g * s.tint.g,
                                s.ghostTint.b * s.tint.b, a);
                            gsr.enabled = true;
                        }
                        a *= s.ghostAlphaFalloff;
                    }
                }
            }, ease: Ease.Linear).OnComplete(() =>
            {
                if (extraRoot != null) Destroy(extraRoot);   // takes the art with it
                else if (go != null) Destroy(go);
            });
        }

    }
}
