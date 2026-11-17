using System;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// The run seed and the seeds derived from it. Everything the map decides (floor layouts, room
    /// furnishing, shop / treasure / shrine stock, each room's enemy wave, room-clear rewards, Grimoire
    /// offers and elite rolls) is rolled inside a seeded scope, so a resumed run produces exactly what an
    /// uninterrupted one would. Combat itself (AI, kill and crate drops) stays random.
    /// </summary>
    public static class RunRandom
    {
        /// <summary>Picked when a run's world is built; restored from the save on Continue.</summary>
        public static int RunSeed { get; set; } = NewSeed();

        /// <summary>
        /// A seed typed by the player: digits are used as the number itself, anything else (a word, a
        /// phrase) is turned into a number, ignoring case and spaces at the ends. 0 = nothing usable.
        /// </summary>
        public static int ParseSeed(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return 0;
            if (int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) && n > 0) return n;
            return Derive("seed", text.ToUpperInvariant());
        }

        /// <summary>When the run was started from a typed seed: what the player typed (shown with the number).</summary>
        public static string TypedSeed;

        /// <summary>The seed as players share it.</summary>
        public static string SeedLabel => TypedSeed != null && TypedSeed != RunSeed.ToString() ? $"{RunSeed} (\"{TypedSeed}\")" : RunSeed.ToString();

        /// <summary>Unscaled time the seed was last copied (the "Copied" note).</summary>
        public static float CopiedAt = -10f;

        public static void CopySeed()
        {
            GUIUtility.systemCopyBuffer = RunSeed.ToString();
            CopiedAt = Time.unscaledTime;
            Sfx.Play("tick", 0.6f, 1.3f);
        }

        public static int NewSeed() => Mathf.Max(1, (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF) ^ UnityEngine.Random.Range(1, int.MaxValue));

        /// <summary>A stable seed from any parts (FNV-1a over their text): the same parts always give the same seed.</summary>
        public static int Derive(params object[] parts)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var p in parts)
                {
                    foreach (char c in p?.ToString() ?? "") { h ^= c; h *= 16777619; }
                    h ^= '|';
                    h *= 16777619;
                }
                return (int)(h & 0x7FFFFFFF) | 1;
            }
        }

        public static int FloorSeed(int floor) => Derive(RunSeed, "floor", floor);

        /// <summary>A seed for one decision in one room of the current floor.</summary>
        public static int RoomSeed(int floorSeed, Room room, string what) => Derive(floorSeed, room != null ? room.Id : "none", what);

        /// <summary>Seeds UnityEngine.Random for the duration of the scope, then restores whatever state it had.</summary>
        public static Scope Use(int seed) => new Scope(seed);

        public readonly struct Scope : IDisposable
        {
            readonly UnityEngine.Random.State previous;
            public Scope(int seed)
            {
                previous = UnityEngine.Random.state;
                UnityEngine.Random.InitState(seed);
            }
            public void Dispose() => UnityEngine.Random.state = previous;
        }
    }
}
