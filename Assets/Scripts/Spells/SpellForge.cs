using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Combines two known spells into a new one: the first spell's delivery carries both payloads
    /// (slightly weakened), element pairs add synergy effects, and the new spell gets a longer,
    /// non-conflicting arrow sequence and a cooldown based on both parents. Forged spells can be
    /// forged again, so combinations keep getting more complex.
    /// </summary>
    public static class SpellForge
    {
        public const float PowerScale = 0.85f;
        public const float CooldownScale = 0.8f;
        public const int MaxEffects = 12;

        public static bool TryCombine(SpellDefinition a, SpellDefinition b, IList<SpellDefinition> book,
            out SpellDefinition result, out string error)
        {
            result = null;
            error = null;
            if (a == null || b == null || a == b) { error = "Pick two different spells."; return false; }
            if (book.Count >= SpellBook.MaxSpells) { error = $"Spell book is full ({SpellBook.MaxSpells})."; return false; }
            if (a.Payload.Count + b.Payload.Count > MaxEffects) { error = "Too unstable: too many effects."; return false; }

            var components = a.Components.Concat(b.Components).Distinct().OrderBy(s => s).ToList();
            if (components.Count == a.Components.Distinct().Count() || components.Count == b.Components.Distinct().Count())
            { error = "That combination adds nothing new."; return false; }
            if (book.Any(s => s.Components.OrderBy(c => c).SequenceEqual(components) && s.Delivery.GetType() == PickDelivery(a, b).GetType()))
            { error = "You already know that spell."; return false; }

            var spell = new SpellDefinition
            {
                Id = "forged_" + string.Join("_", components) + "_" + book.Count,
                Noun = a.Noun,
                Adjective = b.Adjective,
                Cooldown = Mathf.Round((a.Cooldown + b.Cooldown) * CooldownScale * 2f) / 2f,
                Color = Color.Lerp(a.Color, b.Color, 0.45f),
                IconKey = a.IconKey,
                SecondaryIconKey = b.IconKey,
                Delivery = PickDelivery(a, b).Clone(),
                Components = components,
                Tier = Mathf.Max(a.Tier, b.Tier) + 1,
            };
            spell.Name = UniqueName($"{b.Adjective} {a.Noun}", book);

            foreach (var e in a.Payload) spell.Payload.Add(e.Scaled(PowerScale));
            foreach (var e in b.Payload) spell.Payload.Add(e.Scaled(PowerScale));
            spell.Synergies.AddRange(a.Synergies.Union(b.Synergies));
            // Synergies already inherited from a parent are in the payload; add the newly unlocked ones.
            var elements = spell.Elements;
            foreach (var rule in SpellSynergies.Rules)
            {
                if (spell.Synergies.Contains(rule.Name) || !rule.Requires(elements)) continue;
                spell.Synergies.Add(rule.Name);
                if (rule.First) spell.Payload.Insert(0, rule.Make());
                else spell.Payload.Add(rule.Make());
            }

            spell.Sequence = GenerateSequence(a, b, book, 3 + spell.Tier, spell.Name.GetHashCode());
            if (spell.Sequence == null) { error = "No free arrow sequence left."; return false; }
            result = spell;
            return true;
        }

        static SpellDelivery PickDelivery(SpellDefinition a, SpellDefinition b) =>
            a.Delivery.IsSelf && !b.Delivery.IsSelf ? b.Delivery : a.Delivery;

        static string UniqueName(string name, IList<SpellDefinition> book)
        {
            string n = name;
            for (int i = 2; book.Any(s => s.Name == n); i++) n = $"{name} {ToRoman(i)}";
            return n;
        }

        static string ToRoman(int i) => i switch { 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => i.ToString() };

        /// <summary>
        /// Starts with the first arrow of each parent (so it's memorable), then fills deterministically,
        /// rejecting any sequence that is a prefix of (or prefixed by) a known or base one.
        /// </summary>
        public static Arrow[] GenerateSequence(SpellDefinition a, SpellDefinition b, IList<SpellDefinition> book, int length, int seed)
        {
            var rng = new System.Random(seed);
            // Also avoid every base spell's sequence, so a rune found later can never clash.
            var reserved = book.Select(s => s.Sequence).Concat(SpellBook.Defaults().Select(s => s.Sequence)).ToList();
            for (int attempt = 0; attempt < 500; attempt++)
            {
                int len = length + attempt / 150; // grow if this length is crowded
                var seq = new Arrow[len];
                seq[0] = a.Sequence[0];
                // Runes can't repeat a stroke, so the second parent's first stroke is used only if it differs.
                int start = 1;
                if (b.Sequence[0] != seq[0]) { seq[1] = b.Sequence[0]; start = 2; }
                for (int i = start; i < len; i++)
                {
                    var next = (Arrow)rng.Next(3);
                    if (next >= seq[i - 1]) next++; // any of the three other directions
                    seq[i] = next;
                }
                if (!reserved.Any(r => Arrows.Conflicts(r, seq))) return seq;
            }
            return null;
        }
    }
}
