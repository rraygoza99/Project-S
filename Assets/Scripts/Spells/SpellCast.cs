using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// One casting of a spell. The delivery decides what gets hit; Resolve then applies the payload:
    /// Direct effects to every primary target, Spread effects to find more targets (which receive the
    /// Direct effects at reduced strength), then Caster effects once.
    /// </summary>
    public class SpellCast
    {
        public readonly SpellDefinition Spell;
        public readonly Player Caster;
        public float DamageDealt;
        readonly HashSet<Enemy> hit = new HashSet<Enemy>();
        bool resolved;

        public SpellCast(SpellDefinition spell, Player caster)
        {
            Spell = spell;
            Caster = caster;
        }

        public void Launch() => Spell.Delivery.Launch(this);

        public bool WasHit(Enemy e) => hit.Contains(e);

        public void Resolve(Enemy primary, Vector3 point) =>
            Resolve(primary ? new List<Enemy> { primary } : new List<Enemy>(), point);

        public void Resolve(List<Enemy> primaries, Vector3 point)
        {
            if (resolved) return;
            resolved = true;

            foreach (var e in primaries) ApplyDirect(e, 1f);

            foreach (var effect in Spell.Payload)
            {
                if (effect.Role != EffectRole.Spread) continue;
                if (primaries.Count == 0) effect.Spread(this, null, point);
                else foreach (var e in primaries) effect.Spread(this, e, e ? e.Center : point);
            }

            foreach (var effect in Spell.Payload)
                if (effect.Role == EffectRole.Caster) effect.ApplyToCaster(this);
        }

        /// <summary>Applies every Direct effect to one enemy (once per cast).</summary>
        public void ApplyDirect(Enemy e, float scale)
        {
            if (!e || !hit.Add(e)) return;
            foreach (var effect in Spell.Payload)
            {
                if (effect.Role != EffectRole.Direct) continue;
                if (!e.IsAlive && !(effect is DamageEffect)) continue;
                effect.ApplyTo(this, e, scale);
            }
        }
    }
}
