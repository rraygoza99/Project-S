using UnityEngine;

namespace Spellcaster
{
    /// <summary>How an effect participates when a spell resolves.</summary>
    public enum EffectRole
    {
        /// <summary>Applied to each enemy the spell hits.</summary>
        Direct,
        /// <summary>Finds extra targets around a hit and applies every Direct effect to them (explosion, chain...).</summary>
        Spread,
        /// <summary>Applied once to the caster after the hits resolve (heal, lifesteal...).</summary>
        Caster,
    }

    /// <summary>
    /// A building block of a spell. Spells are a Delivery plus a list of effects, so new spells are new
    /// combinations, and every new effect type automatically works with all deliveries and with the Forge.
    /// To add an effect: subclass this, implement the role-specific method, Scaled() and Describe().
    /// </summary>
    public abstract class SpellEffect
    {
        public abstract string Name { get; }
        public virtual Element Element => Element.Arcane;
        public virtual EffectRole Role => EffectRole.Direct;
        /// <summary>Copy with numbers multiplied by power (used when the Forge merges spells).</summary>
        public abstract SpellEffect Scaled(float power);
        public abstract string Describe();

        public virtual void ApplyTo(SpellCast cast, Enemy target, float scale) { }
        public virtual void Spread(SpellCast cast, Enemy from, Vector3 point) { }
        public virtual void ApplyToCaster(SpellCast cast) { }
    }

    // ---------------- Direct effects ----------------

    public class DamageEffect : SpellEffect
    {
        public float Amount;
        readonly Element element;
        public DamageEffect(float amount, Element element) { Amount = amount; this.element = element; }
        public override string Name => element + " damage";
        public override Element Element => element;
        public override SpellEffect Scaled(float p) => new DamageEffect(Amount * p, element);
        public override string Describe() => $"{Amount:0} {element.ToString().ToLower()} dmg";
        public override void ApplyTo(SpellCast cast, Enemy target, float scale) =>
            cast.DamageDealt += target.TakeDamage(Amount * scale, element);
    }

    public class FreezeEffect : SpellEffect
    {
        public float Seconds;
        public FreezeEffect(float seconds) { Seconds = seconds; }
        public override string Name => "Freeze";
        public override Element Element => Element.Frost;
        public override SpellEffect Scaled(float p) => new FreezeEffect(Seconds * Mathf.Lerp(1f, p, 0.5f));
        public override string Describe() => $"freeze {Seconds:0.#}s";
        public override void ApplyTo(SpellCast cast, Enemy target, float scale)
        {
            target.Freeze(Seconds * Mathf.Max(0.5f, scale));
            Fx.Flash(target.Center, new Color(0.6f, 0.9f, 1f), 1.6f, 0.4f, 0.6f);
        }
    }

    /// <summary>Damage over time. Base spells don't use it; the Fire+Storm synergy adds it.</summary>
    public class IgniteEffect : SpellEffect
    {
        public float Dps, Seconds;
        public IgniteEffect(float dps, float seconds) { Dps = dps; Seconds = seconds; }
        public override string Name => "Ignite";
        public override Element Element => Element.Fire;
        public override SpellEffect Scaled(float p) => new IgniteEffect(Dps * p, Seconds);
        public override string Describe() => $"burn {Dps:0}/s for {Seconds:0}s";
        public override void ApplyTo(SpellCast cast, Enemy target, float scale)
        {
            target.Ignite(Dps * scale, Seconds);
            Sfx.Play("burn", 0.35f, Random.Range(0.9f, 1.1f));
        }
    }

    /// <summary>Bonus damage against frozen enemies, consuming the freeze. Added by the Fire+Frost synergy.</summary>
    public class ShatterEffect : SpellEffect
    {
        public float Bonus;
        public ShatterEffect(float bonus) { Bonus = bonus; }
        public override string Name => "Shatter";
        public override Element Element => Element.Frost;
        public override SpellEffect Scaled(float p) => new ShatterEffect(Bonus * p);
        public override string Describe() => $"shatter frozen +{Bonus:0}";
        public override void ApplyTo(SpellCast cast, Enemy target, float scale)
        {
            if (!target.ConsumeFreeze()) return;
            cast.DamageDealt += target.TakeDamage(Bonus * scale, Element.Frost);
            Fx.Flash(target.Center, new Color(0.8f, 0.95f, 1f), 2.4f, 0.35f, 1.2f);
            Sfx.Play("shatter", 0.6f, Random.Range(0.9f, 1.1f));
        }
    }

