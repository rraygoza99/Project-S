using Mirror;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>Host to clients: the run is over (every player is down at once).</summary>
    public struct RunOverMsg : NetworkMessage { }

    /// <summary>
    /// Downed and revive in multiplayer (single-player death is unchanged).
    /// A player at 0 HP is downed: they can't act and bleed out after <see cref="BleedOutSeconds"/>.
    /// A teammate standing next to them for <see cref="ReviveSeconds"/> gets them up. A bled-out player
    /// spectates (click to switch who you watch) and comes back when the room is cleared. The run ends
    /// only when every player is down or out at the same time. The host decides all of it.
    /// </summary>
    public static class NetDowned
    {
        /// <summary>Tunables (static so tests can shorten them).</summary>
        public static float BleedOutSeconds = 30f, ReviveSeconds = 3f, ReviveRange = 2f;
        /// <summary>HP after a revive / after coming back when the room is cleared (fraction of max HP).</summary>
        public static float ReviveHpFraction = 0.35f, RespawnHpFraction = 0.35f;

        /// <summary>Downed rules apply: a multiplayer run with more than one player.</summary>
        public static bool Applies => NetPlayers.Running && Players.All.Count > 1;

        /// <summary>Host: this player just hit 0 HP.</summary>
        public static void Down(Player p)
        {
            if (!Applies || p.Downed || p.BledOut) return;
            p.SetDowned(true, BleedOutSeconds);
            GameState.Instance?.Show($"{(string.IsNullOrEmpty(p.DisplayName) ? "A player" : p.DisplayName)} is down!", 1.5f);
        }

        /// <summary>Host, every frame: bleeding out and reviving.</summary>
        public static void Tick()
        {
            if (!NetPlayers.Running || !NetworkServer.active) return;
            float dt = Time.deltaTime;
            foreach (var p in Players.All.ToArray())
            {
                if (!p || !p.Downed) continue;
                bool helper = false;
                foreach (var q in Players.All)
                    if (q && q != p && !q.IsDead && Players.FlatSqr(q.transform.position, p.transform.position) <= ReviveRange * ReviveRange) { helper = true; break; }
                p.ReviveProgress = helper ? p.ReviveProgress + dt : Mathf.Max(0f, p.ReviveProgress - dt * 2f);
                if (p.ReviveProgress >= ReviveSeconds) { Stand(p, ReviveHpFraction); Sfx.Play("heal", 0.7f, 1.1f); continue; }
                p.BleedTimer -= dt;
                if (p.BleedTimer <= 0f) p.SetBledOut();
            }
        }

        /// <summary>Host: back on their feet with this much of their max HP (revive, room cleared, tests).</summary>
        public static void Stand(Player p, float hpFraction)
        {
            if (!p) return;
            p.StandUp(Mathf.Max(1f, p.MaxHP * hpFraction));
        }

        /// <summary>Host: the room is cleared. Bled-out players come back next to a teammate; downed ones get up.</summary>
        public static void RoomCleared()
        {
            if (!NetPlayers.Running || !NetworkServer.active) return;
            Player anchor = null;
            foreach (var p in Players.All) if (p && !p.IsDead) { anchor = p; break; }
            foreach (var p in Players.All.ToArray())
            {
                if (!p || (!p.Downed && !p.BledOut)) continue;
                bool wasOut = p.BledOut;
                Stand(p, RespawnHpFraction);
                if (wasOut && anchor) p.Teleport(anchor.transform.position + anchor.transform.right * 1.5f + Vector3.up * 0.1f, anchor.Yaw);
            }
        }

        /// <summary>Host: the run ended for everyone.</summary>
        public static void RunOver()
        {
            if (NetPlayers.Running && NetworkServer.active) NetworkServer.SendToAll(new RunOverMsg());
        }

        public static void OnRunOver(RunOverMsg m)
        {
            if (NetworkServer.active || !GameState.Instance) return;
            GameState.Instance.EndRunFromHost();
        }
    }
}
