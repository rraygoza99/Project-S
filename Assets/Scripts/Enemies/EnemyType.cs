using System;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>Data for an enemy archetype.</summary>
    public class EnemyType
    {
        public string Name;
        public string SoundPrefix;          // MonsterSounds/<prefix>_hit|_death|_attack, falls back to synth
        public Func<float, SpriteSet> SpriteFactory;
        public float MaxHP;
        public float Speed;
        public float Height;
        public float Radius;
        public float Hover;                 // floating enemies hover this far above the ground

        public bool Ranged;
        public float AttackRange;           // melee reach, or max shooting range
        public float PreferredMin, PreferredMax; // ranged: distance band to keep
        public float Windup, Recover;
        public float AttackCooldown;
        public float Damage;
        public float ProjectileSpeed;
        public Color ProjectileColor;

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
            MaxHP = 60f, Speed = 2.6f, Height = 1.9f, Radius = 0.45f, Hover = 0.35f,
            Ranged = true, AttackRange = 24f, PreferredMin = 8f, PreferredMax = 14f,
            Windup = 0.6f, Recover = 0.35f, AttackCooldown = 2.6f, Damage = 10f,
            ProjectileSpeed = 9f, ProjectileColor = new Color(0.35f, 0.95f, 1f),
        };
    }
}
