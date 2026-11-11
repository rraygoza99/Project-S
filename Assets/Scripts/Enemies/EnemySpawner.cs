using UnityEngine;

namespace Spellcaster
{
    /// <summary>Spawns enemies in the current room (the Dungeon decides what and when).</summary>
    public class EnemySpawner : MonoBehaviour
    {
        public static EnemySpawner Instance { get; private set; }

        void Awake() => Instance = this;

        void Update()
        {
            // Debug: K spawns 10 extra enemies in this room (handy for stress tests).
            if (Player.Instance && Player.Instance.InputEnabled && GameState.Instance.AcceptsGameplayInput && Input.GetKeyDown(KeyCode.K))
                for (int i = 0; i < 10; i++) SpawnRandom();
        }

        public Enemy SpawnRandom() => Spawn(Random.value < 0.65f ? EnemyType.Imp : EnemyType.Cultist);

        /// <summary>Spawns at a free spot of the player's room, away from the player.</summary>
        public Enemy Spawn(EnemyType type)
        {
            var d = Dungeon.Instance;
            var player = Player.Instance;
            var room = d && player ? d.RoomAt(player.transform.position) : null;
            Vector3 pos = room != null ? d.RandomSpawnPoint(room, player.transform.position, 7f) : Vector3.zero;
            Vector2 jitter = Random.insideUnitCircle * 0.6f;
            return SpawnAt(type, pos + new Vector3(jitter.x, 0.05f, jitter.y));
        }

        public Enemy SpawnAt(EnemyType type, Vector3 pos, float hpMult = 1f, float dmgMult = 1f)
        {
            Fx.Flash(pos + Vector3.up * 1f, new Color(0.7f, 0.2f, 0.9f), type.IsBoss ? 4f : 1.8f, 0.45f, 1f);
            return Enemy.Spawn(type, pos, hpMult, dmgMult);
        }
    }
}
