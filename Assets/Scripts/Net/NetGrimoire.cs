using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Spellcaster.Grimoire;
using UnityEngine;
using C = Spellcaster.Grimoire.GrimoireConfig;

namespace Spellcaster.Net
{
    /// <summary>Host to one client: that client's own deck (nobody else sees it).</summary>
    public struct DeckStateMsg : NetworkMessage
    {
        public string[] draw, hand, discard;
        public string toast;      // "cursed" when an elite's curse was just added
    }

    /// <summary>Client to host: throw away my Next card.</summary>
    public struct CycleMsg : NetworkMessage { }

    /// <summary>Host to one client: pick one of these cards (a room was cleared).</summary>
    public struct RewardOfferMsg : NetworkMessage
    {
        public string[] cards;
    }

    /// <summary>Client to host: take the offered card at this index, remove a card (paid), or skip.</summary>
    public struct RewardPickMsg : NetworkMessage
    {
        public int take;          // -1 = none
        public string remove;     // card id to remove, or null
        public bool skip;
    }

    /// <summary>
    /// The Grimoire in multiplayer: one deck per player, all held by the host. The host plays a client's
    /// Next card on that client's casts, handles cycling, room rewards and elite curses, and sends each
    /// client only their own deck. The host's own setting (Options > Grimoire) decides for the session.
    /// </summary>
    public static class NetGrimoire
    {
        /// <summary>The Grimoire is on for this multiplayer run (the host's choice).</summary>
        public static bool Enabled { get; private set; }
        /// <summary>A client: the deck shown here is a copy of the host's; actions go to the host.</summary>
        public static bool ClientMirror => Enabled && NetPlayers.Running && NetSession.IsClientOnly;

        class Remote
        {
            public Deck deck;
            public float lockUntil;
            public List<CardDef> offer;
            public bool removedThisOffer;
            public int played;
        }
        static readonly Dictionary<int, Remote> remotes = new Dictionary<int, Remote>();
        static bool hooked;

        /// <summary>Cards played from each client's deck by the host (tests).</summary>
        public static int PlayedFor(int id) => remotes.TryGetValue(id, out var r) ? r.played : 0;
        public static Deck DeckOf(int id) => remotes.TryGetValue(id, out var r) ? r.deck : null;

        public static void SetupRun(StartRunMsg m)
        {
            remotes.Clear();
            Enabled = m.grimoire;
            if (!hooked)
            {
                Dungeon.RoomCleared += OnRoomCleared;
                Enemy.Killed += OnKilled;
                hooked = true;
            }
            if (Enabled && pending.HasValue && ClientMirror) { var p = pending.Value; pending = null; OnDeckState(p); }
            if (!Enabled || !NetworkServer.active || m.players == null) return;
            foreach (var r in m.players)
            {
                if (r.id == m.yourId) continue; // the host's own deck is GrimoireRun's
                var deck = new Deck();
                deck.Build(C.StartingDeck);
                remotes[r.id] = new Remote { deck = deck };
                Send(r.id);
            }
        }

        public static void EndRun()
        {
            remotes.Clear();
            pending = null;
            Enabled = false;
        }

        public static void RemovePlayer(int id) => remotes.Remove(id);

        /// <summary>A player joining a run in progress gets a starting deck (their kept deck replaces it).</summary>
        public static void AddPlayer(int id)
        {
            if (!Enabled || remotes.ContainsKey(id)) return;
            var deck = new Deck();
            deck.Build(C.StartingDeck);
            remotes[id] = new Remote { deck = deck };
        }

        public static void SetDeck(int id, List<string> draw, List<string> hand, List<string> discard)
        {
            if (!Enabled) return;
            if (!remotes.TryGetValue(id, out var r)) remotes[id] = r = new Remote { deck = new Deck() };
            r.deck.Restore(draw, hand, discard);
        }

