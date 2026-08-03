using System.Collections.Generic;
using UnityEngine;

namespace Flynn.Modules.FXLab
{
    /// <summary>
    /// Turns a <see cref="SheetFXSettings"/> block into playable frames, whichever way its art
    /// was authored: individual sprite assets win, then explicit pixel rects (what the VFX
    /// browser's segmentation writes), then plain grid slicing by row + cell size.
    ///
    /// Shared because more than one effect animates from a sheet now (the slash/burst anims
    /// and the sprite-sheet puff) and they MUST read a browser-assigned block identically -
    /// two slicers drifting apart would show different frames for the same assignment.
    /// Results are cached per settings block and rebuilt when its fields change, so live
    /// scrubbing in the tune panel stays cheap.
    /// </summary>
    public static class FXSheetFrames
    {
        class Cache
        {
            public Sprite[] frames;
            public Texture2D sheet;
            public Rect[] rects;
            public int row = -1, count, cell, cellH;
        }

        static readonly Dictionary<SheetFXSettings, Cache> Caches =
            new Dictionary<SheetFXSettings, Cache>();

        /// <summary>Frames for this block, or null when it has no usable art.</summary>
        public static Sprite[] Resolve(SheetFXSettings s)
        {
            if (s == null) return null;
            if (s.frames != null && s.frames.Length > 0) return s.frames;   // no slicing needed
            if (s.sheet == null) return null;

            if (!Caches.TryGetValue(s, out var c))
            {
                c = new Cache();
                Caches[s] = c;
            }

            if (s.cellRects != null && s.cellRects.Length > 0) BuildRects(s, c);
            else BuildGrid(s, c);
            return c.frames;
        }

        static void BuildRects(SheetFXSettings s, Cache c)
        {
            if (c.frames != null && c.sheet == s.sheet && c.rects == s.cellRects) return;

            var rects = s.cellRects;
            float ppu = 1f;
            foreach (var r in rects) ppu = Mathf.Max(ppu, Mathf.Max(r.width, r.height));

            c.frames = new Sprite[rects.Length];
            for (int i = 0; i < rects.Length; i++)
            {
                var r = rects[i];
                // clamp so hand-edited rects can't fall off the texture
                r.width = Mathf.Min(r.width, s.sheet.width);
                r.height = Mathf.Min(r.height, s.sheet.height);
                r.x = Mathf.Clamp(r.x, 0, s.sheet.width - r.width);
                r.y = Mathf.Clamp(r.y, 0, s.sheet.height - r.height);
                c.frames[i] = Sprite.Create(s.sheet, r, new Vector2(0.5f, 0.5f), ppu);
            }
            c.sheet = s.sheet;
            c.rects = rects;
            c.row = -1;   // invalidate the grid cache
        }

        static void BuildGrid(SheetFXSettings s, Cache c)
        {
            if (s.frameCount < 1 || s.cellSize < 1) { c.frames = null; return; }

            int cellH = s.cellHeight > 0 ? s.cellHeight : s.cellSize;   // non-square cells welcome
            if (c.frames != null && c.sheet == s.sheet && c.row == s.row
                && c.count == s.frameCount && c.cell == s.cellSize && c.cellH == cellH) return;

            int cols = s.sheet.width / s.cellSize;
            int rows = Mathf.Max(1, s.sheet.height / cellH);
            int row = Mathf.Clamp(s.row, 0, rows - 1);
            int count = Mathf.Clamp(s.frameCount, 1, cols);

            c.frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                // row 0 = TOP of the sheet; texture rects are bottom-up
                var rect = new Rect(i * s.cellSize, s.sheet.height - (row + 1) * cellH,
                    s.cellSize, cellH);
                c.frames[i] = Sprite.Create(s.sheet, rect, new Vector2(0.5f, 0.5f),
                    Mathf.Max(s.cellSize, cellH));
            }
            c.sheet = s.sheet;
            c.row = row;
            c.count = count;
            c.cell = s.cellSize;
            c.cellH = cellH;
            c.rects = null;   // invalidate the rect cache
        }
    }
}
