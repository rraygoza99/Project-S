using System;
using System.Collections.Generic;
using UnityEngine;

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

        public enum Page { Main, Options, Controls, Unlocks }
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
        }

        List<Item> mainItems, optionItems, controlItems, unlockItems;
        Vector2 lastMouse;
        int listenFrame;

        List<Item> Items => Current switch { Page.Main => mainItems, Page.Options => optionItems, Page.Unlocks => unlockItems, _ => controlItems };

        static string OnOff(bool b) => b ? "On" : "Off";

        void Awake()
        {
            Instance = this;
            mainItems = new List<Item>
            {
                new Item { Label = "Start game", Activate = StartGame },
                new Item { Label = "Tutorial", Activate = StartTutorial },
                new Item { Label = "Practice tool", Activate = StartPractice },
                new Item { Label = "Unlocks", Activate = () => Open(Page.Unlocks) },
                new Item { Label = "Options", Activate = () => Open(Page.Options) },
                new Item { Label = "Exit game", Activate = ExitGame },
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
                new Item { Label = "Controls...", Activate = () => Open(Page.Controls) },
                new Item { Label = "Reset to defaults", Activate = () => { Settings.ResetToDefaults(); Sfx.Play("pickup", 0.5f); } },
                new Item { Label = "Back", Activate = () => Open(Page.Main) },
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
            if (!Visible) return;
            if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (Listening.HasValue) return; // the next key press is captured in DrawGUI

            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) Move(-1);
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) Move(1);
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
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) Choose(Selected);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Current == Page.Controls) Open(Page.Options);
                else if (Current == Page.Options || Current == Page.Unlocks) Open(Page.Main);
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
        public void StartGame()
        {
            LeaveMenu();
            if (!Settings.TutorialDone && !AutoTest.Active) GameState.Instance.StartTutorial();
            else GameState.Instance.Begin();
        }

        /// <summary>Starts a run that opens with the tutorial.</summary>
        public void StartTutorial()
        {
            LeaveMenu();
            GameState.Instance.StartTutorial();
        }

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

            bool main = Current == Page.Main;
            if (repaint)
            {
                HUD.Fill(new Rect(0, 0, W, H), new Color(0.03f, 0.01f, 0.05f, main ? 0.7f : 0.82f));
                var title = new GUIStyle(HUD.Title);
                title.normal.textColor = new Color(1f, 0.85f, 0.45f);
                if (main)
                {
                    HUD.Shadowed(new Rect(0, H * 0.08f, W, H * 0.15f), "SPELLCASTER", title);
                    HUD.Shadowed(new Rect(0, H * 0.22f, W, 3 * u), "a rune-casting roguelike", HUD.Label.Center());
                }
                else
                {
                    title.fontSize = Mathf.RoundToInt(title.fontSize * 0.55f);
                    string head = Current switch { Page.Options => "OPTIONS", Page.Controls => "CONTROLS", _ => $"UNLOCKS  {Progress.UnlockedCount} / {Progress.All.Count}" };
                    HUD.Shadowed(new Rect(0, H * 0.03f, W, H * 0.09f), head, title);
                }
                if (Current == Page.Unlocks) DrawUnlocks(W, H, u);
            }

            var items = Items;
            bool unlocks = Current == Page.Unlocks;
            float rowH = main || unlocks ? 4.6f * u : (Current == Page.Options ? 3.5f * u : 3.1f * u);
            float w = main || unlocks ? 26f * u : 48f * u;
            float y0 = main ? H * 0.31f : unlocks ? H * 0.86f : H * 0.14f;
            Vector2 mouse = e.mousePosition;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var row = new Rect((W - w) / 2, y0 + i * rowH, w, rowH - 0.5f * u);
                bool hover = row.Contains(mouse);
                if (hover && repaint && mouse != lastMouse && Selected != i && !Listening.HasValue) { Selected = i; Tick(); }

                Rect left = default, right = default, slot0 = default, slot1 = default;
                if (it.Value != null)
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
                HUD.Shadowed(leftAligned ? new Rect(row.x + 1.5f * u, row.y, row.width, row.height) : row, it.Label, st);

                var vs = new GUIStyle(st) { alignment = TextAnchor.MiddleCenter };
                vs.normal.textColor = new Color(1f, 0.85f, 0.45f);
                if (it.Value != null)
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
