using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster.Grimoire
{
    /// <summary>Which cards the basic bolt uses up.</summary>
    public enum BoltCardUse
    {
        /// <summary>A bolt only uses the Next card when it is Blank; special cards wait for a rune spell.</summary>
        BlankOnly,
        /// <summary>Every bolt uses the Next card, whatever it is.</summary>
        Any,
        /// <summary>Bolts never use cards.</summary>
        None,
    }

    /// <summary>
    /// Every tunable of the experimental Grimoire (deck-building) system. The on/off switch itself
    /// (GRIMOIRE_ENABLED) is <see cref="Settings.GrimoireEnabled"/>: Options > "Grimoire cards", or F4 in dev mode.
    /// Static fields rather than constants so tests and the debug panel can change them at runtime.
    /// </summary>
    public static class GrimoireConfig
    {
        /// <summary>GRIMOIRE_ENABLED default for new players (the Options value is saved).</summary>
        public const bool EnabledByDefault = false;

        // ---- deck ----
        public static int HandSize = 3;
        /// <summary>The deck every run starts with (card ids from <see cref="Cards"/>).</summary>
        public static string[] StartingDeck =
            { "blank", "blank", "blank", "blank", "blank", "amplify", "split", "echo" };
        /// <summary>Starting decks the debug panel (F4, P) can switch to.</summary>
        public static readonly List<(string name, string[] cards)> Presets = new List<(string, string[])>
        {
            ("Default", new[] { "blank", "blank", "blank", "blank", "blank", "amplify", "split", "echo" }),
            ("Every card", new[] { "blank", "amplify", "overload", "split", "pierce", "wide", "echo", "quicken", "burning", "frozen", "fumble" }),
            ("Shapes", new[] { "split", "split", "pierce", "pierce", "wide", "wide", "blank", "blank" }),
            ("Tempo", new[] { "echo", "echo", "quicken", "quicken", "overload", "overload", "blank", "blank" }),
            ("Elements", new[] { "burning", "burning", "burning", "frozen", "frozen", "frozen", "amplify", "blank" }),
            ("Cursed", new[] { "fumble", "fumble", "fumble", "blank", "blank", "amplify", "amplify", "amplify" }),
        };
        public static BoltCardUse BoltUse = BoltCardUse.BlankOnly;

        // ---- cycling (discard the Next card unused) ----
        public static KeyCode CycleKey = KeyCode.Q;
        /// <summary>Seconds you can't cast after cycling.</summary>
        public static float CycleLockout = 1f;
        /// <summary>Blank and curse cards can't be cycled away: you have to play them.</summary>
        public static bool CycleBlank = false, CycleCurses = false;

        // ---- shop item: Scroll of Erasure (removes one card, no gold cost once bought) ----
        public static bool ErasureInShops = true;
        public static int ErasurePrice = 35;

        // ---- room rewards and removal ----
        public static int RewardChoices = 3;
        public static bool RewardOnBossRooms = true;
        /// <summary>Gold to remove one card from the deck (once per reward panel).</summary>
        public static int RemoveCost = 50;
        /// <summary>The deck never shrinks below this (a full hand plus one).</summary>
        public static int MinDeckSize = 4;

        // ---- curses ----
        public static bool CursesEnabled = true;
        /// <summary>Chance that a fight room has one elite enemy (only while the Grimoire is on).</summary>
        public static float EliteChance = 0.15f;
        public static float EliteHpMult = 2f;
        /// <summary>Card an elite adds to your discard pile when it dies.</summary>
        public static string EliteCurse = "fumble";

        // ---- card numbers ----
        public static float AmplifyMult = 1.5f;
        public static float OverloadMult = 2f, OverloadCooldownMult = 2f;
        public static float SplitPower = 0.6f, SplitFanDegrees = 15f, SplitAreaScale = 0.6f, SplitAreaSpacing = 3f;
        public static float WideMult = 1.5f;
        public static float EchoDelay = 0.5f;
        public static float BurnDps = 6f, BurnSeconds = 3f;
        public static float SlowFactor = 0.5f, SlowSeconds = 2f;
        /// <summary>Radius of the pulse self spells make with Burning / Frozen / Wide.</summary>
        public static float SelfPulseRadius = 4f;
        /// <summary>Damage Pierce + Blink deals to each enemy along the path.</summary>
        public static float BlinkPierceDamage = 15f;
    }
}
