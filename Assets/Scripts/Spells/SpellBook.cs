using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// THE spell config. Each base spell is one entry: sequence, cooldown, delivery and payload.
    /// To add a spell, add an entry here (reusing effects from SpellEffects.cs, or adding a new
    /// SpellEffect subclass there). A run starts with <see cref="StartingIds"/>; runes and the Forge add to <see cref="Known"/>.
    /// Sequences use U/D/L/R or arrow glyphs and are drawn as runes with the mouse, so a sequence may not
    /// repeat the same stroke twice in a row, and none may be a prefix of another.
    /// </summary>
    public static class SpellBook
    {
        public const int MaxSpells = 9;

        /// <summary>Spells every run starts with; the rest are found as runes.</summary>
        public static readonly string[] StartingIds = { "fireball" };

        /// <summary>Every base spell, fresh copies (forge and runes never share instances).</summary>
        public static List<SpellDefinition> Defaults()
        {
            var list = BaseSpells();
            foreach (var s in list)
                if (s.Components.Count == 0) s.Components.Add(s.Id);
            return list;
        }

        public static IEnumerable<string> BaseIds => Defaults().Select(s => s.Id);

        /// <summary>Base spells that can appear in runs: the originals plus the unlocked ones.</summary>
        public static List<SpellDefinition> Available => Defaults().Where(s => s.IsAvailable).ToList();

        public static SpellDefinition CreateBase(string id) => Defaults().FirstOrDefault(s => s.Id == id);

        public static bool Knows(string id) => Known.Any(s => s.Id == id);

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
                Sequence = Arrows.Parse("→↓→"), Cooldown = 6f, IconKey = "lightning",
                Color = new Color(0.55f, 0.8f, 1f),
                Delivery = new HitscanDelivery(range: 45f),
                Payload = { new DamageEffect(45f, Element.Storm), new ChainEffect(jumps: 2, range: 8f, falloff: 0.8f) },
            },
            new SpellDefinition
            {
                Id = "frostnova", Name = "Frost Nova", Noun = "Nova", Adjective = "Frozen",
                Sequence = Arrows.Parse("↓←↑→"), Cooldown = 10f, IconKey = "frost",
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
            new SpellDefinition
            {
                Id = "frostshard", Name = "Frost Shard", Noun = "Shard", Adjective = "Icy",
                Sequence = Arrows.Parse("←↑→"), Cooldown = 2.5f, IconKey = "frostshard",
                Color = new Color(0.7f, 0.95f, 1f),
                Delivery = new ProjectileDelivery(speed: 32f, size: 0.55f),
                Payload = { new DamageEffect(22f, Element.Frost), new FreezeEffect(seconds: 1.2f) },
            },
            new SpellDefinition
            {
                Id = "thunderring", Name = "Thunder Ring", Noun = "Storm", Adjective = "Thundering",
                Sequence = Arrows.Parse("↑→↓"), Cooldown = 8f, IconKey = "thunderring",
                Color = new Color(0.75f, 0.7f, 1f),
                Delivery = new NovaDelivery(radius: 7f),
                Payload = { new DamageEffect(25f, Element.Storm), new KnockbackEffect(9f) },
            },

            // ---- unlocked through achievements (Progress.cs) ----
            new SpellDefinition
            {
                Id = "poisoncloud", Name = "Poison Cloud", Noun = "Miasma", Adjective = "Toxic", StartsLocked = true,
                Sequence = Arrows.Parse("←↓→"), Cooldown = 7f, IconKey = "poisoncloud",
                Color = new Color(0.5f, 1f, 0.3f),
                Delivery = new ProjectileDelivery(speed: 14f, size: 0.9f),
                Payload = { new DamageEffect(10f, Element.Poison), new PoisonEffect(dps: 8f, seconds: 4f), new CloudEffect(radius: 4f, seconds: 5f, dps: 8f) },
            },
            new SpellDefinition
            {
                Id = "arcanemissiles", Name = "Arcane Missiles", Noun = "Missiles", Adjective = "Arcane", StartsLocked = true,
                Sequence = Arrows.Parse("→↑→"), Cooldown = 4f, IconKey = "arcanemissiles",
                Color = new Color(0.85f, 0.5f, 1f),
                Delivery = new HomingDelivery(count: 5, speed: 16f),
                Payload = { new DamageEffect(14f, Element.Arcane) },
            },
            new SpellDefinition
            {
                Id = "blink", Name = "Blink", Noun = "Step", Adjective = "Shifting", StartsLocked = true,
                Sequence = Arrows.Parse("↑←↑"), Cooldown = 5f, IconKey = "blink",
                Color = new Color(0.6f, 0.9f, 1f),
                Delivery = new SelfDelivery(),
                Payload = { new BlinkEffect(distance: 8f) },
            },
            new SpellDefinition
            {
                Id = "quake", Name = "Quake", Noun = "Quake", Adjective = "Rumbling", StartsLocked = true,
                Sequence = Arrows.Parse("↓←↓"), Cooldown = 9f, IconKey = "quake",
                Color = new Color(0.85f, 0.65f, 0.35f),
                Delivery = new NovaDelivery(radius: 8f),
                Payload = { new DamageEffect(20f, Element.Earth), new StunEffect(seconds: 2.5f) },
            },
            new SpellDefinition
            {
                Id = "lifedrain", Name = "Life Drain", Noun = "Drain", Adjective = "Draining", StartsLocked = true,
                Sequence = Arrows.Parse("←↑←"), Cooldown = 5f, IconKey = "lifedrain",
                Color = new Color(0.85f, 0.2f, 0.4f),
                Delivery = new HitscanDelivery(range: 35f),
                Payload = { new DamageEffect(30f, Element.Life), new LifestealEffect(0.5f) },
            },
            new SpellDefinition
            {
                Id = "meteor", Name = "Meteor", Noun = "Meteor", Adjective = "Falling", StartsLocked = true,
                Sequence = Arrows.Parse("→↓←"), Cooldown = 14f, IconKey = "meteor",
                Color = new Color(1f, 0.4f, 0.15f),
                Delivery = new MeteorDelivery(range: 40f),
                Payload = { new DamageEffect(60f, Element.Fire), new ExplodeEffect(radius: 6f, falloff: 1f) },
            },
        };

        static List<SpellDefinition> known;
        /// <summary>Spells the player can cast this run (starting spells + runes found + forged ones).</summary>
        public static List<SpellDefinition> Known => known ??= Defaults().Where(sp => StartingIds.Contains(sp.Id)).ToList();

        /// <summary>A new run starts with only the starting spells.</summary>
        public static void StartRun() => known = null;

        /// <summary>Know every available base spell (practice tool, tests).</summary>
        public static void LearnAll()
        {
            foreach (var sp in Available)
                if (!Knows(sp.Id)) Known.Add(sp);
        }
    }
}
