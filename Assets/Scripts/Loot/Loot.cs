using UnityEngine;

namespace Spellcaster
{
    /// <summary>Drop chances for enemies, crates and cleared rooms. Luck multiplies every chance.</summary>
    public static class Loot
    {
        static float Luck(Run run) => 1f + (run ? run.Stats.Luck : 0f);

        public static void OnEnemyKilled(Enemy e, Run run)
        {
            if (!Dungeon.Instance || e.Type.IsBoss || !e.Type.DropsLoot || e.SelfDestructed || GameState.PracticeMode || GameState.TutorialMode) return;
            // Boss rooms pay out only through the boss itself (its item and heart), so the endlessly
            // summoned minions can't be farmed for gold and hearts.
            var room = Dungeon.Instance.RoomAt(e.transform.position);
            if (room != null && room.Type == RoomType.Boss) return;
            float luck = Luck(run);
            Vector3 p = e.transform.position;
            float coinChance = (e.Type.Ranged ? 0.65f : 0.4f) * luck;
            if (Random.value < coinChance) Coins(p, e.Type.Ranged ? Random.Range(1, 3) : 1);
            if (Random.value < 0.04f * luck) Pickup.Spawn(Items.Heart, p, pop: true);
            if (Random.value < 0.025f * luck) Pickup.Spawn(Items.RandomConsumable(), p, pop: true);
        }

        public static void CrateDrop(Vector3 p)
        {
            float luck = Luck(Run.Instance);
            float roll = Random.value;
            if (roll < 0.45f * luck) Coins(p, Random.Range(1, 4));
            else if (roll < 0.6f * luck) Pickup.Spawn(Items.Heart, p, pop: true);
            else if (roll < 0.7f * luck) Pickup.Spawn(Items.RandomConsumable(), p, pop: true);
        }

        /// <summary>Reward for clearing a combat room, dropped at a free spot near the centre.</summary>
        public static void RoomClearReward(Room room)
        {
            float luck = Luck(Run.Instance);
            if (Random.value > 0.5f * luck) return;
            Vector3 p = Dungeon.Instance.FindFreeSpot(room, room.Center, 1f);
            float roll = Random.value;
            if (roll < 0.45f) Pickup.Spawn(Items.BigCoin, p);
            else if (roll < 0.75f) Pickup.Spawn(Items.Heart, p);
            else Pickup.Spawn(Items.RandomConsumable(), p);
        }

        public static void Coins(Vector3 p, int n)
        {
            for (int i = 0; i < n; i++) Pickup.Spawn(Items.Coin, p + Vector3.up * 0.5f, pop: true, height: 0.35f);
        }
    }
}
