using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace Spellcaster.Net
{
    // ---------------- messages ----------------

    /// <summary>Host to clients: a pickup appeared (each player gets their own copy).</summary>
    public struct PickupSpawnMsg : NetworkMessage
    {
        public int id, price, onlyFor;
        public string item, spell;   // spell: a forgotten rune's spell (JSON)
        public Vector3 pos, vel;
        public float height;
        public bool pop;
    }
    /// <summary>Client to host: I want this (walked over it, or pressed E / bought it).</summary>
    public struct PickupTakeMsg : NetworkMessage { public int id; }
    /// <summary>Host to a client: this pickup is gone for you (you took it, or everyone did).</summary>
    public struct PickupGoneMsg : NetworkMessage { public int id; }
    /// <summary>Host to a client: why you couldn't take it.</summary>
    public struct DenyMsg : NetworkMessage { public string reason; }
    /// <summary>Host to a client: your gold, items, consumables and stats.</summary>
    public struct RunStateMsg : NetworkMessage { public string json; }
    /// <summary>Client to host: use the consumable in this slot.</summary>
    public struct UseItemMsg : NetworkMessage { public int slot; }
    /// <summary>Host to a client: your spell book (JSON per spell, forged ones included).</summary>
    public struct SpellBookMsg : NetworkMessage { public string[] spells; }
    /// <summary>Client to host: fuse book[a] and book[b], or forget book[forget].</summary>
    public struct ForgeMsg : NetworkMessage { public int a, b, forget; }
    /// <summary>Host to clients: everyone goes to this floor (or the run is won).</summary>
    public struct FloorMsg : NetworkMessage { public int floor; public bool victory; }
    /// <summary>Host to a client: all your cooldowns are reset (Scroll of Renewal).</summary>
    public struct CooldownResetMsg : NetworkMessage { }

    /// <summary>What a client is told about its own run (JsonUtility-friendly).</summary>
    [System.Serializable]
    public class RunNet
    {
        public int gold, goldEarned, toastId, essence, forgesDone, essenceBadLuck, essenceRolls;
        public float haste;
        public List<string> items = new List<string>(), consumables = new List<string>();
        public PlayerStats stats;
        public string toastTitle, toastText;
        public Color toastColor;
    }

    /// <summary>
    /// Loot and progress in a multiplayer run, all decided by the host:
    /// - every pickup is instanced: each player has their own copy of every coin, heart, item and shop
    ///   stock, so nobody takes anybody's loot; it's gone for good once everyone has taken it;
    /// - each player's gold, items, consumables and stats live on the host (in that player's Run) and are
    ///   sent to them when they change;
    /// - consumables, the Forge and learning spells go through the host, on that player's own spell book;
    /// - the floor changes for everyone at once.
    /// </summary>
    public static class NetLoot
    {
        /// <summary>The host mirrors loot to other players (instanced pickups).</summary>
        public static bool Instanced => NetCombat.HostMirroring;
        static bool ClientSide => NetPlayers.Running && NetSession.IsClientOnly;
        /// <summary>A client is creating a pickup the host sent (local floor stock is the host's to send).</summary>
        public static bool SpawningProxy;

        static int nextId = 1;
        static readonly Dictionary<int, Pickup> byId = new Dictionary<int, Pickup>();
        static readonly Dictionary<int, string> lastRunJson = new Dictionary<int, string>();
        static float runTimer;
        public static int LastRunToast;

        public static void SetupRun() { byId.Clear(); lastRunJson.Clear(); }
        public static void EndRun() { byId.Clear(); lastRunJson.Clear(); }

        // ---------------- host: pickups ----------------

        public static void OnSpawned(Pickup p, Vector3 popVelocity, bool pop)
        {
            if (!Instanced) return;
            p.NetId = nextId++;
            byId[p.NetId] = p;
            NetworkServer.SendToAll(new PickupSpawnMsg
            {
                id = p.NetId, item = p.Item.Id, spell = p.Item.Spell != null ? SpellCodec.ToJson(p.Item.Spell) : null,
                pos = p.transform.position, vel = popVelocity, height = p.Height, price = p.Price, pop = pop, onlyFor = p.OnlyFor,
            });
        }

        /// <summary>Host, every frame for an auto pickup (coin, heart): give it to each player in reach, once.</summary>
        public static void HostAuto(Pickup p)
        {
            if (p.Age < 0.4f) return;
            var d = Dungeon.Instance;
            var room = d ? d.RoomAt(p.transform.position) : null;
            bool magnetic = p.Item.Kind == ItemKind.Coin || p.Item.Kind == ItemKind.Essence;
            bool vacuum = magnetic && room != null && room.HasEnemies && room.Cleared;
            foreach (var pl in Players.All.ToArray())
            {
                if (!pl || pl.IsDead || pl.NetId < 0 || !p.For(pl)) continue;
                float dist = Mathf.Sqrt(Players.FlatSqr(pl.transform.position, p.transform.position));
                bool reach = magnetic ? vacuum || dist < Pickup.CoinMagnetRadius : dist < 1.1f;
                if (reach && pl.Run && pl.Run.CanAcquire(p.Item, out _)) Take(p, pl, quiet: true);
                if (!p) return;
            }
        }

        /// <summary>Host: a player takes (or buys) their copy. False (and the reason, for them) if they can't.</summary>
        public static bool Take(Pickup p, Player pl, bool quiet)
        {
            if (!p || !pl || !pl.Run || !p.For(pl)) return false;
            if (!p.CollectInto(pl.Run, out string reason))
            {
                if (!quiet) Deny(pl, reason);
                return false;
            }
            p.MarkTaken(pl);
            if (!pl.IsLocal && NetworkServer.connections.TryGetValue(pl.NetId, out var conn)) conn.Send(new PickupGoneMsg { id = p.NetId });
            if (p.TakenByAll)
            {
                byId.Remove(p.NetId);
                foreach (var other in Players.All)
                    if (other && !other.IsLocal && NetworkServer.connections.TryGetValue(other.NetId, out var c2)) c2.Send(new PickupGoneMsg { id = p.NetId });
                p.Discard();
            }
            return true;
        }

        static void Deny(Player pl, string reason)
        {
            if (pl.IsLocal) { Sfx.Play("fizzle", 0.4f, 1.3f); if (SpellSystem.Instance) SpellSystem.Instance.Flash(reason); }
            else if (NetworkServer.connections.TryGetValue(pl.NetId, out var conn)) conn.Send(new DenyMsg { reason = reason });
        }

        public static void OnTake(NetworkConnectionToClient conn, PickupTakeMsg m)
        {
            var pl = Players.ByNetId(conn.connectionId);
            if (!pl || pl.IsDead || !byId.TryGetValue(m.id, out var p) || !p) return;
            // Close enough on the host's side (with room for lag)?
            float reach = p.IsAuto ? Pickup.CoinMagnetRadius + 1f : Pickup.InteractRange + 1.2f;
            if (Players.FlatSqr(pl.transform.position, p.transform.position) > reach * reach) { Deny(pl, "Too far away"); return; }
            Take(p, pl, quiet: false);
        }

        // ---------------- host: each player's run ----------------

        public static void Tick()
        {
            if (!NetPlayers.Running || !NetworkServer.active) return;
            runTimer -= Time.unscaledDeltaTime;
            if (runTimer > 0f) return;
            runTimer = 0.1f;
            foreach (var pl in Players.All)
            {
                if (!pl || pl.IsLocal || pl.NetId < 0 || !pl.Run) continue;
                string json = JsonUtility.ToJson(Describe(pl.Run));
                if (lastRunJson.TryGetValue(pl.NetId, out var last) && last == json) continue;
                lastRunJson[pl.NetId] = json;
                if (NetworkServer.connections.TryGetValue(pl.NetId, out var conn)) conn.Send(new RunStateMsg { json = json });
            }
        }

        /// <summary>Host: every pickup a player (joining mid-run) can still take.</summary>
        public static void SendAll(NetworkConnectionToClient conn, Player forPlayer)
        {
            foreach (var kv in byId)
            {
                var p = kv.Value;
                if (!p || (forPlayer && !p.For(forPlayer))) continue;
                conn.Send(new PickupSpawnMsg
                {
                    id = p.NetId, item = p.Item.Id, spell = p.Item.Spell != null ? SpellCodec.ToJson(p.Item.Spell) : null,
                    pos = p.transform.position, height = p.Height, price = p.Price, onlyFor = p.OnlyFor,
                });
            }
        }

        public static RunNet Describe(Run r)
        {
            var n = new RunNet
            {
                gold = r.Gold, goldEarned = r.GoldEarned, essence = r.Essence, forgesDone = r.ForgesDone, essenceBadLuck = r.EssenceBadLuck, essenceRolls = r.EssenceRolls, haste = Mathf.Ceil(r.HasteTimer * 4f) / 4f, stats = r.Stats,
                toastId = r.ToastId, toastTitle = r.ToastTitle, toastText = r.ToastText, toastColor = r.ToastColor,
            };
            foreach (var i in r.Items) n.items.Add(i.Id);
            foreach (var c in r.Consumables) n.consumables.Add(c != null ? c.Id : "");
            return n;
        }

        public static void OnUseItem(NetworkConnectionToClient conn, UseItemMsg m)
        {
            var pl = Players.ByNetId(conn.connectionId);
            if (pl && pl.Run && !pl.IsDead) pl.Run.UseConsumable(m.slot);
        }

        public static void OnForge(NetworkConnectionToClient conn, ForgeMsg m)
        {
            var pl = Players.ByNetId(conn.connectionId);
            if (!pl || pl.IsDead) return;
            if (Dungeon.Instance && Dungeon.Instance.InCombat) { Deny(pl, "The Forge is closed during a fight"); return; }
            if (m.forget >= 0)
            {
                var s = NetCombat.Forget(pl, m.forget);
                if (s != null)
                {
                    if (pl.Run) pl.Run.RefundForget(s); // the host pays the refund; the rune remembers it
                    // Its rune drops at their feet, for them only.
                    Vector3 at = pl.transform.position + pl.transform.forward * 1.3f;
                    var room = Dungeon.Instance ? Dungeon.Instance.RoomAt(pl.transform.position) : null;
                    if (room != null && !room.Contains(at, 1f)) at = pl.transform.position;
                    Pickup.SpawnFor(Items.ForgottenRune(s), at, pl.NetId);
                }
                return;
            }
            // The host checks the price: Essence and forges done are that player's own.
            if (pl.Run && !pl.Run.CanAffordForge) { Deny(pl, $"Not enough Essence: forging costs {pl.Run.ForgeCost}"); NetCombat.SendBook(pl); return; }
            NetCombat.Fuse(pl, m.a, m.b);
        }

        // ---------------- host: floors ----------------

        /// <summary>Host: everyone is going to this floor (sent before the host builds it).</summary>
        public static void FloorChanging(int floor, bool victory)
        {
            if (!NetPlayers.Running || !NetworkServer.active) return;
            byId.Clear();
            NetworkServer.SendToAll(new FloorMsg { floor = floor, victory = victory });
        }

        // ---------------- client ----------------

        static readonly Dictionary<int, Pickup> proxies = new Dictionary<int, Pickup>();
        static readonly HashSet<int> requested = new HashSet<int>();

        public static void OnSpawn(PickupSpawnMsg m)
        {
            if (!ClientSide) return;
            if (m.onlyFor >= 0 && NetSession.Instance && m.onlyFor != NetSession.Instance.MyId) return;
            ItemDef item = null;
            try
            {
                // Known items first ("rune_stone" is a consumable), then spell runes ("rune_fireball").
                item = !string.IsNullOrEmpty(m.spell) ? Items.ForgottenRune(SpellCodec.FromJson(m.spell)) : Items.Resolve(m.item);
                if (item == null && m.item != null && m.item.StartsWith("rune_") && SpellBook.CreateBase(m.item.Substring(5)) != null)
                    item = Items.Rune(m.item.Substring(5));
            }
            catch (System.Exception e) { Debug.LogWarning("[Spellcaster] couldn't read item " + m.item + " from host: " + e.Message); }
            if (item == null) { Debug.LogWarning("[Spellcaster] unknown item from host: " + m.item); return; }
            SpawningProxy = true;
            Pickup p;
            try { p = Pickup.Spawn(item, m.pos, m.price, false, m.height); }
            finally { SpawningProxy = false; }
            if (!p) return;
            p.MakeProxy(m.id, m.pos, m.pop ? m.vel : Vector3.zero);
            proxies[m.id] = p;
        }

        public static void OnGone(PickupGoneMsg m)
        {
            if (proxies.TryGetValue(m.id, out var p) && p)
            {
                if (p.Item.Kind == ItemKind.Coin) Sfx.Play("coin", 0.5f, Random.Range(0.95f, 1.1f));
                else if (p.Item.Kind == ItemKind.Essence) { Sfx.Play("essence", 0.55f, Random.Range(0.97f, 1.06f)); if (Run.Instance) Run.Instance.EssencePulse = 1f; }
                else if (p.Item.Kind != ItemKind.Heart) Sfx.Play("item", 0.6f);
                Fx.Flash(p.transform.position, p.Item.Color, 1.2f, 0.3f, 1.5f);
                p.Discard();
            }
            proxies.Remove(m.id);
            requested.Remove(m.id);
        }

        /// <summary>Client: ask the host for this pickup (once until it answers).</summary>
        public static void RequestTake(Pickup p, bool force = false)
        {
            if (!p || p.NetId == 0) return;
            if (!force && requested.Contains(p.NetId)) return;
            requested.Add(p.NetId);
            NetworkClient.Send(new PickupTakeMsg { id = p.NetId });
        }

        public static void OnDeny(DenyMsg m)
        {
            requested.Clear(); // allow trying again
            Sfx.Play("fizzle", 0.4f, 1.3f);
            if (SpellSystem.Instance) SpellSystem.Instance.Flash(m.reason);
        }

        public static void OnRunState(RunStateMsg m)
        {
            if (!ClientSide || !Run.Instance) return;
            var n = JsonUtility.FromJson<RunNet>(m.json);
            Run.Instance.ApplyNet(n);
        }

        public static void SendUse(int slot) => NetworkClient.Send(new UseItemMsg { slot = slot });
        public static void SendFuse(int a, int b) => NetworkClient.Send(new ForgeMsg { a = a, b = b, forget = -1 });
        public static void SendForget(int index) => NetworkClient.Send(new ForgeMsg { a = -1, b = -1, forget = index });

        public static void OnBook(SpellBookMsg m)
        {
            if (!ClientSide || m.spells == null) return;
            // Keep this PC's spell objects where the ids match (cooldowns are tracked per object).
            var old = SpellBook.Known.ToList();
            SpellBook.Known.Clear();
            foreach (var json in m.spells)
            {
                var s = SpellCodec.FromJson(json);
                if (s == null) continue;
                var same = old.FirstOrDefault(o => o.Id == s.Id);
                SpellBook.Known.Add(same ?? s);
            }
            foreach (var gone in old.Where(o => !SpellBook.Known.Contains(o)))
                if (SpellSystem.Instance) SpellSystem.Instance.OnForgotten(gone);
        }

        public static void OnFloor(FloorMsg m)
        {
            if (!ClientSide) return;
            proxies.Clear();
            requested.Clear();
            if (m.victory) { if (GameState.Instance) GameState.Instance.Win(); return; }
            var d = Dungeon.Instance;
            if (!d) return;
            d.BuildFloor(m.floor);
            d.HoldEncounters = true; // fights are the host's
        }

        public static void OnCooldownReset(CooldownResetMsg m)
        {
            if (SpellSystem.Instance) SpellSystem.Instance.ResetCooldowns();
        }

        public static int ProxyCount => proxies.Count(kv => kv.Value);
    }
}
