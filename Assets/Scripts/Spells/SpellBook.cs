using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// THE spell config. Each base spell is one entry: sequence, cooldown, delivery and payload.
    /// To add a spell, add an entry here (reusing effects from SpellEffects.cs, or adding a new
    /// SpellEffect subclass there). Forged spells are appended to <see cref="Known"/> at runtime.
    /// Sequences use U/D/L/R or arrow glyphs.
    /// </summary>
    public static class SpellBook
    {
        public const int MaxSpells = 9;

        public static List<SpellDefinition> Defaults()
        {
            var list = BaseSpells();
            foreach (var s in list)
                if (s.Components.Count == 0) s.Components.Add(s.Id);
            return list;
        }

        static List<SpellDefinition> BaseSpells() => new List<SpellDefinition>
        {
            new SpellDefinition
            {
                Id = "fireball", Name = "Fireball", Noun = "Fireball", Adjective = "Blazing",
                Sequence = Arrows.Parse("↓→↑"), Cooldown = 3f, IconKey = "fireball",
                Color = new Color(1f, 0.45f, 0.12f),
                Delivery = new ProjectileDelivery(speed: 20f, size: 0.9f),
                Payload = { new DamageEffect(40f, Element.Fire), new ExplodeEffect(radius: 3.5f, falloff: 0.8f) },
            },
            new SpellDefinition
            {
                Id = "lightning", Name = "Lightning", Noun = "Lightning", Adjective = "Storm",
                Sequence = Arrows.Parse("→→↓→"), Cooldown = 6f, IconKey = "lightning",
                Color = new Color(0.55f, 0.8f, 1f),
                Delivery = new HitscanDelivery(range: 45f),
                Payload = { new DamageEffect(45f, Element.Storm), new ChainEffect(jumps: 2, range: 8f, falloff: 0.8f) },
            },
            new SpellDefinition
            {
                Id = "frostnova", Name = "Frost Nova", Noun = "Nova", Adjective = "Frozen",
                Sequence = Arrows.Parse("↓↓←→"), Cooldown = 10f, IconKey = "frost",
                Color = new Color(0.55f, 0.9f, 1f),
                Delivery = new NovaDelivery(radius: 6.5f),
                Payload = { new DamageEffect(8f, Element.Frost), new FreezeEffect(seconds: 3f) },
            },
            new SpellDefinition
            {
                Id = "heal", Name = "Heal", Noun = "Mending", Adjective = "Vital",
                Sequence = Arrows.Parse("↑↓↑↓"), Cooldown = 15f, IconKey = "heal",
                Color = new Color(0.35f, 1f, 0.45f),
                Delivery = new SelfDelivery(),
                Payload = { new HealEffect(40f) },
            },
        };

        static List<SpellDefinition> known;
        /// <summary>Spells the player can cast this session (base spells + forged ones).</summary>
        public static List<SpellDefinition> Known => known ??= Defaults();

        public static void Reset() => known = Defaults();
    }
}
