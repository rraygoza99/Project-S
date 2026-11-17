using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>
    /// Debug-only network conditions: extra latency and unreliable packet loss on this copy's sends
    /// (Mirror's LatencySimulation around the real transport). Dev mode: Ctrl+F6 cycles the profiles;
    /// tests and copies can start with "-netlag 150 -netloss 2". Applies from the next session.
    /// </summary>
    public static class NetSim
    {
        public static float LatencyMs { get; private set; }
        public static float LossPercent { get; private set; }
        public static bool Active => NetConfig.DebugTransportAllowed && (LatencyMs > 0f || LossPercent > 0f);

        static readonly (float ms, float loss, string name)[] Profiles =
        {
            (0f, 0f, "off"), (150f, 2f, "150 ms, 2% loss"), (300f, 5f, "300 ms, 5% loss"),
        };
        static int profile;

        public static string Describe => Active ? $"{LatencyMs:0} ms, {LossPercent:0.#}% loss" : "off";

        public static void Set(float ms, float lossPercent)
        {
            LatencyMs = Mathf.Max(0f, ms);
            LossPercent = Mathf.Clamp(lossPercent, 0f, 50f);
        }

        public static void NextProfile()
        {
            profile = (profile + 1) % Profiles.Length;
            Set(Profiles[profile].ms, Profiles[profile].loss);
        }
    }

    /// <summary>
    /// Live network numbers for the debug overlay: bytes per second each way, packet loss measured from
    /// the 30 Hz state and input messages (gaps in their sequence numbers), and round-trip times.
    /// </summary>
    public static class NetStats
    {
        static long inBytes, outBytes;
        static float windowStart;
        public static float InKBps { get; private set; }
        public static float OutKBps { get; private set; }

        /// <summary>Attaches the byte counters (again at each session start: Mirror clears its events at startup).</summary>
        public static void Hook()
        {
            NetworkDiagnostics.InMessageEvent -= OnIn;
            NetworkDiagnostics.OutMessageEvent -= OnOut;
            NetworkDiagnostics.InMessageEvent += OnIn;
            NetworkDiagnostics.OutMessageEvent += OnOut;
        }
        static void OnIn(NetworkDiagnostics.MessageInfo m) => inBytes += m.bytes * Mathf.Max(1, m.count);
        static void OnOut(NetworkDiagnostics.MessageInfo m) => outBytes += m.bytes * Mathf.Max(1, m.count);

        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now - windowStart < 1f) return;
            float span = Mathf.Max(0.001f, now - windowStart);
            InKBps = inBytes / 1024f / span;
            OutKBps = outBytes / 1024f / span;
            inBytes = outBytes = 0;
            windowStart = now;
            foreach (var l in loss.Values) l.Roll();
        }

        /// <summary>Counts gaps in a sequence of numbered messages (unreliable: some never arrive).</summary>
        public class LossMeter
        {
            uint last;
            int got, lost, wGot, wLost;
            /// <summary>Lost share (%) over the last few seconds.</summary>
            public float Percent { get; private set; }
            public void Saw(uint seq)
            {
                if (last != 0 && seq > last) lost += (int)Mathf.Min(seq - last - 1, 100);
                if (seq > last) last = seq;
                got++;
            }
            public void Roll()
            {
                wGot += got; wLost += lost;
                got = lost = 0;
                if (wGot + wLost >= 60) // about two seconds of 30 Hz messages
                {
                    Percent = 100f * wLost / (wGot + wLost);
                    wGot = wLost = 0;
                }
            }
        }

        static readonly Dictionary<int, LossMeter> loss = new Dictionary<int, LossMeter>();
        /// <summary>Key -1: what this client receives from the host. Otherwise a client's inputs, measured by the host.</summary>
        public static LossMeter Loss(int key)
        {
            if (!loss.TryGetValue(key, out var m)) loss[key] = m = new LossMeter();
            return m;
        }
        public static void Reset() => loss.Clear();
    }

    /// <summary>The network debug overlay (dev mode: F6, or "-netoverlay").</summary>
    public static class NetOverlay
    {
        public static bool Shown;

        public static void Update()
        {
            if (!NetConfig.DebugTransportAllowed) { Shown = false; return; }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (ctrl) { NetSim.NextProfile(); Sfx.Play("tick", 0.6f); }
                else Shown = !Shown;
            }
        }

        public static void Draw()
        {
            if (!Shown || Event.current.type != EventType.Repaint) return;
            var ns = NetSession.Instance;
            if (!ns) return;
            HUD.EnsureStyles();
            float u = Screen.height / 60f;
            var lines = new List<string>();
            string role = !ns.InSession ? "offline" : ns.IsHost ? "host" : "client";
            lines.Add($"NETWORK  {role}  ({(ns.InSession ? ns.Using.ToString() : "-")})   tick {NetConfig.TickRate} Hz   sim: {NetSim.Describe} (Ctrl+F6)");
            lines.Add($"traffic  in {NetStats.InKBps:0.0} KB/s   out {NetStats.OutKBps:0.0} KB/s");
            if (ns.InSession && !ns.IsHost)
            {
                lines.Add($"you      ping {NetworkTime.rtt * 1000:0} ms   loss from host {NetStats.Loss(-1).Percent:0.0}%");
                lines.Add($"predict  {NetPlayers.Corrections} corrections, last error {NetPlayers.LastError * 100f:0.0} cm");
            }
            if (ns.IsHost)
                foreach (var r in ns.Roster)
                {
                    if (r.host) { lines.Add($"{r.name,-16} (host)"); continue; }
                    double rtt = NetworkServer.connections.TryGetValue(r.id, out var c) ? c.rtt : 0;
                    lines.Add($"{r.name,-16} ping {rtt * 1000:0} ms   input loss {NetStats.Loss(r.id).Percent:0.0}%");
                }
            var st = new GUIStyle(HUD.Small) { alignment = TextAnchor.UpperLeft };
            st.normal.textColor = new Color(0.7f, 1f, 0.75f);
            float h = lines.Count * 1.7f * u + u;
            var box = new Rect(u, Screen.height * 0.28f, 44f * u, h);
            HUD.Fill(box, new Color(0f, 0f, 0f, 0.65f));
            for (int i = 0; i < lines.Count; i++)
                HUD.Shadowed(new Rect(box.x + 0.6f * u, box.y + 0.5f * u + i * 1.7f * u, box.width, 1.8f * u), lines[i], st);
        }
    }
}
