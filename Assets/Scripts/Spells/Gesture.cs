using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Turns mouse movement into rune strokes. The pen moves with the mouse while channeling; each time it
    /// travels <see cref="StrokeLength"/> in a clearly horizontal or vertical direction that differs from
    /// the last stroke, one arrow is recognised. Carrying on in the same direction never adds a stroke, and
    /// diagonal wobble is ignored, so the player draws shapes (a U, a Z, a loop) rather than counting.
    /// </summary>
    public class GestureRecognizer
    {
        /// <summary>Pen travel (in mouse-axis units, about 10 mouse counts each) that makes a stroke.</summary>
        public float StrokeLength = 4.5f;
        /// <summary>How much the main axis must dominate the other one for a stroke to count.</summary>
        public float Dominance = 1.6f;
        public const int MaxTrail = 600;

        /// <summary>Pen position since the gesture started (y up).</summary>
        public Vector2 Pen { get; private set; }
        /// <summary>Raw pen path, for drawing the rune.</summary>
        public readonly List<Vector2> Trail = new List<Vector2>();
        public Arrow? Last { get; private set; }

        Vector2 anchor;

        public GestureRecognizer() => Reset();

        public void Reset()
        {
            Pen = anchor = Vector2.zero;
            Last = null;
            Trail.Clear();
            Trail.Add(Vector2.zero);
        }

        /// <summary>Moves the pen; returns the stroke recognised by this movement, if any.</summary>
        public Arrow? Feed(Vector2 delta)
        {
            Pen += delta;
            if (Trail.Count < MaxTrail && (Pen - Trail[Trail.Count - 1]).sqrMagnitude > 0.04f) Trail.Add(Pen);

            Vector2 d = Pen - anchor;
            if (d.magnitude < StrokeLength) return null;
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y);
            if (Mathf.Max(ax, ay) < Dominance * Mathf.Min(ax, ay))
            {
                // Diagonal: wait for it to resolve; give up on long diagonal drifts.
                if (d.magnitude > StrokeLength * 2.5f) anchor = Pen;
                return null;
            }
            var dir = ax > ay ? (d.x > 0 ? Arrow.Right : Arrow.Left) : (d.y > 0 ? Arrow.Up : Arrow.Down);
            anchor = Pen;
            if (dir == Last) return null; // still the same stroke
            Last = dir;
            return dir;
        }

        /// <summary>
        /// The ideal shape of a sequence as a polyline (y up, unit strokes). Doubling back adds a small
        /// sideways step so zig-zags like ↑↓↑↓ stay readable.
        /// </summary>
        public static List<Vector2> Glyph(IList<Arrow> seq)
        {
            var pts = new List<Vector2> { Vector2.zero };
            Vector2 p = Vector2.zero, prev = Vector2.zero;
            foreach (var a in seq)
            {
                Vector2 v = Dir(a);
                if (prev != Vector2.zero && v == -prev)
                {
                    // hairpin turn: vertical zig-zags step right, horizontal ones step down
                    p += prev.y != 0f ? new Vector2(0.4f, 0f) : new Vector2(0f, -0.4f);
                    pts.Add(p);
                }
                p += v;
                pts.Add(p);
                prev = v;
            }
            return pts;
        }

        public static Vector2 Dir(Arrow a) => a switch
        {
            Arrow.Up => Vector2.up,
            Arrow.Down => Vector2.down,
            Arrow.Left => Vector2.left,
            _ => Vector2.right,
        };
    }
}
