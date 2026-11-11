using UnityEngine;

namespace Spellcaster
{
    /// <summary>Player options, saved with PlayerPrefs (never during the self-test).</summary>
    public static class Settings
    {
        public const float MinSensitivity = 0.4f, MaxSensitivity = 5f;
        public const float MinRune = 2f, MaxRune = 9f;

        /// <summary>Mouse look speed.</summary>
        public static float MouseSensitivity { get; private set; } = 2f;
        /// <summary>How far the mouse must move to draw one rune stroke (bigger = larger gestures).</summary>
        public static float RuneStroke { get; private set; } = 4.5f;
        /// <summary>Master volume 0..1.</summary>
        public static float Volume { get; private set; } = 0.8f;
        public static bool Fullscreen { get; private set; }

        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (AutoTest.Active) { Apply(); return; } // the self-test always runs with defaults
            MouseSensitivity = PlayerPrefs.GetFloat("mouse_sensitivity", MouseSensitivity);
            RuneStroke = PlayerPrefs.GetFloat("rune_stroke", RuneStroke);
            Volume = PlayerPrefs.GetFloat("volume", Volume);
            Fullscreen = PlayerPrefs.GetInt("fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            Apply();
        }

        public static void SetMouseSensitivity(float v) { MouseSensitivity = Mathf.Clamp(Round(v), MinSensitivity, MaxSensitivity); Save(); }
        public static void SetRuneStroke(float v) { RuneStroke = Mathf.Clamp(Round(v), MinRune, MaxRune); Save(); }
        public static void SetVolume(float v) { Volume = Mathf.Clamp01(Mathf.Round(v * 20f) / 20f); Apply(); Save(); }

        public static void SetFullscreen(bool on)
        {
            Fullscreen = on;
            ApplyWindow();
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
            if (Fullscreen == (Screen.fullScreenMode != FullScreenMode.Windowed)) return;
            if (Fullscreen)
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
            else Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        }

        static void Save()
        {
            if (AutoTest.Active) return;
            PlayerPrefs.SetFloat("mouse_sensitivity", MouseSensitivity);
            PlayerPrefs.SetFloat("rune_stroke", RuneStroke);
            PlayerPrefs.SetFloat("volume", Volume);
            PlayerPrefs.SetInt("fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Test hook: back to defaults without touching saved prefs.</summary>
        public static void ResetForTest()
        {
            MouseSensitivity = 2f;
            RuneStroke = 4.5f;
            Volume = 0.8f;
            Apply();
        }
    }
}
