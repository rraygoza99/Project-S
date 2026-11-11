using System;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>Kinematic glowing projectile using sphere casts (no rigidbodies).</summary>
    public class Projectile : MonoBehaviour
    {
        public delegate void HitHandler(Vector3 point, Collider hit);

        Vector3 velocity;
        float radius, life, pulse;
        int mask;
        HitHandler onHit;
        System.Action<Vector3> onExpire;
        Transform visual;
        float baseSize, gravity;
        Enemy homingTarget;
        float turnRate;

        /// <summary>Steers toward the enemy (degrees per second) while it lives.</summary>
        public void Home(Enemy target, float degreesPerSecond)
        {
            homingTarget = target;
            turnRate = degreesPerSecond;
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
            return p;
        }

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
                if (Physics.SphereCast(transform.position, radius, dir, out var hit, len, mask, QueryTriggerInteraction.Ignore))
                {
                    transform.position += dir * hit.distance;
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
