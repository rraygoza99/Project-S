using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>How a spell reaches its targets. Every delivery ends by calling SpellCast.Resolve.</summary>
    public abstract class SpellDelivery
    {
        public abstract string Name { get; }
        public abstract void Launch(SpellCast cast);
        public abstract SpellDelivery Clone();
        public virtual bool IsSelf => false;

        /// <summary>An extra hit of the same cast (pierce): same power and modifiers, never lifesteals.</summary>
        public static SpellCast Copy(SpellCast cast) => ProjectileDelivery.PierceCopy(cast);
    }

    /// <summary>A travelling glowing orb that resolves where it hits.</summary>
    public class ProjectileDelivery : SpellDelivery
    {
        public float Speed, Size;
        public ProjectileDelivery(float speed, float size) { Speed = speed; Size = size; }
        public override string Name => "projectile";
        public override SpellDelivery Clone() => new ProjectileDelivery(Speed, Size);

        public override void Launch(SpellCast cast)
        {
            Vector3 origin = SpellSystem.CastOriginFor(cast.Caster);
            Vector3 dir = SpellSystem.AimDirectionFor(cast.Caster, origin);
            if (cast.Mods.AimYaw != 0f) dir = Quaternion.AngleAxis(cast.Mods.AimYaw, Vector3.up) * dir;
            float size = Size * cast.Mods.SizeMult;
            Fire(cast, origin, dir, size);
            // Splitting Prism relic: extra, weaker projectiles fanned out to the sides.
            int split = cast.Stats.ProjectileSplit;
            for (int i = 0; i < split; i++)
            {
                float ang = (i / 2 + 1) * 12f * (i % 2 == 0 ? 1f : -1f);
                var extra = new SpellCast(cast.Spell, cast.Caster) { Power = cast.Power * 0.6f, Mods = cast.Mods };
                Fire(extra, origin, Quaternion.AngleAxis(ang, Vector3.up) * dir, size * 0.75f);
            }
        }

        void Fire(SpellCast cast, Vector3 origin, Vector3 dir, float size)
        {
            var color = cast.Spell.Color;
            var p = Projectile.Spawn(origin, dir * Speed, color, size, 0.2f * cast.Mods.SizeMult,
                Layers.PlayerShotMask,
                (point, col) =>
                {
                    Fx.Flash(point, color, size * 1.5f, 0.25f, 1.5f);
                    var prop = col ? col.GetComponentInParent<Prop>() : null;
                    if (prop) prop.TakeDamage(30f * cast.Power);
                    cast.Resolve(col ? col.GetComponentInParent<Enemy>() : null, point);
                },
                life: 4f,
                onExpire: point => cast.Resolve((Enemy)null, point));
            if (cast.Mods.Pierce) p.PassThroughEnemies(PierceHit(cast, color, size));
        }

        /// <summary>Pierce: each enemy passed through takes its own hit (the first one is the main cast).</summary>
        internal static Projectile.HitHandler PierceHit(SpellCast cast, Color color, float size)
        {
            bool first = true;
            return (point, col) =>
            {
                Fx.Flash(point, color, size * 1.5f, 0.25f, 1.5f);
                var c = first ? cast : PierceCopy(cast);
                first = false;
                c.Resolve(col ? col.GetComponentInParent<Enemy>() : null, point);
            };
        }

        internal static SpellCast PierceCopy(SpellCast cast)
        {
            var mods = cast.Mods.Clone();
            mods.NoLifesteal = true;
            return new SpellCast(cast.Spell, cast.Caster) { Power = cast.Power, Mods = mods };
        }
    }

    /// <summary>Instant ray from the staff to whatever is under the crosshair.</summary>
    public class HitscanDelivery : SpellDelivery
    {
        public float Range;
        public HitscanDelivery(float range) { Range = range; }
        public override string Name => "instant bolt";
        public override SpellDelivery Clone() => new HitscanDelivery(Range);

        public override void Launch(SpellCast cast)
        {
            var cam = cast.Caster.Cam.transform;
            Vector3 origin = SpellSystem.CastOriginFor(cast.Caster);
            Vector3 fwd = cast.Mods.AimYaw != 0f ? Quaternion.AngleAxis(cast.Mods.AimYaw, Vector3.up) * cam.forward : cam.forward;
            Vector3 end = cam.position + fwd * Range;
            Enemy target = null;
            if (cast.Mods.Pierce) { PierceRay(cast, cam.position, fwd); return; }
            if (Physics.Raycast(cam.position, fwd, out var hit, Range, Layers.PlayerShotMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                target = hit.collider.GetComponentInParent<Enemy>();
                if (target) end = target.Center;
                var prop = hit.collider.GetComponentInParent<Prop>();
                if (prop) prop.TakeDamage(30f * cast.Power);
            }
            LightningFx.Bolt(origin, end, cast.Spell.Color, 0.3f);
            DevMode.Line(origin, end, DevMode.SpellRay);
            Fx.Flash(end, cast.Spell.Color, 1.4f, 0.3f, 1f);
            Sfx.Play("zap", 0.7f, Random.Range(0.95f, 1.05f));
            cast.Resolve(target, end);
        }

        /// <summary>Pierce: the ray goes through the first enemy and also hits the next one behind it.</summary>
        void PierceRay(SpellCast cast, Vector3 from, Vector3 fwd)
        {
            var hits = Physics.RaycastAll(from, fwd, Range, Layers.PlayerShotMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Vector3 origin = SpellSystem.CastOriginFor(cast.Caster), last = from + fwd * Range;
            int struck = 0;
            var seen = new HashSet<Enemy>();
            foreach (var h in hits)
            {
                var e = h.collider.GetComponentInParent<Enemy>();
                if (!e)
                {
                    var prop = h.collider.GetComponentInParent<Prop>();
                    if (prop) prop.TakeDamage(30f * cast.Power);
                    last = h.point;
                    break;
                }
                if (!seen.Add(e)) continue;
                last = e.Center;
                var c = struck == 0 ? cast : SpellDelivery.Copy(cast);
                Fx.Flash(e.Center, cast.Spell.Color, 1.4f, 0.3f, 1f);
                c.Resolve(e, e.Center);
                if (++struck >= 2) break;
            }
            LightningFx.Bolt(origin, last, cast.Spell.Color, 0.3f);
            DevMode.Line(origin, last, DevMode.SpellRay);
            Sfx.Play("zap", 0.7f, Random.Range(0.95f, 1.05f));
            if (struck == 0) cast.Resolve((Enemy)null, last);
        }
    }

    /// <summary>Expanding ring around the caster that hits everything in range.</summary>
    public class NovaDelivery : SpellDelivery
    {
        public float Radius;
        public NovaDelivery(float radius) { Radius = radius; }
        public override string Name => "nova";
        public override SpellDelivery Clone() => new NovaDelivery(Radius);

        public override void Launch(SpellCast cast)
        {
            Vector3 center = cast.Caster.transform.position + cast.Mods.Offset;
            float radius = Radius * cast.AreaMult;
            Fx.Ring(center + Vector3.up * 0.1f, cast.Spell.Color, radius, 0.45f);
            Fx.AreaMarker(center, cast.Spell.Color, radius, 0.6f);
            DevMode.Ring(center + Vector3.up * 0.1f, radius, DevMode.SpellArea);
            Fx.Flash(center + Vector3.up * 1f, cast.Spell.Color, radius * 0.8f, 0.35f, 1.2f);
            if (cast.Spell.Payload.Exists(e => e is DamageEffect)) Combat.DamageProps(center + Vector3.up, radius, 20f * cast.Power);
            Sfx.Play(cast.Spell.Payload.Exists(e => e is FreezeEffect) ? "freeze" : "explode", 0.7f);
            var targets = new List<Enemy>();
            foreach (var e in Enemy.All)
            {
                Vector3 d = e.transform.position - center;
                d.y = 0f;
                if (e.IsAlive && d.sqrMagnitude <= radius * radius) targets.Add(e);
            }
            cast.Resolve(targets, center + Vector3.up);
        }
    }

    /// <summary>Affects only the caster.</summary>
    public class SelfDelivery : SpellDelivery
    {
        public override string Name => "self";
        public override bool IsSelf => true;
        public override SpellDelivery Clone() => new SelfDelivery();

        public override void Launch(SpellCast cast)
        {
            if (!cast.Spell.Payload.Exists(e => e is DashEffect)) // the dash's feedback is the view kick, not a flash
                Fx.Flash(SpellSystem.CastOriginFor(cast.Caster), cast.Spell.Color, 1.5f, 0.5f, 1.5f);
            cast.Resolve(new List<Enemy>(), cast.Caster.ChestPosition);
        }
    }
}
