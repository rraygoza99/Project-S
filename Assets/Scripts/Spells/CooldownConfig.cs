using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Which spells have a cooldown, read from <c>cooldowns.json</c> next to the game (Spellcaster.exe; the
    /// project folder in the editor), so cooldowns can come back without rebuilding. The default: spells
    /// that heal (Heal, Life Drain, Quickening, and any forged spell with a heal, a life drain or regen) keep
    /// their cooldown; every other spell has none. The file is written with the defaults if it's missing.
    /// The self-test ignores the file (it sets the values it needs).
    /// </summary>
    public static class CooldownConfig
    {
        [Serializable]
        public class Override
        {
            public string spell;   // spell id ("meteor", "fireball"...); a forged spell's id works too
            public float seconds;  // its cooldown (base, before items); 0 = none
        }

        [Serializable]
        class FileData
        {
            public string _help;
            public bool nonHealingCooldowns;
            public List<Override> overrides = new List<Override>();
            public float dashIFrameLockout;
            public float ccImmunitySeconds;
            public float bossCcMult;
        }

        public const string FileName = "cooldowns.json";
        const string Help = "Spell cooldowns. nonHealingCooldowns: true gives every spell its normal cooldown back. " +
            "overrides: a cooldown for one spell by id, e.g. {\"spell\": \"meteor\", \"seconds\": 14} (ids: fireball, lightning, " +
            "frostnova, heal, frostshard, thunderring, blinkdash, quickening, poisoncloud, arcanemissiles, blink, quake, " +
            "lifedrain, meteor). dashIFrameLockout: a Blink Dash only makes you untouchable if the last dash's i-frames " +
            "ended this many seconds ago. ccImmunitySeconds: after a freeze or stun ends, that enemy can't be frozen or " +
            "stunned again for this long (0 = off). bossCcMult: freeze and stun last this share on bosses.";

        /// <summary>True: every spell keeps its normal cooldown (the game before playtest 0.20).</summary>
        public static bool NonHealingCooldowns;
        /// <summary>Per-spell cooldowns (base seconds) that win over the rule above.</summary>
        public static readonly Dictionary<string, float> Overrides = new Dictionary<string, float>();
        /// <summary>Blink Dash: seconds after the last dash's i-frames end before a dash makes you untouchable again.</summary>
        public static float DashIFrameLockout = 1.5f;
        /// <summary>Seconds an enemy can't be frozen or stunned again after one ends (0 = no limit, as before).</summary>
        public static float CcImmunitySeconds = 2f;
        /// <summary>Freeze and stun duration on bosses (share of the normal duration).</summary>
        public static float BossCcMult = 0.5f;

        static bool loaded;
        public static string LoadedFrom { get; private set; }

        /// <summary>The spell restores health: a heal, a life drain, or regen (Quickening).</summary>
        public static bool Heals(SpellDefinition s)
        {
            if (s == null) return false;
            foreach (var e in s.Payload)
            {
                if (e is HealEffect || e is LifestealEffect) return true;
                if (e is QuickenEffect q && q.Regen > 0f) return true;
            }
            return false;
        }

        /// <summary>The spell's cooldown before items and buffs: an override, its own (healing), or none.</summary>
        public static float BaseCooldown(SpellDefinition s)
        {
            if (s == null) return 0f;
            Load();
            if (Overrides.TryGetValue(s.Id, out float o)) return Mathf.Max(0f, o);
            return NonHealingCooldowns || Heals(s) ? s.Cooldown : 0f;
        }

        public static bool HasCooldown(SpellDefinition s) => BaseCooldown(s) > 0f;

        /// <summary>Reads cooldowns.json (once); writes it with the defaults when it's missing.</summary>
        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (AutoTest.Active) return; // the self-test sets what it needs
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", FileName);
            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, JsonUtility.ToJson(Snapshot(), true));
                    Debug.Log("[Spellcaster] wrote default " + path);
                    return;
                }
                var d = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
                if (d == null) return;
                NonHealingCooldowns = d.nonHealingCooldowns;
                Overrides.Clear();
                if (d.overrides != null)
                    foreach (var o in d.overrides)
                        if (o != null && !string.IsNullOrWhiteSpace(o.spell)) Overrides[o.spell.Trim()] = o.seconds;
                DashIFrameLockout = Mathf.Max(0f, d.dashIFrameLockout);
                CcImmunitySeconds = Mathf.Max(0f, d.ccImmunitySeconds);
                BossCcMult = d.bossCcMult > 0f ? d.bossCcMult : 1f;
                LoadedFrom = path;
                Debug.Log($"[Spellcaster] cooldowns from {path}: non-healing cooldowns {NonHealingCooldowns}, {Overrides.Count} overrides, dash i-frame lockout {DashIFrameLockout}s, CC immunity {CcImmunitySeconds}s, boss CC x{BossCcMult}");
            }
            catch (Exception ex) { Debug.LogWarning("[Spellcaster] couldn't read " + path + ": " + ex.Message); }
        }

        static FileData Snapshot()
        {
            var d = new FileData
            {
                _help = Help, nonHealingCooldowns = NonHealingCooldowns, dashIFrameLockout = DashIFrameLockout,
                ccImmunitySeconds = CcImmunitySeconds, bossCcMult = BossCcMult,
            };
            foreach (var kv in Overrides) d.overrides.Add(new Override { spell = kv.Key, seconds = kv.Value });
            return d;
        }

        /// <summary>Test hook: the rules as they were before playtest 0.20 (every spell has its cooldown, no CC limit).</summary>
        public static void UseLegacyForTest()
        {
            loaded = true;
            NonHealingCooldowns = true;
            Overrides.Clear();
            DashIFrameLockout = 0f;
            CcImmunitySeconds = 0f;
            BossCcMult = 1f;
        }

        /// <summary>Test hook: the shipped defaults.</summary>
        public static void UseDefaultsForTest()
        {
            loaded = true;
            NonHealingCooldowns = false;
            Overrides.Clear();
            DashIFrameLockout = 1.5f;
            CcImmunitySeconds = 2f;
            BossCcMult = 0.5f;
        }
    }
}
