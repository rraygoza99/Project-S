using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>
    /// Enemies in a multiplayer run. The host runs them (AI, attacks, damage, death) and sends each one's
    /// position, health and the exact sprite frame it shows, 30 times a second. A client keeps a proxy per
    /// enemy: no AI and no hits, moved smoothly between snapshots and drawn with the host's frame and tint.
    /// </summary>
    public static class NetEnemies
    {
        static int nextId = 1;
        /// <summary>Host: every networked enemy until its object is destroyed (dying ones included).</summary>
        static readonly Dictionary<int, Enemy> tracked = new Dictionary<int, Enemy>();
        /// <summary>Client: the proxies.</summary>
        static readonly Dictionary<int, Enemy> proxies = new Dictionary<int, Enemy>();
        static readonly Dictionary<int, List<Snap>> buffers = new Dictionary<int, List<Snap>>();
        struct Snap { public double t; public Vector3 pos; }
        static float stateTimer;
        static bool hooked;

        public static Enemy ById(int id)
        {
            if (tracked.TryGetValue(id, out var e) && e) return e;
            if (proxies.TryGetValue(id, out e) && e) return e;
            return null;
        }

        public static int ProxyCount { get { int n = 0; foreach (var e in proxies.Values) if (e) n++; return n; } }

        public static void SetupRun()
        {
            Clear();
            if (!hooked) { Enemy.Spawned += OnSpawned; hooked = true; }
        }

        public static void EndRun()
        {
            foreach (var e in proxies.Values) if (e) Object.Destroy(e.gameObject);
            Clear();
        }

        static void Clear()
        {
            tracked.Clear();
            proxies.Clear();
            buffers.Clear();
        }

        // ---------------- host ----------------

        static void OnSpawned(Enemy e)
        {
            if (!NetPlayers.Running || !NetworkServer.active || e.Proxy) return;
            e.NetId = nextId++;
            tracked[e.NetId] = e;
            NetworkServer.SendToAll(new EnemySpawnMsg
            {
                id = e.NetId, type = e.Type.Name, pos = e.transform.position, hp = e.HP, maxHp = e.MaxHP, boss = e.Type.IsBoss,
            });
        }

        /// <summary>Host: every living enemy, for a player joining mid-run.</summary>
        public static void SendAll(NetworkConnectionToClient conn)
        {
            foreach (var kv in tracked)
            {
                var e = kv.Value;
                if (!e || !e.IsAlive) continue;
                conn.Send(new EnemySpawnMsg { id = kv.Key, type = e.Type.Name, pos = e.transform.position, hp = e.HP, maxHp = e.MaxHP, boss = e.Type.IsBoss });
            }
        }

        /// <summary>An enemy object is gone (host): clients remove its proxy.</summary>
        public static void OnDestroyed(Enemy e)
        {
            if (e.NetId == 0 || !tracked.Remove(e.NetId)) return;
            if (NetworkServer.active) NetworkServer.SendToAll(new EnemyGoneMsg { id = e.NetId });
        }

        static readonly List<EnemyStateEntry> entries = new List<EnemyStateEntry>();

        public static void Tick()
        {
            if (!NetPlayers.Running) return;
            if (NetworkServer.active)
            {
                stateTimer -= Time.unscaledDeltaTime;
                if (stateTimer > 0f) return;
                stateTimer = Mathf.Max(0f, stateTimer + 1f / NetConfig.TickRate);
                entries.Clear();
                foreach (var kv in tracked)
                {
                    var e = kv.Value;
                    if (!e) continue;
                    var v = e.LastVisual;
                    entries.Add(new EnemyStateEntry
                    {
                        id = kv.Key, pos = e.transform.position, hp = e.HP, alpha = v.alpha, squash = v.squash, lift = v.lift,
                        anim = (byte)v.anim, frame = (byte)v.frame, tint = v.tint, flash = v.flash, dead = !e.IsAlive, elite = e.IsElite,
                    });
                }
                if (entries.Count > 0) NetworkServer.SendToAll(new EnemyStatesMsg { time = NetworkTime.time, enemies = entries.ToArray() }, Channels.Unreliable);
            }
            else Interpolate();
        }

        // ---------------- client ----------------

        public static void OnSpawn(EnemySpawnMsg m)
        {
            if (!NetPlayers.Running || NetworkServer.active || proxies.ContainsKey(m.id)) return;
            var type = EnemyType.ByName(m.type);
            if (type == null) { Debug.LogWarning("[Spellcaster] unknown enemy type from host: " + m.type); return; }
            var e = Enemy.Spawn(type, m.pos);
            e.MakeProxy(m.id, m.hp, m.maxHp);
            proxies[m.id] = e;
            if (m.boss && Dungeon.Instance) Dungeon.Instance.SetBossForNet(e);
        }

        public static void OnStates(EnemyStatesMsg m)
        {
            if (!NetPlayers.Running || NetworkServer.active || m.enemies == null) return;
            double now = m.time;
            foreach (var s in m.enemies)
            {
                if (!proxies.TryGetValue(s.id, out var e) || !e) continue;
                e.ApplyNetState(s);
                if (!buffers.TryGetValue(s.id, out var list)) buffers[s.id] = list = new List<Snap>();
                if (list.Count > 0 && list[list.Count - 1].t >= now) continue; // late or repeated packet
                list.Add(new Snap { t = now, pos = s.pos });
                if (list.Count > 30) list.RemoveRange(0, list.Count - 30);
            }
        }

        public static void OnGone(EnemyGoneMsg m)
        {
            if (proxies.TryGetValue(m.id, out var e) && e) Object.Destroy(e.gameObject);
            proxies.Remove(m.id);
            buffers.Remove(m.id);
        }

        static void Interpolate()
        {
            double t = NetworkTime.time - NetPlayers.InterpDelay;
            foreach (var kv in buffers)
            {
                if (!proxies.TryGetValue(kv.Key, out var e) || !e) continue;
                var list = kv.Value;
                if (list.Count == 0) continue;
                int b = list.FindIndex(s => s.t >= t);
                Snap s0, s1;
                if (b <= 0) s0 = s1 = list[b < 0 ? list.Count - 1 : 0];
                else { s0 = list[b - 1]; s1 = list[b]; }
                float k = s1.t > s0.t ? Mathf.Clamp01((float)((t - s0.t) / (s1.t - s0.t))) : 1f;
                e.transform.position = Vector3.Lerp(s0.pos, s1.pos, k);
                if (b > 1) list.RemoveRange(0, b - 1);
            }
        }
    }
}
