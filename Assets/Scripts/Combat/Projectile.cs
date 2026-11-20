using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>Kinematic glowing projectile using sphere casts (no rigidbodies).</summary>
    public class Projectile : MonoBehaviour
    {
        public delegate void HitHandler(Vector3 point, Collider hit);

        /// <summary>Live projectiles (dev mode hitboxes, tests).</summary>
        public static readonly List<Projectile> All = new List<Projectile>();
        public float Radius => radius;
        public int Mask => mask;
        public Vector3 Velocity => velocity;
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        Vector3 velocity;
        float radius, life, pulse;
        int mask;
        /// <summary>Host: this projectile's copy on the other players' PCs (0 = none).</summary>
        int netId;

        void OnDestroy()
        {
            if (netId != 0 && Net.NetCombat.HostMirroring) Net.NetCombat.ProjectileEnded(netId, transform.position);
        }
        HitHandler onHit;
        System.Action<Vector3> onExpire;
        Transform visual;
        float baseSize, gravity;
        Enemy homingTarget;
        float turnRate;
        HitHandler onPass;
        HashSet<Collider> passed;
        static readonly Comparison<RaycastHit> ByDistance = (a, b) => a.distance.CompareTo(b.distance);

        /// <summary>Pierce: enemies hit call onPass and the projectile keeps going (walls still stop it).</summary>
        public void PassThroughEnemies(HitHandler onPass)
        {
            this.onPass = onPass;
            passed = new HashSet<Collider>();
        }

        /// <summary>Steers toward the enemy (degrees per second) while it lives.</summary>
        public void Home(Enemy target, float degreesPerSecond)
        {
            homingTarget = target;
            turnRate = degreesPerSecond;
            if (netId != 0) Net.NetCombat.ProjectileHoming(netId, target, degreesPerSecond);
        }

        public static Projectile Spawn(Vector3 position, Vector3 velocity, Color color, float size, float radius,
            int mask, HitHandler onHit, float life = 4f, System.Action<Vector3> onExpire = null, float gravity = 0f)
        {
            var go = new GameObject("Projectile");
            go.transform.position = position;
            var r = Fx.CreateQuad(go.transform, color, size);
            var p = go.AddComponent<Projectile>();
            p.velocity = velocity;
            p.radius = radius;
            p.mask = mask;
            p.onHit = onHit;
            p.onExpire = onExpire;
            p.life = life;
            p.visual = r.transform;
            p.baseSize = size;
            p.gravity = gravity;
            if (Net.NetCombat.HostMirroring) p.netId = Net.NetCombat.ProjectileSpawned(position, velocity, color, size, life, gravity);
            return p;
        }

        /// <summary>The first thing in the way, skipping players who can't be hit right now (Blink Dash).</summary>
        bool CastFirst(Vector3 dir, float len, out RaycastHit hit)
        {
            if (!Physics.SphereCast(transform.position, radius, dir, out hit, len, mask, QueryTriggerInteraction.Ignore)) return false;
            if (!Untouchable(hit.collider)) return true;
            var hits = Physics.SphereCastAll(transform.position, radius, dir, len, mask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, ByDistance);
            foreach (var h in hits)
                if (!Untouchable(h.collider)) { hit = h; return true; }
            return false;
        }

        static bool Untouchable(Collider c) =>
            c && c.gameObject.layer == Layers.Player && c.GetComponent<Player>() is Player p && p.Invulnerable;

        void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f)
            {
                onExpire?.Invoke(transform.position);
                Destroy(gameObject);
                return;
            }

            velocity.y -= gravity * dt;
            if (homingTarget && homingTarget.IsAlive)
            {
                Vector3 want = (homingTarget.Center - transform.position).normalized * velocity.magnitude;
                velocity = Vector3.RotateTowards(velocity, want, turnRate * Mathf.Deg2Rad * dt, 0f);
            }
            Vector3 step = velocity * dt;
            float len = step.magnitude;
            if (len > 1e-5f)
            {
                Vector3 dir = step / len;
                if (onPass != null)
                {
                    var hits = Physics.SphereCastAll(transform.position, radius, dir, len, mask, QueryTriggerInteraction.Ignore);
                    Array.Sort(hits, ByDistance);
                    Vector3 from = transform.position;
                    foreach (var h in hits)
                    {
                        if (passed.Contains(h.collider)) continue;
                        Vector3 at = from + dir * h.distance;
                        if (h.collider.gameObject.layer == Layers.Enemy)
                        {
                            passed.Add(h.collider);
                            DevMode.Sphere(at, Mathf.Max(radius, 0.1f), DevMode.PlayerShot, 0.35f);
                            onPass(at, h.collider);
                            continue;
                        }
                        transform.position = at;
                        onHit?.Invoke(at, h.collider);
                        Destroy(gameObject);
                        return;
                    }
                    transform.position += step;
                }
                else if (CastFirst(dir, len, out var hit))
                {
                    transform.position += dir * hit.distance;
                    DevMode.Sphere(transform.position, Mathf.Max(radius, 0.1f), (mask & Layers.PlayerMask) != 0 ? DevMode.EnemyShot : DevMode.PlayerShot, 0.35f);
                    onHit?.Invoke(transform.position, hit.collider);
                    Destroy(gameObject);
                    return;
                }
                transform.position += step;
            }

            pulse += dt * 25f;
            visual.localScale = Vector3.one * baseSize * (1f + 0.12f * Mathf.Sin(pulse));
        }
    }
}
