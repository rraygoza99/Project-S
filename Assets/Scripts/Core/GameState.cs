using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spellcaster
{
    public enum Phase { Title, Playing, Intermission, Dead }

    /// <summary>Game flow: title screen, waves with short breaks, death screen and restart.</summary>
    public class GameState : MonoBehaviour
    {
        public static GameState Instance { get; private set; }

        public const int MaxAlive = 30;
        public const float BreakDuration = 6f;

        public Phase Phase { get; private set; } = Phase.Title;
        public int Wave { get; private set; }
        public int Kills { get; private set; }
        public int RemainingInWave => toSpawn + Enemy.All.Count;
        public float BreakTimeLeft => Phase == Phase.Intermission ? phaseTimer : 0f;
        public string Banner { get; private set; }
        public float BannerTimer { get; private set; }
        public bool Paused { get; private set; }
        /// <summary>Test hook: freeze the wave flow (no spawning, no wave transitions).</summary>
        public bool HoldWaves;

        public bool ForgeOpen => SpellForgeUI.Instance && SpellForgeUI.Instance.IsOpen;
        public bool AcceptsGameplayInput => (Phase == Phase.Playing || Phase == Phase.Intermission) && !ForgeOpen && !Paused;

        static bool skipTitle;
        /// <summary>True after a restart: go straight into wave 1.</summary>
        public static bool SkipTitleOnLoad => skipTitle;
        int toSpawn;
        float spawnTimer, spawnInterval, phaseTimer;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            Enemy.Killed += OnKilled;
        }

        void OnDestroy() => Enemy.Killed -= OnKilled;

        void Start()
        {
            if (skipTitle || AutoTest.Active) Begin();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            BannerTimer = Mathf.Max(0f, BannerTimer - Time.unscaledDeltaTime);

            bool inRun = Phase == Phase.Playing || Phase == Phase.Intermission;
            Paused = inRun && !AutoTest.Active && Cursor.lockState != CursorLockMode.Locked && !ForgeOpen;
            Time.timeScale = (Paused || ForgeOpen || Phase == Phase.Dead) ? 0f : 1f;

            switch (Phase)
            {
                case Phase.Title:
                    if (Input.GetMouseButtonDown(0)) Begin();
                    break;

                case Phase.Playing:
                    if (HoldWaves) break;
                    spawnTimer -= dt;
                    if (toSpawn > 0 && spawnTimer <= 0f && Enemy.All.Count < MaxAlive)
                    {
                        spawnTimer = spawnInterval;
                        toSpawn--;
                        EnemySpawner.Instance.Spawn(PickType());
                    }
                    if (toSpawn == 0 && Enemy.All.Count == 0)
                    {
                        Phase = Phase.Intermission;
                        phaseTimer = BreakDuration;
                        Show($"Wave {Wave} cleared!", 2.5f);
                    }
                    break;

                case Phase.Intermission:
                    phaseTimer -= dt;
                    if (phaseTimer <= 0f || (Input.GetKeyDown(KeyCode.Return) && !ForgeOpen)) StartWave(Wave + 1);
                    break;

                case Phase.Dead:
                    if (Input.GetKeyDown(KeyCode.R)) Restart();
                    break;
            }
        }

        public void Begin()
        {
            if (Phase != Phase.Title) return;
            StartWave(1);
        }

        public void StartWave(int n)
        {
            Wave = n;
            Phase = Phase.Playing;
            toSpawn = 4 + 3 * n;
            spawnInterval = Mathf.Max(0.3f, 1.2f - 0.08f * n);
            spawnTimer = 0.5f;
            Show($"Wave {n}", 2f);
            Sfx.Play("wave", 0.6f);
        }

        EnemyType PickType()
        {
            float rangedChance = Mathf.Min(0.12f + 0.05f * Wave, 0.45f);
            return Random.value < rangedChance ? EnemyType.Cultist : EnemyType.Imp;
        }

        void OnKilled(Enemy e) => Kills++;

        public void OnPlayerDied()
        {
            if (Phase == Phase.Dead) return;
            Phase = Phase.Dead;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (SpellSystem.Instance) SpellSystem.Instance.SetChanneling(false);
        }

        public void Restart()
        {
            skipTitle = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void Show(string text, float seconds)
        {
            Banner = text;
            BannerTimer = seconds;
        }

        /// <summary>Test hook: remove every enemy and the rest of the wave.</summary>
        public void ClearWave()
        {
            toSpawn = 0;
            foreach (var e in Enemy.All.ToArray()) e.TakeDamage(99999f);
        }
    }
}
