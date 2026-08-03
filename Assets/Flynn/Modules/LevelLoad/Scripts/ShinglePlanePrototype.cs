using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Environment
{
    /// <summary>
    /// Prototype for the "overlapping sprite planes" skirt approach. Builds a
    /// flat row of full-sprite quads that overlap like shingles, emitted as one
    /// mesh in back-to-front order so each plane draws on top of the one to its
    /// left. Each quad is mapped to the whole sprite (UV 0..1); the shader
    /// darkens it toward the under edge, and the next plane covers that dark
    /// part — a real cast shadow with no tiling-UV cutoff.
    ///
    /// Standalone + throwaway: iterate the look here, then port the mesh-gen +
    /// shader into <see cref="IslandSkirt"/> along the real contour.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ShinglePlanePrototype : MonoBehaviour
    {
        [Header("Sprite")]
        [Tooltip("Rock sprite each plane renders (full sprite = one shingle).")]
        [SerializeField] private Sprite _sprite;

        [Header("Layout")]
        [Min(1)] [SerializeField] private int _count = 6;
        [SerializeField] private float _planeWidth = 1f;
        [SerializeField] private float _planeHeight = 1f;
        [Tooltip("Fraction of a plane that the next plane laps over.")]
        [Range(0f, 0.9f)] [SerializeField] private float _overlap = 0.4f;

        [Header("Shadow (pushed to material)")]
        [SerializeField] private Color _tint = Color.white;
        [Range(0f, 1f)] [SerializeField] private float _shadowStrength = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float _shadowStart = 0.0f;
        [Range(0.02f, 1f)] [SerializeField] private float _shadowSoft = 1.0f;

        private const string ShaderName = "Flynn/ShinglePlane";
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private Mesh _mesh;
        private Material _mat;

        private void OnEnable() => Rebuild();

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled) return;
                Rebuild();
            };
        }
#endif

        private void OnDestroy()
        {
            if (_mesh != null) DestroyImmediate(_mesh);
            if (_mat != null) DestroyImmediate(_mat);
        }

        /// <summary>Rebuild the shingle mesh + push material params. Safe to call live.</summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            _mf ??= GetComponent<MeshFilter>();
            _mr ??= GetComponent<MeshRenderer>();
            EnsureMaterial();

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "ShinglePlanes (proto)", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
            }
            if (_mf.sharedMesh != _mesh) _mf.sharedMesh = _mesh;

            var verts = new List<Vector3>(_count * 4);
            var uvs = new List<Vector2>(_count * 4);
            var cols = new List<Color>(_count * 4);
            var tris = new List<int>(_count * 6);

            // Full-sprite UV rect (handles atlas sub-rects).
            Vector2 uvMin = Vector2.zero, uvMax = Vector2.one;
            if (_sprite != null && _sprite.texture != null)
            {
                Rect r = _sprite.textureRect;
                float tw = _sprite.texture.width, th = _sprite.texture.height;
                uvMin = new Vector2(r.x / tw, r.y / th);
                uvMax = new Vector2((r.x + r.width) / tw, (r.y + r.height) / th);
            }

            float step = _planeWidth * (1f - _overlap);
            for (int i = 0; i < _count; i++)
            {
                float x0 = i * step;
                float x1 = x0 + _planeWidth;
                int b = verts.Count;

                verts.Add(new Vector3(x0, 0f, 0f));
                verts.Add(new Vector3(x1, 0f, 0f));
                verts.Add(new Vector3(x0, _planeHeight, 0f));
                verts.Add(new Vector3(x1, _planeHeight, 0f));

                uvs.Add(new Vector2(uvMin.x, uvMin.y));
                uvs.Add(new Vector2(uvMax.x, uvMin.y));
                uvs.Add(new Vector2(uvMin.x, uvMax.y));
                uvs.Add(new Vector2(uvMax.x, uvMax.y));

                for (int k = 0; k < 4; k++) cols.Add(Color.white);

                // Later quads emitted last => painter-drawn on top (ZWrite off).
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }

            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetUVs(0, uvs);
            _mesh.SetColors(cols);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();

            PushMaterial();
        }

        private void EnsureMaterial()
        {
            if (_mat == null)
            {
                var sh = Shader.Find(ShaderName);
                if (sh == null) { Debug.LogWarning($"{ShaderName} not found", this); return; }
                _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_mr.sharedMaterial != _mat) _mr.sharedMaterial = _mat;
        }

        private void PushMaterial()
        {
            if (_mat == null) return;
            if (_sprite != null && _sprite.texture != null) _mat.SetTexture("_MainTex", _sprite.texture);
            _mat.SetColor("_Tint", _tint);
            _mat.SetFloat("_ShadowStrength", _shadowStrength);
            _mat.SetFloat("_ShadowStart", _shadowStart);
            _mat.SetFloat("_ShadowSoft", _shadowSoft);
        }
    }
}
