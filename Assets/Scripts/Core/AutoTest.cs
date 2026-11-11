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
        const float WatchdogSeconds = 240f;
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
            var d = Dungeon.Instance;
            var run = Run.Instance;
            ss.Event += e => events.Add(e);
            yield return new WaitForSeconds(1.0f);

            // ---- floor generation ----
            var rooms = d.Rooms.Values.ToList();
            Log($"floor 1: {rooms.Count} rooms, " + string.Join(" ", rooms.Where(r => r.Type != RoomType.Combat).Select(r => $"{r.Type}@{r.Cell.x},{r.Cell.y}(d{r.Depth})")));
            Check("floor 1 has 10 rooms", rooms.Count == 10);
            Check("floor has start, boss, treasure, shop and rune shrine",
                new[] { RoomType.Start, RoomType.Boss, RoomType.Treasure, RoomType.Shop, RoomType.Shrine }.All(t => rooms.Any(r => r.Type == t)));
            Check("every room is reachable through doors", Reachable(d) == rooms.Count);
            Check("boss room is at least 3 rooms from the start", d.FirstOfType(RoomType.Boss).Depth >= 3);
            Check("run starts in the start room, doors open, no enemies", d.Current?.Type == RoomType.Start && !d.DoorsLocked && Enemy.All.Count == 0);
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

            // ---- Fireball: projectile + explosion ----
            var group = new List<Enemy>
            {
                SpawnAhead(player, EnemyType.Imp, 9f, 0f), SpawnAhead(player, EnemyType.Imp, 9.8f, 1.1f), SpawnAhead(player, EnemyType.Imp, 9.6f, -1.1f),
            };
            foreach (var e in group) e.Freeze(5f);
            yield return new WaitForSeconds(0.3f);
            player.LookAt(group[0].Center);
            yield return CastSequence("DRU", expectReady: "Fireball", showChannel: "04_channel_fireball");
            Check("Fireball readied after ↓→↑", ss.Readied != null && ss.Readied.Name == "Fireball");
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
            yield return CastSequence("UU", expectReady: null);
            Check("wrong sequence ↑↑ fizzles with lockout", events.Contains("fizzle") && ss.Lockout > 0f);
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
            yield return CastSequence("RRDR", expectReady: "Lightning");
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
            yield return CastSequence("DDLR", expectReady: "Frost Nova");
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
            Check($"Heal restored 40 HP ({before:0} -> {player.HP:0})", Mathf.Approximately(player.HP, Mathf.Min(player.MaxHP, before + 40f)));
            player.God = true;
            gs.ClearEnemies();
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
            yield return CastSequence("URRU", expectReady: "Thunder Ring");
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
                         ?? d.Rooms.Values.First(r => r.Type == RoomType.Combat);
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
            if (d.Boss) d.Boss.TakeDamage(99999f);
            yield return new WaitForSeconds(1.2f);
            Check("killing the boss kills its minions and opens the portal", Enemy.All.Count == 0 && d.PortalOpen && !d.DoorsLocked);
            player.LookAt(d.PortalPosition + Vector3.up * 1.5f);
            yield return Shot("20_portal");
            player.Teleport(d.PortalPosition + Vector3.up * 0.1f, 0f);
            yield return new WaitForSeconds(1f);
            Check("portal leads to floor 2", d.Floor == 2 && d.Current?.Type == RoomType.Start && d.Rooms.Count == 12);
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

            // ---- death ----
            gs.ForcePlayingForTest();
            player.God = false;
            player.TakeDamage(5000f);
            yield return null;
            Check("player death shows You Died", gs.Phase == Phase.Dead && player.IsDead);
            yield return Shot("23_death");

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

        static Vector3 AheadPoint(Player p, float distance, float side)
        {
            var room = Dungeon.Instance.Current;
            Vector3 pos = p.transform.position + p.transform.forward * distance + p.transform.right * side;
            pos.x = Mathf.Clamp(pos.x, room.Center.x - 11f, room.Center.x + 11f);
            pos.z = Mathf.Clamp(pos.z, room.Center.z - 11f, room.Center.z + 11f);
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
