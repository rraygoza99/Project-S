using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>
    /// Combat in a multiplayer run. The host runs everything that matters (enemies and their AI, damage,
    /// spells, projectiles, hazards, props, doors); clients see it:
    /// - enemies: spawned as proxies (no AI, no hits), moved from snapshots, drawn with the host's frame;
    /// - visuals and sounds: every flash, ring, lightning bolt, projectile, poison cloud and burning
    ///   ground the host makes is replayed on clients (looks only);
    /// - casting: a client draws its rune on its own PC and sends only "cast X looking this way"; the host
    ///   checks the player knows X and that it's off cooldown, then casts it as that player.
    /// Friendly fire is off: player spells only hurt enemies and props.
    /// </summary>
    public static class NetCombat
    {
        public static bool Running => NetPlayers.Running;
        /// <summary>The host is in a run with at least one other player: mirror what happens.</summary>
        public static bool HostMirroring => NetPlayers.Running && NetworkServer.active && NetworkServer.connections.Count > 1;
        static bool ClientProxying => NetPlayers.Running && NetSession.IsClientOnly;

        // ---------------- visuals and sounds (host -> clients) ----------------

        public const byte FxFlash = 1, FxRing = 2, FxArea = 3, FxLightning = 4, FxSound = 5, FxProjectile = 6, FxProjectileEnd = 7,
            FxHoming = 8, FxPoison = 9, FxLava = 10;

        /// <summary>Sounds that belong to the world (heard by everyone); personal ones (coins, menus, your hurt) stay local.</summary>
        static readonly HashSet<string> WorldSounds = new HashSet<string>
        {
            "explode", "claw", "burn", "zap", "door", "clear", "shatter", "freeze", "enemyshot", "bolt", "wave", "throw",
            "spikes", "hit", "crate", "boss", "cast", "portal", "death",
        };

        static void SendFx(FxMsg m, bool reliable = false) =>
            NetworkServer.SendToAll(m, reliable ? Channels.Reliable : Channels.Unreliable);

        public static void Flash(Vector3 p, Color c, float size, float duration, float grow) =>
            SendFx(new FxMsg { kind = FxFlash, a = p, color = c, f0 = size, f1 = duration, f2 = grow });
        public static void Ring(Vector3 p, Color c, float radius, float duration) =>
            SendFx(new FxMsg { kind = FxRing, a = p, color = c, f0 = radius, f1 = duration });
        public static void Area(Vector3 p, Color c, float radius, float duration) =>
            SendFx(new FxMsg { kind = FxArea, a = p, color = c, f0 = radius, f1 = duration });
        public static void Lightning(Vector3 a, Vector3 b, Color c, float duration) =>
            SendFx(new FxMsg { kind = FxLightning, a = a, b = b, color = c, f0 = duration });
        public static void Poison(Vector3 p, float radius, float seconds, Color c) =>
            SendFx(new FxMsg { kind = FxPoison, a = p, f0 = radius, f1 = seconds, color = c }, true);
        public static void Lava(Vector3 p, float radius, float seconds) =>
            SendFx(new FxMsg { kind = FxLava, a = p, f0 = radius, f1 = seconds }, true);

        public static void Sound(string name, float volume, float pitch, string fallback)
        {
            bool world = WorldSounds.Contains(name) || name.EndsWith("_attack") || name.EndsWith("_hit") || name.EndsWith("_death");
            if (world) SendFx(new FxMsg { kind = FxSound, s = name + (fallback != null ? "|" + fallback : ""), f0 = volume, f1 = pitch });
        }

        static int nextProjectile = 1;
        /// <summary>A projectile appeared on the host: clients fly a harmless copy until it ends.</summary>
        public static int ProjectileSpawned(Vector3 pos, Vector3 vel, Color c, float size, float life, float gravity)
        {
            int id = nextProjectile++;
            SendFx(new FxMsg { kind = FxProjectile, id = id, a = pos, b = vel, color = c, f0 = size, f1 = life, f2 = gravity }, true);
            return id;
        }
        public static void ProjectileEnded(int id, Vector3 pos) => SendFx(new FxMsg { kind = FxProjectileEnd, id = id, a = pos }, true);
        public static void ProjectileHoming(int id, Enemy target, float turnRate) =>
            SendFx(new FxMsg { kind = FxHoming, id = id, target = target ? target.NetId : 0, f0 = turnRate }, true);

        static readonly Dictionary<int, Projectile> projectiles = new Dictionary<int, Projectile>();
        /// <summary>Visuals replayed on this client (tests).</summary>
        public static int FxReceived { get; private set; }
        public static int ProjectilesSeen { get; private set; }

        public static void OnFx(FxMsg m)
        {
            if (!ClientProxying) return;
            FxReceived++;
            switch (m.kind)
            {
                case FxFlash: Fx.Flash(m.a, m.color, m.f0, m.f1, m.f2); break;
                case FxRing: Fx.Ring(m.a, m.color, m.f0, m.f1); break;
                case FxArea: Fx.AreaMarker(m.a, m.color, m.f0, m.f1); break;
                case FxLightning: LightningFx.Bolt(m.a, m.b, m.color, m.f0); break;
                case FxPoison: PoisonCloud.Create(m.a, m.f0, m.f1, 0f, m.color); break;
                case FxLava: LavaPool.CreateTemporary(m.a, m.f0, m.f1); break;
                case FxBanner: if (GameState.Instance) GameState.Instance.Show(m.s, m.f0); break;
                case FxPortal:
                    if (Dungeon.Instance && Dungeon.Instance.RoomById(m.s) is Room pr && !Dungeon.Instance.PortalOpen) Dungeon.Instance.OpenPortal(pr);
                    break;
                case FxSound:
                {
                    string name = m.s ?? "", fallback = null;
                    int bar = name.IndexOf('|');
                    if (bar >= 0) { fallback = name.Substring(bar + 1); name = name.Substring(0, bar); }
                    Sfx.Play(name, m.f0, m.f1, fallback);
                    break;
                }
                case FxProjectile:
                {
                    var p = Projectile.Spawn(m.a, m.b, m.color, m.f0, 0f, 0, null, m.f1, null, m.f2);
                    projectiles[m.id] = p;
                    ProjectilesSeen++;
                    break;
                }
                case FxHoming:
                    if (projectiles.TryGetValue(m.id, out var hp) && hp && NetEnemies.ById(m.target) is Enemy t) hp.Home(t, m.f0);
                    break;
                case FxProjectileEnd:
                    if (projectiles.TryGetValue(m.id, out var ep))
                    {
                        if (ep) { ep.transform.position = m.a; Object.Destroy(ep.gameObject); }
                        projectiles.Remove(m.id);
                    }
                    break;
            }
        }

        // ---------------- casting (client -> host) ----------------

        /// <summary>One player's spells as the host keeps them: what they know and when each is ready.</summary>
        class Caster
        {
            public readonly List<SpellDefinition> book = new List<SpellDefinition>();
            public SpellDefinition Find(string id) { foreach (var s in book) if (s.Id == id) return s; return null; }
            public readonly Dictionary<string, float> readyAt = new Dictionary<string, float>();
            public float boltReadyAt;
            public int casts;
        }
        static readonly Dictionary<int, Caster> casters = new Dictionary<int, Caster>();
        /// <summary>Latency allowance on cooldowns (seconds early a cast may arrive and still count).</summary>
        public const float CooldownTolerance = 0.15f;
        public static int RemoteCasts { get; private set; }
        public static int Rejected { get; private set; }

        /// <summary>Run start: every player starts with these spells (the host keeps each client's own copy).</summary>
        public static void SetupRun(RosterEntry[] roster, string[] spells)
        {
            casters.Clear();
            projectiles.Clear();
            RemoteCasts = Rejected = 0;
            FxReceived = ProjectilesSeen = 0;
            if (spells == null) return;
            if (NetworkServer.active)
                foreach (var r in roster)
                {
                    var c = new Caster();
                    foreach (var id in spells) { var s = SpellBook.CreateBase(id); if (s != null) c.book.Add(s); }
                    casters[r.id] = c;
                }
            else
            {
                // A client's spell book is the host's starting list.
                SpellBook.Known.Clear();
                foreach (var id in spells) { var s = SpellBook.CreateBase(id); if (s != null) SpellBook.Known.Add(s); }
            }
        }

        public static void EndRun()
        {
            casters.Clear();
            foreach (var p in projectiles.Values) if (p) Object.Destroy(p.gameObject);
            projectiles.Clear();
        }

        /// <summary>Client: ask the host to cast (the rune was drawn here; the result is the host's).</summary>
        public static void SendCast(SpellDefinition spell, float heldFor = 0f)
        {
            var me = Player.Instance;
            if (!me) return;
            NetworkClient.Send(new CastIntentMsg { spellId = spell != null ? spell.Id : null, yaw = me.Yaw, pitch = me.Pitch, heldFor = heldFor });
        }

        public static void OnCastIntent(NetworkConnectionToClient conn, CastIntentMsg m)
        {
            if (!NetPlayers.Running) return;
            var p = Players.ByNetId(conn.connectionId);
            if (!p || p.IsLocal || p.IsDead || !casters.TryGetValue(conn.connectionId, out var c)) return;
            float now = Time.time;
            var stats = p.Run ? p.Run.Stats : new PlayerStats();
            if (NetGrimoire.Locked(p.NetId)) { Rejected++; return; } // just cycled a card
            p.SetLook(m.yaw, m.pitch);
            if (string.IsNullOrEmpty(m.spellId))
            {
                if (now < c.boltReadyAt - CooldownTolerance) { Rejected++; return; }
                var breq = NetGrimoire.Play(p, null); // the bolt only uses a Blank card
                if (breq != null && breq.Fizzle) { c.boltReadyAt = now + stats.BoltInterval; return; }
                c.boltReadyAt = now + (breq == null ? stats.BoltInterval : breq.ResetCooldown ? 0f : stats.BoltInterval * breq.CooldownMult);
                SpellSystem.FireBoltsFrom(p, stats, breq);
                if (breq != null && breq.EchoDelay > 0f) NetGrimoire.StartEcho(p, null, 1f, breq);
                RemoteCasts++;
                return;
            }
            var spell = c.Find(m.spellId);
            if (spell == null) { Rejected++; return; }
            if (c.readyAt.TryGetValue(m.spellId, out float ready) && now < ready - CooldownTolerance) { Rejected++; return; }
            var req = NetGrimoire.Play(p, spell); // this player's Next card shapes the cast
            if (req != null && req.Fizzle) { Sfx.Play("fizzle", 0.6f, 0.8f); RemoteCasts++; return; } // lost, no cooldown spent
            float cd = spell.Cooldown * (p.Run ? p.Run.CooldownMult : 1f);
            // Their relics, as in single-player: Storm Crown, Overcharge, Echo Stone.
            c.casts++;
            if (stats.FreeCastEvery > 0 && c.casts % stats.FreeCastEvery == 0) cd = 0f;
            float power = stats.Power;
            if (stats.Overcharge && Mathf.Clamp(m.heldFor, 0f, 60f) >= 1.5f) power *= 1.5f;
            if (req != null) cd = req.ResetCooldown ? 0f : cd * req.CooldownMult;
            c.readyAt[m.spellId] = now + cd;
            Sfx.Play("cast", 0.6f);
            if (req == null) new SpellCast(spell, p) { Power = power }.Launch();
            else NetGrimoire.LaunchCopies(p, spell, power, req);
            if (stats.EchoChance > 0f && Random.value < stats.EchoChance && NetSession.Instance)
                NetSession.Instance.StartCoroutine(EchoStone(p, spell, stats.Power * 0.7f));
            RemoteCasts++;
        }

        static System.Collections.IEnumerator EchoStone(Player p, SpellDefinition spell, float power)
        {
            yield return new WaitForSeconds(0.3f);
            if (!p || p.IsDead) yield break;
            new SpellCast(spell, p) { Power = power }.Launch();
            Sfx.Play("cast", 0.5f, 1.25f);
        }

        // ---------------- each player's spell book (host) ----------------

        static Caster CasterOf(Player p) => p && casters.TryGetValue(p.NetId, out var c) ? c : null;
        public static bool Knows(Player p, string id) => CasterOf(p)?.Find(id) != null;
        public static int BookCount(Player p) => CasterOf(p)?.book.Count ?? 0;

        /// <summary>Adds a spell to a player's book (a rune). False if known or full.</summary>
        public static bool Learn(Player p, SpellDefinition s)
        {
            var c = CasterOf(p);
            if (c == null || s == null || c.Find(s.Id) != null || c.book.Count >= SpellBook.MaxSpells) return false;
            c.book.Add(s);
            SendBook(p);
            return true;
        }

        /// <summary>The Forge for another player: fuse two of their spells. Returns the new spell or null.</summary>
        public static SpellDefinition Fuse(Player p, int a, int b)
        {
            var c = CasterOf(p);
            if (c == null || a < 0 || b < 0 || a >= c.book.Count || b >= c.book.Count || a == b) return null;
            if (!SpellForge.TryCombine(c.book[a], c.book[b], c.book, out var made, out _) || made == null) { SendBook(p); return null; }
            c.book.Add(made);
            Sfx.Play("forge", 0.6f);
            SendBook(p);
            return made;
        }

        /// <summary>The Forge for another player: forget one of their spells. Returns it (its rune drops) or null.</summary>
        public static SpellDefinition Forget(Player p, int index)
        {
            var c = CasterOf(p);
            if (c == null || index < 0 || index >= c.book.Count || c.book.Count <= SpellForgeUI.MinSpells) return null;
            var s = c.book[index];
            c.book.RemoveAt(index);
            c.readyAt.Remove(s.Id);
            SendBook(p);
            return s;
        }

        public static void SendBook(Player p)
        {
            var c = CasterOf(p);
            if (c == null || !NetworkServer.connections.TryGetValue(p.NetId, out var conn)) return;
            var list = new List<string>();
            foreach (var s in c.book) list.Add(SpellCodec.ToJson(s));
            conn.Send(new SpellBookMsg { spells = list.ToArray() });
        }

        /// <summary>Scroll of Renewal for another player.</summary>
        public static void ResetCooldowns(Player p)
        {
            var c = CasterOf(p);
            if (c == null) return;
            c.readyAt.Clear();
            c.boltReadyAt = 0f;
            if (NetworkServer.connections.TryGetValue(p.NetId, out var conn)) conn.Send(new CooldownResetMsg());
        }

        public const byte FxBanner = 11, FxPortal = 12;

        public static void PortalOpened(Room room)
        {
            if (HostMirroring && room != null) SendFx(new FxMsg { kind = FxPortal, s = room.Id }, true);
        }

        /// <summary>A player joining a run in progress gets the starting spells (their kept book replaces it).</summary>
        public static void AddCaster(Player p)
        {
            var c = new Caster();
            foreach (var id in SpellBook.StartingIds) { var s = SpellBook.CreateBase(id); if (s != null) c.book.Add(s); }
            casters[p.NetId] = c;
        }

        public static List<string> BookJson(Player p)
        {
            var list = new List<string>();
            var c = CasterOf(p);
            if (c != null) foreach (var s in c.book) list.Add(SpellCodec.ToJson(s));
            return list;
        }

        public static void SetBook(Player p, List<string> jsons)
        {
            var c = CasterOf(p);
            if (c == null) return;
            c.book.Clear();
            foreach (var j in jsons) { var s = SpellCodec.FromJson(j); if (s != null) c.book.Add(s); }
            c.readyAt.Clear();
            SendBook(p);
        }
        public static void Banner(string text, float seconds) =>
            SendFx(new FxMsg { kind = FxBanner, s = text, f0 = seconds }, true);

        // ---------------- rooms and props ----------------

        public static void GatesChanged(Room room, bool locked)
        {
            if (!HostMirroring || room == null) return;
            NetworkServer.SendToAll(new RoomGatesMsg { roomId = room.Id, locked = locked, cleared = room.Cleared });
        }

        public static void OnGates(RoomGatesMsg m)
        {
            if (!ClientProxying || !Dungeon.Instance) return;
            var room = Dungeon.Instance.RoomById(m.roomId);
            if (room == null) return;
            room.Cleared |= m.cleared;
            Dungeon.Instance.SetGates(room, m.locked);
        }

        public static void PropBroken(Prop p)
        {
            if (!HostMirroring || !p) return;
            NetworkServer.SendToAll(new PropBrokenMsg { pos = p.transform.position, kind = (byte)p.Kind });
        }

        public static void OnPropBroken(PropBrokenMsg m)
        {
            if (!ClientProxying) return;
            foreach (var p in Prop.All.ToArray())
                if (p && (byte)p.Kind == m.kind && Players.FlatSqr(p.transform.position, m.pos) < 0.3f * 0.3f) { p.BreakQuietly(); break; }
        }
    }
}
