using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Takes over an enemy's movement, attacks and animation (Enemy keeps HP, status effects and death).
    /// Tick returns the velocity in m/s; it is not called while the enemy is frozen or stunned.
    /// </summary>
    public abstract class EnemyBrain
    {
        protected Enemy E;
        protected SpriteSet S => E.Type.Sprites;
        protected EnemyType T => E.Type;
        /// <summary>What it is doing right now ("burrowed", "rolling"...), for tests and dev mode.</summary>
        public string Phase { get; protected set; } = "";
        /// <summary>Test hook: the next attack to use ("roll", "spikes", "leap", "dive").</summary>
        public string Force;
        public virtual bool ShowsHits => true;
        protected float phaseTime;

        public virtual void Init(Enemy e)
        {
            E = e;
            Play(S.Idle ?? S.Walk);
        }

        public abstract Vector3 Tick(Player p, Vector3 dir, float dist, float dt);

        protected void Enter(string phase, SpriteAnim anim)
        {
            Phase = phase;
            phaseTime = 0f;
            E.BrainAnim = anim ?? S.Walk;
            E.BrainAnimTime = 0f;
        }

        protected void Play(SpriteAnim a)
        {
            a ??= S.Walk;
            if (E.BrainAnim == a) return;
            E.BrainAnim = a;
            E.BrainAnimTime = 0f;
        }

        protected SpriteAnim Anim(string name, SpriteAnim fallback = null) => S.Get(name) ?? fallback ?? S.Walk;
        protected float Dur(SpriteAnim a, float fallback) => a != null && a != S.Walk ? a.Duration : fallback;

        protected Room CurrentRoom => Dungeon.Instance ? Dungeon.Instance.RoomAt(E.transform.position) : null;

        /// <summary>A point inside the current room, pulled in from the walls by margin.</summary>
        protected Vector3 ClampToRoom(Vector3 p, float margin)
        {
            var room = CurrentRoom;
            if (room == null) return p;
            p.x = Mathf.Clamp(p.x, room.Center.x - room.HalfX + margin, room.Center.x + room.HalfX - margin);
            p.z = Mathf.Clamp(p.z, room.Center.z - room.HalfZ + margin, room.Center.z + room.HalfZ - margin);
            return p;
        }

        /// <summary>A ground blast: hurts you if you're within radius.</summary>
        protected void Shockwave(Player p, Vector3 at, float radius, float damage, Color color)
        {
            Fx.Ring(Fx.Ground(at), color, radius, 0.45f);
            Fx.AreaMarker(at, color, radius, 0.5f);
            Fx.Flash(at + Vector3.up * 0.6f, color, radius * 0.9f, 0.35f, 1.4f);
            DevMode.Ring(Fx.Ground(at), radius, DevMode.EnemyArea);
            Sfx.Play("explode", 0.7f, 0.7f);
            Vector3 d = p.transform.position - at;
            d.y = 0f;
            if (!p.IsDead && d.sqrMagnitude <= radius * radius) p.TakeDamage(damage * E.DamageMult);
        }

        protected void Summon(EnemyType type, Vector3 near, int max)
        {
            if (Enemy.All.Count >= max || !EnemySpawner.Instance) return;
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(2f, 3.5f);
            var at = ClampToRoom(near + new Vector3(r.x, 0f, r.y), 2f);
            EnemySpawner.Instance.SpawnAt(type, at + Vector3.up * 0.05f, 1f, E.DamageMult);
        }

        /// <summary>Fires count shots spread over spreadDeg around dir, at chest height.</summary>
        protected void Fan(Vector3 origin, Vector3 dir, int count, float spreadDeg, float speed, Color color, float size, float damage)
        {
            for (int i = 0; i < count; i++)
            {
                float a = count > 1 ? Mathf.Lerp(-spreadDeg * 0.5f, spreadDeg * 0.5f, i / (count - 1f)) : 0f;
                E.Shoot(origin, Quaternion.AngleAxis(a, Vector3.up) * dir, speed, color, size, damage * E.DamageMult);
            }
            Sfx.Play("enemyshot", 0.5f, 0.7f);
        }
    }

    /// <summary>Mimic: a still treasure chest until you come within reach (or hit it); then it chases and bites.</summary>
    public class MimicBrain : EnemyBrain
    {
        public const float WakeRange = 3.2f;
        public bool Dormant => Phase == "dormant";
        public override bool ShowsHits => !Dormant;
        float cooldown;
        bool bit;

        public override void Init(Enemy e)
        {
            base.Init(e);
            Enter("dormant", S.Special ?? S.Idle ?? S.Walk);
        }

        public override Vector3 Tick(Player p, Vector3 dir, float dist, float dt)
        {
            phaseTime += dt;
            cooldown -= dt;
            switch (Phase)
            {
                case "dormant":
                    E.BrainAnimTime = 0f; // hold the closed-chest frame
                    if (dist < WakeRange || E.HP < E.MaxHP)
                    {
                        Enter("waking", S.Special);
                        Sfx.Play("crate", 0.6f, 0.6f);
                        Sfx.Play(T.SoundPrefix + "_attack", 0.5f, 0.8f);
                    }
                    return Vector3.zero;
                case "waking":
                    if (phaseTime >= Dur(S.Special, 0.5f)) Enter("hunt", S.Walk);
                    return Vector3.zero;
                case "bite":
                    if (!bit && phaseTime >= T.Windup)
                    {
                        bit = true;
                        if (dist <= T.AttackRange + 0.5f && !p.IsDead) p.TakeDamage(T.Damage * E.DamageMult);
                        Sfx.Play("claw", 0.6f, 0.8f);
                    }
                    if (phaseTime >= T.Windup + T.Recover) Enter("hunt", S.Walk);
                    return Vector3.zero;
                default:
                    if (dist <= T.AttackRange && cooldown <= 0f && !p.IsDead)
                    {
                        cooldown = T.AttackCooldown;
                        bit = false;
                        Enter("bite", S.Attack ?? S.Walk);
                        return Vector3.zero;
                    }
                    return dist > T.AttackRange * 0.7f ? dir * T.Speed : Vector3.zero;
            }
        }
    }

    /// <summary>
    /// The Burrower (floor 2 boss). Cycle: surface (a blast if you stand on the warning ring) -> spit fans of
    /// frost -> sometimes call a slug and a moth -> dive and travel underground (can't be hurt) -> surface
    /// under you. Below half HP it travels faster, spits wider and calls help every cycle.
    /// </summary>
    public class BurrowerBrain : EnemyBrain
    {
        public const float EruptRadius = 2.8f;
        public int Dives { get; private set; }
        public bool Burrowed => Phase == "burrowed";
        public override bool ShowsHits => Phase == "spit" || Phase == "idle";
        bool Enraged => E.HP < E.MaxHP * 0.5f;
        int volleys, cycles;
        bool fired, warned;
        Vector3 target;
        float trail;

        public override void Init(Enemy e)
        {
            base.Init(e);
            Enter("idle", S.Idle);
            phaseTime = -0.8f; // a moment before the first attack
        }

        public override Vector3 Tick(Player p, Vector3 dir, float dist, float dt)
        {
            phaseTime += dt;
            switch (Phase)
            {
                case "idle":
                    if (phaseTime < 0.6f) return Vector3.zero;
                    if (Force == "dive" || volleys >= (Enraged ? 3 : 2)) { Force = null; StartDive(); return Vector3.zero; }
                    volleys++;
                    fired = false;
                    Enter("spit", S.Attack);
                    return Vector3.zero;
                case "spit":
                    if (!fired && phaseTime >= 0.3f)
                    {
                        fired = true;
                        Vector3 origin = E.Center + Vector3.up * 0.4f;
                        Vector3 aim = p.ChestPosition - origin;
                        aim.y *= 0.6f;
                        Fan(origin, aim.normalized, Enraged ? 7 : 5, Enraged ? 80f : 60f, T.ProjectileSpeed, T.ProjectileColor, 0.7f, T.Damage);
                    }
                    if (phaseTime >= Dur(S.Attack, 0.6f)) Enter("idle", S.Idle);
                    return Vector3.zero;
                case "dive":
                    if (phaseTime >= Dur(S.Run, 0.6f))
                    {
                        Enter("burrowed", S.Idle);
                        E.BrainAlpha = 0f;
                        E.Invulnerable = true;
                        E.SetHitbox(false);
                        Dives++;
                        warned = false;
                        // Surface near where you are now.
                        Vector2 off = Random.insideUnitCircle * 1.2f;
                        target = ClampToRoom(p.transform.position + new Vector3(off.x, 0f, off.y), 2.5f);
                    }
                    return Vector3.zero;
                case "burrowed":
                {
                    float travel = Enraged ? 1.0f : 1.4f;
                    trail -= dt;
                    if (trail <= 0f)
                    {
                        trail = 0.12f;
                        Fx.Flash(Fx.Ground(E.transform.position) + Vector3.up * 0.2f, new Color(0.55f, 0.5f, 0.6f), 1.4f, 0.5f, 1.3f);
                    }
                    if (!warned && phaseTime >= travel - 0.8f)
                    {
                        warned = true;
                        Fx.AreaMarker(target, new Color(0.8f, 0.5f, 1f), EruptRadius, 0.9f);
                        DevMode.Ring(Fx.Ground(target), EruptRadius, DevMode.EnemyArea, 0.9f);
                        Sfx.Play("door", 0.5f, 0.5f);
                    }
                    Vector3 to = target - E.transform.position;
                    to.y = 0f;
                    if (phaseTime >= travel && to.magnitude < 0.6f)
                    {
                        E.BrainAlpha = 1f;
                        E.Invulnerable = false;
                        E.SetHitbox(true);
                        Enter("surface", S.Special);
                        Shockwave(p, E.transform.position, EruptRadius, 16f, new Color(0.75f, 0.5f, 1f));
                        return Vector3.zero;
                    }
                    float speed = Mathf.Max(to.magnitude / Mathf.Max(0.15f, travel - phaseTime), 4f);
                    return to.magnitude > 0.05f ? to.normalized * Mathf.Min(speed, 16f) : Vector3.zero;
                }
                case "surface":
                    if (phaseTime >= Dur(S.Special, 0.6f))
                    {
                        cycles++;
                        volleys = 0;
                        if (Enraged || cycles % 2 == 0)
                        {
                            Summon(EnemyType.Slug, E.transform.position, 10);
                            Summon(EnemyType.Moth, E.transform.position, 10);
                            Sfx.Play("boss", 0.6f, 1.2f);
                        }
                        Enter("idle", S.Idle);
                    }
                    return Vector3.zero;
            }
            Enter("idle", S.Idle);
            return Vector3.zero;
        }

        void StartDive()
        {
            Enter("dive", S.Run);
            Sfx.Play("crate", 0.6f, 0.5f);
        }
    }

    /// <summary>
    /// The Behemoth (floor 3 boss). Walks at you, then picks an attack:
    /// roll (curls up and charges, leaving burning ground; crashing into a wall tires it out for a while),
    /// spikes (a ring of spikes plus an aimed fan) or leap (jumps, can't be hurt in the air, lands on the
    /// warning ring with a shockwave). At half HP it roars and gets faster.
    /// </summary>
    public class BehemothBrain : EnemyBrain
    {
        public const float SlamRadius = 4.5f;
        public int Rolls { get; private set; }
        public int Leaps { get; private set; }
        public int SpikeVolleys { get; private set; }
        public int Crashes { get; private set; }
        public bool Airborne => Phase == "air";
        bool enraged;
        string lastAttack;
        Vector3 rollDir, landAt;
        bool hitPlayer, fired;
        float trail;

        public override void Init(Enemy e)
        {
            base.Init(e);
            Enter("walk", S.Walk);
        }

        public override Vector3 Tick(Player p, Vector3 dir, float dist, float dt)
        {
            phaseTime += dt;
            float pace = enraged ? 1.25f : 1f;
            if (!enraged && E.HP < E.MaxHP * 0.5f && (Phase == "walk" || Phase == "tired"))
            {
                enraged = true;
                Enter("roar", Anim("roar_wind", Anim("roar")));
                Sfx.Play("boss", 0.9f, 0.7f);
            }
            switch (Phase)
            {
                case "roar":
                    if (phaseTime >= Dur(Anim("roar_wind", null), 0.8f) + 0.5f) Enter("walk", S.Walk);
                    else if (phaseTime >= Dur(Anim("roar_wind", null), 0.8f)) Play(Anim("roar"));
                    return Vector3.zero;

                case "walk":
                {
                    Play(dist > 4f ? S.Walk : S.Idle ?? S.Walk);
                    if (phaseTime >= (enraged ? 1.0f : 1.6f) || Force != null) { ChooseAttack(dist); return Vector3.zero; }
                    return dist > 4f ? dir * T.Speed * pace : Vector3.zero;
                }

                case "roll_wind":
                    if (phaseTime < Dur(Anim("roll_wind"), 1f) - 0.2f) rollDir = dir;
                    if (phaseTime >= Dur(Anim("roll_wind"), 1f))
                    {
                        Enter("rolling", Anim("roll"));
                        hitPlayer = false;
                        Rolls++;
                        Sfx.Play("boss", 0.6f, 1.5f);
                    }
                    return Vector3.zero;

                case "rolling":
                {
                    // Burning ground behind it.
                    trail -= dt;
                    if (trail <= 0f)
                    {
                        trail = 0.18f;
                        LavaPool.CreateTemporary(Fx.Ground(E.transform.position), 1.1f, 4f);
                    }
                    Vector3 toP = p.transform.position - E.transform.position;
                    toP.y = 0f;
                    if (!hitPlayer && toP.magnitude < T.Radius + 1.0f && !p.IsDead)
                    {
                        hitPlayer = true;
                        p.TakeDamage(22f * E.DamageMult);
                        Sfx.Play("claw", 0.7f, 0.6f);
                    }
                    if (phaseTime > 0.12f && (E.LastFlags & CollisionFlags.Sides) != 0)
                    {
                        // Crashed into a wall: dazed and tired, the time to punish it.
                        Crashes++;
                        Fx.Flash(E.Center + rollDir * T.Radius, new Color(1f, 0.9f, 0.6f), 3f, 0.35f, 1.5f);
                        Sfx.Play("crate", 0.9f, 0.5f);
                        Enter("tired", Anim("tired"));
                        return Vector3.zero;
                    }
                    if (phaseTime >= 1.6f) { Enter("roll_end", Anim("roll_end")); return Vector3.zero; }
                    return rollDir * (enraged ? 16f : 13f);
                }

                case "roll_end":
                    if (phaseTime >= Dur(Anim("roll_end"), 0.6f)) Enter("walk", S.Walk);
                    return Vector3.zero;

                case "tired":
                    if (phaseTime >= (enraged ? 1.6f : 2.4f)) Enter("walk", S.Walk);
                    return Vector3.zero;

                case "spike_wind":
                    if (phaseTime >= Dur(Anim("spike_wind"), 0.6f)) { Enter("spikes", Anim("spike")); fired = false; }
                    return Vector3.zero;

                case "spikes":
                    if (!fired)
                    {
                        fired = true;
                        SpikeVolleys++;
                        Vector3 origin = E.Center + Vector3.up * 0.3f;
                        int ring = enraged ? 18 : 14;
                        for (int i = 0; i < ring; i++)
                            E.Shoot(origin, Quaternion.AngleAxis(i * 360f / ring + (enraged ? 10f : 0f), Vector3.up) * Vector3.forward,
                                T.ProjectileSpeed, T.ProjectileColor, 0.55f, T.Damage * E.DamageMult);
                        Vector3 aim = p.ChestPosition - origin;
                        aim.y *= 0.5f;
                        Fan(origin, aim.normalized, 5, 40f, T.ProjectileSpeed * 1.2f, new Color(1f, 0.75f, 0.3f), 0.6f, T.Damage);
                    }
                    if (phaseTime >= Dur(Anim("spike"), 0.35f)) Enter("spike_end", Anim("spike_end"));
                    return Vector3.zero;

                case "spike_end":
                    if (phaseTime >= Dur(Anim("spike_end"), 0.6f)) Enter("walk", S.Walk);
                    return Vector3.zero;

                case "takeoff":
                    if (phaseTime >= Dur(Anim("takeoff"), 0.7f))
                    {
                        Enter("air", Anim("fall"));
                        Leaps++;
                        E.Invulnerable = true;
                        E.SetHitbox(false);
                        Sfx.Play("boss", 0.5f, 1.8f);
                    }
                    return Vector3.zero;

                case "air":
                {
                    const float Up = 0.35f, Track = 1.0f, Down = 0.3f;
                    if (phaseTime < Up + Track) landAt = ClampToRoom(p.transform.position, T.Radius + 1f);
                    if (phaseTime >= Up + Track - 0.05f && phaseTime - dt < Up + Track - 0.05f)
                    {
                        Fx.AreaMarker(landAt, new Color(1f, 0.4f, 0.15f), SlamRadius, Down + 0.5f);
                        DevMode.Ring(Fx.Ground(landAt), SlamRadius, DevMode.EnemyArea, Down + 0.5f);
                    }
                    if (phaseTime < Up) E.VisualLift = Mathf.Lerp(0f, 7f, phaseTime / Up);
                    else if (phaseTime < Up + Track) E.VisualLift = 7f;
                    else E.VisualLift = Mathf.Lerp(7f, 0f, (phaseTime - Up - Track) / Down);
                    if (phaseTime >= Up + Track + Down)
                    {
                        E.VisualLift = 0f;
                        E.Invulnerable = false;
                        E.SetHitbox(true);
                        Enter("landing", Anim("landing"));
                        Shockwave(p, E.transform.position, SlamRadius, 20f, new Color(1f, 0.45f, 0.15f));
                        if (enraged) { Summon(EnemyType.Skull, E.transform.position, 8); Summon(EnemyType.Skull, E.transform.position, 8); }
                        return Vector3.zero;
                    }
                    Vector3 to = landAt - E.transform.position;
                    to.y = 0f;
                    return to.magnitude > 0.1f ? to.normalized * Mathf.Min(to.magnitude * 4f, 18f) : Vector3.zero;
                }

                case "landing":
                    if (phaseTime >= Dur(Anim("landing"), 0.5f)) Enter("walk", S.Walk);
                    return Vector3.zero;
            }
            Enter("walk", S.Walk);
            return Vector3.zero;
        }

        void ChooseAttack(float dist)
        {
            string pick = Force;
            Force = null;
            if (pick == null)
            {
                var options = new List<string> { "roll", "spikes", "leap" };
                options.Remove(lastAttack);
                if (dist < 5f) options.Remove("roll"); // too close to wind up a roll
                pick = options[Random.Range(0, options.Count)];
            }
            lastAttack = pick;
            switch (pick)
            {
                case "roll": Enter("roll_wind", Anim("roll_wind")); break;
                case "spikes": Enter("spike_wind", Anim("spike_wind")); break;
                default: Enter("takeoff", Anim("takeoff")); break;
            }
        }
    }
}
