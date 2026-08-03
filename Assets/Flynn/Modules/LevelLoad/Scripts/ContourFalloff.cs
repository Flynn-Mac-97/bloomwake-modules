using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Environment
{
    /// <summary>
    /// Bakes a distance-to-edge mask for a closed 2D contour: R = 0 at/outside the
    /// boundary → 1 at <c>falloffWidth</c> inward. Exact Euclidean distance computed
    /// in a band around each segment (work scales with boundary length, not area),
    /// interior-tested by scanline parity. Extracted from IslandSkirt's top-fill
    /// falloff so WaterSurface (shore foam / edge blend) shares one implementation.
    /// </summary>
    public static class ContourFalloff
    {
        /// <param name="pts">Closed contour in the consumer's mesh-local space.</param>
        /// <param name="falloffWidth">World width of the 0→1 ramp.</param>
        /// <param name="tex">Reused/reallocated bake target (DontSave).</param>
        /// <param name="rect">Local-space rect (x, y, w, h) the texture maps onto.</param>
        /// <param name="texelHint">Optional world texel size — pass when the consumer
        /// draws features THINNER than the ramp (water foam bands) so they don't go
        /// mushy at the default falloffWidth/8 resolution. 0 = default.</param>
        public static void Bake(IList<Vector2> pts, float falloffWidth, ref Texture2D tex, out Vector4 rect,
            float texelHint = 0f)
        {
            int n = pts.Count;
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 mx = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < n; i++)
            {
                mn = Vector2.Min(mn, pts[i]);
                mx = Vector2.Max(mx, pts[i]);
            }

            // Resolution adapts to falloff width: >=8 texels across the band; 1024
            // cap — bake work scales with boundary length, not area.
            float maxExtent = Mathf.Max(mx.x - mn.x, mx.y - mn.y);
            float texel = Mathf.Clamp(
                texelHint > 0f ? texelHint : falloffWidth / 8f,
                maxExtent / 1024f, maxExtent / 64f);
            mn -= Vector2.one * texel; // pad so boundary texels are interior
            mx += Vector2.one * texel;
            int rx = Mathf.Clamp(Mathf.CeilToInt((mx.x - mn.x) / texel), 4, 1032);
            int ry = Mathf.Clamp(Mathf.CeilToInt((mx.y - mn.y) / texel), 4, 1032);

            float band = falloffWidth + texel * 2f;
            var dist = new float[rx * ry];
            for (int i = 0; i < dist.Length; i++) dist[i] = band;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % n];
                Vector2 lo = Vector2.Min(a, b) - Vector2.one * band;
                Vector2 hi = Vector2.Max(a, b) + Vector2.one * band;
                int x0 = Mathf.Clamp((int)((lo.x - mn.x) / texel), 0, rx - 1);
                int x1 = Mathf.Clamp((int)((hi.x - mn.x) / texel) + 1, 0, rx - 1);
                int y0 = Mathf.Clamp((int)((lo.y - mn.y) / texel), 0, ry - 1);
                int y1 = Mathf.Clamp((int)((hi.y - mn.y) / texel) + 1, 0, ry - 1);

                Vector2 ab = b - a;
                float abLenSq = Mathf.Max(ab.sqrMagnitude, 1e-12f);

                for (int y = y0; y <= y1; y++)
                {
                    float py = mn.y + (y + 0.5f) * texel;
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = mn.x + (x + 0.5f) * texel;
                        float t = Mathf.Clamp01(((px - a.x) * ab.x + (py - a.y) * ab.y) / abLenSq);
                        float dx = px - (a.x + ab.x * t);
                        float dy = py - (a.y + ab.y * t);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        int idx = y * rx + x;
                        if (d < dist[idx]) dist[idx] = d;
                    }
                }
            }

            // Interior test (scanline parity): texels OUTSIDE the shape stay at 0.
            var interior = new bool[rx * ry];
            var xs = new List<float>(16);
            for (int y = 0; y < ry; y++)
            {
                float wy = mn.y + (y + 0.5f) * texel;
                xs.Clear();
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = pts[i], b = pts[(i + 1) % n];
                    if ((a.y <= wy) == (b.y <= wy)) continue;
                    xs.Add(a.x + (wy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int p = 0; p + 1 < xs.Count; p += 2)
                {
                    int x0 = Mathf.Clamp(Mathf.CeilToInt((xs[p] - mn.x) / texel - 0.5f), 0, rx - 1);
                    int x1 = Mathf.Clamp(Mathf.FloorToInt((xs[p + 1] - mn.x) / texel - 0.5f), 0, rx - 1);
                    for (int x = x0; x <= x1; x++) interior[y * rx + x] = true;
                }
            }

            if (tex == null || tex.width != rx || tex.height != ry
                || tex.format != TextureFormat.RGBA32)
            {
                if (tex != null)
                {
                    if (Application.isPlaying) Object.Destroy(tex);
                    else Object.DestroyImmediate(tex);
                }
                tex = new Texture2D(rx, ry, TextureFormat.RGBA32, false)
                {
                    name = "ContourFalloff (baked)",
                    hideFlags = HideFlags.DontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            var pixels = new Color32[rx * ry];
            float invWidth = 1f / Mathf.Max(falloffWidth, 1e-4f);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte v = interior[i]
                    ? (byte)(Mathf.Clamp01(dist[i] * invWidth) * 255f)
                    : (byte)0;
                // G = interior flag — consumers use it for exact inside/outside
                // tests (e.g. the water's orthographic back-bank wall).
                pixels[i] = new Color32(v, interior[i] ? (byte)255 : (byte)0, 0, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);

            rect = new Vector4(mn.x, mn.y, mx.x - mn.x, mx.y - mn.y);
        }
    }
}
