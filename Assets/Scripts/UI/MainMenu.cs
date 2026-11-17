using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Spellcaster.Net;

namespace Spellcaster
{
    /// <summary>
    /// Title screen menu: Start game, Tutorial, Practice tool, Options (with a Controls page for key
    /// rebinding) and Exit game. Mouse (hover + click) and keyboard (W/S or arrows to move, Enter/Space
    /// to choose, A/D or arrows to change a value, Esc to go back). Drawn by the HUD on the title phase.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public static MainMenu Instance { get; private set; }

        public enum Page { Main, Options, Controls, Unlocks, Confirm, Seed, Pause, Multiplayer, HostSetup, Browse, Lobby, Direct }
        public Page Current { get; private set; } = Page.Main;
        public int Selected { get; private set; }
        /// <summary>Action and slot waiting for a key press (Controls page), or null.</summary>
        public (GameAction action, int slot)? Listening { get; private set; }

        class Item
        {
            public string Label;
            public Func<string> Value;          // value rows show "< value >"
            public Action Activate;
            public Action<int> Change;          // -1 / +1
            public GameAction? Binding;         // Controls page rows
            public bool Field;                  // a text box (the seed): Value is shown without < >
            public bool Plain;                  // Value shown on the right, no < > (lobby rows)
            public Func<Texture2D> Icon;        // a picture at the left of the row (Steam avatar)
        }

        List<Item> mainItems, optionItems, controlItems, unlockItems, confirmItems, seedItems, pauseItems;
        /// <summary>What the player has typed on the Seeded run page.</summary>
        public string SeedText { get; private set; } = "";
        public const int MaxSeedLength = 24;
        /// <summary>What "Yes" on the confirm page starts (a new run or the tutorial) after deleting the save.</summary>
        Action confirmAction;
        Vector2 lastMouse;
        int listenFrame;

        List<Item> Items => Current switch
        {
            Page.Main => mainItems, Page.Options => optionItems, Page.Unlocks => unlockItems, Page.Confirm => confirmItems,
            Page.Seed => seedItems, Page.Pause => pauseItems, Page.Multiplayer => MultiplayerItems(), Page.HostSetup => hostItems,
            Page.Browse => BrowseItems(), Page.Lobby => LobbyItems(), Page.Direct => directItems, _ => controlItems,
        };

        // ---------------- multiplayer pages ----------------

        static bool IsNetPage(Page p) => p == Page.Multiplayer || p == Page.HostSetup || p == Page.Browse || p == Page.Lobby || p == Page.Direct;
        List<Item> hostItems, directItems;
        int hostMax = NetConfig.DefaultMaxPlayers;
        LobbyPrivacy hostPrivacy = LobbyPrivacy.FriendsOnly;
        /// <summary>Debug direct connect: the address typed on the Direct page.</summary>
        public string AddressText { get; private set; } = NetConfig.DebugAddress;

        public void SetAddressText(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (char c in s ?? "")
                if ((char.IsLetterOrDigit(c) || c == '.' || c == ':' || c == '-') && b.Length < 64) b.Append(c);
            AddressText = b.ToString();
        }

        void BuildNetPages()
        {
            hostItems = new List<Item>
            {
                new Item { Label = "Max players", Value = () => hostMax.ToString(),
                    Change = d => hostMax = Mathf.Clamp(hostMax + d, NetConfig.MinPlayers, NetConfig.MaxPlayersLimit) },
                new Item { Label = "Who can join", Value = () => hostPrivacy switch { LobbyPrivacy.Public => "Public", LobbyPrivacy.InviteOnly => "Invite only", _ => "Friends only" },
                    Change = d => hostPrivacy = (LobbyPrivacy)(((int)hostPrivacy + d + 3) % 3) },
                new Item { Label = "Create lobby", Activate = () => { if (!NetSession.Instance.HostSteam(hostMax, hostPrivacy)) Sfx.Play("fizzle", 0.4f, 1.3f); } },
                new Item { Label = "Back", Activate = () => Open(Page.Multiplayer) },
            };
            foreach (var it in hostItems)
                if (it.Change != null && it.Activate == null) { var ch = it.Change; it.Activate = () => ch(1); }
            directItems = new List<Item>
            {
                new Item { Label = "Address", Field = true, Value = () => AddressText + (Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f ? "_" : " ") },
                new Item { Label = "Join this address", Activate = () => { if (!NetSession.Instance.JoinDirect(AddressText)) Sfx.Play("fizzle", 0.4f, 1.3f); } },
                new Item { Label = $"Host on port {NetConfig.DebugPort}", Activate = () => NetSession.Instance.HostDirect(NetConfig.DebugPort, hostMax) },
                new Item { Label = "Back", Activate = () => Open(Page.Multiplayer) },
            };
        }

        List<Item> MultiplayerItems()
        {
            var list = new List<Item>
            {
                new Item { Label = "Host a game", Activate = () => { if (SteamService.Ready) Open(Page.HostSetup); else Sfx.Play("fizzle", 0.4f, 1.3f); } },
                new Item { Label = "Join a game", Activate = () => { if (!SteamService.Ready) { Sfx.Play("fizzle", 0.4f, 1.3f); return; } Open(Page.Browse); NetSession.Instance.SearchLobbies(); } },
            };
            if (NetConfig.DebugTransportAllowed) list.Add(new Item { Label = "Direct IP (debug)", Activate = () => Open(Page.Direct) });
            list.Add(new Item { Label = "Back", Activate = () => Open(Page.Main) });
            return list;
        }

