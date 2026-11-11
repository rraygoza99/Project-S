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
        public readonly Dictionary<Vector2Int, Room> Rooms = new Dictionary<Vector2Int, Room>();
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
        }

        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        static Palette MakePalette(int floor)
        {
            switch (floor)
            {
                case 1:
                    return new Palette
                    {
                        Wall = Materials.World("wall1", PixelArt.Bricks("tex_wall1", C(122, 92, 76), C(46, 36, 34), 11), Color.white, 2f),
                        Pillar = Materials.World("pillar1", PixelArt.Bricks("tex_pillar1", C(92, 98, 112), C(34, 34, 42), 23), Color.white, 2f),
                        Floor = Materials.World("floor1", PixelArt.Tiles("tex_floor1", C(84, 78, 70), C(34, 30, 28), 5), Color.white, 2f),
                        Ceiling = Materials.World("ceil1", PixelArt.Tiles("tex_ceil1", C(52, 44, 58), C(20, 16, 24), 7), Color.white, 3f),
                        Fog = new Color(0.06f, 0.035f, 0.08f), Sigil = new Color(0.55f, 0.15f, 0.7f, 0.6f),
                    };
                case 2:
                    return new Palette
                    {
                        Wall = Materials.World("wall2", PixelArt.Bricks("tex_wall2", C(70, 96, 104), C(24, 34, 40), 13), Color.white, 2f),
                        Pillar = Materials.World("pillar2", PixelArt.Bricks("tex_pillar2", C(110, 120, 128), C(36, 40, 48), 29), Color.white, 2f),
                        Floor = Materials.World("floor2", PixelArt.Tiles("tex_floor2", C(60, 74, 80), C(22, 28, 32), 6), Color.white, 2f),
                        Ceiling = Materials.World("ceil2", PixelArt.Tiles("tex_ceil2", C(36, 46, 56), C(14, 18, 24), 8), Color.white, 3f),
                        Fog = new Color(0.025f, 0.05f, 0.07f), Sigil = new Color(0.2f, 0.55f, 0.9f, 0.6f),
                    };
                default:
                    return new Palette
                    {
                        Wall = Materials.World("wall3", PixelArt.Bricks("tex_wall3", C(120, 52, 40), C(40, 14, 12), 17), Color.white, 2f),
                        Pillar = Materials.World("pillar3", PixelArt.Bricks("tex_pillar3", C(70, 60, 60), C(26, 18, 18), 31), Color.white, 2f),
                        Floor = Materials.World("floor3", PixelArt.Tiles("tex_floor3", C(80, 50, 44), C(30, 14, 12), 9), Color.white, 2f),
                        Ceiling = Materials.World("ceil3", PixelArt.Tiles("tex_ceil3", C(50, 24, 22), C(18, 8, 8), 10), Color.white, 3f),
                        Fog = new Color(0.08f, 0.025f, 0.02f), Sigil = new Color(0.9f, 0.3f, 0.1f, 0.6f),
                    };
            }
        }

        void Awake()
        {
            Instance = this;
            Enemy.Killed += OnKilled;
        }

        void OnDestroy() => Enemy.Killed -= OnKilled;

        // ---------------- floor building ----------------

        public void BuildFloor(int floor, int seed = 0)
        {
            Floor = floor;
            InCombat = false;
            PortalOpen = false;
            Boss = null;
            pending.Clear();
            foreach (var e in Enemy.All.ToArray()) Destroy(e.gameObject);
            Enemy.All.Clear();
            foreach (var p in FindObjectsByType<Projectile>(FindObjectsSortMode.None)) Destroy(p.gameObject);
            if (LevelRoot) Destroy(LevelRoot.gameObject);
            Rooms.Clear();
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
            Materials.SetFog(pal.Fog, 5f, 30f);
            if (Player.Instance) Player.Instance.Cam.backgroundColor = pal.Fog;

            LevelRoot = new GameObject("Floor " + floor).transform;
            LevelRoot.SetParent(transform, false);
            var rng = new System.Random(seed != 0 ? seed : Random.Range(1, int.MaxValue));
            var layout = FloorGenerator.Generate(8 + 2 * floor, rng);

            foreach (var kv in layout)
            {
                var room = new Room
                {
                    Cell = kv.Key, Type = kv.Value.type, Depth = kv.Value.depth,
                    Center = new Vector3(kv.Key.x * Spacing, 0f, kv.Key.y * Spacing),
                };
                room.Root = new GameObject($"Room {kv.Key} {room.Type}").transform;
                room.Root.SetParent(LevelRoot, false);
                Rooms[kv.Key] = room;
            }
            foreach (var room in Rooms.Values) BuildRoom(room);
            foreach (var room in Rooms.Values)
            {
                RoomLayouts.Furnish(room, pal, rng, floor);
                StockRoom(room);
            }

            Current = null;
            var start = Rooms[Vector2Int.zero];
            if (Player.Instance) Player.Instance.Teleport(start.Center + new Vector3(0f, 0.1f, -3f), 0f);
            Enter(start);
            if (GameState.Instance) GameState.Instance.Show(floor == 1 ? "Floor 1" : $"Floor {floor}", 2.5f);
        }

        void BuildRoom(Room room)
        {
            var root = room.Root;
            Vector3 c = room.Center;
            Box(root, "Floor", c + new Vector3(0, -0.5f, 0), new Vector3(Spacing, 1, Spacing), pal.Floor);
            Box(root, "Ceiling", c + new Vector3(0, WallH + 0.5f, 0), new Vector3(Spacing, 1, Spacing), pal.Ceiling);

            foreach (var dir in FloorGenerator.Directions)
            {
                var d3 = new Vector3(dir.x, 0, dir.y);
                var along = new Vector3(Mathf.Abs(dir.y), 0, Mathf.Abs(dir.x)); // wall runs perpendicular to dir
                Vector3 wallCenter = c + d3 * (Half + 0.5f) + Vector3.up * WallH * 0.5f;
                bool open = Rooms.TryGetValue(room.Cell + dir, out var other);
                Vector3 thick = new Vector3(Mathf.Abs(dir.x), 0, Mathf.Abs(dir.y)); // 1 m thickness axis
                if (!open)
                {
                    Box(root, "Wall", wallCenter, along * Spacing + thick + Vector3.up * WallH, pal.Wall);
                    continue;
                }
                float seg = (Spacing - DoorW) * 0.5f;
                foreach (float s in new[] { -1f, 1f })
                    Box(root, "Wall", wallCenter + along * s * (DoorW * 0.5f + seg * 0.5f), along * seg + thick + Vector3.up * WallH, pal.Wall);
                float lintel = WallH - DoorH;
                Box(root, "Lintel", c + d3 * (Half + 0.5f) + Vector3.up * (DoorH + lintel * 0.5f), along * DoorW + thick + Vector3.up * lintel, pal.Wall);

                // One gate per doorway, owned by the room with the lower cell.
                if (Compare(room.Cell, other.Cell) < 0)
                {
                    var door = new Door { A = room, B = other };
                    Vector3 gatePos = c + d3 * (Spacing * 0.5f) + Vector3.up * (-DoorH * 0.5f - 0.05f);
                    door.Gate = Box(LevelRoot, "Gate", gatePos, along * DoorW + new Vector3(Mathf.Abs(dir.x), 0, Mathf.Abs(dir.y)) * 1.8f + Vector3.up * DoorH, pal.Gate).transform;
                    doors.Add(door);
                    room.Doors.Add(door);
                    other.Doors.Add(door);
                }
            }

            // wall torches for atmosphere
            foreach (var dir in FloorGenerator.Directions)
            {
                var d3 = new Vector3(dir.x, 0, dir.y);
                var along = new Vector3(Mathf.Abs(dir.y), 0, Mathf.Abs(dir.x));
                foreach (float s in new[] { -1f, 1f })
                {
                    var q = Fx.CreateQuad(root, new Color(1f, 0.55f, 0.2f, 0.8f), 0.9f);
                    q.transform.position = c + d3 * (Half - 0.15f) + along * s * 7f + Vector3.up * 3.2f;
                }
            }

            if (room.Type == RoomType.Combat || room.Type == RoomType.Boss)
            {
                var sigil = Fx.CreateQuad(root, room.Type == RoomType.Boss ? new Color(0.9f, 0.15f, 0.1f, 0.6f) : pal.Sigil * 0.7f, room.Type == RoomType.Boss ? 8f : 3f, billboard: false);
                sigil.transform.position = c + Vector3.up * 0.02f;
                sigil.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
        }

        static int Compare(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);

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

        void Pedestal(Room room, Vector3 local, ItemDef item, int price, Color glow)
        {
            var pos = room.Center + local;
            Box(room.Root, "Pedestal", pos + Vector3.up * 0.45f, new Vector3(1.1f, 0.9f, 1.1f), pal.Pedestal);
            room.Occupy(pos, 1f);
            var halo = Fx.CreateQuad(room.Root, new Color(glow.r, glow.g, glow.b, 0.5f), 2.2f, billboard: false);
            halo.transform.position = pos + Vector3.up * 0.92f;
            halo.transform.rotation = Quaternion.Euler(90, 0, 0);
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
            var cell = new Vector2Int(Mathf.RoundToInt(pos.x / Spacing), Mathf.RoundToInt(pos.z / Spacing));
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
                pending.Enqueue((EnemyType.Boss, room.Center + new Vector3(0, 0.05f, 0)));
                GameState.Instance.Show(EnemyType.Boss.Name, 2.5f);
                Sfx.Play("boss", 0.9f);
                return;
            }
            int count = 3 + 2 * Floor + Random.Range(0, 3) + Mathf.Min(room.Depth, 4) / 2;
            float rangedChance = Mathf.Min(0.2f + 0.1f * Floor + 0.03f * room.Depth, 0.5f);
            for (int i = 0; i < count; i++)
                pending.Enqueue((Random.value < rangedChance ? EnemyType.Cultist : EnemyType.Imp, RandomSpawnPoint(room, player, 7f)));
        }

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
            if (room.Type == RoomType.Boss)
            {
                PortalPosition = FindFreeSpot(room, room.Center + new Vector3(0, 0, 3f), 1.5f);
                PortalOpen = true;
                var portal = Fx.CreateQuad(room.Root, new Color(0.6f, 0.3f, 1f, 0.9f), 3.2f);
                portal.transform.position = PortalPosition + Vector3.up * 1.6f;
                var ring = Fx.CreateQuad(room.Root, new Color(0.7f, 0.4f, 1f, 0.7f), 3f, billboard: false);
                ring.transform.position = PortalPosition + Vector3.up * 0.03f;
                ring.transform.rotation = Quaternion.Euler(90, 0, 0);
                Pedestal(room, new Vector3(-4f, 0, 0), Items.RandomTreasure(Run.Instance, 0.8f, floorStock), 0, new Color(1f, 0.8f, 0.3f));
                Pickup.Spawn(Items.Heart, room.Center + new Vector3(4f, 0, 0));
                GameState.Instance.Show(Floor >= FinalFloor ? "Step into the portal to escape" : "Portal opened", 3f);
            }
            else Loot.RoomClearReward(room);
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
                var p = room.Center + new Vector3(Random.Range(-11f, 11f), 0.05f, Random.Range(-11f, 11f));
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
            var cell = new Vector2Int(Mathf.RoundToInt(pos.x / Spacing), Mathf.RoundToInt(pos.z / Spacing));
            return Rooms.TryGetValue(cell, out var r) ? r : null;
        }

        public Room FirstOfType(RoomType t) => Rooms.Values.FirstOrDefault(r => r.Type == t);

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
