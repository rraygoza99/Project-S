using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>A castable spell: an arrow sequence, a cooldown, a delivery and a payload of effects.</summary>
    public class SpellDefinition
    {
        public string Id;
        public string Name;
        /// <summary>Word used when this spell is the base of a forged spell ("Blazing <Noun>").</summary>
        public string Noun;
        /// <summary>Adjective this spell lends to forged spells ("<Adjective> Lightning").</summary>
        public string Adjective;
        public Arrow[] Sequence;
        public float Cooldown;
        public Color Color;
        public string IconKey;              // Resources/SpellIcons/<key>.png
        public string SecondaryIconKey;     // forged spells show their second component in the corner
        public SpellDelivery Delivery;
        public List<SpellEffect> Payload = new List<SpellEffect>();

        /// <summary>Forge bookkeeping: base spell ids this was made from, synergy names, and depth.</summary>
        public List<string> Components = new List<string>();
        public List<string> Synergies = new List<string>();
        public int Tier = 1;
        public bool IsForged => Tier > 1;

        public HashSet<Element> Elements => new HashSet<Element>(Payload.Select(e => e.Element));

        public string Describe()
        {
            var parts = new List<string> { Delivery.Name };
            parts.AddRange(Payload.Select(e => e.Describe()));
            return string.Join(", ", parts);
        }

        Texture2D icon, secondaryIcon;
        public Texture2D Icon => icon ? icon : (icon = SpellIcons.Get(IconKey, Color));
        public Texture2D SecondaryIcon => string.IsNullOrEmpty(SecondaryIconKey) ? null
            : secondaryIcon ? secondaryIcon : (secondaryIcon = SpellIcons.Get(SecondaryIconKey, Color));
    }

    /// <summary>Icons from Resources/SpellIcons (SpellBook Megapack) with a procedural rune fallback.</summary>
    public static class SpellIcons
    {
        public static Texture2D Get(string key, Color color)
        {
            var t = string.IsNullOrEmpty(key) ? null : Resources.Load<Texture2D>("SpellIcons/" + key);
            if (t) return t;
            return PixelArt.Cached("rune_" + key, () =>
            {
                const int s = 16;
                var rng = new System.Random((key ?? "").GetHashCode());
                var px = new Color32[s * s];
                Color32 bg = Color.Lerp(color, Color.black, 0.75f), fg = Color.Lerp(color, Color.white, 0.3f);
                for (int i = 0; i < px.Length; i++) px[i] = bg;
                // symmetric random rune
                for (int y = 3; y < 13; y++)
                for (int x = 3; x < 8; x++)
                    if (rng.NextDouble() < 0.45)
                    {
                        px[y * s + x] = fg;
                        px[y * s + (s - 1 - x)] = fg;
                    }
                return PixelArt.FromPixels(s, s, px);
            });
        }
    }
}
