using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>A volley of small missiles that home in on the enemies nearest the crosshair.</summary>
    public class HomingDelivery : SpellDelivery
    {
        public int Count;
        public float Speed;
        public HomingDelivery(int count, float speed) { Count = count; Speed = speed; }
        public override string Name => $"{Count} homing missiles";
        public override SpellDelivery Clone() => new HomingDelivery(Count, Speed);

        public override void Launch(SpellCast cast)
        {
            var cam = cast.Caster.Cam.transform;
            Vector3 origin = SpellSystem.CastOrigin;
            // Targets: living enemies in front, closest to the crosshair first.
            var targets = Enemy.All.Where(e => e.IsAlive && Vector3.Dot((e.Center - cam.position).normalized, cam.forward) > 0.2f)
                .OrderBy(e => Vector3.Angle(cam.forward, e.Center - cam.position)).Take(Count).ToList();
            var color = cast.Spell.Color;
            for (int i = 0; i < Count; i++)
            {
                // Each missile is its own cast (its own hit), fanned out, then curving onto a target.
                var c = i == 0 ? cast : new SpellCast(cast.Spell, cast.Caster) { Power = cast.Power };
                float ang = (i - (Count - 1) * 0.5f) * 14f;
                Vector3 dir = Quaternion.AngleAxis(ang, cam.up) * Quaternion.AngleAxis(-12f, cam.right) * cam.forward;
                var p = Projectile.Spawn(origin, dir * Speed, color, 0.4f, 0.15f, Layers.PlayerShotMask,
                    (point, col) =>
                    {
                        Fx.Flash(point, color, 0.9f, 0.2f, 1.5f);
                        var prop = col ? col.GetComponentInParent<Prop>() : null;
                        if (prop) prop.TakeDamage(10f * c.Power);
                        c.Resolve(col ? col.GetComponentInParent<Enemy>() : null, point);
                    }, life: 3f, onExpire: point => c.Resolve((Enemy)null, point));
                if (targets.Count > 0) p.Home(targets[i % targets.Count], 260f);
            }
            Sfx.Play("bolt", 0.6f, 1.4f);
        }
    }

    /// <summary>Calls a meteor down from the ceiling onto the spot under the crosshair, with a warning ring.</summary>
    public class MeteorDelivery : SpellDelivery
    {
        public float Range;
        public MeteorDelivery(float range) { Range = range; }
        public override string Name => "meteor";
        public override SpellDelivery Clone() => new MeteorDelivery(Range);

        public override void Launch(SpellCast cast)
        {
            var cam = cast.Caster.Cam.transform;
            Vector3 point = cam.position + cam.forward * Range;
            if (Physics.Raycast(cam.position, cam.forward, out var hit, Range, Layers.PlayerShotMask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                var e = hit.collider.GetComponentInParent<Enemy>();
                if (e) point = e.transform.position;
            }
            // Land on the floor below that point.
            if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out var ground, 10f, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                point = ground.point;
            else point.y = 0f;
            var color = cast.Spell.Color;
            Fx.Ring(point + Vector3.up * 0.05f, color, 5f, 0.6f);
            Vector3 start = point + new Vector3(0.6f, Dungeon.WallH - 0.4f, 0.6f);
            Projectile.Spawn(start, (point - start).normalized * 8f, color, 1.8f, 0.4f, Layers.WorldMask,
                (p, col) =>
                {
                    Fx.Flash(p + Vector3.up, new Color(1f, 0.9f, 0.6f), 5f, 0.4f, 1.2f);
                    cast.Resolve((Enemy)null, p + Vector3.up * 0.5f);
                }, life: 3f, onExpire: p => cast.Resolve((Enemy)null, p));
            Sfx.Play("cast", 0.8f, 0.6f);
        }
    }
}
