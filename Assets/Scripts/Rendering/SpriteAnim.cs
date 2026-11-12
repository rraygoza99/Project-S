using UnityEngine;

namespace Spellcaster
{
    /// <summary>One animation laid out as a grid of equal cells in a single texture (row-major, top-left first).</summary>
    public class SpriteAnim
    {
        public Texture2D Texture;
        public Vector4[] Frames; // _MainTex_ST per frame: (scaleX, scaleY, offsetX, offsetY)
        public Vector2 CellPx;
        public float Fps = 10f;
        public bool Loop = true;

        public int Count => Frames.Length;
        public float Duration => Count / Fps;

        public static SpriteAnim Sheet(Texture2D tex, int cellW, int cellH, int count, float fps, bool loop = true)
        {
            if (tex == null) return null;
            int cols = Mathf.Max(1, tex.width / cellW);
            float sx = cellW / (float)tex.width, sy = cellH / (float)tex.height;
            var frames = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                int c = i % cols, r = i / cols;
                frames[i] = new Vector4(sx, sy, c * sx, 1f - (r + 1) * sy);
            }
            return new SpriteAnim { Texture = tex, Frames = frames, CellPx = new Vector2(cellW, cellH), Fps = fps, Loop = loop };
        }

        /// <summary>Frame index for time t (seconds since the animation started).</summary>
        public int FrameAt(float t)
        {
            int i = Mathf.FloorToInt(t * Fps);
            return Loop ? ((i % Count) + Count) % Count : Mathf.Clamp(i, 0, Count - 1);
        }

        /// <summary>Frame index when the whole animation is stretched over a fixed duration.</summary>
        public int FrameAtNormalized(float k) => Mathf.Clamp(Mathf.FloorToInt(k * Count), 0, Count - 1);
    }

    /// <summary>All animations for one enemy plus how sheet pixels map to world units.</summary>
    public class SpriteSet
    {
        public SpriteAnim Walk, Attack, Hit, Dead;
        /// <summary>Optional: Run (fast movement, e.g. a charge) and Special (a wind-up pose).</summary>
        public SpriteAnim Run, Special;
        public float PixelsPerUnit = 16f;
        /// <summary>Distance in sheet pixels from the bottom of a cell to the character's feet.</summary>
        public float FeetPx;
        /// <summary>Source art faces right; flip when moving left on screen.</summary>
        public bool FacesRight = true;
        public bool Flippable = true;
    }
}
