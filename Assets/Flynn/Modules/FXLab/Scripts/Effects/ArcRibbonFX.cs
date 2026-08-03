using PrimeTween;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Deterministic crescent slash: a parametric ribbon mesh swept around the character.
    ///
    /// Shape is BAKED once per play (<see cref="BakeShape"/>) and the sweep is a pure function of
    /// one head value (<see cref="WriteReveal"/>) writing vertex colours - so the silhouette is
    /// pixel-identical every swing and only the light walks along it. That is the difference from
    /// a TrailRenderer, whose shape is emitted from motion and therefore changes with framerate
    /// and with the character's own movement.
    ///
    /// Rig (the component's OWN transform is never moved, reparented or scaled - it may sit on a
    /// shared services object):
    ///     ArcRibbonRig   parented to the character, scale (1, groundSquash, 1)  = iso projection
    ///       Aim          rotated on Z to the ground-space aim
    ///         Ribbon     MeshFilter + MeshRenderer, offset by rig.pivotOffset
    /// The squash is the PARENT of the rotation, so the arc is drawn flat and then projected -
    /// squashing first and rotating after would shear the crescent instead of foreshortening it.
    /// </summary>
    public class ArcRibbonFX : MonoBehaviour
    {
        const int Rows = 4;   // outer edge, outer core, inner core, inner edge

        public FXLabTuning tuning;
        [Tooltip("Character the arc rides when the caller does not name one - i.e. when a swing " +
                 "arrives as a bare UnityEvent<Vector2>. Without it the arc plays where this " +
                 "component happens to sit, and cannot sort behind the character.")]
        public Transform defaultPivot;
        [Tooltip("Optional material. Empty = built at runtime from a URP 2D sprite-unlit shader, " +
                 "so this effect can never silently no-op on a missing asset reference.")]
        public Material materialOverride;

        Transform _rig, _aim, _ribbon;
        MeshRenderer _renderer;
        Mesh _mesh;
        Material _runtimeMat;
        Vector3[] _verts;
        Vector2[] _uvs;
        Color[] _colors;
        Color[] _baseColors;
        float[] _stationT;
        int _segments = -1;
        Tween _tween;

        // cached so the per-frame step allocates nothing
        ArcRibbonSettings _s;
        float _total, _windupFrac;

        /// <summary>Aim from a direction vector (recipe/moment path).</summary>
        public void Play(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
            PlayAtAngle(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, null, null);
        }

        /// <summary>
        /// Swing toward a single 0-360 facing angle, rigged on the character.
        /// </summary>
        /// <param name="facingDeg">Where the character is swinging. 0 = +X, counter-clockwise.
        /// Treated as a SCREEN angle unless the block says otherwise - see
        /// <see cref="ArcRibbonSettings.aimIsScreenSpace"/>.</param>
        /// <param name="pivot">Character transform to ride. Null = play at this object's position.</param>
        public void PlayAtAngle(float facingDeg, Transform pivot, SwingRigSettings rig)
        {
            if (tuning == null) return;
            var s = tuning.arcRibbon;
            rig = rig ?? tuning.swingRig;
            if (pivot == null) pivot = defaultPivot;
            EnsureRig();
            if (_tween.isAlive) _tween.Stop();

            _s = s;
            float k = Mathf.Clamp(s.groundSquash, 0.05f, 1f);

            if (pivot != null)
            {
                if (_rig.parent != pivot) _rig.SetParent(pivot, worldPositionStays: false);
                _rig.localPosition = Vector3.zero;   // origin ON the character
            }
            else
            {
                if (_rig.parent != transform) _rig.SetParent(transform, worldPositionStays: false);
                _rig.localPosition = Vector3.zero;
            }
            _rig.localRotation = Quaternion.identity;
            _rig.localScale = new Vector3(1f, k, 1f);

            // The rig squashes Y, so a rotation applied under it lands on screen at
            // atan2(k*sin a, cos a) - a 45deg aim reads as 27deg at k=0.5. Undo that first, so
            // the crescent points where the caller actually aimed.
            _aim.localRotation = Quaternion.Euler(0f, 0f, GroundAim(facingDeg, k, s.aimIsScreenSpace)
                + rig.angleOffsetDeg);
            _ribbon.localPosition = rig.pivotOffset;   // inside the aim frame: rotates WITH the swing

            ApplySorting(pivot, rig);
            FXAudio.Play(s.sfx, _rig.position);

            BakeShape(s, k);
            _renderer.enabled = true;

            // head runs past 1 by the tail length, so the last station finishes fading out
            float tail = Mathf.Max(0.01f, s.tailFrac);
            float sweep = Mathf.Max(0.01f, s.duration);
            _total = s.windup + sweep * (1f + tail);
            _windupFrac = s.windup / _total;
            WriteReveal(0f);

            _tween = Tween.Custom(this, 0f, 1f, _total, (fx, v) => fx.Step(v), ease: Ease.Linear)
                .OnComplete(this, fx => fx.Finish());
        }

        /// <summary>Screen angle -> ground angle, the inverse of the ellipse projection.</summary>
        static float GroundAim(float screenDeg, float k, bool convert)
        {
            if (!convert || k >= 0.999f) return screenDeg;
            float a = screenDeg * Mathf.Deg2Rad;
            return Mathf.Atan2(Mathf.Sin(a) / k, Mathf.Cos(a)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Sort RELATIVE to the character whenever there is one - layer included. The rig's
        /// absolute order is a lab convenience: in a Y-sorted world the character sits in the
        /// thousands, so an absolute 50 would bury the arc under the scenery. Falling back to it
        /// only when the arc has no character keeps the lab's flat scene behaving as dialed.
        /// </summary>
        void ApplySorting(Transform pivot, SwingRigSettings rig)
        {
            var psr = pivot != null ? pivot.GetComponentInChildren<SpriteRenderer>() : null;
            if (psr == null)
            {
                _renderer.sortingOrder = rig.sortingOrder;
                return;
            }
            _renderer.sortingLayerID = psr.sortingLayerID;
            _renderer.sortingOrder = psr.sortingOrder + (rig.behindPlayer ? -1 : 1);
        }

        void Step(float v)
        {
            if (_renderer == null || _s == null) return;
            if (v < _windupFrac) { WriteReveal(0f); return; }   // wind-up: the arc has not opened yet
            float p = (v - _windupFrac) / Mathf.Max(0.0001f, 1f - _windupFrac);
            float tail = Mathf.Max(0.01f, _s.tailFrac);
            WriteReveal(Eval(_s.sweepEase, p) * (1f + tail));
        }

        void Finish()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        /// <summary>
        /// Lay the crescent out in the aim's local frame: +X is the aim, the sweep runs from
        /// startDeg so that the station at <see cref="ArcRibbonSettings.contactAt"/> sits exactly
        /// on it - the arc opens before the aim and whips past it.
        /// </summary>
        void BakeShape(ArcRibbonSettings s, float k)
        {
            int seg = Mathf.Clamp(s.segments, 6, 64);
            EnsureBuffers(seg);

            float startDeg = -s.contactAt * s.sweepDeg;
            float halfW = Mathf.Max(0.001f, s.width) * 0.5f;
            float core = 1f - Mathf.Clamp(s.feather, 0f, 0.9f);

            for (int i = 0; i <= seg; i++)
            {
                float t = (float)i / seg;
                _stationT[i] = t;
                float phi = (startDeg + t * s.sweepDeg) * Mathf.Deg2Rad;
                float cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);

                // the parent squash foreshortens whatever is radial here, so a N/S station would
                // render thinner than an E/W one - divide it back out to keep one screen thickness
                float sq = Mathf.Max(0.05f, Mathf.Sqrt(cos * cos + k * k * sin * sin));
                float w = halfW * Eval(s.widthCurve, t) / sq;

                Vector3 c = new Vector3(cos, sin, 0f) * s.radius;
                Vector3 n = new Vector3(cos, sin, 0f);
                int b = i * Rows;
                _verts[b + 0] = c + n * w;
                _verts[b + 1] = c + n * (w * core);
                _verts[b + 2] = c - n * (w * core);
                _verts[b + 3] = c - n * w;
                for (int r = 0; r < Rows; r++)
                    _uvs[b + r] = new Vector2(t, r / (float)(Rows - 1));

                // colour along the arc; the feathered edge rows carry the soft falloff
                Color col = Color.Lerp(s.colorTail, s.colorHead, t);
                col.a *= s.alpha;
                _baseColors[b + 0] = Fade(col, 0f);
                _baseColors[b + 1] = col;
                _baseColors[b + 2] = col;
                _baseColors[b + 3] = Fade(col, 0f);
            }

            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            if (_segments != seg)
            {
                _mesh.SetTriangles(BuildTriangles(seg), 0, true);
                _segments = seg;
            }
            _mesh.RecalculateBounds();
        }

        /// <summary>Walk the lit window along the baked arc. Pure function of head - this is the
        /// whole animation.</summary>
        void WriteReveal(float head)
        {
            var s = _s;
            float tail = Mathf.Max(0.01f, s.tailFrac);
            float headW = Mathf.Max(0.01f, s.headWidth);
            float shape = Mathf.Max(0.1f, s.tailShape);
            int seg = _segments;

            for (int i = 0; i <= seg; i++)
            {
                float d = head - _stationT[i];
                float a;
                if (d < 0f || d >= tail) a = 0f;
                else a = Mathf.Pow(1f - d / tail, shape);

                // bright head just behind the tip - the contact accent, inside the silhouette
                float boost = d >= 0f && d < headW ? 1f + s.headBoost * (1f - d / headW) : 1f;

                int b = i * Rows;
                for (int r = 0; r < Rows; r++)
                {
                    Color c = _baseColors[b + r];
                    _colors[b + r] = new Color(c.r * boost, c.g * boost, c.b * boost, c.a * a);
                }
            }
            _mesh.SetColors(_colors);
        }

        static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        /// <summary>AnimationCurve fields added to an existing .asset deserialise EMPTY, and an
        /// empty curve evaluates to 0 - which would silently zero the width or the sweep. Fall
        /// back to linear whenever the curve has no keys.</summary>
        static float Eval(AnimationCurve c, float t) =>
            c == null || c.length == 0 ? t : c.Evaluate(t);

        void EnsureBuffers(int seg)
        {
            int count = (seg + 1) * Rows;
            if (_verts != null && _verts.Length == count) return;
            _verts = new Vector3[count];
            _uvs = new Vector2[count];
            _colors = new Color[count];
            _baseColors = new Color[count];
            _stationT = new float[seg + 1];
            _mesh.Clear(false);
            _segments = -1;
        }

        static int[] BuildTriangles(int seg)
        {
            var tris = new int[seg * (Rows - 1) * 6];
            int n = 0;
            for (int i = 0; i < seg; i++)
            {
                for (int r = 0; r < Rows - 1; r++)
                {
                    int a = i * Rows + r, b = a + 1;
                    int c = (i + 1) * Rows + r, d = c + 1;
                    tris[n++] = a; tris[n++] = b; tris[n++] = c;
                    tris[n++] = b; tris[n++] = d; tris[n++] = c;
                }
            }
            return tris;
        }

        void EnsureRig()
        {
            if (_rig != null) return;

            // built as a child so this component's own transform is never reparented or scaled -
            // FXServices provisions effects onto one shared object, and scaling that would drag
            // every other effect in the scene with it
            _rig = new GameObject("ArcRibbonRig").transform;
            _rig.SetParent(transform, worldPositionStays: false);
            _aim = new GameObject("Aim").transform;
            _aim.SetParent(_rig, worldPositionStays: false);

            var go = new GameObject("Ribbon");
            go.transform.SetParent(_aim, worldPositionStays: false);
            _ribbon = go.transform;

            _mesh = new Mesh { name = "ArcRibbon" };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = materialOverride != null ? materialOverride : RuntimeMaterial();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        /// <summary>Unlit sprite shader: multiplies by vertex colour (which is the whole effect)
        /// and honours sorting layers. No .mat asset to wire, so nothing to forget.</summary>
        Material RuntimeMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            _runtimeMat = new Material(sh) { name = "ArcRibbon (runtime)" };
            _runtimeMat.mainTexture = Texture2D.whiteTexture;
            return _runtimeMat;
        }

        void OnDisable()
        {
            if (_tween.isAlive) _tween.Stop();
            if (_renderer != null) _renderer.enabled = false;
        }

        void OnDestroy()
        {
            if (_rig != null) Destroy(_rig.gameObject);   // it lives on the character, not on us
            if (_mesh != null) Destroy(_mesh);
            if (_runtimeMat != null) Destroy(_runtimeMat);
        }
    }
}
