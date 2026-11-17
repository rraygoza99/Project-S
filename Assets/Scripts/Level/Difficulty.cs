using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// How the dungeon gets harder (and more rewarding) floor by floor. Every number in one place.
    /// Floors 1-3 keep the classic steps; deep floors (4+) rise more gently, with more gold, a second boss
    /// reward and a rest when you arrive, so a strong build can keep going.
    /// </summary>
    public static class Difficulty
    {
        // ---- enemies ----
        /// <summary>Enemy HP gained per floor on floors 2-3, and per floor after that.</summary>
        public static float HpPerFloor = 0.4f, DeepHpPerFloor = 0.25f;
        /// <summary>Enemy damage gained per floor on floors 2-3, and per floor after that (capped).</summary>
        public static float DmgPerFloor = 0.25f, DeepDmgPerFloor = 0.12f, MaxDmgMult = 3f;

        // ---- rewards on deep floors ----
        /// <summary>Coin drop chance per deep floor (floor 4 = +15%, floor 5 = +30%...).</summary>
        public static float DeepCoinBonus = 0.15f;
        /// <summary>Boss reward items from floor 4 on (floors 1-3: one).</summary>
        public static int DeepBossItems = 2;
        /// <summary>Arriving on a deep floor heals this share of max HP (everyone, in co-op).</summary>
        public static float DeepArrivalHeal = 0.25f;

        /// <summary>How many floors past the classic three (0 on floors 1-3).</summary>
        public static int Deep(int floor) => Mathf.Max(0, floor - Scenarios.ClassicFloors);

        public static float HpMult(int floor)
        {
            int classic = Mathf.Clamp(floor, 1, Scenarios.ClassicFloors) - 1;
            return 1f + HpPerFloor * classic + DeepHpPerFloor * Deep(floor);
        }

        public static float DmgMult(int floor)
        {
            int classic = Mathf.Clamp(floor, 1, Scenarios.ClassicFloors) - 1;
            return Mathf.Min(MaxDmgMult, 1f + DmgPerFloor * classic + DeepDmgPerFloor * Deep(floor));
        }

        public static float CoinChanceMult(int floor) => 1f + DeepCoinBonus * Deep(floor);
        public static int BossItems(int floor) => Deep(floor) > 0 ? DeepBossItems : 1;
    }
}
