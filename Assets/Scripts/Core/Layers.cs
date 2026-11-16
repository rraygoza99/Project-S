using UnityEngine;

namespace Spellcaster
{
    /// <summary>Physics layers (unnamed user layers work fine by index).</summary>
    public static class Layers
    {
        public const int World = 8;
        public const int Enemy = 9;
        public const int Player = 10;
        public const int Prop = 11;
        /// <summary>Enemy movement colliders (on the floor). Shots and blasts hit the Enemy-layer hitbox instead.</summary>
        public const int EnemyBody = 12;

        public const int WorldMask = 1 << World;
        public const int EnemyMask = 1 << Enemy;
        public const int PlayerMask = 1 << Player;
        public const int PropMask = 1 << Prop;
        public const int EnemyBodyMask = 1 << EnemyBody;

        static bool setUp;
        /// <summary>Enemy hitboxes are for shots and blasts only: they never block anything's movement.</summary>
        public static void Setup()
        {
            if (setUp) return;
            setUp = true;
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Enemy, i, true);
        }
        /// <summary>What the player's bolts and spells collide with.</summary>
        public const int PlayerShotMask = WorldMask | EnemyMask | PropMask;
        /// <summary>Solid things enemies steer around.</summary>
        public const int SolidMask = WorldMask | PropMask;
    }
}