    /// <summary>Pushes enemies away from the caster. Added by the Frost+Storm synergy.</summary>
    public class KnockbackEffect : SpellEffect
    {
        public float Force;
        public KnockbackEffect(float force) { Force = force; }
        public override string Name => "Knockback";
        public override Element Element => Element.Storm;
        public override SpellEffect Scaled(float p) => new KnockbackEffect(Force * p);
        public override string Describe() => "knockback";
        public override void ApplyTo(SpellCast cast, Enemy target, float scale)
        {
            Vector3 d = target.transform.position - cast.Caster.transform.position;
            d.y = 0f;
            target.Knockback(d.normalized * Force * scale);
        }
    }

    // ---------------- Spread effects ----------------

    public class ExplodeEffect : SpellEffect
    {
        public float Radius, Falloff;
        public ExplodeEffect(float radius, float falloff) { Radius = radius; Falloff = falloff; }
        public override string Name => "Explosion";
        public override Element Element => Element.Fire;
        public override EffectRole Role => EffectRole.Spread;
        public override SpellEffect Scaled(float p) => new ExplodeEffect(Radius * Mathf.Lerp(1f, p, 0.5f), Falloff);
        public override string Describe() => $"explodes {Radius:0.#}m";
        public override void Spread(SpellCast cast, Enemy from, Vector3 point)
        {
            Fx.Flash(point, Color.Lerp(cast.Spell.Color, new Color(1f, 0.6f, 0.2f), 0.5f), Radius * 1.3f, 0.45f, 0.6f);
            Fx.Flash(point, new Color(1f, 0.95f, 0.7f), Radius * 0.6f, 0.2f, 0.8f);
            Sfx.Play("explode", 0.7f, Random.Range(0.9f, 1.1f));
            var hits = Physics.OverlapSphere(point, Radius, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            foreach (var col in hits)
            {
                var e = col.GetComponent<Enemy>();
                if (e && e != from) cast.ApplyDirect(e, Falloff);
            }
        }
    }

    public class ChainEffect : SpellEffect
    {
        public int Jumps;
        public float Range, Falloff;
        public ChainEffect(int jumps, float range, float falloff) { Jumps = jumps; Range = range; Falloff = falloff; }
        public override string Name => "Chain";
        public override Element Element => Element.Storm;
        public override EffectRole Role => EffectRole.Spread;
        public override SpellEffect Scaled(float p) => new ChainEffect(Jumps, Range, Falloff);
        public override string Describe() => $"chains to {Jumps}";
        public override void Spread(SpellCast cast, Enemy from, Vector3 point)
        {
            Vector3 pos = from ? from.Center : point;
            float scale = 1f;
            for (int j = 0; j < Jumps; j++)
            {
                Enemy next = null;
                float best = Range * Range;
                foreach (var e in Enemy.All)
                {
                    if (!e.IsAlive || cast.WasHit(e)) continue;
                    float d = (e.Center - pos).sqrMagnitude;
                    if (d < best) { best = d; next = e; }
                }
                if (!next) break;
                scale *= Falloff;
                LightningFx.Bolt(pos, next.Center, cast.Spell.Color, 0.25f);
                pos = next.Center;
                cast.ApplyDirect(next, scale);
            }
        }
    }

    // ---------------- Caster effects ----------------

    public class HealEffect : SpellEffect
    {
        public float Amount;
        public HealEffect(float amount) { Amount = amount; }
        public override string Name => "Heal";
        public override Element Element => Element.Life;
        public override EffectRole Role => EffectRole.Caster;
        public override SpellEffect Scaled(float p) => new HealEffect(Amount * p);
        public override string Describe() => $"heal {Amount:0}";
        public override void ApplyToCaster(SpellCast cast)
        {
            cast.Caster.Heal(Amount);
            Sfx.Play("heal", 0.7f);
        }
    }

    /// <summary>Heals the caster for a share of the damage this cast dealt. Added by the Life+damage synergy.</summary>
    public class LifestealEffect : SpellEffect
    {
        public float Fraction;
        public LifestealEffect(float fraction) { Fraction = fraction; }
        public override string Name => "Lifesteal";
        public override Element Element => Element.Life;
        public override EffectRole Role => EffectRole.Caster;
        public override SpellEffect Scaled(float p) => new LifestealEffect(Fraction);
        public override string Describe() => $"lifesteal {Fraction * 100:0}%";
        public override void ApplyToCaster(SpellCast cast)
        {
            if (cast.DamageDealt > 0f) cast.Caster.Heal(cast.DamageDealt * Fraction);
        }
    }
}
