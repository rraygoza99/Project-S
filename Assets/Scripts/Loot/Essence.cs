using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Every tunable value for Arcane Essence (the currency that pays for the Spell Forge), in one place.
    /// </summary>
    public static class EssenceConfig
    {
        /// <summary>Chance and amount per enemy kind. Amounts are inclusive ranges.</summary>
        public struct Drop { public float Chance; public int Min, Max; }

        public static Drop Normal = new Drop { Chance = 0.15f, Min = 1, Max = 1 };
        public static Drop Elite = new Drop { Chance = 0.75f, Min = 1, Max = 2 };
        public static Drop Boss = new Drop { Chance = 1f, Min = 3, Max = 5 };

        /// <summary>Bad-luck protection: each kill without a drop adds this to the next chance (reset on a drop).</summary>
        public static float BadLuckStep = 0.03f;

        /// <summary>Forge cost: BaseCost + forgesDone * CostIncrease, then times CostMultiplier^forgesDone.</summary>
        public static int BaseCost = 3;
        public static int CostIncrease = 2;
        /// <summary>1 = the linear formula above. Set e.g. 1.5 to make each forge cost 50% more than the last.</summary>
        public static float CostMultiplier = 1f;

        /// <summary>Forgetting a spell gives back this fraction of the Essence paid for it (rounded down).</summary>
        public static float RefundRate = 0.5f;

        /// <summary>Pickup look and feel.</summary>
        public static Color Color = new Color(0.62f, 0.45f, 1f);
        public static float MagnetRadius = 7f;

        public static int ForgeCost(int forgesDone)
        {
            float c = (BaseCost + forgesDone * CostIncrease) * Mathf.Pow(CostMultiplier, forgesDone);
            return Mathf.Max(0, Mathf.RoundToInt(c));
        }

        public static int Refund(int paid) => Mathf.Max(0, Mathf.FloorToInt(paid * RefundRate));
    }

    /// <summary>
    /// Arcane Essence drops. Only enemies drop it (not crates, chests or room rewards), each player rolls
    /// for themself (co-op: their own copy that only they can see), and the roll is seeded from the run
    /// seed and that player's roll count, so loading a save can't reroll a drop.
    /// </summary>
    public static class Essence
    {
        /// <summary>Drops this run per enemy name (debug statistics).</summary>
        public static readonly Dictionary<string, (int kills, int drops, int essence)> Stats = new Dictionary<string, (int, int, int)>();

        public static void ResetStats() => Stats.Clear();

        /// <summary>Practice tool runs: the Forge is free and nothing drops.</summary>
        public static bool Free => GameState.PracticeMode;

        /// <summary>
        /// Host / offline: an enemy died. Every player rolls once. Called once per kill (from the local run).
        /// </summary>
        public static void OnEnemyKilled(Enemy e)
        {
            if (!Dungeon.Instance || e.SelfDestructed || GameState.PracticeMode || GameState.TutorialMode) return;
            if (Net.NetSession.IsClientOnly) return; // the host rolls for everyone
            if (!e.Type.IsBoss)
            {
                if (!e.Type.DropsLoot) return; // split-off shadelings and the like
                // Boss rooms pay out through the boss: its endlessly summoned minions can't be farmed.
                var room = Dungeon.Instance.RoomAt(e.transform.position);
                if (room != null && room.Type == RoomType.Boss) return;
            }
            var table = e.Type.IsBoss ? EssenceConfig.Boss : e.IsElite ? EssenceConfig.Elite : EssenceConfig.Normal;
            Stats.TryGetValue(e.Type.Name, out var st);
            st.kills++;
            foreach (var p in Players.All)
            {
                if (!p || !p.Run) continue;
                int n = Roll(p.Run, table);
                if (n <= 0) continue;
                st.drops++; st.essence += n;
                Spawn(e.transform.position, n, p);
            }
            Stats[e.Type.Name] = st;
        }

        /// <summary>One player's roll: chance (plus bad-luck bonus), then amount. Returns 0 for no drop.</summary>
        public static int Roll(Run run, EssenceConfig.Drop table)
        {
            int seed = RunRandom.Derive(RunRandom.RunSeed, "essence", run.Owner ? run.Owner.NetKey ?? "" : "", run.EssenceRolls);
            run.EssenceRolls++;
            var rng = new System.Random(seed);
            float chance = table.Chance + run.EssenceBadLuck * EssenceConfig.BadLuckStep;
            if (rng.NextDouble() >= chance) { run.EssenceBadLuck++; return 0; }
            run.EssenceBadLuck = 0;
            return rng.Next(table.Min, table.Max + 1);
        }

        /// <summary>Drops n Essence for this player (co-op: a copy only they can see and take).</summary>
        public static void Spawn(Vector3 at, int n, Player forPlayer)
        {
            bool shared = Net.NetPlayers.Running;
            for (int i = 0; i < n; i++)
            {
                if (shared && forPlayer) Pickup.SpawnFor(Items.Essence, at + Vector3.up * 0.5f, forPlayer.NetId, pop: true);
                else Pickup.Spawn(Items.Essence, at + Vector3.up * 0.5f, pop: true, height: Items.EssenceHeight);
            }
        }

        /// <summary>A line per enemy type: kills, drops, Essence (debug).</summary>
        public static string Describe()
        {
            if (Stats.Count == 0) return "Essence drops: no kills yet this run.";
            var sb = new System.Text.StringBuilder("Essence drops this run (kills / drops / Essence):");
            foreach (var kv in Stats) sb.Append($"\n  {kv.Key}: {kv.Value.kills} / {kv.Value.drops} / {kv.Value.essence}");
            return sb.ToString();
        }
    }
}
