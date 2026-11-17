using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Every player in the world (co-op groundwork). Single-player has one, the local player.
    /// Gameplay that acts on "a player" (enemy targets, damage, hazards, pickups) goes through here
    /// instead of Player.Instance, which now means "the player on this PC" (camera, HUD, input).
    /// </summary>
    public static class Players
    {
        /// <summary>All players, the local one first. Players add and remove themselves.</summary>
        public static readonly List<Player> All = new List<Player>();

        /// <summary>The player on this PC (camera, HUD, input). Same as Player.Instance.</summary>
        public static Player Local => Player.Instance;

        public static bool AnyAlive
        {
            get { foreach (var p in All) if (p && !p.IsDead) return true; return false; }
        }

        public static int AliveCount
        {
            get { int n = 0; foreach (var p in All) if (p && !p.IsDead) n++; return n; }
        }

        /// <summary>
        /// The player the room follows for now (co-op rooms are Phase 4): the local player while alive,
        /// otherwise the first living one, otherwise the local player.
        /// </summary>
        public static Player Leader
        {
            get
            {
                if (Local && !Local.IsDead) return Local;
                foreach (var p in All) if (p && !p.IsDead) return p;
                return Local;
            }
        }

        /// <summary>The nearest player to a point (flat distance), living ones only unless aliveOnly is false.</summary>
        public static Player Nearest(Vector3 pos, bool aliveOnly = true)
        {
            Player best = null;
            float bestD = float.MaxValue;
            foreach (var p in All)
            {
                if (!p || (aliveOnly && p.IsDead)) continue;
                float d = FlatSqr(p.transform.position, pos);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        public static float FlatSqr(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        internal static void Add(Player p)
        {
            if (All.Contains(p)) return;
            if (p.IsLocal) All.Insert(0, p); else All.Add(p);
        }

        internal static void Remove(Player p) => All.Remove(p);
    }
}
