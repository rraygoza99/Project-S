using UnityEngine;

namespace Spellcaster
{
    // Effects added with the meta-progression spells (Poison Cloud, Quake, Blink...). Same rules as
    // SpellEffects.cs: each works with every delivery, the Forge and the relics.

    /// <summary>Poison damage over time (stacks with burning).</summary>
    public class PoisonEffect : SpellEffect
    {
        public float Dps, Seconds;
        public PoisonEffect(float dps, float seconds) { Dps = dps; Seconds = seconds; }
        public override string Name => "Poison";
        public override Element Element => Element.Poison;
        public override SpellEffect Scaled(float p) => new PoisonEffect(Dps * p, Seconds);
        public override string Describe() => $"poison {Dps:0}/s for {Seconds:0}s";
        public override void ApplyTo(SpellCast cast, Enemy target, float scale) => target.Poison(Dps * scale * cast.Power, Seconds);
    }

    /// <summary>Stops enemies in place (not ice: Shatter and Permafrost don't react to it).</summary>
    public class StunEffect : SpellEffect
    {
        public float Seconds;
        public StunEffect(float seconds) { Seconds = seconds; }
        public override string Name => "Stun";
        public override Element Element => Element.Earth;
        public override SpellEffect Scaled(float p) => new StunEffect(Seconds * Mathf.Lerp(1f, p, 0.5f));
        public override string Describe() => $"stun {Seconds:0.#}s";
        public override void ApplyTo(SpellCast cast, Enemy target, float scale)
        {
            target.Stun(Seconds * Mathf.Max(0.5f, scale));
            Fx.Flash(target.Center, new Color(1f, 0.85f, 0.45f), 1.3f, 0.3f, 0.8f);
        }
    }

    /// <summary>Leaves a toxic cloud where the spell lands that keeps poisoning whatever is inside.</summary>
    public class CloudEffect : SpellEffect
    {
        public float Radius, Seconds, Dps;
        public CloudEffect(float radius, float seconds, float dps) { Radius = radius; Seconds = seconds; Dps = dps; }
        public override string Name => "Cloud";
        public override Element Element => Element.Poison;
        public override EffectRole Role => EffectRole.Spread;
        public override SpellEffect Scaled(float p) => new CloudEffect(Radius, Seconds, Dps * p);
        public override string Describe() => $"cloud {Radius * 1f:0.#}m for {Seconds:0}s";
        public override void Spread(SpellCast cast, Enemy from, Vector3 point)
        {
            point.y = 0f;
            PoisonCloud.Create(point, Radius * cast.Stats.AreaMult, Seconds, Dps * cast.Power, cast.Spell.Color);
        }
    }

    /// <summary>Teleports the caster forward along the view (stops short of walls).</summary>
    public class BlinkEffect : SpellEffect
    {
        public float Distance;
        public BlinkEffect(float distance) { Distance = distance; }
        public override string Name => "Blink";
        public override Element Element => Element.Arcane;
        public override EffectRole Role => EffectRole.Caster;
        public override SpellEffect Scaled(float p) => new BlinkEffect(Distance);
        public override string Describe() => $"blink {Distance:0}m";
        public override void ApplyToCaster(SpellCast cast) => cast.Caster.Blink(Distance, cast.Spell.Color);
    }

    /// <summary>A lingering poison zone (Poison Cloud).</summary>
    public class PoisonCloud : MonoBehaviour
    {
        float radius, life, dps, tick;
        Transform[] puffs;
        float[] phase;

        public static void Create(Vector3 point, float radius, float seconds, float dps, Color color)
        {
            var go = new GameObject("PoisonCloud");
            if (Dungeon.Instance && Dungeon.Instance.LevelRoot) go.transform.SetParent(Dungeon.Instance.LevelRoot, true);
            go.transform.position = point;
            var floor = Fx.CreateQuad(go.transform, new Color(color.r, color.g, color.b, 0.35f), radius * 2f, billboard: false);
            floor.transform.localPosition = Vector3.up * 0.05f;
            floor.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var c = go.AddComponent<PoisonCloud>();
            c.radius = radius;
            c.life = seconds;
            c.dps = dps;
            c.puffs = new Transform[7];
            c.phase = new float[7];
            for (int i = 0; i < c.puffs.Length; i++)
            {
                var q = Fx.CreateQuad(go.transform, new Color(color.r * 0.8f, color.g, color.b * 0.6f, 0.5f), radius * 0.8f);
                Vector2 off = Random.insideUnitCircle * radius * 0.7f;
                q.transform.localPosition = new Vector3(off.x, 0.6f + Random.value, off.y);
                c.puffs[i] = q.transform;
                c.phase[i] = Random.value * 6f;
            }
            Sfx.Play("burn", 0.6f, 0.6f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            for (int i = 0; i < puffs.Length; i++)
            {
                var p = puffs[i].localPosition;
                p.y = 0.8f + 0.3f * Mathf.Sin(Time.time * 1.5f + phase[i]);
                puffs[i].localPosition = p;
            }
            if (life <= 0f) { Destroy(gameObject); return; }
            tick -= dt;
            if (tick > 0f) return;
            tick = 0.5f;
            Vector3 c = transform.position;
            foreach (var e in Enemy.All.ToArray())
            {
                if (!e.IsAlive) continue;
                Vector3 d = e.transform.position - c;
                d.y = 0f;
                if (d.sqrMagnitude <= radius * radius) e.Poison(dps, 2f);
            }
        }
    }
}
