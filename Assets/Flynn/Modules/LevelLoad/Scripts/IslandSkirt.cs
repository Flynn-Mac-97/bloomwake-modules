using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace Flynn.Environment
{
    /// <summary>
    /// Fakes 3D thickness for a floating island in 2D isometric view by
    /// hanging procedural cliff strips from the downward-facing edges of the
    /// island contour, baking a rounded-lip falloff into the top fill, and
    /// growing a grass-blade fringe over the lip.
    ///
    /// Every visual knob lives in an <see cref="IslandVisualProfile"/> asset
    /// — swap the profile to restyle the island live. All materials are
    /// runtime instances built from the profile's shaders (no per-profile
    /// material assets); profile values are pushed every LateUpdate, and a
    /// profile edit/swap triggers a full regen.
    ///
    /// Samples the SpriteShapeController spline with the true cubic bezier
    /// SpriteShape renders, so the skirt hugs the visible edge exactly.
    /// <see cref="TilemapToSpriteShape"/> supplies the regen trigger
    /// (event-driven — painting the tilemap regenerates the skirt) and a
    /// last-resort polygon fallback.
    ///
    /// Geometry: contour → edges whose outward normal points down become
    /// cliff chains → each chain extrudes a multi-row strip whose horizontal
    /// profile follows the profile's AnimationCurve (bulge/taper by depth).
    /// The top row tucks slightly inside the island silhouette (the island
    /// fill draws over it, so no seam-matching is needed); lower rows are
    /// displaced by Perlin noise along the contour normal and in depth for
    /// an eroded edge. Strips taper to nothing at chain ends — no caps, no
    /// polygon triangulation — and quads are painter-sorted back-to-front by
    /// top-edge Y so staircase folds resolve correctly.
    ///
    /// Rendering: child MeshFilter/MeshRenderer marked HideFlags.DontSave
    /// (nothing procedural is serialized into the scene), sorted behind the
    /// source renderer, using the profile's URP 2D-lit shaders so the cliff
    /// reacts to Light2D like every sprite around it.
    /// </summary>
    [ExecuteAlways]
    public class IslandSkirt : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Boundary provider. If unassigned, looked up on this GameObject.")]
        [SerializeField] private TilemapToSpriteShape _contourSource;
        [Tooltip("Fallback when no TilemapToSpriteShape: sample this spline directly.")]
        [SerializeField] private SpriteShapeController _splineSource;

        [Header("Profile")]
        [Tooltip("All visual knobs live here — swap the asset to restyle the island live. Empty = built-in defaults.")]
        [SerializeField] private IslandVisualProfile _profile;

        // Sorting note: the skirt draws as ONE merged mesh and belongs to the island BASE, which the
        // player always walks on top of — so it needs no per-piece sorting. Raised platforms are not
        // drawn here at all; Flynn.Modules.PlatformTiles emits one sprite per cell, which is what
        // lets the player sort BETWEEN them. The old opt-in facing/depth-band/column-slice paths were
        // attempts at that with a single mesh and are gone.

        private const float DownFacingThreshold = -0.25f; // outward normal.y below this grows a cliff
        private const string ChildName = "Skirt";
        private const string FringeChildName = "Fringe";
        private const string ShaderName = "Flynn/IslandSkirt";
        private const string SpriteShaderName = "Flynn/IslandSkirtSprite";
        private const string PlatformShaderName = "Flynn/PlatformSkirt";
        private const string ShingleShaderName = "Flynn/ShinglePlane";
        private const string FillShaderName = "Flynn/IslandTopFill";
        private const string FringeShaderName = "Flynn/IslandFringe";

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _runtimeMaterial;
        private Texture2D _falloffTex;
        private Material _fillRuntimeMaterial;
        private MeshFilter _fringeFilter;
        private MeshRenderer _fringeRenderer;
        private Mesh _fringeMesh;
        private Material _fringeRuntimeMaterial;
        private MeshFilter _decalFilter;
        private MeshRenderer _decalRenderer;
        private Mesh _decalMesh;
        private Material _decalRuntimeMaterial;
        private Vector4 _falloffRectCached;
        private MaterialPropertyBlock _skirtMpb;
        private IslandVisualProfile _defaultProfile;
        private IslandVisualProfile _appliedProfile;
        private int _appliedVersion = -1;
        private TilemapToSpriteShape _subscribed;
        private bool _generating;

        /// <summary>
        /// Never-null profile: the assigned asset, or a hidden instance whose
        /// field initializers are the built-in defaults — every code path
        /// reads through here, zero scattered null checks.
        /// </summary>
        private IslandVisualProfile Profile
        {
            get
            {
                if (_profile != null) return _profile;
                if (_defaultProfile == null)
                {
                    _defaultProfile = ScriptableObject.CreateInstance<IslandVisualProfile>();
                    _defaultProfile.hideFlags = HideFlags.HideAndDontSave;
                }
                return _defaultProfile;
            }
        }

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        private void OnEnable()
        {
            ResolveSources();
            Subscribe();
            Generate();
        }

        private void LateUpdate()
        {
            // Profile swap or inspector edit (Version bump) → regen; always
            // push values onto the runtime materials + per-renderer blocks
            // (live-follows the island tint and profile sliders; nothing
            // here dirties assets).
            var prof = Profile;
            if (_appliedProfile != prof || _appliedVersion != prof.Version)
            {
                _appliedProfile = prof;
                _appliedVersion = prof.Version;
                if (_contourSource != null) _contourSource.ApplyContourSettings(prof.contour);
                Generate();
            }
            SyncMaterials();
        }

        private void OnDisable()
        {
            if (_subscribed != null)
            {
                _subscribed.ContourChanged -= OnSourceContourChanged;
                _subscribed = null;
            }
        }

        private void OnDestroy()
        {
            var mine = new List<Material>();
            foreach (var kv in RuntimeMatClaims)
                if (kv.Value == this) mine.Add(kv.Key);
            foreach (var m in mine) RuntimeMatClaims.Remove(m);

            DestroyHelper(_runtimeMaterial);
            DestroyHelper(_mesh);
            if (_meshFilter != null) DestroyHelper(_meshFilter.gameObject);
            DestroyHelper(_fringeRuntimeMaterial);
            DestroyHelper(_fringeMesh);
            if (_fringeFilter != null) DestroyHelper(_fringeFilter.gameObject);
            DestroyHelper(_decalRuntimeMaterial);
            DestroyHelper(_decalMesh);
            if (_decalFilter != null) DestroyHelper(_decalFilter.gameObject);
            DestroyHelper(_fillRuntimeMaterial);
            DestroyHelper(_falloffTex);
            DestroyHelper(_defaultProfile);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Defer: creating GameObjects / touching renderers inside
            // OnValidate triggers warnings and misbehaves in prefab isolation.
            // Runs in play mode too — inspector tweaks regenerate live.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled) return;
                ResolveSources();
                Subscribe();
                Generate();
            };
        }
