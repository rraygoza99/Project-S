using UnityEngine;

namespace Spellcaster
{
    /// <summary>The six co-op player colours (sprite robe and spell glow), by lobby slot.</summary>
    public static class PlayerColors
    {
        static readonly Color[] All =
        {
            new Color(0.55f, 0.35f, 0.95f), // violet
            new Color(0.95f, 0.5f, 0.15f),  // orange
            new Color(0.3f, 0.8f, 0.35f),   // green
            new Color(0.25f, 0.75f, 0.95f), // cyan
            new Color(0.9f, 0.25f, 0.3f),   // red
            new Color(0.95f, 0.85f, 0.25f), // yellow
        };
        public static int Count => All.Length;
        public static Color Get(int i) => All[((i % All.Length) + All.Length) % All.Length];
    }

    /// <summary>
    /// Placeholder wizard sprites for other players (until real art): a robed, hatted wizard with a staff,
    /// drawn in code in the player's colour. 5 views (front, front 3/4, side, back 3/4, back; the left-facing
    /// three are mirrored) x 4 frames (idle, walk A, walk B, casting). Cells are 24 x 40 pixels.
    /// </summary>
    public static class PlayerSprites
    {
        public const int CellW = 24, CellH = 40, Views = 5, Frames = 4;
        public const int Idle = 0, WalkA = 1, WalkB = 2, Cast = 3;
        public const float PixelsPerUnit = 21f;

        public static Texture2D Atlas(int colorIndex) =>
            PixelArt.Cached("player_sprites_" + colorIndex, () => Build(PlayerColors.Get(colorIndex)));

        /// <summary>The UV rect (as _MainTex_ST) of a view and frame; mirrored flips it horizontally.</summary>
        public static Vector4 Frame(int view, int frame, bool mirrored)
        {
            float w = 1f / Frames, h = 1f / Views;
            var st = new Vector4(w, h, frame * w, (Views - 1 - view) * h);
            if (mirrored) st = new Vector4(-st.x, st.y, st.z + w, st.w);
            return st;
        }

        static Color32[] px;
        static int ox, oy;

        static Texture2D Build(Color robe)
        {
            int W = CellW * Frames, H = CellH * Views;
            px = new Color32[W * H];
            for (int v = 0; v < Views; v++)
                for (int f = 0; f < Frames; f++)
                {
                    ox = f * CellW;
                    oy = (Views - 1 - v) * CellH;
                    DrawWizard(v, f, robe, W);
                }
            Outline(W, H);
            return PixelArt.FromPixels(W, H, px);
        }

        static int stride;
        static void P(int x, int y, Color c)
        {
            if (x < 0 || x >= CellW || y < 0 || y >= CellH) return;
            px[(oy + y) * stride + ox + x] = c;
        }
        static void R(int x, int y, int w, int h, Color c)
        {
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) P(x + i, y + j, c);
        }

