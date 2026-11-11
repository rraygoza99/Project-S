using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Title screen menu: Start game, Options, Exit game. Mouse (hover + click) and keyboard
    /// (W/S or arrows to move, Enter/Space to choose, A/D or arrows to change a value, Esc to go back).
    /// Drawn by the HUD while the game is on the title phase, over the start room.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public static MainMenu Instance { get; private set; }

        public enum Page { Main, Options }
        public Page Current { get; private set; } = Page.Main;
        public int Selected { get; private set; }

        class Item
        {
            public string Label;
            public Func<string> Value;          // options show a value with < > arrows
            public Action Activate;
            public Action<int> Change;          // -1 / +1
        }

        List<Item> mainItems, optionItems;
        Vector2 lastMouse;
        List<Item> Items => Current == Page.Main ? mainItems : optionItems;

        void Awake()
        {
            Instance = this;
            mainItems = new List<Item>
            {
                new Item { Label = "Start game", Activate = StartGame },
                new Item { Label = "Options", Activate = () => Open(Page.Options) },
                new Item { Label = "Exit game", Activate = ExitGame },
            };
            optionItems = new List<Item>
            {
                new Item { Label = "Mouse sensitivity", Value = () => Settings.MouseSensitivity.ToString("0.0"),
                    Change = d => Settings.SetMouseSensitivity(Settings.MouseSensitivity + 0.2f * d) },
                new Item { Label = "Rune drawing size", Value = () => Settings.RuneStroke.ToString("0.0"),
                    Change = d => Settings.SetRuneStroke(Settings.RuneStroke + 0.5f * d) },
                new Item { Label = "Volume", Value = () => Mathf.RoundToInt(Settings.Volume * 100f) + "%",
                    Change = d => Settings.SetVolume(Settings.Volume + 0.05f * d) },
                new Item { Label = "Fullscreen", Value = () => Settings.Fullscreen ? "On" : "Off",
                    Change = d => Settings.SetFullscreen(!Settings.Fullscreen) },
                new Item { Label = "Back", Activate = () => Open(Page.Main) },
            };
            foreach (var it in optionItems)
                if (it.Change != null && it.Activate == null) { var ch = it.Change; it.Activate = () => ch(1); }
        }

        public bool Visible => GameState.Instance && GameState.Instance.Phase == Phase.Title;

        void Update()
        {
            if (!Visible) return;
            if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) Move(-1);
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) Move(1);
            var item = Items[Selected];
            if (item.Change != null)
            {
                if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) { item.Change(-1); Tick(); }
                if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) { item.Change(1); Tick(); }
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) Choose(Selected);
            if (Input.GetKeyDown(KeyCode.Escape) && Current == Page.Options) Open(Page.Main);
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
            Sfx.Play("pickup", 0.5f);
        }

        public void StartGame()
        {
            Sfx.Play("cast", 0.6f);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            GameState.Instance.Begin();
        }

        public static void ExitGame()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // ---------------- drawing (called from HUD.OnGUI for every event, so clicks work) ----------------

        public void DrawGUI(float W, float H)
        {
            HUD.EnsureStyles();
            float u = H / 60f;
            var e = Event.current;
            bool repaint = e.type == EventType.Repaint;

            if (repaint)
            {
                HUD.Fill(new Rect(0, 0, W, H), new Color(0.03f, 0.01f, 0.05f, 0.7f));
                var title = new GUIStyle(HUD.Title);
                title.normal.textColor = new Color(1f, 0.85f, 0.45f);
                HUD.Shadowed(new Rect(0, H * 0.1f, W, H * 0.15f), "SPELLCASTER", title);
                HUD.Shadowed(new Rect(0, H * 0.25f, W, 3 * u), Current == Page.Main ? "a rune-casting roguelike" : "OPTIONS", HUD.Label.Center());
            }

            var items = Items;
            float rowH = 4.6f * u, w = Current == Page.Main ? 26f * u : 44f * u;
            float y0 = H * 0.38f;
            Vector2 mouse = e.mousePosition;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var row = new Rect((W - w) / 2, y0 + i * rowH, w, rowH - 0.6f * u);
                bool hover = row.Contains(mouse);
                if (hover && repaint && mouse != lastMouse && Selected != i) { Selected = i; Tick(); }

                Rect left = default, right = default;
                if (it.Value != null)
                {
                    right = new Rect(row.xMax - 4f * u, row.y, 4f * u, row.height);
                    left = new Rect(right.x - 11f * u, row.y, 4f * u, row.height);
                }

                if (e.type == EventType.MouseDown && e.button == 0 && hover)
                {
                    if (it.Change != null && left.Contains(mouse)) { Selected = i; it.Change(-1); Tick(); }
                    else if (it.Change != null && right.Contains(mouse)) { Selected = i; it.Change(1); Tick(); }
                    else Choose(i);
                    e.Use();
                }

                if (!repaint) continue;
                bool sel = Selected == i;
                HUD.Fill(row, sel ? new Color(0.55f, 0.35f, 0.9f, 0.45f) : new Color(0f, 0f, 0f, 0.45f));
                if (sel) HUD.Frame(row, new Color(1f, 0.85f, 0.45f, 0.9f), Mathf.Max(2f, 0.15f * u));
                var st = new GUIStyle(HUD.Label) { alignment = it.Value != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(HUD.Label.fontSize * 1.15f) };
                st.normal.textColor = sel ? Color.white : new Color(0.8f, 0.8f, 0.85f);
                HUD.Shadowed(it.Value != null ? new Rect(row.x + 1.5f * u, row.y, row.width, row.height) : row, it.Label, st);
                if (it.Value != null)
                {
                    var vs = new GUIStyle(st) { alignment = TextAnchor.MiddleCenter };
                    vs.normal.textColor = new Color(1f, 0.85f, 0.45f);
                    HUD.Shadowed(left, "<", vs);
                    HUD.Shadowed(new Rect(left.xMax, row.y, right.x - left.xMax, row.height), it.Value(), vs);
                    HUD.Shadowed(right, ">", vs);
                }
            }

            if (repaint) lastMouse = mouse;
            if (repaint)
            {
                string hint = Current == Page.Main
                    ? "Mouse or W/S + Enter"
                    : "A/D or click < > to change   Esc: back   (rune size: how far the mouse moves per stroke)";
                HUD.Shadowed(new Rect(0, H - 4 * u, W, 2 * u), hint, HUD.Small.Center());
                HUD.Shadowed(new Rect(0, H - 4 * u, W - u, 2 * u), Application.version, HUD.Small.Right());
            }
        }
    }
}
