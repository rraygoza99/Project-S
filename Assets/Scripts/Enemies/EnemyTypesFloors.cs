using System;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Floor 2 (Frozen Caverns) and floor 3 (The Forge) enemies, the per-floor bosses and each floor's
    /// encounter table. Floor 1 keeps the original roster and The Watcher.
    /// </summary>
    public partial class EnemyType
    {
        /// <summary>Takes over movement and animation (bosses, the mimic); see EnemyBrain.</summary>
        public Func<EnemyBrain> BrainFactory;
        /// <summary>At most this many per room (0 = no limit).</summary>
        public int MaxPerRoom;

        // ---------------- floor 2: Frozen Caverns ----------------

        /// <summary>Slow, tough slug covered in ice spikes.</summary>
        public static readonly EnemyType Slug = new EnemyType
        {
            Name = "Frost Slug", SoundPrefix = "watcher", SpriteFactory = Spellcaster.Sprites.Slug, MinFloor = 2,
            MaxHP = 85f, Speed = 1.7f, Height = 0.6f, Radius = 0.7f,
            AttackRange = 1.8f, Windup = 0.55f, Recover = 0.4f, AttackCooldown = 1.4f, Damage = 14f,
        };

        /// <summary>Flying moth that spits frost in pairs.</summary>
        public static readonly EnemyType Moth = new EnemyType
        {
            Name = "Gloom Moth", SoundPrefix = "watcher", SpriteFactory = Spellcaster.Sprites.Moth, MinFloor = 2,
            MaxHP = 34f, Speed = 3.6f, Height = 1f, Radius = 0.45f, Hover = 1.1f, Flying = true,
            Ranged = true, AttackRange = 20f, PreferredMin = 6f, PreferredMax = 11f,
            Windup = 0.45f, Recover = 0.3f, AttackCooldown = 2.2f, Damage = 7f,
            ProjectileSpeed = 11f, ProjectileColor = new Color(0.75f, 0.6f, 1f), Shots = 2, SpreadDeg = 14f,
        };

        /// <summary>Crouches, then leaps at you (a short charge); bites up close.</summary>
        public static readonly EnemyType Frog = new EnemyType
        {
            Name = "Cave Frog", SoundPrefix = "imp", SpriteFactory = Spellcaster.Sprites.Frog, MinFloor = 2,
            MaxHP = 40f, Speed = 3.3f, Height = 0.75f, Radius = 0.5f,
            AttackRange = 1.6f, Windup = 0.3f, Recover = 0.35f, AttackCooldown = 1.1f, Damage = 9f,
            ChargeSpeed = 12f, ChargeTime = 0.45f, ChargeMin = 3.5f, ChargeMax = 9f, ChargeDamage = 12f, ChargeCooldown = 2.6f,
        };

        // ---------------- floor 3: The Forge ----------------

        /// <summary>Slug with burning spikes it throws in a fan.</summary>
        public static readonly EnemyType Crawler = new EnemyType
        {
            Name = "Cinder Crawler", SoundPrefix = "imp", SpriteFactory = Spellcaster.Sprites.Crawler, MinFloor = 3,
            MaxHP = 50f, Speed = 2.2f, Height = 0.55f, Radius = 0.65f,
            Ranged = true, AttackRange = 16f, PreferredMin = 5f, PreferredMax = 10f,
            Windup = 0.6f, Recover = 0.4f, AttackCooldown = 2.4f, Damage = 8f,
            ProjectileSpeed = 10f, ProjectileColor = new Color(1f, 0.5f, 0.15f), Shots = 3, SpreadDeg = 36f,
        };

        /// <summary>Little forge robot that fires bursts.</summary>
        public static readonly EnemyType Robot = new EnemyType
        {
            Name = "Forge Sentry", SoundPrefix = "watcher", SpriteFactory = Spellcaster.Sprites.Robot, MinFloor = 3,
            MaxHP = 45f, Speed = 2.8f, Height = 1f, Radius = 0.45f,
            Ranged = true, AttackRange = 22f, PreferredMin = 7f, PreferredMax = 13f,
            Windup = 0.5f, Recover = 0.3f, AttackCooldown = 2.6f, Damage = 6f,
            ProjectileSpeed = 14f, ProjectileColor = new Color(0.4f, 0.95f, 1f), Burst = 3, BurstGap = 0.15f,
        };

        /// <summary>Sits still as a treasure chest until you come close or hit it, then bites.</summary>
        public static readonly EnemyType Mimic = new EnemyType
        {
            Name = "Mimic", SoundPrefix = "imp", SpriteFactory = Spellcaster.Sprites.Mimic, MinFloor = 3, MaxPerRoom = 1,
            MaxHP = 90f, Speed = 4.4f, Height = 1.1f, Radius = 0.6f,
            AttackRange = 1.9f, Windup = 0.35f, Recover = 0.4f, AttackCooldown = 1.0f, Damage = 16f,
            BrainFactory = () => new MimicBrain(),
        };

        // ---------------- bosses ----------------

        /// <summary>Floor 2 boss: dives underground, erupts under you, spits frost and calls slugs and moths.</summary>
        public static readonly EnemyType Burrower = new EnemyType
        {
            Name = "The Burrower", SoundPrefix = "watcher", SpriteFactory = Spellcaster.Sprites.Burrower, IsBoss = true, MinFloor = 2,
            MaxHP = 600f, Speed = 2.4f, Height = 3.4f, Radius = 1.0f,
            Damage = 9f, ProjectileSpeed = 11f, ProjectileColor = new Color(0.7f, 0.5f, 1f),
            BrainFactory = () => new BurrowerBrain(),
        };

        /// <summary>Floor 3 boss: rolls across the arena, fires spikes, leaps and slams down; tires after a crash.</summary>
        public static readonly EnemyType Behemoth = new EnemyType
        {
            Name = "The Behemoth", SoundPrefix = "imp", SpriteFactory = Spellcaster.Sprites.Behemoth, IsBoss = true, MinFloor = 3,
            MaxHP = 700f, Speed = 2.6f, Height = 3.2f, Radius = 1.6f,
            Damage = 10f, ProjectileSpeed = 12f, ProjectileColor = new Color(1f, 0.45f, 0.15f),
            BrainFactory = () => new BehemothBrain(),
        };

        /// <summary>An enemy type by its name (multiplayer spawns), or null.</summary>
        public static EnemyType ByName(string name)
        {
            foreach (var t in AllRegulars) if (t.Name == name) return t;
            foreach (var t in new[] { Boss, Burrower, Behemoth, Shadeling }) if (t.Name == name) return t;
            return null;
        }

        /// <summary>The boss at the end of each floor.</summary>
        public static EnemyType BossFor(int floor) => floor <= 1 ? Boss : floor == 2 ? Burrower : Behemoth;

        /// <summary>Every enemy that can appear in rooms, all floors (menus, tests).</summary>
        public static readonly EnemyType[] AllRegulars = { Imp, Cultist, Hornet, Shade, Skull, Beetle, Slug, Moth, Frog, Crawler, Robot, Mimic };

        /// <summary>
        /// Each floor's own mix (weights grow a little with room depth d). Floor 1: imps, cultists, hornets, shades.
        /// Floor 2: slugs, moths, frogs and shades. Floor 3: crawlers, sentries, skulls, beetles, imps and a mimic.
        /// </summary>
        public static (EnemyType type, float w)[] Roster(int floor, int depth)
        {
            float d = Mathf.Min(depth, 5);
            switch (Mathf.Clamp(floor, 1, 3))
            {
                case 1:
                    return new[]
                    {
                        (Imp, 1f), (Cultist, 0.3f + 0.04f * d), (Hornet, 0.15f + 0.03f * d), (Shade, depth >= 2 ? 0.15f : 0f),
                    };
                case 2:
                    return new[]
                    {
                        (Slug, 0.7f), (Moth, 0.5f + 0.04f * d), (Frog, 0.5f), (Shade, 0.35f), (Cultist, 0.2f), (Imp, 0.2f),
                    };
                default:
                    return new[]
                    {
                        (Crawler, 0.6f), (Robot, 0.45f + 0.04f * d), (Skull, 0.45f), (Beetle, 0.25f), (Imp, 0.45f), (Mimic, depth >= 1 ? 0.2f : 0f),
                    };
            }
        }

        /// <summary>Picks an enemy for a room encounter from the floor's roster; countSoFar caps beetles (2) and MaxPerRoom types.</summary>
        public static EnemyType PickForEncounter(int floor, int depth, Func<float> rand, Func<EnemyType, int> countSoFar)
        {
            var pool = Roster(floor, depth);
            float total = 0f;
            for (int i = 0; i < pool.Length; i++)
            {
                var t = pool[i].type;
                int n = countSoFar != null ? countSoFar(t) : 0;
                if ((t == Beetle && n >= 2) || (t.MaxPerRoom > 0 && n >= t.MaxPerRoom)) pool[i].w = 0f;
                total += pool[i].w;
            }
            float r = rand() * total;
            foreach (var p in pool)
            {
                r -= p.w;
                if (r < 0f && p.w > 0f) return p.type;
            }
            return pool[0].type;
        }
    }
}
