using System;
using System.Collections.Generic;
using System.Text;

namespace Spellcaster
{
    public enum Arrow { Up, Down, Left, Right }

    /// <summary>Spell schools. Effects carry one; combinations of schools unlock synergies in the Forge.</summary>
    public enum Element { Arcane, Fire, Storm, Frost, Life }

    public static class Arrows
    {
        /// <summary>Parses "↓→↑" or "DRU" (U/D/L/R) into a sequence.</summary>
        public static Arrow[] Parse(string s)
        {
            var list = new List<Arrow>();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '↑': case 'U': case 'u': list.Add(Arrow.Up); break;
                    case '↓': case 'D': case 'd': list.Add(Arrow.Down); break;
                    case '←': case 'L': case 'l': list.Add(Arrow.Left); break;
                    case '→': case 'R': case 'r': list.Add(Arrow.Right); break;
                    case ' ': break;
                    default: throw new ArgumentException($"Bad arrow '{c}' in \"{s}\"");
                }
            }
            return list.ToArray();
        }

        public static string ToText(IList<Arrow> seq)
        {
            var sb = new StringBuilder();
            foreach (var a in seq) sb.Append("UDLR"[(int)a]);
            return sb.ToString();
        }

        /// <summary>"↓ → ↑" for messages.</summary>
        public static string Glyphs(IList<Arrow> seq)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < seq.Count; i++) sb.Append(i > 0 ? " " : "").Append("↑↓←→"[(int)seq[i]]);
            return sb.ToString();
        }

        /// <summary>True if the same stroke appears twice in a row (impossible to draw as a rune).</summary>
        public static bool HasRepeat(IList<Arrow> seq)
        {
            for (int i = 1; i < seq.Count; i++) if (seq[i] == seq[i - 1]) return true;
            return false;
        }

        public static bool HasPrefix(IList<Arrow> seq, IList<Arrow> prefix)
        {
            if (prefix.Count > seq.Count) return false;
            for (int i = 0; i < prefix.Count; i++)
                if (seq[i] != prefix[i]) return false;
            return true;
        }

        /// <summary>Two sequences conflict when one is a prefix of the other (the longer could never be typed).</summary>
        public static bool Conflicts(IList<Arrow> a, IList<Arrow> b) => HasPrefix(a, b) || HasPrefix(b, a);
    }
}
