using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>
    /// Players in a multiplayer run.
    /// - Your own player moves at once on your PC (prediction). Your inputs go to the host 30 times a
    ///   second; the host replays them on its copy (the authority) and sends back where you really are.
    ///   If that differs, your player is put there and your newer inputs are replayed (reconciliation).
    /// - Other players on a client are drawn from the host's snapshots, slightly in the past so they
    ///   move smoothly between updates (interpolation).
    /// - Health comes from the host; only the host applies damage.
    /// </summary>
    public static class NetPlayers
    {
        /// <summary>How far a prediction may be off before it is corrected (metres).</summary>
        public const float CorrectionThreshold = 0.12f;
        /// <summary>Other players are shown this far in the past (seconds), to always have two snapshots to blend.</summary>
        public const float InterpDelay = 0.1f;
        /// <summary>At most this many inputs per message (an unreliable packet must stay small).</summary>
        const int MaxBatch = 40;

        /// <summary>A multiplayer run is on (players are synced).</summary>
        public static bool Running { get; private set; }

        // ---------------- run start / end ----------------

        /// <summary>
        /// Creates the other players for a run (host: simulated from their inputs; client: snapshot playback)
        /// and gives everyone their lobby id, name and colour. Called before the floor is built, so the
        /// floor places everyone in the start room.
        /// </summary>
        /// <summary>Run start: players, then the combat side (each player's spells, enemies).</summary>
        public static void SetupRun(StartRunMsg m)
        {
            SetupRun(m.players ?? new RosterEntry[0], m.yourId);
            NetCombat.SetupRun(m.players ?? new RosterEntry[0], m.spells);
            NetEnemies.SetupRun();
            NetGrimoire.SetupRun(m);
        }

        public static void SetupRun(RosterEntry[] roster, int myId)
        {
            ResetState();
            var local = Player.Instance;
            bool host = NetworkServer.active;
            for (int i = 0; i < roster.Length; i++)
            {
                var r = roster[i];
                Player p;
                if (r.id == myId) p = local;
                else
                {
                    p = Players.ByNetId(r.id);
                    if (!p)
                    {
                        Vector3 at = local ? local.transform.position : Vector3.zero;
                        p = Player.Create(at + new Vector3(1.5f * (i + 1), 0f, 0f), local: false);
                    }
                    p.Mode = host ? Player.Control.Simulated : Player.Control.Interpolated;
                }
                if (!p) continue;
                p.NetId = r.id;
                p.DisplayName = r.name;
                p.ColorIndex = i;
            }
            Running = true;
        }

        /// <summary>A player left mid-run (Phase 6 decides what stays): their character is removed.</summary>
        public static void RemovePlayer(int id)
        {
            var p = Players.ByNetId(id);
            if (p && !p.IsLocal) Object.Destroy(p.gameObject);
            buffers.Remove(id);
            lastSeq.Remove(id);
            budget.Remove(id);
            NetGrimoire.RemovePlayer(id);
        }

        /// <summary>The session ended: other players go, this PC's player is single-player again.</summary>
        public static void EndRun()
        {
            foreach (var p in Players.All.ToArray())
                if (p && !p.IsLocal) Object.Destroy(p.gameObject);
            if (Player.Instance) { Player.Instance.NetId = -1; Player.Instance.ColorIndex = 0; }
            ResetState();
            Running = false;
            NetCombat.EndRun();
            NetEnemies.EndRun();
            NetGrimoire.EndRun();
        }

        static void ResetState()
        {
            history.Clear();
            buffers.Clear();
            lastSeq.Clear();
            budget.Clear();
            lastAcked = 0;
            Corrections = 0;
            LastError = 0f;
            StatesReceived = 0;
        }

        // ---------------- every frame (NetSession.Update) ----------------

        static float sendTimer, stateTimer;

        public static void Tick()
        {
            if (!Running) return;
            float dt = Time.unscaledDeltaTime;
            if (NetworkServer.active)
            {
                stateTimer -= dt;
                if (stateTimer <= 0f) { stateTimer += 1f / NetConfig.TickRate; if (stateTimer < 0f) stateTimer = 0f; BroadcastStates(); }
            }
            else if (NetworkClient.isConnected)
            {
                sendTimer -= dt;
                if (sendTimer <= 0f) { sendTimer += 1f / NetConfig.TickRate; if (sendTimer < 0f) sendTimer = 0f; SendInputs(); }
                Interpolate();
            }
        }

        // ---------------- client: prediction and reconciliation ----------------

        struct Hist { public Player.MoveInput input; public Player.SimState after; }
        static readonly List<Hist> history = new List<Hist>();
        static uint lastAcked;
        /// <summary>Times the host corrected this client's prediction, and the last error measured (tests, debug overlay).</summary>
        public static int Corrections { get; private set; }
        public static float LastError { get; private set; }
        public static int StatesReceived { get; private set; }

        /// <summary>Called by the local player after each movement step.</summary>
        public static void RecordLocal(Player p, Player.MoveInput input)
        {
            if (!Running || !NetSession.IsClientOnly) return;
            history.Add(new Hist { input = input, after = p.GetSimState() });
            if (history.Count > 1200) history.RemoveRange(0, history.Count - 1200);
        }

        /// <summary>Sends every input the host hasn't confirmed (resent until confirmed, so a lost packet costs nothing).</summary>
        static void SendInputs()
        {
            int start = history.FindIndex(h => h.input.seq > lastAcked);
            if (start < 0) return;
            start = Mathf.Max(start, history.Count - MaxBatch * 6); // a very long stall: the oldest are dropped
            for (int from = start; from < history.Count; from += MaxBatch)
            {
                var frames = new InputFrame[Mathf.Min(MaxBatch, history.Count - from)];
                for (int i = 0; i < frames.Length; i++)
                {
                    var m = history[from + i].input;
                    frames[i] = new InputFrame { seq = m.seq, dt = m.dt, yaw = m.yaw, pitch = m.pitch, wish = m.wish, autoDir = m.autoDir, flags = m.flags };
                }
                NetworkClient.Send(new InputBatchMsg { frames = frames }, Channels.Unreliable);
            }
        }

        public static void OnStates(PlayerStatesMsg msg)
        {
            if (NetworkServer.active || !Running || msg.players == null) return;
            StatesReceived++;
            float now = Time.unscaledTime;
            foreach (var e in msg.players)
            {
                var p = Players.ByNetId(e.id);
                if (!p) continue;
                p.SetNetHealth(e.hp);
                if (p.IsLocal) Reconcile(p, e);
                else
                {
                    if (!buffers.TryGetValue(e.id, out var list)) buffers[e.id] = list = new List<Snap>();
                    list.Add(new Snap { t = now, pos = e.pos, yaw = e.yaw, pitch = e.pitch, casting = e.casting });
                    if (list.Count > 40) list.RemoveRange(0, list.Count - 40);
                }
            }
        }

        static void Reconcile(Player p, PlayerStateEntry e)
        {
            if (e.lastSeq == 0 || e.lastSeq <= lastAcked) return;
            lastAcked = e.lastSeq;
            int i = history.FindIndex(h => h.input.seq == e.lastSeq);
            if (i < 0) return;
            LastError = Vector3.Distance(history[i].after.pos, e.pos);
            history.RemoveRange(0, i + 1);
            if (LastError <= CorrectionThreshold) return;
            Corrections++;
            p.SetSimState(new Player.SimState
            {
                pos = e.pos, planar = e.planar, vertical = e.vertical, decel = e.decel, chill = e.chill,
                wasAuto = e.wasAuto, wasStill = e.wasStill,
            });
            for (int k = 0; k < history.Count; k++)
            {
                var h = history[k];
                p.Simulate(h.input);
                h.after = p.GetSimState();
                history[k] = h;
            }
        }

        // ---------------- client: other players ----------------

        struct Snap { public float t; public Vector3 pos; public float yaw, pitch; public bool casting; }
        static readonly Dictionary<int, List<Snap>> buffers = new Dictionary<int, List<Snap>>();

        static void Interpolate()
        {
            float t = Time.unscaledTime - InterpDelay;
            foreach (var kv in buffers)
            {
                var p = Players.ByNetId(kv.Key);
                var list = kv.Value;
                if (!p || p.IsLocal || list.Count == 0) continue;
                int b = list.FindIndex(s => s.t >= t);
                Snap s0, s1;
                if (b <= 0) { s0 = s1 = list[b < 0 ? list.Count - 1 : 0]; }
                else { s0 = list[b - 1]; s1 = list[b]; }
                float k = s1.t > s0.t ? Mathf.Clamp01((t - s0.t) / (s1.t - s0.t)) : 1f;
                p.SetInterpolated(Vector3.Lerp(s0.pos, s1.pos, k), Mathf.LerpAngle(s0.yaw, s1.yaw, k), Mathf.Lerp(s0.pitch, s1.pitch, k), s1.casting);
                if (b > 1) list.RemoveRange(0, b - 1);
            }
        }

        // ---------------- host ----------------

        static readonly Dictionary<int, uint> lastSeq = new Dictionary<int, uint>();
        /// <summary>Movement time each client may still use (refills with real time; stops speed hacks).</summary>
        static readonly Dictionary<int, float> budget = new Dictionary<int, float>();
        static readonly Dictionary<int, float> budgetAt = new Dictionary<int, float>();
        const float MaxBudget = 0.5f;

        public static void OnInputs(NetworkConnectionToClient conn, InputBatchMsg msg)
        {
            if (!Running || msg.frames == null) return;
            var p = Players.ByNetId(conn.connectionId);
            if (!p || p.IsLocal || p.Mode != Player.Control.Simulated) return;
            lastSeq.TryGetValue(conn.connectionId, out uint last);
            float now = Time.unscaledTime;
            budget.TryGetValue(conn.connectionId, out float b);
            if (budgetAt.TryGetValue(conn.connectionId, out float at)) b = Mathf.Min(MaxBudget, b + (now - at) * 1.1f);
            else b = MaxBudget;
            budgetAt[conn.connectionId] = now;
            foreach (var f in msg.frames)
            {
                if (f.seq <= last) continue;
                float d = Mathf.Clamp(f.dt, 0f, 0.1f);
                if (d > b) break; // more movement than real time allows: wait for the next batch
                b -= d;
                last = f.seq;
                if (p.IsDead) continue;
                p.Simulate(new Player.MoveInput { seq = f.seq, dt = d, yaw = f.yaw, pitch = f.pitch, wish = Vector2.ClampMagnitude(f.wish, 1f), autoDir = f.autoDir, flags = f.flags });
            }
            budget[conn.connectionId] = b;
            lastSeq[conn.connectionId] = last;
        }

        static readonly List<PlayerStateEntry> entries = new List<PlayerStateEntry>();

        static void BroadcastStates()
        {
            entries.Clear();
            foreach (var p in Players.All)
            {
                if (!p || p.NetId < 0) continue;
                var s = p.GetSimState();
                lastSeq.TryGetValue(p.NetId, out uint seq);
                bool casting = p.IsLocal ? (p.LastInput.flags & Player.FlagCasting) != 0 : p.NetCasting;
                entries.Add(new PlayerStateEntry
                {
                    id = p.NetId, lastSeq = p.IsLocal ? 0 : seq, pos = s.pos, planar = s.planar, vertical = s.vertical,
                    decel = s.decel, chill = s.chill, wasAuto = s.wasAuto, wasStill = s.wasStill,
                    yaw = p.Yaw, pitch = p.Pitch, hp = p.HP, casting = casting,
                });
            }
            if (entries.Count > 0) NetworkServer.SendToAll(new PlayerStatesMsg { players = entries.ToArray() }, Channels.Unreliable);
        }
    }
}
