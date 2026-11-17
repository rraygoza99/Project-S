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
        /// <summary>The window lost focus mid-run: paused until you click back in.</summary>
        public bool FocusPaused { get; private set; }
        int resumeFrame = -10;

        /// <summary>True while a full-screen panel is open (the Forge, or one registered in <see cref="ExtraPanelOpen"/>): the game is paused.</summary>
        public bool ForgeOpen => (SpellForgeUI.Instance && SpellForgeUI.Instance.IsOpen) || (ExtraPanelOpen != null && ExtraPanelOpen());
        /// <summary>Hook: another panel that pauses the game like the Forge (Grimoire rewards).</summary>
        public static System.Func<bool> ExtraPanelOpen;
        // The click that pressed "Start game" must not also fire a bolt.
        public bool AcceptsGameplayInput => Phase == Phase.Playing && !ForgeOpen && !Paused && Time.frameCount > beganFrame + 1 && Time.frameCount > resumeFrame + 1;
        int beganFrame = -10;
        public bool InRun => Phase == Phase.Playing;

        /// <summary>Practice tool session (main menu): all spells, no cooldowns, no damage, no fights.</summary>
        public static bool PracticeMode { get; private set; }
        /// <summary>The run opens with the tutorial in the start room.</summary>
        public static bool TutorialMode { get; private set; }

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

        void OnApplicationFocus(bool focus) => SetFocus(focus);

        /// <summary>Alt-tab / focus loss: pause the run (test hook too). The game resumes when you click back in.</summary>
        public void SetFocus(bool focus)
        {
            Debug.Log($"[Spellcaster] window focus {(focus ? "regained" : "lost")} ({Screen.width}x{Screen.height}, {Screen.fullScreenMode})");
            if (focus || !InRun || ForgeOpen) return;
            FocusPaused = true;
            if (!AutoTest.Active) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        /// <summary>The key or click that closed a panel this frame must not also act in the game.</summary>
        public void IgnoreInputThisFrame() => resumeFrame = Time.frameCount;

        public void ClearFocusPause()
        {
            FocusPaused = false;
            resumeFrame = Time.frameCount; // the click that resumes must not also cast
        }

        void Start()
        {
            if (PendingContinue) { PendingContinue = false; Continue(); return; }
            if (skipTitle && !AutoTest.Active) Begin();
        }

        void Update()
        {
            BannerTimer = Mathf.Max(0f, BannerTimer - Time.unscaledDeltaTime);
            if (FocusPaused && Application.isFocused && !AutoTest.Active && Input.GetMouseButtonDown(0)) ClearFocusPause();
            Paused = InRun && !ForgeOpen && (FocusPaused || (!AutoTest.Active && Cursor.lockState != CursorLockMode.Locked));
            Time.timeScale = (Paused || ForgeOpen || Phase == Phase.Dead || Phase == Phase.Victory) ? 0f : 1f;
            if (Phase == Phase.Playing) RunTime += Time.deltaTime;

            switch (Phase)
            {
                case Phase.Playing:
                    if (Paused && Input.GetKeyDown(KeyCode.Q)) ToMenu();
                    break;
                case Phase.Dead:
                case Phase.Victory:
                    if (Input.GetKeyDown(KeyCode.R)) Restart();
                    else if (Input.GetKeyDown(KeyCode.Escape)) ToMenu();
                    break;
            }
        }

        public void Begin()
        {
            if (Phase != Phase.Title) return;
            Phase = Phase.Playing;
            beganFrame = Time.frameCount;
            if (PracticeMode) SetupPractice();
            else if (TutorialMode && Tutorial.Instance) Tutorial.Instance.Begin();
            Sfx.Play("wave", 0.6f);
        }

        /// <summary>Starts a run that opens with the tutorial.</summary>
        public void StartTutorial()
        {
            TutorialMode = true;
            Begin();
        }

        public void EndTutorial() => TutorialMode = false;

        /// <summary>Starts the practice tool from the main menu.</summary>
        public void StartPractice()
        {
            PracticeMode = true;
            Begin();
        }

        /// <summary>Test hook: switch the current run into practice.</summary>
        /// <summary>Test hook: back to a normal run after the practice checks.</summary>
        public void ExitPracticeForTest() => PracticeMode = false;

        public void EnterPracticeForTest()
        {
            PracticeMode = true;
            SetupPractice();
        }

        void SetupPractice()
        {
            SpellBook.LearnAll();
            if (Dungeon.Instance) Dungeon.Instance.HoldEncounters = true;
            if (Player.Instance) Player.Instance.God = true;
            if (SpellSystem.Instance) SpellSystem.Instance.ResetCooldowns();
            Show("Practice", 2f);
            if (PracticeTool.Instance) PracticeTool.Instance.Begin();
        }

        void OnKilled(Enemy e) => Kills++;

        /// <summary>Loading a save: the run's kill count and time so far.</summary>
        public void RestoreTotals(int kills, float runTime)
        {
            Kills = kills;
            RunTime = runTime;
        }

        /// <summary>Set before a scene reload to continue the saved run once the world is built (debug load).</summary>
        public static bool PendingContinue;

        /// <summary>Continues the saved run from the main menu. On failure the menu shows why.</summary>
        public bool Continue()
        {
            if (Phase != Phase.Title) return false;
            PracticeMode = false;
            TutorialMode = false;
            if (!SaveSystem.LoadRun())
            {
                ToMenu(); // a clean world for the menu; it shows SaveSystem.LastError
                return false;
            }
            Begin();
            Show("Continued", 1.5f);
            return true;
        }

        public void OnPlayerDied()
        {
            if (Phase != Phase.Playing) return;
            Phase = Phase.Dead;
            if (!PracticeMode && !TutorialMode) SaveSystem.Delete(); // roguelike: the run is over
            Progress.Add("deaths");
            Progress.Flush();
            ReleaseCursor();
        }

        public void Win()
        {
            if (Phase != Phase.Playing) return;
            Phase = Phase.Victory;
            if (!PracticeMode && !TutorialMode) SaveSystem.Delete();
            Progress.Add("wins");
            Progress.Flush();
            Sfx.Play("ready", 0.8f, 0.8f);
            ReleaseCursor();
        }

        void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (SpellSystem.Instance) SpellSystem.Instance.SetChanneling(false);
        }

        /// <summary>Ends the run and goes back to the main menu.</summary>
        public void ToMenu()
        {
            skipTitle = false;
            PracticeMode = false;
            TutorialMode = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void Restart()
        {
            skipTitle = true;
            PracticeMode = false;
            TutorialMode = false;
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
            foreach (var e in Enemy.All.ToArray()) e.Kill();
        }
    }
}
