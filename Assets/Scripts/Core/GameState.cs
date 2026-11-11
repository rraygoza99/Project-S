using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spellcaster
{
    public enum Phase { Title, Playing, Dead, Victory }

    /// <summary>Game flow: title screen, the run, death / victory screens and restart.</summary>
    public class GameState : MonoBehaviour
    {
        public static GameState Instance { get; private set; }

        public Phase Phase { get; private set; } = Phase.Title;
        public int Kills { get; private set; }
        public float RunTime { get; private set; }
        public string Banner { get; private set; }
        public float BannerTimer { get; private set; }
        public bool Paused { get; private set; }

        public bool ForgeOpen => SpellForgeUI.Instance && SpellForgeUI.Instance.IsOpen;
        public bool AcceptsGameplayInput => Phase == Phase.Playing && !ForgeOpen && !Paused;
        public bool InRun => Phase == Phase.Playing;

        static bool skipTitle;
        /// <summary>True after a restart: go straight into the run.</summary>
        public static bool SkipTitleOnLoad => skipTitle;

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
            BannerTimer = Mathf.Max(0f, BannerTimer - Time.unscaledDeltaTime);
            Paused = InRun && !AutoTest.Active && Cursor.lockState != CursorLockMode.Locked && !ForgeOpen;
            Time.timeScale = (Paused || ForgeOpen || Phase == Phase.Dead || Phase == Phase.Victory) ? 0f : 1f;
            if (Phase == Phase.Playing) RunTime += Time.deltaTime;

            switch (Phase)
            {
                case Phase.Title:
                    if (Input.GetMouseButtonDown(0)) Begin();
                    break;
                case Phase.Dead:
                case Phase.Victory:
                    if (Input.GetKeyDown(KeyCode.R)) Restart();
                    break;
            }
        }

        public void Begin()
        {
            if (Phase != Phase.Title) return;
            Phase = Phase.Playing;
            Sfx.Play("wave", 0.6f);
        }

        void OnKilled(Enemy e) => Kills++;

        public void OnPlayerDied()
        {
            if (Phase != Phase.Playing) return;
            Phase = Phase.Dead;
            ReleaseCursor();
        }

        public void Win()
        {
            if (Phase != Phase.Playing) return;
            Phase = Phase.Victory;
            Sfx.Play("ready", 0.8f, 0.8f);
            ReleaseCursor();
        }

        void ReleaseCursor()
        {
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

        /// <summary>Test hook: resume play after the victory screen.</summary>
        public void ForcePlayingForTest() => Phase = Phase.Playing;

        /// <summary>Test hook: kill every enemy in the current fight.</summary>
        public void ClearEnemies()
        {
            foreach (var e in Enemy.All.ToArray()) e.TakeDamage(99999f);
        }
    }
}
