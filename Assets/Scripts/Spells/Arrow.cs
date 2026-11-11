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

        /// <summary>Number key that enters this arrow while channeling: 1 = left, 2 = down, 3 = up, 4 = right.</summary>
        public static int KeyNumber(Arrow a) => a switch { Arrow.Left => 1, Arrow.Down => 2, Arrow.Up => 3, _ => 4 };

        /// <summary>The keys to press for a sequence, e.g. Fireball (down, right, up) = "2 4 3".</summary>
        public static string KeysText(IList<Arrow> seq)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < seq.Count; i++) sb.Append(i > 0 ? " " : "").Append(KeyNumber(seq[i]));
            return sb.ToString();
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
