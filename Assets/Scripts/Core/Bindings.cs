using System;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>Gameplay actions the player can rebind (Options > Controls).</summary>
    public enum GameAction { MoveForward, MoveBack, MoveLeft, MoveRight, Channel, Cast, Item1, Item2, Item3, Interact, Map, Forge }

    /// <summary>
    /// Rebindable controls: each action has a primary and a secondary key (mouse buttons count as keys).
    /// Saved with PlayerPrefs (never during the self-test). Menus, pause (Esc) and R / Q stay fixed.
    /// </summary>
    public static class Bindings
    {
        public static readonly GameAction[] All = (GameAction[])Enum.GetValues(typeof(GameAction));
        static readonly KeyCode[,] keys = new KeyCode[All.Length, 2];

        static readonly (KeyCode a, KeyCode b)[] Defaults =
        {
            (KeyCode.W, KeyCode.UpArrow),         // MoveForward
            (KeyCode.S, KeyCode.DownArrow),       // MoveBack
            (KeyCode.A, KeyCode.LeftArrow),       // MoveLeft
            (KeyCode.D, KeyCode.RightArrow),      // MoveRight
            (KeyCode.Mouse1, KeyCode.LeftControl),// Channel (draw a rune)
            (KeyCode.Mouse0, KeyCode.None),       // Cast / magic bolt
            (KeyCode.Alpha1, KeyCode.Keypad1),    // Item1
            (KeyCode.Alpha2, KeyCode.Keypad2),    // Item2
            (KeyCode.Alpha3, KeyCode.Keypad3),    // Item3
            (KeyCode.E, KeyCode.None),            // Interact
            (KeyCode.M, KeyCode.None),            // Map
            (KeyCode.Tab, KeyCode.None),          // Forge
        };

        static Bindings() => ResetDefaults(save: false);

        public static KeyCode Get(GameAction a, int slot) => keys[(int)a, slot];

        public static bool Held(GameAction a) => Key(a, 0, Input.GetKey) || Key(a, 1, Input.GetKey);
        public static bool Pressed(GameAction a) => Key(a, 0, Input.GetKeyDown) || Key(a, 1, Input.GetKeyDown);

        static bool Key(GameAction a, int slot, Func<KeyCode, bool> test)
        {
            var k = keys[(int)a, slot];
            return k != KeyCode.None && test(k);
        }

        /// <summary>Binds a key; if another action used it, that action gets this slot's old key (swap).</summary>
        public static void Set(GameAction a, int slot, KeyCode key)
        {
            var old = keys[(int)a, slot];
            if (key != KeyCode.None)
                for (int i = 0; i < All.Length; i++)
                for (int s = 0; s < 2; s++)
                    if ((i != (int)a || s != slot) && keys[i, s] == key) keys[i, s] = old;
            keys[(int)a, slot] = key;
            Save();
        }

        public static void ResetDefaults(bool save = true)
        {
            for (int i = 0; i < All.Length; i++)
            {
                keys[i, 0] = Defaults[i].a;
                keys[i, 1] = Defaults[i].b;
            }
            if (save) Save();
        }

        public static void Load()
        {
            if (AutoTest.Active) return;
            for (int i = 0; i < All.Length; i++)
            for (int s = 0; s < 2; s++)
            {
                string pref = $"bind_{All[i]}_{s}";
                if (PlayerPrefs.HasKey(pref)) keys[i, s] = (KeyCode)PlayerPrefs.GetInt(pref);
            }
        }

        static void Save()
        {
            if (AutoTest.Active) return;
            for (int i = 0; i < All.Length; i++)
            for (int s = 0; s < 2; s++)
                PlayerPrefs.SetInt($"bind_{All[i]}_{s}", (int)keys[i, s]);
            PlayerPrefs.Save();
        }

        public static string Label(GameAction a) => a switch
        {
            GameAction.MoveForward => "Move forward",
            GameAction.MoveBack => "Move back",
            GameAction.MoveLeft => "Move left",
            GameAction.MoveRight => "Move right",
            GameAction.Channel => "Draw a rune (hold)",
            GameAction.Cast => "Cast / magic bolt",
            GameAction.Item1 => "Use item 1",
            GameAction.Item2 => "Use item 2",
            GameAction.Item3 => "Use item 3",
            GameAction.Interact => "Take / buy",
            GameAction.Map => "Map (hold)",
            _ => "Spell Forge",
        };

        /// <summary>Full key name for menus ("Right Mouse", "Left Ctrl").</summary>
        public static string KeyName(KeyCode k) => k switch
        {
            KeyCode.None => "-",
            KeyCode.Mouse0 => "Left Mouse",
            KeyCode.Mouse1 => "Right Mouse",
            KeyCode.Mouse2 => "Middle Mouse",
            KeyCode.LeftControl => "Left Ctrl",
            KeyCode.RightControl => "Right Ctrl",
            KeyCode.LeftShift => "Left Shift",
            KeyCode.RightShift => "Right Shift",
            KeyCode.LeftAlt => "Left Alt",
            KeyCode.UpArrow => "Up",
            KeyCode.DownArrow => "Down",
            KeyCode.LeftArrow => "Left",
            KeyCode.RightArrow => "Right",
            _ when k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9 => ((int)k - (int)KeyCode.Alpha0).ToString(),
            _ when k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9 => "Num " + ((int)k - (int)KeyCode.Keypad0),
            _ when k >= KeyCode.Mouse3 && k <= KeyCode.Mouse6 => "Mouse " + ((int)k - (int)KeyCode.Mouse0 + 1),
            _ => k.ToString(),
        };

        /// <summary>Short name of the primary key for on-screen hints ("RMB", "LMB", "1").</summary>
        public static string Short(GameAction a)
        {
            var k = keys[(int)a, 0];
            return k switch
            {
                KeyCode.Mouse0 => "LMB",
                KeyCode.Mouse1 => "RMB",
                KeyCode.Mouse2 => "MMB",
                KeyCode.LeftControl => "Ctrl",
                _ => KeyName(k),
            };
        }
    }
}