        List<Item> BrowseItems()
        {
            var ns = NetSession.Instance;
            var list = new List<Item>();
            foreach (var l in ns.Found)
            {
                var lobby = l;
                string who = string.IsNullOrEmpty(lobby.hostName) ? "A game" : lobby.hostName + "'s game";
                list.Add(new Item
                {
                    Label = who + (lobby.friend ? "  (friend)" : ""), Plain = true,
                    Value = () => lobby.VersionOk ? $"{lobby.members} / {lobby.max}" : $"version {lobby.version}",
                    Activate = () => { if (lobby.VersionOk) ns.JoinSteamLobby(lobby.id); else Sfx.Play("fizzle", 0.4f, 1.3f); },
                });
            }
            list.Add(new Item { Label = ns.Searching ? "Searching..." : "Refresh", Activate = () => ns.SearchLobbies() });
            list.Add(new Item { Label = "Back", Activate = () => Open(Page.Multiplayer) });
            return list;
        }

        List<Item> LobbyItems()
        {
            var ns = NetSession.Instance;
            var list = new List<Item>();
            foreach (var r in ns.Roster)
            {
                var p = r;
                bool me = p.id == ns.MyId;
                list.Add(new Item
                {
                    Label = p.name + (me ? "  (you)" : ""), Plain = true,
                    Value = () => p.host ? "Host" : p.ready ? "Ready" : "Not ready",
                    Icon = p.steamId != 0 ? () => SteamService.Avatar(p.steamId) : (Func<Texture2D>)null,
                    Activate = ns.IsHost && !p.host ? () => ns.Kick(p.id) : (Action)null,
                });
            }
            if (!ns.IsHost && ns.MyId >= 0)
                list.Add(new Item { Label = "Ready", Value = () => ns.MyReady ? "Yes" : "No", Change = d => ns.SetReady(!ns.MyReady), Activate = () => ns.SetReady(!ns.MyReady) });
            if (ns.IsHost && ns.Using == NetSession.Kind.Steam)
                list.Add(new Item { Label = "Invite friends (Steam overlay)", Activate = ns.OpenInviteOverlay });
            if (ns.IsHost && SaveSystem.HasValidSave)
                list.Add(new Item { Label = "Run", Value = () => ns.ContinueSaved ? "Continue saved (" + SaveSystem.Describe() + ")" : "New run",
                    Change = d => ns.ContinueSaved = !ns.ContinueSaved, Activate = () => ns.ContinueSaved = !ns.ContinueSaved });
            if (ns.IsHost)
            {
                int waiting = ns.Roster.Count(r => !r.ready && !r.host);
                list.Add(new Item { Label = waiting > 0 ? $"Start run (waiting for {waiting})" : "Start run",
                    Activate = () => { if (!ns.StartRun()) Sfx.Play("fizzle", 0.4f, 1.3f); } });
            }
            list.Add(new Item { Label = ns.IsHost ? "Close lobby" : "Leave lobby", Activate = () => { ns.Leave(); Open(Page.Multiplayer); } });
            return list;
        }

        /// <summary>The host started the run: everyone gets the other players, then the same seeded floor.</summary>
        public void BeginNetworkRun(StartRunMsg msg)
        {
            if (!GameState.Instance || GameState.Instance.Phase != Phase.Title) return;
            LeaveMenu();
            NetPlayers.SetupRun(msg);
            if (msg.resume && NetSession.Instance.IsHost)
            {
                // The host resumes its save; the others get their kept characters and the world.
                if (GameState.Instance.Continue()) NetSave.AfterResume();
                return;
            }
            if (msg.floor > 1 || msg.join) GameState.Instance.BeginAtFloor(msg.seed, Mathf.Max(1, msg.floor));
            else GameState.Instance.BeginSeeded(msg.seed, null);
            // Enemies and fights are the host's (replicated in Phase 3): a client's own world stays empty of them.
            if (NetSession.IsClientOnly && Dungeon.Instance) Dungeon.Instance.HoldEncounters = true;
        }

        /// <summary>Joining or leaving a session moves the menu to the lobby or back to the multiplayer page.</summary>
        void TrackSession()
        {
            var ns = NetSession.Instance;
            if (!ns || !Visible) return;
            if (ns.InSession && Current != Page.Lobby && IsNetPage(Current)) { Current = Page.Lobby; Selected = 0; }
            else if (!ns.InSession && Current == Page.Lobby) { Current = Page.Multiplayer; Selected = 0; }
        }

        // ---------------- pause menu (in a run) ----------------

        /// <summary>The run is paused: the menu shows Resume / Options / Quit over the game.</summary>
        public bool PauseUp
        {
            get { var gs = GameState.Instance; return gs && gs.InRun && gs.Paused; }
        }
        bool wasPauseUp;
        int pauseOpenedFrame = -10;
        /// <summary>The frame the pause menu resumed the game (the Esc that resumed must not pause again).</summary>
        public static int ResumedFrame = -10;

        /// <summary>A real run (not the tutorial or practice): Quit saves.</summary>
        static bool QuitSaves => !GameState.PracticeMode && !GameState.TutorialMode && !NetSession.IsClientOnly; // only the host saves

