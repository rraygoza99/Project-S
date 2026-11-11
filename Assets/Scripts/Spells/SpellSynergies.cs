using System;
using System.Collections.Generic;

namespace Spellcaster
{
    /// <summary>
    /// Emergent effects unlocked when the Forge merges spells whose elements react with each other.
    /// Add a rule here (often with a new SpellEffect) to make combinations deeper.
    /// </summary>
    public static class SpellSynergies
    {
        public class Rule
        {
            public string Name;
            public string IconKey;
            public Func<HashSet<Element>, bool> Requires;
            public Func<SpellEffect> Make;
            /// <summary>Insert at the start of the payload (so it reads state before other effects change it).</summary>
            public bool First;
        }

        static bool Damaging(HashSet<Element> e) =>
            e.Contains(Element.Fire) || e.Contains(Element.Storm) || e.Contains(Element.Frost) || e.Contains(Element.Poison) || e.Contains(Element.Earth);

        public static readonly List<Rule> Rules = new List<Rule>
        {
            // Hit a frozen enemy with fire: it shatters for big bonus damage.
            new Rule { Name = "Shatter", IconKey = "shatter", First = true,
                Requires = e => e.Contains(Element.Fire) && e.Contains(Element.Frost),
                Make = () => new ShatterEffect(45f) },
            // Fire carried by lightning sets every struck enemy burning.
            new Rule { Name = "Ignite", IconKey = "burn",
                Requires = e => e.Contains(Element.Fire) && e.Contains(Element.Storm),
                Make = () => new IgniteEffect(dps: 12f, seconds: 4f) },
            // Thunder through ice: a concussive blast that throws enemies back.
            new Rule { Name = "Thunderclap",
                Requires = e => e.Contains(Element.Frost) && e.Contains(Element.Storm),
                Make = () => new KnockbackEffect(10f) },
            // Life magic woven into an attack drains health from what it hits.
            new Rule { Name = "Lifesteal",
                Requires = e => e.Contains(Element.Life) && Damaging(e),
                Make = () => new LifestealEffect(0.25f) },
            // Fire meets poison gas: every hit bursts into flame around the target.
            new Rule { Name = "Combustion",
                Requires = e => e.Contains(Element.Fire) && e.Contains(Element.Poison),
                Make = () => new ExplodeEffect(radius: 2.5f, falloff: 0.6f) },
            // Lightning through the ground: struck enemies are stunned.
            new Rule { Name = "Tremor",
                Requires = e => e.Contains(Element.Earth) && e.Contains(Element.Storm),
                Make = () => new StunEffect(1.2f) },
        };
    }
}
