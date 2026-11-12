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
        public float MaxHP { get; private set; }
        public float DamageMult { get; private set; } = 1f;
        /// <summary>True if the enemy was frozen when it died (Permafrost relic).</summary>
        public bool DiedFrozen { get; private set; }
        public bool IsAlive => state != State.Dead;
        public bool IsFrozen => frozenTimer > 0f;
        public bool IsBurning => burnTimer > 0f;
        public bool IsPoisoned => poisonTimer > 0f;
        public bool IsStunned => stunTimer > 0f;
        /// <summary>Element of the last damage taken, and whether it was the magic bolt (kill tracking).</summary>
        public Element LastElement { get; private set; }
        public bool LastHitByBolt { get; private set; }
        /// <summary>Global enemy speed (Time Scroll slows every enemy to half).</summary>
        public static float TimeFactor => Run.Instance && Run.Instance.SlowTimer > 0f ? 0.5f : 1f;
        public Vector3 Center => transform.position + Vector3.up * (Type.Hover + Type.Height * 0.5f);

        const float FadeDuration = 0.35f;

        enum State { Walk, Attack, Charge, Dead }
        State state = State.Walk;

        CharacterController cc;
        Transform visual;
        Renderer rend;
        MaterialPropertyBlock mpb;
        float stateTimer, attackCooldown, hurtFlash, hitAnimTimer, frozenTimer, animTime, deathTimer, verticalVelocity, strafeTimer;
        float burnTimer, burnDps, burnTick;
        float poisonTimer, poisonDps, poisonTick, stunTimer;
        float strafeSign = 1f, avoidSign;
        Vector3 knockback, lastMove;
        bool attackFired, facingLeft;
        float bobPhase;
        int attacksDone;

        // Ranged hunting: when the player hides, walk to a spot that can see them again.
        float losTimer, flankRepick;
        bool flanking;
        Vector3 flankPoint;
        /// <summary>Whether this enemy could see the player at its last check (ranged enemies).</summary>
        public bool SeesPlayer { get; private set; } = true;
        /// <summary>True while a ranged enemy is moving around cover to find the player.</summary>
        public bool IsFlanking => flanking;
        public int AttacksDone => attacksDone;

        // Special behaviours (see EnemyType)
        const float ChargeBrace = 0.75f;
        float chargeCd, burstTimer, hpMult = 1f;
        int burstLeft;
        Vector3 chargeDir;
        bool chargeHit, exploded;
        CollisionFlags lastFlags;
        /// <summary>A kamikaze that blew itself up (no loot for that).</summary>
        public bool SelfDestructed { get; private set; }
        /// <summary>Charger: true while bracing or dashing / only while dashing.</summary>
        public bool IsCharging => state == State.Charge;
        public bool IsDashing => state == State.Charge && stateTimer >= ChargeBrace;
        /// <summary>Projectiles fired so far (tests).</summary>
        public int ShotsFired { get; private set; }

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        public static Enemy Spawn(EnemyType type, Vector3 position, float hpMult = 1f, float dmgMult = 1f)
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
            e.MaxHP = e.HP = type.MaxHP * hpMult;
            e.hpMult = hpMult;
            e.chargeCd = Random.Range(1f, 2.5f);
            e.DamageMult = dmgMult;
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
            UpdatePoison(dt);
            if (state == State.Dead) return; // burned or poisoned to death this frame
            float realDt = dt;
            dt *= TimeFactor; // Time Scroll: movement and attacks at half speed

            var player = Player.Instance;
            if (!player) return;

            Vector3 move = Vector3.zero;
            if (frozenTimer > 0f || stunTimer > 0f)
            {
                frozenTimer = Mathf.Max(0f, frozenTimer - realDt);
                stunTimer = Mathf.Max(0f, stunTimer - realDt);
            }
            else
            {
                attackCooldown -= dt;
                chargeCd -= dt;
                if (burstLeft > 0 && state != State.Dead)
                {
                    burstTimer -= dt;
                    if (burstTimer <= 0f)
                    {
                        burstLeft--;
                        burstTimer = Type.BurstGap;
                        if (SeesPlayer && !player.IsDead) FireVolley(player);
                    }
                }
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
                        if (state == State.Dead) return; // a kamikaze blew itself up
                    }
                    if (stateTimer >= Type.Windup + Type.Recover) state = State.Walk;
                    if (Type.Kamikaze && !attackFired) hurtFlash = Mathf.Repeat(stateTimer * 14f, 1f) < 0.5f ? 0.05f : 0f; // about to blow
                }
                else if (state == State.Charge)
                {
                    move = UpdateCharge(player, dir, dist, dt);
                    animTime += dt;
                }
                else
                {
                    move = Steer(player, dir, dist, dt);
                    if (Type.ChargeSpeed > 0f && chargeCd <= 0f && !player.IsDead && dist >= Type.ChargeMin && dist <= Type.ChargeMax && HasLineOfSight(player))
                    {
                        state = State.Charge;
                        stateTimer = 0f;
                        chargeDir = dir;
                        chargeHit = false;
                        move = Vector3.zero;
                        Sfx.Play("boss", 0.45f, 1.7f);
                    }
                    else if (attackCooldown <= 0f && !player.IsDead && CanAttack(player, dist))
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
            lastFlags = cc.Move((velocity + Vector3.up * verticalVelocity) * dt);

            // Pick the animation frame.
            bool held = frozenTimer > 0f || stunTimer > 0f;
            if (state == State.Charge && !held)
            {
                if (IsDashing) { var run = set.Run ?? set.Walk; ApplyVisual(run, run.FrameAt(animTime * (set.Run != null ? 1f : 3f)), 1f); }
                else { var brace = set.Special ?? set.Walk; ApplyVisual(brace, brace.FrameAt(stateTimer), 1f); }
            }
            else if (state == State.Attack && !held && set.Attack != null)
                ApplyVisual(set.Attack, set.Attack.FrameAtNormalized(stateTimer / (Type.Windup + Type.Recover)), 1f);
            else if (hitAnimTimer > 0f && set.Hit != null)
                ApplyVisual(set.Hit, set.Hit.FrameAt(set.Hit.Duration - hitAnimTimer), 1f);
            else
                ApplyVisual(set.Walk, held ? set.Walk.FrameAt(0) : set.Walk.FrameAt(animTime), 1f);
        }

        void UpdatePoison(float dt)
        {
            if (poisonTimer <= 0f) return;
            poisonTimer -= dt;
            poisonTick -= dt;
            if (poisonTick <= 0f)
            {
                poisonTick = 0.5f;
                Fx.Flash(Center + Random.insideUnitSphere * 0.35f, new Color(0.45f, 1f, 0.3f), 0.6f, 0.35f, 0.8f);
                TakeDamage(poisonDps * 0.5f, Element.Poison, silent: true);
            }
        }

        public void Poison(float dps, float seconds)
        {
            if (!IsAlive) return;
            poisonDps = Mathf.Max(poisonDps, dps);
            poisonTimer = Mathf.Max(poisonTimer, seconds);
        }

        /// <summary>Stops the enemy in place (like a freeze, but not ice: Shatter and Permafrost ignore it).</summary>
        public void Stun(float seconds)
        {
            if (!IsAlive) return;
            stunTimer = Mathf.Max(stunTimer, seconds);
            if (state == State.Attack || state == State.Charge) state = State.Walk;
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

        Vector3 Steer(Player player, Vector3 dir, float dist, float dt)
        {
            Vector3 desire;
            if (Type.Ranged)
            {
                losTimer -= dt;
                if (losTimer <= 0f)
                {
                    losTimer = 0.2f;
                    SeesPlayer = player.IsDead || HasLineOfSight(player);
                }
            }
            if (Type.Ranged && !SeesPlayer)
            {
                // The player is behind cover: circle around to a spot that has a clear shot.
                flankRepick -= dt;
                Vector3 toFlank = flankPoint - transform.position;
                toFlank.y = 0f;
                if (!flanking || flankRepick <= 0f || toFlank.sqrMagnitude < 0.8f * 0.8f)
                {
                    PickFlankPoint(player);
                    toFlank = flankPoint - transform.position;
                    toFlank.y = 0f;
                }
                desire = toFlank.sqrMagnitude > 0.04f ? toFlank.normalized : dir;
            }
            else if (Type.Ranged)
            {
                flanking = false;
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
                if (Physics.SphereCast(Center, Mathf.Min(Type.Radius * 0.8f, 0.6f), d, out var hit, 1.4f + Type.Radius, Layers.SolidMask, QueryTriggerInteraction.Ignore))
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

        /// <summary>
        /// Picks the closest point around the player, at shooting distance and inside the room, that has
        /// a clear line to them. Points the enemy can walk to in a straight line are preferred; if none
        /// can see the player, it just closes in.
        /// </summary>
        void PickFlankPoint(Player player)
        {
            flanking = true;
            flankRepick = 1.2f;
            Vector3 p = player.transform.position, me = transform.position;
            var room = Dungeon.Instance ? Dungeon.Instance.Current : null;
            float eyeUp = Type.Hover + Type.Height * 0.5f;
            Vector3 chest = player.ChestPosition;
            Vector3 fromPlayer = me - p;
            fromPlayer.y = 0f;
            float baseAngle = Mathf.Atan2(fromPlayer.z, fromPlayer.x);
            float mid = (Type.PreferredMin + Type.PreferredMax) * 0.5f;

            float best = float.MaxValue;
            Vector3 bestPoint = p;
            const int Steps = 16;
            for (int ring = 0; ring < 2; ring++)
            {
                float radius = ring == 0 ? Mathf.Clamp(fromPlayer.magnitude, Type.PreferredMin, mid) : Type.PreferredMin * 0.75f;
                for (int i = 0; i < Steps; i++)
                {
                    // Alternate sides outward from the enemy's own bearing, starting with its strafe side.
                    int k = (i + 1) / 2;
                    float sign = (i % 2 == 0 ? 1f : -1f) * strafeSign;
                    float a = baseAngle + sign * k * (Mathf.PI * 2f / Steps);
                    var c = new Vector3(p.x + Mathf.Cos(a) * radius, me.y, p.z + Mathf.Sin(a) * radius);
                    if (room != null && !room.Contains(c, Type.Radius + 0.8f)) continue;
                    var eye = c + Vector3.up * eyeUp;
                    if (Physics.CheckSphere(eye, Type.Radius, Layers.SolidMask, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.Linecast(eye, chest, Layers.WorldMask, QueryTriggerInteraction.Ignore)) continue;
                    float cost = Vector3.Distance(me, c);
                    if (Physics.Linecast(Center, eye, Layers.SolidMask, QueryTriggerInteraction.Ignore)) cost += 6f;
                    if (cost < best) { best = cost; bestPoint = c; }
                }
            }
            flankPoint = bestPoint;
        }

        /// <summary>Charger: brace (turning to track you), then dash in a straight line.</summary>
        Vector3 UpdateCharge(Player player, Vector3 dir, float dist, float dt)
        {
            stateTimer += dt;
            if (stateTimer < ChargeBrace)
            {
                if (stateTimer < ChargeBrace - 0.2f) chargeDir = dir; // locks on just before it goes
                return Vector3.zero;
            }
            float dashT = stateTimer - ChargeBrace;
            if (dashT > Type.ChargeTime) { EndCharge(); return Vector3.zero; }

            // Ran into you?
            if (!chargeHit && dist < Type.Radius + 1.0f)
            {
                chargeHit = true;
                player.TakeDamage(Type.ChargeDamage * DamageMult);
                Sfx.Play("claw", 0.6f, 0.7f);
                Fx.Flash(player.ChestPosition, new Color(1f, 0.6f, 0.3f), 1.2f, 0.25f, 1.2f);
                EndCharge();
                return Vector3.zero;
            }
            // Slammed into a wall or pillar: dazed.
            if (dashT > 0.08f && (lastFlags & CollisionFlags.Sides) != 0)
            {
                EndCharge();
                Stun(1.8f);
                Sfx.Play("crate", 0.7f, 0.6f);
                Fx.Flash(Center + chargeDir * Type.Radius, new Color(1f, 0.92f, 0.55f), 1.5f, 0.3f, 1.5f);
                Raise("dazed");
                return Vector3.zero;
            }
            return chargeDir * (Type.ChargeSpeed / Type.Speed);
        }

        void EndCharge()
        {
            state = State.Walk;
            chargeCd = Type.ChargeCooldown;
            attackCooldown = Mathf.Max(attackCooldown, 0.6f);
        }

        /// <summary>Test hook: what just happened to this enemy ("dazed").</summary>
        public string LastEvent { get; private set; }
        void Raise(string e) => LastEvent = e;

        /// <summary>Kamikaze blast: hurts you, other enemies and props in the radius.</summary>
        void Explode()
        {
            Vector3 c = Center;
            float r = Type.ExplodeRadius, dmg = Type.ExplodeDamage * DamageMult;
            Fx.Flash(c, new Color(1f, 0.55f, 0.15f), r * 0.9f, 0.4f, 2f);
            Fx.Ring(transform.position + Vector3.up * 0.1f, new Color(1f, 0.5f, 0.1f), r, 0.4f);
            Sfx.Play("explode", 0.7f, 1.15f);
            var player = Player.Instance;
            if (player && !player.IsDead && (player.ChestPosition - c).sqrMagnitude <= r * r
                && !Physics.Linecast(c, player.ChestPosition, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                player.TakeDamage(dmg);
            foreach (var o in All.ToArray())
                if (o && o.IsAlive && (o.Center - c).sqrMagnitude <= r * r) o.TakeDamage(Type.ExplodeDamage, Element.Fire);
            Combat.DamageProps(c, r, Type.ExplodeDamage);
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
            if (Type.Kamikaze)
            {
                SelfDestructed = true;
                TakeDamage(HP + 1f, Element.Fire, silent: true);
                return;
            }
            if (!Type.Ranged)
            {
                attackCooldown = Type.AttackCooldown;
                if (dist <= Type.AttackRange + 0.5f)
                {
                    player.TakeDamage(Type.Damage * DamageMult);
                    float thorns = Run.Instance ? Run.Instance.Stats.Thorns : 0f; // Thorned Robe
                    if (thorns > 0f) { TakeDamage(thorns, Element.Arcane); Fx.Flash(Center, new Color(0.6f, 1f, 0.4f), 1f, 0.2f, 1f); }
                }
                Sfx.Play("claw", 0.5f, Random.Range(0.9f, 1.15f));
                if (Random.value < 0.4f) Sfx.Play(Type.SoundPrefix + "_attack", 0.35f, Random.Range(0.95f, 1.15f));
                return;
            }

            attackCooldown = Type.AttackCooldown * Random.Range(0.8f, 1.3f);
            attacksDone++;
            if (Type.SummonEvery > 0 && attacksDone % Type.SummonEvery == Type.SummonEvery - 1 && Enemy.All.Count < 12)
            {
                // Boss: open two summoning sigils beside it instead of shooting.
                for (int i = 0; i < 2; i++)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, (player.transform.position - transform.position).normalized) * (i == 0 ? 2.5f : -2.5f);
                    var at = transform.position + side;
                    var room = Dungeon.Instance ? Dungeon.Instance.Current : null;
                    if (room != null && !room.Contains(at, 1.5f)) at = transform.position;
                    EnemySpawner.Instance.SpawnAt(EnemyType.Imp, at + Vector3.up * 0.05f, 1f, DamageMult);
                }
                Sfx.Play("boss", 0.6f, 1.3f);
                return;
            }
            FireVolley(player);
            burstLeft = Type.Burst - 1;
            burstTimer = Type.BurstGap;
        }

        void FireVolley(Player player)
        {
            Vector3 origin = Center;
            Vector3 toChest = (player.ChestPosition - origin).normalized;
            origin += toChest * (0.6f + Type.Radius * 0.5f);
            float damage = Type.Damage * DamageMult;
            var color = Type.ProjectileColor;
            for (int i = 0; i < Type.Shots; i++)
            {
                float ang = Type.Shots > 1 ? Mathf.Lerp(-Type.SpreadDeg * 0.5f, Type.SpreadDeg * 0.5f, i / (Type.Shots - 1f)) : 0f;
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * toChest;
                ShotsFired++;
                Projectile.Spawn(origin, dir * Type.ProjectileSpeed, color, Type.IsBoss ? 0.8f : 0.6f, 0.18f,
                    Layers.WorldMask | Layers.PlayerMask | Layers.PropMask, (point, col) =>
                    {
                        var p = col.GetComponent<Player>();
                        if (p) p.TakeDamage(damage);
                        else if (col.GetComponentInParent<Prop>() is Prop prop) prop.TakeDamage(damage);
                        Fx.Flash(point, color, 0.8f, 0.25f, 1.5f);
                    }, life: 6f);
            }
            Sfx.Play("enemyshot", 0.45f, Random.Range(0.9f, 1.1f) * (Type.IsBoss ? 0.7f : 1f));
        }

        /// <summary>Returns the damage actually dealt.</summary>
        public float TakeDamage(float amount, Element element = Element.Arcane, bool silent = false, bool bolt = false)
        {
            if (!IsAlive || amount <= 0f) return 0f;
            LastElement = element;
            LastHitByBolt = bolt;
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
            if (state == State.Attack || state == State.Charge) state = State.Walk;
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
            DiedFrozen = frozenTimer > 0f;
            state = State.Dead;
            cc.enabled = false;
            frozenTimer = 0f;
            burnTimer = 0f;
            poisonTimer = 0f;
            stunTimer = 0f;
            deathTimer = 0f;
            All.Remove(this);
            burstLeft = 0;
            Sfx.Play(Type.SoundPrefix + "_death", 0.5f, Random.Range(0.9f, 1.1f), fallback: "death");
            Killed?.Invoke(this);
            if (Type.Kamikaze && !exploded) { exploded = true; Explode(); }
            if (Type.SplitInto != null && EnemySpawner.Instance) Split();
        }

        void Split()
        {
            var room = Dungeon.Instance ? Dungeon.Instance.RoomAt(transform.position) : null;
            Vector3 side = Player.Instance ? Vector3.Cross(Vector3.up, (Player.Instance.transform.position - transform.position).normalized) : Vector3.right;
            for (int i = 0; i < Type.SplitCount; i++)
            {
                float s = Type.SplitCount == 1 ? 0f : Mathf.Lerp(-1f, 1f, i / (Type.SplitCount - 1f));
                var at = transform.position + side * s * 0.9f;
                if (room != null && !room.Contains(at, 1f)) at = transform.position;
                var child = EnemySpawner.Instance.SpawnAt(Type.SplitInto, at + Vector3.up * 0.05f, hpMult, DamageMult);
                child.Knockback(side * s * 5f);
            }
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
                : stunTimer > 0f ? new Color(1f, 0.92f, 0.55f)
                : burnTimer > 0f ? new Color(1f, 0.75f + 0.25f * Mathf.Sin(Time.time * 20f), 0.55f)
                : poisonTimer > 0f ? new Color(0.6f, 1f, 0.5f)
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
