using UnityEngine;

namespace Spellcaster
{
    /// <summary>Spawns enemies at the arena's spawn sigils (GameState decides what and when).</summary>
    public class EnemySpawner : MonoBehaviour
    {
        public static EnemySpawner Instance { get; private set; }

        void Awake() => Instance = this;

        void Update()
        {
            // Debug: K spawns 10 extra enemies (handy for stress tests).
            if (Player.Instance && Player.Instance.InputEnabled && GameState.Instance.AcceptsGameplayInput && Input.GetKeyDown(KeyCode.K))
                for (int i = 0; i < 10; i++) SpawnRandom();
        }

        public Enemy SpawnRandom() => Spawn(Random.value < 0.65f ? EnemyType.Imp : EnemyType.Cultist);

        public Enemy Spawn(EnemyType type)
        {
            // Prefer spawn points away from the player.
            Vector3 point = ArenaBuilder.SpawnPoints[Random.Range(0, ArenaBuilder.SpawnPoints.Length)];
            for (int tries = 0; tries < 6 && Player.Instance && Vector3.Distance(point, Player.Instance.transform.position) < 8f; tries++)
                point = ArenaBuilder.SpawnPoints[Random.Range(0, ArenaBuilder.SpawnPoints.Length)];
            Vector2 jitter = Random.insideUnitCircle * 1.2f;
            return SpawnAt(type, point + new Vector3(jitter.x, 0.05f, jitter.y));
        }

        public Enemy SpawnAt(EnemyType type, Vector3 pos)
        {
            Fx.Flash(pos + Vector3.up * 1f, new Color(0.7f, 0.2f, 0.9f), 1.8f, 0.45f, 1f);
            return Enemy.Spawn(type, pos);
        }
    }
}