        public static void Send(int id, string toast = null)
        {
            if (!remotes.TryGetValue(id, out var r) || !NetworkServer.connections.TryGetValue(id, out var conn)) return;
            conn.Send(new DeckStateMsg
            {
                draw = r.deck.DrawPile.Select(c => c.Def.Id).ToArray(),
                hand = r.deck.Hand.Select(c => c.Def.Id).ToArray(),
                discard = r.deck.DiscardPile.Select(c => c.Def.Id).ToArray(),
                toast = toast,
            });
        }

        // ---------------- host: casts ----------------

        /// <summary>Casting is locked for this player right after a cycle.</summary>
        public static bool Locked(int id) => remotes.TryGetValue(id, out var r) && Time.time < r.lockUntil;

        /// <summary>
        /// Host: plays the client's Next card on their cast (same rules as single-player: the bolt only uses
        /// a Blank). Returns the cast request the card shaped, or null when no card applies.
        /// </summary>
        public static CastRequest Play(Player p, SpellDefinition spell)
        {
            if (!Enabled || !remotes.TryGetValue(p.NetId, out var r)) return null;
            var next = r.deck.Next;
            if (next == null) return null;
            var req = new CastRequest { Spell = spell };
            if (req.IsBolt)
            {
                if (C.BoltUse == BoltCardUse.None) return null;
                if (C.BoltUse == BoltCardUse.BlankOnly && next.Def.Id != "blank") return null;
            }
            r.deck.PlayNext();
            r.played++;
            Cards.Apply(next.Def, new CardContext { Request = req, Spell = spell, Shape = Cards.ShapeOf(spell), Player = p });
            req.Label = (spell != null ? spell.Name : "Bolt") + " + " + next.Def.Name;
            Send(p.NetId);
            return req;
        }

        /// <summary>Host: launches a spell shaped by a card (all its copies), and its echo if the card has one.</summary>
        public static void LaunchCopies(Player p, SpellDefinition spell, float power, CastRequest req, bool echo = false)
        {
            foreach (var m in req.Copies)
            {
                var mods = echo ? m.Clone() : m;
                if (echo) mods.NoLifesteal = true;
                new SpellCast(spell, p) { Power = power * mods.DamageMult, Mods = mods }.Launch();
            }
            if (!echo) req.AfterLaunch?.Invoke();
            if (!echo && req.EchoDelay > 0f && NetSession.Instance) NetSession.Instance.StartCoroutine(Echo(p, spell, power, req));
        }

        public static void StartEcho(Player p, SpellDefinition spell, float power, CastRequest req)
        {
            if (NetSession.Instance) NetSession.Instance.StartCoroutine(Echo(p, spell, power, req));
        }

        static IEnumerator Echo(Player p, SpellDefinition spell, float power, CastRequest req)
        {
            yield return new WaitForSeconds(req.EchoDelay);
            if (!p || p.IsDead) yield break;
            if (spell == null) SpellSystem.FireBoltsFrom(p, p.Run ? p.Run.Stats : new PlayerStats(), req, true);
            else LaunchCopies(p, spell, power, req, true);
            Sfx.Play("cast", 0.5f, 1.25f);
        }

        // ---------------- host: cycling, rewards, curses ----------------

        public static void OnCycle(NetworkConnectionToClient conn, CycleMsg m)
        {
            if (!Enabled || !remotes.TryGetValue(conn.connectionId, out var r) || r.deck.Next == null) return;
            var d = r.deck.Next.Def;
            if ((d.IsCurse && !C.CycleCurses) || (d.Id == "blank" && !C.CycleBlank)) { Send(conn.connectionId); return; }
            r.deck.CycleNext();
            r.lockUntil = Time.time + C.CycleLockout;
            Send(conn.connectionId);
        }

        static void OnRoomCleared(Room room)
        {
            if (!Enabled || !NetworkServer.active || !NetPlayers.Running) return;
            if (room.Type == RoomType.Boss && !C.RewardOnBossRooms) return;
            foreach (var id in remotes.Keys.ToList()) Offer(id);
        }

