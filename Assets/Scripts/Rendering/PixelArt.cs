using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>Procedural, point-filtered placeholder textures (no imported art).</summary>
    public static class PixelArt
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        public static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        public static readonly Color32 OutlineColor = new Color32(18, 8, 20, 255);

        public static Texture2D Cached(string key, Func<Texture2D> make)
        {
            if (!cache.TryGetValue(key, out var t) || t == null)
            {
                t = make();
                t.name = key;
                cache[key] = t;
            }
            return t;
        }

        public static Texture2D NewTex(int w, int h, bool repeat = false) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
            };

        public static Texture2D FromPixels(int w, int h, Color32[] px, bool repeat = false)
        {
            var t = NewTex(w, h, repeat);
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        /// <summary>Rows are listed top to bottom. Unknown chars are transparent.</summary>
        public static Texture2D FromAscii(string[] rows, Dictionary<char, Color32> palette, bool outline = true)
        {
            int h = rows.Length, w = 0;
            foreach (var r in rows) w = Mathf.Max(w, r.Length);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                string row = rows[h - 1 - y];
                for (int x = 0; x < w; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    px[y * w + x] = palette.TryGetValue(c, out var col) ? col : Clear;
                }
            }
            if (outline) Outline(px, w, h);
            return FromPixels(w, h, px);
        }

        public static Texture2D White => Cached("white", () => FromPixels(1, 1, new[] { new Color32(255, 255, 255, 255) }));

        public static Texture2D Glow => Cached("glow", () =>
        {
            const int s = 32;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                float d = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                float a = Mathf.Round(d * d * 6f) / 6f; // stepped falloff for a pixel look
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            var t = FromPixels(s, s, px);
            return t;
        });

        static readonly string[] ArrowUp =
        {
            "....#....",
            "...###...",
            "..#####..",
            ".#######.",
            "#########",
            "...###...",
            "...###...",
            "...###...",
            "...###...",
        };

        /// <summary>White 9x9 arrow glyph (tint with GUI.color), with a dark outline.</summary>
        public static Texture2D ArrowTex(Arrow dir) => Cached("arrow_" + dir, () =>
        {
            const int n = 9, s = n + 2;
            var px = new Color32[s * s];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                if (ArrowUp[y][x] != '#') continue;
                // (x, y) in "up" glyph space, y = 0 at top → rotate into the requested direction.
                int u = x, v = n - 1 - y; // texture coords, v up
                int tx, ty;
                switch (dir)
                {
                    case Arrow.Down: tx = u; ty = n - 1 - v; break;
                    case Arrow.Left: tx = n - 1 - v; ty = u; break;
                    case Arrow.Right: tx = v; ty = n - 1 - u; break;
                    default: tx = u; ty = v; break;
                }
                px[(ty + 1) * s + tx + 1] = new Color32(255, 255, 255, 255);
            }
            Outline(px, s, s);
            return FromPixels(s, s, px);
        });

        public static Texture2D RingTex =>Cached("ring", () =>
        {
            const int s = 64;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.88f) / 0.12f);
                a = Mathf.Round(a * 4f) / 4f;
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            return FromPixels(s, s, px);
        });

        public static Texture2D Bricks(string key, Color32 brick, Color32 mortar, int seed) => Cached(key, () =>
        {
            const int s = 32;
            var rng = new System.Random(seed);
            var shades = new float[16];
            for (int i = 0; i < shades.Length; i++) shades[i] = 0.78f + (float)rng.NextDouble() * 0.3f;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                int row = y / 8;
                int offset = (row % 2) * 8;
                for (int x = 0; x < s; x++)
                {
                    int bx = (x + offset) % s;
                    bool isMortar = y % 8 == 7 || bx % 16 == 15;
                    float noise = (float)rng.NextDouble() * 0.16f - 0.08f;
                    float edge = (y % 8 == 6 || bx % 16 == 14) ? -0.12f : (y % 8 == 0 ? 0.08f : 0f);
                    px[y * s + x] = isMortar
                        ? Mul(mortar, 1f + noise)
                        : Mul(brick, shades[row * 2 + bx / 16] + noise + edge);
                }
            }
            return FromPixels(s, s, px, true);
        });

        public static Texture2D Tiles(string key, Color32 stone, Color32 grout, int seed) => Cached(key, () =>
        {
            const int s = 32;
            var rng = new System.Random(seed);
            var shades = new float[4];
            for (int i = 0; i < 4; i++) shades[i] = 0.8f + (float)rng.NextDouble() * 0.3f;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool isGrout = x % 16 == 0 || y % 16 == 0;
                float noise = (float)rng.NextDouble() * 0.18f - 0.09f;
                float bevel = (x % 16 == 1 || y % 16 == 15) ? 0.1f : ((x % 16 == 15 || y % 16 == 1) ? -0.1f : 0f);
                px[y * s + x] = isGrout ? Mul(grout, 1f + noise) : Mul(stone, shades[(y / 16) * 2 + x / 16] + noise + bevel);
            }
            return FromPixels(s, s, px, true);
        });

        // --- raster helpers for procedural sprites (y = 0 is the bottom row) ---

        public static void Set(Color32[] px, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            px[y * w + x] = c;
        }

        public static void Rect(Color32[] px, int w, int h, int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Set(px, w, h, x, y, c);
        }

        public static void Ellipse(Color32[] px, int w, int h, float cx, float cy, float rx, float ry, Color32 c)
        {
            for (int y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
            for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                if (dx * dx + dy * dy <= 1f) Set(px, w, h, x, y, c);
            }
        }

        public static void ThickLine(Color32[] px, int w, int h, Vector2 a, Vector2 b, float radius, Color32 c)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 2f) + 1;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                Ellipse(px, w, h, p.x, p.y, radius, radius, c);
            }
        }

        /// <summary>1px dark outline around opaque pixels.</summary>
        public static void Outline(Color32[] px, int w, int h)
        {
            var src = (Color32[])px.Clone();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (src[y * w + x].a != 0) continue;
                bool near = (x > 0 && src[y * w + x - 1].a != 0) || (x < w - 1 && src[y * w + x + 1].a != 0)
                         || (y > 0 && src[(y - 1) * w + x].a != 0) || (y < h - 1 && src[(y + 1) * w + x].a != 0);
                if (near) px[y * w + x] = OutlineColor;
            }
        }

        public static Color32 Mul(Color32 c, float f) =>
            new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), c.a);
    }
}
