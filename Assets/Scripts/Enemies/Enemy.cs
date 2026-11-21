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
        /// <summary>Hook: raised for every enemy spawned (Grimoire elites).</summary>
        public static event System.Action<Enemy> Spawned;

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
        public bool IsSlowed => slowTimer > 0f;
        /// <summary>Optional controller for bosses and the mimic (movement, attacks, animation).</summary>
        public EnemyBrain Brain { get; private set; }
        /// <summary>Set by a brain: the animation to show, its clock, opacity and extra height (a leap).</summary>
        public SpriteAnim BrainAnim;
        public float BrainAnimTime, BrainAlpha = 1f, VisualLift;
        /// <summary>Takes no damage (a burrowed worm, a boss in the air).</summary>
        public bool Invulnerable;
        /// <summary>Elite variant (Grimoire experiment): tougher and tinted.</summary>
        public bool IsElite { get; private set; }
        public static readonly Color EliteTint = new Color(1f, 0.6f, 0.45f);
        float slowTimer, slowFactor = 1f;
        /// <summary>Element of the last damage taken, and whether it was the magic bolt (kill tracking).</summary>
        public Element LastElement { get; private set; }
        public bool LastHitByBolt { get; private set; }
        /// <summary>Global enemy speed (Time Scroll slows every enemy to half).</summary>
        public static float TimeFactor => Players.All.Exists(p => p && p.Run && p.Run.SlowTimer > 0f) ? 0.5f : 1f; // any player's Time Scroll
        public Vector3 Center => transform.position + Vector3.up * (Type.Hover + Type.Height * 0.5f);

        const float FadeDuration = 0.35f;

        enum State { Walk, Attack, Charge, Dead }
        State state = State.Walk;

        CharacterController cc;
        CapsuleCollider hitbox;
        float nextVoice;
        /// <summary>The collider shots and blasts hit (centred on the sprite).</summary>
        public CapsuleCollider Hitbox => hitbox;
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
        /// <summary>What the last move bumped into (a charging boss hitting a wall).</summary>
        public CollisionFlags LastFlags => lastFlags;

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
            Layers.Setup();
            var go = new GameObject(type.Name) { layer = Layers.EnemyBody };
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            cc.radius = type.Radius;
            cc.height = type.Height;
            cc.center = new Vector3(0, type.Height * 0.5f, 0); // collider stands on the floor; only the sprite hovers
            cc.stepOffset = 0.2f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;

            // What shots and blasts hit: a capsule around the sprite, including its flying height.
            var hitGo = new GameObject("Hitbox") { layer = Layers.Enemy };
            hitGo.transform.SetParent(go.transform, false);
            var hitbox = hitGo.AddComponent<CapsuleCollider>();
            hitbox.radius = type.Radius;
            hitbox.height = Mathf.Max(type.Height, type.Radius * 2f);
            hitbox.center = new Vector3(0, type.Hover + type.Height * 0.5f, 0);

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
            e.hitbox = hitbox;
            e.visual = quad.transform;
            e.rend = r;
            e.mpb = new MaterialPropertyBlock();
            e.attackCooldown = Random.Range(0.5f, 1.5f);
            e.strafeSign = Random.value < 0.5f ? -1f : 1f;
            e.animTime = Random.value * 2f;
            e.bobPhase = Random.value * 6f;
            if (type.BrainFactory != null) { e.Brain = type.BrainFactory(); e.Brain.Init(e); }
            e.ApplyVisual(type.Sprites.Walk, 0, 1f);
            All.Add(e);
            Spawned?.Invoke(e);
            return e;
        }

        void OnDestroy()
        {
            All.Remove(this);
            Net.NetEnemies.OnDestroyed(this);
        }

        // ---------------- multiplayer ----------------

        /// <summary>Network id of this enemy (0 offline).</summary>
        public int NetId;
        /// <summary>Who hit a player, for the hit-direction arc (one arc per attacker).</summary>
        public int HitKey => NetId != 0 ? NetId : GetInstanceID();
        /// <summary>A client's copy of a host enemy: no AI, no hits; position and frame come from the host.</summary>
        public bool Proxy { get; private set; }

        /// <summary>What this enemy drew last (the host sends it so clients draw the same frame).</summary>
        public struct NetVisual { public int anim, frame; public float alpha, squash, lift; public Color32 tint; public bool flash; }
        public NetVisual LastVisual { get; private set; }
        /// <summary>Client: when this proxy last got a state from the host.</summary>
        public float LastNetStateAt;
        Vector3 proxyLastPos;

        public void MakeProxy(int id, float hp, float maxHp)
        {
            NetId = id;
            Proxy = true;
            HP = hp;
            MaxHP = maxHp;
            if (cc) cc.enabled = false;
            Brain = null;
            proxyLastPos = transform.position;
        }

        /// <summary>Client: the host's view of this enemy (health, death and the exact sprite frame).</summary>
        public void ApplyNetState(Net.EnemyStateEntry s)
        {
            if (s.hp < HP - 0.01f) hurtFlash = 0.12f;
            HP = s.hp;
            if (s.elite) IsElite = true;
            if (s.dead && state != State.Dead) { state = State.Dead; All.Remove(this); }
            var set = Type.Sprites;
            var anim = set.ByIndex(s.anim) ?? set.Walk;
            int frame = Mathf.Clamp(s.frame, 0, anim.Count - 1);
            Vector3 moved = transform.position - proxyLastPos;
            moved.y = 0f;
            if (moved.sqrMagnitude > 1e-4f) lastMove = moved / Mathf.Max(Time.deltaTime, 1e-3f);
            proxyLastPos = transform.position;
            Render(anim, frame, s.alpha, s.squash, s.lift, s.tint, s.flash);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            hurtFlash = Mathf.Max(0f, hurtFlash - dt);
            if (Proxy) return; // moved and drawn from the host's snapshots
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
            if (slowTimer > 0f) { slowTimer -= realDt; dt *= slowFactor; }

            var player = PickTarget(realDt);
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

                if (Brain != null)
                {
                    move = Brain.Tick(player, dir, dist, dt);
                    BrainAnimTime += dt;
                    if (state == State.Dead) return;
                }
                else if (state == State.Attack)
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
            Vector3 velocity = (Brain != null ? move : move * Type.Speed) + knockback;
            lastMove = velocity;
            lastFlags = cc.Move((velocity + Vector3.up * verticalVelocity) * dt);

            // Pick the animation frame.
            bool held = frozenTimer > 0f || stunTimer > 0f;
            if (Brain != null && BrainAnim != null)
            {
                if (hitAnimTimer > 0f && set.Hit != null && Brain.ShowsHits) ApplyVisual(set.Hit, set.Hit.FrameAt(set.Hit.Duration - hitAnimTimer), BrainAlpha);
                else ApplyVisual(BrainAnim, BrainAnim.FrameAt(BrainAnimTime), BrainAlpha);
            }
            else if (state == State.Charge && !held)
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
            if (Proxy) return; // a client's copy: the host decides
            if (!IsAlive) return;
            poisonDps = Mathf.Max(poisonDps, dps);
            poisonTimer = Mathf.Max(poisonTimer, seconds);
        }

        /// <summary>Stops the enemy in place (like a freeze, but not ice: Shatter and Permafrost ignore it).</summary>
        public void Stun(float seconds)
        {
            if (Proxy) return; // a client's copy: the host decides
            if (!IsAlive || Invulnerable) return;
            if (!TakeCc(ref seconds)) return;
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
                player.TakeDamage(Type.ChargeDamage * DamageMult, transform.position, HitKey);
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

        // ---------------- target (co-op: the nearest living player) ----------------

        /// <summary>The player this enemy is after.</summary>
        public Player Target { get; private set; }
        /// <summary>Seconds between target checks, and how much closer another player must be to win it.</summary>
        public const float RetargetEvery = 0.5f, RetargetMargin = 1.5f;
        float retargetTimer;

        /// <summary>
        /// Keeps the current target unless it is gone or down; twice a second it switches to a living
        /// player who is clearly closer (the margin stops it flip-flopping between two players).
        /// </summary>
        Player PickTarget(float dt)
        {
            retargetTimer -= dt;
            bool lost = !Target || Target.IsDead || !Players.All.Contains(Target);
            if (!lost && retargetTimer > 0f) return Target;
            retargetTimer = RetargetEvery;
            var best = Players.Nearest(transform.position);
            if (lost) Target = best ? best : Players.Nearest(transform.position, aliveOnly: false);
            else if (best && best != Target)
            {
                float dBest = Mathf.Sqrt(Players.FlatSqr(best.transform.position, transform.position));
                float dNow = Mathf.Sqrt(Players.FlatSqr(Target.transform.position, transform.position));
                if (dBest + RetargetMargin < dNow) Target = best;
            }
            return Target;
        }

        /// <summary>Kamikaze blast: hurts you, other enemies and props in the radius.</summary>
        void Explode()
        {
            Vector3 c = Center;
            float r = Type.ExplodeRadius, dmg = Type.ExplodeDamage * DamageMult;
            Fx.Flash(c, new Color(1f, 0.55f, 0.15f), r * 0.9f, 0.4f, 2f);
            Fx.Ring(transform.position + Vector3.up * 0.1f, new Color(1f, 0.5f, 0.1f), r, 0.4f);
            Fx.AreaMarker(c, new Color(1f, 0.35f, 0.1f, 0.9f), r, 0.7f);
            Sfx.Play("explode", 0.7f, 1.15f);
            DevMode.Sphere(c, r, DevMode.EnemyArea);
            foreach (var player in Players.All.ToArray())
                if (player && !player.IsDead && (player.ChestPosition - c).sqrMagnitude <= r * r
                    && !Physics.Linecast(c, player.ChestPosition, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                    player.TakeDamage(dmg, c, HitKey);
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
                DevMode.Ring(transform.position + Vector3.up * 0.1f, Type.AttackRange + 0.5f, DevMode.EnemyArea, 0.35f);
                if (dist <= Type.AttackRange + 0.5f)
                {
                    player.TakeDamage(Type.Damage * DamageMult, transform.position, HitKey);
                    float thorns = player.Run ? player.Run.Stats.Thorns : 0f; // Thorned Robe
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
            int key = HitKey;
            Vector3 from = origin;
            for (int i = 0; i < Type.Shots; i++)
            {
                float ang = Type.Shots > 1 ? Mathf.Lerp(-Type.SpreadDeg * 0.5f, Type.SpreadDeg * 0.5f, i / (Type.Shots - 1f)) : 0f;
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * toChest;
                ShotsFired++;
                Projectile.Spawn(origin, dir * Type.ProjectileSpeed, color, Type.IsBoss ? 0.8f : 0.6f, 0.18f,
                    Layers.WorldMask | Layers.PlayerMask | Layers.PropMask, (point, col) =>
                    {
                        var p = col.GetComponent<Player>();
                        if (p) p.TakeDamage(damage, from, key);
                        else if (col.GetComponentInParent<Prop>() is Prop prop) prop.TakeDamage(damage);
                        Fx.Flash(point, color, 0.8f, 0.25f, 1.5f);
                    }, life: 6f);
            }
            Sfx.Play("enemyshot", 0.45f, Random.Range(0.9f, 1.1f) * (Type.IsBoss ? 0.7f : 1f));
        }

        /// <summary>Returns the damage actually dealt.</summary>
        public float TakeDamage(float amount, Element element = Element.Arcane, bool silent = false, bool bolt = false)
        {
            if (Proxy) return 0f; // a client's copy: the host decides
            if (!IsAlive || amount <= 0f || Invulnerable) return 0f;
            LastElement = element;
            LastHitByBolt = bolt;
            float dealt = Mathf.Min(HP, amount);
            HP -= amount;
            hurtFlash = 0.1f;
            if (!silent) hitAnimTimer = Type.Sprites.Hit != null ? Type.Sprites.Hit.Duration : 0f;
            if (HP <= 0f) Die();
            else if (!silent)
            {
                // Every hit: the dry impact. The creature's (reverby) pain voice only now and then.
                Sfx.Play("hit", 0.6f, Random.Range(0.92f, 1.08f));
                if (Time.time >= nextVoice && Random.value < 0.35f)
                {
                    nextVoice = Time.time + 1.2f;
                    Sfx.Play(Type.SoundPrefix + "_hit", 0.25f, Random.Range(0.95f, 1.15f));
                }
            }
            return dealt;
        }

        /// <summary>Kills it whatever its state (tests, debug).</summary>
        public void Kill()
        {
            if (Proxy) return; // a client's copy: the host decides
            Invulnerable = false;
            TakeDamage(HP + 1f, Element.Arcane, silent: true);
        }

        /// <summary>Moves it straight to a spot, through anything in between (a worm travelling underground).</summary>
        public void Warp(Vector3 position)
        {
            if (Proxy) return; // a client's copy: the host decides
            cc.enabled = false;
            transform.position = position;
            cc.enabled = IsAlive;
        }

        /// <summary>Turns the shot / blast hitbox on or off (a worm underground, a boss in the air).</summary>
        public void SetHitbox(bool on) { if (hitbox) hitbox.enabled = on && IsAlive; }

        /// <summary>Fires one enemy projectile (brains use this for their own patterns).</summary>
        public void Shoot(Vector3 origin, Vector3 dir, float speed, Color color, float size, float damage)
        {
            ShotsFired++;
            int key = HitKey;
            Projectile.Spawn(origin, dir.normalized * speed, color, size, 0.18f + size * 0.1f,
                Layers.WorldMask | Layers.PlayerMask | Layers.PropMask, (point, col) =>
                {
                    var p = col.GetComponent<Player>();
                    if (p) p.TakeDamage(damage, origin, key);
                    else if (col.GetComponentInParent<Prop>() is Prop prop) prop.TakeDamage(damage);
                    Fx.Flash(point, color, 0.8f, 0.25f, 1.5f);
                }, life: 6f);
        }

        public void Freeze(float seconds)
        {
            if (Proxy) return; // a client's copy: the host decides
            if (!IsAlive || Invulnerable) return;
            if (!TakeCc(ref seconds)) return;
            frozenTimer = Mathf.Max(frozenTimer, seconds);
            if (state == State.Attack || state == State.Charge) state = State.Walk;
        }

        float ccFreeAt = -10f;
        /// <summary>Freezes / stuns refused because of CC immunity (tests).</summary>
        public int CcResisted { get; private set; }

        /// <summary>
        /// Freeze and stun limits (CooldownConfig): bosses get a shorter one; while frozen or stunned nothing
        /// extends it, and once it ends the enemy shrugs off any new one for a moment. With no immunity set,
        /// the old rule (the longer one wins).
        /// </summary>
        /// <summary>A freeze or stun of `seconds` on this kind of enemy (bosses get a shorter one).</summary>
        public static float CcFor(EnemyType t, float seconds) => t != null && t.IsBoss ? seconds * CooldownConfig.BossCcMult : seconds;

        bool TakeCc(ref float seconds)
        {
            seconds = CcFor(Type, seconds);
            float immune = CooldownConfig.CcImmunitySeconds;
            if (immune <= 0f) return seconds > 0f;
            if (Time.time < ccFreeAt || frozenTimer > 0f || stunTimer > 0f) { CcResisted++; return false; }
            ccFreeAt = Time.time + seconds + immune;
            return seconds > 0f;
        }

        /// <summary>Removes the freeze (used by Shatter). Returns true if the enemy was frozen.</summary>
        public bool ConsumeFreeze()
        {
            bool was = frozenTimer > 0f;
            frozenTimer = 0f;
            return was;
        }

        /// <summary>Moves and attacks at factor speed (0.5 = half) for a while.</summary>
        public void Slow(float factor, float seconds)
        {
            if (Proxy) return; // a client's copy: the host decides
            if (!IsAlive) return;
            slowFactor = slowTimer > 0f ? Mathf.Min(slowFactor, factor) : factor;
            slowTimer = Mathf.Max(slowTimer, seconds);
        }

        /// <summary>Turns this enemy into an elite: more HP, tinted.</summary>
        public void MakeElite(float hpMult)
        {
            if (Proxy) return; // a client's copy: the host decides
            if (!IsAlive || IsElite || Type.IsBoss) return;
            IsElite = true;
            MaxHP *= hpMult;
            HP *= hpMult;
        }

        public void Ignite(float dps, float seconds)
        {
            if (Proxy) return; // a client's copy: the host decides
            if (!IsAlive) return;
            burnDps = Mathf.Max(burnDps, dps);
            burnTimer = Mathf.Max(burnTimer, seconds);
        }

        public void Knockback(Vector3 impulse)
        {
            if (Proxy) return; // a client's copy: the host decides
            if (!IsAlive) return;
            impulse.y = 0f;
            knockback += impulse;
        }

        void Die()
        {
            DiedFrozen = frozenTimer > 0f;
            state = State.Dead;
            cc.enabled = false;
            hitbox.enabled = false;
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
            var away = Target ? Target : Players.Nearest(transform.position, aliveOnly: false);
            Vector3 side = away ? Vector3.Cross(Vector3.up, (away.transform.position - transform.position).normalized) : Vector3.right;
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
            Color tint = state == State.Dead ? Color.white
                : frozenTimer > 0f ? new Color(0.55f, 0.85f, 1f)
                : stunTimer > 0f ? new Color(1f, 0.92f, 0.55f)
                : burnTimer > 0f ? new Color(1f, 0.75f + 0.25f * Mathf.Sin(Time.time * 20f), 0.55f)
                : poisonTimer > 0f ? new Color(0.6f, 1f, 0.5f)
                : slowTimer > 0f ? new Color(0.7f, 0.75f, 1f)
                : IsElite ? EliteTint
                : Color.white;
            tint.a = alpha;
            bool flash = hurtFlash > 0f;
            LastVisual = new NetVisual
            {
                anim = Type.Sprites.IndexOf(anim), frame = frame, alpha = alpha, squash = squash,
                lift = VisualLift, tint = tint, flash = flash,
            };
            Render(anim, frame, alpha, squash, VisualLift, tint, flash);
        }

        void Render(SpriteAnim anim, int frame, float alpha, float squash, float lift, Color tint, bool flash)
        {
            var set = Type.Sprites;
            float ppu = set.PixelsPerUnit;
            float w = anim.CellPx.x / ppu, h = anim.CellPx.y / ppu * squash;
            float hover = Type.Hover > 0f && state != State.Dead ? Type.Hover + Mathf.Sin(Time.time * 2.5f + bobPhase) * 0.08f : Type.Hover * 0.3f;
            if (state != State.Dead) hover += lift;
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

            mpb.SetTexture(MainTexId, anim.Texture);
            mpb.SetVector(MainTexStId, st);
            mpb.SetColor(ColorId, tint);
            mpb.SetFloat(FlashId, flash || hurtFlash > 0f ? 1f : 0f);
            rend.SetPropertyBlock(mpb);
        }
    }
}