        /// <summary>Host: offers a client a choice of cards (also a test hook).</summary>
        public static void Offer(int id)
        {
            if (!remotes.TryGetValue(id, out var r) || !NetworkServer.connections.TryGetValue(id, out var conn)) return;
            var pool = Cards.RewardPool;
            r.offer = new List<CardDef>();
            for (int i = 0; i < C.RewardChoices && pool.Count > 0; i++)
            {
                int k = Random.Range(0, pool.Count);
                r.offer.Add(pool[k]);
                pool.RemoveAt(k);
            }
            r.removedThisOffer = false;
            conn.Send(new RewardOfferMsg { cards = r.offer.Select(c => c.Id).ToArray() });
        }

        public static void OnPick(NetworkConnectionToClient conn, RewardPickMsg m)
        {
            if (!remotes.TryGetValue(conn.connectionId, out var r) || r.offer == null) return;
            if (!string.IsNullOrEmpty(m.remove))
            {
                var def = Cards.Get(m.remove);
                var p = Players.ByNetId(conn.connectionId);
                var run = p ? p.Run : null;
                if (def != null && !r.removedThisOffer && r.deck.Count > C.MinDeckSize && run && run.Gold >= C.RemoveCost && r.deck.Remove(def))
                {
                    run.Gold -= C.RemoveCost;
                    r.removedThisOffer = true;
                }
                Send(conn.connectionId);
                return;
            }
            if (m.take >= 0 && m.take < r.offer.Count) r.deck.AddToDiscard(r.offer[m.take]);
            r.offer = null;
            Send(conn.connectionId);
        }

        static void OnKilled(Enemy e)
        {
            // An elite's curse falls on the whole party.
            if (!Enabled || !NetworkServer.active || !e.IsElite || !C.CursesEnabled) return;
            var def = Cards.Get(C.EliteCurse);
            if (def == null) return;
            foreach (var id in remotes.Keys.ToList())
            {
                remotes[id].deck.AddToDiscard(def);
                Send(id, "cursed");
            }
        }

        // ---------------- client ----------------

        public static int DecksReceived { get; private set; }

        static DeckStateMsg? pending;

        public static void OnDeckState(DeckStateMsg m)
        {
            if (!ClientMirror || !GrimoireRun.Instance) { pending = m; return; } // arrived before the run started here
            DecksReceived++;
            GrimoireRun.Instance.ApplyNetDeck(m.draw ?? new string[0], m.hand ?? new string[0], m.discard ?? new string[0]);
            if (m.toast == "cursed" && Run.Instance)
            {
                var def = Cards.Get(C.EliteCurse);
                if (def != null) Run.Instance.Toast("Cursed!", $"The elite's death put {def.Name} in your Grimoire", def.Color);
            }
        }

        public static void OnOffer(RewardOfferMsg m)
        {
            if (!ClientMirror || !GrimoireRun.Instance || m.cards == null) return;
            var defs = m.cards.Select(Cards.Get).Where(d => d != null).ToList();
            GrimoireRun.Instance.Rewards.OpenOffer(GrimoireRun.Instance, defs);
            if (autoTake && NetSession.Instance) NetSession.Instance.StartCoroutine(TakeSoon());
        }

        /// <summary>Test only (-nettestcombat): take the first offered card a moment later.</summary>
        public static bool autoTake;
        static IEnumerator TakeSoon()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (GrimoireRun.Instance && GrimoireRun.Instance.Rewards.IsOpen) GrimoireRun.Instance.Rewards.Take(0);
        }

        public static void SendCycle() => NetworkClient.Send(new CycleMsg());
        public static void SendPick(int take, string remove, bool skip) =>
            NetworkClient.Send(new RewardPickMsg { take = take, remove = remove, skip = skip });
    }
}