#endif

        // ---------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------

        /// <summary>
        /// The assigned profile asset (null = built-in defaults). Settable so
        /// TilemapToSpriteShape can hand the parent island's profile to the
        /// procedural isle children it spawns for disconnected blobs.
        /// </summary>
        public IslandVisualProfile ProfileAsset
        {
            get => _profile;
            set => _profile = value;
        }

        /// <summary>
        /// Absolute hang depth override in world units, REPLACING the profile's
        /// thickness / shingle height. Set by JsonMapLoader on elevated style
        /// groups to exactly height × heightStep — the profile's thickness is
        /// the BASE island's outer-void cliff and has nothing to do with how
        /// far a terrace must drop to meet the ground below. Negative = no
        /// override (profile depth). SERIALIZED so a loader-set terrace depth
        /// survives domain reload / play (the skirt regenerates at runtime and
        /// must keep the right drop instead of reverting to the base thickness).
        /// </summary>
        public float DepthOverride = -1f;

        /// <summary>
        /// World height of ONE texture band on the cliff wall, for shaders that repeat their
        /// texture down the rim instead of stretching one copy over it (Flynn/PlatformSkirt).
        /// Set it to the level grid step and every platform wears rock at the same texel
        /// density regardless of how many steps tall it is. SERIALIZED for the same reason as
        /// <see cref="DepthOverride"/>. Negative = no banding, one copy over the whole wall.
        /// </summary>
        public float RimBandHeight = -1f;

        /// <summary>
        /// Per-point depth sampler (world position → hang depth, world units) —
        /// supersedes <see cref="DepthOverride"/>. JsonMapLoader supplies one on
        /// terrace groups so each cliff edge drops exactly to the layer directly
        /// below THAT edge (a z3 plateau ringed by z2 steps 1, its base-facing
        /// edges step 3). Not serialized — owners reapply.
        /// </summary>
        [System.NonSerialized] public System.Func<Vector3, float> DepthSampler;

        /// <summary>
        /// Per-column fringe gate (contour world position, outward world dir →
        /// true = suppress the lip here). PaintedStyleLayer supplies one so a
        /// lower terrace's fringe never pokes out of the wall of a platform
        /// sitting directly on its edge. Not serialized — owners reapply.
        /// </summary>
        [System.NonSerialized] public System.Func<Vector3, Vector3, bool> FringeMask;

        /// <summary>Rebuild the skirt mesh from the current contour. Safe at runtime.</summary>
        [ContextMenu("Generate Skirt")]
        public void Generate()
        {
            if (_generating) return; // re-entry guard: pulling the source fires ContourChanged
            _generating = true;
            try { GenerateInternal(); }
            finally { _generating = false; }
        }

        // ---------------------------------------------------------------------
        // Generation
        // ---------------------------------------------------------------------

        private void GenerateInternal()
        {
            var prof = Profile;
            EnsureRenderer();
            EnsureFillMaterial();
            if (_mesh == null) return;

            var ring = BuildRing();
            if (ring == null || ring.Count < 8)
            {
                _mesh.Clear();
                if (_fringeMesh != null) _fringeMesh.Clear();
                return;
            }

            int n = ring.Count;
            var segLen = new float[n]; // arc length ring[i] → ring[i+1]
            for (int i = 0; i < n; i++)
                segLen[i] = Vector3.Distance(ring[i], ring[(i + 1) % n]);

            var outward = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = ring[(i + 1) % n] - ring[(i - 1 + n) % n];
                var o = new Vector2(t.y, -t.x); // CCW ring → outward normal
                outward[i] = o.sqrMagnitude > 1e-8f ? o.normalized : Vector2.down;
            }

            var down = MarkDownFacing(outward, segLen, prof.skirtShape.bridgeGaps);
            var chains = ExtractChains(down, out bool closedRing);

            // Closed ring: the seam (chain start/end) lands wherever the contour
            // trace happened to begin. Rotate it to the rightmost point so any
            // residual seam sits edge-on at the far right corner, out of the
            // front view — matters most on very small tiles.
            if (closedRing)
                foreach (var c in chains)
                {
                    if (c.Count < 3) continue;
                    int best = 0;
                    for (int i = 1; i < c.Count; i++)
                        if (ring[c[i]].x > ring[c[best]].x) best = i;
                    if (best == 0) continue;
                    var rot = new List<int>(c.Count);
                    for (int i = 0; i < c.Count; i++) rot.Add(c[(best + i) % c.Count]);
                    c.Clear();
                    c.AddRange(rot);
                }

            // Bulge directions = outward normals smoothed over an arc window,
            // so the profile pushes out from whichever side the edge faces
            // (concave bays included) while sharp corners fan instead of
            // converging into self-intersections. Residual pinches are fixed
            // by the weld-relax pass in BuildStrip.
            var bulgeDir = SmoothedOutward(outward, segLen, prof.skirtShape.bulgeSmoothing);

            var verts  = new List<Vector3>();
            var uvs    = new List<Vector2>();
            var uvs1   = new List<Vector2>(); // surface space: arc, world depth
            var uvs2   = new List<Vector2>(); // smoothed outward normal (facing)
            var colors = new List<Color>();
            var quads  = new List<SkirtQuad>();

            // Top-edge Y bounds for depth-layer shading.
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in ring)
            {
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            float invYRange = maxY > minY ? 1f / (maxY - minY) : 0f;

            // No feather collar: the grass lip is the fringe mesh (below),
            // so the skirt never insets its top row — arbitrary-width
            // inward offsets invert at concave corners.
            bool shingles = prof.skirtSprite.useShinglePlanes;
            _tracedDepth = -1f; // rebuilt by the chain builders below
            foreach (var chain in chains)
            {
                if (shingles)
                    BuildShingles(chain, closedRing, ring, verts, uvs, uvs1, colors, quads);
                else
                    BuildStrip(chain, closedRing, ring, outward, bulgeDir, minY, invYRange,
                        verts, uvs, uvs1, uvs2, colors, quads);
            }

            // Painter's sort. The mesh is transparent (no ZWrite), so
            // overlapping quads at staircase folds resolve by index order.
            // Emit back-to-front by top-edge Y: lower = nearer in the iso
            // illusion = drawn last, regardless of chain walk direction.
            quads.Sort((a, b) =>
            {
                int c = b.Key.CompareTo(a.Key);
                return c != 0 ? c : a.Seq.CompareTo(b.Seq);
            });
            var tris = new List<int>(quads.Count * 6);
            foreach (var q in quads)
            {
                tris.Add(q.Pt); tris.Add(q.Ct); tris.Add(q.Pb);
                tris.Add(q.Ct); tris.Add(q.Cb); tris.Add(q.Pb);
            }

            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetUVs(0, uvs);
            if (uvs1.Count == verts.Count) _mesh.SetUVs(1, uvs1);
            if (uvs2.Count == verts.Count) _mesh.SetUVs(2, uvs2);
            _mesh.SetColors(colors);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();

            // --- Top falloff: rounded-edge shading baked into the fill ---
            if (prof.fill.falloffEnabled)
                BakeTopFalloff(ring);
            else
                NeutralizeTopFalloff();

            // --- Grass fringe: blade strip overhanging the lip ---
            if (prof.fringe.enabled)
                BuildFringe(ring, outward, segLen);
            else if (_fringeMesh != null)
                _fringeMesh.Clear();

            // --- Decal cards: scatter vines/bushes to break the silhouette ---
            if (prof.decals.enabled)
                BuildDecals(ring, outward, segLen, down);
            else if (_decalMesh != null)
                _decalMesh.Clear();

            SyncMaterials();

        }

        /// <summary>
        /// Blade strip hugging the island lip. Inner edge roots over the top
        /// fill, outer edge reaches past the silhouette and droops down
        /// everywhere — over cliffs it hands the lip to the skirt, on the
        /// far (up-facing) rim the reach is additionally folded flat so the
        /// blades curl over the edge instead of floating up past the
        /// silhouette. The hand-painted overhang look. Drawn above the fill, so the fill
        /// needs no blade cutout (which visibly inset the grass) and the
        /// skirt needs no collar (which inverted at concave corners).
        /// </summary>
        private void BuildFringe(List<Vector3> ring, Vector2[] outward, float[] segLen)
        {
            var f = Profile.fringe;
            EnsureFringeRenderer();
            if (_fringeMesh == null) return;
            int n = ring.Count;

            var dir = SmoothedOutward(outward, segLen, f.smoothing);

            // u snapped to whole strip repeats over the perimeter (seamless
            // loop). The first column is duplicated at u = repeats instead
            // of wrapping a quad back to u = 0, which would smear the whole
            // strip across one segment.
            float perimeter = 0f;
            for (int i = 0; i < n; i++) perimeter += segLen[i];
            float repeats = Mathf.Max(1, Mathf.RoundToInt(perimeter / Mathf.Max(f.texScale, 0.01f)));
            float uScale = repeats / Mathf.Max(perimeter, 1e-4f);

            int rows = Mathf.Max(1, f.rows);
            int stride = rows + 1;
            int cols = n + 1; // duplicated seam column
            var verts = new List<Vector3>(cols * stride);
            var uvs = new List<Vector2>(cols * stride);
            var uvs1 = new List<Vector2>(cols * stride); // x = 0 root -> 1 tip
            var colors = new List<Color>(cols * stride);

            float arc = 0f;
            for (int c = 0; c < cols; c++)
            {
                int i = c % n;
                if (c > 0) arc += segLen[c - 1];
                float u = arc * uScale;

                // Up-facing (far) edges: fold the upward reach flat so the
                // strip lies over the rim instead of floating up past the
                // silhouette — iso foreshortening, the far lip recedes.
                Vector2 lat = dir[i];
                if (lat.y > 0f) lat.y *= 1f - f.upFold;
                // 0 = hanging/side edge (full droop), 1 = clearly up-facing
                float upBlend = Mathf.Clamp01(lat.y * 4f);

                // Masked columns collapse onto the ring (zero reach, alpha 0):
                // quads degenerate there, neighbours taper in — no index surgery.
                bool colMasked = FringeMask != null && FringeMask(
                    transform.TransformPoint(ring[i]),
                    transform.TransformDirection(new Vector3(dir[i].x, dir[i].y, 0f)));

                for (int r = 0; r <= rows; r++)
                {
                    float fr = (float)r / rows; // 0 = rooted inner edge, 1 = blade tip
                    float lateral = Mathf.Lerp(-f.innerOverlap, f.reach, fr);
                    // Quadratic gravity droop. On up-facing edges it is
                    // CLAMPED to the height the blade has risen: tips arc
                    // out over the rim and curve back down exactly to the
                    // silhouette — rounded off the edge, but never below it
                    // (below = onto the island top, which draws in front of
                    // the far rim in the iso illusion).
                    float droopDesired = f.droop * fr * fr;
                    float arcDroop = Mathf.Min(droopDesired, Mathf.Max(0f, lat.y * lateral));
                    float droop = Mathf.Lerp(droopDesired, arcDroop, upBlend);
                    verts.Add(colMasked
                        ? ring[i]
                        : ring[i] + (Vector3)(lat * lateral) + Vector3.down * droop);
                    uvs.Add(new Vector2(u, Mathf.Lerp(f.vInner, f.vOuter, fr)));
                    uvs1.Add(new Vector2(fr, 0f));
                    float darken = 1f - f.tipDarken * fr;
                    colors.Add(new Color(
                        f.tint.r * darken, f.tint.g * darken, f.tint.b * darken,
                        colMasked ? 0f : f.tint.a));
                }
            }

            // Outward offsets can still cross at very tight concave corners —
            // weld inverted row segments to their midpoint (same trick as
            // RelaxRowInversions; the rooted inner row stays pinned).
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int c = 0; c < cols - 1; c++)
                {
                    Vector3 orig = ring[(c + 1) % n] - ring[c % n];
                    for (int r = 1; r <= rows; r++)
                    {
                        int a = c * stride + r, b = (c + 1) * stride + r;
                        Vector3 cur = verts[b] - verts[a];
                        if (cur.x * orig.x + cur.y * orig.y < 0f)
                        {
                            Vector3 mid = (verts[a] + verts[b]) * 0.5f;
                            verts[a] = mid;
                            verts[b] = mid;
                            moved = true;
                        }
                    }
                }
                if (!moved) break;
            }

            var tris = new List<int>((cols - 1) * rows * 6);
            for (int c = 0; c < cols - 1; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    int a = c * stride + r, b = (c + 1) * stride + r;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(b); tris.Add(b + 1); tris.Add(a + 1);
                }
            }

            _fringeMesh.Clear();
            _fringeMesh.SetVertices(verts);
            _fringeMesh.SetUVs(0, uvs);
            _fringeMesh.SetUVs(1, uvs1);
            _fringeMesh.SetColors(colors);
            _fringeMesh.SetTriangles(tris, 0);
            _fringeMesh.RecalculateBounds();
        }

        // ---------------------------------------------------------------------
        // Material sync (profile → runtime materials, every LateUpdate)
        // ---------------------------------------------------------------------

        /// <summary>True for our HideAndDontSave runtime instances — never true for assets.</summary>
        private static bool IsRuntimeMat(Material m)
            => m != null && (m.hideFlags & HideFlags.DontSaveInEditor) != 0;

        // Runtime materials are strictly per-island. Duplicating an island
        // GameObject copies REFERENCES to the original's instances, so the
        // domain-reload re-adopt below must never claim a material another
        // island already owns — else both islands sync their profiles into
        // one material and the last writer wins ("stone detail applies to
        // every island"). Unclaimed survivors still re-adopt normally.
        private static readonly Dictionary<Material, IslandSkirt> RuntimeMatClaims =
            new Dictionary<Material, IslandSkirt>();

        private bool TryClaimRuntimeMat(Material m)
        {
            if (m == null) return false;
            if (RuntimeMatClaims.TryGetValue(m, out var owner) && owner != null && owner != this)
                return false;
            RuntimeMatClaims[m] = this;
            return true;
        }

        /// <summary>Shaders driven by the profile's skirtSprite block (rock sheet + tiling).</summary>
        private static bool IsSpriteFamily(string shaderName) =>
            shaderName == SpriteShaderName || shaderName == PlatformShaderName;

        /// <summary>World depth the last trace actually hung the cliff, before per-column jitter
        /// and taper. Prefers what was measured over what was asked for, because
        /// <see cref="DepthSampler"/> supersedes <see cref="DepthOverride"/> and only the trace
        /// sees it; -1 until a build has run.</summary>
        private float _tracedDepth = -1f;

        /// <summary>World height of the cliff wall — what a rim-relative shader sizes itself off.</summary>
        private float EffectiveDepth =>
            _tracedDepth > 0f ? _tracedDepth
            : DepthOverride >= 0f ? DepthOverride
            : Profile.skirtShape.thickness;

        private void SyncMaterials()
        {
            var prof = Profile;

            if (_fillRuntimeMaterial != null)
                prof.ApplyToFillMaterial(_fillRuntimeMaterial);
            if (_runtimeMaterial != null)
            {
                if (_runtimeMaterial.shader.name == ShingleShaderName)
                    prof.ApplyToShingleMaterial(_runtimeMaterial);
                else if (IsSpriteFamily(_runtimeMaterial.shader.name))
                    prof.ApplyToSkirtSpriteMaterial(_runtimeMaterial);
                else
                    prof.ApplyToSkirtMaterial(_runtimeMaterial);

                // The platform variant sizes its tiles off a WORLD height instead of the
                // normalised UV0. Prefer the band height (one grid step) so a tall rim
                // repeats the rock rather than stretching one copy down it; fall back to
                // the traced depth, which is the un-banded single-copy behaviour.
                if (_runtimeMaterial.shader.name == PlatformShaderName)
                    _runtimeMaterial.SetFloat(
                        "_RimHeight", RimBandHeight > 0f ? RimBandHeight : EffectiveDepth);
            }

            SyncSkirtTint(prof);

            // Fringe: profile props + live per-island state. Re-adopt a
            // HideAndDontSave instance that survived a domain reload (the
            // field ref is lost but the material persists on the renderer).
            Material fmat = _fringeRenderer != null ? _fringeRenderer.sharedMaterial : null;
            if (_fringeRuntimeMaterial == null && IsRuntimeMat(fmat) && TryClaimRuntimeMat(fmat))
                _fringeRuntimeMaterial = fmat;
            if (fmat == null || fmat != _fringeRuntimeMaterial) return;

            prof.ApplyToFringeMaterial(fmat);
            if (_splineSource != null)
            {
                // The top's base colour lives on the SpriteShapeRenderer (its
                // fill texture can be plain white) — carry it into the fringe.
                // Fill texture sampled in object space at texWidth/PPU world
                // units per repeat, matching SpriteShape's local fill UVs.
                if (_splineSource.spriteShape != null)
                {
                    Texture2D fillTex = _splineSource.spriteShape.fillTexture;
                    if (fillTex != null)
                    {
                        fmat.SetTexture("_FillTex", fillTex);
                        fmat.SetFloat("_FillScale",
                            fillTex.width / Mathf.Max(_splineSource.fillPixelsPerUnit, 1f));
                    }
                }
                if (_splineSource.spriteShapeRenderer != null)
                    fmat.SetColor("_Color",
                        prof.fill.baseTint * _splineSource.spriteShapeRenderer.color);
            }
            // Same baked falloff mask as the fill, so the border dip flows
            // across the lip onto the blades (white = no dip when disabled).
            if (prof.fill.falloffEnabled && _falloffTex != null)
            {
                fmat.SetTexture("_FalloffTex", _falloffTex);
                fmat.SetVector("_FalloffRect", _falloffRectCached);
            }
            else
            {
                fmat.SetTexture("_FalloffTex", Texture2D.whiteTexture);
            }
        }

        /// <summary>
        /// The skirt's grass band (_TopColor) is the same grass as the top
        /// surface — follow the island's renderer tint so tinting the island
        /// recolours the whole lip. Per-renderer MPB: the shared runtime
        /// material keeps the profile's base colour.
        /// </summary>
        private void SyncSkirtTint(IslandVisualProfile prof)
        {
            if (_meshRenderer == null || _splineSource == null) return;
            var srcRend = _splineSource.spriteShapeRenderer;
            if (srcRend == null) return;

            // Shingle shader tints via material _Tint; no per-renderer grass/top colour.
            if (_runtimeMaterial != null && _runtimeMaterial.shader.name == ShingleShaderName) return;

            _skirtMpb ??= new MaterialPropertyBlock();
            _meshRenderer.GetPropertyBlock(_skirtMpb);

            string shaderName = _runtimeMaterial != null ? _runtimeMaterial.shader.name : null;
            if (shaderName == PlatformShaderName)
                // The platform rim has no grass band of its own — the Fringe mesh owns the lip —
                // so the island tint reaches it through the global tint instead.
                _skirtMpb.SetColor("_Tint", prof.skirtSprite.tint * srcRend.color);
            else if (shaderName == SpriteShaderName)
                _skirtMpb.SetColor("_GrassColor", prof.skirtSprite.grassColor * srcRend.color);
            else
                _skirtMpb.SetColor("_TopColor", prof.skirtSurface.topColor * srcRend.color);

            _meshRenderer.SetPropertyBlock(_skirtMpb);
        }

        /// <summary>
        /// Runtime material for the SpriteShape fill (submesh 0; the edge
        /// submesh is unused since the fringe replaced edge sprites).
        /// </summary>
        private void EnsureFillMaterial()
        {
            if (_splineSource == null) return;
            var rend = _splineSource.spriteShapeRenderer;
            if (rend == null) return;

            var mats = rend.sharedMaterials;
            if (mats.Length == 0) return;
            if (_fillRuntimeMaterial == null && IsRuntimeMat(mats[0]) && TryClaimRuntimeMat(mats[0]))
                _fillRuntimeMaterial = mats[0]; // survived a domain reload

            Material fill = ResolveRuntimeMaterial(
                ref _fillRuntimeMaterial, Profile.fillShader, FillShaderName);
            if (fill != null && mats[0] != fill)
            {
                mats[0] = fill;
                rend.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Shared resolve for the three runtime materials: create from the
        /// profile's shader (name-lookup fallback), and rebuild if the
        /// desired shader changed (profile swap, or the instance was created
        /// against a fallback before the real shader imported).
        /// </summary>
        private Material ResolveRuntimeMaterial(ref Material cache, Shader profileShader, string shaderName)
        {
            Shader shader = profileShader != null ? profileShader : Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"{nameof(IslandSkirt)}: {shaderName} shader not found.", this);
                return cache;
            }
            if (cache != null && cache.shader != shader)
            {
                DestroyHelper(cache);
                cache = null;
            }
            if (cache == null)
            {
                cache = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                RuntimeMatClaims[cache] = this;
            }
            return cache;
        }

        /// <summary>
        /// Falloff off: bind a white mask (= fully interior) so the fill
        /// shows no dip and no roll. Everything else stays untouched —
        /// re-enabling rebakes.
        /// </summary>
        private void NeutralizeTopFalloff()
        {
            if (_splineSource == null) return;
            var rend = _splineSource.spriteShapeRenderer;
            if (rend == null) return;

            var mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetTexture("_FalloffTex", Texture2D.whiteTexture);
            rend.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// Bakes a distance-to-edge mask over the contour bounds in the
        /// CONTROLLER's local space — the space the SpriteShape fill mesh is
        /// in — and feeds it to the fill material via a MaterialPropertyBlock
        /// (merged, so SpriteShape's own fill-texture binding survives).
        /// R = border distance, driving the fill shader's edge darken and
        /// roll. The grass lip itself is the fringe mesh (BuildFringe).
        /// </summary>
        private void BakeTopFalloff(List<Vector3> ring)
        {
            if (_splineSource == null) return;
            var rend = _splineSource.spriteShapeRenderer;
            if (rend == null) return;
            float falloffWidth = Profile.fill.falloffWidth;

            // Contour → controller-local 2D points; the shared baker does the rest.
            Transform ctrl = _splineSource.transform;
            int n = ring.Count;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 l = ctrl.InverseTransformPoint(transform.TransformPoint(ring[i]));
                pts[i] = new Vector2(l.x, l.y);
            }

            ContourFalloff.Bake(pts, falloffWidth, ref _falloffTex, out Vector4 bakedRect);

            // Baked data via property block, merged with the renderer's own.
            _falloffRectCached = bakedRect; // fringe material binds the same bake
            var mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetTexture("_FalloffTex", _falloffTex);
            mpb.SetVector("_FalloffRect", bakedRect);
            mpb.SetFloat("_FalloffWorldWidth", falloffWidth);
            rend.SetPropertyBlock(mpb);
        }


        // ---------------------------------------------------------------------
        // Contour sampling
        // ---------------------------------------------------------------------

        /// <summary>
        /// Closed CCW contour in this transform's local space. Bezier-samples
        /// the spline — the rendered truth — and keeps the raw samples, which
        /// include every spline control point exactly (segment endpoints).
        /// No uniform resample: that corner-cut the spline points between
        /// columns and left seam gaps. Column spacing is non-uniform; noise is
        /// position-based and taper/bridging run on arc length, so nothing
        /// downstream needs uniformity. The TilemapToSpriteShape polygon is a
        /// last-resort fallback only.
        /// </summary>
        private List<Vector3> BuildRing()
        {
            List<Vector3> local = null;

            if (_splineSource != null)
            {
                if (_splineSource.spline.GetPointCount() < 3 && _contourSource != null)
                    _contourSource.GenerateBoundary(); // cold start (domain reload)
                local = SampleSplineLocal();
            }

            if (local == null && _contourSource != null)
            {
                var world = _contourSource.ContourWorld;
                if (world == null)
                {
                    _contourSource.GenerateBoundary();
                    world = _contourSource.ContourWorld;
                }
                if (world != null)
                {
                    local = new List<Vector3>(world.Count);
                    foreach (var w in world)
                    {
                        Vector3 l = transform.InverseTransformPoint(w);
                        l.z = 0f;
                        local.Add(l);
                    }
                }
            }

            if (local == null || local.Count < 3) return null;

            if (SignedArea(local) < 0f) local.Reverse();
            return local;
        }

        /// <summary>Exact cubic bezier sampling — matches what SpriteShape renders.</summary>
        private List<Vector3> SampleSplineLocal()
        {
            var spline = _splineSource.spline;
            int n = spline.GetPointCount();
            if (n < 3) return null;

            float spacing = Profile.skirtShape.spacing;
            var pts = new List<Vector3>(n * 8);
            Transform src = _splineSource.transform;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 p0 = spline.GetPosition(i);
                Vector3 p3 = spline.GetPosition(j);
                Vector3 p1 = p0 + spline.GetRightTangent(i);
                Vector3 p2 = p3 + spline.GetLeftTangent(j);

                int steps = Mathf.Clamp(
                    Mathf.CeilToInt(Vector3.Distance(p0, p3) / Mathf.Max(spacing * 0.5f, 0.05f)),
                    2, 64);
                for (int s = 0; s < steps; s++)
                {
                    float t = (float)s / steps;
                    Vector3 l = transform.InverseTransformPoint(
                        src.TransformPoint(CubicBezier(p0, p1, p2, p3, t)));
                    l.z = 0f;
                    pts.Add(l);
                }
            }

            return pts;
        }

        private static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        private static float SignedArea(List<Vector3> pts)
        {
            float a = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i];
                Vector3 q = pts[(i + 1) % pts.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5f;
        }

        // ---------------------------------------------------------------------
        // Chain extraction
        // ---------------------------------------------------------------------

        private static bool[] MarkDownFacing(Vector2[] outward, float[] segLen, float bridgeArc)
        {
            int n = outward.Length;
            var down = new bool[n];
            for (int i = 0; i < n; i++) down[i] = outward[i].y < DownFacingThreshold;

            if (bridgeArc <= 0f) return down;

            // A boundary index starts an up-run right after a down-run — every
            // up-run in a walk from here is flanked by down on both sides.
            int boundary = FindRunBoundary(down);
            if (boundary < 0) return down; // uniform ring

            int k = 0;
            while (k < n)
            {
                int idx = (boundary + k) % n;
                if (down[idx]) { k++; continue; }

                int runLen = 0;
                float runArc = 0f;
                while (runLen < n - k && !down[(boundary + k + runLen) % n])
                {
                    runArc += segLen[(boundary + k + runLen) % n];
                    runLen++;
                }
                if (runArc <= bridgeArc)
                    for (int r = 0; r < runLen; r++) down[(boundary + k + r) % n] = true;
                k += runLen;
            }

            return down;
        }

        private static List<List<int>> ExtractChains(bool[] down, out bool closedRing)
        {
            int n = down.Length;
            var chains = new List<List<int>>();
            closedRing = false;

            int boundary = FindRunBoundary(down);
            if (boundary < 0)
            {
                if (down[0]) // everything down-facing → one closed loop
                {
                    closedRing = true;
                    var all = new List<int>(n);
                    for (int i = 0; i < n; i++) all.Add(i);
                    chains.Add(all);
                }
                return chains;
            }

            List<int> cur = null;
            for (int k = 0; k < n; k++)
            {
                int idx = (boundary + k) % n;
                if (down[idx])
                {
                    cur ??= new List<int>();
                    cur.Add(idx);
                }
                else if (cur != null)
                {
                    if (cur.Count >= 2) chains.Add(cur);
                    cur = null;
                }
            }
            if (cur != null && cur.Count >= 2) chains.Add(cur);

            return chains;
        }

        /// <summary>First index that is up-facing while its predecessor is down-facing; -1 if uniform.</summary>
        private static int FindRunBoundary(bool[] down)
        {
            int n = down.Length;
            for (int i = 0; i < n; i++)
                if (!down[i] && down[(i - 1 + n) % n]) return i;
            return -1;
        }

        // ---------------------------------------------------------------------
        // Strip building
        // ---------------------------------------------------------------------

        /// <summary>One strip quad; sorted back-to-front before index emission.</summary>
        private struct SkirtQuad
        {
            public int Pt, Pb, Ct, Cb; // prev/cur column, top/bottom verts
            public float Key;          // top-edge Y (higher = further back)
            public int Seq;            // emission order, stable tie-break
        }

        private void BuildStrip(
            List<int> chain, bool closedRing, List<Vector3> ring, Vector2[] outward,
            Vector2[] bulgeDir, float ringMinY, float invYRange,
            List<Vector3> verts, List<Vector2> uvs, List<Vector2> uvs1, List<Vector2> uvs2,
            List<Color> colors, List<SkirtQuad> quads)
        {
            var prof = Profile;
            var shape = prof.skirtShape;
            var shading = prof.skirtShading;
            var erosion = prof.erosion;

            float topReach = shape.topInset;
            float lightRad = shading.shadeLightAngle * Mathf.Deg2Rad;
            Vector2 lightDir = new Vector2(Mathf.Cos(lightRad), Mathf.Sin(lightRad));
            int m = chain.Count;
            float u = 0f;
            int firstCol = -1, prevCol = -1;

            // Closed ring: emit a DUPLICATE of the first column at the end (arc-UV
            // continued past the closing segment) instead of stitching back to the
            // first column's u=0 — reusing it compresses/reverses the whole texture
            // into the one closing quad (visible squashed band at the seam).
            bool closeDup = closedRing && m > 2;
            int totalCols = closeDup ? m + 1 : m;

            // Columns are non-uniformly spaced (raw bezier samples), so taper
            // runs on arc length: taper distance = taperColumns × spacing.
            float taperLen = Mathf.Max(shape.taperColumns * shape.spacing, 0.001f);
            float chainArc = 0f;
            for (int k = 1; k < m; k++)
                chainArc += Vector3.Distance(ring[chain[k - 1]], ring[chain[k]]);

            int rows = Mathf.Max(2, shape.rows);

            // Tiny blobs (single-tile isles, style-lab swatches): the horizontal
            // silhouette displacement (profile bulge + erosion) is tuned in world
            // units for full-size islands — on a blob smaller than the bulge itself
            // the two flanks push past each other and columns pile up crossing.
            // Cap total sideways offset to a fraction of the chain length; a no-op
            // on full-size contours.
            float maxSide = 0.15f * chainArc;

            for (int k = 0; k < totalCols; k++)
            {
                int i = chain[k % m];
                Vector3 p = ring[i];
                Vector2 nrm = outward[i];

                if (k > 0) u += Vector3.Distance(ring[chain[(k - 1) % m]], p);

                float taper = closedRing
                    ? 1f
                    : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(u, chainArc - u) / taperLen));

                float side  = Noise(p, 0, erosion) * erosion.noiseStrength * taper;
                float baseDepth = shape.thickness;
                if (DepthSampler != null) baseDepth = DepthSampler(transform.TransformPoint(p));
                else if (DepthOverride >= 0f) baseDepth = DepthOverride;
                float depth = baseDepth
                    * Mathf.Max(0f, 1f + Noise(p, 1, erosion) * erosion.depthJitter) * taper;
                if (baseDepth > _tracedDepth) _tracedDepth = baseDepth;

                // Depth-driven cliffs (terraces): V tracks the FINAL world depth of
                // this column (jitter + taper included), so texel density is uniform
                // across every column and every layer — v = worldDepth / styledDepth,
                // the top slice of the rock sprite at native density.
                float vScale = (DepthSampler != null || DepthOverride >= 0f)
                    ? Mathf.Clamp01(depth / Mathf.Max(shape.thickness, 1e-3f))
                    : 1f;

                // Profile displaces horizontally only — silhouette widening,
                // signed by which flank the edge faces (smoothed normal X).
                // Vertical placement is owned by thickness alone, so the
                // curve's X axis maps 1:1 onto the skirt height and the
                // profile starts exactly at the top edge at any strength.
                // Bottom-centre edges (normal straight down) get no side
                // bulge: flanks swell, the centre drops straight — teardrop.
                Vector2 flank = new Vector2(bulgeDir[i].x, 0f);

                // --- Fake shading, baked to vertex colour ---
                // directional: faces angled away from the fake light darken
                float lit = Vector2.Dot(nrm, lightDir) * 0.5f + 0.5f;
                float shade = Mathf.Lerp(1f - shading.shadeStrength, 1f, lit);
                // depth layering: walls from higher (further back) edges darken
                shade *= 1f - shading.layerShade * Mathf.Clamp01((p.y - ringMinY) * invYRange);
                // crevice AO: concave turns (CCW ring turns right) darken
                Vector3 dirIn  = (ring[i] - ring[(i - 1 + ring.Count) % ring.Count]).normalized;
                Vector3 dirOut = (ring[(i + 1) % ring.Count] - ring[i]).normalized;
                float turn = dirIn.x * dirOut.y - dirIn.y * dirOut.x;
                shade *= 1f - shading.creviceAO * Mathf.Clamp01(-turn * 6f);
                var shadeColor = new Color(shade, shade, shade, 1f);

                int col = verts.Count;
                for (int r = 0; r <= rows; r++)
                {
                    float fr = (float)r / rows; // 0 = island edge, 1 = bottom
                    float bulge = EvaluateProfile(shape.profile, fr) * shape.profileStrength * taper;
                    Vector2 off = nrm * (-topReach * (1f - fr))       // seam tuck, fades out
                                + flank * (bulge + side * fr);         // profile + erosion (ramps in)
                    off.x = Mathf.Clamp(off.x, -maxSide, maxSide);
                    verts.Add(p + (Vector3)off + Vector3.down * (depth * fr));
                    uvs.Add(new Vector2(u, fr * vScale));
                    uvs1.Add(new Vector2(u, -fr * depth)); // arc, world depth (negative: texture V up)
                    // Smoothed outward normal = per-column facing. The sprite
                    // shader reads it as: layerBias = -x (which arc direction
                    // descends toward the nearest-to-cam point → draws on top),
                    // sideness = |x| (edge-on side silhouette, for edge break-up).
                    uvs2.Add(bulgeDir[i]);
                    colors.Add(shadeColor);
                }

                if (k > 0) AddColumnQuads(quads, verts, prevCol, col, rows);
                if (k == 0) firstCol = col;
                prevCol = col;
            }

            RelaxRowInversions(chain, closeDup, ring, verts, firstCol, rows);
        }

        /// <summary>
        /// Overlapping full-sprite quads (shingles) along a cliff chain. Each
        /// quad maps the whole rock sprite (UV 0..1), hangs from the contour and
        /// overlaps its neighbour; the ShinglePlane shader darkens each toward
        /// its under edge (u=1), which the nearer plane covers — a cast shadow
        /// with no tiling-UV cutoff. Quads are keyed by top-edge Y and
        /// painter-sorted with everything else (nearest = drawn last = on top).
        /// The under edge is oriented toward whichever neighbour is nearer the
        /// camera (lower Y), so the shadow always falls on the lapped side.
        /// </summary>
        private void BuildShingles(
            List<int> chain, bool closedRing, List<Vector3> ring,
            List<Vector3> verts, List<Vector2> uvs, List<Vector2> uvs1,
            List<Color> colors, List<SkirtQuad> quads)
        {
            var s = Profile.skirtSprite;
            float w = Mathf.Max(s.shingleWidth, 0.05f);
            float h = DepthOverride >= 0f
                ? Mathf.Max(DepthOverride, 0.05f)
                : Mathf.Max(s.shingleHeight, 0.05f);
            float step = Mathf.Max(w * (1f - s.overlap), 0.02f);

            int m = chain.Count;
            if (m < 2) return;

            // Closed ring (whole contour down-facing, e.g. bridged small islands):
            // include the last→first closing segment and wrap all sampling —
            // otherwise the layout clamps both "ends" onto the same world point
            // and planks pile up where the loop meets itself.
            var pts = chain;
            if (closedRing)
            {
                pts = new List<int>(m + 1);
                pts.AddRange(chain);
                pts.Add(chain[0]);
                m += 1;
            }
            var arc = new float[m];
            for (int k = 1; k < m; k++)
                arc[k] = arc[k - 1] + Vector3.Distance(ring[pts[k - 1]], ring[pts[k]]);
            float chainArc = arc[m - 1];

            // Tiny blobs (single-tile isles, style-lab swatches): planks can't be
            // wider than the coastline they cover. Scale width AND height together
            // (aspect kept) so a small island wears >= ~3 proportional planks
            // instead of full-size ones piling on top of each other.
            float wEff = Mathf.Min(w, chainArc / 3f);
            if (wEff < w)
            {
                h *= wEff / w;
                w = wEff;
                step = Mathf.Max(w * (1f - s.overlap), 0.02f);
            }

            // Open chain: shingle centres run from half..chainArc-half, so the end
            // shingles' outer corners land ON the chain ends — no overflow past the
            // left/right island corners. Closed ring: centres spread uniformly around
            // the full perimeter, no end clamping (the seam is covered by wrap).
            float half = w * 0.5f;
            int count, last;
            if (closedRing)
            {
                count = Mathf.Max(1, Mathf.RoundToInt(chainArc / step));
                last = count - 1;
            }
            else
            {
                float span = chainArc - w;
                count = span > 0f ? Mathf.CeilToInt(span / step) : 0;
                last = count;
            }
            float WrapArc(float a) => closedRing ? Mathf.Repeat(a, chainArc) : Mathf.Clamp(a, 0f, chainArc);
            for (int i = 0; i <= last; i++)
            {
                float center = closedRing
                    ? i * chainArc / count
                    : (count > 0 ? Mathf.Lerp(half, chainArc - half, (float)i / count) : chainArc * 0.5f);
                float aL = WrapArc(center - half);
                float aR = WrapArc(center + half);

                SampleChainAt(pts, ring, arc, aL, out Vector3 pL, out _);
                SampleChainAt(pts, ring, arc, aR, out Vector3 pR, out _);

                // Sharp corners wrap the arc, collapsing the chord between aL/aR —
                // stacked squashed slivers. Rebuild those as full-width planks on the
                // local tangent so the corner fans instead of bunching. Also covers
                // the seam chord on closed rings (aL/aR on either side of the wrap).
                if ((pR - pL).magnitude < w * 0.6f)
                {
                    SampleChainAt(pts, ring, arc, WrapArc(center), out Vector3 pC, out Vector2 tanC);
                    var t3 = new Vector3(tanC.x, tanC.y, 0f);
                    pL = pC - t3 * half;
                    pR = pC + t3 * half;
                }

                // Under edge faces the nearer-camera (lower Y) neighbour.
                SampleChainAt(pts, ring, arc, WrapArc(center - step), out Vector3 pPrev, out _);
                SampleChainAt(pts, ring, arc, WrapArc(center + step), out Vector3 pNext, out _);
                bool underRight = pNext.y <= pPrev.y;

                float hPlank = h;
                if (DepthSampler != null)
                    hPlank = Mathf.Max(
                        DepthSampler(transform.TransformPoint((pL + pR) * 0.5f)), 0.05f);
                Vector3 downV = Vector3.down * hPlank;
                Vector3 tL = pL, tR = pR;
                Vector3 bL = tL + downV, bR = tR + downV;

                int b0 = verts.Count;
                verts.Add(tL); verts.Add(tR); verts.Add(bL); verts.Add(bR);

                float uL = underRight ? 0f : 1f;
                float uR = underRight ? 1f : 0f;
                // Depth-driven planks (terraces): crop to the sprite's top slice
                // at native density instead of compressing the whole sprite.
                float vBot = (DepthSampler != null || DepthOverride >= 0f)
                    ? 1f - Mathf.Clamp01(hPlank / Mathf.Max(s.shingleHeight, 1e-3f))
                    : 0f;
                uvs.Add(new Vector2(uL, 1f)); // tL — sprite top (grass)
                uvs.Add(new Vector2(uR, 1f)); // tR
                uvs.Add(new Vector2(uL, vBot)); // bL — sprite bottom
                uvs.Add(new Vector2(uR, vBot)); // bR
                // Surface space, same convention as BuildStrip: y = -drop below the
                // contour top in world units. The UnderMist haze fades by this drop,
                // so the dissolve tracks the cliff bottom evenly around the island
                // instead of a flat world-Y line.
                uvs1.Add(new Vector2(aL, 0f));      // tL
                uvs1.Add(new Vector2(aR, 0f));      // tR
                uvs1.Add(new Vector2(aL, -hPlank)); // bL
                uvs1.Add(new Vector2(aR, -hPlank)); // bR
                for (int c = 0; c < 4; c++) colors.Add(Color.white);

                quads.Add(new SkirtQuad
                {
                    Pt = b0 + 0, Pb = b0 + 2, // tL, bL
                    Ct = b0 + 1, Cb = b0 + 3, // tR, bR
                    Key = Mathf.Max(pL.y, pR.y),
                    Seq = quads.Count,
                });
            }
        }

        /// <summary>Point + unit tangent at arc length <paramref name="s"/> along a chain.</summary>
        private static void SampleChainAt(
            List<int> chain, List<Vector3> ring, float[] arc, float s,
            out Vector3 pos, out Vector2 tangent)
        {
            int m = chain.Count;
            s = Mathf.Clamp(s, 0f, arc[m - 1]);
            int k = 0;
            while (k < m - 2 && arc[k + 1] < s) k++;
            Vector3 a = ring[chain[k]], b = ring[chain[k + 1]];
            float segLen = Mathf.Max(arc[k + 1] - arc[k], 1e-4f);
            float t = Mathf.Clamp01((s - arc[k]) / segLen);
            pos = Vector3.Lerp(a, b, t);
            Vector2 tan = new Vector2(b.x - a.x, b.y - a.y);
            tangent = tan.sqrMagnitude > 1e-8f ? tan.normalized : Vector2.right;
        }

        /// <summary>
        /// Anti-crossing pass. Smoothed bulge directions still converge at
        /// very tight concave corners; where that inverts a row segment
        /// relative to its source contour segment, weld the two verts to
        /// their midpoint. Degenerate quads render as nothing.
        /// </summary>
        private static void RelaxRowInversions(
            List<int> chain, bool closedDup, List<Vector3> ring,
            List<Vector3> verts, int firstCol, int rows)
        {
            int m = chain.Count;
            int stride = rows + 1; // verts per column, contiguous from firstCol

            // closedDup: a duplicate of column 0 sits at index m (continued UV);
            // relax pairs run through it linearly instead of wrapping.
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                int pairs = closedDup ? m : m - 1;
                for (int k = 0; k < pairs; k++)
                {
                    int ka = k, kb = k + 1;
                    Vector3 orig = ring[chain[(k + 1) % m]] - ring[chain[k % m]];
                    for (int r = 1; r <= rows; r++) // row 0 stays pinned to the seam
                    {
                        int a = firstCol + ka * stride + r;
                        int b = firstCol + kb * stride + r;
                        Vector3 cur = verts[b] - verts[a];
                        if (cur.x * orig.x + cur.y * orig.y < 0f)
                        {
                            Vector3 mid = (verts[a] + verts[b]) * 0.5f;
                            verts[a] = mid;
                            verts[b] = mid;
                            moved = true;
                        }
                    }
                }
                if (!moved) break;
            }

            // Weld the duplicate column back onto column 0 so relaxation can't
            // crack the seam open.
            if (closedDup)
                for (int r = 0; r <= rows; r++)
                {
                    int a = firstCol + r;
                    int b = firstCol + m * stride + r;
                    Vector3 mid = (verts[a] + verts[b]) * 0.5f;
                    verts[a] = mid;
                    verts[b] = mid;
                }
        }

        private static float EvaluateProfile(AnimationCurve curve, float t)
        {
            if (curve == null || curve.length == 0) return 0f;
            return curve.Evaluate(t);
        }

        /// <summary>
        /// Outward normals box-blurred over an arc-length window. Sharp
        /// corners fan their offset directions across the window instead of
        /// converging, which is what keeps the bulge from self-intersecting.
        /// </summary>
        private static Vector2[] SmoothedOutward(Vector2[] outward, float[] segLen, float window)
        {
            int n = outward.Length;
            var result = new Vector2[n];
            float half = Mathf.Max(window, 0.01f) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                Vector2 sum = outward[i];
                float d = 0f;
                for (int s = 1; s < n; s++)
                {
                    d += segLen[(i + s - 1 + n) % n];
                    if (d > half) break;
                    sum += outward[(i + s) % n];
                }
                d = 0f;
                for (int s = 1; s < n; s++)
                {
                    d += segLen[(i - s + n) % n];
                    if (d > half) break;
                    sum += outward[(i - s + n) % n];
                }
                result[i] = sum.sqrMagnitude > 1e-8f ? sum.normalized : outward[i];
            }

            return result;
        }

        private static void AddColumnQuads(
            List<SkirtQuad> quads, List<Vector3> verts, int prevCol, int col, int rows)
        {
            // One key for ALL rows of this wall segment: its row-0 silhouette
            // Y. Keying sub-quads individually lets a back wall's deep rows
            // sort in front of a nearer wall's upper rows at staircase folds.
            float key = Mathf.Max(verts[prevCol].y, verts[col].y);
            for (int r = 0; r < rows; r++)
            {
                quads.Add(new SkirtQuad
                {
                    Pt = prevCol + r, Pb = prevCol + r + 1,
                    Ct = col + r,     Cb = col + r + 1,
                    Key = key,
                    Seq = quads.Count,
                });
            }
        }

        /// <summary>Deterministic -1..1 Perlin. Local coords → stable while the island moves.</summary>
        private static float Noise(Vector3 p, int channel, IslandVisualProfile.ErosionSettings erosion)
        {
            float o = channel * 137.31f + erosion.seed * 0.7131f;
            return Mathf.PerlinNoise(p.x * erosion.noiseScale + o, p.y * erosion.noiseScale + o * 1.618f) * 2f - 1f;
        }

        // ---------------------------------------------------------------------
        // Renderer plumbing
        // ---------------------------------------------------------------------

        private void ResolveSources()
        {
            if (_contourSource == null) _contourSource = GetComponent<TilemapToSpriteShape>();
            if (_splineSource == null)
            {
                if (_contourSource != null && _contourSource.Controller != null)
                    _splineSource = _contourSource.Controller;
                else
                    _splineSource = GetComponent<SpriteShapeController>();
            }
        }

        private void Subscribe()
        {
            if (_subscribed == _contourSource) return;
            if (_subscribed != null) _subscribed.ContourChanged -= OnSourceContourChanged;
            _subscribed = _contourSource;
            if (_subscribed != null) _subscribed.ContourChanged += OnSourceContourChanged;
        }

        private void OnSourceContourChanged() => Generate();

        private void EnsureRenderer()
        {
            Transform child = transform.Find(ChildName);
            if (child == null)
            {
                var go = new GameObject(ChildName) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                child = go.transform;
            }
            else
            {
                child.gameObject.hideFlags = HideFlags.DontSave;
            }

            _meshFilter = child.GetComponent<MeshFilter>();
            if (_meshFilter == null) _meshFilter = child.gameObject.AddComponent<MeshFilter>();
            _meshRenderer = child.GetComponent<MeshRenderer>();
            if (_meshRenderer == null) _meshRenderer = child.gameObject.AddComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "IslandSkirt (generated)", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
            }
            if (_meshFilter.sharedMesh != _mesh) _meshFilter.sharedMesh = _mesh;

            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // Shingle mode forces the ShinglePlane shader; otherwise the
            // profile's skirt shader (sprite-tiling or surface).
            Shader desiredShader; string desiredName;
            if (Profile.skirtSprite.useShinglePlanes)
            {
                desiredShader = Shader.Find(ShingleShaderName);
                desiredName = ShingleShaderName;
            }
            else
            {
                desiredShader = Profile.skirtShader;
                desiredName = ShaderName;
            }

            if (_runtimeMaterial == null && IsRuntimeMat(_meshRenderer.sharedMaterial)
                && TryClaimRuntimeMat(_meshRenderer.sharedMaterial))
                _runtimeMaterial = _meshRenderer.sharedMaterial; // survived a domain reload
            Material mat = ResolveRuntimeMaterial(ref _runtimeMaterial, desiredShader, desiredName);
            if (mat != null && _meshRenderer.sharedMaterial != mat)
                _meshRenderer.sharedMaterial = mat;

            SpriteShapeRenderer srcRenderer = null;
            if (_splineSource != null) srcRenderer = _splineSource.spriteShapeRenderer;

            var rendering = Profile.rendering;
            _meshRenderer.sortingLayerName = !string.IsNullOrEmpty(rendering.sortingLayer)
                ? rendering.sortingLayer
                : (srcRenderer != null ? srcRenderer.sortingLayerName : "Default");
            _meshRenderer.sortingOrder =
                (srcRenderer != null ? srcRenderer.sortingOrder : 0) + rendering.sortingOrderOffset;
        }

        private void EnsureFringeRenderer()
        {
            Transform child = transform.Find(FringeChildName);
            if (child == null)
            {
                var go = new GameObject(FringeChildName) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                child = go.transform;
            }
            else
            {
                child.gameObject.hideFlags = HideFlags.DontSave;
            }

            _fringeFilter = child.GetComponent<MeshFilter>();
            if (_fringeFilter == null) _fringeFilter = child.gameObject.AddComponent<MeshFilter>();
            _fringeRenderer = child.GetComponent<MeshRenderer>();
            if (_fringeRenderer == null) _fringeRenderer = child.gameObject.AddComponent<MeshRenderer>();

            if (_fringeMesh == null)
            {
                _fringeMesh = new Mesh { name = "IslandFringe (generated)", hideFlags = HideFlags.DontSave };
                _fringeMesh.MarkDynamic();
            }
            if (_fringeFilter.sharedMesh != _fringeMesh) _fringeFilter.sharedMesh = _fringeMesh;

            _fringeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fringeRenderer.receiveShadows = false;
            _fringeRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _fringeRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            if (_fringeRuntimeMaterial == null && IsRuntimeMat(_fringeRenderer.sharedMaterial)
                && TryClaimRuntimeMat(_fringeRenderer.sharedMaterial))
                _fringeRuntimeMaterial = _fringeRenderer.sharedMaterial; // survived a domain reload
            Material mat = ResolveRuntimeMaterial(ref _fringeRuntimeMaterial, Profile.fringeShader, FringeShaderName);
            if (mat != null && _fringeRenderer.sharedMaterial != mat)
                _fringeRenderer.sharedMaterial = mat;

            SpriteShapeRenderer srcRenderer = null;
            if (_splineSource != null) srcRenderer = _splineSource.spriteShapeRenderer;
            var rendering = Profile.rendering;
            _fringeRenderer.sortingLayerName = !string.IsNullOrEmpty(rendering.sortingLayer)
                ? rendering.sortingLayer
                : (srcRenderer != null ? srcRenderer.sortingLayerName : "Default");
            _fringeRenderer.sortingOrder =
                (srcRenderer != null ? srcRenderer.sortingOrder : 0) + Profile.fringe.orderOffset;
        }

        // ---------------------------------------------------------------------
        // Decal scatter (silhouette break-up)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Scatter decal sprite cards (vines/bushes) along the down-facing skirt
        /// contour to break the cliff silhouette. Each card is a quad hanging
        /// from an anchor on the skirt band, mapped to a random decal from a
        /// shared sheet (atlas rect baked into UVs). Cards poke outward (side
        /// silhouette) and hang below (bottom silhouette); painter-sorted by
        /// anchor Y. Rendered as a child "Decals" mesh in front of the skirt.
        /// </summary>
        private void BuildDecals(List<Vector3> ring, Vector2[] outward, float[] segLen, bool[] down)
        {
            var dcl = Profile.decals;
            EnsureDecalRenderer();
            if (_decalMesh == null) return;
            int n = ring.Count;

            // Decal atlas rects (all variants must share one texture).
            Texture atlas = null;
            var rects = new List<Vector4>();
            if (dcl.decalSprites != null)
            {
                foreach (var sp in dcl.decalSprites)
                {
                    if (sp == null || sp.texture == null) continue;
                    if (atlas == null) atlas = sp.texture;
                    if (sp.texture != atlas) continue;
                    var r = sp.textureRect;
                    rects.Add(new Vector4(r.x / atlas.width, r.y / atlas.height,
                                          r.width / atlas.width, r.height / atlas.height));
                }
            }
            if (atlas == null || rects.Count == 0) { _decalMesh.Clear(); return; }
            if (_decalRuntimeMaterial != null) _decalRuntimeMaterial.SetTexture("_MainTex", atlas);

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var cards = new List<KeyValuePair<int, float>>(); // vert base, anchor Y

            uint rng = (uint)(dcl.seed * 73856093 ^ 0x1B56C4E9);
            float Rand()
            {
                rng = rng * 1664525u + 1013904223u;
                return ((rng >> 8) & 0xFFFFFFu) / 16777216f;
            }

            for (int i = 0; i < n; i++)
            {
                if (!down[i]) continue;
                int cnt = Mathf.FloorToInt(segLen[i] * dcl.density + Rand());
                for (int c = 0; c < cnt; c++)
                {
                    Vector3 p = Vector3.Lerp(ring[i], ring[(i + 1) % n], Rand());
                    Vector2 nrm = outward[i];
                    Vector3 anchor = p
                        + (Vector3)(nrm * (Rand() * dcl.pokeOut))
                        + Vector3.down * (Rand() * dcl.bandHeight);
                    float sz = Mathf.Lerp(dcl.sizeMin, dcl.sizeMax, Rand());
                    bool flip = Rand() < 0.5f;
                    Vector4 rect = rects[Mathf.Min((int)(Rand() * rects.Count), rects.Count - 1)];

                    float hw = sz * 0.5f;
                    Vector3 tL = anchor + new Vector3(-hw, 0f, 0f);
                    Vector3 tR = anchor + new Vector3(hw, 0f, 0f);
                    int b0 = verts.Count;
                    verts.Add(tL); verts.Add(tR);
                    verts.Add(tL + Vector3.down * sz); verts.Add(tR + Vector3.down * sz);

                    float uL = flip ? rect.x + rect.z : rect.x;
                    float uR = flip ? rect.x : rect.x + rect.z;
                    float vT = rect.y + rect.w, vB = rect.y;
                    uvs.Add(new Vector2(uL, vT)); uvs.Add(new Vector2(uR, vT));
                    uvs.Add(new Vector2(uL, vB)); uvs.Add(new Vector2(uR, vB));
                    for (int k = 0; k < 4; k++) cols.Add(Color.white);
                    cards.Add(new KeyValuePair<int, float>(b0, anchor.y));
                }
            }

            if (cards.Count == 0) { _decalMesh.Clear(); return; }
            cards.Sort((a, b) => b.Value.CompareTo(a.Value)); // higher Y = further back
            var tris = new List<int>(cards.Count * 6);
            foreach (var cd in cards)
            {
                int b0 = cd.Key;
                tris.Add(b0 + 0); tris.Add(b0 + 1); tris.Add(b0 + 2);
                tris.Add(b0 + 1); tris.Add(b0 + 3); tris.Add(b0 + 2);
            }

            _decalMesh.Clear();
            _decalMesh.SetVertices(verts);
            _decalMesh.SetUVs(0, uvs);
            _decalMesh.SetColors(cols);
            _decalMesh.SetTriangles(tris, 0);
            _decalMesh.RecalculateBounds();
        }

        private void EnsureDecalRenderer()
        {
            Transform child = transform.Find("Decals");
            if (child == null)
            {
                var go = new GameObject("Decals") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                child = go.transform;
            }
            else child.gameObject.hideFlags = HideFlags.DontSave;

            _decalFilter = child.GetComponent<MeshFilter>();
            if (_decalFilter == null) _decalFilter = child.gameObject.AddComponent<MeshFilter>();
            _decalRenderer = child.GetComponent<MeshRenderer>();
            if (_decalRenderer == null) _decalRenderer = child.gameObject.AddComponent<MeshRenderer>();

            if (_decalMesh == null)
            {
                _decalMesh = new Mesh { name = "IslandDecals (generated)", hideFlags = HideFlags.DontSave };
                _decalMesh.MarkDynamic();
            }
            if (_decalFilter.sharedMesh != _decalMesh) _decalFilter.sharedMesh = _decalMesh;

            _decalRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _decalRenderer.receiveShadows = false;
            _decalRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _decalRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            if (_decalRuntimeMaterial == null && IsRuntimeMat(_decalRenderer.sharedMaterial)
                && TryClaimRuntimeMat(_decalRenderer.sharedMaterial))
                _decalRuntimeMaterial = _decalRenderer.sharedMaterial;
            Material mat = ResolveRuntimeMaterial(ref _decalRuntimeMaterial,
                Shader.Find(ShingleShaderName), ShingleShaderName);
            if (mat != null)
            {
                if (_decalRenderer.sharedMaterial != mat) _decalRenderer.sharedMaterial = mat;
                mat.SetFloat("_ShadowStrength", 0f);
                mat.SetFloat("_AlphaCutoff", 0.3f);
                mat.SetColor("_Tint", Color.white);
            }

            SpriteShapeRenderer srcRenderer = _splineSource != null ? _splineSource.spriteShapeRenderer : null;
            var rendering = Profile.rendering;
            _decalRenderer.sortingLayerName = !string.IsNullOrEmpty(rendering.sortingLayer)
                ? rendering.sortingLayer
                : (srcRenderer != null ? srcRenderer.sortingLayerName : "Default");
            _decalRenderer.sortingOrder =
                (srcRenderer != null ? srcRenderer.sortingOrder : 0)
                + rendering.sortingOrderOffset + Profile.decals.orderOffset;
        }

        private static void DestroyHelper(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
