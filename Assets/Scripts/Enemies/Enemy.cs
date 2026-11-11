using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// A billboarded 2D enemy: simple steering toward the player with wall sliding/avoidance,
    /// melee or ranged attacks, hurt flash, freeze, burn, knockback, and an animated death.
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        public static readonly List<Enemy> All = new List<Enemy>();
        public static event System.Action<Enemy> Killed;

        public EnemyType Type { get; private set; }
        public float HP { get; private set; }
        public bool IsAlive => state != State.Dead;
        public bool IsFrozen => frozenTimer > 0f;
        public bool IsBurning => burnTimer > 0f;
        public Vector3 Center => transform.position + Vector3.up * (Type.Hover + Type.Height * 0.5f);

        const float FadeDuration = 0.35f;

        enum State { Walk, Attack, Dead }
        State state = State.Walk;

        CharacterController cc;
        Transform visual;
        Renderer rend;
        MaterialPropertyBlock mpb;
        float stateTimer, attackCooldown, hurtFlash, hitAnimTimer, frozenTimer, animTime, deathTimer, verticalVelocity, strafeTimer;
        float burnTimer, burnDps, burnTick;
        float strafeSign = 1f, avoidSign;
        Vector3 knockback, lastMove;
        bool attackFired, facingLeft;
        float bobPhase;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        public static Enemy Spawn(EnemyType type, Vector3 position)
        {
            var go = new GameObject(type.Name) { layer = Layers.Enemy };
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            cc.radius = type.Radius;
            cc.height = type.Height;
            cc.center = new Vector3(0, type.Height * 0.5f, 0); // collider stands on the floor; only the sprite hovers
            cc.stepOffset = 0.2f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "Sprite";
            quad.transform.SetParent(go.transform, false);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = Materials.Sprite;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            quad.AddComponent<Billboard>().Cylindrical = true;

            var e = go.AddComponent<Enemy>();
            e.Type = type;
            e.HP = type.MaxHP;
            e.cc = cc;
            e.visual = quad.transform;
            e.rend = r;
            e.mpb = new MaterialPropertyBlock();
            e.attackCooldown = Random.Range(0.5f, 1.5f);
            e.strafeSign = Random.value < 0.5f ? -1f : 1f;
            e.animTime = Random.value * 2f;
            e.bobPhase = Random.value * 6f;
            e.ApplyVisual(type.Sprites.Walk, 0, 1f);
            All.Add(e);
            return e;
        }

        void OnDestroy() => All.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            hurtFlash = Mathf.Max(0f, hurtFlash - dt);
            hitAnimTimer = Mathf.Max(0f, hitAnimTimer - dt);
            var set = Type.Sprites;

            if (state == State.Dead)
            {
                deathTimer += dt;
                if (set.Dead != null)
                {
                    float fadeK = Mathf.Clamp01((deathTimer - set.Dead.Duration) / FadeDuration);
                    ApplyVisual(set.Dead, set.Dead.FrameAt(deathTimer), 1f - fadeK);
                    if (fadeK >= 1f) Destroy(gameObject);
                }
                else
                {
                    // Fallback: squash and dither away.
                    float k = Mathf.Clamp01(deathTimer / 0.7f);
                    float squash = 1f - 0.75f * Mathf.Sqrt(k);
                    ApplyVisual(set.Walk, 0, 1f - k * k, squash);
                    if (k >= 1f) Destroy(gameObject);
                }
                return;
            }

            UpdateBurn(dt);
            if (state == State.Dead) return; // burned to death this frame

            var player = Player.Instance;
            if (!player) return;

            Vector3 move = Vector3.zero;
            if (frozenTimer > 0f)
            {
                frozenTimer -= dt;
            }
            else
            {
                attackCooldown -= dt;
                Vector3 toPlayer = player.transform.position - transform.position;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;
                Vector3 dir = dist > 1e-3f ? toPlayer / dist : transform.forward;

                if (state == State.Attack)
                {
                    stateTimer += dt;
                    if (!attackFired && stateTimer >= Type.Windup)
                    {
                        attackFired = true;
                        PerformAttack(player, dist);
                    }
                    if (stateTimer >= Type.Windup + Type.Recover) state = State.Walk;
                }
                else
                {
                    move = Steer(dir, dist, dt);
                    if (attackCooldown <= 0f && !player.IsDead && CanAttack(player, dist))
                    {
                        state = State.Attack;
                        stateTimer = 0f;
                        attackFired = false;
                        if (Type.Ranged) Sfx.Play(Type.SoundPrefix + "_attack", 0.35f, Random.Range(0.95f, 1.1f));
                    }
                    animTime += dt * (move.sqrMagnitude > 0.01f ? 1f : 0.5f);
                }
            }

            knockback = Vector3.MoveTowards(knockback, Vector3.zero, 18f * dt);
            verticalVelocity = cc.isGrounded ? -1f : verticalVelocity - 20f * dt;
            Vector3 velocity = move * Type.Speed + knockback;
            lastMove = velocity;
            cc.Move((velocity + Vector3.up * verticalVelocity) * dt);

            // Pick the animation frame.
            if (state == State.Attack && frozenTimer <= 0f && set.Attack != null)
                ApplyVisual(set.Attack, set.Attack.FrameAtNormalized(stateTimer / (Type.Windup + Type.Recover)), 1f);
            else if (hitAnimTimer > 0f && set.Hit != null)
                ApplyVisual(set.Hit, set.Hit.FrameAt(set.Hit.Duration - hitAnimTimer), 1f);
            else
                ApplyVisual(set.Walk, frozenTimer > 0f ? set.Walk.FrameAt(0) : set.Walk.FrameAt(animTime), 1f);
        }

        void UpdateBurn(float dt)
        {
            if (burnTimer <= 0f) return;
            burnTimer -= dt;
            burnTick -= dt;
            if (burnTick <= 0f)
            {
                burnTick = 0.5f;
                Fx.Flash(Center + Random.insideUnitSphere * 0.3f, new Color(1f, 0.45f, 0.1f), 0.7f, 0.3f, 0.8f);
                TakeDamage(burnDps * 0.5f, Element.Fire, silent: true);
            }
        }

        Vector3 Steer(Vector3 dir, float dist, float dt)
        {
            Vector3 desire;
            if (Type.Ranged)
            {
                strafeTimer -= dt;
                if (strafeTimer <= 0f)
                {
                    strafeTimer = Random.Range(1.5f, 3.5f);
                    strafeSign = -strafeSign;
                }
                if (dist > Type.PreferredMax) desire = dir;
                else if (dist < Type.PreferredMin) desire = -dir;
                else desire = Vector3.Cross(Vector3.up, dir) * strafeSign * 0.6f;
            }
            else
            {
                desire = dist > Type.AttackRange * 0.75f ? dir : Vector3.zero;
            }

            // Obstacle avoidance: slide along walls/pillars instead of pushing into them.
            if (desire.sqrMagnitude > 1e-3f)
            {
                Vector3 d = desire.normalized;
                if (Physics.SphereCast(Center, Type.Radius * 0.8f, d, out var hit, 1.4f, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 n = hit.normal;
                    n.y = 0f;
                    if (n.sqrMagnitude > 1e-4f)
                    {
                        n.Normalize();
                        Vector3 tangent = Vector3.Cross(Vector3.up, n);
                        if (avoidSign == 0f) avoidSign = Vector3.Dot(tangent, d) >= 0f ? 1f : -1f;
                        desire = (tangent * avoidSign * 0.9f + d * 0.1f) * desire.magnitude;
                    }
                }
                else avoidSign = 0f;
            }

            // Separation so enemies don't stack into one sprite.
            Vector3 sep = Vector3.zero, pos = transform.position;
            for (int i = 0; i < All.Count; i++)
            {
                var o = All[i];
                if (o == this || !o.IsAlive) continue;
                Vector3 off = pos - o.transform.position;
                off.y = 0f;
                float min = Type.Radius + o.Type.Radius + 0.35f;
                float sq = off.sqrMagnitude;
                if (sq < min * min && sq > 1e-6f)
                {
                    float m = Mathf.Sqrt(sq);
                    sep += off / m * (1f - m / min);
                }
            }
            desire += sep * 2f;
            if (desire.sqrMagnitude > 1f) desire.Normalize();
            return desire;
        }

        bool CanAttack(Player player, float dist)
        {
            if (!Type.Ranged) return dist <= Type.AttackRange;
            return dist <= Type.AttackRange && dist > 2.5f && HasLineOfSight(player);
        }

        bool HasLineOfSight(Player player) =>
            !Physics.Linecast(Center, player.ChestPosition, Layers.WorldMask, QueryTriggerInteraction.Ignore);

        void PerformAttack(Player player, float dist)
        {
            if (!Type.Ranged)
            {
                attackCooldown = Type.AttackCooldown;
                if (dist <= Type.AttackRange + 0.5f) player.TakeDamage(Type.Damage);
                Sfx.Play("claw", 0.5f, Random.Range(0.9f, 1.15f));
                if (Random.value < 0.4f) Sfx.Play(Type.SoundPrefix + "_attack", 0.35f, Random.Range(0.95f, 1.15f));
                return;
            }

            attackCooldown = Type.AttackCooldown * Random.Range(0.8f, 1.3f);
            Vector3 origin = Center;
            Vector3 toChest = (player.ChestPosition - origin).normalized;
            origin += toChest * 0.6f;
            float damage = Type.Damage;
            var color = Type.ProjectileColor;
            Projectile.Spawn(origin, toChest * Type.ProjectileSpeed, color, 0.6f, 0.18f,
                Layers.WorldMask | Layers.PlayerMask, (point, col) =>
                {
                    var p = col.GetComponent<Player>();
                    if (p) p.TakeDamage(damage);
                    Fx.Flash(point, color, 0.8f, 0.25f, 1.5f);
                }, life: 6f);
            Sfx.Play("enemyshot", 0.45f, Random.Range(0.9f, 1.1f));
        }

        /// <summary>Returns the damage actually dealt.</summary>
        public float TakeDamage(float amount, Element element = Element.Arcane, bool silent = false)
        {
            if (!IsAlive || amount <= 0f) return 0f;
            float dealt = Mathf.Min(HP, amount);
            HP -= amount;
            hurtFlash = 0.1f;
            if (!silent) hitAnimTimer = Type.Sprites.Hit != null ? Type.Sprites.Hit.Duration : 0f;
            if (HP <= 0f) Die();
            else if (!silent) Sfx.Play(Type.SoundPrefix + "_hit", 0.45f, Random.Range(0.9f, 1.15f), fallback: "hit");
            return dealt;
        }

        public void Freeze(float seconds)
        {
            if (!IsAlive) return;
            frozenTimer = Mathf.Max(frozenTimer, seconds);
            if (state == State.Attack) state = State.Walk;
        }

        /// <summary>Removes the freeze (used by Shatter). Returns true if the enemy was frozen.</summary>
        public bool ConsumeFreeze()
        {
            bool was = frozenTimer > 0f;
            frozenTimer = 0f;
            return was;
        }

        public void Ignite(float dps, float seconds)
        {
            if (!IsAlive) return;
            burnDps = Mathf.Max(burnDps, dps);
            burnTimer = Mathf.Max(burnTimer, seconds);
        }

        public void Knockback(Vector3 impulse)
        {
            if (!IsAlive) return;
            impulse.y = 0f;
            knockback += impulse;
        }

        void Die()
        {
            state = State.Dead;
            cc.enabled = false;
            frozenTimer = 0f;
            burnTimer = 0f;
            deathTimer = 0f;
            All.Remove(this);
            Sfx.Play(Type.SoundPrefix + "_death", 0.5f, Random.Range(0.9f, 1.1f), fallback: "death");
            Killed?.Invoke(this);
        }

        void ApplyVisual(SpriteAnim anim, int frame, float alpha, float squash = 1f)
        {
            var set = Type.Sprites;
            float ppu = set.PixelsPerUnit;
            float w = anim.CellPx.x / ppu, h = anim.CellPx.y / ppu * squash;
            float hover = Type.Hover > 0f && state != State.Dead ? Type.Hover + Mathf.Sin(Time.time * 2.5f + bobPhase) * 0.08f : Type.Hover * 0.3f;
            visual.localScale = new Vector3(w, h, 1f);
            visual.localPosition = new Vector3(0f, h * 0.5f - set.FeetPx / ppu * squash + hover, 0f);

            Vector4 st = anim.Frames[frame];
            if (set.Flippable && state != State.Dead)
            {
                // Mirror the side-view art so it faces the way it moves on screen.
                var cam = Player.Instance ? Player.Instance.Cam.transform : null;
                if (cam && lastMove.sqrMagnitude > 0.05f)
                {
                    float side = Vector3.Dot(lastMove, cam.right);
                    if (Mathf.Abs(side) > 0.2f) facingLeft = side < 0f;
                }
                if (facingLeft == set.FacesRight) st = new Vector4(-st.x, st.y, st.z + st.x, st.w);
            }

            Color tint = state == State.Dead ? Color.white
                : frozenTimer > 0f ? new Color(0.55f, 0.85f, 1f)
                : burnTimer > 0f ? new Color(1f, 0.75f + 0.25f * Mathf.Sin(Time.time * 20f), 0.55f)
                : Color.white;
            tint.a = alpha;
            mpb.SetTexture(MainTexId, anim.Texture);
            mpb.SetVector(MainTexStId, st);
            mpb.SetColor(ColorId, tint);
            mpb.SetFloat(FlashId, hurtFlash > 0f ? 1f : 0f);
            rend.SetPropertyBlock(mpb);
        }
    }
}
