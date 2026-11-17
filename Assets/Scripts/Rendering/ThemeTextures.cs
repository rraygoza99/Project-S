using UnityEngine;

namespace Spellcaster
{
    /// <summary>Procedural textures for the floor themes: cave rock, ice, iron plates, basalt with lava cracks, lava.</summary>
    public static class ThemeTextures
    {
        const int S = 32;

        static Color32 Mul(Color32 c, float k) =>
            new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), 255);

        static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, Mathf.Clamp01(t));

        /// <summary>Distances to the nearest and second-nearest cell point (tiling), and the nearest index.</summary>
        static void Cells(Vector2[] pts, int x, int y, out float d1, out float d2, out int idx)
        {
            d1 = d2 = float.MaxValue;
            idx = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                float dx = Mathf.Abs(x - pts[i].x), dy = Mathf.Abs(y - pts[i].y);
                dx = Mathf.Min(dx, S - dx);
                dy = Mathf.Min(dy, S - dy);
                float d = dx * dx + dy * dy;
                if (d < d1) { d2 = d1; d1 = d; idx = i; }
                else if (d < d2) d2 = d;
            }
            d1 = Mathf.Sqrt(d1);
            d2 = Mathf.Sqrt(d2);
        }

        static Vector2[] Points(System.Random rng, int n)
        {
            var p = new Vector2[n];
            for (int i = 0; i < n; i++) p[i] = new Vector2((float)rng.NextDouble() * S, (float)rng.NextDouble() * S);
            return p;
        }

        /// <summary>Lumpy cave rock: irregular stones with dark cracks; frost tints the tops of stones.</summary>
        public static Texture2D Rock(string key, Color32 stone, Color32 crack, Color32 frost, float frostAmount, int seed) => PixelArt.Cached(key, () =>
        {
            var rng = new System.Random(seed);
            var pts = Points(rng, 9);
            var shade = new float[pts.Length];
            for (int i = 0; i < shade.Length; i++) shade[i] = 0.75f + (float)rng.NextDouble() * 0.35f;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Cells(pts, x, y, out float d1, out float d2, out int i);
                float edge = d2 - d1;
                float noise = (float)rng.NextDouble() * 0.14f - 0.07f;
                Color32 c = edge < 1.1f ? Mul(crack, 1f + noise) : Mul(stone, shade[i] + noise - d1 * 0.012f);
                // Frost settles on the upper side of each stone.
                if (edge >= 1.1f && frostAmount > 0f && y > pts[i].y + 1f && rng.NextDouble() < frostAmount) c = Lerp(c, frost, 0.6f);
                px[y * S + x] = c;
            }
            return PixelArt.FromPixels(S, S, px, true);
        });

        /// <summary>Clear ice: pale with diagonal streaks and bright cracks.</summary>
        public static Texture2D Ice(string key, Color32 ice, int seed) => PixelArt.Cached(key, () =>
        {
            var rng = new System.Random(seed);
            var pts = Points(rng, 5);
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Cells(pts, x, y, out float d1, out float d2, out _);
                float streak = ((x + y * 2) % 11 < 2) ? 0.18f : 0f;
                float noise = (float)rng.NextDouble() * 0.08f - 0.04f;
                var c = Mul(ice, 0.88f + streak + noise);
                if (d2 - d1 < 0.8f) c = Lerp(c, new Color32(235, 245, 255, 255), 0.35f);
                px[y * S + x] = c;
            }
            return PixelArt.FromPixels(S, S, px, true);
        });

        /// <summary>Riveted iron plates (forge walls).</summary>
        public static Texture2D Plates(string key, Color32 metal, Color32 seam, int seed) => PixelArt.Cached(key, () =>
        {
            var rng = new System.Random(seed);
            var shades = new float[4];
            for (int i = 0; i < 4; i++) shades[i] = 0.8f + (float)rng.NextDouble() * 0.28f;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int lx = x % 16, ly = y % 16;
                bool isSeam = lx == 0 || ly == 0;
                bool rivet = (lx == 3 || lx == 13) && (ly == 3 || ly == 13);
                float noise = (float)rng.NextDouble() * 0.1f - 0.05f;
                float grime = ly < 4 ? -0.08f : 0f;
                float bevel = lx == 1 || ly == 15 ? 0.12f : (lx == 15 || ly == 1 ? -0.12f : 0f);
                px[y * S + x] = isSeam ? Mul(seam, 1f + noise)
                    : rivet ? Mul(metal, 1.45f)
                    : Mul(metal, shades[(y / 16) * 2 + x / 16] + noise + bevel + grime);
            }
            return PixelArt.FromPixels(S, S, px, true);
        });

        /// <summary>Dark basalt cells with glowing lava in the cracks (forge floor).</summary>
        public static Texture2D Basalt(string key, Color32 stone, Color32 glow, int seed) => PixelArt.Cached(key, () =>
        {
            var rng = new System.Random(seed);
            var pts = Points(rng, 7);
            var shade = new float[pts.Length];
            for (int i = 0; i < shade.Length; i++) shade[i] = 0.8f + (float)rng.NextDouble() * 0.3f;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Cells(pts, x, y, out float d1, out float d2, out int i);
                float edge = d2 - d1;
                float noise = (float)rng.NextDouble() * 0.12f - 0.06f;
                px[y * S + x] = edge < 0.9f ? Mul(glow, 0.85f + noise * 2f)
                    : edge < 1.8f ? Lerp(Mul(stone, 0.7f), glow, 0.25f)
                    : Mul(stone, shade[i] + noise);
            }
            return PixelArt.FromPixels(S, S, px, true);
        });

        /// <summary>Molten lava: bright swirls over deep orange.</summary>
        public static Texture2D Lava(string key, int seed) => PixelArt.Cached(key, () =>
        {
            var rng = new System.Random(seed);
            var pts = Points(rng, 6);
            var px = new Color32[S * S];
            var deep = new Color32(190, 50, 10, 255);
            var hot = new Color32(255, 200, 70, 255);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Cells(pts, x, y, out float d1, out float d2, out _);
                float swirl = Mathf.Sin(x * 0.6f + Mathf.Sin(y * 0.4f) * 2f) * 0.5f + 0.5f;
                float t = Mathf.Clamp01(swirl * 0.5f + (d2 - d1 < 1.2f ? 0.5f : 0f) + (float)rng.NextDouble() * 0.15f);
                px[y * S + x] = Lerp(deep, hot, t);
            }
            return PixelArt.FromPixels(S, S, px, true);
        });
    }
}
