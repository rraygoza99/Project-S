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
            public readonly Dictionary<string, SpellDefinition> known = new Dictionary<string, SpellDefinition>();
            public readonly Dictionary<string, float> readyAt = new Dictionary<string, float>();
            public float boltReadyAt;
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
                    foreach (var id in spells) { var s = SpellBook.CreateBase(id); if (s != null) c.known[id] = s; }
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
        public static void SendCast(SpellDefinition spell)
        {
            var me = Player.Instance;
            if (!me) return;
            NetworkClient.Send(new CastIntentMsg { spellId = spell != null ? spell.Id : null, yaw = me.Yaw, pitch = me.Pitch });
        }

        public static void OnCastIntent(NetworkConnectionToClient conn, CastIntentMsg m)
        {
            if (!NetPlayers.Running) return;
            var p = Players.ByNetId(conn.connectionId);
            if (!p || p.IsLocal || p.IsDead || !casters.TryGetValue(conn.connectionId, out var c)) return;
            float now = Time.time;
            var stats = p.Run ? p.Run.Stats : new PlayerStats();
            p.SetLook(m.yaw, m.pitch);
            if (string.IsNullOrEmpty(m.spellId))
            {
                if (now < c.boltReadyAt - CooldownTolerance) { Rejected++; return; }
                c.boltReadyAt = now + stats.BoltInterval;
                SpellSystem.FireBoltsFrom(p, stats);
                RemoteCasts++;
                return;
            }
            if (!c.known.TryGetValue(m.spellId, out var spell)) { Rejected++; return; }
            if (c.readyAt.TryGetValue(m.spellId, out float ready) && now < ready - CooldownTolerance) { Rejected++; return; }
            float cd = spell.Cooldown * (p.Run ? p.Run.CooldownMult : 1f);
            c.readyAt[m.spellId] = now + cd;
            Sfx.Play("cast", 0.6f);
            new SpellCast(spell, p) { Power = stats.Power }.Launch();
            RemoteCasts++;
        }

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
