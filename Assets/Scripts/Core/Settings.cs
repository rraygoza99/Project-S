using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>Player options, saved with PlayerPrefs (never during the self-test). Key bindings live in <see cref="Bindings"/>.</summary>
    public static class Settings
    {
        public const float MinSensitivity = 0.4f, MaxSensitivity = 5f;
        public const float MinRune = 2f, MaxRune = 9f;
        public const float MinFov = 60f, MaxFov = 110f;

        const float DefSensitivity = 2f, DefRune = 4.5f, DefVolume = 0.8f, DefFov = 75f;
        const int WindowW = 1280, WindowH = 720;

        /// <summary>Mouse look speed.</summary>
        public static float MouseSensitivity { get; private set; } = DefSensitivity;
        public static bool InvertY { get; private set; }
        /// <summary>How far the mouse must move to draw one rune stroke (bigger = larger gestures).</summary>
        public static float RuneStroke { get; private set; } = DefRune;
        public static float FieldOfView { get; private set; } = DefFov;
        /// <summary>Master volume 0..1.</summary>
        public static float Volume { get; private set; } = DefVolume;
        public static bool Fullscreen { get; private set; }
        /// <summary>Chosen resolution; 0 = automatic (1280x720 windowed, the monitor's in fullscreen).</summary>
        public static int ResolutionW { get; private set; }
        public static int ResolutionH { get; private set; }
        public static bool ShowFps { get; private set; } = true;
        /// <summary>Set after the tutorial has been finished or skipped once.</summary>
        public static bool TutorialDone { get; private set; }

        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (AutoTest.Active) { Apply(); return; } // the self-test always runs with defaults
            MouseSensitivity = PlayerPrefs.GetFloat("mouse_sensitivity", MouseSensitivity);
            InvertY = PlayerPrefs.GetInt("invert_y", 0) == 1;
            RuneStroke = PlayerPrefs.GetFloat("rune_stroke", RuneStroke);
            FieldOfView = PlayerPrefs.GetFloat("fov", FieldOfView);
            Volume = PlayerPrefs.GetFloat("volume", Volume);
            Fullscreen = PlayerPrefs.GetInt("fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            ResolutionW = PlayerPrefs.GetInt("res_w", 0);
            ResolutionH = PlayerPrefs.GetInt("res_h", 0);
            ShowFps = PlayerPrefs.GetInt("show_fps", 1) == 1;
            TutorialDone = PlayerPrefs.GetInt("tutorial_done", 0) == 1;
            Bindings.Load();
            Apply();
        }

        public static void SetMouseSensitivity(float v) { MouseSensitivity = Mathf.Clamp(Round(v), MinSensitivity, MaxSensitivity); Save(); }
        public static void SetInvertY(bool on) { InvertY = on; Save(); }
        public static void SetRuneStroke(float v) { RuneStroke = Mathf.Clamp(Round(v), MinRune, MaxRune); Save(); }
        public static void SetFieldOfView(float v) { FieldOfView = Mathf.Clamp(Mathf.Round(v), MinFov, MaxFov); Save(); }
        public static void SetVolume(float v) { Volume = Mathf.Clamp01(Mathf.Round(v * 20f) / 20f); Apply(); Save(); }
        public static void SetShowFps(bool on) { ShowFps = on; Save(); }
        public static void SetTutorialDone(bool on) { TutorialDone = on; Save(); }

        public static void SetFullscreen(bool on)
        {
            Fullscreen = on;
            ResolutionW = ResolutionH = 0; // each mode starts at its natural size
            ApplyWindow();
            Save();
        }

        /// <summary>Resolutions offered: the monitor's, at least 1024x576, without duplicates.</summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            var list = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height))
                .Where(r => r.x >= 1024 && r.y >= 576).Distinct().OrderBy(r => r.x * r.y).ToList();
            if (!list.Contains(new Vector2Int(WindowW, WindowH))) list.Insert(0, new Vector2Int(WindowW, WindowH));
            return list;
        }

        public static Vector2Int CurrentResolution
        {
            get
            {
                if (ResolutionW > 0 && ResolutionH > 0) return new Vector2Int(ResolutionW, ResolutionH);
                if (Fullscreen) { var r = Screen.currentResolution; return new Vector2Int(r.width, r.height); }
                return new Vector2Int(WindowW, WindowH);
            }
        }

        public static void CycleResolution(int dir)
        {
            var list = AvailableResolutions();
            int i = list.IndexOf(CurrentResolution);
            i = i < 0 ? 0 : (i + dir + list.Count) % list.Count;
            ResolutionW = list[i].x;
            ResolutionH = list[i].y;
            ApplyWindow();
            Save();
        }

        public static void ResetToDefaults()
        {
            MouseSensitivity = DefSensitivity;
            InvertY = false;
            RuneStroke = DefRune;
            FieldOfView = DefFov;
            Volume = DefVolume;
            ShowFps = true;
            Fullscreen = false;
            ResolutionW = ResolutionH = 0;
            Apply();
            Save();
        }

        static float Round(float v) => Mathf.Round(v * 10f) / 10f;

        static void Apply()
        {
            AudioListener.volume = Volume;
            ApplyWindow();
        }

        static void ApplyWindow()
        {
            if (Application.isEditor || AutoTest.Active) return;
            var res = CurrentResolution;
            var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.fullScreenMode == mode && Screen.width == res.x && Screen.height == res.y) return;
            Screen.SetResolution(res.x, res.y, mode);
        }

        static void Save()
        {
            if (AutoTest.Active) return;
            PlayerPrefs.SetFloat("mouse_sensitivity", MouseSensitivity);
            PlayerPrefs.SetInt("invert_y", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat("rune_stroke", RuneStroke);
            PlayerPrefs.SetFloat("fov", FieldOfView);
            PlayerPrefs.SetFloat("volume", Volume);
            PlayerPrefs.SetInt("fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt("res_w", ResolutionW);
            PlayerPrefs.SetInt("res_h", ResolutionH);
            PlayerPrefs.SetInt("show_fps", ShowFps ? 1 : 0);
            PlayerPrefs.SetInt("tutorial_done", TutorialDone ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Test hook: back to defaults without touching saved prefs.</summary>
        public static void ResetForTest()
        {
            ResetToDefaults();
            Bindings.ResetDefaults();
        }
    }
}
