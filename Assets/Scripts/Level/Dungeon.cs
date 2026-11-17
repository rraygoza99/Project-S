using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Builds each floor (rooms, doors, obstacles, special rooms), tracks which room the player is in,
    /// locks the doors and spawns the encounter when an uncleared room is entered, opens them when it's
    /// cleared, and moves to the next floor through the portal that appears after the boss.
    /// </summary>
    public class Dungeon : MonoBehaviour
    {
        public static Dungeon Instance { get; private set; }

        public const float Spacing = 28f;      // grid cell size
        public const float Half = 13f;         // half the interior size
        public const float WallH = 5f;
        public const float DoorW = 3.2f, DoorH = 3.6f;
        public const int FinalFloor = 3;

        public int Floor { get; private set; }
        /// <summary>Room by grid cell (a big room is listed under each of its cells).</summary>
        public readonly Dictionary<Vector2Int, Room> Rooms = new Dictionary<Vector2Int, Room>();
        /// <summary>Every room once.</summary>
        public readonly List<Room> AllRooms = new List<Room>();
        public Room Current { get; private set; }
        public bool InCombat { get; private set; }
        public Enemy Boss { get; private set; }
        public Transform LevelRoot { get; private set; }
        public Vector3 PortalPosition { get; private set; }
        public bool PortalOpen { get; private set; }
        public int RoomsCleared { get; private set; }
        /// <summary>Test hook: entering rooms doesn't start fights.</summary>
        public bool HoldEncounters;

        readonly List<Door> doors = new List<Door>();
        readonly Queue<(EnemyType type, Vector3 pos)> pending = new Queue<(EnemyType, Vector3)>();
        float spawnTimer, hpMult = 1f, dmgMult = 1f;
        bool hurtThisRoom;

        /// <summary>Player.TakeDamage calls this (flawless-room achievement).</summary>
        public void NotePlayerHurt() => hurtThisRoom = true;
        Palette pal;
        /// <summary>Items already placed on this floor, so treasure, shrine, shop and boss never repeat each other.</summary>
        readonly List<ItemDef> floorStock = new List<ItemDef>();

        public class Palette
        {
            public Material Wall, Pillar, Floor, Ceiling, Gate, Pedestal;
            public Color Fog, Sigil;
            public float FogStart = 5f, FogEnd = 30f;
            /// <summary>Wall lights: torches, ice glow, braziers.</summary>
            public Color Torch = new Color(1f, 0.55f, 0.2f, 0.8f);
        }

        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        /// <summary>Floor 1 keeps the original crypt; floors 2 and 3 come from FloorThemes.</summary>
        static Palette MakePalette(int floor)
        {
            if (floor >= 2) return FloorThemes.Palette(floor);
            return new Palette
            {
                Wall = Materials.World("wall1", PixelArt.Bricks("tex_wall1", C(122, 92, 76), C(46, 36, 34), 11), Color.white, 2f),
                Pillar = Materials.World("pillar1", PixelArt.Bricks("tex_pillar1", C(92, 98, 112), C(34, 34, 42), 23), Color.white, 2f),
                Floor = Materials.World("floor1", PixelArt.Tiles("tex_floor1", C(84, 78, 70), C(34, 30, 28), 5), Color.white, 2f),
                Ceiling = Materials.World("ceil1", PixelArt.Tiles("tex_ceil1", C(52, 44, 58), C(20, 16, 24), 7), Color.white, 3f),
                Fog = new Color(0.06f, 0.035f, 0.08f), Sigil = new Color(0.55f, 0.15f, 0.7f, 0.6f),
            };
        }

        void Awake()
        {
            Instance = this;
            Enemy.Killed += OnKilled;
            SaveSystem.Register(new SaveSection<DungeonSave>("dungeon", 0, SaveState, LoadState));
        }

        // ---------------- save / load ----------------

        public DungeonSave SaveState()
        {
            var s = new DungeonSave
            {
                floor = Floor, floorSeed = FloorSeed, roomsCleared = RoomsCleared,
                checkpoint = CheckpointRoomId ?? "", portalOpen = PortalOpen,
            };
            foreach (var r in AllRooms) s.rooms.Add(new RoomSave { id = r.Id, visited = r.Visited, cleared = r.Cleared });
            foreach (var p in Pickup.All)
            {
                if (!p) continue;
                var pos = p.transform.position;
                s.pickups.Add(new PickupSave
                {
                    item = p.Item.Id, price = p.Price, x = pos.x, y = p.Height, z = pos.z,
                    spell = p.Item.Spell != null ? SpellCodec.ToData(p.Item.Spell) : null,
                });
            }
            foreach (var b in BuiltProps)
                if (!b.prop || !Prop.All.Contains(b.prop)) s.destroyed.Add(new PropSave { kind = b.kind.ToString(), x = b.pos.x, z = b.pos.z });
            foreach (var i in floorStock) s.floorStock.Add(i.Id);
            return s;
        }

        /// <summary>Rebuilds the saved floor from its seed, then puts back room states, pickups and broken props.</summary>
        public void LoadState(DungeonSave s)
        {
            BuildFloor(s.floor, s.floorSeed);
            var byId = AllRooms.ToDictionary(r => r.Id);
            foreach (var rs in s.rooms)
                if (byId.TryGetValue(rs.id, out var r)) { r.Visited = rs.visited; r.Cleared = rs.cleared; }
            RoomsCleared = s.roomsCleared;

            floorStock.Clear();
            foreach (var id in s.floorStock) { var it = Items.Resolve(id); if (it != null) floorStock.Add(it); }

            // What lies on the floor is exactly what was saved (bought and collected things stay gone).
            foreach (var p in Pickup.All.ToArray()) if (p) p.Discard();
            foreach (var ps in s.pickups)
            {
                bool hasSpell = ps.spell != null && (!string.IsNullOrEmpty(ps.spell.baseId) || !string.IsNullOrEmpty(ps.spell.id));
                var item = hasSpell ? Items.ForgottenRune(SpellCodec.FromData(ps.spell)) : Items.Resolve(ps.item);
                if (item == null) { Debug.LogWarning("[Spellcaster] save: unknown item " + ps.item); continue; }
                Pickup.Spawn(item, new Vector3(ps.x, 0f, ps.z), ps.price, height: ps.y);
            }

            foreach (var d in s.destroyed)
            {
                var hit = BuiltProps.FirstOrDefault(b => b.prop && Prop.All.Contains(b.prop) && b.kind.ToString() == d.kind
                    && new Vector2(b.pos.x - d.x, b.pos.z - d.z).sqrMagnitude < 0.3f * 0.3f);
                if (hit.prop) { Prop.All.Remove(hit.prop); Destroy(hit.prop.gameObject); }
            }

            var boss = AllRooms.FirstOrDefault(r => r.Type == RoomType.Boss);
            if (s.portalOpen && boss != null) RestoreBossRoom(boss);

            CheckpointRoomId = string.IsNullOrEmpty(s.checkpoint) ? null : s.checkpoint;
            var cp = CheckpointRoomId != null && byId.TryGetValue(CheckpointRoomId, out var c) ? c : Rooms[Vector2Int.zero];
            if (Player.Instance) Player.Instance.Teleport(FindFreeSpot(cp, cp.Center, 1f) + Vector3.up * 0.1f, 0f);
            Current = null;
            Enter(cp);
        }

        void OnDestroy() => Enemy.Killed -= OnKilled;

        // ---------------- floor building ----------------

        /// <summary>The seed this floor was built from (derived from the run seed; saved).</summary>
        public int FloorSeed { get; private set; }
        /// <summary>The last room cleared: where Continue puts you (saved).</summary>
        public string CheckpointRoomId;
        /// <summary>Breakable props as built, to tell which were destroyed (saved by kind and position).</summary>
        public readonly List<(Prop prop, PropKind kind, Vector3 pos)> BuiltProps = new List<(Prop, PropKind, Vector3)>();
        /// <summary>Items already placed on this floor (saved, so treasure, shop and boss never repeat).</summary>
        public List<ItemDef> FloorStock => floorStock;

        public void BuildFloor(int floor, int seed = 0)
        {
            Floor = floor;
            FloorSeed = seed != 0 ? seed : RunRandom.FloorSeed(floor);
            CheckpointRoomId = null;
            InCombat = false;
            PortalOpen = false;
            Boss = null;
            pending.Clear();
            foreach (var e in Enemy.All.ToArray()) Destroy(e.gameObject);
            Enemy.All.Clear();
            foreach (var p in FindObjectsByType<Projectile>(FindObjectsSortMode.None)) Destroy(p.gameObject);
            if (LevelRoot) Destroy(LevelRoot.gameObject);
            Rooms.Clear();
            AllRooms.Clear();
            doors.Clear();
            Pickup.All.Clear();
            Prop.All.Clear();
            floorStock.Clear();

            Progress.Max("max_floor", floor);
            hpMult = 1f + 0.4f * (floor - 1);
            dmgMult = 1f + 0.25f * (floor - 1);
            pal = MakePalette(floor);
            pal.Gate = Materials.World("gate", PixelArt.Bars("tex_gate", C(120, 116, 128)), Color.white, 1.6f);
            pal.Pedestal = pal.Pillar;
            Materials.SetFog(pal.Fog, pal.FogStart, pal.FogEnd);
            var ambient = GetComponent<Ambient>();
            if (!ambient) ambient = gameObject.AddComponent<Ambient>();
            ambient.SetFloor(floor);
            if (Player.Instance) Player.Instance.Cam.backgroundColor = pal.Fog;

            LevelRoot = new GameObject("Floor " + floor).transform;
            LevelRoot.SetParent(transform, false);
            var rng = new System.Random(FloorSeed);
            var plan = FloorGenerator.Generate(8 + 2 * floor, rng);

            foreach (var pr in plan.Rooms)
            {
                var room = new Room
                {
                    Cell = pr.Min, Size = pr.Size, Type = pr.Type, Depth = pr.Depth,
                    Center = Room.CellCenter(pr.Min) + new Vector3((pr.Size.x - 1) * Spacing * 0.5f, 0f, (pr.Size.y - 1) * Spacing * 0.5f),
                };
                room.Root = new GameObject($"Room {pr.Min} {pr.Size.x}x{pr.Size.y} {room.Type}").transform;
                room.Root.SetParent(LevelRoot, false);
                foreach (var c in room.Cells) Rooms[c] = room;
                AllRooms.Add(room);
            }
            foreach (var pd in plan.Doors)
            {
                var door = new Door { A = AllRooms[pd.A], B = AllRooms[pd.B], Cell = pd.Cell, Dir = pd.Dir };
                doors.Add(door);
                door.A.Doors.Add(door);
                door.B.Doors.Add(door);
            }
            foreach (var room in AllRooms) BuildRoom(room);
            foreach (var room in AllRooms)
            {
                // Seeded per room, so the same run seed always gives the same props and stock.
                using (RunRandom.Use(RunRandom.RoomSeed(FloorSeed, room, "furnish")))
                {
                    RoomLayouts.Furnish(room, pal, rng, floor);
                    StockRoom(room);
                }
            }
            BuiltProps.Clear();
            foreach (var p in Prop.All) BuiltProps.Add((p, p.Kind, p.transform.position));

            Current = null;
            var start = Rooms[Vector2Int.zero];
            if (Player.Instance) Player.Instance.Teleport(start.Center + new Vector3(0f, 0.1f, -3f), 0f);
            Enter(start);
            if (GameState.Instance) GameState.Instance.Show($"Floor {floor}: {FloorThemes.Name(floor)}", 2.5f);
        }

        void BuildRoom(Room room)
        {
            var root = room.Root;
            Vector3 c = room.Center;
            var span = new Vector3(room.Size.x * Spacing, 1, room.Size.y * Spacing);
            Box(root, "Floor", c + new Vector3(0, -0.5f, 0), span, pal.Floor);
            Box(root, "Ceiling", c + new Vector3(0, WallH + 0.5f, 0), span, pal.Ceiling);

            // Walls go on every cell edge that leads out of the room; edges between the room's own cells stay open.
            foreach (var cell in room.Cells)
            {
                Vector3 cc = Room.CellCenter(cell);
                foreach (var dir in FloorGenerator.Directions)
                {
                    if (room.Owns(cell + dir)) continue;
                    var d3 = new Vector3(dir.x, 0, dir.y);
                    var along = new Vector3(Mathf.Abs(dir.y), 0, Mathf.Abs(dir.x)); // wall runs perpendicular to dir
                    Vector3 wallCenter = cc + d3 * (Half + 0.5f) + Vector3.up * WallH * 0.5f;
                    Vector3 thick = new Vector3(Mathf.Abs(dir.x), 0, Mathf.Abs(dir.y)); // 1 m thickness axis
                    var door = room.Doors.FirstOrDefault(dr => (dr.Cell == cell && dr.Dir == dir) || (dr.Cell == cell + dir && dr.Dir == -dir));

                    // wall torches for atmosphere
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var q = Fx.CreateQuad(root, pal.Torch, Floor >= 3 ? 1.2f : 0.9f);
                        q.transform.position = cc + d3 * (Half - 0.15f) + along * s * 7f + Vector3.up * 3.2f;
                    }

                    if (door == null)
                    {
                        Box(root, "Wall", wallCenter, along * Spacing + thick + Vector3.up * WallH, pal.Wall);
                        continue;
                    }
                    float seg = (Spacing - DoorW) * 0.5f;
                    foreach (float s in new[] { -1f, 1f })
                        Box(root, "Wall", wallCenter + along * s * (DoorW * 0.5f + seg * 0.5f), along * seg + thick + Vector3.up * WallH, pal.Wall);
                    float lintel = WallH - DoorH;
                    Box(root, "Lintel", cc + d3 * (Half + 0.5f) + Vector3.up * (DoorH + lintel * 0.5f), along * DoorW + thick + Vector3.up * lintel, pal.Wall);

                    // One gate per doorway, built by the door's A room.
                    if (door.A == room)
                    {
                        Vector3 gatePos = door.Position + Vector3.up * (-DoorH * 0.5f - 0.05f);
                        door.Gate = Box(LevelRoot, "Gate", gatePos, along * DoorW + thick * 1.8f + Vector3.up * DoorH, pal.Gate).transform;
                    }
                }
            }

            if (room.Type == RoomType.Combat || room.Type == RoomType.Boss)
            {
                var sigil = Fx.CreateQuad(root, room.Type == RoomType.Boss ? new Color(0.9f, 0.15f, 0.1f, 0.6f) : pal.Sigil * 0.7f, room.Type == RoomType.Boss ? 8f : 3f, billboard: false);
                sigil.transform.position = c + Vector3.up * 0.02f;
                sigil.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
        }

        /// <summary>Puts the items on the pedestals of treasure rooms, shops and rune shrines.</summary>
        void StockRoom(Room room)
        {
            var run = Run.Instance;
            switch (room.Type)
            {
                case RoomType.Treasure:
                    Pedestal(room, Vector3.zero, Items.RandomTreasure(run, 0.6f, floorStock), 0, new Color(1f, 0.8f, 0.3f));
                    break;
                case RoomType.Shrine:
                    var rune = Items.RandomRune(floorStock) ?? Items.RandomTreasure(run, 1f, floorStock);
                    Pedestal(room, Vector3.zero, rune, 0, rune.Color);
                    break;
                case RoomType.Shop:
                    var stock = new List<ItemDef>();
                    stock.Add(Items.RandomTreasure(run, 0.5f, floorStock));
                    floorStock.Add(stock[0]);
                    stock.Add(Items.RandomTreasure(run, 0.5f, floorStock));
                    floorStock.Add(stock[1]);
                    stock.Add(Items.RandomRune(floorStock) ?? Items.RandomTreasure(run, 1f, floorStock));
                    floorStock.Add(stock[2]);
                    stock.Add(Items.RandomConsumable());
                    stock.Add(Items.Heart);
                    for (int i = 0; i < stock.Count; i++)
                    {
                        var item = stock[i];
                        int price = Mathf.RoundToInt(item.Price * (1f + 0.15f * (Floor - 1)));
                        Pedestal(room, new Vector3((i - (stock.Count - 1) * 0.5f) * 3.5f, 0, 4f), item, price, new Color(1f, 0.85f, 0.3f));
                    }
                    var rug = Fx.CreateQuad(room.Root, new Color(0.9f, 0.7f, 0.2f, 0.35f), 6f, billboard: false);
                    rug.transform.position = room.Center + new Vector3(0, 0.02f, 4f);
                    rug.transform.rotation = Quaternion.Euler(90, 0, 0);
                    rug.transform.localScale = new Vector3(18f, 4f, 1f);
                    break;
            }
        }

        Vector3 PedestalBase(Room room, Vector3 local, Color glow)
        {
            var pos = room.Center + local;
            Box(room.Root, "Pedestal", pos + Vector3.up * 0.45f, new Vector3(1.1f, 0.9f, 1.1f), pal.Pedestal);
            room.Occupy(pos, 1f);
            var halo = Fx.CreateQuad(room.Root, new Color(glow.r, glow.g, glow.b, 0.5f), 2.2f, billboard: false);
            halo.transform.position = pos + Vector3.up * 0.92f;
            halo.transform.rotation = Quaternion.Euler(90, 0, 0);
            return pos;
        }

        /// <summary>An item on a pedestal (price 0 = free). Public for optional systems that add stock (Grimoire).</summary>
        public void Pedestal(Room room, Vector3 local, ItemDef item, int price, Color glow)
        {
            var pos = PedestalBase(room, local, glow);
            if (item.Kind != ItemKind.Heart && item.Kind != ItemKind.Consumable) floorStock.Add(item);
            Pickup.Spawn(item, pos, price, height: 1.55f);
        }

        // ---------------- room flow ----------------

        void Update()
        {
            var gs = GameState.Instance;
            var player = Player.Instance;
            AnimateGates();
            if (!gs || gs.Phase != Phase.Playing || !player || LevelRoot == null) return;

            var pos = player.transform.position;
            var cell = Room.CellAt(pos);
            if (Rooms.TryGetValue(cell, out var room) && room != Current && room.Contains(pos, 1.2f)) Enter(room);

            if (InCombat)
            {
                spawnTimer -= Time.deltaTime;
                while (pending.Count > 0 && spawnTimer <= 0f)
                {
                    var (type, at) = pending.Dequeue();
                    var e = EnemySpawner.Instance.SpawnAt(type, at, hpMult, dmgMult);
                    if (type.IsBoss) Boss = e;
                    spawnTimer += type.IsBoss ? 0.6f : 0.12f;
                }
                if (pending.Count == 0 && Enemy.All.Count == 0) ClearCurrent();
            }

            if (PortalOpen && Current != null && Current.Type == RoomType.Boss)
            {
                Vector3 d = pos - PortalPosition;
                d.y = 0f;
                if (d.sqrMagnitude < 1.4f * 1.4f) EnterPortal();
            }
        }

        void Enter(Room room)
        {
            Current = room;
            bool first = !room.Visited;
            room.Visited = true;
            if (!room.HasEnemies) { room.Cleared = true; if (first && room.Label != null) GameState.Instance?.Show(room.Label, 1.5f); return; }
            if (room.Cleared || HoldEncounters) return;
            StartEncounter(room);
        }

        void StartEncounter(Room room)
        {
            InCombat = true;
            hurtThisRoom = false;
            SetGates(room, true);
            Sfx.Play("door", 0.8f, 0.8f);
            pending.Clear();
            spawnTimer = 0.5f;
            var player = Player.Instance.transform.position;
            if (room.Type == RoomType.Boss)
            {
                var boss = EnemyType.BossFor(Floor);
                pending.Enqueue((boss, room.Center + new Vector3(0, 0.05f, 0)));
                GameState.Instance.Show(boss.Name, 2.5f);
                Sfx.Play("boss", 0.9f);
                return;
            }
            // The wave is seeded per room: the same run always meets the same enemies here.
            // (Where they appear depends on where you stand, so that is rolled separately.)
            foreach (var type in PreviewWave(room))
                pending.Enqueue((type, RandomSpawnPoint(room, player, type.Kamikaze ? 10f : 7f)));
        }

        /// <summary>The enemy types of this room's wave, from the room's seed (also a test hook).</summary>
        public List<EnemyType> PreviewWave(Room room)
        {
            var list = new List<EnemyType>();
            using (RunRandom.Use(RunRandom.RoomSeed(FloorSeed, room, "wave")))
            {
                int count = 3 + 2 * Floor + Random.Range(0, 3) + Mathf.Min(room.Depth, 4) / 2;
                // Big rooms hold bigger fights.
                if (room.IsBig) count = Mathf.Min(Mathf.RoundToInt(count * (room.CellCount >= 4 ? 2.1f : 1.6f)), 22);
                var picked = new Dictionary<EnemyType, int>();
                for (int i = 0; i < count; i++)
                {
                    var type = EnemyType.PickForEncounter(Floor, room.Depth, () => Random.value, t => picked.TryGetValue(t, out int n) ? n : 0);
                    picked[type] = (picked.TryGetValue(type, out int had) ? had : 0) + 1;
                    list.Add(type);
                }
            }
            return list;
        }

        /// <summary>Hook: a fight room was cleared for the first time (Grimoire rewards).</summary>
        public static event System.Action<Room> RoomCleared;

        public void ClearCurrent()
        {
            var room = Current;
            InCombat = false;
            pending.Clear();
            if (room == null) return;
            bool wasCleared = room.Cleared;
            room.Cleared = true;
            SetGates(room, false);
            if (wasCleared) return;
            RoomsCleared++;
            Progress.Add("rooms");
            if (!hurtThisRoom) Progress.Add("flawless_rooms");
            Sfx.Play("clear", 0.7f);
            // Rewards are seeded per room (the same run always gets the same ones here).
            using (RunRandom.Use(RunRandom.RoomSeed(FloorSeed, room, "clear")))
            {
                if (room.Type == RoomType.Boss)
                {
                    OpenPortal(room);
                    Pedestal(room, BossPedestalLocal, Items.RandomTreasure(Run.Instance, 0.8f, floorStock), 0, BossPedestalGlow);
                    Pickup.Spawn(Items.Heart, room.Center + new Vector3(4f, 0, 0));
                    GameState.Instance.Show(Floor >= FinalFloor ? "Step into the portal to escape" : "Portal opened", 3f);
                }
                else Loot.RoomClearReward(room);
                RoomCleared?.Invoke(room);
            }
        }

        static readonly Vector3 BossPedestalLocal = new Vector3(-4f, 0, 0);
        static readonly Color BossPedestalGlow = new Color(1f, 0.8f, 0.3f);

        /// <summary>The portal out of a cleared boss room (also rebuilt when a save is loaded).</summary>
        public void OpenPortal(Room room)
        {
            PortalPosition = FindFreeSpot(room, room.Center + new Vector3(0, 0, 3f), 1.5f);
            PortalOpen = true;
            var portal = Fx.CreateQuad(room.Root, new Color(0.6f, 0.3f, 1f, 0.9f), 3.2f);
            portal.transform.position = PortalPosition + Vector3.up * 1.6f;
            var ring = Fx.CreateQuad(room.Root, new Color(0.7f, 0.4f, 1f, 0.7f), 3f, billboard: false);
            ring.transform.position = PortalPosition + Vector3.up * 0.03f;
            ring.transform.rotation = Quaternion.Euler(90, 0, 0);
        }

        /// <summary>Loading a cleared boss room: the portal and the empty reward pedestal (its item is a saved pickup).</summary>
        public void RestoreBossRoom(Room room)
        {
            OpenPortal(room);
            PedestalBase(room, BossPedestalLocal, BossPedestalGlow);
        }

        void EnterPortal()
        {
            PortalOpen = false;
            Sfx.Play("portal", 0.9f);
            if (Floor >= FinalFloor) GameState.Instance.Win();
            else BuildFloor(Floor + 1);
        }

        void OnKilled(Enemy e)
        {
            if (e != Boss) return;
            // The boss takes its minions with it.
            foreach (var o in Enemy.All.ToArray()) o.TakeDamage(99999f);
            Boss = null;
        }

        public void SetGates(Room room, bool locked)
        {
            foreach (var d in room.Doors) d.Locked = locked;
        }

        void AnimateGates()
        {
            float dt = Time.deltaTime;
            foreach (var d in doors)
            {
                if (!d.Gate) continue;
                var p = d.Gate.position;
                float target = d.Locked ? DoorH * 0.5f : -DoorH * 0.5f - 0.05f;
                p.y = Mathf.MoveTowards(p.y, target, (d.Locked ? 14f : 5f) * dt);
                d.Gate.position = p;
            }
        }

        public bool DoorsLocked => Current != null && Current.Doors.Any(d => d.Locked);

        // ---------------- helpers ----------------

        /// <summary>A free floor spot in the room, at least minDist from avoid.</summary>
        public Vector3 RandomSpawnPoint(Room room, Vector3 avoid, float minDist)
        {
            Vector3 best = room.Center;
            for (int t = 0; t < 40; t++)
            {
                float hx = room.HalfX - 2f, hz = room.HalfZ - 2f;
                var p = room.Center + new Vector3(Random.Range(-hx, hx), 0.05f, Random.Range(-hz, hz));
                if (!room.IsFree(p, 0.9f)) continue;
                best = p;
                Vector3 d = p - avoid;
                d.y = 0f;
                if (d.magnitude >= minDist) return p;
            }
            return best;
        }

        public Vector3 FindFreeSpot(Room room, Vector3 near, float radius)
        {
            if (room.IsFree(near, radius)) return near;
            for (int ring = 1; ring < 10; ring++)
            for (int a = 0; a < 8; a++)
            {
                float ang = a * Mathf.PI / 4f;
                var p = near + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * ring * 1.2f;
                if (room.Contains(p, 1.5f) && room.IsFree(p, radius)) return p;
            }
            return near;
        }

        public Room RoomAt(Vector3 pos)
        {
            return Rooms.TryGetValue(Room.CellAt(pos), out var r) ? r : null;
        }

        public Room FirstOfType(RoomType t) => AllRooms.FirstOrDefault(r => r.Type == t);

        /// <summary>Is the room drawn on the map? Visited rooms and their neighbours are.</summary>
        public bool IsRevealed(Room r) => r.Visited || r.Doors.Any(d => d.Other(r).Visited);

        public static GameObject Box(Transform parent, string name, Vector3 worldPos, Vector3 size, Material mat)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.layer = Layers.World;
            g.transform.SetParent(parent, false);
            g.transform.position = worldPos;
            g.transform.localScale = size;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g;
        }
    }
}
