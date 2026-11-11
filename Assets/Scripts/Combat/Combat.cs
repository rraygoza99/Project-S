using UnityEngine;

namespace Spellcaster
{
    /// <summary>Shared damage helpers: hitting whatever a shot struck, and explosions (barrels, bombs, relics).</summary>
    public static class Combat
    {
        /// <summary>Damages the enemy or breakable prop that owns this collider. Returns true if it was an enemy.</summary>
        public static bool HitCollider(Collider col, float damage, Element element = Element.Arcane, bool bolt = false)
        {
            if (!col) return false;
            var e = col.GetComponentInParent<Enemy>();
            if (e) { e.TakeDamage(damage, element, bolt: bolt); return true; }
            var p = col.GetComponentInParent<Prop>();
            if (p) p.TakeDamage(damage);
            return false;
        }

        public static void DamageProps(Vector3 point, float radius, float damage, Prop except = null)
        {
            foreach (var col in Physics.OverlapSphere(point, radius, Layers.PropMask, QueryTriggerInteraction.Ignore))
            {
                var p = col.GetComponentInParent<Prop>();
                if (p && p != except) p.TakeDamage(damage);
            }
        }

        /// <summary>A fiery blast that hurts enemies, props and (optionally) the player.</summary>
        public static void Explode(Vector3 point, float radius, float enemyDamage, float playerDamage, Color color, Prop source = null)
        {
            Fx.Flash(point, color, radius * 1.4f, 0.45f, 0.6f);
            Fx.Flash(point, new Color(1f, 0.95f, 0.7f), radius * 0.6f, 0.2f, 0.8f);
            Sfx.Play("explode", 0.8f, Random.Range(0.85f, 1.05f));
            foreach (var col in Physics.OverlapSphere(point, radius, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
            {
                var e = col.GetComponent<Enemy>();
                if (!e || !e.IsAlive) continue;
                e.TakeDamage(enemyDamage, Element.Fire);
                Vector3 d = e.transform.position - point;
                d.y = 0f;
                e.Knockback(d.normalized * 6f);
            }
            var player = Player.Instance;
            if (playerDamage > 0f && player && (player.ChestPosition - point).sqrMagnitude < radius * radius)
                player.TakeDamage(playerDamage);
            DamageProps(point, radius, enemyDamage, source);
        }

        public static readonly Color BombColor = new Color(1f, 0.5f, 0.15f);

        /// <summary>Lobs a fire bomb where you're looking; it explodes on impact.</summary>
        public static void ThrowBomb(Player p)
        {
            var cam = p.Cam.transform;
            Vector3 origin = SpellSystem.CastOrigin;
            Vector3 velocity = cam.forward * 15f + Vector3.up * 3.5f;
            Projectile.Spawn(origin, velocity, BombColor, 0.55f, 0.22f, Layers.PlayerShotMask,
                (point, col) => Explode(point, 4f, 60f, 0f, BombColor),
                life: 3f, onExpire: point => Explode(point, 4f, 60f, 0f, BombColor), gravity: 14f);
            Sfx.Play("throw", 0.6f);
            WizardHands.Instance.Kick();
        }
    }
}