        static void DrawWizard(int view, int frame, Color robe, int W)
        {
            stride = W;
            var robeDark = robe * 0.62f; robeDark.a = 1f;
            var robeLight = Color.Lerp(robe, Color.white, 0.25f);
            var hat = robe * 0.45f; hat.a = 1f;
            var skin = new Color(0.93f, 0.76f, 0.6f);
            var beard = new Color(0.92f, 0.92f, 0.95f);
            var boots = new Color(0.22f, 0.15f, 0.12f);
            var gold = new Color(1f, 0.8f, 0.3f);
            var wood = new Color(0.5f, 0.32f, 0.16f);
            var gem = Color.Lerp(robe, Color.white, 0.55f);
            var eye = new Color(0.12f, 0.08f, 0.15f);

            bool back = view >= 3, side = view == 2;
            int cx = 12;
            int sway = frame == WalkA ? -1 : frame == WalkB ? 1 : 0;
            bool cast = frame == Cast;

            // boots (one tucked up under the robe while walking)
            if (frame != WalkB) R(cx - 4, 0, 3, 2, boots);
            if (frame != WalkA) R(cx + 1, 0, 3, 2, boots);
            if (side) { R(cx - 1 + sway, 0, 4, 2, boots); }

            // robe: wider at the hem; light from the left
            for (int y = 2; y <= 21; y++)
            {
                int hw = 5 + (21 - y) / 6;
                int shift = y < 6 ? sway : 0;
                if (side) hw = Mathf.Max(4, hw - 1);
                for (int x = cx - hw + shift; x < cx + hw + shift; x++)
                {
                    Color c = robe;
                    if (x >= cx + hw + shift - 2) c = robeDark;
                    else if (x <= cx - hw + shift + 1) c = robeLight;
                    if (y <= 3) c = robeDark;
                    P(x, y, c);
                }
            }
            if (!back) R(cx - 5, 11, 10, 1, gold); else R(cx - 5, 11, 10, 1, robeDark);
            if (!back && !side) R(cx - 1, 4, 2, 7, robeDark); // front opening

            // head
            int hx = cx - 3 + (view == 1 ? 1 : view == 3 ? -1 : 0) + (side ? 1 : 0);
            if (back) R(hx, 22, 6, 5, new Color(0.75f, 0.75f, 0.8f)); // grey hair under the hat
            else
            {
                R(hx, 22, 6, 5, skin);
                if (side) { P(hx + 4, 25, eye); P(hx + 6, 24, skin); }
                else if (view == 1) { P(hx + 2, 25, eye); P(hx + 5, 25, eye); }
                else { P(hx + 1, 25, eye); P(hx + 4, 25, eye); }
                // beard
                int bx = side ? hx + 2 : hx;
                int bw = side ? 4 : 6;
                for (int y = 17; y <= 23; y++)
                {
                    int shrink = (23 - y) / 3;
                    R(bx + shrink, y, Mathf.Max(1, bw - shrink * 2), 1, beard);
                }
            }

            // hat: brim and a cone whose tip bends back
            int brimX = cx - 6 + (side ? 1 : 0);
            R(brimX, 27, 12, 2, hat);
            for (int y = 29; y <= 39; y++)
            {
                int k = y - 29;
                int hw = Mathf.Max(1, 4 - k * 4 / 11);
                int bend = side ? -(k / 3) : (back ? 0 : k / 5);
                R(cx - hw + bend + (side ? 1 : 0), y, hw * 2, 1, hat);
            }
            R(cx - 4 + (side ? 1 : 0), 29, 8, 1, gold); // hat band

            // staff and hands
            int sx = back ? cx - 8 : side ? cx + 4 : cx + 7;
            if (view == 1) sx = cx + 7;
            if (view == 3) sx = cx - 7;
            int lift = cast ? 5 : 0;
            R(sx, 2 + lift, 1, 28, wood);
            R(sx - 1, 30 + lift, 3, 2, gem);
            P(sx, 32 + lift, Color.white);
            if (!back) R(sx - 1, 14 + lift, 2, 2, skin);
            if (cast)
            {
                // the free hand raised, glowing
                int fx = back ? cx + 6 : side ? cx - 2 : cx - 7;
                R(fx, 20, 2, 6, robe);
                R(fx, 26, 2, 2, skin);
                P(fx - 1, 28, gem); P(fx + 2, 28, gem); P(fx, 29, Color.white);
            }
        }

        /// <summary>A dark outline around the figure so it reads against any floor.</summary>
        static void Outline(int W, int H)
        {
            var copy = (Color32[])px.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (copy[y * W + x].a > 0) continue;
                    bool near = (x > 0 && copy[y * W + x - 1].a > 0) || (x < W - 1 && copy[y * W + x + 1].a > 0)
                             || (y > 0 && copy[(y - 1) * W + x].a > 0) || (y < H - 1 && copy[(y + 1) * W + x].a > 0);
                    // don't bleed across cells
                    if (near && x % CellW != 0 && x % CellW != CellW - 1 && y % CellH != CellH - 1) px[y * W + x] = PixelArt.OutlineColor;
                }
        }
    }
}
