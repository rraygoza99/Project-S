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
    /// systems (floor generation, rooms and doors, arrow input, casting, Forge, loot, relics, items,
    /// obstacles, boss, floors, victory, death), writes screenshots, logs "AUTOTEST PASS/FAIL ..." lines
    /// and an FPS measurement with 30 enemies, then quits.
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        public static bool Active { get; private set; }
        const float WatchdogSeconds = 420f;
        string outDir;
        int passed, failed;
        readonly List<string> events = new List<string>();

        public static void TryStart(GameObject host)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autotest");
            if (i < 0) return;
            Active = true;
            CooldownConfig.UseLegacyForTest(); // the older checks test cooldowns; the new rules have their own section
            var t = host.AddComponent<AutoTest>();
            t.outDir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.GetFullPath("AutoTest");
            Directory.CreateDirectory(t.outDir);
            Player.Instance.God = true;
            Progress.Tracking = false; // the run's own events don't unlock things mid-test; the meta section turns it on
        }

        IEnumerator Start()
        {
            var player = Player.Instance;
            var ss = SpellSystem.Instance;
            var gs = GameState.Instance;
            var d = Dungeon.Instance;
            var run = Run.Instance;
            ss.Event += e => events.Add(e);
            SaveSystem.Delete(); // a test save left by an interrupted run (the autotest folder only)
            yield return new WaitForSeconds(0.5f);

            // ---- multiplayer lobby: Steam, and a second copy of the game joining by direct IP ----
            {
                var mpNs = Net.NetSession.Instance;
                Check($"Steam starts, or the game says why not ({(Net.SteamService.Ready ? "signed in as " + Net.SteamService.MyName : Net.SteamService.Error)}); single-player is unaffected",
                    mpNs && (Net.SteamService.Ready || !string.IsNullOrEmpty(Net.SteamService.Error)) && !GameState.Multiplayer);
                var mpBad = new Net.HelloMsg { version = "0.0", name = "x" };
                var mpGood = new Net.HelloMsg { version = Net.NetConfig.Version, name = "x" };
                string mpVMis = Net.NetSession.Validate(mpBad, 1, 4, false), mpVFull = Net.NetSession.Validate(mpGood, 4, 4, false);
                string mpVRun = Net.NetSession.Validate(mpGood, 1, 4, true), mpVOk = Net.NetSession.Validate(mpGood, 1, 4, false);
                Check($"joins are refused for another version, a full lobby, or a run in progress (\"{mpVMis}\")",
                    mpVMis != null && mpVMis.Contains("Version") && mpVFull != null && mpVFull.Contains("full") && mpVRun != null && mpVRun.Contains("progress") && mpVOk == null);

                int mpSeed = 0;
                Net.NetSession.StartRunHandler = m => mpSeed = m.seed; // don't start this copy's run: the test goes on in single-player
                // Playtest bug: an invite accepted on a menu page that isn't a multiplayer page didn't open the lobby.
                MainMenu.Instance.Open(MainMenu.Page.Options);
                yield return null;
                bool mpHosted = mpNs.HostDirect(Net.NetConfig.DebugPort, 4);
                for (int mpF = 0; mpF < 30 && mpNs.Roster.Count < 1; mpF++) yield return null;
                yield return null;
                Check($"joining a session from any menu page (here Options) opens the lobby ({MainMenu.Instance.Current})", MainMenu.Instance.Current == MainMenu.Page.Lobby);
                Check($"hosting (debug direct-IP): the host is in its own lobby ({mpNs.Roster.Count} player, {mpNs.Roster.FirstOrDefault().name}), multiplayer mode on",
                    mpHosted && mpNs.IsHost && mpNs.Roster.Count == 1 && mpNs.Roster[0].host && GameState.Multiplayer);
                MainMenu.Instance.Open(MainMenu.Page.Multiplayer);
                yield return null;
                Check("the menu shows the lobby while a session is on", MainMenu.Instance.Current == MainMenu.Page.Lobby);

                string mpExe = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Spellcaster.exe");
                string mpLog = Path.Combine(outDir, "net_client.log");
                System.Diagnostics.Process mpClient = null;
                try
                {
                    mpClient = Launched(System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mpExe,
                        $"-batchmode -nographics -netjoin {Net.NetConfig.DebugAddress} -netready -netquitafterstart -logFile \"{mpLog}\"") { UseShellExecute = false }));
                }
                catch (Exception mpEx) { Debug.LogWarning("[AutoTest] couldn't start a second copy: " + mpEx.Message); }
                float mpT = 0f;
                while (mpT < 45f && mpNs.Roster.Count < 2) { mpT += Time.unscaledDeltaTime; yield return null; }
                Check($"a second copy of the game joins the lobby by address ({mpNs.Roster.Count} players after {mpT:0.0}s)", mpNs.Roster.Count == 2);
                mpT = 0f;
                while (mpT < 10f && !mpNs.AllReady) { mpT += Time.unscaledDeltaTime; yield return null; }
                MainMenu.FakeSaveForTest = "Floor 12 - 1234 gold - 18 items";
                mpNs.ContinueSaved = true;
                yield return Shot("00h_lobby");
                string mpLayout = MainMenu.Instance.LayoutProblem;
                Check($"lobby layout: the saved-run row and the players fit ({mpLayout ?? "ok"})",
                    mpLayout == null && MainMenu.Instance.LabelsForTest().Contains("Run"));
                mpNs.ContinueSaved = false;
                MainMenu.FakeSaveForTest = null;
                Check($"the joining player readies up and the host sees it ({string.Join(", ", mpNs.Roster.Select(r => r.name + (r.ready ? " ready" : "")))})",
                    mpNs.Roster.Count == 2 && mpNs.AllReady);
                bool mpStarted = mpNs.StartRun();
                mpT = 0f;
                while (mpT < 10f && mpNs.StartAcks < 2) { mpT += Time.unscaledDeltaTime; yield return null; }
                Check($"the host starts the run for everyone (seed {mpSeed}, {mpNs.StartAcks} of 2 players started it)", mpStarted && mpNs.StartAcks >= 2 && mpSeed != 0);
                mpT = 0f;
                while (mpT < 15f && mpNs.Roster.Count > 1) { mpT += Time.unscaledDeltaTime; yield return null; }
                Check($"a player who quits leaves the lobby ({mpNs.Roster.Count} left)", mpNs.Roster.Count == 1);
                if (mpClient != null && !mpClient.HasExited) { try { mpClient.Kill(); } catch { } }
                mpNs.Leave();
                yield return null;
                yield return null;
                Net.NetSession.StartRunHandler = null;
                MainMenu.Instance.Open(MainMenu.Page.Main);
                Check("closing the lobby ends the session; back to single-player", !mpNs.InSession && !GameState.Multiplayer && mpNs.Roster.Count == 0);
            }

            // ---- main menu ----
            var menu = MainMenu.Instance;
            Check("game opens on the main menu", gs.Phase == Phase.Title && menu.Visible && menu.Current == MainMenu.Page.Main);
            yield return Shot("00_main_menu");
            string mmLayout1 = menu.LayoutProblem;
            MainMenu.FakeSaveForTest = "Floor 12 - 1234 gold - 18 items";
            menu.Open(MainMenu.Page.Main);
            yield return Shot("00a_main_menu_continue");
            string mmLayout2 = menu.LayoutProblem;
            bool mmContinue = menu.LabelsForTest().FirstOrDefault() == "Continue";
            MainMenu.FakeSaveForTest = null;
            menu.Open(MainMenu.Page.Main);
            Check($"main menu fits on screen, with and without a saved run's Continue row ({mmLayout1 ?? "ok"}, {mmLayout2 ?? "ok"})",
                mmLayout1 == null && mmLayout2 == null && mmContinue);

            // ---- options ----
            menu.Open(MainMenu.Page.Options);
            Settings.SetMouseSensitivity(3f);
            Settings.SetRuneStroke(6f);
            Settings.SetFieldOfView(95f);
            Settings.SetInvertY(true);
            yield return Shot("00b_options");
            string opLayout = menu.LayoutProblem;
            yield return null;
            Check("options change sensitivity, rune size, FOV and invert Y",
                Mathf.Approximately(Settings.MouseSensitivity, 3f) && Mathf.Approximately(ss.Gesture.StrokeLength, 6f) &&
                Mathf.Approximately(player.Cam.fieldOfView, 95f) && Settings.InvertY);
            int resCount = Settings.AvailableResolutions().Count;
            Settings.CycleResolution(1);
            Check($"resolution list offered ({resCount} sizes, now {Settings.CurrentResolution.x}x{Settings.CurrentResolution.y})", resCount >= 1);

            // ---- controls (rebinding) ----
            menu.Open(MainMenu.Page.Controls);
            Bindings.Set(GameAction.Cast, 0, KeyCode.Mouse1);
            Check("rebinding a key that is in use swaps the two actions",
                Bindings.Get(GameAction.Cast, 0) == KeyCode.Mouse1 && Bindings.Get(GameAction.Channel, 0) == KeyCode.Mouse0);
            Bindings.Set(GameAction.Interact, 1, KeyCode.F);
            menu.Listen(GameAction.Map, 1);
            yield return Shot("00c_controls");
            string ctLayout = menu.LayoutProblem;
            Progress.Tracking = true;
            Progress.Add("kills", 37);
            Progress.Add("deaths", 3);
            Progress.Add("rooms", 10); // unlocks Poison Cloud
            menu.Open(MainMenu.Page.Unlocks);
            yield return Shot("00g_unlocks");
            menu.Open(MainMenu.Page.Seed);
            yield return Shot("00i_seed");
            string sdLayout = menu.LayoutProblem;
            menu.Open(MainMenu.Page.Multiplayer);
            yield return Shot("00j_multiplayer");
            string mlLayout = menu.LayoutProblem;
            menu.Open(MainMenu.Page.Main);
            Check($"Options, Controls, Seeded run and Multiplayer pages: nothing runs into anything ({opLayout ?? "ok"}, {ctLayout ?? "ok"}, {sdLayout ?? "ok"}, {mlLayout ?? "ok"})",
                opLayout == null && ctLayout == null && sdLayout == null && mlLayout == null);
            Check("Unlocks page lists 18 goals with progress", Progress.All.Count == 18 && Progress.UnlockedCount == 2);
            Progress.ResetForTest();
            Progress.Tracking = false;
            Settings.ResetForTest();
            Check("reset restores default settings and controls",
                Bindings.Get(GameAction.Channel, 0) == KeyCode.Mouse1 && Bindings.Get(GameAction.Cast, 0) == KeyCode.Mouse0 &&
                Bindings.Get(GameAction.Interact, 1) == KeyCode.None && !Settings.InvertY && Mathf.Approximately(Settings.FieldOfView, 75f));
            menu.Open(MainMenu.Page.Main);

            // ---- tutorial (first run) ----
            var tut = Tutorial.Instance;
            menu.StartTutorial();
            yield return new WaitForSeconds(0.5f);
            Check("Tutorial starts the run with step 1 (walk), start room doors locked",
                gs.Phase == Phase.Playing && !menu.Visible && tut.Active && tut.Current == Tutorial.Step.Move && d.DoorsLocked);
            yield return Shot("00d_tutorial_move");
            player.Teleport(d.Current.Center + new Vector3(0f, 0.1f, 3f), 0f);
            yield return new WaitForSeconds(0.3f);
            Check("tutorial: walking moves on to the magic bolt", tut.Current == Tutorial.Step.Bolt && Enemy.All.Count == 1);
            var tdummy = Enemy.All[0];
            for (int i = 0; i < 6 && tdummy && tdummy.IsAlive; i++)
            {
                player.LookAt(tdummy.Center);
                ss.FireBasic();
                yield return new WaitForSeconds(0.5f);
            }
            yield return null;
            Check("tutorial: killing the dummy moves on to drawing", tut.Current == Tutorial.Step.Channel);
            ss.SetChanneling(true);
            for (int i = 0; i < 12; i++) { ss.FeedGesture(new Vector2(0.3f, 0.1f)); yield return null; }
            Check("tutorial: starting a rune moves on to Fireball", tut.Current == Tutorial.Step.Fireball);
            ss.SetChanneling(false);
            yield return Shot("00e_tutorial_fireball");
            yield return CastSequence("DRU", expectReady: "Fireball");
            Check("tutorial: drawing Fireball moves on to casting it", tut.Current == Tutorial.Step.Cast && Enemy.All.Count == 3);
            player.LookAt(Enemy.All[0].Center);
            ss.CastReadied();
            yield return new WaitForSeconds(2f);
            Check("tutorial: casting moves on to the tips card", tut.Current == Tutorial.Step.Tips);
            yield return Shot("00f_tutorial_tips");
            tut.Finish();
            yield return null;
            Check("finishing the tutorial opens the doors and clears the dummies",
                !GameState.TutorialMode && !d.DoorsLocked && Enemy.All.Count == 0 && Settings.TutorialDone && !ss.OnCooldown(Spell("Fireball")));
            ClearPickups();
            player.Teleport(d.Current.Center + new Vector3(0f, 0.1f, -3f), 0f);
            yield return new WaitForSeconds(0.3f);

            // ---- floor generation ----
            // Generator stress test: 300 floors of every size, checked for overlaps, doors and room kinds.
            {
                int badFloors = 0, big = 0, floors = 0;
                var shapes = new HashSet<Vector2Int>();
                for (int seed = 1; seed <= 300; seed++)
                {
                    int n = 10 + 2 * (seed % 3);
                    var plan = FloorGenerator.Generate(n, new System.Random(seed));
                    floors++;
                    var owner = new Dictionary<Vector2Int, int>();
                    bool ok = plan.Rooms.Count == n && plan.Doors.Count == n - 1;
                    for (int i = 0; i < plan.Rooms.Count; i++)
                        foreach (var c in plan.Rooms[i].Cells) { if (owner.ContainsKey(c)) ok = false; owner[c] = i; }
                    foreach (var dr in plan.Doors)
                        ok &= owner.TryGetValue(dr.Cell, out var a) && a == dr.A && owner.TryGetValue(dr.Cell + dr.Dir, out var b) && b == dr.B;
                    foreach (var r in plan.Rooms)
                    {
                        if (r.Size != Vector2Int.one) { big++; shapes.Add(r.Size); }
                        if (r.Type != RoomType.Combat && r.Type != RoomType.Boss && r.Size != Vector2Int.one) ok = false;
                    }
                    ok &= plan.Rooms.Count(r => r.Type == RoomType.Boss) == 1 && plan.Rooms.Any(r => r.Type == RoomType.Shop)
                          && plan.Rooms.Any(r => r.Type == RoomType.Treasure) && plan.Rooms.Any(r => r.Type == RoomType.Shrine);
                    if (!ok) badFloors++;
                }
                Check($"300 generated floors: no overlaps, one door per room, specials are 1x1 ({badFloors} bad, {big} big rooms, shapes {string.Join(" ", shapes.Select(s => $"{s.x}x{s.y}"))})",
                    badFloors == 0 && big >= floors / 2 && shapes.Count == 3);
            }

            // Make sure floor 1 has a big room to test (rebuild with another seed if this one has none).
            for (int seed = 11; !d.AllRooms.Any(r => r.IsBig && r.Type == RoomType.Combat) && seed < 200; seed++)
            {
                d.BuildFloor(1, seed);
                yield return null;
            }
            var rooms = d.AllRooms.ToList();
            Log($"floor 1: {rooms.Count} rooms, " + string.Join(" ", rooms.Where(r => r.Type != RoomType.Combat || r.IsBig).Select(r => $"{r.Type}@{r.Cell.x},{r.Cell.y}({r.Size.x}x{r.Size.y},d{r.Depth})")));
            Check("floor 1 has 10 rooms", rooms.Count == 10);
            Check("floor has start, boss, treasure, shop and rune shrine",
                new[] { RoomType.Start, RoomType.Boss, RoomType.Treasure, RoomType.Shop, RoomType.Shrine }.All(t => rooms.Any(r => r.Type == t)));
            Check("every room is reachable through doors", Reachable(d) == rooms.Count);
            Check("boss room is at least 3 rooms from the start", d.FirstOfType(RoomType.Boss).Depth >= 3);
            Check("run starts in the start room, doors open, no enemies", d.Current?.Type == RoomType.Start && !d.DoorsLocked && Enemy.All.Count == 0);

            // ---- big rooms ----
            {
                var bigRoom = rooms.First(r => r.IsBig && r.Type == RoomType.Combat);
                // No wall or lintel inside the room: every pair of neighbouring cells is open.
                int inner = 0;
                foreach (var c in bigRoom.Cells)
                foreach (var dir in FloorGenerator.Directions)
                {
                    if (!bigRoom.Owns(c + dir)) continue;
                    var from = Room.CellCenter(c) + Vector3.up * 1.5f;
                    var to = Room.CellCenter(c + dir) + Vector3.up * 1.5f;
                    inner += Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), Layers.WorldMask)
                        .Count(h => h.collider.name == "Wall" || h.collider.name == "Lintel");
                }
                var bigDoor = bigRoom.Doors[0];
                Vector3 inward = new Vector3(bigDoor.Dir.x, 0, bigDoor.Dir.y) * (bigDoor.A == bigRoom ? -1f : 1f);
                player.Teleport(bigDoor.Position + inward * 3.5f + Vector3.up * 0.1f, 0f);
                player.LookAt(bigRoom.Center + Vector3.up * 1.5f);
                yield return new WaitForSeconds(3f);
                Check($"a {bigRoom.Size.x}x{bigRoom.Size.y} room is one open space ({inner} walls inside) and holds a bigger fight ({Enemy.All.Count} enemies)",
                    inner == 0 && d.Current == bigRoom && d.DoorsLocked && Enemy.All.Count >= 8);
                Check($"enemies spawn all over the big room ({Enemy.All.Select(e => Room.CellAt(e.transform.position)).Distinct().Count()} cells, {Enemy.All.Count(e => !bigRoom.Contains(e.transform.position))} outside: {string.Join(", ", Enemy.All.Where(e => !bigRoom.Contains(e.transform.position)).Select(e => e.Type.Name))})",
                    Enemy.All.Select(e => Room.CellAt(e.transform.position)).Distinct().Count() >= 2 && Enemy.All.All(e => bigRoom.Contains(e.transform.position)));
                HUD.ShowMap = true;
                yield return Shot("01b_big_room_map");
                HUD.ShowMap = false;
                yield return Shot("01c_big_room");
                gs.ClearEnemies();
                d.ClearCurrent();
                yield return new WaitForSeconds(0.5f);
                ClearPickups();
                player.Teleport(d.FirstOfType(RoomType.Start).Center + new Vector3(0f, 0.1f, -3f), 0f);
                yield return new WaitForSeconds(0.3f);
            }
            Check("run starts knowing only Fireball", SpellBook.Known.Count == 1 && SpellBook.Known[0].Id == "fireball");
            foreach (var p in Prop.All.ToArray()) Destroy(p.gameObject); // keep the test range clear
            yield return Shot("01_start_room");

            // ---- performance with 30 enemies ----
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
            gs.ClearEnemies();
            yield return new WaitForSeconds(1.2f);
            ClearPickups();
            player.Teleport(d.Current.Center + new Vector3(0, 0.1f, -3f), 0f);
            SpellBook.LearnAll();
            Check("all 8 base spells learnable", SpellBook.Known.Count == 8);
            {
                // Boss rooms are taller (big boss sprites fit); other rooms keep the normal ceiling.
                float CeilingOf(Room chR)
                {
                    Vector3 chFrom = chR.Center + new Vector3(0f, 0.5f, 0f);
                    return Physics.Raycast(chFrom, Vector3.up, out var chHit, 30f, Layers.WorldMask, QueryTriggerInteraction.Ignore) ? chHit.point.y : -1f;
                }
                var chBoss = d.AllRooms.FirstOrDefault(chR => chR.Type == RoomType.Boss);
                float chBossH = chBoss != null ? CeilingOf(chBoss) : -1f, chStartH = CeilingOf(d.Rooms[Vector2Int.zero]);
                Check($"the boss room is taller ({chBossH:0.0} m to the ceiling) than other rooms ({chStartH:0.0} m)",
                    Mathf.Abs(chBossH - Dungeon.BossWallH) < 0.1f && Mathf.Abs(chStartH - Dungeon.WallH) < 0.1f);
            }

            // ---- basic bolt ----
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
            gs.ClearEnemies();
            yield return new WaitForSeconds(1.2f);

            // ---- rune gestures ----
            var g = new GestureRecognizer();
            Check("gesture: a U shape with wobble reads ↓ → ↑", Arrows.ToText(DrawPath(g, "DRU", 9f, 0.8f)) == "DRU");
            g.Reset();
            Check("gesture: one long stroke is a single ↓", Arrows.ToText(DrawPath(g, "D", 40f, 0.5f)) == "D");
            g.Reset();
            var diag = new List<Arrow>();
            for (int i = 0; i < 20; i++) { var a = g.Feed(new Vector2(0.5f, 0.45f)); if (a.HasValue) diag.Add(a.Value); }
            Check($"gesture: a diagonal drift adds no stroke ({diag.Count})", diag.Count == 0);
            // Stress: random drawable runes (3-7 strokes, wobbly, including zig-zags) must read back exactly.
            var rng = new System.Random(1234);
            int bad = 0;
            string firstBad = null;
            for (int n = 0; n < 300; n++)
            {
                int len = rng.Next(3, 8);
                var seq = new Arrow[len];
                seq[0] = (Arrow)rng.Next(4);
                for (int i = 1; i < len; i++) { var nx = (Arrow)rng.Next(3); if (nx >= seq[i - 1]) nx++; seq[i] = nx; }
                var rg = new GestureRecognizer();
                var got = new List<Arrow>();
                for (int i = 0; i < len; i++)
                {
                    Vector2 dir = GestureRecognizer.Dir(seq[i]), side = new Vector2(-dir.y, dir.x);
                    int strokeFrames = rng.Next(6, 14); // short and long strokes (overshoot)
                    for (int f = 0; f < strokeFrames; f++)
                    {
                        var r = rg.Feed(dir * 1.1f + side * (float)(rng.NextDouble() * 2 - 1) * 0.45f);
                        if (r.HasValue) got.Add(r.Value);
                    }
                }
                if (Arrows.ToText(got) != Arrows.ToText(seq)) { bad++; firstBad ??= $"{Arrows.Glyphs(seq)} read as {Arrows.Glyphs(got)}"; }
            }
            Check($"gesture: 300 random wobbly runes read back exactly ({bad} misread{(firstBad != null ? ", e.g. " + firstBad : "")})", bad == 0);

            var baseSpells = SpellBook.Defaults();
            Check("every base rune is drawable (no repeated stroke) and prefix-free",
                baseSpells.All(s => !Arrows.HasRepeat(s.Sequence)) &&
                baseSpells.All(x => baseSpells.All(y => x == y || !Arrows.Conflicts(x.Sequence, y.Sequence))));

            // Holding the button: aiming motion right after pressing is ignored, and a brief slip of the
            // button (or a dropped input frame) doesn't wipe the half-drawn rune.
            events.Clear();
            const float frame = 1f / 60f;
            for (int f = 0; f < 4; f++) ss.ProcessChannelInput(true, new Vector2(-1.5f, 0.3f), frame); // still aiming left
            foreach (var a in Arrows.Parse("DR"))
                for (int f = 0; f < 8; f++) ss.ProcessChannelInput(true, GestureRecognizer.Dir(a) * 1.1f, frame);
            for (int f = 0; f < 5; f++) ss.ProcessChannelInput(false, Vector2.zero, frame);            // 80 ms slip
            bool keptRune = ss.Channeling && ss.Entered.Count == 2;
            for (int f = 0; f < 8; f++) ss.ProcessChannelInput(true, Vector2.up * 1.1f, frame);
            Check($"aiming motion at the start is ignored and a brief button slip keeps the rune (kept {keptRune})",
                keptRune && ss.Readied?.Name == "Fireball" && !events.Contains("fizzle"));
            for (int f = 0; f < 20; f++) ss.ProcessChannelInput(false, Vector2.zero, frame);
            Check("letting go for real ends channeling", !ss.Channeling);
            ss.ClearReadied();

            // Finishing a rune and carrying on with the last stroke must not enter that stroke again.
            events.Clear();
            ss.SetChanneling(true);
            foreach (var a in Arrows.Parse("DRU"))
                for (int f = 0; f < 8; f++) ss.FeedGesture(GestureRecognizer.Dir(a) * 1.1f);
            bool readied = ss.Readied != null && ss.Readied.Name == "Fireball";
            for (int f = 0; f < 15; f++) ss.FeedGesture(Vector2.up * 1.1f); // overshoot upwards
            Check($"overshooting a rune's last stroke doesn't repeat it (entered {ss.Entered.Count})",
                readied && ss.Entered.Count == 0 && !events.Contains("fizzle") && ss.Readied?.Name == "Fireball");
            ss.SetChanneling(false);
            ss.ClearReadied();
            events.Clear();

            // ---- Fireball: projectile + explosion ----
            var group = new List<Enemy>
            {
                SpawnAhead(player, EnemyType.Imp, 9f, 0f), SpawnAhead(player, EnemyType.Imp, 9.8f, 1.1f), SpawnAhead(player, EnemyType.Imp, 9.6f, -1.1f),
            };
            foreach (var e in group) e.Freeze(5f);
            yield return new WaitForSeconds(0.3f);
            player.LookAt(group[0].Center);
            yield return CastSequence("DRU", expectReady: "Fireball", showChannel: "04_channel_fireball");
            Check("Fireball readied by drawing ↓→↑ with the mouse", ss.Readied != null && ss.Readied.Name == "Fireball");
            yield return Shot("05_fireball_ready");
            ss.CastReadied();
            yield return new WaitForSeconds(0.85f);
            Check("Fireball explosion killed 3 imps", group.Count(e => e && e.IsAlive) == 0);
            Check("Fireball on cooldown (3s)", ss.OnCooldown(Spell("Fireball")));

            // ---- cooldown blocks readying; wrong input fizzles ----
            yield return CastSequence("DRU", expectReady: null);
            Check($"Fireball on cooldown cannot be readied (says \"{ss.LastMessage}\")", ss.Readied == null && events.Contains("cooldown Fireball") && ss.LastMessage.StartsWith("Spell on CD"));
            yield return new WaitForSeconds(0.35f);
            events.Clear();
            yield return CastSequence("UL", expectReady: null);
            Check("wrong rune ↑← fizzles with lockout", events.Contains("fizzle") && ss.Lockout > 0f);
            ss.PushArrow(Arrow.Up);
            Check("input ignored during 0.3s lockout", ss.Entered.Count == 0);
            ss.SetChanneling(false);
            yield return new WaitForSeconds(0.4f);

            // ---- Lightning: hitscan + chain ----
            var line = new List<Enemy>
            {
                SpawnAhead(player, EnemyType.Imp, 8f, 0f), SpawnAhead(player, EnemyType.Imp, 10f, 2.5f), SpawnAhead(player, EnemyType.Imp, 11f, -2.5f),
            };
            foreach (var e in line) e.Freeze(5f);
            yield return new WaitForSeconds(0.3f);
            player.LookAt(line[0].Center);
            yield return CastSequence("RDR", expectReady: "Lightning");
            ss.CastReadied();
            yield return null;
            yield return Shot("06_lightning");
            yield return new WaitForSeconds(0.4f);
            int lightningKills = line.Count(e => !e || !e.IsAlive);
            Check($"Lightning hit primary and chained ({lightningKills}/3 killed)", lightningKills >= 2);
            gs.ClearEnemies();
            yield return new WaitForSeconds(1.2f);

            // ---- Frost Nova ----
            var ring = new List<Enemy>();
            for (int i = 0; i < 5; i++)
            {
                float ang = i * 72f * Mathf.Deg2Rad;
                ring.Add(EnemySpawner.Instance.SpawnAt(i % 2 == 0 ? EnemyType.Imp : EnemyType.Cultist,
                    player.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 3.5f));
            }
            yield return new WaitForSeconds(0.4f);
            yield return CastSequence("DLUR", expectReady: "Frost Nova");
            ss.CastReadied();
            yield return new WaitForSeconds(0.15f);
            Check("Frost Nova froze all 5 nearby enemies", ring.All(e => e && e.IsFrozen));

            // ---- Heal ----
            player.Heal(player.MaxHP);
            player.God = false;
            player.TakeDamage(50f);
            float before = player.HP;
            yield return CastSequence("UDUD", expectReady: "Heal");
            ss.CastReadied();
            yield return null;
            Check($"Heal restored 25 HP ({before:0} -> {player.HP:0})", Mathf.Approximately(player.HP, Mathf.Min(player.MaxHP, before + 25f)));
            player.God = true;
            gs.ClearEnemies();
            // Out of combat the cooldown is frozen; it resumes once enemies are around.
            yield return null;
            float healCd = ss.CooldownRemaining(Spell("Heal"));
            yield return new WaitForSeconds(1f);
            Check($"cooldowns pause out of combat ({healCd:0.0}s -> {ss.CooldownRemaining(Spell("Heal")):0.0}s)",
                !SpellSystem.CooldownsTick && healCd > 0f && Mathf.Approximately(ss.CooldownRemaining(Spell("Heal")), healCd));
            var idle = SpawnAhead(player, EnemyType.Imp, 12f, 0f);
            idle.Freeze(5f);
            yield return new WaitForSeconds(1f);
            Check($"cooldowns run again in combat ({healCd:0.0}s -> {ss.CooldownRemaining(Spell("Heal")):0.0}s)",
                SpellSystem.CooldownsTick && ss.CooldownRemaining(Spell("Heal")) < healCd - 0.5f);
            gs.ClearEnemies();

            // ---- ranged enemies walk around cover to find you ----
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "TestCover";
                wall.layer = Layers.World;
                wall.transform.position = AheadPoint(player, 2.2f, 0f) + Vector3.up * 2f;
                wall.transform.rotation = Quaternion.LookRotation(player.transform.forward);
                wall.transform.localScale = new Vector3(4f, 4f, 0.5f);
                var hunter = SpawnAhead(player, EnemyType.Cultist, 10f, 0f);
                yield return new WaitForSeconds(0.5f);
                bool hiddenAtStart = !hunter.SeesPlayer;
                Vector3 startPos = hunter.transform.position;
                int shotsBefore = hunter.AttacksDone;
                float coverWait = 0f;
                while (coverWait < 10f && hunter && hunter.AttacksDone == shotsBefore) { coverWait += Time.deltaTime; yield return null; }
                yield return Shot("05b_cultist_flanked");
                float moved = hunter ? Vector3.Distance(startPos, hunter.transform.position) : 0f;
                Check($"a cultist behind cover walks around it and shoots again (hidden {hiddenAtStart}, moved {moved:0.0} m, {coverWait:0.0} s)",
                    hiddenAtStart && hunter && hunter.AttacksDone > shotsBefore && moved > 1.5f);
                UnityEngine.Object.Destroy(wall);
                gs.ClearEnemies();
                yield return null;
            }

            // ---- new enemy types ----
            {
                // Line-up screenshot of every regular enemy.
                var ntLine = new List<Enemy>();
                for (int i = 0; i < EnemyType.Regulars.Length; i++)
                {
                    var ntE = SpawnAhead(player, EnemyType.Regulars[i], 7.5f, (i - (EnemyType.Regulars.Length - 1) * 0.5f) * 1.9f);
                    ntE.Freeze(3f);
                    ntLine.Add(ntE);
                }
                yield return new WaitForSeconds(0.4f);
                foreach (var ntE in ntLine) ntE.ConsumeFreeze();
                yield return Shot("05c_enemy_lineup");
                gs.ClearEnemies();
                yield return null;

                // Flame Skull: rushes in and explodes.
                player.Heal(player.MaxHP);
                player.God = false;
                float ntHp = player.HP;
                var ntSkull = SpawnAhead(player, EnemyType.Skull, 9f, 0f);
                float ntT = 0f;
                while (ntT < 5f && ntSkull && ntSkull.IsAlive) { ntT += Time.deltaTime; yield return null; }
                Check($"Flame Skull rushes you and explodes ({ntHp:0} -> {player.HP:0} HP, {ntT:0.0} s)", ntSkull && !ntSkull.IsAlive && ntSkull.SelfDestructed && player.HP < ntHp);
                player.God = true;
                player.Heal(player.MaxHP);
                gs.ClearEnemies();
                yield return null;

                // ...killed at range, its blast hurts its neighbours instead.
                var ntSkull2 = SpawnAhead(player, EnemyType.Skull, 10f, 0f);
                var ntImp = SpawnAhead(player, EnemyType.Imp, 10.6f, 1.2f);
                ntSkull2.Freeze(5f);
                ntImp.Freeze(5f);
                yield return new WaitForSeconds(0.2f);
                ntSkull2.TakeDamage(999f, Element.Arcane);
                yield return null;
                Check($"a Flame Skull killed at range blows up its neighbours ({ntImp.HP:0}/{ntImp.MaxHP:0} HP left)", ntImp.HP < ntImp.MaxHP && !ntSkull2.SelfDestructed);
                gs.ClearEnemies();
                yield return null;

                // Horned Beetle: braces, then charges and rams you.
                player.God = false;
                player.Heal(player.MaxHP);
                ntHp = player.HP;
                var ntBeetle = SpawnAhead(player, EnemyType.Beetle, 14f, 0f); // far enough that its first charge is always ready before it is too close
                yield return null;
                float ntTop = 0f;
                bool ntCharged = false;
                Vector3 ntLast = ntBeetle.transform.position;
                ntT = 0f;
                while (ntT < 7f && player.HP >= ntHp)
                {
                    yield return null;
                    ntT += Time.deltaTime;
                    var ntPos = ntBeetle.transform.position;
                    if (Time.deltaTime > 0f) ntTop = Mathf.Max(ntTop, Vector3.Distance(ntPos, ntLast) / Time.deltaTime);
                    ntLast = ntPos;
                    ntCharged |= ntBeetle.IsDashing;
                }
                Check($"Horned Beetle charges ({ntTop:0.0} m/s) and rams you ({ntHp:0} -> {player.HP:0} HP)", ntCharged && ntTop > 9f && player.HP < ntHp);
                player.God = true;
                player.Heal(player.MaxHP);
                gs.ClearEnemies();
                yield return null;

                // ...and a wall in its path dazes it.
                var ntB2 = SpawnAhead(player, EnemyType.Beetle, 14f, 0f);
                ntT = 0f;
                while (ntT < 7f && !ntB2.IsDashing) { ntT += Time.deltaTime; yield return null; }
                Vector3 ntDir = player.transform.position - ntB2.transform.position;
                ntDir.y = 0f;
                ntDir.Normalize();
                var ntWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ntWall.name = "TestCover";
                ntWall.layer = Layers.World;
                ntWall.transform.position = ntB2.transform.position + ntDir * 2.6f + Vector3.up * 2f;
                ntWall.transform.rotation = Quaternion.LookRotation(ntDir);
                ntWall.transform.localScale = new Vector3(4f, 4f, 0.6f);
                ntT = 0f;
                while (ntT < 2f && !ntB2.IsStunned) { ntT += Time.deltaTime; yield return null; }
                Check($"a charging beetle that hits a wall is dazed ({ntB2.LastEvent})", ntB2.IsStunned && ntB2.LastEvent == "dazed" && !ntB2.IsCharging);
                UnityEngine.Object.Destroy(ntWall);
                gs.ClearEnemies();
                yield return null;

                // Hornet: bursts of three stingers.
                var ntHornet = SpawnAhead(player, EnemyType.Hornet, 9f, 0f);
                ntT = 0f;
                while (ntT < 6f && ntHornet.ShotsFired < 3) { ntT += Time.deltaTime; yield return null; }
                Check($"Hornet fires a burst of 3 stingers ({ntHornet.ShotsFired} shots from {ntHornet.AttacksDone} attack)", ntHornet.ShotsFired >= 3 && ntHornet.AttacksDone == 1);
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.3f);
                ClearPickups();

                // Shade: splits into two shadelings, which drop nothing.
                var ntShade = SpawnAhead(player, EnemyType.Shade, 8f, 0f);
                ntShade.Freeze(3f);
                yield return new WaitForSeconds(0.2f);
                ntShade.TakeDamage(999f);
                yield return null;
                var ntLings = Enemy.All.Where(x => x.Type == EnemyType.Shadeling).ToList();
                Check($"Shade splits into 2 shadelings ({ntLings.Count})", ntLings.Count == 2);
                int ntPickups = Pickup.All.Count;
                foreach (var l in ntLings) l.TakeDamage(999f);
                yield return new WaitForSeconds(0.3f);
                Check($"shadelings drop no loot ({Pickup.All.Count - ntPickups} new pickups)", Pickup.All.Count == ntPickups && Enemy.All.Count == 0);
                ClearPickups();

                // Encounter tables: each floor has its own mix.
                var ntF1 = new HashSet<EnemyType>();
                var ntF2 = new HashSet<EnemyType>();
                var ntF3 = new HashSet<EnemyType>();
                for (int i = 0; i < 3000; i++)
                {
                    ntF1.Add(EnemyType.PickForEncounter(1, 4, () => UnityEngine.Random.value, 0));
                    ntF2.Add(EnemyType.PickForEncounter(2, 4, () => UnityEngine.Random.value, 0));
                    ntF3.Add(EnemyType.PickForEncounter(3, 4, () => UnityEngine.Random.value, 0));
                }
                Check($"floor 1 rooms: {string.Join(", ", ntF1.Select(x => x.Name))}; floor 2: {string.Join(", ", ntF2.Select(x => x.Name))}; floor 3: {string.Join(", ", ntF3.Select(x => x.Name))}",
                    ntF1.Count == 4 && !ntF1.Contains(EnemyType.Skull) && ntF2.Contains(EnemyType.Slug) && ntF2.Contains(EnemyType.Frog) && !ntF2.Contains(EnemyType.Skull)
                    && ntF3.Contains(EnemyType.Crawler) && ntF3.Contains(EnemyType.Mimic) && !ntF3.Contains(EnemyType.Cultist));
                int ntMimics = 0;
                var ntCount = new Dictionary<EnemyType, int>();
                for (int i = 0; i < 40; i++)
                {
                    var ntPick = EnemyType.PickForEncounter(3, 4, () => UnityEngine.Random.value, x => ntCount.TryGetValue(x, out int ntC) ? ntC : 0);
                    ntCount[ntPick] = (ntCount.TryGetValue(ntPick, out int ntH) ? ntH : 0) + 1;
                    if (ntPick == EnemyType.Mimic) ntMimics++;
                }
                Check($"at most one mimic per room ({ntMimics} in 40 picks)", ntMimics <= 1);

                // Dev mode: hitbox wireframes over enemies, projectiles and spell areas.
                bool dvWasOn = Settings.DevMode;
                DevMode.Toggle();
                var dvTypes = new[] { EnemyType.Imp, EnemyType.Cultist, EnemyType.Beetle, EnemyType.Skull };
                for (int i = 0; i < dvTypes.Length; i++)
                    SpawnAhead(player, dvTypes[i], 8f, (i - 1.5f) * 2.6f).Freeze(5f);
                int dvRec = DevMode.Recorded;
                Vector3 dvAt = player.transform.position + player.transform.forward * 5f + Vector3.up * 1f;
                Combat.Explode(dvAt + player.transform.right * 2.5f, 2.5f, 0f, 0f, Combat.BombColor);
                var dvShot = Projectile.Spawn(dvAt - player.transform.right * 1.5f, player.transform.forward * 0.5f, Color.yellow, 0.4f, 0.3f, Layers.PlayerShotMask, null, life: 3f);
                yield return null;
                yield return null;
                yield return Shot("05d_dev_hitboxes");
                Check($"dev mode (F3) draws hitboxes ({DevMode.Recorded - dvRec} spell area, {Projectile.All.Count} projectile, {Enemy.All.Count} enemies)",
                    !dvWasOn && Settings.DevMode && DevMode.Recorded > dvRec && Projectile.All.Contains(dvShot));
                DevMode.Toggle();
                int dvOff = DevMode.Recorded;
                Combat.Explode(dvAt, 1f, 0f, 0f, Combat.BombColor);
                Check("dev mode off records nothing", !Settings.DevMode && DevMode.Recorded == dvOff);
                if (dvShot) UnityEngine.Object.Destroy(dvShot.gameObject);
                gs.ClearEnemies();
                yield return null;

                // ---- playtest fixes ----
                // #1 Flying enemies: the hitbox sits on the sprite, so bolts at the sprite hit and bolts at the floor under it miss.
                DevMode.Toggle();
                var fxTypes = new[] { EnemyType.Hornet, EnemyType.Cultist, EnemyType.Skull }; // the skull last: it explodes when killed
                var fxFlyers = new List<Enemy>();
                for (int i = 0; i < fxTypes.Length; i++)
                {
                    var fe = SpawnAhead(player, fxTypes[i], 8f, (i - 1f) * 3f);
                    fe.Freeze(8f);
                    fxFlyers.Add(fe);
                }
                yield return new WaitForSeconds(0.2f);
                player.LookAt(fxFlyers[0].Center);
                yield return Shot("05e_flyer_hitboxes");
                bool fxAligned = fxFlyers.All(f => Vector3.Distance(f.Hitbox.bounds.center, f.Center) < 0.05f);
                bool fxRayHits = true, fxFloorMisses = true;
                Vector3 fxEye = player.Cam.transform.position;
                foreach (var f in fxFlyers)
                {
                    fxRayHits &= Physics.Raycast(fxEye, (f.Center - fxEye).normalized, out var fh, 30f, Layers.PlayerShotMask, QueryTriggerInteraction.Ignore)
                                 && fh.collider.GetComponentInParent<Enemy>() == f;
                    Vector3 fxFeet = f.transform.position + Vector3.up * 0.25f;
                    bool hitFeet = Physics.Raycast(fxEye, (fxFeet - fxEye).normalized, out var fh2, 30f, Layers.PlayerShotMask, QueryTriggerInteraction.Ignore)
                                   && fh2.collider.GetComponentInParent<Enemy>() == f;
                    if (f.Type.Hover >= 0.9f) fxFloorMisses &= !hitFeet;
                }
                Check($"flying enemies' hitboxes sit on their sprites ({string.Join(", ", fxFlyers.Select(f => f.Type.Name))})", fxAligned && fxRayHits);
                Check("shots at the floor under a flying enemy miss it", fxFloorMisses);
                // A real magic bolt aimed at each sprite hits it.
                bool fxBoltsHit = true;
                foreach (var f in fxFlyers)
                {
                    float fxHp = f.HP;
                    player.LookAt(f.Center);
                    yield return null;
                    ss.FireBasic();
                    float fxT = 0f;
                    while (fxT < 1f && f && f.IsAlive && f.HP >= fxHp) { fxT += Time.deltaTime; yield return null; }
                    fxBoltsHit &= !f || !f.IsAlive || f.HP < fxHp;
                    yield return new WaitForSeconds(0.3f);
                }
                Check("magic bolts aimed at a Hornet, Cultist and Flame Skull hit them", fxBoltsHit);
                DevMode.Toggle();
                gs.ClearEnemies();
                yield return null;

                // #2 Flying enemies ignore spike traps; walking ones don't.
                var fxHornet = SpawnAhead(player, EnemyType.Hornet, 6f, -2f);
                var fxImp = SpawnAhead(player, EnemyType.Imp, 6f, 2f);
                yield return new WaitForSeconds(0.2f);
                fxHornet.Freeze(8f);
                fxImp.Freeze(8f);
                var fxTrapH = SpikeTrap.Create(Dungeon.Instance.Current.Root, fxHornet.transform.position, 0.5f);
                var fxTrapI = SpikeTrap.Create(Dungeon.Instance.Current.Root, fxImp.transform.position, 0.5f);
                yield return new WaitForSeconds(SpikeTrap.Cycle * 2f + 0.3f);
                Check($"spike traps skip Flying enemies (Hornet {fxHornet.HP:0}/{fxHornet.MaxHP:0}) but hurt walkers (Imp {fxImp.HP:0}/{fxImp.MaxHP:0})",
                    EnemyType.Hornet.Flying && fxHornet.HP == fxHornet.MaxHP && (!fxImp || fxImp.HP < fxImp.MaxHP));
                UnityEngine.Object.Destroy(fxTrapH.gameObject);
                UnityEngine.Object.Destroy(fxTrapI.gameObject);
                gs.ClearEnemies();
                yield return null;

                // #3 Losing window focus pauses the run; a dropped render texture is rebuilt.
                gs.SetFocus(false);
                yield return null;
                yield return null;
                var pmMenu = MainMenu.Instance;
                bool fxPaused = gs.Paused && gs.FocusPaused && Time.timeScale == 0f;
                var pmLabels = string.Join(", ", pmMenu.LabelsForTest());
                bool pmFirst = pmMenu.PauseUp && pmMenu.Current == MainMenu.Page.Pause && pmLabels == "Resume, Options, Save & quit";
                Check($"pausing opens the pause menu ({pmLabels})", pmFirst);
                yield return Shot("05i_pause_menu");
                string pmLayout1 = pmMenu.LayoutProblem;
                pmMenu.ChooseForTest("Options");
                bool pmOptions = pmMenu.Current == MainMenu.Page.Options && pmMenu.LabelsForTest().Contains("Casting style");
                yield return Shot("05j_pause_options");
                string pmLayout2 = pmMenu.LayoutProblem;
                Check($"pause menu and its Options page lay out cleanly ({pmLayout1 ?? "ok"}, {pmLayout2 ?? "ok"})", pmLayout1 == null && pmLayout2 == null);
                pmMenu.ChooseForTest("Back");
                Check("pause menu: Options opens the settings, Back returns to the pause menu",
                    pmOptions && pmMenu.Current == MainMenu.Page.Pause && gs.Paused);
                float pmBefore = SaveSystem.LastSaveTime;
                yield return null;
                bool pmSaved = MainMenu.SaveForQuit();
                var pmD = Dungeon.Instance;
                Check($"pause menu: Save & quit saves the run (checkpoint {pmD.CheckpointRoomId}, in a fight: {pmD.InCombat})",
                    pmSaved && SaveSystem.HasValidSave && SaveSystem.LastSaveTime > pmBefore && (pmD.InCombat || pmD.CheckpointRoomId == pmD.Current.Id));
                pmMenu.ChooseForTest("Resume");
                yield return null;
                Check("losing window focus pauses the game; Resume goes back to it", fxPaused && !gs.Paused && Time.timeScale == 1f && pmMenu.Current == MainMenu.Page.Main);

                // A Steam invite accepted during a run pauses it and asks first (no join under the player).
                var ivNs = Net.NetSession.Instance;
                ivNs.AcceptInvite(new Steamworks.CSteamID(109775240000000001UL), "Tester");
                yield return null;
                yield return null;
                var ivLabels = string.Join(", ", pmMenu.LabelsForTest());
                bool ivAsk = gs.Paused && pmMenu.Current == MainMenu.Page.Pause && ivLabels.StartsWith("Save & join, Ignore invite") && pmMenu.HintForTest().Contains("Tester invited you") && !ivNs.InSession && gs.InRun;
                yield return Shot("05k_invite_in_run");
                string ivLayout = pmMenu.LayoutProblem;
                pmMenu.ChooseForTest("Ignore invite");
                yield return null;
                var ivAfter = string.Join(", ", pmMenu.LabelsForTest());
                Check($"an invite during a run pauses and asks ({ivLabels}); Ignore keeps the run ({ivAfter}, layout {ivLayout ?? "ok"})",
                    ivAsk && ivLayout == null && ivNs.PendingInvite == Steamworks.CSteamID.Nil && ivAfter == "Resume, Options, Save & quit" && gs.InRun);
                pmMenu.ChooseForTest("Resume");
                yield return null;

                // Hit direction: a red arc around the crosshair points at an attacker you can't see.
                var hdStartPos = player.transform.position;
                float hdStartYaw = player.Yaw;
                DamageIndicator.Clear();
                int hdAdded = DamageIndicator.Added;
                float hdHp = player.HP;
                var hdFwd = player.Cam.transform.forward; hdFwd.y = 0f; hdFwd.Normalize();
                var hdRightDir = Vector3.Cross(Vector3.up, hdFwd);
                var hdBase = player.transform.position;
                var hdBehind = hdBase - hdFwd * 5f;
                player.TakeDamage(1f, hdBehind, 9001);
                float hdA0 = DamageIndicator.AngleForTest(player, 9001);
                player.TakeDamage(1f, hdBehind + hdRightDir * 0.3f, 9001); // the same attacker again: one arc
                player.TakeDamage(1f, hdBase + hdFwd * 5f, 9002);          // in view: no arc (default: outside view only)
                player.TakeDamage(1f);                                     // a trap (no source): no arc
                int hdAfterSame = DamageIndicator.Count;
                player.TakeDamage(4f, hdBase + hdRightDir * 5f, 9003);    // another attacker on your right
                float hdA3 = DamageIndicator.AngleForTest(player, 9003);
                yield return null;
                yield return Shot("28_damage_indicator");
                Check($"a hit from behind draws an arc pointing back ({hdA0:0} deg); the same attacker refreshes it, in-view and trap hits draw none ({hdAfterSame} arc); another attacker on the right adds one at {hdA3:0} deg ({DamageIndicator.Count} arcs)",
                    Mathf.Abs(Mathf.Abs(hdA0) - 180f) < 12f && hdAfterSame == 1 && Mathf.Abs(hdA3 - 90f) < 12f && DamageIndicator.Count == 2 && DamageIndicator.Added == hdAdded + 2);
                player.LookAt(hdBase + hdRightDir * 5f + Vector3.up * 1.4f);
                yield return null;
                float hdTurned = DamageIndicator.AngleForTest(player, 9003), hdBack = DamageIndicator.AngleForTest(player, 9001);
                Check($"turning keeps the arcs on the attackers (right one now {hdTurned:0} deg, the one behind now {hdBack:0} deg)",
                    Mathf.Abs(hdTurned) < 12f && Mathf.Abs(hdBack - 90f) < 15f); // facing right, the one behind is on your right
                yield return new WaitForSeconds(DamageIndicator.Lifetime + 0.15f);
                Check($"the arcs fade out after about {DamageIndicator.Lifetime:0.0} s ({DamageIndicator.Count} left)", DamageIndicator.Count == 0);
                player.Heal(player.MaxHP, quiet: true);
                if (player.HP < hdHp) player.Heal(hdHp - player.HP, quiet: true);

                // ---- Cooldowns (playtest 0.20): only spells that heal keep one (cooldowns.json) ----
                CooldownConfig.UseDefaultsForTest();
                string cdList = string.Join(", ", SpellBook.Defaults().Select(sp => $"{sp.Id} {CooldownConfig.BaseCooldown(sp):0.#}"));
                bool cdRule = SpellBook.Defaults().All(sp => CooldownConfig.HasCooldown(sp) == (sp.Id == "heal" || sp.Id == "lifedrain" || sp.Id == SelfSpellConfig.QuickeningId));
                SpellForge.TryCombine(SpellBook.CreateBase("heal"), SpellBook.CreateBase("fireball"), new List<SpellDefinition>(), out var cdFusedHeal, out _);
                SpellForge.TryCombine(SpellBook.CreateBase("frostshard"), SpellBook.CreateBase("fireball"), new List<SpellDefinition>(), out var cdFusedHit, out _);
                bool cdForged = cdFusedHeal != null && CooldownConfig.HasCooldown(cdFusedHeal) && cdFusedHit != null && !CooldownConfig.HasCooldown(cdFusedHit);
                CooldownConfig.Overrides["meteor"] = 14f;
                bool cdOverride = Mathf.Approximately(CooldownConfig.BaseCooldown(SpellBook.CreateBase("meteor")), 14f);
                CooldownConfig.Overrides.Clear();
                Check($"only spells that heal keep a cooldown ({cdList}); forged spells follow their effects (heal+fireball: {cdForged}); cooldowns.json can give one back (meteor 14 s: {cdOverride})",
                    cdRule && cdForged && cdOverride);

                var cdSs = SpellSystem.Instance;
                var cdFire = SpellBook.Known.FirstOrDefault(sp => sp.Id == "fireball");
                bool cdTwice = cdFire != null && cdSs.CastForTest(cdFire) && !cdSs.OnCooldown(cdFire) && cdSs.CastForTest(cdFire) && !cdSs.OnCooldown(cdFire);
                Check("Fireball casts twice in a row (no cooldown)", cdTwice);

                // Blink Dash: no cooldown, but chained dashes don't keep you untouchable.
                player.QueueDash(3f, 0f);
                yield return null;
                yield return null;
                bool cdD1 = player.LastDashIFrames && player.Invulnerable;
                yield return new WaitForSeconds(0.4f);
                player.QueueDash(3f, 0f);
                yield return null;
                yield return null;
                bool cdD2 = !player.LastDashIFrames && !player.Invulnerable;
                yield return new WaitForSeconds(CooldownConfig.DashIFrameLockout + 0.5f);
                player.QueueDash(3f, 0f);
                yield return null;
                yield return null;
                bool cdD3 = player.LastDashIFrames && player.Invulnerable;
                Check($"Blink Dash i-frames: the first dash ({cdD1}), not one 0.4 s later ({cdD2}), again {CooldownConfig.DashIFrameLockout:0.0} s after the last i-frames ended ({cdD3})", cdD1 && cdD2 && cdD3);
                yield return new WaitForSeconds(0.4f);

                // Freeze and stun: no chaining. Bosses get half.
                var cdImp = SpawnAhead(player, EnemyType.Imp, 6f, 0f);
                yield return null;
                cdImp.Freeze(3f);
                bool cdFrozen = cdImp.IsFrozen;
                cdImp.Freeze(3f);
                cdImp.ConsumeFreeze();
                cdImp.Stun(1f);
                bool cdImmune = !cdImp.IsStunned && !cdImp.IsFrozen && cdImp.CcResisted == 2;
                float cdBoss = Enemy.CcFor(EnemyType.Boss, 4f);
                Check($"freeze and stun can't be chained: frozen {cdFrozen}; another freeze while frozen and a stun right after it ends are shrugged off ({cdImp.CcResisted} resisted); a boss gets {cdBoss:0.#} s of a 4 s freeze",
                    cdFrozen && cdImmune && Mathf.Approximately(cdBoss, 2f));
                gs.ClearEnemies();

                // Grimoire cards on a spell without a cooldown: Quicken is a burst of speed, Overload a casting lock.
                float cdSpeed0 = player.Run.MoveSpeed;
                float cdQ = SpellSystem.CardCooldown(0f, false, new CastRequest { ResetCooldown = true }, player, out float cdQLock);
                float cdSpeed1 = player.Run.MoveSpeed;
                var cdOv = new CastRequest();
                cdOv.CooldownMult = Grimoire.GrimoireConfig.OverloadCooldownMult;
                SpellSystem.CardCooldown(0f, false, cdOv, player, out float cdOvLock);
                float cdHealOv = SpellSystem.CardCooldown(15f, true, cdOv, player, out float cdHealLock);
                float cdHealQ = SpellSystem.CardCooldown(15f, true, new CastRequest { ResetCooldown = true }, player, out _);
                Check($"cards on a spell without a cooldown: Quicken gives +{(cdSpeed1 / cdSpeed0 - 1f) * 100f:0}% speed, Overload a {cdOvLock:0.0} s casting lock; on Heal they still drop it ({cdHealQ:0}) or double it ({cdHealOv:0} s, no lock)",
                    cdQ == 0f && cdQLock == 0f && cdSpeed1 > cdSpeed0 * 1.25f && Mathf.Approximately(cdOvLock, Grimoire.GrimoireConfig.OverloadCastLock)
                    && cdHealQ == 0f && Mathf.Approximately(cdHealOv, 30f) && cdHealLock == 0f);
                player.Run.SprintTimer = 0f;
                CooldownConfig.UseLegacyForTest();
                player.Teleport(hdStartPos, hdStartYaw); // the dashes and turns above: back where the next checks expect you
                yield return null;
                RetroView.DropForTest();
                bool fxDropped = !RetroView.Ready;
                yield return null;
                yield return null;
                Check("a lost render texture is recreated (no white screen)", fxDropped && RetroView.Ready);

                // #8 Healing pulses the health bar (with +N) instead of flashing the screen.
                player.God = false;
                player.TakeDamage(30f);
                player.Heal(18f);
                Check($"healing shows +{player.LastHeal:0} on the health bar", Mathf.Approximately(player.LastHeal, 18f) && player.HealFlash > 0f);
                yield return Shot("05f_heal_bar");
                player.God = true;
                player.Heal(player.MaxHP);

                // #10 Aiming at an explosive barrel shows its blast radius on the floor.
                var fxBarrel = Prop.Create(PropKind.Barrel, Dungeon.Instance.Current.Root, AheadPoint(player, 7f, 0f));
                player.LookAt(fxBarrel.Center);
                yield return new WaitForSeconds(0.4f);
                Check("aiming at a barrel shows its blast radius ring", Prop.Aimed == fxBarrel && fxBarrel.ShowingBlastRing);
                yield return Shot("05g_barrel_radius");
                UnityEngine.Object.Destroy(fxBarrel.gameObject);
                var fxNova = SpellBook.Known.FirstOrDefault(s => s.Name == "Frost Nova");
                var fxBolt = SpellBook.Known.FirstOrDefault(s => s.Name == "Lightning");
                Check($"area spells are tagged ({fxNova?.Name}: {fxNova?.AreaTag}; {fxBolt?.Name}: none)",
                    fxNova != null && fxNova.AreaTag != null && fxNova.AreaTag.StartsWith("AREA") && fxBolt != null && fxBolt.AreaTag == null);
                yield return null;
            }
            yield return new WaitForSeconds(1f);

            // ---- new base spells ----
            var shardTarget = SpawnAhead(player, EnemyType.Cultist, 8f, 0f);
            yield return new WaitForSeconds(0.3f);
            shardTarget.Freeze(0.6f); // hold still for aiming; only the shard's own freeze lasts past the check
            player.LookAt(shardTarget.Center);
            yield return CastSequence("LUR", expectReady: "Frost Shard");
            ss.CastReadied();
            yield return new WaitForSeconds(0.6f);
            Check("Frost Shard hits and freezes", shardTarget && shardTarget.IsFrozen && shardTarget.HP < shardTarget.MaxHP);
            // Drawing a rune whose spell is cooling down says so, instead of a plain "Fizzle!".
            var cdEvents = new List<string>();
            System.Action<string> cdHook = cdE => cdEvents.Add(cdE);
            ss.Event += cdHook;
            yield return CastSequence("LUR", expectReady: null);
            string cdMsg = ss.LastMessage;
            yield return Shot("05m_spell_on_cd");
            ss.SetChanneling(false);
            ss.Event -= cdHook;
            Check($"drawing the rune of a spell on cooldown says \"{cdMsg}\" instead of Fizzle (events: {string.Join(", ", cdEvents)})",
                cdMsg != null && cdMsg.StartsWith("Spell on CD") && cdEvents.Any(x => x.StartsWith("cooldown Frost Shard")) && !cdEvents.Contains("fizzle"));
            var pushed = SpawnAhead(player, EnemyType.Imp, 3f, 0f);
            yield return new WaitForSeconds(0.2f);
            pushed.Freeze(2f);
            Vector3 p0 = pushed.transform.position;
            yield return CastSequence("URD", expectReady: "Thunder Ring");
            ss.CastReadied();
            yield return new WaitForSeconds(0.4f);
            Check("Thunder Ring damages and knocks back", !pushed || !pushed.IsAlive || (pushed.transform.position - player.transform.position).magnitude > (p0 - player.transform.position).magnitude + 1f);
            gs.ClearEnemies();
            yield return new WaitForSeconds(1f);

            // ---- Arcane Essence: drops ----
            {
                // Rolls: a normal enemy drops ~23% of the time with bad-luck protection, never 30 kills dry.
                int esRolls0 = run.EssenceRolls, esLuck0 = run.EssenceBadLuck;
                run.EssenceBadLuck = 0;
                int esDrops = 0, esStreak = 0, esLongest = 0, esN = 3000;
                for (int i = 0; i < esN; i++)
                {
                    int got = Essence.Roll(run, EssenceConfig.Normal);
                    if (got > 0) { esDrops++; esStreak = 0; } else esLongest = Mathf.Max(esLongest, ++esStreak);
                }
                float esRate = esDrops / (float)esN;
                Check($"Essence drop rate for normal enemies is about 23% with bad-luck protection ({esRate:P1}; longest dry run {esLongest} kills; gold drops from 40%+)",
                    esRate > 0.18f && esRate < 0.29f && esLongest <= 29);
                // The same run seed and roll count give the same roll (a loaded save can't reroll).
                run.EssenceRolls = 77; run.EssenceBadLuck = 2;
                int esA = Essence.Roll(run, EssenceConfig.Elite);
                run.EssenceRolls = 77; run.EssenceBadLuck = 2;
                int esB = Essence.Roll(run, EssenceConfig.Elite);
                int esBoss = Essence.Roll(run, EssenceConfig.Boss);
                Check($"Essence rolls come from the run seed (same roll twice: {esA} = {esB}); a boss always drops 3-5 ({esBoss})",
                    esA == esB && esBoss >= 3 && esBoss <= 5);
                run.EssenceRolls = esRolls0; run.EssenceBadLuck = esLuck0;

                // Only enemies drop it: crates and room rewards never do.
                int esBefore = Pickup.All.Count(p => p && p.Item.Kind == ItemKind.Essence);
                var esJunk = new List<Pickup>(Pickup.All);
                for (int i = 0; i < 60; i++) Loot.CrateDrop(AheadPoint(player, 4f, 0f));
                for (int i = 0; i < 30; i++) Loot.RoomClearReward(Dungeon.Instance.Current);
                int esAfter = Pickup.All.Count(p => p && p.Item.Kind == ItemKind.Essence);
                foreach (var p in Pickup.All.ToArray()) if (p && !esJunk.Contains(p)) p.Discard();
                Check($"crates and room rewards never drop Essence ({esAfter - esBefore} from 60 crates + 30 rewards)", esAfter == esBefore);

                // A kill with a guaranteed roll drops a violet, glowing, floating crystal that flies to you.
                run.EssenceBadLuck = 40;
                var esVictim = SpawnAhead(player, EnemyType.Imp, 4f, 0f);
                yield return new WaitForSeconds(0.2f);
                int esHave = run.Essence;
                esVictim.TakeDamage(9999f);
                yield return null;
                var esDrop = Pickup.All.FirstOrDefault(p => p && p.Item.Kind == ItemKind.Essence);
                Check($"an enemy kill drops Arcane Essence (distinct from gold: {Items.Essence.Name}, auto-collect {esDrop && esDrop.IsAuto}, bad luck reset to {run.EssenceBadLuck})",
                    esDrop && esDrop.IsAuto && Items.Essence.Color != Items.Coin.Color && run.EssenceBadLuck == 0);
                yield return Shot("05k_essence_drop");
                float esT = 0f;
                while (esT < 4f && run.Essence == esHave) { esT += Time.deltaTime; yield return null; }
                Check($"Essence flies to you and is collected ({esHave} -> {run.Essence}), and the HUD counter pulses ({run.EssencePulse:0.00})",
                    run.Essence > esHave && run.EssencePulse > 0f);
                yield return Shot("05l_essence_hud");
                gs.ClearEnemies();
            }

            // ---- Spell Forge ----
            // These tests fill the book to its 9-spell limit from the 6 original spells: put Blink Dash and
            // Quickening aside (the unlock section learns them again).
            SpellBook.Known.RemoveAll(sfS => sfS.Id == SelfSpellConfig.BlinkDashId || sfS.Id == SelfSpellConfig.QuickeningId);
            var forge = SpellForgeUI.Instance;
            // Forging costs Essence: 3, then 5, then 7...
            run.Essence = 0;
            run.ForgesDone = 0;
            forge.Open();
            forge.Toggle(IndexOf("Fireball"));
            forge.Toggle(IndexOf("Frost Nova"));
            int esBook0 = SpellBook.Known.Count;
            bool esRefused = forge.Forge() == null && SpellBook.Known.Count == esBook0;
            Check($"with no Essence the Forge refuses (forge costs {run.ForgeCost})", esRefused && run.ForgeCost == EssenceConfig.BaseCost);
            yield return null;
            yield return Shot("07a_forge_no_essence");
            run.Essence = 30;
            forge.Close();
            forge.Open();
            forge.Toggle(IndexOf("Fireball"));
            forge.Toggle(IndexOf("Frost Nova"));
            yield return Shot("07_forge_preview");
            var frozenFireball = forge.Forge();
            Check("Forge: Fireball + Frost Nova", frozenFireball != null && frozenFireball.Synergies.Contains("Shatter"));
            int esAfter1 = run.Essence;
            if (frozenFireball != null) Log($"forged '{frozenFireball.Name}' {Arrows.ToText(frozenFireball.Sequence)} cd={frozenFireball.Cooldown}: {frozenFireball.Describe()}");
            forge.Toggle(IndexOf("Lightning"));
            forge.Toggle(IndexOf("Fireball"));
            var stormLightning = forge.Forge();
            Check("Forge: Lightning + Fireball unlocks Ignite", stormLightning != null && stormLightning.Synergies.Contains("Ignite"));
            Check($"each forge costs more: paid {frozenFireball?.EssencePaid} then {stormLightning?.EssencePaid} (Essence 30 -> {esAfter1} -> {run.Essence}); forges done {run.ForgesDone}, next costs {run.ForgeCost}",
                frozenFireball != null && stormLightning != null && frozenFireball.EssencePaid == 3 && stormLightning.EssencePaid == 5
                && esAfter1 == 27 && run.Essence == 22 && run.ForgesDone == 2 && run.ForgeCost == 7 && run.NextForgeCost == 9);
            forge.Toggle(IndexOf("Fireball"));
            forge.Toggle(IndexOf("Fireball"));
            Check("Forge rejects picking the same spell twice", forge.Forge() == null);
            forge.Close();
            forge.Open();
            yield return null;
            yield return null; // let the HUD lay out the Forge's clickable areas
            forge.Click(forge.RowRect(IndexOf("Frost Nova")).center);
            string fgMissing = forge.Missing;
            forge.Click(forge.RowRect(IndexOf("Lightning")).center);
            yield return null;
            yield return Shot("07b_forge_ui");
            int fgCount = SpellBook.Known.Count;
            int fgCostPaid = run.ForgeCost;
            forge.Click(forge.ForgeButtonRect.center);
            Check($"Forge UI: clicking spells and the Forge button fuses them (first said \"{fgMissing}\")",
                fgMissing.Contains("second") && SpellBook.Known.Count == fgCount + 1);
            var fgVictim = SpellBook.Known[SpellBook.Known.Count - 1];
            forge.Toggle(SpellBook.Known.Count - 1);
            yield return null;
            forge.Click(forge.ForgetButtonRect.center);
            bool fgStillKnown = SpellBook.Knows(fgVictim.Id);
            int fgEssBefore = run.Essence, fgForges = run.ForgesDone;
            forge.Click(forge.ForgetButtonRect.center);
            var fgRune = Pickup.All.FirstOrDefault(p => p.Item.Spell == fgVictim);
            Check($"Forget asks to confirm, then removes {fgVictim.Name} and drops its rune", fgStillKnown && !SpellBook.Knows(fgVictim.Id) && fgRune);
            int fgRefund = run.Essence - fgEssBefore;
            Check($"forgetting refunds half of what that spell cost (paid {fgVictim.EssencePaid}, refund {fgRefund}) and forging doesn't get cheaper (forges done {fgForges} -> {run.ForgesDone}, next costs {run.ForgeCost})",
                fgVictim.EssencePaid == fgCostPaid && fgRefund == fgCostPaid / 2 && run.ForgesDone == fgForges && run.ForgeCost == EssenceConfig.ForgeCost(fgForges));
            forge.Close();
            int fgEssKeep = run.Essence;
            run.Essence = 0;
            bool fgNeedsRefund = fgRune && fgRefund > 0 && !run.CanAcquire(fgRune.Item, out _);
            run.Essence = fgEssKeep;
            if (fgRune)
            {
                run.Acquire(fgRune.Item);
                Destroy(fgRune.gameObject);
            }
            Check($"taking the rune back returns the refund (Essence {fgEssKeep} -> {run.Essence}; refused without it: {fgNeedsRefund}), so forge/forget/relearn isn't a discount",
                run.Essence == fgEssKeep - fgRefund && fgNeedsRefund && fgVictim.EssenceRefunded == 0);
            Check("picking the rune back up relearns the forgotten spell", SpellBook.Knows(fgVictim.Id));
            // A starting spell (never paid for) refunds nothing.
            Check($"starting spells refund nothing ({SpellBook.Known[0].Name}: paid {SpellBook.Known[0].EssencePaid}, refund {EssenceConfig.Refund(SpellBook.Known[0].EssencePaid)})",
                EssenceConfig.Refund(SpellBook.Known[0].EssencePaid) == 0);
            SpellBook.Known.Remove(fgVictim); // leave the book as the later checks expect it
            bool conflictFree = true;
            var book = SpellBook.Known;
            for (int i = 0; i < book.Count; i++)
                for (int j = i + 1; j < book.Count; j++)
                    if (Arrows.Conflicts(book[i].Sequence, book[j].Sequence)) conflictFree = false;
            Check("no two known sequences conflict (prefix-free)", conflictFree);
            Check("forged runes never repeat a stroke", SpellBook.Known.All(s => !Arrows.HasRepeat(s.Sequence)));
            // Every known rune, drawn the way the test draws (wobbly strokes), must read back exactly.
            var misread = new List<string>();
            foreach (var sp in SpellBook.Known)
            {
                var rg = new GestureRecognizer();
                var got = new List<Arrow>();
                for (int i = 0; i < sp.Sequence.Length; i++)
                {
                    Vector2 dir = GestureRecognizer.Dir(sp.Sequence[i]), side = new Vector2(-dir.y, dir.x);
                    for (int f = 0; f < 7; f++)
                    {
                        var r = rg.Feed(dir * 1.1f + side * Mathf.Sin(f * 1.7f + i) * 0.35f);
                        if (r.HasValue) got.Add(r.Value);
                    }
                }
                if (Arrows.ToText(got) != Arrows.ToText(sp.Sequence)) misread.Add($"{sp.Name} {Arrows.Glyphs(sp.Sequence)} read as {Arrows.Glyphs(got)}");
            }
            Check("every known rune (forged too) reads back exactly" + (misread.Count > 0 ? ": " + string.Join("; ", misread) : ""), misread.Count == 0);

            if (frozenFireball != null)
            {
                ss.ResetCooldowns();
                var tanks = new List<Enemy> { SpawnAhead(player, EnemyType.Cultist, 8f, 0f), SpawnAhead(player, EnemyType.Cultist, 8.5f, 1.3f) };
                yield return new WaitForSeconds(0.3f);
                foreach (var e in tanks) e.Freeze(4f);
                player.LookAt(tanks[0].Center);
                yield return CastSequence(Arrows.ToText(frozenFireball.Sequence), expectReady: frozenFireball.Name);
                ss.CastReadied();
                yield return new WaitForSeconds(0.9f);
                Check("Frozen Fireball shattered frozen cultists (60 HP)", tanks.All(e => !e || !e.IsAlive));
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.8f);
            }
            ClearPickups();

            // ---- Grimoire (experimental deck-building cards) ----
            {
                var gm = Grimoire.GrimoireRun.Instance;
                Check("Grimoire is off by default: no deck, no cast hook, no Scroll of Erasure in the shop",
                    gm && !Settings.GrimoireEnabled && !gm.Active && gm.Deck == null && !SpellSystem.HasCastHooks && !Pickup.All.Any(p => p && p.Item == Grimoire.GrimoireShop.Erasure));
                Settings.SetGrimoireEnabled(true);
                yield return null;
                var gmDeck = gm.Deck;
                var gmGroups = gmDeck != null ? gmDeck.Grouped() : new List<(Grimoire.CardDef def, int count)>();
                Check($"Grimoire on: starting deck {string.Join(", ", gmGroups.Select(g => g.count + " " + g.def.Name))}; hand 3, draw pile 5",
                    gm.Active && SpellSystem.HasCastHooks && gmDeck.Count == 8 && gmDeck.Hand.Count == 3 && gmDeck.DrawPile.Count == 5 &&
                    gmGroups.Any(g => g.def.Id == "blank" && g.count == 5) && gmGroups.Any(g => g.def.Id == "amplify" && g.count == 1) &&
                    gmGroups.Any(g => g.def.Id == "split" && g.count == 1) && gmGroups.Any(g => g.def.Id == "echo" && g.count == 1));

                // Rotation: hand -> discard, draw refills, an empty draw pile reshuffles the discard pile.
                gm.StartDeck(new[] { "blank", "amplify", "overload", "split", "pierce" });
                gmDeck = gm.Deck;
                var gmFirst = gmDeck.Next;
                var gmSecond = gmDeck.Hand[1];
                gmDeck.PlayNext();
                bool gmRot = gmDeck.DiscardPile.Count == 1 && gmDeck.DiscardPile[0] == gmFirst && gmDeck.Next == gmSecond && gmDeck.Hand.Count == 3 && gmDeck.DrawPile.Count == 1;
                gmDeck.PlayNext();
                gmDeck.PlayNext();
                Check($"cards rotate hand -> discard -> reshuffled into the draw pile ({gmDeck.Reshuffles} reshuffle, draw {gmDeck.DrawPile.Count}, hand {gmDeck.Hand.Count})",
                    gmRot && gmDeck.Reshuffles == 1 && gmDeck.Count == 5 && gmDeck.Hand.Count == 3 && gmDeck.DrawPile.Count == 2 && gmDeck.DiscardPile.Count == 0);
                Check("reshuffle plays its card-HUD animation", Grimoire.GrimoireHUD.Animating);
                yield return Shot("13a_grimoire_reshuffle");
                gmDeck.AddToHand(Grimoire.Cards.Get("pierce"));
                var gmCyc = gmDeck.Next;
                gm.Cycle();
                Check($"cycling (Q) discards the Next card unused and locks casting for {Grimoire.GrimoireConfig.CycleLockout:0.#} s",
                    gmDeck.DiscardPile.Contains(gmCyc) && ss.CastLock > 0.9f && gmDeck.Hand.Count == 3);
                ss.CastLock = 0f;
                gmDeck.AddToHand(Grimoire.Cards.Get("blank"));
                bool gmBlankStays = !gm.Cycle() && gmDeck.Next.Def.Id == "blank";
                gmDeck.AddToHand(Grimoire.Cards.Get("fumble"));
                bool gmCurseStays = !gm.Cycle() && gmDeck.Next.Def.Id == "fumble";
                Check("Blank and curse cards can't be cycled away", gmBlankStays && gmCurseStays && ss.CastLock == 0f);
                gmDeck.PlayNext();
                gmDeck.AddToHand(Grimoire.Cards.Get("amplify"));
                ss.FireBasic();
                bool gmKept = gmDeck.Next.Def.Id == "amplify";
                gmDeck.AddToHand(Grimoire.Cards.Get("blank"));
                ss.FireBasic();
                Check("the basic bolt only uses Blank cards; special cards wait for a spell", gmKept && gmDeck.Next.Def.Id == "amplify");
                foreach (var pr in Projectile.All.ToArray()) Destroy(pr.gameObject); // those bolts must not hit the next target

                // Amplify, on an elite (double HP) so it survives.
                ss.ResetCooldowns();
                var gmFb = Spell("Fireball");
                var gmTank = SpawnAhead(player, EnemyType.Cultist, 8f, 0f);
                yield return new WaitForSeconds(0.3f);
                gm.MakeElite(gmTank);
                gmTank.Freeze(6f);
                player.LookAt(gmTank.Center);
                float gmHp0 = gmTank.HP;
                ss.ReadyForTest(gmFb);
                yield return null;
                yield return Shot("13b_grimoire_ready_tint");
                ss.CastReadied();
                yield return new WaitForSeconds(0.7f);
                float gmExp = 40f * 1.5f * run.Stats.Power;
                Check($"Amplify: Fireball hits the elite (HP {gmTank.MaxHP:0}) for {gmHp0 - gmTank.HP:0} (expected {gmExp:0}); log '{gm.LastCast}'",
                    gmTank && gmTank.IsElite && Mathf.Approximately(gmTank.MaxHP, 120f) && Mathf.Abs(gmHp0 - gmTank.HP - gmExp) < 1f && gm.LastCast == "Fireball + Amplify");
                var gmShard = Spell("Frost Shard");
                ss.ResetCooldowns();
                gmDeck.AddToHand(Grimoire.Cards.Get("frozen"));
                ss.ReadyForTest(gmShard);
                ss.CastReadied();
                yield return new WaitForSeconds(0.5f);
                bool gmSlowed = gmTank && gmTank.IsSlowed;
                ss.ResetCooldowns();
                gmDeck.AddToHand(Grimoire.Cards.Get("burning"));
                ss.ReadyForTest(gmShard);
                ss.CastReadied();
                yield return new WaitForSeconds(0.5f);
                Check("Frozen slows the enemy hit; Burning sets it burning", gmSlowed && gmTank && gmTank.IsBurning);
                int gmCurses = gmDeck.AllCards.Count(c => c.Def.IsCurse);
                if (gmTank) gmTank.TakeDamage(99999f);
                yield return null;
                Check("an elite's death puts a Fumble curse in the discard pile", gmDeck.AllCards.Count(c => c.Def.IsCurse) == gmCurses + 1 && gmDeck.DiscardPile.Last().Def.Id == "fumble");

                // Cooldown cards, Split, Fumble, Echo.
                ss.ResetCooldowns();
                gmDeck.AddToHand(Grimoire.Cards.Get("overload"));
                ss.ReadyForTest(gmFb);
                ss.CastReadied();
                float gmCdO = ss.CooldownRemaining(gmFb);
                ss.ResetCooldowns();
                gmDeck.AddToHand(Grimoire.Cards.Get("quicken"));
                ss.ReadyForTest(gmFb);
                ss.CastReadied();
                Check($"Overload doubles the cooldown ({gmCdO:0.0} s, normally {SpellSystem.EffectiveCooldown(gmFb):0.0} s); Quicken leaves none",
                    Mathf.Abs(gmCdO - 2f * SpellSystem.EffectiveCooldown(gmFb)) < 0.05f && !ss.OnCooldown(gmFb));
                ss.ResetCooldowns();
                int gmShots0 = FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length;
                gmDeck.AddToHand(Grimoire.Cards.Get("split"));
                ss.ReadyForTest(gmFb);
                ss.CastReadied();
                int gmShots = FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length - gmShots0;
                Check($"Split: Fireball fires 3 projectiles in a fan ({gmShots})", gmShots == 3);
                yield return Shot("13c_grimoire_split");
                ss.ResetCooldowns();
                int gmEv = events.Count;
                int gmShots1 = FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length;
                gmDeck.AddToHand(Grimoire.Cards.Get("fumble"));
                ss.ReadyForTest(gmFb);
                ss.CastReadied();
                Check("Fumble: the readied spell fizzles and spends no cooldown",
                    ss.Readied == null && !ss.OnCooldown(gmFb) && FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length <= gmShots1 && events.Skip(gmEv).Contains("fumble Fireball"));
                gmEv = events.Count;
                gmDeck.AddToHand(Grimoire.Cards.Get("echo"));
                ss.ReadyForTest(gmFb);
                ss.CastReadied();
                yield return new WaitForSeconds(0.8f);
                Check("Echo: the spell goes off again 0.5 s later", events.Skip(gmEv).Contains("echo Fireball"));
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.6f);

                // Pierce and Wide.
                var gmA = SpawnAhead(player, EnemyType.Cultist, 4f, 0f);
                Vector3 gmLine = gmA.transform.position - player.transform.position;
                gmLine.y = 0f;
                var gmB = EnemySpawner.Instance.SpawnAt(EnemyType.Cultist, gmA.transform.position + gmLine.normalized * 3f);
                yield return new WaitForSeconds(0.3f);
                gmA.Freeze(4f);
                gmB.Freeze(4f);
                player.LookAt(gmA.Center);
                ss.ResetCooldowns();
                gmDeck.AddToHand(Grimoire.Cards.Get("pierce"));
                ss.ReadyForTest(gmShard);
                ss.CastReadied();
                yield return new WaitForSeconds(0.7f);
                Check("Pierce: Frost Shard passes through the first enemy and hits the one behind",
                    gmA && gmB && gmA.HP < gmA.MaxHP && gmB.HP < gmB.MaxHP);
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.6f);
                var gmFar = SpawnAhead(player, EnemyType.Cultist, 8f, 0f);
                yield return new WaitForSeconds(0.3f);
                Vector3 gmDelta = gmFar.transform.position - player.transform.position;
                gmDelta.y = 0f;
                float gmDist = gmDelta.magnitude;
                ss.ResetCooldowns();
                gmDeck.AddToHand(Grimoire.Cards.Get("wide"));
                ss.ReadyForTest(Spell("Frost Nova"));
                ss.CastReadied();
                yield return null;
                Check($"Wide: Frost Nova (6.5 m -> 9.75 m) freezes an enemy {gmDist:0.0} m away", gmDist > 6.6f && gmFar && gmFar.IsFrozen);
                yield return Shot("13d_grimoire_wide_nova");
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.6f);

                // Heal: Amplify heals 50% more; Split heals twice at 60%.
                player.God = false;
                player.TakeDamage(60f);
                float gmH0 = player.HP;
                ss.ResetCooldowns();
                gmDeck.AddToHand(Grimoire.Cards.Get("amplify"));
                ss.ReadyForTest(Spell("Heal"));
                ss.CastReadied();
                float gmHealA = player.HP - gmH0;
                player.TakeDamage(40f);
                float gmH1 = player.HP;
                ss.ResetCooldowns();
                gmDeck.AddToHand(Grimoire.Cards.Get("split"));
                ss.ReadyForTest(Spell("Heal"));
                ss.CastReadied();
                float gmHealS = player.HP - gmH1;
                player.God = true;
                float gmHp = 25f * run.Stats.Power;
                Check($"Heal + Amplify heals {gmHealA:0.#} (expected {gmHp * 1.5f:0.#}); Heal + Split heals {gmHealS:0.#} (expected {gmHp * 1.2f:0.#})",
                    Mathf.Abs(gmHealA - gmHp * 1.5f) < 0.1f && Mathf.Abs(gmHealS - gmHp * 1.2f) < 0.1f);

                // Every card on every base spell and on the bolt: nothing throws, each cast is logged right.
                Vector3 gmPos = player.transform.position;
                float gmYaw = player.transform.eulerAngles.y;
                int gmErrors = 0;
                string gmFirstError = null;
                Application.LogCallback gmLog = (msg, st, type) =>
                {
                    if (type != LogType.Exception && type != LogType.Error) return;
                    gmErrors++;
                    if (gmFirstError == null) gmFirstError = msg;
                };
                Application.logMessageReceived += gmLog;
                var gmBad = new List<string>();
                var gmBoltUse = Grimoire.GrimoireConfig.BoltUse;
                Grimoire.GrimoireConfig.BoltUse = Grimoire.BoltCardUse.Any;
                var gmSpells = SpellBook.Defaults();
                foreach (var card in Grimoire.Cards.All)
                {
                    var gmTargets = new[] { SpawnAhead(player, EnemyType.Cultist, 6f, -1.5f), SpawnAhead(player, EnemyType.Imp, 7f, 1.5f) };
                    yield return null;
                    foreach (var tg in gmTargets) tg.Freeze(5f);
                    foreach (var sp in gmSpells)
                    {
                        player.Teleport(gmPos, gmYaw);
                        if (gmTargets[0]) player.LookAt(gmTargets[0].Center);
                        gmDeck.AddToHand(card);
                        ss.ResetCooldowns();
                        ss.ReadyForTest(sp);
                        ss.CastReadied();
                        if (gm.LastCast != sp.Name + " + " + card.Name) gmBad.Add($"{sp.Name} + {card.Name} logged as {gm.LastCast}");
                        yield return null;
                    }
                    player.Teleport(gmPos, gmYaw);
                    gmDeck.AddToHand(card);
                    ss.FireBasic();
                    if (gm.LastCast != "Bolt + " + card.Name) gmBad.Add($"Bolt + {card.Name} logged as {gm.LastCast}");
                    yield return new WaitForSeconds(0.6f); // echoes go off, shots land
                    gs.ClearEnemies();
                }
                Application.logMessageReceived -= gmLog;
                Grimoire.GrimoireConfig.BoltUse = gmBoltUse;
                player.Teleport(gmPos, gmYaw);
                Check($"every card ({Grimoire.Cards.All.Count}) works on every base spell ({gmSpells.Count}) and the bolt: {gmErrors} errors{(gmFirstError != null ? " (" + gmFirstError + ")" : "")}" +
                    (gmBad.Count > 0 ? "; " + string.Join("; ", gmBad.Take(3)) : ""), gmErrors == 0 && gmBad.Count == 0);
                yield return new WaitForSeconds(0.8f);
                gs.ClearEnemies();
                ClearPickups();

                // Room reward panel: pauses the game; mouse picks a card; removal costs gold, once per panel.
                gm.StartDeck(Grimoire.GrimoireConfig.StartingDeck);
                gmDeck = gm.Deck;
                var rw = gm.Rewards;
                int gmN = gmDeck.Count;
                rw.Open(gm);
                yield return null;
                yield return null;
                bool gmPaused = gs.ForgeOpen && Time.timeScale == 0f && !gs.AcceptsGameplayInput;
                yield return Shot("13e_grimoire_reward");
                var gmPick = rw.Choices.Count > 1 ? rw.Choices[1] : null;
                if (rw.ChoiceRects.Count > 1) rw.Click(rw.ChoiceRects[1].center);
                Check($"room reward: the panel pauses the game and offers {rw.Choices.Count} non-curse cards; clicking one puts {gmPick?.Name} in the discard pile",
                    gmPaused && rw.Choices.Count == 3 && rw.Choices.All(c => !c.IsCurse) && !rw.IsOpen && gmDeck.Count == gmN + 1 && gmDeck.DiscardPile.Last().Def == gmPick);
                yield return null;
                Check("closing the panel resumes the game", !gs.ForgeOpen && Time.timeScale == 1f);
                run.Gold = 60;
                rw.Open(gm);
                yield return null;
                yield return null;
                rw.Click(rw.RemoveButton.center);
                yield return null;
                yield return null;
                yield return Shot("13f_grimoire_remove");
                var gmG0 = gmDeck.Grouped()[0];
                if (rw.RemoveRects.Count > 0) rw.Click(rw.RemoveRects[0].center);
                int gmLeft = gmDeck.AllCards.Count(c => c.Def == gmG0.def);
                int gmCost = Grimoire.GrimoireConfig.RemoveCost;
                Check($"removing a card (click) costs {gmCost} gold and takes one {gmG0.def.Name} out of the deck",
                    gmDeck.Count == gmN && run.Gold == 60 - gmCost && gmLeft == gmG0.count - 1);
                bool gmOnce = rw.RemoveBlocked() != null;
                rw.Click(rw.SkipButton.center);
                Check("only one removal per panel; Skip closes the panel", gmOnce && !rw.IsOpen);
                run.Gold = 0;
                rw.Open(gm);
                rw.StartRemove();
                Check($"removal needs {gmCost} gold (\"{rw.Message}\")", !rw.Removing && rw.Message != null && rw.Message.Contains("gold"));
                rw.Skip();
                yield return null;

                // Scroll of Erasure: sold in the shop while the Grimoire is on; using it removes a card for free.
                var gmScroll = Pickup.All.FirstOrDefault(p => p && p.Item == Grimoire.GrimoireShop.Erasure);
                var gmShop = d.FirstOfType(RoomType.Shop);
                Check($"the shop sells a Scroll of Erasure ({(gmScroll ? gmScroll.Price + " gold" : "missing")})",
                    gmScroll && gmScroll.Price > 0 && gmShop != null && gmShop.Contains(gmScroll.transform.position, 0.5f));
                if (gmScroll)
                {
                    for (int i = 0; i < Run.Slots; i++) run.Consumables[i] = null;
                    run.Gold = gmScroll.Price + 10;
                    bool gmBought = gmScroll.TryCollect(run) && run.Consumables[0] == Grimoire.GrimoireShop.Erasure && run.Gold == 10;
                    int gmBefore = gmDeck.Count;
                    run.UseConsumable(0);
                    yield return null;
                    yield return null;
                    bool gmFreeOpen = rw.IsOpen && rw.IsFree && rw.Removing && run.Consumables[0] == null;
                    yield return Shot("13f2_grimoire_erasure");
                    if (rw.RemoveRects.Count > 0) rw.Click(rw.RemoveRects[0].center);
                    Check("buying and using the Scroll of Erasure removes a card for free", gmBought && gmFreeOpen && !rw.IsOpen &&
                        gmDeck.Count == gmBefore - 1 && run.Gold == 10 && run.Consumables[0] == null);
                    run.Consumables[0] = Grimoire.GrimoireShop.Erasure;
                    run.UseConsumable(0);
                    rw.Back();
                    Check("backing out of the Scroll of Erasure gives the scroll back", !rw.IsOpen && run.Consumables[0] == Grimoire.GrimoireShop.Erasure && gmDeck.Count == gmBefore - 1);
                    run.Consumables[0] = null;
                    yield return null;
                }

                gs.SetFocus(false);
                yield return null;
                yield return Shot("13g_grimoire_pause");
                gs.ClearFocusPause();
                yield return null;

                // Off again: nothing is modified.
                Settings.SetGrimoireEnabled(false);
                yield return null;
                string gmLast = gm.LastCast;
                ss.ResetCooldowns();
                ss.ReadyForTest(gmFb);
                ss.CastReadied();
                Check("Grimoire off again: cast hook gone, casts unmodified, no card used",
                    !SpellSystem.HasCastHooks && !gm.Active && gm.LastCast == gmLast && Mathf.Abs(ss.CooldownRemaining(gmFb) - SpellSystem.EffectiveCooldown(gmFb)) < 0.01f);
                ss.ResetCooldowns();
                yield return new WaitForSeconds(0.8f);
                gs.ClearEnemies();
                ClearPickups();
            }

            // ---- co-op groundwork: a second player in the same world (no networking yet) ----
            {
                gs.ClearEnemies();
                ss.ResetCooldowns();
                var cpRoom = d.Current;
                Vector3 cpHome = player.transform.position;
                float cpHomeYaw = player.transform.eulerAngles.y;
                float cpX = Mathf.Min(5f, cpRoom.HalfX - 2f);
                Vector3 CpSpot(float x, float z = 0f) => d.FindFreeSpot(cpRoom, cpRoom.Center + new Vector3(x, 0f, z), 0.8f) + Vector3.up * 0.1f;
                player.Teleport(CpSpot(-cpX), 0f);
                var cpOther = Player.Create(CpSpot(cpX), local: false);
                yield return null;
                int cpListeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
                Check($"co-op: a second player joins the world ({Players.All.Count} players), with its own run; this PC keeps one camera and one listener ({cpListeners})",
                    Players.All.Count == 2 && Players.Local == player && Player.Instance == player && Run.Instance == player.Run
                    && cpOther.Run != null && cpOther.Run != player.Run && !cpOther.IsLocal && !cpOther.Cam.enabled && cpListeners == 1 && !cpOther.InputEnabled);

                // Enemies go for the nearest living player.
                float cpHp1 = player.HP, cpHp2 = cpOther.HP;
                var cpImp = EnemySpawner.Instance.SpawnAt(EnemyType.Imp, cpOther.transform.position + new Vector3(0f, 0.05f, 1.6f));
                yield return new WaitForSeconds(2.5f);
                Check($"co-op: an enemy goes for the nearest player and hurts only them (other player {cpOther.HP:0}/{cpHp2:0}, you {player.HP:0}/{cpHp1:0})",
                    cpImp && cpImp.Target == cpOther && cpOther.HP < cpHp2 && player.HP == cpHp1);
                cpOther.Teleport(CpSpot(-cpX, cpRoom.HalfZ - 2.5f), 0f);
                player.Teleport(cpImp.transform.position + new Vector3(-1.5f, 0.1f, 0f), 0f);
                yield return new WaitForSeconds(Enemy.RetargetEvery + 0.2f);
                Check("co-op: when another player gets clearly closer, the enemy switches to them", cpImp && cpImp.Target == player);
                gs.ClearEnemies();
                player.Heal(999f);
                cpOther.Heal(999f);
                yield return null;

                // A blast hurts every player in it.
                player.Teleport(CpSpot(-1f), 0f);
                cpOther.Teleport(player.transform.position + new Vector3(2f, 0f, 0f), 0f);
                yield return null;
                float cpB1 = player.HP, cpB2 = cpOther.HP;
                Combat.Explode(player.transform.position + new Vector3(1f, 1.3f, 0f), 3f, 0f, 10f, Color.red);
                Check($"co-op: a blast hurts every player in it (you -{cpB1 - player.HP:0}, other -{cpB2 - cpOther.HP:0})", player.HP < cpB1 && cpOther.HP < cpB2);
                player.Heal(999f);
                cpOther.Heal(999f);

                // A coin goes to the nearest player's own gold.
                int cpGold1 = player.Run.Gold, cpGold2 = cpOther.Run.Gold;
                Pickup.Spawn(Items.Coin, cpOther.transform.position + new Vector3(0f, 0f, 0.6f));
                yield return new WaitForSeconds(0.9f);
                Check($"co-op: a coin goes to the player who picks it up (their gold {cpGold2} -> {cpOther.Run.Gold}, yours {cpGold1} -> {player.Run.Gold})",
                    cpOther.Run.Gold > cpGold2 && player.Run.Gold == cpGold1);

                // The run only ends when nobody is left standing.
                cpOther.TakeDamage(9999f);
                yield return null;
                Check("co-op: one player falling doesn't end the run while another stands", cpOther.IsDead && gs.Phase == Phase.Playing && Players.AliveCount == 1);

                // In multiplayer, pausing doesn't stop time.
                GameState.Multiplayer = true;
                gs.SetFocus(false);
                yield return null;
                yield return null;
                bool cpMpRuns = gs.Paused && Time.timeScale == 1f;
                MainMenu.Instance.ChooseForTest("Resume");
                GameState.Multiplayer = false;
                yield return null;
                yield return null; // the pause menu sees the game running again before the next pause
                gs.SetFocus(false);
                yield return null;
                yield return null;
                bool cpSpStops = gs.Paused && Time.timeScale == 0f;
                if (!MainMenu.Instance.ChooseForTest("Resume")) gs.ClearFocusPause(); // never leave the test paused
                yield return null;
                yield return null;
                Check("co-op: in multiplayer the pause menu doesn't stop time; single-player still pauses", cpMpRuns && cpSpStops && !gs.Paused);

                UnityEngine.Object.Destroy(cpOther.gameObject);
                yield return null;
                player.Teleport(cpHome, cpHomeYaw);
                Check("co-op: a player leaving is removed from the world", Players.All.Count == 1 && Players.Local == player && Run.Instance == player.Run);
            }

            // ---- casting style: Momentum ----
            {
                Check("Classic is the default casting style", Settings.CastingStyle == CastingStyle.Classic);
                Settings.SetCastingStyle(CastingStyle.Momentum);
                var mmRoom = d.Current;
                Vector3 mmHome = player.transform.position;
                float mmHomeYaw = player.transform.eulerAngles.y;
                player.Teleport(mmRoom.Center + new Vector3(0f, 0.1f, -6f), 0f);
                player.SetLook(0f, 0f);
                gs.ClearEnemies();
                ss.ResetCooldowns();
                ss.ClearReadied();
                ss.ClearFreshPressForTest();
                yield return null;
                // order: up (W), left (A), down (S), right (D)
                var mmDown = new bool[4];
                var mmHeld = new bool[4];
                void MmFrame(bool castKey, int pressKey = -1, int holdKey = -1)
                {
                    for (int i = 0; i < 4; i++) { mmDown[i] = i == pressKey; mmHeld[i] = i == pressKey || i == holdKey; }
                    ss.ProcessMomentumInput(castKey, mmDown, mmHeld, Time.deltaTime);
                }

                // W already held when entering casting mode: not an input.
                MmFrame(true, pressKey: 0);
                bool mmIgnoredHeld = ss.InMomentum && ss.Entered.Count == 0 && player.AutoForward && !player.Channeling;
                MmFrame(true, holdKey: 0);
                MmFrame(true);                 // W released
                MmFrame(true, pressKey: 2);    // S: down
                MmFrame(true, holdKey: 2);     // S held: still one input
                MmFrame(true, holdKey: 2);
                int mmAfterHold = ss.Entered.Count;
                MmFrame(true);
                Check($"Momentum: a key held when casting starts doesn't count, and a held key is one input ({mmAfterHold} arrow)",
                    mmIgnoredHeld && mmAfterHold == 1);
                MmFrame(true, pressKey: 3);    // D: right
                MmFrame(true);
                MmFrame(true, pressKey: 0);    // W: up -> Fireball
                MmFrame(true);
                Check($"Momentum: S D W types Fireball (readied {ss.Readied?.Name})", ss.Readied != null && ss.Readied.Name == "Fireball");

                // Running forward where the camera looks; the mouse steers.
                Vector3 mmP0 = player.transform.position;
                yield return new WaitForSeconds(0.5f);
                Vector3 mmRun = player.transform.position - mmP0;
                mmRun.y = 0f;
                float mmSpeed = player.PlanarSpeed;
                float mmWant = (run ? run.MoveSpeed : player.MoveSpeed) * MomentumConfig.SpeedMultiplier;
                Check($"Momentum: you run forward ({mmRun.magnitude:0.0} m in 0.5 s, speed {mmSpeed:0.0} of {mmWant:0.0}), with free mouse look and the cue on (FOV +{player.Cam.fieldOfView - Settings.FieldOfView:0.0})",
                    mmRun.z > 1.5f && Mathf.Abs(mmRun.x) < 0.3f && Mathf.Abs(mmSpeed - mmWant) < 0.3f && !player.Channeling && player.Cue > 0.5f
                    && player.Cam.fieldOfView > Settings.FieldOfView + 1f);
                player.SetLook(90f, 0f);
                yield return new WaitForSeconds(0.35f);
                Vector3 mmVel = player.PlanarVelocity.normalized;
                Check($"Momentum: turning the camera steers the run (heading {mmVel.x:0.00}, {mmVel.z:0.00})", mmVel.x > 0.9f);
                Settings.SetCastingStyle(CastingStyle.Classic);
                bool mmKeptStyle = ss.InMomentum;
                Settings.SetCastingStyle(CastingStyle.Momentum);
                bool mmFired = ss.CastReadied();
                Check("Momentum: Left Click fires while running; a style change waits for the next cast", mmFired && ss.InMomentum && player.AutoForward && mmKeptStyle);

                // A wall stops you without going through it.
                yield return new WaitForSeconds(2.5f);
                Check($"Momentum: running into a wall slides along it, never through ({player.transform.position.x - mmRoom.Center.x:0.0} m from the centre)",
                    mmRoom.Contains(player.transform.position, 0f));

                // Letting go: running stops with a short slow-down; the rune survives a short slip.
                player.SetLook(180f, 0f);
                yield return new WaitForSeconds(0.4f);
                MmFrame(false);
                bool mmStopsRunning = !player.AutoForward && ss.InMomentum;
                ss.ProcessMomentumInput(false, mmDown, mmHeld, SpellSystem.ReleaseGrace + 0.01f);
                float mmSpeedAtRelease = player.PlanarSpeed;
                yield return new WaitForSeconds(MomentumConfig.RampOut + 0.15f);
                Check($"Momentum: letting go stops the run smoothly ({mmSpeedAtRelease:0.0} -> {player.PlanarSpeed:0.00} m/s) and ends casting mode; the cue fades",
                    mmStopsRunning && !ss.Channeling && player.PlanarSpeed < 0.1f && mmSpeedAtRelease > 1f && player.Cue < 0.5f);

                // A panel / pause / map / focus loss stops you on the spot, until the key is pressed again.
                MmFrame(true, holdKey: 0);     // moving (W held), so this cast runs
                yield return new WaitForSeconds(0.3f);
                ss.Block();
                bool mmStopped = !ss.Channeling && !player.AutoForward && player.PlanarSpeed == 0f && ss.WaitingForFreshPress;
                MmFrame(true);
                yield return null;
                bool mmStaysOff = !ss.Channeling && !player.AutoForward;
                MmFrame(false);
                MmFrame(true, holdKey: 0);
                bool mmResumes = ss.InMomentum && player.AutoForward;
                Check("Momentum: a panel, pause or focus loss stops you at once, and only a fresh press resumes casting mode",
                    mmStopped && mmStaysOff && mmResumes);
                MmFrame(false);
                ss.ProcessMomentumInput(false, mmDown, mmHeld, 1f);
                yield return new WaitForSeconds(0.3f);

                // The run keeps the direction you were going: D held when the cast starts runs right.
                ss.ProcessMomentumInput(false, mmDown, mmHeld, 1f);
                player.SetLook(0f, 0f);
                yield return new WaitForSeconds(0.3f);
                MmFrame(true, holdKey: 3);
                for (int mmF = 0; mmF < 30; mmF++) { MmFrame(true, holdKey: 3); yield return null; }
                yield return new WaitForSeconds(0.2f);
                Vector3 mmSide = player.PlanarVelocity.normalized;
                int mmSideTyped = ss.Entered.Count;
                Check($"Momentum: the run keeps the direction you were moving (D held -> heading {mmSide.x:0.00}, {mmSide.z:0.00}); the held key doesn't type ({mmSideTyped})",
                    mmSide.x > 0.9f && player.AutoForward && mmSideTyped == 0);
                MmFrame(false);
                ss.ProcessMomentumInput(false, mmDown, mmHeld, 1f);
                yield return new WaitForSeconds(0.4f);

                // Standing still when the key goes down: you stay put and W A S D only type the rune.
                Vector3 mmStill0 = player.transform.position;
                MmFrame(true);
                bool mmRestStart = ss.InMomentum && ss.CastFromRest && !player.AutoForward && player.HoldStill;
                MmFrame(true, pressKey: 0);    // W: up, typed, not walked
                for (int mmF = 0; mmF < 20; mmF++) { MmFrame(true, holdKey: 0); yield return null; }
                yield return new WaitForSeconds(0.3f);
                Vector3 mmStillMove = player.transform.position - mmStill0;
                mmStillMove.y = 0f;
                int mmStillTyped = ss.Entered.Count;
                Check($"Momentum: casting from standing still doesn't move you ({mmStillMove.magnitude:0.00} m), W A S D still type ({mmStillTyped} arrow), no cue",
                    mmRestStart && mmStillMove.magnitude < 0.1f && player.PlanarSpeed < 0.05f && mmStillTyped == 1 && player.Cue == 0f);
                MmFrame(false);
                ss.ProcessMomentumInput(false, mmDown, mmHeld, 1f);
                yield return null;
                Check("Momentum: letting go of a still cast frees you to walk again",
                    !ss.Channeling && !player.HoldStill && !player.AutoForward);
                yield return new WaitForSeconds(0.2f);
                player.SetLook(0f, 0f);
                yield return Shot("05h_momentum_cue");
                Settings.SetCastingStyle(CastingStyle.Classic);
                ss.SetChanneling(false);
                ss.ClearReadied();
                ss.ResetCooldowns();
                player.Teleport(mmHome, mmHomeYaw);
                yield return new WaitForSeconds(0.3f);
                Check("back to Classic: casting mode stops the view again and doesn't run", Settings.CastingStyle == CastingStyle.Classic && !player.AutoForward && player.Cue == 0f);
            }

            // ---- stats and passive items ----
            run.Acquire(Items.Get("arcane_focus"));
            run.Acquire(Items.Get("ruby_heart"));
            Check("Arcane Focus: spell power 120%", Mathf.Approximately(run.Stats.Power, 1.2f));
            Check("Ruby Heart: max HP 125", Mathf.Approximately(player.MaxHP, 125f));
            yield return Shot("08_item_toast");

            // ---- gold, coins and the shop ----
            int gold0 = run.Gold;
            Pickup.Spawn(Items.Coin, player.transform.position + player.transform.forward * 0.6f);
            yield return new WaitForSeconds(0.9f);
            Check("walking over a coin collects it", run.Gold == gold0 + 1);
            run.Gold = 0;
            var boots = Pickup.Spawn(Items.Get("swift_boots"), player.transform.position + player.transform.forward * 1.5f, price: 20);
            yield return null;
            Check("shop item can't be bought without gold", !boots.TryCollect(run) && !run.Has("swift_boots"));
            run.Gold = 30;
            float speed0 = run.MoveSpeed;
            Check("shop item bought with E: gold spent, item owned", boots.TryCollect(run) && run.Gold == 10 && run.Has("swift_boots") && run.MoveSpeed > speed0);

            // ---- consumables ----
            run.Acquire(Items.Get("potion"));
            Check("potion goes into slot 1", run.Consumables[0]?.Id == "potion");
            player.God = false;
            player.TakeDamage(60f);
            float hp0 = player.HP;
            run.UseConsumable(0);
            Check($"potion heals 35 ({hp0:0} -> {player.HP:0})", Mathf.Approximately(player.HP, hp0 + 35f) && run.Consumables[0] == null);
            player.God = true;

            var bombTarget = SpawnAhead(player, EnemyType.Cultist, 6f, 0f);
            bombTarget.Freeze(4f);
            yield return new WaitForSeconds(0.3f);
            player.LookAt(bombTarget.transform.position + Vector3.up * 0.3f);
            run.Acquire(Items.Get("bomb"));
            run.UseConsumable(0);
            yield return Shot("09_bomb_thrown");
            yield return new WaitForSeconds(1.2f);
            Check("fire bomb kills a 60 HP cultist", !bombTarget || !bombTarget.IsAlive);

            var trio = new List<Enemy> { SpawnAhead(player, EnemyType.Imp, 5f, -2f), SpawnAhead(player, EnemyType.Imp, 6f, 0f), SpawnAhead(player, EnemyType.Imp, 5f, 2f) };
            yield return new WaitForSeconds(0.2f);
            run.Acquire(Items.Get("frost_scroll"));
            run.UseConsumable(0);
            yield return null;
            Check("frost scroll freezes every nearby enemy", trio.All(e => e && e.IsFrozen));
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.8f);
            ClearPickups();

            // ---- relics ----
            run.Acquire(Items.Get("prism"));
            ss.ResetCooldowns();
            player.SetLook(0f, 0f);
            int shots0 = FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length;
            yield return CastSequence("DRU", expectReady: "Fireball");
            ss.CastReadied();
            yield return null;
            int shots = FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length - shots0;
            Check($"Splitting Prism: Fireball fires 3 projectiles ({shots})", shots == 3);
            yield return Shot("10_prism");
            yield return new WaitForSeconds(1f);
            run.Stats.ProjectileSplit = 0; // keep the following checks single-shot

            run.Acquire(Items.Get("volatile"));
            ss.ResetCooldowns();
            var victim = SpawnAhead(player, EnemyType.Imp, 8f, 0f);
            var neighbour = SpawnAhead(player, EnemyType.Imp, 9.2f, 1.2f);
            yield return new WaitForSeconds(0.2f);
            victim.Freeze(3f); neighbour.Freeze(3f);
            victim.TakeDamage(10f, Element.Arcane, silent: true);
            neighbour.TakeDamage(18f, Element.Arcane, silent: true);
            player.LookAt(victim.Center);
            yield return CastSequence("LUR", expectReady: "Frost Shard");
            ss.CastReadied();
            yield return new WaitForSeconds(0.6f);
            Check("Volatile Soul: spell kill explodes and kills the neighbour", (!victim || !victim.IsAlive) && (!neighbour || !neighbour.IsAlive));
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.6f);

            // ---- obstacles ----
            var barrel = Prop.Create(PropKind.Barrel, d.Current.Root, AheadPoint(player, 7f, 0f));
            var near1 = SpawnAhead(player, EnemyType.Imp, 7.5f, 1.4f);
            var near2 = SpawnAhead(player, EnemyType.Imp, 7.5f, -1.4f);
            yield return new WaitForSeconds(0.3f);
            near1.Freeze(3f); near2.Freeze(3f);
            player.LookAt(barrel.Center);
            yield return Shot("11_barrel");
            ss.FireBasic();
            yield return new WaitForSeconds(0.6f);
            Check("explosive barrel: one bolt blows it up and kills nearby imps", !barrel && (!near1 || !near1.IsAlive) && (!near2 || !near2.IsAlive));
            var crate = Prop.Create(PropKind.Crate, d.Current.Root, AheadPoint(player, 4f, 2f));
            crate.TakeDamage(100f);
            yield return null;
            Check("crates break", !crate);

            var spikeImp = SpawnAhead(player, EnemyType.Imp, 5f, -3f);
            yield return new WaitForSeconds(0.2f);
            spikeImp.Freeze(6f);
            var trap = SpikeTrap.Create(d.Current.Root, spikeImp.transform.position, 0.5f);
            float t = 0f;
            while (t < 3.2f && spikeImp && spikeImp.IsAlive && Mathf.Approximately(spikeImp.HP, spikeImp.MaxHP)) { t += Time.deltaTime; yield return null; }
            Check("spike trap rises and hurts enemies standing on it", !spikeImp || spikeImp.HP < spikeImp.MaxHP);
            player.LookAt(trap.transform.position);
            yield return Shot("12_spikes");
            Destroy(trap.gameObject);
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.8f);
            ClearPickups();

            // ---- rooms: doors lock, encounter, clear ----
            var combat = d.Current.Doors.Select(x => x.Other(d.Current)).FirstOrDefault(r => r.Type == RoomType.Combat)
                         ?? d.AllRooms.First(r => r.Type == RoomType.Combat && !r.Cleared);
            EnterRoom(player, combat);
            yield return new WaitForSeconds(1.6f);
            Check($"entering a combat room locks its doors ({combat.Doors.Count} doors)", d.Current == combat && d.DoorsLocked && d.InCombat);
            Check($"room encounter spawned ({Enemy.All.Count} enemies, layout {combat.Layout})", Enemy.All.Count >= 4);
            FaceNearest(player);
            yield return Shot("13_room_fight");
            gs.ClearEnemies();
            yield return new WaitForSeconds(1.5f);
            Check("clearing the room opens the doors", !d.InCombat && combat.Cleared && !d.DoorsLocked);
            player.SetLook(180f, 0f);
            yield return Shot("14_room_cleared");
            EnterRoom(player, combat);
            yield return new WaitForSeconds(0.5f);
            Check("cleared rooms stay cleared", !d.InCombat && Enemy.All.Count == 0);
            {
                var trImp = SpawnAhead(player, EnemyType.Imp, 6f, 0f);
                yield return new WaitForSeconds(0.2f);
                trImp.Freeze(8f);
                var trTrap = SpikeTrap.Create(combat.Root, trImp.transform.position, 0.5f);
                yield return new WaitForSeconds(SpikeTrap.Cycle + 0.4f);
                Check("spike traps stop once their room is cleared", trTrap.Disarmed && !trTrap.IsUp && trImp.HP == trImp.MaxHP);
                Destroy(trTrap.gameObject);
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.6f);
                ClearPickups();
                int trGold = run.Gold;
                var trFar = Pickup.Spawn(Items.Coin, combat.Center + new Vector3(combat.HalfX - 2.5f, 0f, combat.HalfZ - 2.5f));
                float trT = 0f;
                while (trT < 4f && trFar) { trT += Time.deltaTime; yield return null; }
                Check($"after a room is cleared, coins anywhere in it fly to you ({trT:0.0} s)", !trFar && run.Gold > trGold);
            }

            // ---- save & resume ----
            {
                var sv = SaveRunner.Instance;
                yield return null;
                Check($"clearing a fight room autosaves ({sv.Autosaves} autosaves, checkpoint {d.CheckpointRoomId}) to {SaveSystem.FilePath}",
                    sv.Autosaves > 0 && File.Exists(SaveSystem.FilePath) && SaveSystem.HasValidSave && d.CheckpointRoomId == combat.Id);

                // Put some state in that is easy to get wrong: a deck, a dropped forged-spell rune, a broken crate.
                Settings.SetGrimoireEnabled(true);
                yield return null;
                var svGrim = Grimoire.GrimoireRun.Instance;
                svGrim.Deck.AddToHand(Grimoire.Cards.Get("echo"));
                svGrim.Deck.AddToDiscard(Grimoire.Cards.Get("pierce"));
                var svForged = SpellBook.Known.FirstOrDefault(s => s.IsForged);
                if (svForged != null) Pickup.Spawn(Items.ForgottenRune(svForged), combat.Center + new Vector3(2f, 0f, 2f));
                var svCrate = d.BuiltProps.Select(b => b.prop).FirstOrDefault(p => p && p.Kind == PropKind.Crate && Prop.All.Contains(p))
                    ?? d.BuiltProps.Select(b => b.prop).FirstOrDefault(p => p && Prop.All.Contains(p));
                if (svCrate) svCrate.TakeDamage(999f);
                yield return null;
                ClearPickups(); // a crate drop would roll away and move between saves
                run.Gold = 77;
                bool svSaved = SaveSystem.Save();

                // Round trip: save, load, save again; the two files must match (all but the time stamp).
                string StripTime(string j) => string.Join("\n", j.Split('\n').Where(l => !l.Contains("\"timestamp\"")));
                string svJson1 = StripTime(SaveSystem.ToJson(SaveSystem.Capture()));
                SaveSystem.Apply(SaveSystem.Parse(File.ReadAllText(SaveSystem.FilePath)));
                string svJson2 = StripTime(SaveSystem.ToJson(SaveSystem.Capture()));
                string svDiff = null;
                {
                    var a = svJson1.Split('\n');
                    var b = svJson2.Split('\n');
                    for (int i = 0; i < Mathf.Max(a.Length, b.Length) && svDiff == null; i++)
                        if (i >= a.Length || i >= b.Length || a[i] != b[i]) svDiff = $"line {i}: {(i < a.Length ? a[i].Trim() : "-")} | {(i < b.Length ? b[i].Trim() : "-")}";
                }
                Check($"save -> load -> save gives an identical file ({svJson1.Length} chars{(svDiff != null ? "; first difference " + svDiff : "")}; saved {svSaved}, forged {svForged?.Name}, crate {(bool)svCrate})",
                    svSaved && svDiff == null && svForged != null);

                // Resume: change everything after the checkpoint, then Continue puts it all back.
                float svHp = player.HP;
                int svGold = run.Gold;
                string svItems = string.Join(",", run.Items.Select(i => i.Id));
                string svSpells = string.Join(",", SpellBook.Known.Select(s => s.Name));
                string svDeck = string.Join(",", svGrim.Deck.DrawPile.Concat(svGrim.Deck.Hand).Concat(svGrim.Deck.DiscardPile).Select(c => c.Def.Id));
                string svMap = string.Join(",", d.AllRooms.Where(r => r.Visited).Select(r => r.Id));
                string svCleared = string.Join(",", d.AllRooms.Where(r => r.Cleared).Select(r => r.Id));
                int svPickups = Pickup.All.Count;
                var svNext = d.AllRooms.FirstOrDefault(r => r.Type == RoomType.Combat && !r.Cleared);
                string svWave = svNext != null ? string.Join(",", d.PreviewWave(svNext).Select(t => t.Name)) : "";
                int svNextFloor = RunRandom.FloorSeed(d.Floor + 1);
                var svShop = string.Join(",", Pickup.All.Where(p => p.Price > 0).Select(p => p.Item.Id).OrderBy(x => x));

                int svEss = run.Essence, svForges = run.ForgesDone, svLuck = run.EssenceBadLuck, svRolls = run.EssenceRolls;
                run.Essence = 999; run.ForgesDone = 50; run.EssenceBadLuck = 9; run.EssenceRolls = 1;
                run.Gold = 1;
                run.Items.Clear();
                player.God = false;
                player.TakeDamage(30f);
                player.God = true;
                SpellBook.Known.RemoveAt(SpellBook.Known.Count - 1);
                svGrim.Deck.ForceReshuffle();
                svGrim.Deck.CycleNext();
                foreach (var r in d.AllRooms) r.Cleared = true;
                foreach (var p in Pickup.All.ToArray()) p.Discard();
                RunRandom.RunSeed = 12345;

                bool svLoaded = SaveSystem.LoadRun();
                int svPickupsNow = Pickup.All.Count;
                bool svEssence = run.Essence == svEss && run.ForgesDone == svForges && run.EssenceBadLuck == svLuck && run.EssenceRolls == svRolls
                    && (svForged == null || SpellBook.Known.FirstOrDefault(s => s.Id == svForged.Id)?.EssencePaid == svForged.EssencePaid);
                Check($"Essence survives save and Continue (Essence {run.Essence}/{svEss}, forges done {run.ForgesDone}/{svForges}, bad luck {run.EssenceBadLuck}, paid per spell kept)", svLoaded && svEssence);
                yield return null;
                Log($"resume detail: loaded {svLoaded} ({SaveSystem.LastError}), items {string.Join(",", run.Items.Select(i => i.Id)) == svItems}, spells {string.Join(",", SpellBook.Known.Select(s => s.Name)) == svSpells}, deck {string.Join(",", svGrim.Deck.DrawPile.Concat(svGrim.Deck.Hand).Concat(svGrim.Deck.DiscardPile).Select(c => c.Def.Id)) == svDeck}, map {string.Join(",", d.AllRooms.Where(r => r.Visited).Select(r => r.Id)) == svMap}, pickups {svPickupsNow}/{svPickups}, room {d.Current?.Id}, cleared now {string.Join(",", d.AllRooms.Where(r => r.Cleared).Select(r => r.Id))}");
                Check($"Continue restores HP ({player.HP:0}), gold ({run.Gold}), items, spells, map, deck order and pickups, in the checkpoint room",
                    svLoaded && Mathf.Approximately(player.HP, svHp) && run.Gold == svGold
                    && string.Join(",", run.Items.Select(i => i.Id)) == svItems
                    && string.Join(",", SpellBook.Known.Select(s => s.Name)) == svSpells
                    && string.Join(",", svGrim.Deck.DrawPile.Concat(svGrim.Deck.Hand).Concat(svGrim.Deck.DiscardPile).Select(c => c.Def.Id)) == svDeck
                    && string.Join(",", d.AllRooms.Where(r => r.Visited).Select(r => r.Id)) == svMap
                    && svPickupsNow == svPickups && d.Current != null && d.Current.Id == combat.Id);
                var svNextNow = svNext != null ? d.AllRooms.FirstOrDefault(r => r.Id == svNext.Id) : null; // rooms are rebuilt on load
                Check($"cleared rooms stay cleared and the others don't ({svCleared})",
                    string.Join(",", d.AllRooms.Where(r => r.Cleared).Select(r => r.Id)) == svCleared && (svNextNow == null || !svNextNow.Cleared));
                Check($"the run continues the same: next room's wave ({svWave}), shop stock and next floor match",
                    svNext != null && string.Join(",", d.PreviewWave(svNext).Select(t => t.Name)) == svWave
                    && RunRandom.FloorSeed(d.Floor + 1) == svNextFloor
                    && string.Join(",", Pickup.All.Where(p => p.Price > 0).Select(p => p.Item.Id).OrderBy(x => x)) == svShop);
                yield return Shot("15b_continued");
                {
                    // Turn the current save into a version-1 file (no Essence fields) and load it.
                    SaveSystem.Save();
                    string v2 = File.ReadAllText(SaveSystem.FilePath);
                    string v1 = System.Text.RegularExpressions.Regex.Replace(v2, @"""saveVersion"":\s*\d+", "\"saveVersion\": 1");
                    v1 = System.Text.RegularExpressions.Regex.Replace(v1,
                        @"\\""(essence|forgesDone|essenceBadLuck|essenceRolls|essencePaid|essenceRefunded)\\"":\s*-?\d+,?", "");
                    File.WriteAllText(SaveSystem.FilePath, v1);
                    SaveSystem.ClearError();
                    run.Essence = 5; run.ForgesDone = 5;
                    bool oldLoaded = SaveSystem.LoadRun();
                    Check($"an old save (version 1, no Essence) still loads, with Essence 0 and forges done 0 (stripped: {v1.Length < v2.Length}; loaded {oldLoaded}, {SaveSystem.LastError ?? "no error"})",
                        v1.Length < v2.Length && v1.Contains("\"saveVersion\": 1") && oldLoaded && run.Essence == 0 && run.ForgesDone == 0);
                }

                // Broken files never crash: a damaged save falls back to its backup; both damaged hides Continue.
                SaveSystem.Save(); // the previous file becomes save.json.bak
                File.WriteAllText(SaveSystem.FilePath, "{ this is not a save");
                SaveSystem.ClearError();
                bool svBackupOk = SaveSystem.HasValidSave && SaveSystem.Read(SaveSystem.FilePath) == null && SaveSystem.LoadRun();
                string svBackupMsg = SaveSystem.LastError;
                File.WriteAllText(SaveSystem.FilePath, "garbage");
                File.WriteAllText(SaveSystem.BackupPath, "");
                SaveSystem.ClearError();
                bool svBothBad = !SaveSystem.HasValidSave && !SaveSystem.LoadRun() && SaveSystem.LastError != null && !SaveSystem.HasValidSave;
                var svFuture = SaveSystem.ToJson(SaveSystem.Capture()).Replace($"\"saveVersion\": {SaveSystem.Version}", "\"saveVersion\": 999");
                bool svNewer = SaveSystem.Parse(svFuture) == null;
                Check($"a damaged save falls back to the backup (\"{svBackupMsg}\"); two damaged files or a newer version hide Continue without crashing",
                    svBackupOk && svBothBad && svNewer);
                yield return null;
                SaveSystem.Save();
                SaveSystem.ClearError();
                MainMenu.Instance.StartGame();
                bool svAsks = MainMenu.Instance.Current == MainMenu.Page.Confirm && gs.Phase == Phase.Playing;
                MainMenu.Instance.Open(MainMenu.Page.Main);
                Check("New Run while a save exists asks before overwriting it", svAsks && SaveSystem.HasValidSave);
                Settings.SetGrimoireEnabled(false);
                yield return null;
                gs.ClearEnemies();
                ClearPickups();
            }

            // ---- Grimoire: elites in fight rooms, reward on room clear ----
            {
                var gmRoom = d.AllRooms.FirstOrDefault(r => r.Type == RoomType.Combat && !r.Cleared && r != combat);
                var gmR = Grimoire.GrimoireRun.Instance;
                if (gmRoom == null) Log("no second fight room for the Grimoire room check");
                else
                {
                    Settings.SetGrimoireEnabled(true);
                    float gmChance = Grimoire.GrimoireConfig.EliteChance;
                    Grimoire.GrimoireConfig.EliteChance = 1f;
                    yield return null;
                    int gmCurses = gmR.Deck.AllCards.Count(c => c.Def.IsCurse);
                    EnterRoom(player, gmRoom);
                    yield return new WaitForSeconds(1.6f);
                    var gmElite = Enemy.All.FirstOrDefault(e => e.IsElite);
                    Check($"Grimoire on: a fight room gets one elite ({(gmElite ? gmElite.Type.Name + ", " + gmElite.MaxHP.ToString("0") + " HP" : "none")})",
                        gmElite && Enemy.All.Count(e => e.IsElite) == 1);
                    if (gmElite)
                    {
                        player.LookAt(gmElite.Center);
                        yield return Shot("13h_grimoire_elite");
                    }
                    gs.ClearEnemies();
                    float gmT = 0f;
                    while (gmT < 6f && !gmR.Rewards.IsOpen)
                    {
                        if (Enemy.All.Count > 0) gs.ClearEnemies(); // queued spawns and split-off children
                        gmT += Time.unscaledDeltaTime;
                        yield return null;
                    }
                    int gmCursesNow = gmR.Deck.AllCards.Count(c => c.Def.IsCurse);
                    Check($"clearing a fight room opens the Grimoire reward ({gmR.Rewards.IsOpen}); the elite added a Fumble (curses {gmCurses} -> {gmCursesNow})",
                        gmR.Rewards.IsOpen && gmCursesNow == gmCurses + 1);
                    gmR.Rewards.Skip();
                    Grimoire.GrimoireConfig.EliteChance = gmChance;
                    Settings.SetGrimoireEnabled(false);
                    yield return new WaitForSeconds(0.5f);
                    ClearPickups();
                }
            }

            // ---- special rooms ----
            var treasure = d.FirstOfType(RoomType.Treasure);
            EnterRoom(player, treasure);
            yield return new WaitForSeconds(0.6f);
            player.LookAt(treasure.Center + Vector3.up * 1.5f);
            yield return Shot("15_treasure_room");
            var loot = Pickup.All.FirstOrDefault(p => p && treasure.Contains(p.transform.position) && !p.IsAuto);
            int items0 = run.Items.Count;
            Check("treasure room has an item that can be taken", loot && loot.TryCollect(run) && run.Items.Count == items0 + 1);

            var shop = d.FirstOfType(RoomType.Shop);
            EnterRoom(player, shop);
            yield return new WaitForSeconds(0.6f);
            player.LookAt(shop.Center + new Vector3(0, 1.2f, 4f));
            run.Gold = 25;
            yield return Shot("16_shop");
            int forSale = Pickup.All.Count(p => p && shop.Contains(p.transform.position) && p.Price > 0);
            Check($"shop sells 5 priced items ({forSale})", forSale == 5);

            var shrine = d.FirstOfType(RoomType.Shrine);
            EnterRoom(player, shrine);
            yield return new WaitForSeconds(0.6f);
            player.LookAt(shrine.Center + Vector3.up * 1.5f);
            yield return Shot("17_shrine");

            player.SetLook(90f, 0f); // face east: the map marker should point right
            HUD.ShowMap = true;
            yield return Shot("18_map");
            HUD.ShowMap = false;

            // ---- boss, portal, next floor ----
            int goldBefore = run.Gold, spellsBefore = SpellBook.Known.Count, itemsBefore = run.Items.Count;
            var bossRoom = d.FirstOfType(RoomType.Boss);
            EnterRoom(player, bossRoom);
            yield return new WaitForSeconds(2.5f);
            Check("boss spawns in the boss room", d.Boss && d.Boss.IsAlive && d.DoorsLocked);
            if (d.Boss) player.LookAt(d.Boss.Center);
            yield return Shot("19_boss");
            float waited = 0f;
            while (waited < 6f && Enemy.All.Count < 2) { waited += Time.deltaTime; yield return null; }
            Check("boss summons imps", Enemy.All.Count >= 2);
            float luck0 = run.Stats.Luck;
            run.Stats.Luck = 100f; // anywhere else this would guarantee drops
            int loot0 = Pickup.All.Count(p => p && bossRoom.Contains(p.transform.position));
            foreach (var minion in Enemy.All.Where(e => e != d.Boss).ToArray()) minion.TakeDamage(99999f);
            yield return new WaitForSeconds(0.6f);
            int loot1 = Pickup.All.Count(p => p && bossRoom.Contains(p.transform.position));
            run.Stats.Luck = luck0;
            Check($"boss minions drop nothing ({loot1 - loot0} new pickups)", loot1 == loot0);
            if (d.Boss) d.Boss.TakeDamage(99999f);
            yield return new WaitForSeconds(1.2f);
            Check("killing the boss kills its minions and opens the portal", Enemy.All.Count == 0 && d.PortalOpen && !d.DoorsLocked);
            player.LookAt(d.PortalPosition + Vector3.up * 1.5f);
            yield return Shot("20_portal");
            player.Teleport(d.PortalPosition + Vector3.up * 0.1f, 0f);
            yield return new WaitForSeconds(1f);
            Check("portal leads to floor 2", d.Floor == 2 && d.Current?.Type == RoomType.Start && d.AllRooms.Count == 12);
            Check("gold, spells and items carry over to the next floor",
                run.Gold == goldBefore && SpellBook.Known.Count == spellsBefore && run.Items.Count == itemsBefore);
            player.SetLook(0f, 0f);
            yield return Shot("21_floor2");

            // ---- floor 2: Frozen Caverns (own look, hazards, enemies and boss) ----
            {
                d.HoldEncounters = true;
                var fcRoom = d.AllRooms.Where(r => r.Type == RoomType.Combat).OrderByDescending(r => r.CellCount).First();
                int fcIce = IcePatch.All.Count, fcCrystals = Prop.All.Count(x => x && x.Kind == PropKind.Crystal);
                int fcPods = FindObjectsByType<CeilingPod>(FindObjectsSortMode.None).Length;
                int fcSpikes = FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None).Length;
                Check($"floor 2 has its own hazards: {fcIce} ice patches, {fcCrystals} frost crystals, {fcPods} ceiling pods, {fcSpikes} spike traps",
                    fcIce > 0 && fcCrystals > 0 && fcPods > 0 && fcSpikes == 0);
                EnterRoom(player, fcRoom);
                yield return new WaitForSeconds(0.3f);
                player.SetLook(0f, -5f);
                yield return Shot("21b_floor2_room");

                // Ice: the player's grip drops on a patch.
                var fcPatch = IcePatch.All.FirstOrDefault(x => x && x.Contains(x.transform.position));
                if (fcPatch)
                {
                    player.Teleport(new Vector3(fcPatch.transform.position.x, 0.1f, fcPatch.transform.position.z), 0f);
                    yield return null;
                    yield return null;
                    Check($"standing on ice you slide (grip {player.Grip:0.00})", Mathf.Approximately(player.Grip, IcePatch.Grip));
                }
                EnterRoom(player, fcRoom);
                // The entrance can itself be on a patch (random floors): step to plain ground.
                for (int fcTry = 0; fcTry < 24 && IcePatch.GripAt(player.transform.position) < 1f; fcTry++)
                {
                    float fcA = fcTry * 47f * Mathf.Deg2Rad, fcR = 2f + fcTry * 0.4f;
                    player.Teleport(d.FindFreeSpot(fcRoom, fcRoom.Center + new Vector3(Mathf.Cos(fcA), 0f, Mathf.Sin(fcA)) * fcR, 0.8f) + Vector3.up * 0.1f, 0f);
                }
                yield return null;
                yield return null;
                Check("off the ice, grip is back to normal", Mathf.Approximately(player.Grip, 1f));

                // Frost crystal: shooting it freezes the enemy beside it and chills you if you're close.
                var fcCrystal = Prop.Create(PropKind.Crystal, fcRoom.Root, AheadPoint(player, 2.5f, 0f));
                var fcImp = EnemySpawner.Instance.SpawnAt(EnemyType.Imp, fcCrystal.transform.position + player.transform.right * 1.5f);
                yield return null;
                player.ChillTimer = 0f;
                fcCrystal.TakeDamage(100f);
                yield return null;
                Check($"a shattered frost crystal freezes nearby enemies and chills you ({player.ChillTimer:0.0} s)", fcImp && fcImp.IsFrozen && player.ChillTimer > 1f);
                gs.ClearEnemies();
                player.ChillTimer = 0f;

                // Ceiling pod: its tentacle strikes when you walk under it.
                var fcPod = CeilingPod.Create(fcRoom.Root, player.transform.position + player.transform.forward * 0.5f);
                float fcT = 0f;
                while (fcT < 2f && fcPod.Strikes == 0) { fcT += Time.deltaTime; yield return null; }
                Check($"a ceiling pod strikes when you stand under it ({fcT:0.0} s)", fcPod.Strikes > 0);
                player.SetLook(0f, 40f);
                yield return Shot("21c_ceiling_pod");
                Destroy(fcPod.gameObject);
                player.SetLook(0f, 0f);

                // Floor 2 enemies.
                var fcTypes = new[] { EnemyType.Slug, EnemyType.Moth, EnemyType.Frog };
                var fcEnemies = new List<Enemy>();
                for (int i = 0; i < fcTypes.Length; i++)
                {
                    var fcE = SpawnAhead(player, fcTypes[i], 6f, (i - 1f) * 2.6f);
                    fcE.Freeze(4f);
                    fcEnemies.Add(fcE);
                }
                yield return new WaitForSeconds(0.3f);
                yield return Shot("21d_floor2_enemies");
                Check($"floor 2 enemies use the art pack: {string.Join(", ", fcTypes.Select(t => t.Name))}",
                    fcTypes.All(t => t.Sprites.Dead != null) && fcEnemies.All(e => e && e.IsAlive));
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.5f);
                d.HoldEncounters = false;

                // The Burrower: dives (can't be hurt underground) and comes up under you.
                var fcBossRoom = d.FirstOfType(RoomType.Boss);
                EnterRoom(player, fcBossRoom);
                yield return new WaitForSeconds(2.5f);
                var fcBoss = d.Boss;
                var fcBrain = fcBoss ? fcBoss.Brain as BurrowerBrain : null;
                Check($"floor 2 boss is {(fcBoss ? fcBoss.Type.Name : "none")}", fcBoss && fcBoss.Type == EnemyType.Burrower && fcBrain != null);
                if (fcBrain != null)
                {
                    player.LookAt(fcBoss.Center);
                    yield return Shot("21e_burrower");
                    fcBrain.Force = "dive";
                    fcT = 0f;
                    while (fcT < 4f && !fcBrain.Burrowed) { fcT += Time.deltaTime; yield return null; }
                    float fcHp = fcBoss.HP;
                    float fcDealt = fcBoss.TakeDamage(50f);
                    Check("the Burrower dives underground, where it can't be hurt", fcBrain.Burrowed && fcDealt == 0f && Mathf.Approximately(fcBoss.HP, fcHp) && !fcBoss.Hitbox.enabled);
                    fcT = 0f;
                    while (fcT < 5f && fcBrain.Burrowed) { fcT += Time.deltaTime; yield return null; }
                    Vector3 fcGap = fcBoss.transform.position - player.transform.position;
                    fcGap.y = 0f;
                    Check($"it surfaces near you ({fcGap.magnitude:0.0} m away) and can be hit again", !fcBrain.Burrowed && fcGap.magnitude < 4f && fcBoss.Hitbox.enabled && fcBrain.Dives >= 1);
                    yield return Shot("21f_burrower_surfaced");
                }
                if (fcBoss) fcBoss.Kill();
                yield return new WaitForSeconds(1.2f);
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.5f);
            }

            // ---- final floor: victory ----
            d.BuildFloor(Dungeon.FinalFloor);
            yield return new WaitForSeconds(0.5f);

            // ---- floor 3: The Forge ----
            {
                d.HoldEncounters = true;
                int ffLava = LavaPool.All.Count(x => x && !x.Temporary);
                int ffJets = FindObjectsByType<FireJet>(FindObjectsSortMode.None).Length;
                int ffSpikes = FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None).Length;
                Check($"floor 3 has its own hazards: {ffLava} lava pools, {ffJets} fire jets, {ffSpikes} spike traps, {IcePatch.All.Count} ice",
                    ffLava > 0 && ffJets > 0 && ffSpikes == 0 && IcePatch.All.Count == 0);
                var ffRoom = d.AllRooms.Where(r => r.Type == RoomType.Combat).OrderByDescending(r => r.CellCount).First();
                EnterRoom(player, ffRoom);
                yield return new WaitForSeconds(0.3f);
                player.SetLook(0f, -5f);
                yield return Shot("22a_floor3_room");

                // Lava burns walking enemies (and you); fire jets erupt on a cycle.
                var ffPool = LavaPool.All.First(x => x && !x.Temporary);
                var ffImp = EnemySpawner.Instance.SpawnAt(EnemyType.Imp, ffPool.transform.position);
                ffImp.Freeze(5f);
                var ffJet = FindObjectsByType<FireJet>(FindObjectsSortMode.None).First();
                var ffImp2 = EnemySpawner.Instance.SpawnAt(EnemyType.Imp, ffJet.transform.position);
                ffImp2.Freeze(6f);
                player.God = false;
                player.Heal(player.MaxHP);
                player.Teleport(new Vector3(ffPool.transform.position.x + 0.3f, 0.1f, ffPool.transform.position.z), 0f);
                float ffHp = player.HP;
                yield return new WaitForSeconds(1.2f);
                float ffBurn = ffHp - player.HP;
                player.God = true;
                player.Heal(player.MaxHP);
                Check($"lava burns you ({ffBurn:0} HP in 1.2 s) and walking enemies", ffBurn > 0f && (!ffImp || ffImp.HP < ffImp.MaxHP));
                float ffT = 0f;
                while (ffT < FireJet.Cycle + 0.5f && ffImp2 && ffImp2.IsAlive && Mathf.Approximately(ffImp2.HP, ffImp2.MaxHP)) { ffT += Time.deltaTime; yield return null; }
                Check($"fire jets erupt and burn what stands on them ({ffT:0.0} s)", !ffImp2 || ffImp2.HP < ffImp2.MaxHP);
                gs.ClearEnemies();
                EnterRoom(player, ffRoom);
                yield return new WaitForSeconds(0.4f);

                // Floor 3 enemies; the mimic sleeps as a chest until you come close.
                var ffTypes = new[] { EnemyType.Crawler, EnemyType.Robot, EnemyType.Mimic };
                var ffEnemies = new List<Enemy>();
                for (int i = 0; i < ffTypes.Length; i++) ffEnemies.Add(SpawnAhead(player, ffTypes[i], 6f, (i - 1f) * 2.8f));
                foreach (var e in ffEnemies.Take(2)) e.Freeze(4f);
                yield return null;
                // Checked at once: a hazard in the room may hit (and wake) it a moment later.
                var ffMimic = ffEnemies[2];
                var ffMb = ffMimic ? ffMimic.Brain as MimicBrain : null;
                bool ffDormant = ffMb != null && ffMb.Dormant;
                yield return new WaitForSeconds(0.4f);
                yield return Shot("22b_floor3_enemies");
                Check($"floor 3 enemies use the art pack: {string.Join(", ", ffTypes.Select(t => t.Name))}; the mimic waits as a chest",
                    ffTypes.All(t => t.Sprites.Dead != null) && ffDormant);
                if (ffMimic)
                {
                    player.Teleport(ffMimic.transform.position - player.transform.forward * 2f + Vector3.up * 0.1f, player.transform.eulerAngles.y);
                    ffT = 0f;
                    while (ffT < 1.5f && ffMb.Dormant) { ffT += Time.deltaTime; yield return null; }
                    Check($"the mimic wakes when you come close ({ffMb.Phase})", !ffMb.Dormant);
                    yield return new WaitForSeconds(0.4f);
                    player.LookAt(ffMimic.Center);
                    yield return Shot("22c_mimic");
                }
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.5f);
                d.HoldEncounters = false;
            }

            var finalBoss = d.FirstOfType(RoomType.Boss);
            EnterRoom(player, finalBoss);
            yield return new WaitForSeconds(2.5f);
            Check("floor 3 boss is tougher", d.Boss && d.Boss.MaxHP > EnemyType.Boss.MaxHP * 1.5f);
            {
                var bmBoss = d.Boss;
                var bmBrain = bmBoss ? bmBoss.Brain as BehemothBrain : null;
                Check($"floor 3 boss is {(bmBoss ? bmBoss.Type.Name : "none")}", bmBoss && bmBoss.Type == EnemyType.Behemoth && bmBrain != null);
                if (bmBrain != null)
                {
                    player.LookAt(bmBoss.Center);
                    yield return Shot("22d_behemoth");
                    bmBrain.Force = "leap";
                    float bmT = 0f;
                    while (bmT < 5f && !bmBrain.Airborne) { bmT += Time.deltaTime; yield return null; }
                    bool bmAir = bmBrain.Airborne && bmBoss.Invulnerable && !bmBoss.Hitbox.enabled;
                    player.LookAt(bmBoss.Center + Vector3.up * 5f);
                    yield return Shot("22e_behemoth_leap");
                    while (bmT < 8f && bmBrain.Airborne) { bmT += Time.deltaTime; yield return null; }
                    Check("the Behemoth leaps (can't be hurt in the air) and lands with a shockwave", bmAir && !bmBrain.Airborne && bmBrain.Leaps >= 1 && !bmBoss.Invulnerable);
                    int bmShots = bmBoss.ShotsFired, bmVolleys = bmBrain.SpikeVolleys;
                    bmBrain.Force = "spikes";
                    bmT = 0f;
                    while (bmT < 10f && bmBrain.SpikeVolleys == bmVolleys) { bmT += Time.deltaTime; yield return null; }
                    Check($"it fires a ring of spikes ({bmBoss.ShotsFired - bmShots} shots, volleys {bmBrain.SpikeVolleys}, phase {bmBrain.Phase}, frozen {bmBoss.IsFrozen}, stunned {bmBoss.IsStunned})", bmBrain.SpikeVolleys > bmVolleys && bmBoss.ShotsFired - bmShots >= 14);
                    bmBrain.Force = "roll";
                    bmT = 0f;
                    while (bmT < 8f && bmBrain.Rolls == 0) { bmT += Time.deltaTime; yield return null; }
                    yield return new WaitForSeconds(0.4f);
                    int bmTrail = LavaPool.All.Count(x => x && x.Temporary);
                    yield return Shot("22f_behemoth_roll");
                    Check($"it rolls at you, leaving burning ground ({bmTrail} patches)", bmBrain.Rolls >= 1 && bmTrail > 0);
                }
            }
            if (d.Boss) d.Boss.Kill();
            yield return new WaitForSeconds(1.2f);
            player.Teleport(d.PortalPosition + Vector3.up * 0.1f, 0f);
            yield return new WaitForSecondsRealtime(0.8f);
            Check($"endless: the floor 3 portal leads deeper instead of ending the run (now floor {d.Floor}: {Scenarios.NameFor(d.Floor)}, run still on)",
                gs.Phase == Phase.Playing && d.Floor == Dungeon.FinalFloor + 1);
            yield return Shot("22_endless_floor4");

            // ---- endless: scenario, boss and enemy rotation ----
            {
                var enThemes = new List<int>();
                var enBosses = new List<EnemyType>();
                for (int f = 1; f <= 30; f++) { enThemes.Add(Scenarios.ThemeFor(f)); enBosses.Add(Scenarios.BossFor(f)); }
                bool enClassic = enThemes[0] == 1 && enThemes[1] == 2 && enThemes[2] == 3
                    && enBosses[0] == EnemyType.Boss && enBosses[1] == EnemyType.Burrower && enBosses[2] == EnemyType.Behemoth;
                bool enNoRepeat = true;
                for (int i = 3; i < 30; i++) enNoRepeat &= enThemes[i] != enThemes[i - 1] && enBosses[i] != enBosses[i - 1];
                bool enAllSeen = Scenarios.All.All(s => enThemes.Skip(3).Contains(s.Theme)) && Scenarios.All.All(s => enBosses.Skip(3).Contains(s.Boss));
                int enSeed = RunRandom.RunSeed;
                var enAgain = Enumerable.Range(4, 20).Select(Scenarios.ThemeFor).ToList();
                RunRandom.RunSeed = enSeed + 7919;
                var enOther = Enumerable.Range(4, 20).Select(Scenarios.ThemeFor).ToList();
                RunRandom.RunSeed = enSeed;
                Check($"endless rotation: floors 1-3 are the classic order, then a seeded scenario and boss each floor, never the same twice in a row ({string.Join("", enThemes.Take(12))}...)",
                    enClassic && enNoRepeat && enAllSeen && enAgain.SequenceEqual(enThemes.Skip(3).Take(20)) && !enOther.SequenceEqual(enAgain));
                var enOwn = new HashSet<EnemyType>(EnemyType.Roster(Scenarios.ThemeFor(5), 3).Select(r => r.type));
                var enMix = Scenarios.Roster(5, 3);
                var enGuests = enMix.Where(r => !enOwn.Contains(r.type)).Select(r => r.type.Name).ToList();
                Check($"from floor 4, enemies mix the scenario's own roster with guests from other scenarios (floor 5 {Scenarios.NameFor(5)}: guests {string.Join(", ", enGuests)})",
                    enGuests.Count == Scenarios.GuestTypes && Scenarios.Roster(2, 3).All(r => EnemyType.Roster(2, 3).Contains(r)));
                float enHp3 = 1f + 0.4f * 2f;
                d.BuildFloor(6);
                yield return new WaitForSeconds(0.4f);
                Check($"a deep floor builds with its scenario and keeps getting harder (floor 6: {Scenarios.NameFor(6)}, enemy HP x{d.HpMult:0.0} vs x{enHp3:0.0} on floor 3, {d.AllRooms.Count} rooms, boss {Scenarios.BossFor(6).Name})",
                    d.Theme == Scenarios.ThemeFor(6) && d.HpMult > enHp3 && d.AllRooms.Count >= 10 && d.AllRooms.Count <= 20
                    && d.AllRooms.Any(r => r.Type == RoomType.Boss));
                yield return Shot("22h_floor6");

                // Deep floors: a gentler curve with more reward.
                Check($"deep floors rise more gently: enemy HP x{Difficulty.HpMult(3):0.00} on floor 3, x{Difficulty.HpMult(6):0.00} on 6, x{Difficulty.HpMult(10):0.00} on 10; damage x{Difficulty.DmgMult(10):0.00} on 10 (cap x{Difficulty.MaxDmgMult})",
                    Mathf.Approximately(Difficulty.HpMult(3), 1.8f) && Mathf.Approximately(Difficulty.DmgMult(3), 1.5f)
                    && Mathf.Approximately(Difficulty.HpMult(6), 1.8f + 3 * Difficulty.DeepHpPerFloor) && Difficulty.DmgMult(30) <= Difficulty.MaxDmgMult
                    && Mathf.Approximately(d.HpMult, Difficulty.HpMult(6)));
                Check($"deep floors pay more: coins x{Difficulty.CoinChanceMult(6):0.00} on floor 6, {Difficulty.BossItems(6)} boss rewards (1 on floors 1-3)",
                    Difficulty.CoinChanceMult(3) == 1f && Difficulty.CoinChanceMult(6) > 1f && Difficulty.BossItems(3) == 1 && Difficulty.BossItems(6) == 2);
                player.God = true;
                player.TakeDamage(player.MaxHP * 0.6f);
                float dpHp = player.HP;
                int dpFloor = d.Floor;
                d.EnterPortalForTest();
                yield return null;
                Check($"arriving on a deep floor heals {Difficulty.DeepArrivalHeal * 100f:0}% of max HP ({dpHp:0} -> {player.HP:0}, floor {dpFloor} -> {d.Floor})",
                    d.Floor == dpFloor + 1 && player.HP >= dpHp + player.MaxHP * Difficulty.DeepArrivalHeal - 1f);
            }

            // ---- meta progression: locked content, counters, achievements, new spells and items ----
            gs.ForcePlayingForTest();
            Progress.ResetForTest();
            Check("unlockable spells and items start locked and never drop",
                SpellBook.Available.Count == 8 && !SpellBook.Available.Any(sp => sp.StartsLocked) &&
                Enumerable.Range(0, 200).Select(_ => Items.RandomTreasure(run, 0.5f)).All(i => i.IsAvailable) &&
                Enumerable.Range(0, 50).Select(_ => Items.RandomConsumable()).All(i => i.IsAvailable));
            Progress.Tracking = true;
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.6f);
            ClearPickups();
            player.Teleport(d.Current.Center + new Vector3(0f, 0.1f, -8f), 0f);
            var tally = SpawnAhead(player, EnemyType.Imp, 6f, 0f);
            yield return new WaitForSeconds(0.3f);
            tally.Freeze(5f);
            for (int i = 0; i < 5 && tally.IsAlive; i++) { player.LookAt(tally.Center); ss.FireBasic(); yield return new WaitForSeconds(0.5f); }
            Check($"kills are counted, including magic-bolt kills ({Progress.Get("kills")} / {Progress.Get("bolt_kills")})",
                Progress.Get("kills") >= 1 && Progress.Get("bolt_kills") >= 1);
            Progress.Add("rooms", 10);
            Check("an achievement unlocks its reward (clear 10 rooms -> Poison Cloud)",
                Progress.IsUnlocked("spell:poisoncloud") && Progress.NewThisRun.Count == 1 && SpellBook.Available.Any(sp => sp.Id == "poisoncloud"));
            Progress.UnlockAllForTest();
            Progress.Tracking = false;
            Check("18 achievements unlock 6 spells and 12 items",
                Progress.All.Count == 18 && Progress.All.Count(a => a.IsSpell) == 6 && SpellBook.Available.Count == 14 &&
                Items.Passives.Concat(Items.Relics).Concat(Items.Consumables).Count(i => i.StartsLocked) == 12);

            for (int i = 0; i < Run.Slots; i++) run.Consumables[i] = null;
            int known0 = SpellBook.Known.Count;
            run.Acquire(Items.Get("rune_stone"));
            run.UseConsumable(0);
            Check($"Rune Stone teaches a spell you don't know ({known0} -> {SpellBook.Known.Count})", SpellBook.Known.Count == known0 + 1);
            SpellBook.LearnAll();
            ss.ResetCooldowns();

            // Poison Cloud: the hit target and its neighbour (inside the cloud) are poisoned
            var p1 = SpawnAhead(player, EnemyType.Cultist, 8f, 0f);
            var p2 = SpawnAhead(player, EnemyType.Cultist, 8.5f, 2f);
            yield return new WaitForSeconds(0.3f);
            p1.Freeze(6f); p2.Freeze(6f);
            player.LookAt(p1.Center);
            yield return CastSequence("LDR", expectReady: "Poison Cloud");
            ss.CastReadied();
            yield return new WaitForSeconds(1.4f);
            yield return Shot("23a_poison_cloud");
            Check("Poison Cloud poisons the target and enemies in the cloud",
                (!p1 || !p1.IsAlive || p1.IsPoisoned) && (!p2 || !p2.IsAlive || p2.IsPoisoned) && (!p2 || !p2.IsAlive || p2.HP < p2.MaxHP));
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.6f);

            // Arcane Missiles: five homing missiles spread over several targets
            var volley = new List<Enemy> { SpawnAhead(player, EnemyType.Cultist, 8f, -3f), SpawnAhead(player, EnemyType.Cultist, 9f, 0f), SpawnAhead(player, EnemyType.Cultist, 8f, 3f) };
            yield return new WaitForSeconds(0.3f);
            foreach (var e in volley) e.Freeze(6f);
            player.LookAt(volley[1].Center);
            yield return CastSequence("RUR", expectReady: "Arcane Missiles");
            ss.CastReadied();
            yield return null;
            yield return Shot("23b_arcane_missiles");
            yield return new WaitForSeconds(1.5f);
            float volleyDamage = volley.Sum(e => e ? e.MaxHP - Mathf.Max(0f, e.HP) : 60f);
            Check($"Arcane Missiles home in ({volleyDamage:0} damage over {volley.Count(e => !e || e.HP < e.MaxHP)} targets)",
                volleyDamage >= 60f && volley.Count(e => !e || e.HP < e.MaxHP) >= 2);
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.6f);

            // Blink: jump forward
            player.Teleport(d.Current.Center + new Vector3(0f, 0.1f, -8f), 0f);
            Vector3 before0 = player.transform.position;
            yield return CastSequence("ULU", expectReady: "Blink");
            ss.CastReadied();
            yield return null;
            Check($"Blink teleports forward ({(player.transform.position - before0).magnitude:0.0} m)", (player.transform.position - before0).z > 6f);

            // ---- Blink Dash and Quickening: Instant self-buffs (SelfSpells.cs) ----
            {
                var bdSpell = Spell("Blink Dash");
                var qkSpell = Spell("Quickening");
                var sbBases = SpellBook.Defaults();
                Check($"Blink Dash has the shortest rune in the game ({Arrows.Glyphs(bdSpell.Sequence)}; Quickening {Arrows.Glyphs(qkSpell.Sequence)}), both are Instant, every other spell Readied",
                    bdSpell.Sequence.Length == 2 && sbBases.All(sbS => sbS.Id == bdSpell.Id || sbS.Sequence.Length > 2)
                    && bdSpell.IsInstant && qkSpell.IsInstant && sbBases.Count(sbS2 => sbS2.IsInstant) == 2);
                var sbEvents = new List<string>();
                System.Action<string> sbHook = sbE => sbEvents.Add(sbE);
                ss.Event += sbHook;

                // Instant: ← → dashes at once, along the view, through an enemy; a readied Fireball stays readied.
                gs.ClearEnemies();
                ss.ResetCooldowns();
                ss.SetChanneling(false);
                player.Teleport(d.FindFreeSpot(d.Current, d.Current.Center, 0.8f) + Vector3.up * 0.1f, 0f);
                yield return null;
                float sbYaw = ClearHeading(player, 9f);
                Vector3 sbDir = Quaternion.Euler(0f, sbYaw, 0f) * Vector3.forward;
                player.SetLook(sbYaw, 0f);
                Vector3 sbFrom = player.transform.position;
                var sbImp = EnemySpawner.Instance.SpawnAt(EnemyType.Imp, sbFrom + sbDir * 3f);
                yield return new WaitForSeconds(0.2f);
                if (sbImp) sbImp.Freeze(6f);
                player.God = false;
                player.Heal(player.MaxHP);
                float sbHp = player.HP;
                ss.ReadyForTest(Spell("Fireball"));
                int sbDashes = player.DashCount;
                sbEvents.Clear();
                yield return CastSequence("LR", expectReady: null);
                bool sbInvuln = player.Invulnerable;
                player.TakeDamage(25f); // during the dash: no damage
                // An enemy shot flying straight at you during the dash passes through.
                if (sbImp) sbImp.Shoot(player.ChestPosition + sbDir * 2.5f, -sbDir, 25f, Color.red, 0.6f, 10f);
                yield return Shot("24a_blink_dash");
                ss.SetChanneling(false);
                yield return new WaitForSeconds(0.5f);
                Vector3 sbMoved = player.transform.position - sbFrom;
                sbMoved.y = 0f;
                float sbDist0 = sbMoved.magnitude;
                Check($"Blink Dash goes off the moment ← → is drawn, no click: {sbMoved.magnitude:0.00} m along the view (Fireball still readied: {ss.Readied?.Name}; cooldown {ss.CooldownRemaining(bdSpell):0.0}s; events {string.Join(", ", sbEvents)})",
                    sbEvents.Contains("instant Blink Dash") && player.DashCount == sbDashes + 1 && sbMoved.magnitude > 5.4f && sbMoved.magnitude < 6.6f
                    && Vector3.Dot(sbMoved.normalized, sbDir) > 0.95f && ss.Readied != null && ss.Readied.Name == "Fireball" && ss.OnCooldown(bdSpell));
                Check($"the dash passes through the enemy in its way ({(sbImp ? Vector3.Dot(player.transform.position - sbImp.transform.position, sbDir) : 0f):0.0} m past it)",
                    sbImp && sbImp.IsAlive && Vector3.Dot(player.transform.position - sbImp.transform.position, sbDir) > 1.5f);
                Check($"no damage during the dash, from a hit or an enemy shot (HP {sbHp:0} -> {player.HP:0}, invulnerable during it: {sbInvuln}, after: {player.Invulnerable})",
                    sbInvuln && Mathf.Approximately(player.HP, sbHp) && !player.Invulnerable);
                player.TakeDamage(5f);
                Check("after the dash damage applies again", player.HP < sbHp);
                player.Heal(player.MaxHP);
                ss.ClearReadied();
                gs.ClearEnemies();

                // Walls: straight on, it stops at the wall; at an angle, it slides along it. Never inside it.
                yield return new WaitForSeconds(0.3f);
                player.Teleport(d.FindFreeSpot(d.Current, d.Current.Center, 0.8f) + Vector3.up * 0.1f, 0f);
                yield return null;
                sbYaw = ClearHeading(player, 9f);
                sbDir = Quaternion.Euler(0f, sbYaw, 0f) * Vector3.forward;
                Vector3 wlFrom = player.transform.position;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.layer = Layers.World;
                wall.transform.position = wlFrom + sbDir * 3f + Vector3.up * 1.5f;
                wall.transform.rotation = Quaternion.LookRotation(sbDir);
                wall.transform.localScale = new Vector3(10f, 3f, 0.4f);
                Physics.SyncTransforms();
                player.SetLook(sbYaw, 0f);
                ss.ResetCooldowns();
                yield return CastSequence("LR", expectReady: null);
                ss.SetChanneling(false);
                yield return new WaitForSeconds(0.4f);
                float wlAlong = Vector3.Dot(player.transform.position - wlFrom, sbDir);
                bool wlInside = PlayerInSolid(player);
                Check($"Blink Dash stops at a wall: {wlAlong:0.00} m of 6 (the wall is 2.8 m away), not inside it", wlAlong > 1.8f && wlAlong < 2.6f && !wlInside);
                // The same wall turned 45 degrees: the dash slides along it.
                player.Teleport(wlFrom, sbYaw);
                wall.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, 45f, 0f) * sbDir);
                Physics.SyncTransforms();
                yield return null;
                Vector3 wlN = wall.transform.forward;
                float wlSide0 = Mathf.Sign(Vector3.Dot(wlFrom - wall.transform.position, wlN));
                ss.ResetCooldowns();
                yield return CastSequence("LR", expectReady: null);
                ss.SetChanneling(false);
                yield return new WaitForSeconds(0.4f);
                Vector3 wlMoved = player.transform.position - wlFrom;
                wlMoved.y = 0f;
                float wlSlide = Vector3.Cross(sbDir, wlMoved).magnitude; // sideways, along the wall
                float wlGap = Vector3.Dot(player.transform.position - wall.transform.position, wlN) * wlSide0;
                Check($"at an angle it slides along the wall ({wlSlide:0.0} m sideways, {wlGap:0.00} m in front of it) and never clips in",
                    wlSlide > 0.8f && wlGap > 0.3f && !PlayerInSolid(player));
                UnityEngine.Object.Destroy(wall);
                yield return null;

                // Momentum: dashing while running; the run carries on after it.
                player.Teleport(d.FindFreeSpot(d.Current, d.Current.Center, 0.8f) + Vector3.up * 0.1f, 0f);
                yield return null;
                sbYaw = ClearHeading(player, 9f);
                player.SetLook(sbYaw, 0f);
                ss.ResetCooldowns();
                ss.SetChanneling(false);
                ss.ClearFreshPressForTest();
                Settings.SetCastingStyle(CastingStyle.Momentum);
                void MdFrame(bool mdHeld, int mdPress = -1, int mdHold = -1)
                {
                    var mdPr = new bool[4]; var mdHd = new bool[4];
                    if (mdPress >= 0) { mdPr[mdPress] = true; mdHd[mdPress] = true; }
                    if (mdHold >= 0) mdHd[mdHold] = true;
                    ss.ProcessMomentumInput(mdHeld, mdPr, mdHd, Time.deltaTime);
                }
                MdFrame(true, mdHold: 0);        // cast key down while W is held: run forward
                yield return null;
                MdFrame(true);                 // W released
                yield return new WaitForSeconds(0.3f);
                int mdDashes = player.DashCount;
                MdFrame(true, mdPress: 1);       // A: left
                yield return null;
                MdFrame(true);
                MdFrame(true, mdPress: 3);       // D: right -> Blink Dash
                yield return null;
                bool mdDashing = player.Dashing;
                yield return new WaitForSeconds(0.45f);
                float mdWant = run.MoveSpeed * MomentumConfig.SpeedMultiplier;
                Check($"Momentum: A D dashes while running, and the run carries on after it (speed {player.PlanarSpeed:0.0} of {mdWant:0.0}, still running: {player.AutoForward})",
                    player.DashCount == mdDashes + 1 && mdDashing && ss.InMomentum && player.AutoForward && Mathf.Abs(player.PlanarSpeed - mdWant) < 0.6f);
                for (int mdI = 0; mdI < 20 && ss.Channeling; mdI++) { MdFrame(false); yield return null; }
                Settings.SetCastingStyle(CastingStyle.Classic);
                ss.SetChanneling(false);
                yield return new WaitForSeconds(0.3f);

                // Quickening: +30% speed and 3 HP/s for 6 s; the health bar glows, no "+N" pop.
                gs.ClearEnemies();
                ss.ResetCooldowns();
                run.HasteTimer = 0f;
                float qkBase = run.MoveSpeed;
                player.Heal(player.MaxHP);
                player.TakeDamage(40f);
                yield return new WaitForSeconds(0.7f); // the hurt flash and any heal pop fade
                float qkHp = player.HP;
                sbEvents.Clear();
                yield return CastSequence("DUD", expectReady: null);
                ss.SetChanneling(false);
                bool qkOn = run.Quickened && Mathf.Abs(run.QuickenTimer - 6f) < 0.3f;
                float qkSpeed = run.MoveSpeed;
                yield return new WaitForSeconds(1f);
                float qkHealed = player.HP - qkHp;
                bool qkQuiet = player.HealFlash <= 0f;
                yield return Shot("24b_quickening_hud");
                Check($"Quickening goes off at once: speed {qkBase:0.00} -> {qkSpeed:0.00} (+30%), {qkHealed:0.0} HP back in 1 s (3 x spell power {run.Stats.Power:0.00}), on: {qkOn}, the bar glows instead of a +N pop (events {string.Join(", ", sbEvents)})",
                    sbEvents.Contains("instant Quickening") && qkOn && Mathf.Abs(qkSpeed - qkBase * 1.3f) < 0.01f && Mathf.Abs(qkHealed - 3f * run.Stats.Power) < 0.6f && qkQuiet);
                Check($"Quickening also speeds up Momentum's running ({run.MoveSpeed * MomentumConfig.SpeedMultiplier:0.00} vs {qkBase * MomentumConfig.SpeedMultiplier:0.00})",
                    run.MoveSpeed * MomentumConfig.SpeedMultiplier > qkBase * MomentumConfig.SpeedMultiplier * 1.29f);
                float qkLeft = run.QuickenTimer;
                ss.ResetCooldowns();
                yield return CastSequence("DUD", expectReady: null);
                ss.SetChanneling(false);
                Check($"recasting refreshes it ({qkLeft:0.0} s left -> {run.QuickenTimer:0.0} s) without stacking (speed {run.MoveSpeed:0.00})",
                    run.QuickenTimer > 5.7f && run.QuickenTimer <= 6.01f && Mathf.Abs(run.MoveSpeed - qkBase * 1.3f) < 0.01f);
                run.HasteTimer = 5f;
                float qkBoth = run.MoveSpeed;
                Check($"the Haste potion stacks with it ({qkBoth:0.00} = {qkBase:0.00} x 1.35 x 1.3)", Mathf.Abs(qkBoth - qkBase * 1.35f * 1.3f) < 0.01f);
                run.HasteTimer = 0f;
                // The dash distance doesn't grow with the speed bonus.
                player.Teleport(d.FindFreeSpot(d.Current, d.Current.Center, 0.8f) + Vector3.up * 0.1f, 0f);
                yield return null;
                sbYaw = ClearHeading(player, 9f);
                player.SetLook(sbYaw, 0f);
                sbFrom = player.transform.position;
                ss.ResetCooldowns();
                yield return CastSequence("LR", expectReady: null);
                ss.SetChanneling(false);
                yield return new WaitForSeconds(0.4f);
                sbMoved = player.transform.position - sbFrom;
                sbMoved.y = 0f;
                Check($"Blink Dash goes as far while Quickened ({sbMoved.magnitude:0.00} m, {sbDist0:0.00} m without)", Mathf.Abs(sbMoved.magnitude - sbDist0) < 0.15f);
                run.QuickenTimer = 0.05f;
                yield return new WaitForSeconds(0.2f);
                Check($"when it ends the speed is back ({run.MoveSpeed:0.00})", !run.Quickened && Mathf.Abs(run.MoveSpeed - qkBase) < 0.01f);
                ss.Event -= sbHook;

                // Grimoire: every card does something to both spells (Blank: nothing).
                var sbNoEffect = new List<string>();
                foreach (var sbCard in Grimoire.Cards.All)
                    foreach (var sbSp in new[] { bdSpell, qkSpell })
                    {
                        var sbReq = new CastRequest { Spell = sbSp };
                        Grimoire.Cards.Apply(sbCard, new Grimoire.CardContext { Request = sbReq, Spell = sbSp, Shape = Grimoire.Cards.ShapeOf(sbSp), Player = player });
                        bool sbChanged = CardChanged(sbReq);
                        if (sbCard.Id == "blank" ? sbChanged : !sbChanged) sbNoEffect.Add(sbCard.Name + " on " + sbSp.Name);
                    }
                Check($"every Grimoire card changes Blink Dash and Quickening, Blank changes nothing ({(sbNoEffect.Count == 0 ? "all ok" : string.Join(", ", sbNoEffect))})", sbNoEffect.Count == 0);

                // Cards on the dash, played for real.
                IEnumerator CardDash(string cdCard)
                {
                    player.Teleport(d.FindFreeSpot(d.Current, d.Current.Center, 0.8f) + Vector3.up * 0.1f, 0f);
                    yield return null;
                    float cdYaw = ClearHeading(player, 12f);
                    player.SetLook(cdYaw, 0f);
                    ss.ResetCooldowns();
                    yield return null;
                }
                void PlayCard(SpellDefinition pcSpell, string pcCard)
                {
                    var pcReq = new CastRequest { Spell = pcSpell };
                    Grimoire.Cards.Apply(Grimoire.Cards.Get(pcCard), new Grimoire.CardContext { Request = pcReq, Spell = pcSpell, Shape = Grimoire.Cards.ShapeOf(pcSpell), Player = player });
                    if (pcReq.Fizzle) return;
                    foreach (var pcM in pcReq.Copies) new SpellCast(pcSpell, player) { Power = run.Stats.Power * pcM.DamageMult, Mods = pcM }.Launch();
                    pcReq.AfterLaunch?.Invoke();
                }
                Vector3 AheadOf(float aoDist, float aoSide = 0f) =>
                    player.transform.position + Quaternion.Euler(0f, player.Yaw, 0f) * new Vector3(aoSide, 0f, aoDist);

                yield return CardDash("split");
                int cdN = player.DashCount;
                PlayCard(bdSpell, "split");
                yield return new WaitForSeconds(0.8f);
                Check($"Split: three short dashes chained ({player.DashCount - cdN})", player.DashCount == cdN + 3);

                yield return CardDash("echo");
                cdN = player.DashCount;
                PlayCard(bdSpell, "echo");
                yield return new WaitForSeconds(0.3f);
                int cdEarly = player.DashCount - cdN;
                yield return new WaitForSeconds(0.5f);
                Check($"Echo: a second dash 0.5 s later ({cdEarly} after 0.3 s, {player.DashCount - cdN} after 0.8 s)", cdEarly == 1 && player.DashCount == cdN + 2);

                yield return CardDash("pierce");
                var cdVictim = EnemySpawner.Instance.SpawnAt(EnemyType.Cultist, AheadOf(3f));
                yield return new WaitForSeconds(0.2f);
                if (cdVictim) cdVictim.Freeze(4f);
                PlayCard(bdSpell, "pierce");
                yield return new WaitForSeconds(0.4f);
                Check($"Pierce: the dash hurts the enemy it passes through ({(cdVictim ? cdVictim.HP : 0):0}/{(cdVictim ? cdVictim.MaxHP : 0):0})", cdVictim && cdVictim.HP < cdVictim.MaxHP);
                gs.ClearEnemies();

                yield return CardDash("burning");
                int cdLava = LavaPool.All.Count;
                float cdHp = player.HP;
                PlayCard(bdSpell, "burning");
                yield return new WaitForSeconds(1.2f);
                Check($"Burning: a trail of fire ({LavaPool.All.Count - cdLava} patches) that never burns you (HP {cdHp:0} -> {player.HP:0})",
                    LavaPool.All.Count - cdLava >= 3 && Mathf.Approximately(player.HP, cdHp));

                yield return CardDash("frozen");
                var cdIce = EnemySpawner.Instance.SpawnAt(EnemyType.Cultist, AheadOf(-2.5f));
                var cdPush = EnemySpawner.Instance.SpawnAt(EnemyType.Cultist, AheadOf(0f, 2.5f));
                yield return new WaitForSeconds(0.2f);
                if (cdPush) cdPush.Freeze(4f);
                PlayCard(bdSpell, "frozen");
                yield return null;
                Check("Frozen: enemies where the dash starts are frozen", cdIce && cdIce.IsFrozen);
                Vector3 cdStart = player.transform.position;
                float cdPush0 = cdPush ? Vector3.Distance(cdPush.transform.position, cdStart) : 0f;
                ss.ResetCooldowns();
                PlayCard(bdSpell, "wide");
                yield return new WaitForSeconds(0.5f);
                float cdPush1 = cdPush ? Vector3.Distance(cdPush.transform.position, cdStart) : 0f;
                Check($"Wide: a knockback burst where the dash starts ({cdPush0:0.0} -> {cdPush1:0.0} m)", cdPush && cdPush1 > cdPush0 + 1f);
                gs.ClearEnemies();

                // Cards on Quickening.
                yield return new WaitForSeconds(0.3f);
                var cdBurn = EnemySpawner.Instance.SpawnAt(EnemyType.Cultist, AheadOf(1.1f));
                var cdSlow = EnemySpawner.Instance.SpawnAt(EnemyType.Cultist, AheadOf(-3f));
                yield return new WaitForSeconds(0.2f);
                // Put them exactly in place (a spawn can be nudged to a free spot further away).
                if (cdBurn) { cdBurn.Warp(AheadOf(1.1f)); cdBurn.Freeze(4f); }
                if (cdSlow) { cdSlow.Warp(AheadOf(-3f)); cdSlow.Freeze(4f); }
                PlayCard(qkSpell, "burning");
                yield return new WaitForSeconds(0.7f);
                Check($"Quickening + Burning: enemies touching you catch fire ({(cdBurn ? Vector3.Distance(cdBurn.transform.position, player.transform.position) : -1f):0.0} m away, burning {(cdBurn && cdBurn.IsBurning)})", cdBurn && cdBurn.IsBurning);
                PlayCard(qkSpell, "frozen");
                yield return new WaitForSeconds(1.2f);
                Check("Quickening + Frozen: enemies near you are slowed while it lasts", cdSlow && cdSlow.IsSlowed);
                gs.ClearEnemies();
                player.ChillTimer = 3f;
                PlayCard(qkSpell, "pierce");
                Check("Quickening + Pierce: clears your slows", player.ChillTimer <= 0f);
                PlayCard(qkSpell, "echo");
                Check($"Quickening + Echo: lasts 50% longer ({run.QuickenTimer:0.0} s)", Mathf.Abs(run.QuickenTimer - 9f) < 0.1f);
                PlayCard(qkSpell, "overload");
                Check($"Quickening + Overload: lasts twice as long ({run.QuickenTimer:0.0} s)", Mathf.Abs(run.QuickenTimer - 12f) < 0.1f);
                PlayCard(qkSpell, "amplify");
                Check($"Quickening + Amplify: +50% speed and regen ({run.QuickenSpeed:0.00} speed, {run.QuickenRegen:0.0} HP/s)",
                    Mathf.Abs(run.QuickenSpeed - 0.45f) < 0.01f && Mathf.Abs(run.QuickenRegen - 4.5f * run.Stats.Power) < 0.05f);
                PlayCard(qkSpell, "split");
                Check($"Quickening + Split (solo): +1 regen ({run.QuickenRegen:0.0} HP/s)", Mathf.Abs(run.QuickenRegen - (3f * run.Stats.Power + 1f)) < 0.05f);
                run.QuickenTimer = 0f;

                // Forge: they fuse like any spell (the result is Readied, with a long rune).
                var fgOk = SpellForge.TryCombine(Spell("Fireball"), bdSpell, new List<SpellDefinition> { Spell("Fireball"), bdSpell }, out var fgMade, out var fgErr);
                var fgPairOk = SpellForge.TryCombine(Spell("Frost Shard"), Spell("Lightning"), new List<SpellDefinition> { Spell("Frost Shard"), Spell("Lightning") }, out var fgPair, out var fgPairErr);
                Check($"Frost Shard (←...) + Lightning (→...) still fuse although ← → is Blink Dash's rune ({(fgPair != null ? Arrows.Glyphs(fgPair.Sequence) : fgPairErr)})",
                    fgPairOk && fgPair != null && sbBases.All(fgB2 => !Arrows.Conflicts(fgB2.Sequence, fgPair.Sequence)));
                Check($"the Forge fuses Fireball + Blink Dash into {fgMade?.Name} ({(fgMade != null ? Arrows.Glyphs(fgMade.Sequence) : fgErr)}), Readied, still dashing",
                    fgOk && fgMade != null && !fgMade.IsInstant && fgMade.Sequence.Length >= 5 && fgMade.Payload.Exists(fgE => fgE is DashEffect)
                    && sbBases.All(fgB => !Arrows.Conflicts(fgB.Sequence, fgMade.Sequence)));

                // An old save with a forged spell whose rune now starts with ← →: it gets a new rune when loaded.
                var fxS0 = ss.SaveState();
                SpellForge.TryCombine(Spell("Frost Shard"), Spell("Lightning"), new List<SpellDefinition> { Spell("Frost Shard"), Spell("Lightning") }, out var fxOld, out var fxErr);
                if (fxOld == null) Log("forge for the old-save test failed: " + fxErr);
                if (fxOld != null) fxOld.Sequence = Arrows.Parse("LRUD");
                var fxS1 = ss.SaveState();
                if (fxOld != null) fxS1.known.Add(SpellCodec.ToData(fxOld));
                ss.LoadState(fxS1);
                var fxGot = SpellBook.Known.Last();
                bool fxFree = true;
                var fxBook = SpellBook.Known;
                for (int fxI = 0; fxI < fxBook.Count; fxI++)
                    for (int fxJ = fxI + 1; fxJ < fxBook.Count; fxJ++)
                        if (Arrows.Conflicts(fxBook[fxI].Sequence, fxBook[fxJ].Sequence)) fxFree = false;
                Check($"an old forged spell whose rune Blink Dash now blocks gets a new one on load (LRUD -> {Arrows.ToText(fxGot.Sequence)}, toast \"{run.ToastTitle}\")",
                    fxGot.IsForged && fxGot.Sequence[0] == Arrow.Left && Arrows.ToText(fxGot.Sequence) != "LRUD" && fxFree && run.ToastTitle != null && run.ToastTitle.StartsWith("New rune"));
                ss.LoadState(fxS0);
                ss.ResetCooldowns();
                player.God = true;
                player.Heal(player.MaxHP);
                gs.ClearEnemies();
                yield return new WaitForSeconds(0.5f);
            }

            // Quake: stuns and damages everything around
            var ringQ = new List<Enemy>();
            for (int i = 0; i < 3; i++)
            {
                float ang = i * 120f * Mathf.Deg2Rad;
                ringQ.Add(EnemySpawner.Instance.SpawnAt(EnemyType.Imp, player.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 4f));
            }
            yield return new WaitForSeconds(0.3f);
            yield return CastSequence("DLD", expectReady: "Quake");
            ss.CastReadied();
            yield return new WaitForSeconds(0.15f);
            Check("Quake stuns and damages everything around you", ringQ.All(e => e && (!e.IsAlive || (e.IsStunned && e.HP < e.MaxHP))));
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.6f);

            // Life Drain: damage that heals
            player.God = false;
            player.Heal(player.MaxHP);
            player.TakeDamage(50f);
            float hpDrain = player.HP;
            var drained = SpawnAhead(player, EnemyType.Cultist, 7f, 0f);
            yield return new WaitForSeconds(0.3f);
            drained.Freeze(4f);
            player.LookAt(drained.Center);
            yield return CastSequence("LUL", expectReady: "Life Drain");
            ss.CastReadied();
            yield return null;
            Check($"Life Drain damages and heals you ({hpDrain:0} -> {player.HP:0})", player.HP > hpDrain && drained.HP < drained.MaxHP);
            gs.ClearEnemies();
            yield return null;
            // An area spell with two lifesteal effects on a crowd: only the larger counts, and the cast heals at most the cap.
            var lsSpell = new SpellDefinition
            {
                Id = "test_lifesteal_nova", Name = "Test Drain Nova", Color = Color.red, Sequence = new Arrow[0],
                Delivery = new NovaDelivery(8f),
                Payload = { new DamageEffect(40f, Element.Life), new LifestealEffect(0.35f), new LifestealEffect(0.15f) },
            };
            for (int i = 0; i < 5; i++) SpawnAhead(player, EnemyType.Imp, 4f, (i - 2f) * 1.4f).Freeze(4f);
            yield return new WaitForSeconds(0.2f);
            player.Heal(player.MaxHP);
            player.TakeDamage(60f);
            float lsHp = player.HP;
            float lsKillHeal = run.Stats.HealOnKill;
            run.Stats.HealOnKill = 0f; // Vampire Fang would add its own healing to the kills
            var lsCast = new SpellCast(lsSpell, player);
            lsCast.Launch();
            yield return null;
            Check($"lifesteal is capped per cast ({lsCast.DamageDealt:0} dmg -> healed {lsCast.LifestealHealed:0}, cap {SpellCast.MaxLifestealPerCast:0}, HP +{player.HP - lsHp:0.##}, kills {lsCast.Kills})",
                lsCast.DamageDealt * 0.35f > SpellCast.MaxLifestealPerCast && Mathf.Approximately(lsCast.LifestealHealed, SpellCast.MaxLifestealPerCast)
                && Mathf.Abs(player.HP - lsHp - SpellCast.MaxLifestealPerCast) < 0.01f);
            run.Stats.HealOnKill = lsKillHeal;
            player.God = true;
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.6f);

            // Meteor: falls on the crosshair spot and flattens a group
            var crater = new List<Enemy> { SpawnAhead(player, EnemyType.Imp, 9f, 0f), SpawnAhead(player, EnemyType.Imp, 9.8f, 1.5f), SpawnAhead(player, EnemyType.Imp, 9.6f, -1.5f) };
            yield return new WaitForSeconds(0.3f);
            foreach (var e in crater) e.Freeze(6f);
            player.LookAt(crater[0].transform.position);
            yield return CastSequence("RDL", expectReady: "Meteor");
            ss.CastReadied();
            yield return new WaitForSeconds(0.35f);
            yield return Shot("23c_meteor");
            yield return new WaitForSeconds(1.2f);
            Check("Meteor kills the whole group", crater.All(e => !e || !e.IsAlive));
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.6f);
            ClearPickups();

            // New items
            run.Acquire(Items.Get("bolt_splitter"));
            int bolts0 = FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length;
            player.SetLook(0f, 0f);
            ss.FireBasic();
            Check("Bolt Splitter: the magic bolt fires 3 bolts", FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length - bolts0 == 3);
            run.Acquire(Items.Get("gold_tooth"));
            int goldT = run.Gold;
            Pickup.Spawn(Items.Coin, player.transform.position + player.transform.forward * 0.6f);
            yield return new WaitForSeconds(0.9f);
            Check("Gold Tooth: coins are worth 2", run.Gold == goldT + 2);
            int mgGold = run.Gold;
            var mgCoin = Pickup.Spawn(Items.Coin, player.transform.position + player.transform.forward * (Pickup.CoinMagnetRadius - 1f));
            float mgT = 0f;
            while (mgT < 3f && mgCoin) { mgT += Time.deltaTime; yield return null; }
            Check($"coins {Pickup.CoinMagnetRadius - 1f:0} m away drift to you ({mgT:0.0} s)", !mgCoin && run.Gold > mgGold);
            gs.SetFocus(false);
            yield return null;
            yield return Shot("21b_pause_items");
            gs.ClearFocusPause();
            yield return null;
            run.Acquire(Items.Get("phoenix"));
            player.God = false;
            player.TakeDamage(99999f);
            Check($"Phoenix Feather survives a lethal hit at half HP ({player.HP:0})",
                !player.IsDead && gs.Phase == Phase.Playing && Mathf.Approximately(player.HP, player.MaxHP * 0.5f) && !run.Has("phoenix"));
            player.God = true;
            run.Acquire(Items.Get("toxic_vial"));
            ss.ResetCooldowns();
            var vialTarget = SpawnAhead(player, EnemyType.Cultist, 7f, 0f);
            yield return new WaitForSeconds(0.3f);
            vialTarget.Freeze(4f);
            player.LookAt(vialTarget.Center);
            yield return CastSequence("LUR", expectReady: "Frost Shard");
            ss.CastReadied();
            yield return new WaitForSeconds(0.5f);
            Check("Toxic Vial: spell hits poison", !vialTarget.IsAlive || vialTarget.IsPoisoned);
            run.Acquire(Items.Get("overcharge"));
            ss.ResetCooldowns();
            yield return CastSequence("LUR", expectReady: "Frost Shard");
            yield return new WaitForSeconds(1.6f);
            ss.CastReadied();
            Check("Overcharge: a spell kept ready for 1.5s is overcharged", ss.LastMessage == "Overcharged!");
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.6f);
            for (int i = 0; i < Run.Slots; i++) run.Consumables[i] = null;
            run.Acquire(Items.Get("time_scroll"));
            run.UseConsumable(0);
            Check("Time Scroll: enemies at half speed", Mathf.Approximately(Enemy.TimeFactor, 0.5f));
            run.SlowTimer = 0f;
            var smoked = SpawnAhead(player, EnemyType.Imp, 5f, 0f);
            yield return new WaitForSeconds(0.3f);
            run.Acquire(Items.Get("smoke_bomb"));
            run.UseConsumable(0);
            Check("Smoke Bomb: nearby enemies are stunned", smoked.IsStunned);
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.4f);
            run.Acquire(Items.Get("thorn_robe"));
            var biter = SpawnAhead(player, EnemyType.Imp, 1.6f, 0f);
            float tw = 0f;
            while (tw < 5f && biter && biter.IsAlive && Mathf.Approximately(biter.HP, biter.MaxHP)) { tw += Time.deltaTime; yield return null; }
            Check("Thorned Robe: biting the wizard hurts the imp", !biter || biter.HP < biter.MaxHP);
            gs.ClearEnemies();
            yield return new WaitForSeconds(0.6f);
            ClearPickups();

            // ---- practice tool ----
            gs.ForcePlayingForTest();
            gs.EnterPracticeForTest();
            var practice = PracticeTool.Instance;
            yield return new WaitForSeconds(0.6f);
            Check($"practice tool: every spell known, a target, {Enemy.All.Count} dummies",
                SpellBook.Available.All(s => SpellBook.Knows(s.Id)) && practice.Active && practice.Target != null && Enemy.All.Count >= PracticeTool.DummyCount);
            var target = practice.Target;
            ss.ResetCooldowns();
            events.Clear();
            yield return CastSequence(Arrows.ToText(target.Sequence), expectReady: target.Name);
            yield return null;
            Log($"practice target {target.Name} {Arrows.Glyphs(target.Sequence)} lockout={ss.Lockout:0.00} events: {string.Join(", ", events)} | {practice.Feedback}");
            Check($"practice: drawing the target ({target.Name}) scores a hit and picks a new target", practice.Successes == 1 && practice.Target != target);
            yield return Shot("23_practice");
            ss.CastReadied();
            Check("practice: no cooldowns", !ss.OnCooldown(target));
            // A two-stroke start that no known spell begins with, so it must fizzle.
            string badStart = null;
            foreach (var a in new[] { Arrow.Up, Arrow.Down, Arrow.Left, Arrow.Right })
            foreach (var b in new[] { Arrow.Up, Arrow.Down, Arrow.Left, Arrow.Right })
                if (badStart == null && a != b && !SpellBook.Known.Any(sp => Arrows.HasPrefix(sp.Sequence, new[] { a, b })))
                    badStart = Arrows.ToText(new[] { a, b });
            yield return CastSequence(badStart, expectReady: null);
            Check($"practice: a fizzle is reported with the strokes read ({practice.Feedback})", practice.Attempts == 2 && practice.Feedback.StartsWith("Fizzle"));
            yield return Shot("23b_practice_fizzle");
            ss.SetChanneling(false);
            // An Instant spell counts too: it goes off without being readied.
            {
                var prDash = SpellBook.Known.First(prS => prS.Id == SelfSpellConfig.BlinkDashId);
                practice.SetTargetForTest(prDash);
                int prHits = practice.Successes;
                ss.ResetCooldowns();
                yield return new WaitForSeconds(0.4f); // the fizzle's lockout ends
                yield return CastSequence(Arrows.ToText(prDash.Sequence), expectReady: null);
                ss.SetChanneling(false);
                Check($"practice: drawing an Instant target (Blink Dash) scores a hit ({practice.Feedback})", practice.Successes == prHits + 1 && practice.Target != prDash);
            }

            // ---- seeded runs ----
            {
                MainMenu.Instance.SetSeedText("ab c-1!?");
                Check($"seeds: numbers are used as they are, words become a number, case and spaces don't matter (\"{MainMenu.Instance.SeedText}\")",
                    RunRandom.ParseSeed("12345") == 12345 && RunRandom.ParseSeed(" moon ") == RunRandom.ParseSeed("MOON")
                    && RunRandom.ParseSeed("moon") > 0 && RunRandom.ParseSeed("   ") == 0 && MainMenu.Instance.SeedText == "ab c-1");
                string SdSig()
                {
                    var combatRoom = d.AllRooms.First(r => r.Type == RoomType.Combat);
                    return string.Join(",", d.AllRooms.Select(r => r.Id + ":" + r.Type + ":" + r.Size.x + "x" + r.Size.y))
                        + " | " + string.Join(",", Pickup.All.Where(p => p && p.Price > 0).Select(p => p.Item.Id).OrderBy(x => x))
                        + " | " + string.Join(",", d.PreviewWave(combatRoom).Select(t => t.Name))
                        + " | " + string.Join(",", d.BuiltProps.Select(b => b.kind + "@" + b.pos.x.ToString("0.0") + "," + b.pos.z.ToString("0.0")));
                }
                RunRandom.RunSeed = RunRandom.ParseSeed("spellcaster");
                d.BuildFloor(1);
                string sd1 = SdSig();
                RunRandom.RunSeed = 777;
                d.BuildFloor(1);
                string sd2 = SdSig();
                RunRandom.RunSeed = RunRandom.ParseSeed("SPELLCASTER");
                d.BuildFloor(1);
                string sd3 = SdSig();
                Check($"the same seed builds the same floor 1: rooms, shop, first wave and props ({sd1.Length} chars); another seed builds a different one",
                    sd1 == sd3 && sd1 != sd2);
                gs.ExitPracticeForTest(); // the seed line shows in real runs only
                gs.SetFocus(false);
                yield return null;
                yield return Shot("23b_pause_seed");
                gs.ClearFocusPause();
                yield return null;
            }

            // ---- co-op in the world: a second copy of the game walks around with us (debug direct IP) ----
            {
                gs.ForcePlayingForTest();
                var ws = Net.NetSession.Instance;
                var wsStart = default(Net.StartRunMsg);
                Net.NetSession.StartRunHandler = m =>
                {
                    // This copy is mid-test (not on the menu): set up the run the way BeginNetworkRun does.
                    wsStart = m;
                    Net.NetPlayers.SetupRun(m);
                    RunRandom.RunSeed = m.seed;
                    d.BuildFloor(1);
                };
                player.God = true;
                bool gbSetting = Settings.GrimoireEnabled;
                Settings.SetGrimoireEnabled(true); // the host's setting decides the Grimoire for everyone
                ws.HostDirect(Net.NetConfig.DebugPort, 4);
                string wsExe = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Spellcaster.exe");
                System.Diagnostics.Process wsClient = null;
                try
                {
                    wsClient = Launched(System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(wsExe,
                        $"-batchmode -nographics -netjoin {Net.NetConfig.DebugAddress} -netready -netwalk -netreport -nettestcombat -nettestloot -netkey testclient -logFile \"{Path.Combine(outDir, "net_client2.log")}\"") { UseShellExecute = false }));
                }
                catch (Exception wsEx) { Debug.LogWarning("[AutoTest] couldn't start a second copy: " + wsEx.Message); }
                float wsT = 0f;
                while (wsT < 45f && !(ws.Roster.Count == 2 && ws.AllReady)) { wsT += Time.unscaledDeltaTime; yield return null; }
                bool wsRun = ws.StartRun();
                wsT = 0f;
                while (wsT < 10f && ws.StartAcks < 2) { wsT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(0.5f);
                int gbHostCardsBefore = Grimoire.GrimoireRun.Instance ? Grimoire.GrimoireRun.Instance.CardsPlayed : 0;
                var wsOther = Players.All.FirstOrDefault(p => p && !p.IsLocal);
                int wsClientId = wsOther ? wsOther.NetId : -1;
                Check($"co-op run: everyone spawns in the start room, the other player has a name, colour and sprite ({(wsOther ? wsOther.DisplayName : "none")}, colour {(wsOther ? wsOther.ColorIndex : -1)})",
                    wsRun && Players.All.Count == 2 && wsOther && wsOther.Mode == Player.Control.Simulated && wsOther.GetComponent<PlayerAvatar>()
                    && wsOther.ColorIndex == 1 && !string.IsNullOrEmpty(wsOther.DisplayName) && d.Current == d.Rooms[Vector2Int.zero]
                    && d.Current.Contains(wsOther.transform.position, 0f));
                Vector3 wsFrom = wsOther ? wsOther.transform.position : Vector3.zero;
                if (wsOther) player.LookAt(wsOther.ChestPosition);
                yield return null;
                yield return Shot("25_coop_other_player");

                // The client walks forward on its own PC; the host replays its inputs.
                wsT = 0f;
                while (wsT < 8f && wsOther && Vector3.Distance(wsOther.transform.position, wsFrom) < 3f) { wsT += Time.unscaledDeltaTime; yield return null; }
                Vector3 wsMoved = wsOther ? wsOther.transform.position - wsFrom : Vector3.zero;
                if (wsOther) player.LookAt(wsOther.ChestPosition);
                yield return Shot("25b_coop_walking");
                Check($"the other player's movement reaches the host ({wsMoved.magnitude:0.0} m, heading {wsMoved.normalized.x:0.00}, {wsMoved.normalized.z:0.00})",
                    wsMoved.magnitude >= 3f && wsMoved.z > 2.5f);
                yield return new WaitForSecondsRealtime(2.5f); // it stops walking; let a report arrive
                ws.Reports.TryGetValue(wsClientId, out var wsRep);
                float wsGap = wsOther ? Vector3.Distance(wsRep.pos, wsOther.transform.position) : 99f;
                Check($"the client's own (predicted) position agrees with the host's ({wsGap:0.00} m apart, {wsRep.corrections} corrections, last error {wsRep.lastError:0.000} m, {wsRep.states} state updates)",
                    wsRep.states > 0 && wsGap < 0.3f);

                // The host moves: the client sees it (interpolated).
                player.Teleport(player.transform.position + new Vector3(2f, 0f, 1f), 0f);
                yield return new WaitForSecondsRealtime(1.5f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                float wsSeen = Vector3.Distance(new Vector3(wsRep.otherPos.x, 0f, wsRep.otherPos.z), new Vector3(player.transform.position.x, 0f, player.transform.position.z));
                Check($"the client sees the host where the host is ({wsSeen:0.00} m off, {wsRep.players} players on the client)",
                    wsRep.players == 2 && wsSeen < 0.5f);

                // Health: only the host applies damage; the client's HP follows.
                if (wsOther) wsOther.TakeDamage(20f);
                yield return new WaitForSecondsRealtime(1.5f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"damage is the host's call and the client's health follows ({(wsOther ? wsOther.HP : 0):0} on the host, {wsRep.hp:0} on the client)",
                    wsOther && wsOther.HP < 100f && Mathf.Approximately(wsRep.hp, wsOther.HP));

                // ---- combat (Phase 3): the host's enemy, a client's spells, an enemy's attack, doors ----
                // Keep the host well away, so the enemy goes for the other player.
                if (wsOther) player.Teleport(d.FindFreeSpot(d.Current, wsOther.transform.position - new Vector3(0f, 0f, 6f), 0.8f) + Vector3.up * 0.1f, 0f);
                var cbImp = wsOther ? EnemySpawner.Instance.SpawnAt(EnemyType.Imp, d.FindFreeSpot(d.Current, wsOther.transform.position + new Vector3(0f, 0f, 3f), 0.6f) + Vector3.up * 0.05f, 30f, 1f) : null;
                float cbT = 0f;
                float cbOff = 99f;
                while (cbT < 6f && cbImp)
                {
                    ws.Reports.TryGetValue(wsClientId, out wsRep);
                    cbOff = Vector3.Distance(new Vector3(wsRep.enemyPos.x, 0f, wsRep.enemyPos.z), new Vector3(cbImp.transform.position.x, 0f, cbImp.transform.position.z));
                    if (wsRep.enemies >= 1 && cbOff < 0.8f) break;
                    cbT += Time.unscaledDeltaTime;
                    yield return null;
                }
                Check($"a client sees the host's enemy where it is ({wsRep.enemies} on the client, {cbOff:0.00} m off)", cbImp && wsRep.enemies >= 1 && cbOff < 0.8f);
                cbT = 0f;
                while (cbT < 8f && cbImp && cbImp.HP >= cbImp.MaxHP) { cbT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(1f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"a client's Fireball, checked and cast by the host, hurts the enemy ({(cbImp ? cbImp.HP : 0):0}/{(cbImp ? cbImp.MaxHP : 0):0}; {Net.NetCombat.RemoteCasts} casts; the client saw {wsRep.fx} effects and {wsRep.projectiles} projectiles, enemy at {wsRep.enemyHp:0} HP there)",
                    cbImp && cbImp.HP < cbImp.MaxHP && Net.NetCombat.RemoteCasts >= 1 && wsRep.fx > 0 && wsRep.projectiles > 0 && wsRep.enemyHp < cbImp.MaxHP);
                Check($"a second cast before the cooldown is refused by the host ({Net.NetCombat.Rejected} refused)", Net.NetCombat.Rejected >= 1);
                float cbHp = wsOther ? wsOther.HP : 0f;
                cbT = 0f;
                while (cbT < 8f && wsOther && wsOther.HP >= cbHp) { cbT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(0.8f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"the enemy attacks the client: the host applies it and the client's health follows ({cbHp:0} -> {(wsOther ? wsOther.HP : 0):0}, client shows {wsRep.hp:0})",
                    wsOther && wsOther.HP < cbHp && Mathf.Abs(wsRep.hp - wsOther.HP) < 9f);
                d.SetGates(d.Current, true);
                yield return new WaitForSecondsRealtime(0.8f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                int cbLocked = wsRep.gatesLocked;
                d.SetGates(d.Current, false);
                gs.ClearEnemies();
                yield return new WaitForSecondsRealtime(2.5f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"doors and deaths reach the client ({cbLocked} doors locked there; {wsRep.enemies} enemies left after the kill)", cbLocked > 0 && wsRep.enemies == 0);

                // Blink Dash online: the client predicts it at once; the host plays it on the same movement frame.
                if (wsOther)
                {
                    int bdHostDashes = wsOther.DashCount;
                    Vector3 bdHostFrom = wsOther.transform.position;
                    Net.NetCombat.Learn(wsOther, SpellBook.CreateBase(SelfSpellConfig.BlinkDashId));
                    float bdT = 0f;
                    while (bdT < 15f && wsOther.DashCount == bdHostDashes) { bdT += Time.unscaledDeltaTime; yield return null; }
                    yield return new WaitForSecondsRealtime(2.5f);
                    ws.Reports.TryGetValue(wsClientId, out wsRep);
                    float bdGap = Vector3.Distance(wsRep.pos, wsOther.transform.position);
                    var bdAv = wsOther.GetComponent<PlayerAvatar>();
                    // Back to the book it had (the loot test below fuses its first two spells).
                    Net.NetCombat.SetBook(wsOther, Net.NetCombat.BookJson(wsOther).Where(bdJ => !bdJ.Contains("\"" + SelfSpellConfig.BlinkDashId + "\"")).ToList());
                    Check($"a client's Blink Dash: predicted on its PC, played by the host on the same movement frame ({wsOther.DashCount - bdHostDashes} dash on the host, {Vector3.Distance(bdHostFrom, wsOther.transform.position):0.0} m; client and host agree within {bdGap:0.00} m, last error {wsRep.lastError:0.000} m; {(bdAv ? bdAv.AfterimagesShown : 0)} afterimages)",
                        wsOther.DashCount >= bdHostDashes + 1 && bdGap < 0.3f && bdAv && bdAv.AfterimagesShown > 0);
                }

                // A big fight: every enemy keeps getting updates on the client (the update is split to fit packets).
                for (int bf = 0; bf < 34; bf++)
                {
                    var bfSpot = d.FindFreeSpot(d.Current, d.Current.Center + new Vector3((bf % 7 - 3) * 2.2f, 0f, (bf / 7 - 2) * 2.2f), 0.5f);
                    var bfE = EnemySpawner.Instance.SpawnAt(EnemyType.Imp, bfSpot + Vector3.up * 0.05f, 50f, 1f);
                    bfE.Freeze(30f);
                }
                yield return new WaitForSecondsRealtime(2.5f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                var bfIn = new Net.InputBatchMsg { frames = new Net.InputFrame[Net.NetPlayers.MaxBatch] };
                var bfSt = new Net.EnemyStatesMsg { enemies = new Net.EnemyStateEntry[Net.NetEnemies.ChunkSize] };
                var bfW = new Mirror.NetworkWriter();
                Mirror.NetworkMessages.Pack(bfIn, bfW); int bfInBytes = bfW.Position; bfW.Reset();
                Mirror.NetworkMessages.Pack(bfSt, bfW); int bfStBytes = bfW.Position;
                int bfMax = Mirror.NetworkMessages.MaxMessageSize(Mirror.Channels.Unreliable);
                Check($"a big fight stays in sync: {wsRep.enemies} enemies on the client, {wsRep.enemiesFresh} of them updated in the last second; every message fits a packet (inputs {bfInBytes} B, enemy chunk {bfStBytes} B, limit {bfMax} B)",
                    wsRep.enemies >= 34 && wsRep.enemiesFresh >= 34 && bfInBytes <= bfMax && bfStBytes <= bfMax);
                gs.ClearEnemies();
                yield return new WaitForSecondsRealtime(2f);

                // ---- Grimoire (Phase 3b): every player has their own deck, held by the host ----
                var gbHostDeck = Grimoire.GrimoireRun.Instance ? Grimoire.GrimoireRun.Instance.Deck : null;
                var gbRemote = Net.NetGrimoire.DeckOf(wsClientId);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                string gbHostHand = gbRemote != null ? string.Join(",", gbRemote.Hand.Select(c => c.Def.Id)) : "";
                Check($"Grimoire online: the host holds a separate deck for the other player ({(gbRemote != null ? gbRemote.Count : 0)} cards), who sees only their own hand ({wsRep.hand}) and it matches ({gbHostHand})",
                    Net.NetGrimoire.Enabled && gbRemote != null && gbRemote != gbHostDeck && wsRep.deck == gbRemote.Count && wsRep.hand == gbHostHand && wsRep.decks > 0);
                Check($"the other player's casts play cards from their own deck ({Net.NetGrimoire.PlayedFor(wsClientId)} played by the host), not the host's",
                    Net.NetGrimoire.PlayedFor(wsClientId) >= 1 && gbHostCardsBefore == Grimoire.GrimoireRun.Instance.CardsPlayed);
                int gbCount = gbRemote != null ? gbRemote.Count : 0;
                Net.NetGrimoire.Offer(wsClientId);
                float gbT = 0f;
                while (gbT < 5f && gbRemote != null && gbRemote.Count == gbCount) { gbT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(0.8f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"a room reward reaches the other player, who picks a card; the host adds it to their deck ({gbCount} -> {(gbRemote != null ? gbRemote.Count : 0)}, client shows {wsRep.deck})",
                    gbRemote != null && gbRemote.Count == gbCount + 1 && wsRep.deck == gbRemote.Count);

                // Playtest bug: the Scroll of Erasure did nothing for clients (the host opened its own list instead).
                int erBefore = gbRemote != null ? gbRemote.Count : 0, erDone = Net.NetGrimoire.Erased;
                int erHostDeck = gbHostDeck != null ? gbHostDeck.Count : 0;
                wsOther.Run.Acquire(Grimoire.GrimoireShop.Erasure);
                float erT = 0f;
                while (erT < 8f && Net.NetGrimoire.Erased == erDone) { erT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(0.8f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                bool erScrollGone = System.Array.IndexOf(wsOther.Run.Consumables, Grimoire.GrimoireShop.Erasure) < 0;
                bool erHostPanel = Grimoire.GrimoireRun.Instance && Grimoire.GrimoireRun.Instance.Rewards.IsOpen;
                Check($"the other player's Scroll of Erasure opens the list on their screen; the host removes the card from their deck and takes their scroll (deck {erBefore} -> {(gbRemote != null ? gbRemote.Count : 0)}, client shows {wsRep.deck}, scroll gone {erScrollGone}; your deck {erHostDeck} -> {(gbHostDeck != null ? gbHostDeck.Count : 0)}, list opened on your screen {erHostPanel})",
                    gbRemote != null && gbRemote.Count == erBefore - 1 && wsRep.deck == gbRemote.Count && erScrollGone && !erHostPanel
                    && (gbHostDeck == null || gbHostDeck.Count == erHostDeck));

                // ---- loot and progress (Phase 4): instanced loot, each player's run, Forge, rooms, floors ----
                player.Teleport(wsOther.transform.position + new Vector3(2f, 0f, 0f), player.Yaw);
                yield return new WaitForSecondsRealtime(0.3f);
                int ltHostGold = player.Run.Gold, ltCliGold = wsOther.Run.Gold;
                Pickup.Spawn(Items.Coin, (player.transform.position + wsOther.transform.position) * 0.5f);
                yield return new WaitForSecondsRealtime(1.5f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"a coin is instanced: both players get their own (you {ltHostGold} -> {player.Run.Gold}, other {ltCliGold} -> {wsOther.Run.Gold}, their screen shows {wsRep.gold})",
                    player.Run.Gold == ltHostGold + 1 + player.Run.Stats.CoinBonus && wsOther.Run.Gold == ltCliGold + 1 + wsOther.Run.Stats.CoinBonus && wsRep.gold == wsOther.Run.Gold);

                var ltHeart = Items.Get("ruby_heart");
                var ltItem = Pickup.Spawn(ltHeart, wsOther.transform.position + wsOther.transform.forward * 1.2f);
                float ltT = 0f;
                while (ltT < 5f && !wsOther.Run.Has("ruby_heart")) { ltT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(0.6f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                bool ltStillMine = ltItem && !ltItem.TakenByLocal && ltItem.For(player);
                Check($"an item is instanced: the other player takes theirs (max HP {wsOther.MaxHP:0}, their screen {wsRep.maxHp:0}, {wsRep.items} items) and yours is still there",
                    wsOther.Run.Has("ruby_heart") && Mathf.Approximately(wsRep.maxHp, wsOther.MaxHP) && wsRep.items >= 1 && ltStillMine);
                bool ltMine = ltItem && ltItem.TryCollect(player.Run);
                yield return null;
                Check("you take your copy too, and then it's gone for everyone", ltMine && player.Run.Has("ruby_heart") && !ltItem);

                int hdCliArcs = wsRep.hitArcs;
                // Hits from four sides (their camera may face elsewhere than the host last heard): two are always out of view.
                for (int hdSide = 0; hdSide < 4; hdSide++)
                    wsOther.TakeDamage(hdSide == 0 ? 27f : 1f, wsOther.transform.position + Quaternion.Euler(0f, 90f * hdSide, 0f) * Vector3.forward * 5f, 4242 + hdSide);
                float ltHurt = wsOther.HP;
                wsOther.Run.Acquire(Items.Get("potion"));
                ltT = 0f;
                while (ltT < 4f && wsOther.HP <= ltHurt) { ltT += Time.unscaledDeltaTime; yield return null; }
                Check($"the other player uses a consumable; the host applies it ({ltHurt:0} -> {wsOther.HP:0} HP, slot empty: {wsOther.Run.Consumables[0] == null})",
                    wsOther.HP > ltHurt && wsOther.Run.Consumables[0] == null);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"hits on the other player from outside their view draw hit-direction arcs on their screen (arcs {hdCliArcs} -> {wsRep.hitArcs})", wsRep.hitArcs >= hdCliArcs + 2);

                // Essence is per player: a drop for them is theirs alone.
                int esHost0 = player.Run.Essence, esCli0 = wsOther.Run.Essence;
                Essence.Spawn((player.transform.position + wsOther.transform.position) * 0.5f, 1, wsOther);
                yield return new WaitForSecondsRealtime(2f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"Essence is per player: a drop rolled for the other player goes to them only (them {esCli0} -> {wsOther.Run.Essence}, their screen {wsRep.essence}; you {esHost0} -> {player.Run.Essence})",
                    wsOther.Run.Essence == esCli0 + 1 && wsRep.essence == wsOther.Run.Essence && player.Run.Essence == esHost0);
                wsOther.Run.Essence = 10; // enough for one forge (3)
                wsOther.Run.ForgesDone = 0;
                bool ltLearned = Net.NetCombat.Learn(wsOther, SpellBook.CreateBase("lightning"));
                ltT = 0f;
                while (ltT < 5f && Net.NetCombat.BookCount(wsOther) < 3) { ltT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(0.6f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"each player has their own spell book and Forge: they learn Lightning, fuse it with Fireball on their screen, and the host forges it into their book ({Net.NetCombat.BookCount(wsOther)} spells, their screen {wsRep.spells}; yours {SpellBook.Known.Count})",
                    ltLearned && Net.NetCombat.BookCount(wsOther) == 3 && wsRep.spells == 3);
                Check($"the host charges the other player's own Essence for their forge (10 -> {wsOther.Run.Essence}, their screen {wsRep.essence}; their forges done {wsOther.Run.ForgesDone}, yours {player.Run.ForgesDone})",
                    wsOther.Run.Essence == 7 && wsRep.essence == 7 && wsOther.Run.ForgesDone == 1 && wsRep.forgesDone == 1);

                // A fight room waits for everyone: nobody is pulled in, and nothing starts until all who count are inside.
                // You wait in the start room (outside any fight), so the only room waiting is the one they enter.
                var gtStart = d.Rooms[Vector2Int.zero].Center;
                player.Teleport(gtStart + new Vector3(0f, 0.1f, -3f), 0f);
                yield return new WaitForSecondsRealtime(0.3f);
                var gtRooms = d.AllRooms.Where(r => r.Type == RoomType.Combat && !r.Cleared && r.HasEnemies).Take(2).ToList();
                if (gtRooms.Count == 2)
                {
                    var ltRoom = gtRooms[0];
                    d.HoldEncounters = false;
                    wsOther.Teleport(ltRoom.Center + new Vector3(0f, 0.1f, 0f), 0f);
                    yield return new WaitForSecondsRealtime(0.6f);
                    ws.Reports.TryGetValue(wsClientId, out wsRep);
                    bool gtWait = d.GatherRoom == ltRoom && !d.InCombat && d.GatherInside == 1 && d.GatherNeed == 2 && d.GatherOutside.Contains(player);
                    string gtAt = $"room {d.GatherRoom?.Id ?? "none"} vs {ltRoom.Id}, fight {d.InCombat}, {d.GatherInside}/{d.GatherNeed}; their screen {wsRep.gatherInside}/{wsRep.gatherNeed}";
                    bool gtTheirs = wsRep.gatherInside == 1 && wsRep.gatherNeed == 2;
                    yield return Shot("27a_gather_outside");
                    int gtMarkOut = HUD.GatherMarkersShown;
                    yield return new WaitForSecondsRealtime(3.5f); // longer than the old countdown
                    bool gtNoPull = !d.InCombat && d.GatherRoom == ltRoom && !ltRoom.Contains(player.transform.position, 0f);
                    Check($"a fight room waits for everyone: no fight, no teleport, \"Waiting for players (1/2)\" on both screens ({gtAt}; after 3.5 s fight {d.InCombat}, you still outside {!ltRoom.Contains(player.transform.position, 0f)}; markers {gtMarkOut})",
                        gtWait && gtTheirs && gtNoPull && gtMarkOut == 1);

                    // Walking back out lets the room go.
                    wsOther.Teleport(gtStart + new Vector3(2f, 0.1f, -3f), 0f);
                    yield return new WaitForSecondsRealtime(0.6f);
                    ws.Reports.TryGetValue(wsClientId, out wsRep);
                    Check($"everyone walking back out lets the room go (room {d.GatherRoom?.Id ?? "none"}, fight {d.InCombat}, their screen {wsRep.gatherInside}/{wsRep.gatherNeed})",
                        d.GatherRoom == null && !d.InCombat && wsRep.gatherNeed == 0);

                    // Players who are out (spectating, downed, or gone) stop counting: you're in, they're out, it starts.
                    var gtIn = ltRoom.Center + new Vector3(0f, 0.1f, -2f);
                    player.Teleport(gtIn, 0f);
                    yield return new WaitForSecondsRealtime(0.4f);
                    bool gtYouWait = d.GatherRoom == ltRoom && d.GatherInside == 1 && d.GatherOutside.Contains(wsOther);
                    yield return Shot("27b_gather_inside");
                    int gtMarkIn = HUD.GatherMarkersShown;
                    wsOther.SetBledOut();
                    yield return new WaitForSecondsRealtime(0.3f);
                    bool gtStarted = d.InCombat && d.Current == ltRoom;
                    Net.NetDowned.Stand(wsOther, 1f);
                    Check($"you wait inside with a marker on them ({gtYouWait}, markers {gtMarkIn}); once they're out of play (spectating) they stop counting and the fight starts (fight {gtStarted})",
                        gtYouWait && gtMarkIn == 1 && gtStarted);
                    yield return new WaitForSecondsRealtime(0.5f);
                    gs.ClearEnemies();
                    d.ClearCurrent();
                    gs.ClearEnemies();
                    yield return new WaitForSecondsRealtime(0.5f);

                    // Both walk in: the fight starts where they stand.
                    var lt2 = gtRooms[1];
                    player.Teleport(gtStart + new Vector3(0f, 0.1f, -3f), 0f);
                    wsOther.Teleport(lt2.Center + new Vector3(1.5f, 0.1f, 0f), 0f);
                    yield return new WaitForSecondsRealtime(0.5f);
                    bool gt2Wait = d.GatherRoom == lt2 && !d.InCombat;
                    var gtMe = lt2.Center + new Vector3(-1.5f, 0.1f, -1f);
                    player.Teleport(gtMe, 0f);
                    yield return new WaitForSecondsRealtime(1.0f);
                    ws.Reports.TryGetValue(wsClientId, out wsRep);
                    var ltScale = Net.NetConfig.ScaleFor(2);
                    float gtMoved = Players.FlatSqr(player.transform.position, gtMe);
                    Check($"when the last player walks in, the doors close and the fight starts with nobody moved (waited {gt2Wait}, fight {d.InCombat}, you moved {Mathf.Sqrt(gtMoved):0.00} m; scaled for 2: HP x{d.LastScale.hp}, enemies x{d.LastScale.count}; doors locked on their screen: {wsRep.gatesLocked})",
                        gt2Wait && d.InCombat && d.Current == lt2 && gtMoved < 0.25f && d.LastScale == ltScale && wsRep.gatesLocked > 0);
                    yield return new WaitForSecondsRealtime(1.5f);
                    gs.ClearEnemies();
                    d.ClearCurrent();
                    gs.ClearEnemies();
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                else Check("two fight rooms for the co-op room test", false);

                // The boss falls: everyone sees the portal and the reward pedestal.
                var bsRoom = d.AllRooms.First(r => r.Type == RoomType.Boss);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                int bsBefore = wsRep.pedestals;
                // Both players step in together first (anyone left in another fight room would start that fight instead).
                d.HoldEncounters = true;
                wsOther.Teleport(bsRoom.Center + new Vector3(2f, 0.1f, -6f), 0f);
                player.Teleport(bsRoom.Center + new Vector3(0f, 0.1f, -6f), 0f);
                if (d.InCombat) { gs.ClearEnemies(); d.ClearCurrent(); gs.ClearEnemies(); }
                yield return new WaitForSecondsRealtime(0.5f);
                d.CancelGatherForTest();
                d.HoldEncounters = false;
                float bsT = 0f;
                while (bsT < 10f && !(d.InCombat && d.Boss)) { bsT += Time.unscaledDeltaTime; yield return null; }
                bool bsFought = d.InCombat && d.Boss;
                Debug.Log($"[AutoTest] boss room {bsRoom.Id}: in it {bsRoom.Contains(player.transform.position, 0f)}, gather {d.GatherRoom?.Id}, current {d.Current?.Id}, combat {d.InCombat}, cleared {bsRoom.Cleared}, hold {d.HoldEncounters}");
                yield return new WaitForSecondsRealtime(0.5f);
                bsT = 0f;
                while (bsT < 8f && d.InCombat) { gs.ClearEnemies(); bsT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(1.5f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"the boss falls and everyone sees the portal and the reward pedestal (host: fight {bsFought}, portal {d.PortalOpen}, boss room cleared {bsRoom.Cleared}; their screen: portal {wsRep.portalOpen}, pedestals {bsBefore} -> {wsRep.pedestals})",
                    d.PortalOpen && wsRep.portalOpen && wsRep.pedestals == bsBefore + Difficulty.BossItems(d.Floor));

                // Through the portal: everyone goes to the next floor together.
                int ltFloor = d.Floor;
                d.EnterPortalForTest();
                yield return new WaitForSecondsRealtime(2f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"the floor changes for everyone at once (floor {ltFloor} -> {d.Floor}, their screen: floor {wsRep.floor}; both in the start room)",
                    d.Floor == ltFloor + 1 && wsRep.floor == d.Floor && d.Rooms[Vector2Int.zero].Contains(wsOther.transform.position, 0f));

                // ---- downed, revive, spectate, respawn, game over (Phase 5) ----
                float dnBleed = Net.NetDowned.BleedOutSeconds;
                wsOther.TakeDamage(99999f);
                yield return new WaitForSecondsRealtime(0.8f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"at 0 HP a co-op player is downed, not dead: the run goes on and their screen says so (downed: {wsOther.Downed}, on their screen: {wsRep.downed})",
                    wsOther.Downed && !wsOther.BledOut && gs.Phase == Phase.Playing && wsRep.downed);
                player.Teleport(wsOther.transform.position + wsOther.transform.right * 1f + Vector3.up * 0.1f, player.Yaw);
                yield return new WaitForSecondsRealtime(Net.NetDowned.ReviveSeconds + 1f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"standing next to them for {Net.NetDowned.ReviveSeconds:0} s revives them ({wsOther.HP:0}/{wsOther.MaxHP:0} HP, their screen {wsRep.hp:0}, downed there: {wsRep.downed})",
                    !wsOther.IsDead && Mathf.Abs(wsOther.HP - wsOther.MaxHP * Net.NetDowned.ReviveHpFraction) < 2f && !wsRep.downed && wsRep.hp > 0f);
                Net.NetDowned.BleedOutSeconds = 1.5f;
                player.Teleport(d.FindFreeSpot(d.Current, d.Current.Center + new Vector3(-5f, 0f, -4f), 0.8f) + Vector3.up * 0.1f, player.Yaw);
                yield return null;
                wsOther.TakeDamage(99999f);
                yield return new WaitForSecondsRealtime(2.6f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"nobody helps: they bleed out and spectate a teammate (out: {wsOther.BledOut}, their screen spectating: {wsRep.spectating})",
                    wsOther.BledOut && wsRep.bledOut && wsRep.spectating);
                Net.NetDowned.BleedOutSeconds = dnBleed;
                d.ClearCurrent();
                yield return new WaitForSecondsRealtime(1f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                float dnNear = Vector3.Distance(wsOther.transform.position, player.transform.position);
                Check($"when the room is cleared they come back next to a teammate with reduced HP ({wsOther.HP:0}/{wsOther.MaxHP:0}, {dnNear:0.0} m away; their screen out: {wsRep.bledOut})",
                    !wsOther.IsDead && dnNear < 3.5f && wsOther.HP <= wsOther.MaxHP * Net.NetDowned.RespawnHpFraction + 1f && !wsRep.bledOut); // reduced HP (the floor may nick them after)
                player.God = false;
                wsOther.TakeDamage(99999f);
                for (int dnI = 0; dnI < 4 && !player.IsDead; dnI++) player.TakeDamage(99999f); // a Phoenix Feather may save the first hit
                yield return new WaitForSecondsRealtime(1f);
                ws.Reports.TryGetValue(wsClientId, out wsRep);
                Check($"the run ends only when everyone is down at once, for everyone (host: {gs.Phase}, their screen: {(Phase)wsRep.phase})",
                    gs.Phase == Phase.Dead && wsRep.phase == (int)Phase.Dead);
                Net.NetDowned.Stand(player, 1f);
                player.God = true;
                gs.ForcePlayingForTest();

                // ---- disconnects and saving (Phase 6) ----
                bool svOk = SaveSystem.Save();
                var svFile = SaveSystem.Read(SaveSystem.FilePath);
                string svCoop = svFile != null ? svFile.Get("coop") : null;
                int svGold = wsOther.Run.Gold, svSpells = Net.NetCombat.BookCount(wsOther), svFloor = d.Floor;
                Check($"only the host saves, and its save keeps the other player's character by SteamID (coop section: {(svCoop != null && svCoop.Contains("\"testclient\"") ? "testclient" : "missing")}, {svGold} gold, {svSpells} spells)",
                    svOk && svCoop != null && svCoop.Contains("\"testclient\""));

                // The player leaves: their character goes from the world; the host keeps it.
                if (Mirror.NetworkServer.connections.TryGetValue(wsClientId, out var wsConn)) wsConn.Disconnect();
                yield return new WaitForSecondsRealtime(0.5f);
                var svKept = Net.NetSave.Stored("testclient");
                Check($"a player who leaves mid-run is removed from the world, and the host keeps their character ({(svKept != null ? svKept.run.gold + " gold" : "none")})",
                    Players.All.Count == 1 && Players.ByNetId(wsClientId) == null && svKept != null && svKept.run.gold == svGold);
                if (wsClient != null && !wsClient.HasExited) { try { wsClient.Kill(); } catch { } }
                var svStranger = new Net.HelloMsg { version = Net.NetConfig.Version, name = "x", key = "stranger" };
                Check("a newcomer can't join a run in progress, but a player coming back can",
                    Net.NetSession.Validate(svStranger, 1, 4, true) != null && Net.NetSession.Validate(svStranger, 1, 4, true, comingBack: true) == null);

                // They come back to the same run: their character returns.
                System.Diagnostics.Process svClient2 = null;
                try
                {
                    svClient2 = Launched(System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(wsExe,
                        $"-batchmode -nographics -netjoin {Net.NetConfig.DebugAddress} -netkey testclient -netreport -logFile \"{Path.Combine(outDir, "net_client3.log")}\"") { UseShellExecute = false }));
                }
                catch (Exception svEx) { Debug.LogWarning("[AutoTest] couldn't start a copy: " + svEx.Message); }
                float svT = 0f;
                while (svT < 45f && Players.All.Count < 2) { svT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(3f);
                var svBack = Players.All.FirstOrDefault(p => p && !p.IsLocal);
                Net.DebugReportMsg svRep = default;
                if (svBack) ws.Reports.TryGetValue(svBack.NetId, out svRep);
                Check($"the player comes back to the run in progress with their character (gold {(svBack ? svBack.Run.Gold : -1)}/{svGold}, spells {(svBack ? Net.NetCombat.BookCount(svBack) : -1)}/{svSpells}; their screen: {svRep.gold} gold, {svRep.spells} spells, floor {svRep.floor}, {svRep.players} players)",
                    svBack && svBack.Run.Gold >= svGold && Net.NetCombat.BookCount(svBack) == svSpells && svBack.Run.Has("ruby_heart")
                    && svRep.gold == svBack.Run.Gold && svRep.spells == svSpells && svRep.floor == d.Floor && svRep.players == 2);
                if (svClient2 != null && !svClient2.HasExited) { try { svClient2.Kill(); } catch { } }
                ws.Leave();
                yield return new WaitForSecondsRealtime(0.3f);

                // The host resumes the saved run in a new session: the returning player gets their saved character.
                bool svResumed = false;
                Net.NetSession.StartRunHandler = m =>
                {
                    svResumed = m.resume;
                    Net.NetPlayers.SetupRun(m);
                    SaveSystem.LoadRun();
                    Net.NetSave.AfterResume();
                };
                ws.HostDirect(Net.NetConfig.DebugPort, 4);
                ws.ContinueSaved = true;
                System.Diagnostics.Process svClient3 = null;
                try
                {
                    svClient3 = Launched(System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(wsExe,
                        $"-batchmode -nographics -netjoin {Net.NetConfig.DebugAddress} -netready -netkey testclient -netreport -logFile \"{Path.Combine(outDir, "net_client4.log")}\"") { UseShellExecute = false }));
                }
                catch (Exception svEx2) { Debug.LogWarning("[AutoTest] couldn't start a copy: " + svEx2.Message); }
                svT = 0f;
                while (svT < 45f && !(ws.Roster.Count == 2 && ws.AllReady)) { svT += Time.unscaledDeltaTime; yield return null; }
                bool svStarted = ws.StartRun();
                svT = 0f;
                while (svT < 10f && ws.StartAcks < 2) { svT += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(3f);
                var svRes = Players.All.FirstOrDefault(p => p && !p.IsLocal);
                svRep = default;
                if (svRes) ws.Reports.TryGetValue(svRes.NetId, out svRep);
                Check($"resuming the saved co-op run: the returning player gets their saved character back (gold {(svRes ? svRes.Run.Gold : -1)}/{svGold}; their screen: {svRep.gold} gold, {svRep.spells} spells, floor {svRep.floor}/{svFloor})",
                    svStarted && svResumed && svRes && svRes.Run.Gold == svGold && svRep.gold == svGold && svRep.spells == svSpells && svRep.floor == svFloor);
                if (svClient3 != null && !svClient3.HasExited) { try { svClient3.Kill(); } catch { } }
                ws.ContinueSaved = false;
                ws.Leave();
                // ---- under 150 ms ping and 2% packet loss (Phase 7) ----
                {
                    Net.NetSim.Set(0f, 2f); // the host drops 2% of what it sends unreliably; the other copy adds 150 ms and 2%
                    Net.NetSession.StartRunHandler = m =>
                    {
                        Net.NetPlayers.SetupRun(m);
                        RunRandom.RunSeed = m.seed;
                        d.BuildFloor(1);
                    };
                    player.God = true;
                    yield return new WaitForSecondsRealtime(0.5f); // the previous session lets go of the port
                    ws.HostDirect(Net.NetConfig.DebugPort, 4);
                    System.Diagnostics.Process lgClient = null;
                    try
                    {
                        lgClient = Launched(System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(wsExe,
                            $"-batchmode -nographics -netjoin {Net.NetConfig.DebugAddress} -netready -netwalk -netreport -nettestcombat -netkey lagclient -netlag 150 -netloss 2 -logFile \"{Path.Combine(outDir, "net_client_lag.log")}\"") { UseShellExecute = false }));
                    }
                    catch (Exception lgEx) { Debug.LogWarning("[AutoTest] couldn't start a copy: " + lgEx.Message); }
                    float lgT = 0f;
                    while (lgT < 45f && !(ws.Roster.Count == 2 && ws.AllReady)) { lgT += Time.unscaledDeltaTime; yield return null; }
                    ws.StartRun();
                    lgT = 0f;
                    while (lgT < 10f && ws.StartAcks < 2) { lgT += Time.unscaledDeltaTime; yield return null; }
                    yield return new WaitForSecondsRealtime(0.5f);
                    var lgOther = Players.All.FirstOrDefault(p => p && !p.IsLocal);
                    Vector3 lgFrom = lgOther ? lgOther.transform.position : Vector3.zero;
                    lgT = 0f;
                    while (lgT < 10f && lgOther && Vector3.Distance(lgOther.transform.position, lgFrom) < 3f) { lgT += Time.unscaledDeltaTime; yield return null; }
                    yield return new WaitForSecondsRealtime(3f);
                    Net.DebugReportMsg lgRep = default;
                    if (lgOther) ws.Reports.TryGetValue(lgOther.NetId, out lgRep);
                    float lgGap = lgOther ? Vector3.Distance(lgRep.pos, lgOther.transform.position) : 99f;
                    float lgMoved = lgOther ? Vector3.Distance(lgOther.transform.position, lgFrom) : 0f;
                    Check($"at ~150 ms and 2% loss, movement stays consistent: ping {lgRep.rtt:0} ms, {lgMoved:0.0} m walked on the host, own position {lgGap:0.00} m from the host's ({lgRep.corrections} corrections)",
                        lgRep.rtt >= 120f && lgMoved >= 3f && lgGap < 0.35f);
                    NetOverlay_Show(true);
                    yield return Shot("26_net_overlay");
                    // Combat under lag: the host's enemy, the client's Fireball through the host.
                    if (lgOther) player.Teleport(d.FindFreeSpot(d.Current, lgOther.transform.position - new Vector3(0f, 0f, 6f), 0.8f) + Vector3.up * 0.1f, 0f);
                    var lgImp = lgOther ? EnemySpawner.Instance.SpawnAt(EnemyType.Imp, d.FindFreeSpot(d.Current, lgOther.transform.position + new Vector3(0f, 0f, 3f), 0.6f) + Vector3.up * 0.05f, 30f, 1f) : null;
                    lgT = 0f;
                    while (lgT < 10f && lgImp && lgImp.HP >= lgImp.MaxHP) { lgT += Time.unscaledDeltaTime; yield return null; }
                    yield return new WaitForSecondsRealtime(1.5f);
                    if (lgOther) ws.Reports.TryGetValue(lgOther.NetId, out lgRep);
                    Check($"at ~150 ms and 2% loss, combat stays consistent: the client's Fireball hurts the host's enemy ({(lgImp ? lgImp.HP : 0):0}/{(lgImp ? lgImp.MaxHP : 0):0}, {lgRep.enemyHp:0} on their screen) and their health matches ({(lgOther ? lgOther.HP : 0):0} / {lgRep.hp:0})",
                        lgImp && lgImp.HP < lgImp.MaxHP && Mathf.Abs(lgRep.enemyHp - lgImp.HP) < 45f && lgOther && Mathf.Abs(lgRep.hp - lgOther.HP) < 17f); // a hit may be in flight
                    var lgLossIn = Net.NetStats.Loss(lgOther ? lgOther.NetId : 0).Percent;
                    Check($"the debug overlay's numbers are live: traffic out {Net.NetStats.OutKBps:0.0} KB/s, in {Net.NetStats.InKBps:0.0} KB/s, their input loss {lgLossIn:0.0}%, loss on their side {lgRep.lossIn:0.0}%",
                        Net.NetStats.OutKBps > 0f && Net.NetStats.InKBps > 0f && lgRep.lossIn >= 0f && lgRep.lossIn < 15f && lgLossIn < 15f);
                    NetOverlay_Show(false);
                    gs.ClearEnemies();
                    if (lgClient != null && !lgClient.HasExited) { try { lgClient.Kill(); } catch { } }
                    ws.Leave();
                    Net.NetSim.Set(0f, 0f);
                    yield return null;
                }
                Settings.SetGrimoireEnabled(gbSetting);
                Net.NetSession.StartRunHandler = null;
                yield return null;
                yield return null;
                Check("ending the session leaves a normal single-player world", Players.All.Count == 1 && player.NetId == -1 && !GameState.Multiplayer && !ws.InSession);
            }

            // ---- death ----
            gs.ForcePlayingForTest();
            gs.ExitPracticeForTest();
            SaveSystem.Save();
            bool svBeforeDeath = File.Exists(SaveSystem.FilePath);
            player.God = false;
            player.TakeDamage(5000f);
            yield return null;
            Check("player death shows You Died and deletes the save", gs.Phase == Phase.Dead && player.IsDead && svBeforeDeath && !File.Exists(SaveSystem.FilePath));
            yield return Shot("24_death");

            Log($"SUMMARY passed={passed} failed={failed}");
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void Update()
        {
            // Watchdog: never leave a hung test process behind.
            if (Time.realtimeSinceStartup > WatchdogSeconds)
            {
                Log($"FAIL watchdog timeout  SUMMARY passed={passed} failed={failed + 1}");
                Application.Quit(2);
                enabled = false;
            }
        }

        static int Reachable(Dungeon d)
        {
            var start = d.Rooms[Vector2Int.zero];
            var seen = new HashSet<Room> { start };
            var q = new Queue<Room>();
            q.Enqueue(start);
            while (q.Count > 0)
                foreach (var door in q.Dequeue().Doors)
                    foreach (var r in new[] { door.A, door.B })
                        if (seen.Add(r)) q.Enqueue(r);
            return seen.Count;
        }

        static void NetOverlay_Show(bool on) => Net.NetOverlay.Shown = on;

        /// <summary>Copies of the game this test started: closed when the test ends, however it ends.</summary>
        static readonly List<System.Diagnostics.Process> launched = new List<System.Diagnostics.Process>();
        static System.Diagnostics.Process Launched(System.Diagnostics.Process p) { if (p != null) launched.Add(p); return p; }

        void OnApplicationQuit()
        {
            foreach (var p in launched)
                try { if (!p.HasExited) p.Kill(); } catch { }
        }

        static void EnterRoom(Player p, Room r) => p.Teleport(r.Center + new Vector3(0f, 0.1f, -8f), 0f);

        static void ClearPickups()
        {
            foreach (var p in Pickup.All.ToArray())
                if (p && p.IsAuto) Destroy(p.gameObject);
        }

        IEnumerator CastSequence(string seq, string expectReady, string showChannel = null)
        {
            // Draws the rune with synthetic mouse movement, through the real gesture recognizer.
            var ss = SpellSystem.Instance;
            ss.SetChanneling(true);
            yield return null;
            var arrows = Arrows.Parse(seq);
            for (int i = 0; i < arrows.Length; i++)
            {
                Vector2 dir = GestureRecognizer.Dir(arrows[i]), side = new Vector2(-dir.y, dir.x);
                for (int f = 0; f < 7; f++)
                {
                    ss.FeedGesture(dir * 1.1f + side * Mathf.Sin(f * 1.7f + i) * 0.35f);
                    yield return null;
                }
                if (showChannel != null && i == arrows.Length - 2) yield return Shot(showChannel);
            }
            if (expectReady != null) ss.SetChanneling(false); // releasing keeps the spell readied
        }

        /// <summary>Feeds a wobbly path (strokeLen units per stroke) into a recognizer; returns the strokes read.</summary>
        static List<Arrow> DrawPath(GestureRecognizer g, string seq, float strokeLen, float wobble)
        {
            var got = new List<Arrow>();
            int n = 0;
            foreach (var a in Arrows.Parse(seq))
            {
                Vector2 dir = GestureRecognizer.Dir(a), side = new Vector2(-dir.y, dir.x);
                for (float t = 0f; t < strokeLen; t += 0.5f, n++)
                {
                    var r = g.Feed(dir * 0.5f + side * Mathf.Sin(n * 0.9f) * wobble * 0.5f);
                    if (r.HasValue) got.Add(r.Value);
                }
            }
            return got;
        }

        static SpellDefinition Spell(string name) => SpellBook.Known.First(s => s.Name == name);

        /// <summary>A direction (yaw) with nothing solid for `dist` metres from where the player stands.</summary>
        static float ClearHeading(Player p, float dist)
        {
            Vector3 from = p.transform.position + Vector3.up * 0.9f;
            for (int a = 0; a < 360; a += 15)
            {
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                if (!Physics.SphereCast(from, 0.5f, dir, out _, dist, Layers.SolidMask, QueryTriggerInteraction.Ignore)) return a;
            }
            return 0f;
        }

        /// <summary>The player's body overlaps a wall or prop (clipped into geometry).</summary>
        static bool PlayerInSolid(Player p)
        {
            Vector3 b = p.transform.position;
            return Physics.CheckCapsule(b + Vector3.up * 0.45f, b + Vector3.up * 1.35f, 0.33f, Layers.SolidMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>A card changed this cast in some way (Blink Dash / Quickening card tests).</summary>
        static bool CardChanged(CastRequest r)
        {
            if (r.Copies.Count != 1 || r.CooldownMult != 1f || r.ResetCooldown || r.Fizzle || r.EchoDelay > 0f || r.AfterLaunch != null) return true;
            var m = r.Copies[0];
            return m.DamageMult != 1f || m.RangeMult != 1f || m.AreaMult != 1f || m.SizeMult != 1f || m.DashPierce > 0f || m.DashTrail || m.DashEcho > 0f
                || m.DurationMult != 1f || m.AllyRadius > 0f || m.ExtraRegen > 0f || m.Cleanse || m.BuffBurn || m.BuffFrost || m.OnHit != null;
        }
        static int IndexOf(string name) => SpellBook.Known.FindIndex(s => s.Name == name);

        static Vector3 AheadPoint(Player p, float distance, float side)
        {
            var room = Dungeon.Instance.Current;
            Vector3 pos = p.transform.position + p.transform.forward * distance + p.transform.right * side;
            pos.x = Mathf.Clamp(pos.x, room.Center.x - room.HalfX + 2f, room.Center.x + room.HalfX - 2f);
            pos.z = Mathf.Clamp(pos.z, room.Center.z - room.HalfZ + 2f, room.Center.z + room.HalfZ - 2f);
            pos.y = 0.05f;
            return pos;
        }

        static Enemy SpawnAhead(Player p, EnemyType type, float distance, float side) =>
            EnemySpawner.Instance.SpawnAt(type, AheadPoint(p, distance, side));

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
