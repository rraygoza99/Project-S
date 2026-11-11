using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    public enum RoomType { Start, Combat, Treasure, Shop, Shrine, Boss }

    /// <summary>One cell of the floor grid.</summary>
    public class Room
    {
        public Vector2Int Cell;
        public RoomType Type;
        public int Depth;              // steps from the start room
        public Vector3 Center;
        public Transform Root;
        public bool Visited, Cleared;
        public int Layout = -1;
        public readonly List<Door> Doors = new List<Door>();
        /// <summary>Footprints of obstacles (x, z, radius) in world space, for placing props, spawns and loot.</summary>
        public readonly List<Vector3> Occupied = new List<Vector3>();

        public bool HasEnemies => Type == RoomType.Combat || Type == RoomType.Boss;

        /// <summary>True when p is inside the room's floor, at least margin metres from the walls.</summary>
        public bool Contains(Vector3 p, float margin = 0f)
        {
            float h = Dungeon.Half - margin;
            return Mathf.Abs(p.x - Center.x) < h && Mathf.Abs(p.z - Center.z) < h;
        }

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
        public Transform Gate;
        public bool Locked;
        public Room Other(Room r) => r == A ? B : A;
    }

    /// <summary>Lays out a floor as a tree of rooms on a grid (Isaac style) and assigns the special rooms.</summary>
    public static class FloorGenerator
    {
        public const int Radius = 4; // grid is 9x9 around the start
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        public static Dictionary<Vector2Int, (RoomType type, int depth)> Generate(int roomCount, System.Random rng)
        {
            for (int attempt = 0; attempt < 400; attempt++)
            {
                var cells = new HashSet<Vector2Int> { Vector2Int.zero };
                var list = new List<Vector2Int> { Vector2Int.zero };
                for (int guard = 0; cells.Count < roomCount && guard < 4000; guard++)
                {
                    var from = list[rng.Next(list.Count)];
                    var c = from + Dirs[rng.Next(4)];
                    if (cells.Contains(c) || Mathf.Abs(c.x) > Radius || Mathf.Abs(c.y) > Radius) continue;
                    if (Neighbours(cells, c) > 1) continue; // keep it a tree: every room has one way in
                    if (c == Vector2Int.zero) continue;
                    cells.Add(c);
                    list.Add(c);
                }
                if (cells.Count < roomCount) continue;

                var depth = Distances(cells);
                var deadEnds = cells.Where(c => c != Vector2Int.zero && Neighbours(cells, c) == 1)
                    .OrderByDescending(c => depth[c]).ToList();
                if (deadEnds.Count < 4 || depth[deadEnds[0]] < 3) continue;

                var result = cells.ToDictionary(c => c, c => (RoomType.Combat, depth[c]));
                result[Vector2Int.zero] = (RoomType.Start, 0);
                result[deadEnds[0]] = (RoomType.Boss, depth[deadEnds[0]]);
                var rest = deadEnds.Skip(1).OrderBy(_ => rng.Next()).ToList();
                var specials = new[] { RoomType.Treasure, RoomType.Shop, RoomType.Shrine };
                for (int i = 0; i < specials.Length; i++) result[rest[i]] = (specials[i], depth[rest[i]]);
                return result;
            }
            // Extremely unlikely: fall back to a straight corridor with the specials hanging off it.
            var fallback = new Dictionary<Vector2Int, (RoomType, int)> { [Vector2Int.zero] = (RoomType.Start, 0) };
            for (int i = 1; i < roomCount - 3; i++) fallback[new Vector2Int(0, i)] = (RoomType.Combat, i);
            fallback[new Vector2Int(0, roomCount - 3)] = (RoomType.Boss, roomCount - 3);
            fallback[new Vector2Int(1, 1)] = (RoomType.Treasure, 2);
            fallback[new Vector2Int(-1, 1)] = (RoomType.Shop, 2);
            fallback[new Vector2Int(1, 2)] = (RoomType.Shrine, 3);
            return fallback;
        }

        public static int Neighbours(ICollection<Vector2Int> cells, Vector2Int c) => Dirs.Count(d => cells.Contains(c + d));

        public static IEnumerable<Vector2Int> Directions => Dirs;

        static Dictionary<Vector2Int, int> Distances(HashSet<Vector2Int> cells)
        {
            var dist = new Dictionary<Vector2Int, int> { [Vector2Int.zero] = 0 };
            var q = new Queue<Vector2Int>();
            q.Enqueue(Vector2Int.zero);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (cells.Contains(n) && !dist.ContainsKey(n)) { dist[n] = dist[c] + 1; q.Enqueue(n); }
                }
            }
            return dist;
        }
    }
}
