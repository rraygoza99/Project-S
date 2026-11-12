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
            yield return new WaitForSeconds(0.5f);

            // ---- main menu ----
            var menu = MainMenu.Instance;
            Check("game opens on the main menu", gs.Phase == Phase.Title && menu.Visible && menu.Current == MainMenu.Page.Main);
            yield return Shot("00_main_menu");

            // ---- options ----
            menu.Open(MainMenu.Page.Options);
            Settings.SetMouseSensitivity(3f);
            Settings.SetRuneStroke(6f);
            Settings.SetFieldOfView(95f);
            Settings.SetInvertY(true);
            yield return Shot("00b_options");
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
            Progress.Tracking = true;
            Progress.Add("kills", 37);
            Progress.Add("deaths", 3);
            Progress.Add("rooms", 10); // unlocks Poison Cloud
            menu.Open(MainMenu.Page.Unlocks);
            yield return Shot("00g_unlocks");
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
                Check("enemies spawn all over the big room",
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
            Check("all 6 base spells learnable", SpellBook.Known.Count == 6);

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
            Check("Fireball on cooldown cannot be readied (fizzles)", ss.Readied == null && events.Contains("fizzle"));
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

            // ---- Spell Forge ----
            var forge = SpellForgeUI.Instance;
            forge.Open();
            forge.Toggle(IndexOf("Fireball"));
            forge.Toggle(IndexOf("Frost Nova"));
            yield return Shot("07_forge_preview");
            var frozenFireball = forge.Forge();
            Check("Forge: Fireball + Frost Nova", frozenFireball != null && frozenFireball.Synergies.Contains("Shatter"));
            if (frozenFireball != null) Log($"forged '{frozenFireball.Name}' {Arrows.ToText(frozenFireball.Sequence)} cd={frozenFireball.Cooldown}: {frozenFireball.Describe()}");
            forge.Toggle(IndexOf("Lightning"));
            forge.Toggle(IndexOf("Fireball"));
            var stormLightning = forge.Forge();
            Check("Forge: Lightning + Fireball unlocks Ignite", stormLightning != null && stormLightning.Synergies.Contains("Ignite"));
            forge.Toggle(IndexOf("Fireball"));
            forge.Toggle(IndexOf("Fireball"));
            Check("Forge rejects picking the same spell twice", forge.Forge() == null);
            forge.Close();
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

            // ---- final floor: victory ----
            d.BuildFloor(Dungeon.FinalFloor);
            yield return new WaitForSeconds(0.5f);
            var finalBoss = d.FirstOfType(RoomType.Boss);
            EnterRoom(player, finalBoss);
            yield return new WaitForSeconds(2.5f);
            Check("floor 3 boss is tougher", d.Boss && d.Boss.MaxHP > EnemyType.Boss.MaxHP * 1.5f);
            if (d.Boss) d.Boss.TakeDamage(99999f);
            yield return new WaitForSeconds(1.2f);
            player.Teleport(d.PortalPosition + Vector3.up * 0.1f, 0f);
            yield return new WaitForSecondsRealtime(0.5f); // the victory screen pauses game time
            Check("final portal wins the run", gs.Phase == Phase.Victory);
            yield return Shot("22_victory");

            // ---- meta progression: locked content, counters, achievements, new spells and items ----
            gs.ForcePlayingForTest();
            Progress.ResetForTest();
            Check("unlockable spells and items start locked and never drop",
                SpellBook.Available.Count == 6 && !SpellBook.Available.Any(sp => sp.StartsLocked) &&
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
                Progress.All.Count == 18 && Progress.All.Count(a => a.IsSpell) == 6 && SpellBook.Available.Count == 12 &&
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

            // ---- death ----
            gs.ForcePlayingForTest();
            player.God = false;
            player.TakeDamage(5000f);
            yield return null;
            Check("player death shows You Died", gs.Phase == Phase.Dead && player.IsDead);
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