        void BuildPause()
        {
            pauseItems = new List<Item>
            {
                new Item { Label = "Resume", Activate = Resume },
                new Item { Label = "Options", Activate = () => Open(Page.Options) },
                new Item { Label = NetSession.IsClientOnly ? "Leave game" : QuitSaves ? "Save & quit" : "Quit to menu", Activate = SaveAndQuit },
            };
        }

        /// <summary>Closes the pause menu and goes back to the game.</summary>
        public void Resume()
        {
            var gs = GameState.Instance;
            gs.ClearFocusPause(); // also: the click / key that resumed must not act in the game
            ResumedFrame = Time.frameCount;
            if (!AutoTest.Active) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            Current = Page.Main;
            Sfx.Play("cast", 0.5f);
        }

        /// <summary>
        /// Saves the run for Quit. Out of a fight the save is right here (this room becomes the checkpoint);
        /// in a fight you keep everything you have, and the fight starts over from the last cleared room.
        /// </summary>
        public static bool SaveForQuit()
        {
            if (!QuitSaves || !SaveSystem.CanSave) return false;
            var d = Dungeon.Instance;
            if (d && d.Current != null && !d.InCombat) d.CheckpointRoomId = d.Current.Id;
            return SaveSystem.Save();
        }

        /// <summary>Quit from the pause menu: saves the run (a real run only), then the main menu.</summary>
        public static void SaveAndQuit()
        {
            SaveForQuit();
            GameState.Instance.ToMenu();
        }

        /// <summary>Test hook: choose a row of the current page by its label.</summary>
        public bool ChooseForTest(string label)
        {
            int i = Items.FindIndex(it => it.Label == label);
            if (i < 0) return false;
            Choose(i);
            return true;
        }

        /// <summary>Test hook: the labels of the current page.</summary>
        public List<string> LabelsForTest() => Items.ConvertAll(it => it.Label);

        /// <summary>The pause menu opens on its first page each time the game pauses, and closes with it.</summary>
        void TrackPause()
        {
            bool up = PauseUp;
            if (up && !wasPauseUp)
            {
                BuildPause();
                Current = Page.Pause;
                Selected = 0;
                Listening = null;
                pauseOpenedFrame = Time.frameCount;
            }
            else if (!up && wasPauseUp && Current != Page.Main)
            {
                Current = Page.Main;
                Listening = null;
            }
            wasPauseUp = up;
        }

        /// <summary>The main page: Continue appears first when a valid save exists.</summary>
        void BuildMain()
        {
            mainItems = new List<Item>();
            if (SaveSystem.HasValidSave) mainItems.Add(new Item { Label = "Continue", Activate = ContinueRun });
            mainItems.Add(new Item { Label = "Start game", Activate = StartGame });
            mainItems.Add(new Item { Label = "Seeded run", Activate = () => { Open(Page.Seed); Selected = 1; } });
            mainItems.Add(new Item { Label = "Multiplayer", Activate = () => Open(Page.Multiplayer) });
            mainItems.Add(new Item { Label = "Tutorial", Activate = StartTutorial });
            mainItems.Add(new Item { Label = "Practice tool", Activate = StartPractice });
            mainItems.Add(new Item { Label = "Unlocks", Activate = () => Open(Page.Unlocks) });
            mainItems.Add(new Item { Label = "Options", Activate = () => Open(Page.Options) });
            mainItems.Add(new Item { Label = "Exit game", Activate = ExitGame });
            if (Current == Page.Main) Selected = Mathf.Clamp(Selected, 0, mainItems.Count - 1);
        }

