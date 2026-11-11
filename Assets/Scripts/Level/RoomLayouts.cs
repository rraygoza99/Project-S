using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Furnishes rooms: a structural layout (pillars, cover walls) for fights, then a random sprinkle
    /// of barrels, crates and spike traps. Everything stays inside +-10 m and off the door lanes so
    /// every doorway is always reachable.
    /// </summary>
    public static class RoomLayouts
    {
        public const int Count = 6;
        public const float Inner = 10f;

        public static void Furnish(Room room, Dungeon.Palette pal, System.Random rng, int floor)
        {
            switch (room.Type)
            {
                case RoomType.Combat:
                    room.Layout = rng.Next(Count);
                    Structures(room, pal, room.Layout);
                    Sprinkle(room, rng, barrels: rng.Next(0, 3 + floor), crates: rng.Next(0, 4), spikes: rng.Next(0, floor + 2));
                    break;
                case RoomType.Boss:
                    foreach (var p in new[] { new Vector2(-7, -7), new Vector2(7, -7), new Vector2(-7, 7), new Vector2(7, 7) })
                        Block(room, pal.Pillar, p, new Vector2(2, 2), Dungeon.WallH);
                    Sprinkle(room, rng, barrels: 2, crates: 0, spikes: 0);
                    break;
                case RoomType.Start:
                    Sprinkle(room, rng, barrels: 0, crates: 2, spikes: 0);
                    break;
            }
        }

        static void Structures(Room room, Dungeon.Palette pal, int layout)
        {
            float H = Dungeon.WallH, low = 2.2f;
            switch (layout)
            {
                case 0: // four pillars
                    foreach (var p in new[] { new Vector2(-6, -6), new Vector2(6, -6), new Vector2(-6, 6), new Vector2(6, 6) })
                        Block(room, pal.Pillar, p, new Vector2(2, 2), H);
                    break;
                case 1: // central block with low walls
                    Block(room, pal.Pillar, Vector2.zero, new Vector2(4, 4), H);
                    Block(room, pal.Wall, new Vector2(-8, 0), new Vector2(1, 5), low);
                    Block(room, pal.Wall, new Vector2(8, 0), new Vector2(1, 5), low);
                    break;
                case 2: // cross of cover walls
                    Block(room, pal.Wall, new Vector2(-6, 0), new Vector2(1, 6), low);
                    Block(room, pal.Wall, new Vector2(6, 0), new Vector2(1, 6), low);
                    Block(room, pal.Wall, new Vector2(0, -6), new Vector2(6, 1), low);
                    Block(room, pal.Wall, new Vector2(0, 6), new Vector2(6, 1), low);
                    break;
                case 3: // pillar grid
                    for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        if (x != 0 || z != 0) Block(room, pal.Pillar, new Vector2(x * 6, z * 6), new Vector2(1.4f, 1.4f), H);
                    break;
                case 4: // L-shaped corners
                    foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                    {
                        Block(room, pal.Wall, new Vector2(s.x * 7, s.y * 5), new Vector2(4, 1), low);
                        Block(room, pal.Wall, new Vector2(s.x * 8.5f, s.y * 6.5f), new Vector2(1, 4), low);
                    }
                    break;
                default: // open hall: spikes and barrels do the work
                    break;
            }
        }

        static void Sprinkle(Room room, System.Random rng, int barrels, int crates, int spikes)
        {
            for (int i = 0; i < spikes; i++)
                if (TryPlace(room, rng, 1.9f, 1.5f, out var p)) SpikeTrap.Create(room.Root, p, (float)rng.NextDouble());
            for (int i = 0; i < barrels; i++)
            {
                // barrels come in small clusters
                if (!TryPlace(room, rng, 0.8f, 3f, out var p)) continue;
                Prop.Create(PropKind.Barrel, room.Root, p);
                if (rng.NextDouble() < 0.5)
                {
                    var q = p + new Vector3(1.1f, 0, 0.3f);
                    if (room.IsFree(q, 0.6f) && InBounds(room, q)) { room.Occupy(q, 0.7f); Prop.Create(PropKind.Barrel, room.Root, q); }
                }
            }
            for (int i = 0; i < crates; i++)
                if (TryPlace(room, rng, 0.9f, 2f, out var p)) Prop.Create(PropKind.Crate, room.Root, p);
        }

        static bool InBounds(Room room, Vector3 p)
        {
            float x = p.x - room.Center.x, z = p.z - room.Center.z;
            if (Mathf.Abs(x) > Inner || Mathf.Abs(z) > Inner) return false;
            // keep the lanes in front of each door clear
            bool doorLaneX = Mathf.Abs(z) < 2.6f && Mathf.Abs(x) > 6.5f;
            bool doorLaneZ = Mathf.Abs(x) < 2.6f && Mathf.Abs(z) > 6.5f;
            return !doorLaneX && !doorLaneZ;
        }

        static bool TryPlace(Room room, System.Random rng, float radius, float centerClear, out Vector3 pos)
        {
            for (int t = 0; t < 30; t++)
            {
                pos = room.Center + new Vector3((float)(rng.NextDouble() * 2 - 1) * Inner, 0f, (float)(rng.NextDouble() * 2 - 1) * Inner);
                if (!InBounds(room, pos)) continue;
                if (new Vector2(pos.x - room.Center.x, pos.z - room.Center.z).magnitude < centerClear) continue;
                if (!room.IsFree(pos, radius)) continue;
                room.Occupy(pos, radius);
                return true;
            }
            pos = default;
            return false;
        }

        static void Block(Room room, Material mat, Vector2 local, Vector2 size, float height)
        {
            var pos = room.Center + new Vector3(local.x, height * 0.5f, local.y);
            Dungeon.Box(room.Root, "Block", pos, new Vector3(size.x, height, size.y), mat);
            room.Occupy(pos, Mathf.Max(size.x, size.y) * 0.5f + 0.4f);
        }
    }
}
