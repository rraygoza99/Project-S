using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    public enum RoomType { Start, Combat, Treasure, Shop, Shrine, Boss }

    /// <summary>A room on the floor grid. Most take one cell; big ones span 2x1, 1x2 or 2x2 cells.</summary>
    public class Room
    {
        /// <summary>The room's lowest (min x, min y) cell.</summary>
        public Vector2Int Cell;
        /// <summary>Size in cells (1x1, 2x1, 1x2 or 2x2).</summary>
        public Vector2Int Size = Vector2Int.one;
        public RoomType Type;
        public int Depth;              // rooms from the start room
        public Vector3 Center;
        public Transform Root;
        public bool Visited, Cleared;
        public int Layout = -1;
        public readonly List<Door> Doors = new List<Door>();
        /// <summary>Footprints of obstacles (x, z, radius) in world space, for placing props, spawns and loot.</summary>
        public readonly List<Vector3> Occupied = new List<Vector3>();

        public bool HasEnemies => Type == RoomType.Combat || Type == RoomType.Boss;
        public int CellCount => Size.x * Size.y;
        public bool IsBig => CellCount > 1;

        /// <summary>Half the interior size along x and z.</summary>
        public float HalfX => Dungeon.Half + (Size.x - 1) * Dungeon.Spacing * 0.5f;
        public float HalfZ => Dungeon.Half + (Size.y - 1) * Dungeon.Spacing * 0.5f;

        public IEnumerable<Vector2Int> Cells
        {
            get
            {
                for (int x = 0; x < Size.x; x++)
                for (int y = 0; y < Size.y; y++)
                    yield return Cell + new Vector2Int(x, y);
            }
        }

        public static Vector3 CellCenter(Vector2Int c) => new Vector3(c.x * Dungeon.Spacing, 0f, c.y * Dungeon.Spacing);

        /// <summary>The cell that holds world point p (nearest grid cell).</summary>
        public static Vector2Int CellAt(Vector3 p) =>
            new Vector2Int(Mathf.RoundToInt(p.x / Dungeon.Spacing), Mathf.RoundToInt(p.z / Dungeon.Spacing));

        public bool Owns(Vector2Int c) => c.x >= Cell.x && c.y >= Cell.y && c.x < Cell.x + Size.x && c.y < Cell.y + Size.y;

        /// <summary>True when p is inside the room's floor, at least margin metres from the walls.</summary>
        public bool Contains(Vector3 p, float margin = 0f) =>
            Mathf.Abs(p.x - Center.x) < HalfX - margin && Mathf.Abs(p.z - Center.z) < HalfZ - margin;

        public bool IsFree(Vector3 p, float radius)
        {
            foreach (var o in Occupied)
            {
                float dx = p.x - o.x, dz = p.z - o.y;
                float r = radius + o.z;
                if (dx * dx + dz * dz < r * r) return false;
            }
            return true;
        }

        public void Occupy(Vector3 worldPos, float radius) => Occupied.Add(new Vector3(worldPos.x, worldPos.z, radius));

        public string Label => Type switch
        {
            RoomType.Treasure => "Treasure",
            RoomType.Shop => "Shop",
            RoomType.Shrine => "Rune Shrine",
            RoomType.Boss => "Boss",
            _ => null,
        };
    }

    /// <summary>Doorway between two rooms, with an iron gate that drops while a fight is on.</summary>
    public class Door
    {
        public Room A, B;
        /// <summary>The cell on A's side of the doorway, and the step from it into B.</summary>
        public Vector2Int Cell, Dir;
        public Transform Gate;
        public bool Locked;
        public Room Other(Room r) => r == A ? B : A;
        /// <summary>Middle of the doorway in world space.</summary>
        public Vector3 Position => Room.CellCenter(Cell) + new Vector3(Dir.x, 0, Dir.y) * (Dungeon.Spacing * 0.5f);
    }

    /// <summary>
    /// Lays out a floor as a tree of rooms on a grid (Isaac style): each room joins exactly one earlier room
    /// through one door. Combat rooms are sometimes big (2x1, 1x2, 2x2); start and special rooms are 1x1.
    /// </summary>
    public static class FloorGenerator
    {
        public const int Radius = 4; // grid is 9x9 around the start
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        public class PlanRoom
        {
            public Vector2Int Min, Size;
            public RoomType Type;
            public int Depth;
            public IEnumerable<Vector2Int> Cells
            {
                get
                {
                    for (int x = 0; x < Size.x; x++)
                    for (int y = 0; y < Size.y; y++)
                        yield return Min + new Vector2Int(x, y);
                }
            }
        }

        public class PlanDoor { public int A, B; public Vector2Int Cell, Dir; }

        public class Plan
        {
            public readonly List<PlanRoom> Rooms = new List<PlanRoom>();
            public readonly List<PlanDoor> Doors = new List<PlanDoor>();
        }

        static readonly Vector2Int[] BigShapes = { new Vector2Int(2, 1), new Vector2Int(1, 2), new Vector2Int(2, 2) };

        /// <param name="bigChance">Chance that a new room is big (2x1, 1x2 or 2x2).</param>
        public static Plan Generate(int roomCount, System.Random rng, float bigChance = 0.3f)
        {
            for (int attempt = 0; attempt < 400; attempt++)
            {
                var plan = TryGenerate(roomCount, rng, bigChance);
                if (plan != null) return plan;
            }
            return Fallback(roomCount);
        }

        static Plan TryGenerate(int roomCount, System.Random rng, float bigChance)
        {
            var plan = new Plan();
            var owner = new Dictionary<Vector2Int, int>();
            void Add(PlanRoom r) { foreach (var c in r.Cells) owner[c] = plan.Rooms.Count; plan.Rooms.Add(r); }
            Add(new PlanRoom { Min = Vector2Int.zero, Size = Vector2Int.one, Type = RoomType.Start });
            int maxBig = Mathf.Max(1, roomCount / 3), bigCount = 0;

            for (int guard = 0; plan.Rooms.Count < roomCount && guard < 6000; guard++)
            {
                int fromIdx = rng.Next(plan.Rooms.Count);
                var from = plan.Rooms[fromIdx];
                var fromCells = from.Cells.ToList();
                var fromCell = fromCells[rng.Next(fromCells.Count)];
                var dir = Dirs[rng.Next(4)];
                var doorCell = fromCell + dir;
                if (owner.ContainsKey(doorCell)) continue;

                // Rooms next to the start stay small, so the first fight is never a huge arena.
                var size = Vector2Int.one;
                if (fromIdx != 0 && bigCount < maxBig && rng.NextDouble() < bigChance) size = BigShapes[rng.Next(BigShapes.Length)];
                var min = doorCell - new Vector2Int(rng.Next(size.x), rng.Next(size.y));
                var room = new PlanRoom { Min = min, Size = size, Type = RoomType.Combat };
                var cells = room.Cells.ToList();
                if (cells.Any(c => owner.ContainsKey(c) || Mathf.Abs(c.x) > Radius || Mathf.Abs(c.y) > Radius)) continue;

                // Keep it a tree: the new room may only touch the room it grows from.
                bool touchesOther = false;
                foreach (var c in cells)
                foreach (var d in Dirs)
                    if (owner.TryGetValue(c + d, out int o) && o != fromIdx) touchesOther = true;
                if (touchesOther) continue;

                Add(room);
                if (room.Size != Vector2Int.one) bigCount++;
                plan.Doors.Add(new PlanDoor { A = fromIdx, B = plan.Rooms.Count - 1, Cell = fromCell, Dir = dir });
            }
            if (plan.Rooms.Count < roomCount) return null;

            // Depth = doors from the start. Doors are added parent-first, so one pass is enough.
            var depth = new int[plan.Rooms.Count];
            foreach (var d in plan.Doors) depth[d.B] = depth[d.A] + 1;
            for (int i = 0; i < depth.Length; i++) plan.Rooms[i].Depth = depth[i];

            int Links(int i) => plan.Doors.Count(d => d.A == i || d.B == i);
            var deadEnds = Enumerable.Range(1, plan.Rooms.Count - 1).Where(i => Links(i) == 1).OrderByDescending(i => depth[i]).ToList();
            if (deadEnds.Count < 4 || depth[deadEnds[0]] < 3) return null;

            int boss = deadEnds[0];
            plan.Rooms[boss].Type = RoomType.Boss;
            var small = deadEnds.Skip(1).Where(i => plan.Rooms[i].Size == Vector2Int.one).OrderBy(_ => rng.Next()).ToList();
            if (small.Count < 3) return null;
            var specials = new[] { RoomType.Treasure, RoomType.Shop, RoomType.Shrine };
            for (int i = 0; i < specials.Length; i++) plan.Rooms[small[i]].Type = specials[i];
            return plan;
        }

        /// <summary>Extremely unlikely: a straight corridor with the specials hanging off it.</summary>
        static Plan Fallback(int roomCount)
        {
            var plan = new Plan();
            plan.Rooms.Add(new PlanRoom { Min = Vector2Int.zero, Size = Vector2Int.one, Type = RoomType.Start });
            int spine = roomCount - 3;
            for (int i = 1; i < spine; i++)
            {
                plan.Rooms.Add(new PlanRoom { Min = new Vector2Int(0, i), Size = Vector2Int.one, Type = i == spine - 1 ? RoomType.Boss : RoomType.Combat, Depth = i });
                plan.Doors.Add(new PlanDoor { A = i - 1, B = i, Cell = new Vector2Int(0, i - 1), Dir = Vector2Int.up });
            }
            void Side(Vector2Int at, int parent, RoomType t)
            {
                plan.Rooms.Add(new PlanRoom { Min = at, Size = Vector2Int.one, Type = t, Depth = plan.Rooms[parent].Depth + 1 });
                var p = plan.Rooms[parent].Min;
                plan.Doors.Add(new PlanDoor { A = parent, B = plan.Rooms.Count - 1, Cell = p, Dir = at - p });
            }
            Side(new Vector2Int(1, 1), 1, RoomType.Treasure);
            Side(new Vector2Int(-1, 1), 1, RoomType.Shop);
            Side(new Vector2Int(1, 2), 2, RoomType.Shrine);
            return plan;
        }

        public static IEnumerable<Vector2Int> Directions => Dirs;
    }
}
