using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Spellcaster.Grimoire;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>One player's character as the host keeps it (by SteamID): HP, run, spells, deck.</summary>
    [Serializable]
    public class CharacterSave
    {
        public string key, name;
        public float hp;
        public int netId;
        public RunNet run;
        public List<string> spells = new List<string>();
        public List<string> draw = new List<string>(), hand = new List<string>(), discard = new List<string>();
    }

    /// <summary>The "coop" save section: every other player's character (only the host writes saves).</summary>
    [Serializable]
    public class CoopSave
    {
        public List<CharacterSave> players = new List<CharacterSave>();
    }

    /// <summary>
    /// Disconnects and saving in multiplayer.
    /// - A player who leaves mid-run: their character is removed from the world, and the host keeps it
    ///   (by SteamID). If they come back to the same run, they get it back where the others are.
    /// - Only the host saves. Its save has every other player's character (by SteamID) in a "coop"
    ///   section; when the host resumes the run, returning players get theirs back, new players start fresh.
    /// - The host leaving ends the session for everyone ("The host left the game").
    /// </summary>
    public static class NetSave
    {
        static readonly Dictionary<string, CharacterSave> stored = new Dictionary<string, CharacterSave>();
        static bool registered;

        public static void Register()
        {
            if (registered) return;
            registered = true;
            SaveSystem.Register(new SaveSection<CoopSave>("coop", 50, SaveCoop, LoadCoop));
        }

        public static bool HasCharacter(string key) => !string.IsNullOrEmpty(key) && stored.ContainsKey(key);
        public static CharacterSave Stored(string key) => key != null && stored.TryGetValue(key, out var c) ? c : null;
        public static void Forget() => stored.Clear();

        // ---------------- capture / restore ----------------

        public static CharacterSave Capture(Player p)
        {
            var c = new CharacterSave
            {
                key = p.NetKey, name = p.DisplayName, netId = p.NetId,
                hp = p.IsDead ? Mathf.Max(1f, p.MaxHP * NetDowned.RespawnHpFraction) : p.HP,
                run = p.Run ? NetLoot.Describe(p.Run) : null,
            };
            c.spells.AddRange(NetCombat.BookJson(p));
            var deck = NetGrimoire.DeckOf(p.NetId);
            if (deck != null)
            {
                c.draw.AddRange(deck.DrawPile.Select(x => x.Def.Id));
                c.hand.AddRange(deck.Hand.Select(x => x.Def.Id));
                c.discard.AddRange(deck.DiscardPile.Select(x => x.Def.Id));
            }
            return c;
        }

        /// <summary>Host: gives a player their kept character (run, spells, deck, HP) and forgets the copy.</summary>
        public static bool Restore(Player p)
        {
            if (!p || !stored.TryGetValue(p.NetKey ?? "", out var c)) return false;
            stored.Remove(p.NetKey);
            if (c.run != null && p.Run) p.Run.ApplyNet(c.run);
            p.StandUp(c.hp);
            if (c.spells.Count > 0) NetCombat.SetBook(p, c.spells);
            if (c.draw.Count + c.hand.Count + c.discard.Count > 0) NetGrimoire.SetDeck(p.NetId, c.draw, c.hand, c.discard);
            Pickup.RemapTaken(c.netId, p.NetId); // loot they took before stays taken
            Debug.Log($"[Spellcaster] {p.DisplayName} got their character back ({c.run?.gold ?? 0} gold, {c.spells.Count} spells)");
            return true;
        }

        /// <summary>Host: a player left mid-run; keep their character in case they come back.</summary>
        public static void Departed(Player p)
        {
            if (!p || string.IsNullOrEmpty(p.NetKey) || p.IsLocal) return;
            stored[p.NetKey] = Capture(p);
        }

        // ---------------- the save section (host only) ----------------

        static CoopSave SaveCoop()
        {
            if (!NetPlayers.Running || !NetworkServer.active) return null;
            var s = new CoopSave();
            foreach (var p in Players.All)
                if (p && !p.IsLocal && !string.IsNullOrEmpty(p.NetKey)) s.players.Add(Capture(p));
            foreach (var kv in stored)
                if (!s.players.Any(x => x.key == kv.Key)) s.players.Add(kv.Value);
            return s.players.Count > 0 ? s : null;
        }

        static void LoadCoop(CoopSave s)
        {
            stored.Clear();
            if (s == null) return;
            foreach (var c in s.players) if (!string.IsNullOrEmpty(c.key)) stored[c.key] = c;
        }

        // ---------------- rejoining and resuming (host) ----------------

        /// <summary>Host, after loading a save for a resumed co-op run: everyone gets their character and the world.</summary>
        public static void AfterResume()
        {
            foreach (var p in Players.All)
                if (p && !p.IsLocal) { Restore(p); NetCombat.SendBook(p); NetGrimoire.Send(p.NetId); }
            foreach (var conn in NetworkServer.connections.Values)
                if (!(conn is LocalConnectionToClient)) SendWorldState(conn, dynamic: false);
        }

        /// <summary>
        /// Host: a player (re)joins a run in progress. They get a character next to a teammate (their old
        /// one if they had one), the run's seed and floor, and the world as it is now.
        /// </summary>
        public static void JoinInProgress(NetworkConnectionToClient conn, RosterEntry entry, int slot)
        {
            Player anchor = Players.All.FirstOrDefault(x => x && !x.IsDead) ?? Player.Instance;
            Vector3 at = anchor ? anchor.transform.position + anchor.transform.right * 1.5f : Vector3.zero;
            var p = NetPlayers.AddRemote(entry, slot, at);
            NetCombat.AddCaster(p);
            NetGrimoire.AddPlayer(p.NetId);
            bool back = Restore(p);
            var d = Dungeon.Instance;
            conn.Send(new StartRunMsg
            {
                seed = RunRandom.RunSeed, players = NetSession.Instance.Roster.ToArray(), yourId = conn.connectionId,
                spells = SpellBook.StartingIds, grimoire = NetGrimoire.Enabled, floor = d ? d.Floor : 1, join = true,
            });
            NetCombat.SendBook(p);
            NetGrimoire.Send(p.NetId);
            SendWorldState(conn, dynamic: true, p);
            GameState.Instance?.Show($"{entry.name} {(back ? "is back" : "joined")}", 1.5f);
        }

        /// <summary>The rooms, props, portal (and with dynamic: enemies and that player's pickups) as they are now.</summary>
        public static void SendWorldState(NetworkConnectionToClient conn, bool dynamic, Player forPlayer = null)
        {
            var d = Dungeon.Instance;
            if (!d) return;
            foreach (var room in d.AllRooms)
            {
                bool locked = room.Doors.Any(x => x.Locked);
                if (room.Cleared || locked) conn.Send(new RoomGatesMsg { roomId = room.Id, locked = locked, cleared = room.Cleared });
            }
            foreach (var (kind, pos) in d.DestroyedProps()) conn.Send(new PropBrokenMsg { pos = pos, kind = (byte)kind });
            if (d.PortalOpen && d.PortalRoom != null) conn.Send(new FxMsg { kind = NetCombat.FxPortal, s = d.PortalRoom.Id });
            if (!dynamic) return;
            NetEnemies.SendAll(conn);
            NetLoot.SendAll(conn, forPlayer);
        }
    }
}