        /// <summary>Continues the saved run.</summary>
        public void ContinueRun()
        {
            LeaveMenu();
            if (!GameState.Instance.Continue()) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        /// <summary>A new run replaces the saved one: ask first.</summary>
        void ConfirmNewRun(Action start)
        {
            if (!SaveSystem.HasValidSave) { start(); return; }
            confirmAction = start;
            Open(Page.Confirm);
            Selected = 1; // "No" is the safe default
        }

        static string OnOff(bool b) => b ? "On" : "Off";

        void Awake()
        {
            Instance = this;
            BuildMain();
            BuildNetPages();
            // Back from a session that ended (host left, kicked): show why on the multiplayer page.
            if (NetSession.Instance && NetSession.Instance.LastError != null) Current = Page.Multiplayer;
            seedItems = new List<Item>
            {
                new Item { Label = "Seed", Field = true, Value = () => SeedText.Length > 0 ? SeedText + (Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f ? "_" : " ") : "type a number or a word" },
                new Item { Label = "Start run with this seed", Activate = StartSeeded },
                new Item { Label = "Random seed", Activate = () => { SetSeedText(UnityEngine.Random.Range(100000, 999999999).ToString()); Tick(); } },
                new Item { Label = "Back", Activate = () => Open(Page.Main) },
            };
            confirmItems = new List<Item>
            {
                new Item { Label = "Yes, start a new run", Activate = () =>
                    {
                        SaveSystem.Delete();
                        BuildMain();
                        var go = confirmAction;
                        confirmAction = null;
                        Current = Page.Main;
                        go?.Invoke();
                    } },
                new Item { Label = "No, keep my saved run", Activate = () => Open(Page.Main) },
            };
            optionItems = new List<Item>
            {
                new Item { Label = "Mouse sensitivity", Value = () => Settings.MouseSensitivity.ToString("0.0"),
                    Change = d => Settings.SetMouseSensitivity(Settings.MouseSensitivity + 0.2f * d) },
                new Item { Label = "Invert mouse Y", Value = () => OnOff(Settings.InvertY),
                    Change = d => Settings.SetInvertY(!Settings.InvertY) },
                new Item { Label = "Rune drawing size", Value = () => Settings.RuneStroke.ToString("0.0"),
                    Change = d => Settings.SetRuneStroke(Settings.RuneStroke + 0.5f * d) },
                new Item { Label = "Field of view", Value = () => Settings.FieldOfView.ToString("0"),
                    Change = d => Settings.SetFieldOfView(Settings.FieldOfView + 5f * d) },
                new Item { Label = "Volume", Value = () => Mathf.RoundToInt(Settings.Volume * 100f) + "%",
                    Change = d => Settings.SetVolume(Settings.Volume + 0.05f * d) },
                new Item { Label = "Display mode", Value = () => Settings.Fullscreen ? "Fullscreen" : "Windowed",
                    Change = d => Settings.SetFullscreen(!Settings.Fullscreen) },
                new Item { Label = "Resolution", Value = () => { var r = Settings.CurrentResolution; return $"{r.x} x {r.y}"; },
                    Change = d => Settings.CycleResolution(d) },
                new Item { Label = "Show FPS", Value = () => OnOff(Settings.ShowFps),
                    Change = d => Settings.SetShowFps(!Settings.ShowFps) },
                new Item { Label = "Casting style", Value = () => Settings.CastingStyle == CastingStyle.Momentum ? "Momentum" : "Classic",
                    Change = d => Settings.SetCastingStyle(Settings.CastingStyle == CastingStyle.Momentum ? CastingStyle.Classic : CastingStyle.Momentum) },
                new Item { Label = "Momentum cue (FOV, bob)", Value = () => OnOff(Settings.MomentumCue),
                    Change = d => Settings.SetMomentumCue(!Settings.MomentumCue) },
                new Item { Label = "Map key", Value = () => Settings.MapToggle ? "Toggle" : "Hold",
                    Change = d => Settings.SetMapToggle(!Settings.MapToggle) },
                new Item { Label = "Dev mode: hitboxes (F3)", Value = () => OnOff(Settings.DevMode),
                    Change = d => Settings.SetDevMode(!Settings.DevMode) },
                new Item { Label = "Grimoire cards (experimental)", Value = () => OnOff(Settings.GrimoireEnabled),
                    Change = d => Settings.SetGrimoireEnabled(!Settings.GrimoireEnabled) },
                new Item { Label = "Controls...", Activate = () => Open(Page.Controls) },
                new Item { Label = "Reset to defaults", Activate = () => { Settings.ResetToDefaults(); Sfx.Play("pickup", 0.5f); } },
                new Item { Label = "Back", Activate = () => Open(PauseUp ? Page.Pause : Page.Main) },
            };
            foreach (var it in optionItems)
                if (it.Change != null && it.Activate == null) { var ch = it.Change; it.Activate = () => ch(1); }

            controlItems = new List<Item>();
            foreach (var a in Bindings.All)
            {
                var action = a;
                controlItems.Add(new Item { Label = Bindings.Label(action), Binding = action, Activate = () => Listen(action, 0) });
            }
            controlItems.Add(new Item { Label = "Reset controls", Activate = () => { Bindings.ResetDefaults(); Sfx.Play("pickup", 0.5f); } });
            controlItems.Add(new Item { Label = "Back", Activate = () => Open(Page.Options) });
            unlockItems = new List<Item> { new Item { Label = "Back", Activate = () => Open(Page.Main) } };
        }

        public bool Visible => GameState.Instance && GameState.Instance.Phase == Phase.Title;

        void Update()
        {
            TrackPause();
            bool paused = PauseUp;
            if (!Visible && !paused) return;
            if (!paused && Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (Listening.HasValue) return; // the next key press is captured in DrawGUI
            if (paused && Current == Page.Pause)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && Time.frameCount > pauseOpenedFrame + 1) { Resume(); return; }
                if (Input.GetKeyDown(KeyCode.Q)) { SaveAndQuit(); return; }
                if (Input.GetKeyDown(KeyCode.C) && QuitSaves) RunRandom.CopySeed();
            }

            TrackSession();
            Selected = Mathf.Clamp(Selected, 0, Mathf.Max(0, Items.Count - 1));
            bool typing = Current == Page.Seed || Current == Page.Direct; // letters go into the text box: arrows move, Enter chooses
            if ((!typing && Input.GetKeyDown(KeyCode.W)) || Input.GetKeyDown(KeyCode.UpArrow)) Move(-1);
            if ((!typing && Input.GetKeyDown(KeyCode.S)) || Input.GetKeyDown(KeyCode.DownArrow)) Move(1);
            var item = Items[Selected];
            if (item.Change != null)
            {
                if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) { item.Change(-1); Tick(); }
                if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) { item.Change(1); Tick(); }
            }
            if (item.Binding.HasValue && (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete)))
            {
                Bindings.Set(item.Binding.Value, 1, KeyCode.None); // clear the secondary key
                Tick();
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Choose(typing && Selected == 0 ? 1 : Selected);
            else if (!typing && Input.GetKeyDown(KeyCode.Space)) Choose(Selected);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Current == Page.Controls) Open(Page.Options);
                else if (Current == Page.Options && paused) Open(Page.Pause);
                else if (Current == Page.Options || Current == Page.Unlocks || Current == Page.Confirm || Current == Page.Seed || Current == Page.Multiplayer) Open(Page.Main);
                else if (Current == Page.HostSetup || Current == Page.Browse || Current == Page.Direct) Open(Page.Multiplayer);
                else if (Current == Page.Lobby) { NetSession.Instance.Leave(); Open(Page.Multiplayer); }
            }
        }

        void Move(int d)
        {
            Selected = (Selected + d + Items.Count) % Items.Count;
            Tick();
        }

        static void Tick() => Sfx.Play("tick", 0.6f, 1.1f);

        void Choose(int index)
        {
            Selected = index;
            Items[index].Activate?.Invoke();
            if (Current == Page.Options && Items[index].Change != null) Tick();
        }

        public void Open(Page s)
        {
            if (s == Page.Main) BuildMain();
            if (s == Page.Main && NetSession.Instance) NetSession.Instance.LastError = null;
            Current = s;
            Selected = 0;
            Listening = null;
            Sfx.Play("pickup", 0.5f);
        }

        /// <summary>Waits for the next key or mouse button to bind to this action's slot.</summary>
        public void Listen(GameAction action, int slot)
        {
            Listening = (action, slot);
            listenFrame = Time.frameCount;
            Sfx.Play("channel", 0.5f);
        }

        void Bind(KeyCode key)
        {
            var (action, slot) = Listening.Value;
            Listening = null;
            Bindings.Set(action, slot, key);
            Sfx.Play("ready", 0.5f);
        }

        void LeaveMenu()
        {
            Sfx.Play("cast", 0.6f);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Starts a run; the very first one begins with the tutorial.</summary>
        public void StartGame() => ConfirmNewRun(() =>
        {
            LeaveMenu();
            if (!Settings.TutorialDone && !AutoTest.Active) GameState.Instance.StartTutorial();
            else GameState.Instance.Begin();
        });

        /// <summary>Sets the typed seed (letters, digits, spaces and dashes; at most MaxSeedLength).</summary>
        public void SetSeedText(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (char c in s ?? "")
                if ((char.IsLetterOrDigit(c) || c == ' ' || c == '-') && b.Length < MaxSeedLength) b.Append(c);
            SeedText = b.ToString();
        }

        /// <summary>Starts a run from the typed seed (asks first if a saved run would be replaced).</summary>
        public void StartSeeded()
        {
            int seed = RunRandom.ParseSeed(SeedText);
            if (seed == 0) { Sfx.Play("fizzle", 0.4f, 1.3f); return; }
            string typed = SeedText.Trim();
            ConfirmNewRun(() =>
            {
                LeaveMenu();
                GameState.Instance.BeginSeeded(seed, typed);
            });
        }

        /// <summary>Starts a run that opens with the tutorial.</summary>
        public void StartTutorial() => ConfirmNewRun(() =>
        {
            LeaveMenu();
            GameState.Instance.StartTutorial();
        });

        /// <summary>Practice runes with every spell, no cooldowns and training dummies.</summary>
        public void StartPractice()
        {
            LeaveMenu();
            GameState.Instance.StartPractice();
        }

        public static void ExitGame()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        /// <summary>Under the multiplayer headers: Steam's state, what the session is doing, or what went wrong.</summary>
        static void DrawNetStatus(float W, float H, float u)
        {
            var ns = NetSession.Instance;
            string text; Color col;
            if (ns && ns.LastError != null) { text = ns.LastError; col = new Color(1f, 0.6f, 0.45f); }
            else if (ns && ns.Status != null) { text = ns.Status; col = new Color(0.85f, 0.8f, 1f); }
            else if (!SteamService.Ready) { text = SteamService.Error ?? "Steam isn't available."; col = new Color(1f, 0.6f, 0.45f); }
            else { text = "Signed in to Steam as " + SteamService.MyName; col = new Color(0.6f, 1f, 0.7f); }
            var st = new GUIStyle(HUD.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            st.normal.textColor = col;
            HUD.Shadowed(new Rect(W * 0.1f, H * 0.095f, W * 0.8f, 2.4f * u), text, st);
        }

        /// <summary>Every achievement: reward icon and name, goal, progress bar.</summary>
        static void DrawUnlocks(float W, float H, float u)
        {
            var all = Progress.All;
            int rows = (all.Count + 1) / 2;
            float cellW = Mathf.Min(48f * u, W * 0.46f), cellH = 4.4f * u, gap = 1f * u;
            float x0 = (W - (cellW * 2 + gap)) / 2, y0 = H * 0.135f;
            var name = new GUIStyle(HUD.Label) { fontSize = Mathf.RoundToInt(HUD.Label.fontSize * 0.85f), clipping = TextClipping.Clip };
            var small = new GUIStyle(HUD.Small) { clipping = TextClipping.Clip };
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                bool done = Progress.IsDone(a);
                int col = i < rows ? 0 : 1, row = i < rows ? i : i - rows;
                var cell = new Rect(x0 + col * (cellW + gap), y0 + row * (cellH + 0.3f * u), cellW, cellH);
                HUD.Fill(cell, done ? new Color(0.25f, 0.18f, 0.05f, 0.65f) : new Color(0f, 0f, 0f, 0.5f));
                if (done) HUD.Frame(cell, new Color(1f, 0.85f, 0.45f, 0.7f), Mathf.Max(1f, 0.1f * u));
                var icon = a.RewardIcon;
                var ir = new Rect(cell.x + 0.4f * u, cell.y + 0.4f * u, cellH - 0.8f * u, cellH - 0.8f * u);
                if (icon)
                {
                    var prev = GUI.color;
                    GUI.color = done ? Color.white : new Color(0.35f, 0.35f, 0.4f, 0.9f);
                    GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit);
                    GUI.color = prev;
                }
                float tx = ir.xMax + 0.7f * u, tw = cell.xMax - tx - 0.5f * u;
                name.normal.textColor = done ? new Color(1f, 0.85f, 0.45f) : new Color(0.8f, 0.8f, 0.85f);
                HUD.Shadowed(new Rect(tx, cell.y + 0.15f * u, tw, 2f * u), (a.IsSpell ? "Spell: " : "Item: ") + a.RewardName, name);
                small.normal.textColor = new Color(0.75f, 0.75f, 0.8f);
                HUD.Shadowed(new Rect(tx, cell.y + 2.1f * u, tw * 0.62f, 2f * u), a.Goal, small);
                // progress
                int have = Mathf.Min(Progress.Get(a.Counter), a.Target);
                var bar = new Rect(cell.xMax - tw * 0.36f - 0.5f * u, cell.y + 2.5f * u, tw * 0.36f, 1.1f * u);
                HUD.Fill(bar, new Color(1f, 1f, 1f, 0.12f));
                HUD.Fill(new Rect(bar.x, bar.y, bar.width * (done ? 1f : (float)have / a.Target), bar.height), done ? new Color(1f, 0.8f, 0.3f) : new Color(0.55f, 0.4f, 0.95f));
                var ps = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(small.fontSize * 0.85f) };
                ps.normal.textColor = Color.white;
                HUD.Shadowed(bar, done ? "UNLOCKED" : $"{have} / {a.Target}", ps);
            }
        }

        // ---------------- drawing (called from HUD.OnGUI for every event, so clicks work) ----------------

        public void DrawGUI(float W, float H)
        {
            HUD.EnsureStyles();
            float u = H / 60f;
            var e = Event.current;
            bool repaint = e.type == EventType.Repaint;

            // Key capture for rebinding: any key, or a mouse button pressed after the click that started listening.
            if (Listening.HasValue)
            {
                // (not in the frame listening started: that Enter / click is the one that chose the row)
                if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None && Time.frameCount > listenFrame)
                {
                    if (e.keyCode == KeyCode.Escape) { Listening = null; Sfx.Play("fizzle", 0.4f, 1.3f); }
                    else Bind(e.keyCode);
                    e.Use();
                }
                else if (e.type == EventType.MouseDown && Time.frameCount > listenFrame + 1 && e.button <= 6)
                {
                    Bind(KeyCode.Mouse0 + e.button);
                    e.Use();
                }
                else if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) // shifts send no KeyDown event
                    Bind(Input.GetKeyDown(KeyCode.LeftShift) ? KeyCode.LeftShift : KeyCode.RightShift);
            }

            bool confirm = Current == Page.Confirm;
            bool seedPage = Current == Page.Seed;
            bool addressPage = Current == Page.Direct;
            bool pausePage = Current == Page.Pause;
            bool main = Current == Page.Main || confirm || pausePage;

            // Typing the seed (KeyDown events carry the characters).
            if (seedPage && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace) { if (SeedText.Length > 0) SeedText = SeedText.Substring(0, SeedText.Length - 1); e.Use(); }
                else if (e.character != '\0' && e.character != '\n' && e.character != '\r' && e.character != '\t') { SetSeedText(SeedText + e.character); e.Use(); }
                else if (e.keyCode == KeyCode.V && (e.control || e.command)) { SetSeedText(SeedText + GUIUtility.systemCopyBuffer); e.Use(); }
            }
            if (addressPage && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace) { if (AddressText.Length > 0) AddressText = AddressText.Substring(0, AddressText.Length - 1); e.Use(); }
                else if (e.keyCode == KeyCode.V && (e.control || e.command)) { SetAddressText(AddressText + GUIUtility.systemCopyBuffer); e.Use(); }
                else if (e.character != '\0' && e.character != '\n' && e.character != '\r' && e.character != '\t') { SetAddressText(AddressText + e.character); e.Use(); }
            }
            if (repaint)
            {
                HUD.Fill(new Rect(0, 0, W, H), new Color(0.03f, 0.01f, 0.05f, pausePage ? 0.6f : main ? 0.7f : 0.82f));
                var title = new GUIStyle(HUD.Title);
                title.normal.textColor = new Color(1f, 0.85f, 0.45f);
                if (pausePage)
                {
                    title.fontSize = Mathf.RoundToInt(title.fontSize * 0.55f);
                    bool focus = GameState.Instance && GameState.Instance.FocusPaused;
                    HUD.Shadowed(new Rect(0, H * 0.04f, W, H * 0.09f), focus ? "PAUSED (window lost focus)" : "PAUSED", title);
                }
                else if (confirm)
                {
                    title.fontSize = Mathf.RoundToInt(title.fontSize * 0.55f);
                    HUD.Shadowed(new Rect(0, H * 0.1f, W, H * 0.09f), "START A NEW RUN?", title);
                    HUD.Shadowed(new Rect(0, H * 0.2f, W, 3 * u), "This deletes your saved run" + (SaveSystem.Describe() is string d0 ? $" ({d0})" : "") + ".", HUD.Label.Center());
                }
                else if (main)
                {
                    HUD.Shadowed(new Rect(0, H * 0.08f, W, H * 0.15f), "SPELLCASTER", title);
                    HUD.Shadowed(new Rect(0, H * 0.22f, W, 3 * u), "a rune-casting roguelike", HUD.Label.Center());
                    if (SaveSystem.LastError != null)
                    {
                        var err = new GUIStyle(HUD.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                        err.normal.textColor = new Color(1f, 0.6f, 0.45f);
                        HUD.Shadowed(new Rect(W * 0.15f, H * 0.255f, W * 0.7f, 2.6f * u), SaveSystem.LastError, err);
                    }
                    else if (mainItems.Count > 0 && mainItems[0].Label == "Continue" && SaveSystem.Describe() is string d1)
                        HUD.Shadowed(new Rect(0, H * 0.26f, W, 2 * u), "Saved run: " + d1, HUD.Small.Center());
                }
                else
                {
                    title.fontSize = Mathf.RoundToInt(title.fontSize * 0.55f);
                    var nsh = NetSession.Instance;
                    string head = Current switch
                    {
                        Page.Options => "OPTIONS", Page.Controls => "CONTROLS", Page.Seed => "SEEDED RUN",
                        Page.Multiplayer => "MULTIPLAYER", Page.HostSetup => "HOST A GAME", Page.Browse => "JOIN A GAME",
                        Page.Lobby => $"LOBBY  {nsh.Roster.Count} / {nsh.MaxPlayers}", Page.Direct => "DIRECT CONNECT (DEBUG)",
                        _ => $"UNLOCKS  {Progress.UnlockedCount} / {Progress.All.Count}",
                    };
                    HUD.Shadowed(new Rect(0, H * 0.03f, W, H * 0.09f), head, title);
                    if (IsNetPage(Current)) DrawNetStatus(W, H, u);
                }
                if (Current == Page.Unlocks) DrawUnlocks(W, H, u);
            }

            var items = Items;
            bool unlocks = Current == Page.Unlocks;
            float rowH = main || unlocks ? 4.6f * u : (Current == Page.Options ? 3.5f * u : 3.1f * u);
            float w = pausePage ? Mathf.Min(22f * u, W * 0.22f) : main || unlocks ? 26f * u : 48f * u;
            float y0 = pausePage ? H * 0.3f : main ? H * 0.31f : unlocks ? H * 0.86f : H * 0.14f;
            float x0 = pausePage ? W * 0.04f : (W - w) / 2; // paused: a column on the left, the run's items stay readable
            // Long pages (Options, Controls) shrink their rows to stay above the hint line.
            if (!main && !unlocks) rowH = Mathf.Min(rowH, (H - 4.5f * u - y0) / Mathf.Max(1, items.Count));
            Vector2 mouse = e.mousePosition;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var row = new Rect(x0, y0 + i * rowH, w, rowH - 0.5f * u);
                bool hover = row.Contains(mouse);
                if (hover && repaint && mouse != lastMouse && Selected != i && !Listening.HasValue) { Selected = i; Tick(); }

                Rect left = default, right = default, slot0 = default, slot1 = default;
                if (it.Value != null && !it.Field && !it.Plain)
                {
                    right = new Rect(row.xMax - 4f * u, row.y, 4f * u, row.height);
                    left = new Rect(right.x - 16f * u, row.y, 4f * u, row.height);
                }
                if (it.Binding.HasValue)
                {
                    slot1 = new Rect(row.xMax - 11.5f * u, row.y + 0.25f * u, 11f * u, row.height - 0.5f * u);
                    slot0 = new Rect(slot1.x - 11.5f * u, slot1.y, 11f * u, slot1.height);
                }

                if (e.type == EventType.MouseDown && e.button == 0 && hover && !Listening.HasValue)
                {
                    Selected = i;
                    if (it.Change != null && left.Contains(mouse)) { it.Change(-1); Tick(); }
                    else if (it.Change != null && right.Contains(mouse)) { it.Change(1); Tick(); }
                    else if (it.Binding.HasValue && slot1.Contains(mouse)) Listen(it.Binding.Value, 1);
                    else if (it.Binding.HasValue) Listen(it.Binding.Value, 0);
                    else Choose(i);
                    e.Use();
                }

                if (!repaint) continue;
                bool sel = Selected == i;
                HUD.Fill(row, sel ? new Color(0.55f, 0.35f, 0.9f, 0.45f) : new Color(0f, 0f, 0f, 0.45f));
                if (sel) HUD.Frame(row, new Color(1f, 0.85f, 0.45f, 0.9f), Mathf.Max(2f, 0.15f * u));
                bool leftAligned = it.Value != null || it.Binding.HasValue;
                var st = new GUIStyle(HUD.Label)
                {
                    alignment = leftAligned ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(HUD.Label.fontSize * (main ? 1.15f : 0.95f)),
                };
                st.normal.textColor = sel ? Color.white : new Color(0.8f, 0.8f, 0.85f);
                float labelX = row.x + 1.5f * u;
                if (it.Icon != null)
                {
                    var icon = it.Icon();
                    var ir = new Rect(row.x + 0.4f * u, row.y + 0.3f * u, row.height - 0.6f * u, row.height - 0.6f * u);
                    if (icon) GUI.DrawTextureWithTexCoords(ir, icon, new Rect(0f, 1f, 1f, -1f)); // Steam rows come top-down
                    else HUD.Fill(ir, new Color(1f, 1f, 1f, 0.1f));
                    labelX = ir.xMax + 0.8f * u;
                }
                HUD.Shadowed(leftAligned ? new Rect(labelX, row.y, row.xMax - labelX, row.height) : row, it.Label, st);

                var vs = new GUIStyle(st) { alignment = TextAnchor.MiddleCenter };
                vs.normal.textColor = new Color(1f, 0.85f, 0.45f);
                if (it.Field)
                {
                    var box = new Rect(row.x + 12f * u, row.y + 0.3f * u, row.width - 13f * u, row.height - 0.6f * u);
                    HUD.Fill(box, new Color(0f, 0f, 0f, 0.5f));
                    HUD.Frame(box, new Color(1f, 0.85f, 0.45f, sel ? 0.9f : 0.35f), Mathf.Max(1f, 0.1f * u));
                    var fs = new GUIStyle(vs) { alignment = TextAnchor.MiddleLeft };
                    if (seedPage && SeedText.Length == 0) fs.normal.textColor = new Color(0.6f, 0.6f, 0.65f);
                    HUD.Shadowed(new Rect(box.x + u, box.y, box.width - u, box.height), it.Value(), fs);
                }
                else if (it.Plain)
                {
                    var ps = new GUIStyle(vs) { alignment = TextAnchor.MiddleRight };
                    HUD.Shadowed(new Rect(row.x, row.y, row.width - 1.5f * u, row.height), it.Value(), ps);
                }
                else if (it.Value != null)
                {
                    HUD.Shadowed(left, "<", vs);
                    HUD.Shadowed(new Rect(left.xMax, row.y, right.x - left.xMax, row.height), it.Value(), vs);
                    HUD.Shadowed(right, ">", vs);
                }
                if (it.Binding.HasValue)
                {
                    for (int s = 0; s < 2; s++)
                    {
                        var r = s == 0 ? slot0 : slot1;
                        bool waiting = Listening.HasValue && Listening.Value.action == it.Binding.Value && Listening.Value.slot == s;
                        HUD.Fill(r, waiting ? new Color(1f, 0.85f, 0.45f, 0.35f) : new Color(1f, 1f, 1f, 0.08f));
                        HUD.Frame(r, new Color(1f, 1f, 1f, 0.25f), 1f);
                        HUD.Shadowed(r, waiting ? "press a key..." : Bindings.KeyName(Bindings.Get(it.Binding.Value, s)), vs);
                    }
                }
            }

            if (repaint)
            {
                string hint = Current switch
                {
                    Page.Main => "Mouse or W/S + Enter",
                    Page.Pause => "Esc: resume   W/S + Enter or click   Q: " + (QuitSaves ? "save & quit" : "quit") +
                        (QuitSaves && Dungeon.Instance && Dungeon.Instance.InCombat
                            ? "   (in a fight: you keep what you have, the fight starts over from the last cleared room)" : ""),
                    Page.Confirm => "Esc: back",
                    Page.Multiplayer => "Online co-op for 2-6 players through Steam.   Esc: back",
                    Page.HostSetup => "A/D or click < > to change   Esc: back",
                    Page.Browse => "Friends' games and public lobbies. Friends can also invite you from Steam.   Esc: back",
                    Page.Lobby => NetSession.Instance.IsHost ? "Click a player to remove them. The run starts when everyone is ready.   Esc: close lobby"
                                                             : "Get ready; the host starts the run.   Esc: leave",
                    Page.Direct => "Debug: connect copies of the game by address (127.0.0.1 on this PC). No Steam needed.   Esc: back",
                    Page.Seed => "Type a seed (a number or any word); the same seed always builds the same dungeon, shops and enemy waves.   Ctrl+V: paste   Esc: back",
                    Page.Unlocks => "Reach a goal to add its spell or item to future runs. Your stats are never upgraded.",
                    Page.Options => "A/D or click < > to change   Esc: back   (rune size: how far the mouse moves per stroke)",
                    _ => Listening.HasValue
                        ? "Press a key or mouse button to bind it   Esc: cancel"
                        : "Click a key to change it (left: main, right: alternative)   Backspace: clear alternative   Esc: back",
                };
                HUD.Shadowed(new Rect(0, H - 3.5f * u, W, 2 * u), hint, HUD.Small.Center());
                HUD.Shadowed(new Rect(0, H - 3.5f * u, W - u, 2 * u), Application.version, HUD.Small.Right());
                lastMouse = mouse;
            }
        }
    }
}
