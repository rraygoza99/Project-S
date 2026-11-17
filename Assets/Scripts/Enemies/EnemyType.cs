using System;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>Data for an enemy archetype.</summary>
    public partial class EnemyType
    {
        public string Name;
        public string SoundPrefix;          // MonsterSounds/<prefix>_hit|_death|_attack, falls back to synth
        public Func<float, SpriteSet> SpriteFactory;
        public float MaxHP;
        public float Speed;
        public float Height;
        public float Radius;
        public float Hover;                 // floating enemies hover this far above the ground
        /// <summary>Flies: ignores floor hazards such as spike traps.</summary>
        public bool Flying;

        public bool Ranged;
        public float AttackRange;           // melee reach, or max shooting range
        public float PreferredMin, PreferredMax; // ranged: distance band to keep
        public float Windup, Recover;
        public float AttackCooldown;
        public float Damage;
        public float ProjectileSpeed;
        public Color ProjectileColor;
        public int Shots = 1;               // projectiles per ranged attack (fanned out)
        public float SpreadDeg;             // total fan angle
        public int SummonEvery;             // every Nth attack (starting with the (N-1)th) summons minions instead
        public bool IsBoss;

        // Special behaviours (all off by default)
        /// <summary>Flies at you and explodes (also when killed): ExplodeDamage to everything within ExplodeRadius.</summary>
        public bool Kamikaze;
        public float ExplodeRadius, ExplodeDamage;
        /// <summary>Charger: from ChargeMin..ChargeMax away it braces (Windup) and dashes at ChargeSpeed for ChargeTime.
        /// Hitting you deals ChargeDamage; hitting a wall dazes it.</summary>
        public float ChargeSpeed, ChargeTime, ChargeMin, ChargeMax, ChargeDamage, ChargeCooldown;
        /// <summary>Ranged: volleys per attack, BurstGap seconds apart.</summary>
        public int Burst = 1;
        public float BurstGap = 0.18f;
        /// <summary>Splits into SplitCount of SplitInto when it dies.</summary>
        public EnemyType SplitInto;
        public int SplitCount;
        public bool DropsLoot = true;
        /// <summary>Shown in the encounter tables (first floor it appears on).</summary>
        public int MinFloor = 1;

        SpriteSet sprites;
        public SpriteSet Sprites => sprites ??= SpriteFactory(Height);

        public static readonly EnemyType Imp = new EnemyType
        {
            Name = "Imp", SoundPrefix = "imp", SpriteFactory = Spellcaster.Sprites.Imp,
            MaxHP = 30f, Speed = 4.2f, Height = 1.4f, Radius = 0.4f,
            AttackRange = 1.7f, Windup = 0.35f, Recover = 0.3f, AttackCooldown = 1.0f, Damage = 8f,
        };

        /// <summary>The ranged enemy from the spec ("Cultist"); drawn as the floating eye when the asset pack is imported.</summary>
        public static readonly EnemyType Cultist = new EnemyType
        {
            Name = "Cultist", SoundPrefix = "watcher", SpriteFactory = Spellcaster.Sprites.Cultist,
            MaxHP = 60f, Speed = 2.6f, Height = 1.9f, Radius = 0.45f, Hover = 0.35f, Flying = true,
            Ranged = true, AttackRange = 24f, PreferredMin = 8f, PreferredMax = 14f,
            Windup = 0.6f, Recover = 0.35f, AttackCooldown = 2.6f, Damage = 10f,
            ProjectileSpeed = 9f, ProjectileColor = new Color(0.35f, 0.95f, 1f),
        };

        /// <summary>Fast flying skull that rushes you and explodes. Kill it at range: it also blows up when it dies.</summary>
        public static readonly EnemyType Skull = new EnemyType
        {
            Name = "Flame Skull", SoundPrefix = "imp", SpriteFactory = Spellcaster.Sprites.Skull, MinFloor = 3,
            MaxHP = 22f, Speed = 6.2f, Height = 0.9f, Radius = 0.4f, Hover = 1.0f, Flying = true,
            AttackRange = 1.9f, Windup = 0.3f, Recover = 0f, AttackCooldown = 0.5f, Damage = 0f,
            Kamikaze = true, ExplodeRadius = 3.2f, ExplodeDamage = 18f,
        };

        /// <summary>Slow armoured beetle that braces and charges across the room; ramming a wall dazes it.</summary>
        public static readonly EnemyType Beetle = new EnemyType
        {
            Name = "Horned Beetle", SoundPrefix = "imp", SpriteFactory = Spellcaster.Sprites.Beetle, MinFloor = 3,
            MaxHP = 140f, Speed = 2.3f, Height = 1.8f, Radius = 0.65f,
            AttackRange = 2.1f, Windup = 0.45f, Recover = 0.5f, AttackCooldown = 1.4f, Damage = 12f,
            ChargeSpeed = 15f, ChargeTime = 1.0f, ChargeMin = 4.5f, ChargeMax = 16f, ChargeDamage = 20f, ChargeCooldown = 4.5f,
        };

        /// <summary>Darting flyer that fires bursts of three stingers.</summary>
        public static readonly EnemyType Hornet = new EnemyType
        {
            Name = "Hornet", SoundPrefix = "watcher", SpriteFactory = Spellcaster.Sprites.Hornet,
            MaxHP = 34f, Speed = 4.4f, Height = 1.1f, Radius = 0.4f, Hover = 1.1f, Flying = true,
            Ranged = true, AttackRange = 20f, PreferredMin = 6f, PreferredMax = 11f,
            Windup = 0.45f, Recover = 0.5f, AttackCooldown = 3.0f, Damage = 5f,
            ProjectileSpeed = 15f, ProjectileColor = new Color(1f, 0.85f, 0.25f), Burst = 3, BurstGap = 0.16f,
        };

        /// <summary>The small shades a Shade splits into.</summary>
        public static readonly EnemyType Shadeling = new EnemyType
        {
            Name = "Shadeling", SoundPrefix = "watcher", SpriteFactory = Spellcaster.Sprites.Shade, DropsLoot = false, MinFloor = 99,
            MaxHP = 16f, Speed = 5.2f, Height = 0.45f, Radius = 0.35f,
            AttackRange = 1.5f, Windup = 0.25f, Recover = 0.3f, AttackCooldown = 0.9f, Damage = 5f,
        };

        /// <summary>Shadow blob that splits into two shadelings when it dies.</summary>
        public static readonly EnemyType Shade = new EnemyType
        {
            Name = "Shade", SoundPrefix = "watcher", SpriteFactory = Spellcaster.Sprites.Shade,
            MaxHP = 55f, Speed = 3.2f, Height = 0.8f, Radius = 0.6f,
            AttackRange = 1.9f, Windup = 0.4f, Recover = 0.35f, AttackCooldown = 1.1f, Damage = 9f,
            SplitInto = Shadeling, SplitCount = 2,
        };

        /// <summary>End-of-floor boss: a giant eye that fires fans of bolts and summons imps.</summary>
        public static readonly EnemyType Boss = new EnemyType
        {
            Name = "The Watcher", SoundPrefix = "watcher", SpriteFactory = Spellcaster.Sprites.Cultist, IsBoss = true,
            MaxHP = 650f, Speed = 2.2f, Height = 3.6f, Radius = 1.0f, Hover = 0.7f, Flying = true,
            Ranged = true, AttackRange = 30f, PreferredMin = 7f, PreferredMax = 12f,
            Windup = 0.7f, Recover = 0.5f, AttackCooldown = 1.9f, Damage = 9f,
            ProjectileSpeed = 10f, ProjectileColor = new Color(1f, 0.35f, 0.6f),
            Shots = 5, SpreadDeg = 50f, SummonEvery = 3,
        };

        /// <summary>Every regular enemy (not the boss or shadelings), for menus and tests.</summary>
        public static readonly EnemyType[] Regulars = { Imp, Cultist, Hornet, Shade, Skull, Beetle };

        /// <summary>Picks an enemy for a room encounter from the floor's roster (see Roster); at most 2 beetles.</summary>
        public static EnemyType PickForEncounter(int floor, int depth, System.Func<float> rand, int beetles) =>
            PickForEncounter(floor, depth, rand, t => t == Beetle ? beetles : 0);
    }
}
