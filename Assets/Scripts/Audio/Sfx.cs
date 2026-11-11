using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Sound effects. Spell/UI feedback is synthesised in code (crisp and consistent, especially the
    /// arrow ticks). Creature sounds come from Resources/MonsterSounds (Monster Sounds Pack) when imported:
    /// files named "imp_hit_1.wav", "imp_hit_2.wav"... form the random group "imp_hit".
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;
        static AudioSource[] pool;
        static int next;
        static Dictionary<string, AudioClip> clips;
        static Dictionary<string, List<AudioClip>> groups;
        static readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        static readonly System.Random rng = new System.Random(7);

        public static void Init(GameObject host)
        {
            pool = new AudioSource[16];
            for (int i = 0; i < pool.Length; i++)
            {
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.ignoreListenerPause = true;
                pool[i] = s;
            }
            if (clips == null) BuildClips();
            if (groups == null) LoadExternal();
        }

        public static bool Has(string name) => (groups != null && groups.ContainsKey(name)) || (clips != null && clips.ContainsKey(name));

        public static void Play(string name, float volume = 1f, float pitch = 1f, string fallback = null)
        {
            if (pool == null) return;
            AudioClip clip = null;
            if (groups != null && groups.TryGetValue(name, out var g)) clip = g[rng.Next(g.Count)];
            else if (clips != null && !clips.TryGetValue(name, out clip) && fallback != null) clips.TryGetValue(fallback, out clip);
            if (!clip) return;

            // Don't let a crowd of enemies stack the same sound dozens of times per frame.
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(name, out var last) && now - last < 0.04f) return;
            lastPlayed[name] = now;

            var s = pool[next];
            next = (next + 1) % pool.Length;
            if (!s) return;
            s.Stop();
            s.clip = clip;
            s.volume = volume;
            s.pitch = pitch;
            s.Play();
        }

        static void LoadExternal()
        {
            groups = new Dictionary<string, List<AudioClip>>();
            foreach (var c in Resources.LoadAll<AudioClip>("MonsterSounds"))
            {
                string key = Regex.Replace(c.name, @"_\d+$", "");
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<AudioClip>();
                list.Add(c);
            }
        }

        delegate float Gen(float t, float d);

        static void BuildClips()
        {
            clips = new Dictionary<string, AudioClip>();
            // --- spell input feedback (the heart of the mechanic) ---
            // arrow tick: short bright square blip with a click transient; pitched per direction by SpellSystem
            Add("tick", 0.05f, (t, d) => Sq(1500 * t) * 0.3f * Env(t, d, 3) + Noise() * 0.35f * Env(t, 0.004f, 1) + Sin(3000 * t) * 0.15f * Env(t, d, 4));
            // spell readied: bright rising three-note chime
            Add("ready", 0.42f, (t, d) =>
            {
                float n1 = Sin(880 * t) * Env(t, d, 1.5f);
                float n2 = t > 0.07f ? Sin(1320 * (t - 0.07f)) * Env(t - 0.07f, d - 0.07f, 1.5f) : 0f;
                float n3 = t > 0.14f ? Sin(1760 * (t - 0.14f)) * Env(t - 0.14f, d - 0.14f, 1.5f) : 0f;
                return (n1 + n2 + n3) * 0.22f + Sq(1760 * t) * 0.03f * Env(t, d, 3);
            });
            // fizzle: crackling noise + downward buzz
            Add("fizzle", 0.3f, (t, d) => Noise() * 0.35f * Env(t, d, 2) * (Sin(60 * t) > 0 ? 1f : 0.4f) + Sq(Slide(t, d, 240, 60)) * 0.18f * Env(t, d, 1));
            // cast: airy whoosh rising
            Add("cast", 0.3f, (t, d) => Noise() * 0.25f * Mathf.Sin(Mathf.PI * t / d) + Sin(Slide(t, d, 300, 900)) * 0.18f * Env(t, d, 1));
            // channel start: soft low hum blip
            Add("channel", 0.12f, (t, d) => Sin(Slide(t, d, 180, 260)) * 0.25f * Env(t, d, 1));
            // forge: anvil-ish clang
            Add("forge", 0.7f, (t, d) => (Sin(520 * t) * 0.5f + Sin(1310 * t) * 0.3f + Sin(2280 * t) * 0.2f) * 0.35f * Env(t, d, 2.5f) + Noise() * 0.2f * Env(t, 0.03f, 1));

            // --- spell impacts ---
            Add("bolt", 0.14f, (t, d) => Sq(Slide(t, d, 1100, 280)) * 0.22f * Env(t, d, 2) + Sin(Slide(t, d, 700, 200)) * 0.2f * Env(t, d, 1));
            Add("explode", 0.7f, (t, d) => Noise() * 0.6f * Env(t, d, 2.5f) + Sin(Slide(t, d, 90, 30)) * 0.5f * Env(t, d, 1.5f));
            Add("zap", 0.35f, (t, d) => (Noise() * 0.5f * (Sq(70 * t) > 0 ? 1f : 0.2f) + Saw(Slide(t, d, 900, 300)) * 0.2f) * Env(t, d, 1.5f));
            Add("freeze", 0.6f, (t, d) => (Sin(2400 * t + Sin(7 * t)) * 0.15f + Sin(3150 * t) * 0.1f + Noise() * 0.15f * Env(t, 0.15f, 1)) * Env(t, d, 1.2f));
            Add("heal", 0.6f, (t, d) => (Sin(Slide(t, d, 523, 784)) * 0.25f + Sin(Slide(t, d, 659, 1046)) * 0.15f) * Mathf.Sin(Mathf.PI * t / d));
            Add("burn", 0.25f, (t, d) => Noise() * 0.3f * Env(t, d, 1) * (0.6f + 0.4f * Sin(30 * t)));
            Add("shatter", 0.4f, (t, d) => (Noise() * 0.4f + Sin(3700 * t) * 0.2f + Sin(5100 * t) * 0.15f) * Env(t, d, 3));

            // --- generic creature fallbacks ---
            Add("hit", 0.09f, (t, d) => Noise() * 0.45f * Env(t, d, 3) + Sq(170 * t) * 0.2f * Env(t, d, 2));
            Add("death", 0.45f, (t, d) => Saw(Slide(t, d, 420, 55)) * 0.3f * Env(t, d, 1) + Noise() * 0.15f * Env(t, d, 2));
            Add("hurt", 0.2f, (t, d) => Sq(Slide(t, d, 150, 80)) * 0.3f * Env(t, d, 1) + Noise() * 0.2f * Env(t, d, 3));
            Add("claw", 0.12f, (t, d) => Noise() * 0.35f * Mathf.Sin(Mathf.PI * t / d));
            Add("enemyshot", 0.25f, (t, d) => Sin(Slide(t, d, 260, 620)) * 0.35f * Env(t, d, 1));
            Add("wave", 0.9f, (t, d) => (Saw(Slide(t, d, 110, 82)) * 0.2f + Sin(55 * t) * 0.3f) * Mathf.Sin(Mathf.PI * t / d));
        }

        static void Add(string name, float dur, Gen gen)
        {
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float attack = Mathf.Clamp01(t / 0.002f); // avoid clicks
                data[i] = Mathf.Clamp(gen(t, dur) * attack, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            clips[name] = clip;
        }

        // --- tiny synth toolkit (phase arguments are in cycles) ---
        /// <summary>Phase (cycles) of a linear frequency slide from f0 to f1 over d seconds.</summary>
        static float Slide(float t, float d, float f0, float f1) => f0 * t + (f1 - f0) * t * t / (2f * d);
        static float Sin(float phase) => Mathf.Sin(phase * 2f * Mathf.PI);
        static float Sq(float phase) => (phase - Mathf.Floor(phase)) < 0.5f ? 1f : -1f;
        static float Saw(float phase) => 2f * (phase - Mathf.Floor(phase)) - 1f;
        static float Env(float t, float d, float power) => d <= 0f ? 0f : Mathf.Pow(Mathf.Clamp01(1f - t / d), power);
        static float Noise() => (float)rng.NextDouble() * 2f - 1f;
    }
}
