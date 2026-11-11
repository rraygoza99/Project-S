using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>A goal that unlocks one new spell or item when its counter reaches the target.</summary>
    public class Achievement
    {
        public string Goal;      // "Clear 10 rooms"
        public string Counter;   // lifetime counter it watches
        public int Target;
        public string Reward;    // "spell:poisoncloud" or "item:thorn_robe"

        public bool IsSpell => Reward.StartsWith("spell:");
        public string RewardId => Reward.Substring(Reward.IndexOf(':') + 1);

        public string RewardName => IsSpell
            ? SpellBook.CreateBase(RewardId)?.Name ?? RewardId
            : Items.Get(RewardId)?.Name ?? RewardId;

        public Texture2D RewardIcon => IsSpell ? SpellBook.CreateBase(RewardId)?.Icon : Items.Get(RewardId)?.Icon;

        public string RewardDesc => IsSpell
            ? $"Spell, rune {Arrows.Glyphs(SpellBook.CreateBase(RewardId).Sequence)}"
            : Items.Get(RewardId)?.Desc ?? "";
    }

    /// <summary>
    /// Meta progression: lifetime counters (kills, rooms, deaths...) saved across runs, and achievements
    /// that unlock new spells and items for future runs. Unlocks only add new things to find; they never
    /// raise the wizard's stats. Nothing is tracked in the practice tool or the tutorial, nor saved
    /// during the self-test.
    /// </summary>
    public static class Progress
    {
        /// <summary>Test hook: when false, nothing counts.</summary>
        public static bool Tracking = true;

        static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
        static readonly HashSet<string> unlocked = new HashSet<string>();
        /// <summary>Achievements completed during the current run (shown on the death / victory screen).</summary>
        public static readonly List<Achievement> NewThisRun = new List<Achievement>();

        public static readonly List<Achievement> All = new List<Achievement>
        {
            // spells
            new Achievement { Goal = "Clear 10 rooms", Counter = "rooms", Target = 10, Reward = "spell:poisoncloud" },
            new Achievement { Goal = "Defeat 100 enemies", Counter = "kills", Target = 100, Reward = "spell:arcanemissiles" },
            new Achievement { Goal = "Clear a room without getting hurt", Counter = "flawless_rooms", Target = 1, Reward = "spell:blink" },
            new Achievement { Goal = "Defeat a boss", Counter = "bosses", Target = 1, Reward = "spell:quake" },
            new Achievement { Goal = "Heal 300 HP in total", Counter = "heal", Target = 300, Reward = "spell:lifedrain" },
            new Achievement { Goal = "Reach floor 3", Counter = "max_floor", Target = 3, Reward = "spell:meteor" },
            // items
            new Achievement { Goal = "Die once", Counter = "deaths", Target = 1, Reward = "item:thorn_robe" },
            new Achievement { Goal = "Die 5 times", Counter = "deaths", Target = 5, Reward = "item:phoenix" },
            new Achievement { Goal = "Defeat 50 enemies with the magic bolt", Counter = "bolt_kills", Target = 50, Reward = "item:bolt_splitter" },
            new Achievement { Goal = "Collect 300 gold in total", Counter = "gold", Target = 300, Reward = "item:gold_tooth" },
            new Achievement { Goal = "Defeat 30 enemies with poison", Counter = "poison_kills", Target = 30, Reward = "item:toxic_vial" },
            new Achievement { Goal = "Cast 150 spells", Counter = "spells_cast", Target = 150, Reward = "item:storm_crown" },
            new Achievement { Goal = "Defeat 3 enemies with one spell", Counter = "multikill", Target = 3, Reward = "item:overcharge" },
            new Achievement { Goal = "Freeze 100 enemies", Counter = "freezes", Target = 100, Reward = "item:frozen_heart" },
            new Achievement { Goal = "Forge a spell", Counter = "forged", Target = 1, Reward = "item:hex_mark" },
            new Achievement { Goal = "Use 10 items", Counter = "items_used", Target = 10, Reward = "item:smoke_bomb" },
            new Achievement { Goal = "Buy 5 items in shops", Counter = "items_bought", Target = 5, Reward = "item:time_scroll" },
            new Achievement { Goal = "Learn 8 spells from runes", Counter = "runes_learned", Target = 8, Reward = "item:rune_stone" },
        };

        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (AutoTest.Active) return; // the self-test starts with everything locked
            foreach (var c in All.Select(a => a.Counter).Distinct())
                counters[c] = PlayerPrefs.GetInt("stat_" + c, 0);
            foreach (var a in All)
                if (PlayerPrefs.GetInt("unlock_" + a.Reward, 0) == 1) unlocked.Add(a.Reward);
        }

        public static int Get(string counter) => counters.TryGetValue(counter, out var v) ? v : 0;

        public static bool IsUnlocked(string reward) => unlocked.Contains(reward);
        public static bool IsDone(Achievement a) => unlocked.Contains(a.Reward);
        public static int UnlockedCount => All.Count(IsDone);

        static bool Counting => Tracking && !GameState.PracticeMode && !GameState.TutorialMode;

        /// <summary>Adds to a lifetime counter and unlocks whatever it completes.</summary>
        public static void Add(string counter, int amount = 1)
        {
            if (!Counting || amount <= 0) return;
            Set(counter, Get(counter) + amount);
        }

        /// <summary>Raises a "best ever" counter (floor reached, kills with one spell).</summary>
        public static void Max(string counter, int value)
        {
            if (!Counting || value <= Get(counter)) return;
            Set(counter, value);
        }

        static void Set(string counter, int value)
        {
            counters[counter] = value;
            if (!AutoTest.Active) PlayerPrefs.SetInt("stat_" + counter, value);
            foreach (var a in All)
                if (a.Counter == counter && !IsDone(a) && value >= a.Target) Unlock(a);
        }

        static void Unlock(Achievement a)
        {
            unlocked.Add(a.Reward);
            NewThisRun.Add(a);
            if (!AutoTest.Active)
            {
                PlayerPrefs.SetInt("unlock_" + a.Reward, 1);
                PlayerPrefs.Save();
            }
            Sfx.Play("item", 0.8f, 1.15f);
            if (Run.Instance) Run.Instance.Toast($"Unlocked: {a.RewardName}", $"{a.Goal}. It can now appear in your runs.", new Color(1f, 0.85f, 0.4f), 4f);
        }

        /// <summary>Writes pending counters (end of a run).</summary>
        public static void Flush()
        {
            if (!AutoTest.Active) PlayerPrefs.Save();
        }

        public static void ResetForTest()
        {
            counters.Clear();
            unlocked.Clear();
            NewThisRun.Clear();
        }

        public static void UnlockAllForTest()
        {
            foreach (var a in All) unlocked.Add(a.Reward);
        }
    }
}
