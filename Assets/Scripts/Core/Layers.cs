namespace Spellcaster
{
    /// <summary>Physics layers (unnamed user layers work fine by index).</summary>
    public static class Layers
    {
        public const int World = 8;
        public const int Enemy = 9;
        public const int Player = 10;

        public const int WorldMask = 1 << World;
        public const int EnemyMask = 1 << Enemy;
        public const int PlayerMask = 1 << Player;
    }
}
