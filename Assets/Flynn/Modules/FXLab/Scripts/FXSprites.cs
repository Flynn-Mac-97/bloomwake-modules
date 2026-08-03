using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Cached procedural placeholder sprites for the FX primitives — no texture assets to
    /// import, nothing to wire. Final art replaces these per effect; the components only
    /// care that a sprite exists.
    /// </summary>
    public static class FXSprites
    {
        static Sprite _square, _softCircle, _ring, _crescent;

        /// <summary>Plain 8px white square, 1 world unit.</summary>
        public static Sprite Square
        {
            get
            {
                if (_square != null) return _square;
                const int s = 8;
                var tex = NewTex(s, s);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                        tex.SetPixel(x, y, Color.white);
                tex.Apply();
                _square = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
                return _square;
            }
        }

        /// <summary>Radial-falloff dot — debris, motes, glows.</summary>
        public static Sprite SoftCircle
        {
            get
            {
                if (_softCircle != null) return _softCircle;
                const int s = 32;
                var tex = NewTex(s, s);
                float c = s * 0.5f, r = s * 0.5f;
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                        float a = Mathf.Clamp01(1f - d / r);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                tex.Apply();
                _softCircle = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
                return _softCircle;
            }
        }

        /// <summary>Soft ring band — shockwave pulse.</summary>
        public static Sprite Ring
        {
            get
            {
                if (_ring != null) return _ring;
                const int s = 64;
                var tex = NewTex(s, s);
                float c = s * 0.5f, mid = s * 0.38f, width = s * 0.09f;
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                        float a = Mathf.Clamp01(1f - Mathf.Abs(d - mid) / width);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                tex.Apply();
                _ring = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
                return _ring;
            }
        }

        /// <summary>Thick white crescent, bulge +X, ~1 world unit (ported from CritterBefriend SlashArc).</summary>
        public static Sprite Crescent
        {
            get
            {
                if (_crescent != null) return _crescent;
                const int w = 96, h = 96;
                var tex = NewTex(w, h);
                var c = new Vector2(w * 0.5f, h * 0.5f);
                float inner = h * 0.18f, outer = h * 0.44f, maxAng = Mathf.PI * 0.55f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = x - c.x, dy = y - c.y;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float t = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Atan2(dy, dx)) / maxAng);
                        float innerR = Mathf.Lerp(outer, inner, t * t);   // band narrows to the tips
                        float band = Mathf.Clamp01((outer - dist) / 3f) * Mathf.Clamp01((dist - innerR) / 3f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, band * t));
                    }
                tex.Apply();
                _crescent = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 96f);
                return _crescent;
            }
        }

        static Texture2D NewTex(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
    }
}
