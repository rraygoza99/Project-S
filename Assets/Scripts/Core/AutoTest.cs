using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Scripted self-test, enabled with the command-line flag <c>-autotest &lt;dir&gt;</c>. Drives the real
    /// systems (arrow input, readying, casting, Forge, waves, death), writes screenshots, logs
    /// "AUTOTEST PASS/FAIL ..." lines and an FPS measurement with 30 enemies, then quits.
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        public static bool Active { get; private set; }
        string outDir;
        int passed, failed;
        readonly List<string> events = new List<string>();

        public static void TryStart(GameObject host)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autotest");
            if (i < 0) return;
            Active = true;
            var t = host.AddComponent<AutoTest>();
            t.outDir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.GetFullPath("AutoTest");
            Directory.CreateDirectory(t.outDir);
            Player.Instance.God = true;
        }

        IEnumerator Start()
        {
            var player = Player.Instance;
            var ss = SpellSystem.Instance;
            var gs = GameState.Instance;
            ss.Event += e => events.Add(e);
            yield return new WaitForSeconds(1.5f);
            Check("wave 1 started", gs.Phase == Phase.Playing && gs.Wave == 1);
            FaceNearest(player);
            yield return Shot("01_wave1");

            // ---- performance with 30 enemies ----
            gs.HoldWaves = true;
            while (Enemy.All.Count < 30) EnemySpawner.Instance.SpawnRandom();
            yield return new WaitForSeconds(3f);
            FaceNearest(player);
            yield return Shot("02_horde");
            int frames = 0;
            float elapsed = 0f, worst = 0f;
            while (elapsed < 4f)
            {
                yield return null;
                frames++;
                elapsed += Time.unscaledDeltaTime;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            }
            float fps = frames / elapsed;
            Log($"fps={fps:F1} worstFrameMs={worst * 1000f:F1} enemies={Enemy.All.Count} res={Screen.width}x{Screen.height}");
            Check("60 FPS with 30 enemies", fps >= 57f && Enemy.All.Count >= 29);

            // ---- basic bolt ----
            gs.ClearWave();
            yield return new WaitForSeconds(1.5f);
            var dummy = SpawnAhead(player, EnemyType.Imp, 7f, 0f);
            yield return new WaitForSeconds(0.2f);
            for (int i = 0; i < 4 && dummy.IsAlive; i++)
            {
                player.LookAt(dummy.Center);
                ss.FireBasic();
                yield return new WaitForSeconds(0.5f);
            }
            Check("basic bolt kills an imp (3 hits)", !dummy || !dummy.IsAlive);

            // ---- close-up of both enemy types ----
            var imp = SpawnAhead(player, EnemyType.Imp, 4.5f, -1.2f);
            var eye = SpawnAhead(player, EnemyType.Cultist, 6f, 1.5f);
            yield return new WaitForSeconds(0.6f);
            imp.Freeze(0.6f);
            player.LookAt((imp.Center + eye.Center) * 0.5f);
            yield return Shot("03_enemies_closeup");
            player.SetLook(player.Yaw, -40f);
            yield return Shot("04_look_up");
            player.SetLook(player.Yaw, 30f);
            yield return Shot("05_look_down");
            gs.ClearWave();
            yield return new WaitForSeconds(1.2f);

            // ---- Fireball: projectile + explosion ----
            var group = new List<Enemy>
            {
                SpawnAhead(player, EnemyType.Imp, 9f, 0f), SpawnAhead(player, EnemyType.Imp, 9.8f, 1.1f), SpawnAhead(player, EnemyType.Imp, 9.6f, -1.1f),
            };
            foreach (var e in group) e.Freeze(5f);
            yield return new WaitForSeconds(0.3f);
            player.LookAt(group[0].Center);
            yield return CastSequence("DRU", expectReady: "Fireball", showChannel: "06_channel_fireball");
            Check("Fireball readied after ↓→↑", ss.Readied != null && ss.Readied.Name == "Fireball");
            yield return Shot("07_fireball_ready");
            ss.CastReadied();
            yield return new WaitForSeconds(0.25f);
            yield return Shot("08_fireball_flight");
            yield return new WaitForSeconds(0.6f);
            Check("Fireball explosion killed 3 imps", group.Count(e => e && e.IsAlive) == 0);
            Check("Fireball on cooldown (3s)", ss.OnCooldown(Spell("Fireball")));

            // ---- cooldown blocks readying; wrong input fizzles ----
            yield return CastSequence("DRU", expectReady: null);
            Check("Fireball on cooldown cannot be readied (fizzles)", ss.Readied == null && events.Contains("fizzle"));
            yield return new WaitForSeconds(0.35f);
            events.Clear();
            yield return CastSequence("UU", expectReady: null);
            Check("wrong sequence ↑↑ fizzles with lockout", events.Contains("fizzle") && ss.Lockout > 0f);
            ss.PushArrow(Arrow.Up);
            Check("input ignored during 0.3s lockout", ss.Entered.Count == 0);
            yield return Shot("09_fizzle");
            ss.SetChanneling(false);
            yield return new WaitForSeconds(0.4f);

            // ---- Lightning: hitscan + chain ----
            var line = new List<Enemy>
            {
                SpawnAhead(player, EnemyType.Imp, 8f, 0f), SpawnAhead(player, EnemyType.Imp, 10f, 2.5f), SpawnAhead(player, EnemyType.Imp, 12f, -2.5f),
            };
            foreach (var e in line) e.Freeze(5f);
            yield return new WaitForSeconds(0.3f);
            player.LookAt(line[0].Center);
            yield return CastSequence("RRDR", expectReady: "Lightning");
            ss.CastReadied();
            yield return null;
            yield return Shot("10_lightning");
            yield return new WaitForSeconds(0.4f);
            int lightningKills = line.Count(e => !e || !e.IsAlive);
            Check($"Lightning hit primary and chained ({lightningKills}/3 killed)", lightningKills >= 2);
            gs.ClearWave();
            yield return new WaitForSeconds(1.2f);

            // ---- Frost Nova: freeze everything nearby ----
            var ring = new List<Enemy>();
            for (int i = 0; i < 5; i++)
            {
                float ang = i * 72f * Mathf.Deg2Rad;
                ring.Add(EnemySpawner.Instance.SpawnAt(i % 2 == 0 ? EnemyType.Imp : EnemyType.Cultist,
                    player.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 3.5f));
            }
            yield return new WaitForSeconds(0.4f);
            yield return CastSequence("DDLR", expectReady: "Frost Nova");
            ss.CastReadied();
            yield return new WaitForSeconds(0.15f);
            player.LookAt(ring[0].Center);
            yield return Shot("11_frost_nova");
            Check("Frost Nova froze all 5 nearby enemies", ring.All(e => e && e.IsFrozen));

            // ---- Heal ----
            player.Heal(Player.MaxHP);
            player.God = false;
            player.TakeDamage(50f);
            float before = player.HP;
            yield return CastSequence("UDUD", expectReady: "Heal");
            ss.CastReadied();
            yield return null;
            Check($"Heal restored 40 HP ({before:0} -> {player.HP:0})", Mathf.Approximately(player.HP, Mathf.Min(100f, before + 40f)));
            player.God = true;
            yield return Shot("12_heal");

            // ---- Spell Forge ----
            var forge = SpellForgeUI.Instance;
            forge.Open();
            forge.Toggle(IndexOf("Fireball"));
            forge.Toggle(IndexOf("Frost Nova"));
            yield return Shot("13_forge_preview");
            var frozenFireball = forge.Forge();
            Check("Forge: Fireball + Frost Nova", frozenFireball != null && frozenFireball.Synergies.Contains("Shatter"));
            if (frozenFireball != null) Log($"forged '{frozenFireball.Name}' {Arrows.ToText(frozenFireball.Sequence)} cd={frozenFireball.Cooldown}: {frozenFireball.Describe()}");
            forge.Toggle(IndexOf("Lightning"));
            forge.Toggle(IndexOf("Fireball"));
            var stormLightning = forge.Forge();
            Check("Forge: Lightning + Fireball unlocks Ignite", stormLightning != null && stormLightning.Synergies.Contains("Ignite"));
            if (stormLightning != null) Log($"forged '{stormLightning.Name}' {Arrows.ToText(stormLightning.Sequence)} cd={stormLightning.Cooldown}: {stormLightning.Describe()}");
            if (frozenFireball != null)
            {
                forge.Toggle(IndexOf(frozenFireball.Name));
                forge.Toggle(IndexOf("Heal"));
                var tier3 = forge.Forge();
                Check("Forge tier 3: (Fireball+Frost) + Heal unlocks Lifesteal", tier3 != null && tier3.Tier == 3 && tier3.Synergies.Contains("Lifesteal"));
                if (tier3 != null) Log($"forged '{tier3.Name}' {Arrows.ToText(tier3.Sequence)} cd={tier3.Cooldown}: {tier3.Describe()}");
            }
            forge.Toggle(IndexOf("Fireball"));
            forge.Toggle(IndexOf("Fireball"));
            Check("Forge rejects picking the same spell twice", forge.Forge() == null);
            yield return Shot("14_forge_after");
            forge.Close();
            bool conflictFree = true;
            var book = SpellBook.Known;
            for (int i = 0; i < book.Count; i++)
                for (int j = i + 1; j < book.Count; j++)
                    if (Arrows.Conflicts(book[i].Sequence, book[j].Sequence)) conflictFree = false;
            Check("no two known sequences conflict (prefix-free)", conflictFree);

            // ---- cast forged spells through the normal input path ----
            gs.ClearWave();
            yield return new WaitForSeconds(1.2f);
            ss.ResetCooldowns();
            var tanks = new List<Enemy> { SpawnAhead(player, EnemyType.Cultist, 8f, 0f), SpawnAhead(player, EnemyType.Cultist, 8.5f, 1.3f) };
            yield return new WaitForSeconds(0.3f);
            foreach (var e in tanks) e.Freeze(4f);
            if (frozenFireball != null)
            {
                player.LookAt(tanks[0].Center);
                yield return CastSequence(Arrows.ToText(frozenFireball.Sequence), expectReady: frozenFireball.Name);
                Check($"forged spell readies via its sequence", ss.Readied == frozenFireball);
                yield return Shot("15_forged_ready");
                ss.CastReadied();
                yield return new WaitForSeconds(0.9f);
                Check("Frozen Fireball shattered frozen cultists (60 HP)", tanks.All(e => !e || !e.IsAlive));
                yield return Shot("16_forged_impact");
            }
            if (stormLightning != null)
            {
                var burnTarget = SpawnAhead(player, EnemyType.Cultist, 9f, 0f);
                burnTarget.Freeze(1f);
                yield return new WaitForSeconds(0.3f);
                player.LookAt(burnTarget.Center);
                yield return CastSequence(Arrows.ToText(stormLightning.Sequence), expectReady: stormLightning.Name);
                ss.CastReadied();
                yield return new WaitForSeconds(0.1f);
                Check("Storm Fireball ignites its target", !burnTarget || !burnTarget.IsAlive || burnTarget.IsBurning);
                yield return Shot("17_ignite");
            }

            // ---- waves ----
            gs.HoldWaves = false;
            gs.ClearWave();
            float t = 0f;
            while (gs.Phase != Phase.Intermission && t < 5f) { t += Time.deltaTime; yield return null; }
            Check("cleared wave goes to intermission", gs.Phase == Phase.Intermission);
            yield return Shot("18_intermission");
            t = 0f;
            while (gs.Phase != Phase.Playing && t < 10f) { t += Time.deltaTime; yield return null; }
            Check($"next wave starts automatically (wave {gs.Wave})", gs.Phase == Phase.Playing && gs.Wave == 2);
            yield return new WaitForSeconds(2.5f);
            Check("wave 2 spawns enemies", Enemy.All.Count > 0);

            // ---- death ----
            player.God = false;
            player.TakeDamage(500f);
            yield return null;
            Check("player death shows You Died", gs.Phase == Phase.Dead && player.IsDead);
            yield return Shot("19_death");

            Log($"SUMMARY passed={passed} failed={failed}");
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void Update()
        {
            // Watchdog: never leave a hung test process behind.
            if (Time.realtimeSinceStartup > 180f)
            {
                Log($"FAIL watchdog timeout  SUMMARY passed={passed} failed={failed + 1}");
                Application.Quit(2);
                enabled = false;
            }
        }

        IEnumerator CastSequence(string seq, string expectReady, string showChannel = null)
        {
            var ss = SpellSystem.Instance;
            ss.SetChanneling(true);
            yield return null;
            var arrows = Arrows.Parse(seq);
            for (int i = 0; i < arrows.Length; i++)
            {
                ss.PushArrow(arrows[i]);
                if (showChannel != null && i == arrows.Length - 2) yield return Shot(showChannel);
                yield return new WaitForSeconds(0.06f);
            }
            if (expectReady != null) ss.SetChanneling(false); // releasing keeps the spell readied
        }

        static SpellDefinition Spell(string name) => SpellBook.Known.First(s => s.Name == name);
        static int IndexOf(string name) => SpellBook.Known.FindIndex(s => s.Name == name);

        static Enemy SpawnAhead(Player p, EnemyType type, float distance, float side)
        {
            Vector3 fwd = p.transform.forward, right = p.transform.right;
            Vector3 pos = p.transform.position + fwd * distance + right * side;
            pos.x = Mathf.Clamp(pos.x, -18f, 18f);
            pos.z = Mathf.Clamp(pos.z, -18f, 18f);
            return EnemySpawner.Instance.SpawnAt(type, pos + Vector3.up * 0.05f);
        }

        static Enemy Nearest(Player p)
        {
            Enemy best = null;
            float bestD = float.MaxValue;
            foreach (var e in Enemy.All)
            {
                float d = (e.transform.position - p.transform.position).sqrMagnitude;
                if (d < bestD && !Physics.Linecast(p.Cam.transform.position, e.Center, Layers.WorldMask)) { bestD = d; best = e; }
            }
            return best;
        }

        static void FaceNearest(Player p)
        {
            var e = Nearest(p);
            if (e) p.LookAt(e.Center);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
            yield return null;
            yield return null;
        }

        void Check(string what, bool ok)
        {
            if (ok) passed++; else failed++;
            Log((ok ? "PASS " : "FAIL ") + what);
        }

        static void Log(string msg) => Debug.Log("AUTOTEST " + msg);
    }
}
