using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// What makes each floor its own place: textures and fog, room structures, hazards, decoration and
    /// ambient particles. Floor 1 (The Crypt) keeps the original look; floor 2 is the Frozen Caverns,
    /// floor 3 The Forge. Enemy rosters and bosses are in EnemyTypesFloors.cs.
    /// </summary>
    public static class FloorThemes
    {
        public static string Name(int floor) => floor <= 1 ? "The Crypt" : floor == 2 ? "Frozen Caverns" : "The Forge";

        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        public static Dungeon.Palette Palette(int floor)
        {
            if (floor == 2)
            {
                var frost = C(205, 230, 248);
                return new Dungeon.Palette
                {
                    Wall = Materials.World("wall_cave", ThemeTextures.Rock("tex_cavewall", C(80, 94, 112), C(24, 30, 42), frost, 0.35f, 21), Color.white, 2f),
                    Pillar = Materials.World("pillar_ice", ThemeTextures.Ice("tex_icepillar", C(96, 150, 190), 7), new Color(0.95f, 1f, 1.1f), 1.5f),
                    Floor = Materials.World("floor_cave", ThemeTextures.Rock("tex_cavefloor", C(70, 82, 98), C(30, 36, 48), frost, 0.25f, 22), Color.white, 2f),
                    Ceiling = Materials.World("ceil_cave", ThemeTextures.Rock("tex_caveceil", C(46, 54, 68), C(16, 20, 28), frost, 0.08f, 23), Color.white, 3f),
                    Fog = new Color(0.035f, 0.065f, 0.1f), FogStart = 3f, FogEnd = 24f,
                    Sigil = new Color(0.25f, 0.6f, 1f, 0.6f), Torch = new Color(0.45f, 0.8f, 1f, 0.75f),
                };
            }
            return new Dungeon.Palette
            {
                Wall = Materials.World("wall_forge", ThemeTextures.Plates("tex_forgewall", C(98, 84, 78), C(30, 22, 20), 31), Color.white, 2f),
                Pillar = Materials.World("pillar_basalt", ThemeTextures.Basalt("tex_basaltpillar", C(58, 46, 44), C(170, 64, 20), 32), Color.white, 1.5f),
                Floor = Materials.World("floor_forge", ThemeTextures.Basalt("tex_forgefloor", C(66, 54, 50), C(120, 46, 22), 33), Color.white, 2f),
                Ceiling = Materials.World("ceil_forge", ThemeTextures.Plates("tex_forgeceil", C(54, 42, 40), C(18, 12, 10), 34), Color.white, 3f),
                Fog = new Color(0.11f, 0.04f, 0.02f), FogStart = 6f, FogEnd = 32f,
                Sigil = new Color(1f, 0.35f, 0.08f, 0.6f), Torch = new Color(1f, 0.45f, 0.12f, 0.9f),
            };
        }

        // ---------------- fight rooms ----------------

        /// <summary>Structure layouts per floor (floors 2 and 3 have their own five).</summary>
        public const int Layouts = 5;

        public static void Structures(Room room, Dungeon.Palette pal, System.Random rng, int floor, int layout, Vector3 o)
        {
            if (floor == 2) CaveStructures(room, pal, rng, layout, o);
            else ForgeStructures(room, pal, rng, layout, o);
        }

        public static void Hazards(Room room, System.Random rng, int floor)
        {
            int n = room.CellCount;
            if (floor == 2)
            {
                for (int i = rng.Next(2, 4) * n; i > 0; i--)
                    if (FreeSpot(room, rng, 2.4f, out var p)) IcePatch.Create(room.Root, p, new Vector2(Rand(rng, 3f, 5.5f), Rand(rng, 3f, 5.5f)), Rand(rng, 0f, 90f));
                for (int i = rng.Next(1, 4) * n; i > 0; i--)
                    if (RoomLayouts.TryPlace(room, rng, 0.8f, 2.5f, out var p)) Prop.Create(PropKind.Crystal, room.Root, p);
                for (int i = rng.Next(1, 3) * n; i > 0; i--)
                    if (FreeSpot(room, rng, 1.5f, out var p)) CeilingPod.Create(room.Root, p);
                for (int i = rng.Next(0, 3) * n; i > 0; i--)
                    if (RoomLayouts.TryPlace(room, rng, 0.9f, 2f, out var p)) Prop.Create(PropKind.Crate, room.Root, p);
            }
            else
            {
                for (int i = rng.Next(0, 2) * n; i > 0; i--)
                {
                    float r = Rand(rng, 1.4f, 2.2f);
                    if (RoomLayouts.TryPlace(room, rng, r + 0.4f, 3f, out var p)) LavaPool.Create(room.Root, p, r);
                }
                for (int i = rng.Next(1, 3) * n; i > 0; i--)
                    if (RoomLayouts.TryPlace(room, rng, 1.2f, 1.5f, out var p)) FireJet.Create(room.Root, p, (float)rng.NextDouble());
                RoomLayouts.Sprinkle(room, rng, barrels: rng.Next(1, 5) * n, crates: rng.Next(0, 2) * n, spikes: 0);
            }
        }

        public static void Boss(Room room, Dungeon.Palette pal, System.Random rng, int floor)
        {
            var c = room.Center;
            foreach (var q in new[] { new Vector2(-7, -7), new Vector2(7, -7), new Vector2(-7, 7), new Vector2(7, 7) })
            {
                if (floor == 2) Crystals(room, pal, rng, c + new Vector3(q.x, 0f, q.y), 5);
                else Pillar(room, pal.Pillar, c + new Vector3(q.x, 0f, q.y), 2f, Dungeon.WallH);
            }
            if (floor == 2)
            {
                IcePatch.Create(room.Root, c + new Vector3(-4f, 0f, 2f), new Vector2(4f, 6f), 20f);
                IcePatch.Create(room.Root, c + new Vector3(4.5f, 0f, -3f), new Vector2(5f, 3.5f), -15f);
            }
            else
            {
                foreach (var q in new[] { new Vector2(-6, -9.5f), new Vector2(6, 9.5f), new Vector2(-9.5f, 6), new Vector2(9.5f, -6) })
                {
                    var p = c + new Vector3(q.x, 0f, q.y);
                    room.Occupy(p, 2f);
                    LavaPool.Create(room.Root, p, 1.6f);
                }
            }
        }

        // ---------------- decoration (every room) ----------------

        public static void Decor(Room room, Dungeon.Palette pal, System.Random rng, int floor)
        {
            if (floor == 2)
            {
                // Stalactites hang from the ceiling, too high to get in the way.
                var rock = pal.Wall;
                for (int i = 6 + 4 * room.CellCount; i > 0; i--)
                {
                    var p = RandomPoint(room, rng);
                    float len = Rand(rng, 0.7f, 1.6f), w = Rand(rng, 0.35f, 0.8f);
                    var g = Dungeon.Box(room.Root, "Stalactite", p + Vector3.up * (Dungeon.WallH - len * 0.5f), new Vector3(w, len, w), rock);
                    Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.transform.rotation = Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f);
                    var tip = Dungeon.Box(g.transform.parent, "StalactiteTip", p + Vector3.up * (Dungeon.WallH - len - 0.25f), new Vector3(w * 0.45f, 0.5f, w * 0.45f), rock);
                    Object.DestroyImmediate(tip.GetComponent<Collider>());
                }
            }
            else if (floor >= 3)
            {
                // Glowing floor vents and hanging chains.
                for (int i = 2 + room.CellCount; i > 0; i--)
                {
                    var p = RandomPoint(room, rng);
                    var g = Fx.CreateQuad(room.Root, new Color(1f, 0.35f, 0.05f, 0.35f), Rand(rng, 1.2f, 2.4f), billboard: false);
                    g.transform.position = Fx.Ground(p) + Vector3.up * 0.03f;
                    g.transform.rotation = Quaternion.Euler(90, 0, 0);
                }
                var iron = Materials.World("chain", PixelArt.Bars("tex_chain", C(90, 84, 86)), Color.white, 1f);
                for (int i = 3 + 2 * room.CellCount; i > 0; i--)
                {
                    var p = RandomPoint(room, rng);
                    float len = Rand(rng, 0.8f, 2f);
                    var g = Dungeon.Box(room.Root, "Chain", p + Vector3.up * (Dungeon.WallH - len * 0.5f), new Vector3(0.12f, len, 0.12f), iron);
                    Object.DestroyImmediate(g.GetComponent<Collider>());
                }
            }
        }

        // ---------------- floor 2 structures ----------------

        static void CaveStructures(Room room, Dungeon.Palette pal, System.Random rng, int layout, Vector3 o)
        {
            var rock = pal.Wall;
            switch (layout)
            {
                case 0: // boulder clusters
                    for (int i = 0; i < 4; i++)
                    {
                        if (!RoomLayouts.TryPlace(room, rng, 2f, 2.5f, out var p)) continue;
                        Boulder(room, rock, rng, p, Rand(rng, 1.8f, 3f), Rand(rng, 1.4f, 2.6f));
                        if (rng.NextDouble() < 0.6) Boulder(room, rock, rng, p + new Vector3(Rand(rng, -1.2f, 1.2f), 0f, Rand(rng, -1.2f, 1.2f)), Rand(rng, 1f, 1.6f), Rand(rng, 0.8f, 1.4f));
                    }
                    break;
                case 1: // crystal groves
                    for (int i = 0; i < 2; i++)
                        if (RoomLayouts.TryPlace(room, rng, 2.6f, 3f, out var p)) Crystals(room, pal, rng, p, rng.Next(4, 7));
                    break;
                case 2: // a broken ring of ice walls (gaps on the door lanes)
                    for (int q = 0; q < 4; q++)
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (q * 90f + 22f + k * 23f) * Mathf.Deg2Rad;
                        var p = o + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 6.8f;
                        var g = Dungeon.Box(room.Root, "IceWall", p + Vector3.up * 0.95f, new Vector3(2.5f, 1.9f, 0.9f), pal.Pillar);
                        g.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                        room.Occupy(p, 1.5f);
                    }
                    break;
                case 3: // stalagmite field
                    for (int i = 0; i < 7; i++)
                    {
                        if (!RoomLayouts.TryPlace(room, rng, 1.2f, 2f, out var p)) continue;
                        float h = Rand(rng, 2.2f, 4.2f), w = Rand(rng, 0.9f, 1.4f);
                        var g = Dungeon.Box(room.Root, "Stalagmite", p + Vector3.up * h * 0.5f, new Vector3(w, h, w), rock);
                        g.transform.rotation = Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f);
                        Dungeon.Box(room.Root, "StalagmiteBase", p + Vector3.up * 0.4f, new Vector3(w * 1.6f, 0.8f, w * 1.6f), rock)
                            .transform.rotation = Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f);
                    }
                    break;
                default: // rock ridges in the four corners
                    foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                    {
                        var p = o + new Vector3(s.x * 6.5f, 0f, s.y * 6.5f);
                        var g = Dungeon.Box(room.Root, "Ridge", p + Vector3.up * 1.1f, new Vector3(5f, 2.2f, 1.4f), rock);
                        g.transform.rotation = Quaternion.Euler(0f, s.x * s.y > 0 ? -45f : 45f, 0f);
                        room.Occupy(p, 2.6f);
                    }
                    break;
            }
        }

        static void Boulder(Room room, Material rock, System.Random rng, Vector3 p, float size, float height)
        {
            var g = Dungeon.Box(room.Root, "Boulder", p + Vector3.up * height * 0.5f, new Vector3(size, height, size * Rand(rng, 0.7f, 1.1f)), rock);
            g.transform.rotation = Quaternion.Euler(Rand(rng, -6f, 6f), Rand(rng, 0f, 90f), Rand(rng, -6f, 6f));
            room.Occupy(p, size * 0.6f + 0.3f);
        }

        static void Crystals(Room room, Dungeon.Palette pal, System.Random rng, Vector3 center, int count)
        {
            room.Occupy(center, 2.2f);
            for (int i = 0; i < count; i++)
            {
                var off = i == 0 ? Vector3.zero : new Vector3(Rand(rng, -1.4f, 1.4f), 0f, Rand(rng, -1.4f, 1.4f));
                float h = i == 0 ? Rand(rng, 3.2f, 4.4f) : Rand(rng, 1.4f, 3f), w = Rand(rng, 0.45f, 0.9f);
                var g = Dungeon.Box(room.Root, "Crystal", center + off + Vector3.up * h * 0.5f, new Vector3(w, h, w), pal.Pillar);
                g.transform.rotation = Quaternion.Euler(Rand(rng, -14f, 14f), Rand(rng, 0f, 90f), Rand(rng, -14f, 14f));
            }
            var glow = Fx.CreateQuad(room.Root, new Color(0.45f, 0.8f, 1f, 0.5f), 4.5f, billboard: false);
            glow.transform.position = Fx.Ground(center) + Vector3.up * 0.04f;
            glow.transform.rotation = Quaternion.Euler(90, 0, 0);
            var halo = Fx.CreateQuad(room.Root, new Color(0.45f, 0.8f, 1f, 0.35f), 3.2f);
            halo.transform.position = center + Vector3.up * 2f;
        }

        // ---------------- floor 3 structures ----------------

        static void ForgeStructures(Room room, Dungeon.Palette pal, System.Random rng, int layout, Vector3 o)
        {
            var iron = Materials.World("anvil", ThemeTextures.Plates("tex_anvil", C(70, 66, 68), C(24, 22, 24), 35), Color.white, 1f);
            switch (layout)
            {
                case 0: // anvils to hide behind
                    for (int i = 0; i < 5; i++)
                        if (RoomLayouts.TryPlace(room, rng, 1.7f, 2.5f, out var p)) Anvil(room, iron, rng, p);
                    break;
                case 1: // iron grates on the diagonals
                {
                    var grate = Materials.World("grate", PixelArt.Bars("tex_grate", C(110, 100, 96)), Color.white, 1.4f);
                    foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                    {
                        var p = o + new Vector3(s.x * 6f, 0f, s.y * 6f);
                        var g = Dungeon.Box(room.Root, "Grate", p + Vector3.up * 1.1f, new Vector3(5.5f, 2.2f, 0.4f), grate);
                        g.transform.rotation = Quaternion.Euler(0f, s.x * s.y > 0 ? -45f : 45f, 0f);
                        room.Occupy(p, 2.8f);
                    }
                    break;
                }
                case 2: // a furnace in the middle
                {
                    Dungeon.Box(room.Root, "Furnace", o + Vector3.up * 2f, new Vector3(4f, 4f, 4f), pal.Pillar);
                    Dungeon.Box(room.Root, "Chimney", o + Vector3.up * 4.5f, new Vector3(2f, 1f, 2f), pal.Wall);
                    room.Occupy(o, 3f);
                    foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    {
                        var g = Fx.CreateQuad(room.Root, new Color(1f, 0.5f, 0.1f, 0.85f), 2.2f, billboard: false);
                        g.transform.position = o + d * 2.02f + Vector3.up * 1.2f;
                        g.transform.rotation = Quaternion.LookRotation(-d);
                    }
                    var halo = Fx.CreateQuad(room.Root, new Color(1f, 0.4f, 0.1f, 0.4f), 7f);
                    halo.transform.position = o + Vector3.up * 1.5f;
                    for (int i = 0; i < 3; i++)
                        if (RoomLayouts.TryPlace(room, rng, 1.7f, 5f, out var p)) Anvil(room, iron, rng, p);
                    break;
                }
                case 3: // lava pits in the corners
                    foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                    {
                        var p = o + new Vector3(s.x * 6.5f, 0f, s.y * 6.5f);
                        if (!room.IsFree(p, 2.4f)) continue;
                        room.Occupy(p, 2.6f);
                        LavaPool.Create(room.Root, p, 2.1f);
                    }
                    break;
                default: // basalt pillars with braziers on the diagonals (the paths to the doors stay open)
                    for (int k = 0; k < 4; k++)
                    {
                        float a = (45f + k * 90f) * Mathf.Deg2Rad;
                        var p = o + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 7f;
                        Pillar(room, pal.Pillar, p, 1.6f, Dungeon.WallH);
                        var fire = Fx.CreateQuad(room.Root, new Color(1f, 0.5f, 0.12f, 0.9f), 1.1f);
                        fire.transform.position = p + Vector3.up * 2.4f + (o - p).normalized * 0.95f;
                    }
                    break;
            }
        }

        static void Anvil(Room room, Material iron, System.Random rng, Vector3 p)
        {
            float yaw = Rand(rng, 0f, 180f);
            Dungeon.Box(room.Root, "AnvilBase", p + Vector3.up * 0.55f, new Vector3(1.6f, 1.1f, 1f), iron).transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Dungeon.Box(room.Root, "AnvilTop", p + Vector3.up * 1.27f, new Vector3(2.6f, 0.35f, 1.2f), iron).transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        static void Pillar(Room room, Material mat, Vector3 p, float size, float height)
        {
            Dungeon.Box(room.Root, "Pillar", p + Vector3.up * height * 0.5f, new Vector3(size, height, size), mat);
            room.Occupy(p, size * 0.5f + 0.4f);
        }

        // ---------------- helpers ----------------

        static float Rand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static Vector3 RandomPoint(Room room, System.Random rng)
        {
            float hx = room.HalfX - 1f, hz = room.HalfZ - 1f;
            return room.Center + new Vector3(Rand(rng, -hx, hx), 0f, Rand(rng, -hz, hz));
        }

        /// <summary>A walkable spot (inside the room, off the door lanes) that isn't occupied; it stays free for walking.</summary>
        static bool FreeSpot(Room room, System.Random rng, float radius, out Vector3 pos)
        {
            for (int t = 0; t < 30; t++)
            {
                pos = RandomPoint(room, rng);
                if (RoomLayouts.InBounds(room, pos) && room.IsFree(pos, radius)) return true;
            }
            pos = default;
            return false;
        }
    }

    /// <summary>Drifting particles around the player: dust in the crypt, snow in the caverns, embers in the forge.</summary>
    public class Ambient : MonoBehaviour
    {
        const int Count = 70;
        const float Range = 11f;
        Transform[] motes;
        Renderer[] rends;
        Vector3[] vel;
        float[] phase;
        int floor = -1;

        public void SetFloor(int f)
        {
            if (motes == null)
            {
                motes = new Transform[Count];
                rends = new Renderer[Count];
                vel = new Vector3[Count];
                phase = new float[Count];
                for (int i = 0; i < Count; i++)
                {
                    rends[i] = Fx.CreateQuad(transform, Color.white, 0.1f);
                    rends[i].name = "Mote";
                    motes[i] = rends[i].transform;
                }
            }
            floor = f;
            var p = Player.Instance ? Player.Instance.transform.position : Vector3.zero;
            for (int i = 0; i < Count; i++) Respawn(i, p, true);
        }

        void Respawn(int i, Vector3 around, bool anywhere)
        {
            float y = anywhere ? Random.Range(0.2f, Dungeon.WallH - 0.2f) : (floor == 2 ? Dungeon.WallH - 0.1f : floor >= 3 ? 0.15f : Random.Range(0.2f, Dungeon.WallH - 0.2f));
            motes[i].position = new Vector3(around.x + Random.Range(-Range, Range), y, around.z + Random.Range(-Range, Range));
            phase[i] = Random.value * 10f;
            Color c;
            float size;
            switch (floor)
            {
                case 2: // snow
                    vel[i] = new Vector3(Random.Range(-0.2f, 0.2f), -Random.Range(0.5f, 1f), Random.Range(-0.2f, 0.2f));
                    c = new Color(0.85f, 0.92f, 1f, Random.Range(0.35f, 0.7f));
                    size = Random.Range(0.06f, 0.12f);
                    break;
                case 3: // embers
                    vel[i] = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.6f, 1.4f), Random.Range(-0.3f, 0.3f));
                    c = new Color(1f, Random.Range(0.35f, 0.6f), 0.1f, Random.Range(0.5f, 0.9f));
                    size = Random.Range(0.05f, 0.1f);
                    break;
                default: // dust
                    vel[i] = new Vector3(Random.Range(-0.12f, 0.12f), Random.Range(-0.05f, 0.05f), Random.Range(-0.12f, 0.12f));
                    c = new Color(0.8f, 0.75f, 0.7f, Random.Range(0.12f, 0.3f));
                    size = Random.Range(0.04f, 0.08f);
                    break;
            }
            motes[i].localScale = Vector3.one * size;
            Materials.SetColor(rends[i], c);
        }

        void Update()
        {
            if (motes == null || !Player.Instance) return;
            float dt = Time.deltaTime;
            Vector3 p = Player.Instance.transform.position;
            for (int i = 0; i < Count; i++)
            {
                var t = motes[i];
                phase[i] += dt;
                Vector3 sway = floor == 2 ? new Vector3(Mathf.Sin(phase[i] * 1.3f), 0f, Mathf.Cos(phase[i])) * 0.25f : Vector3.zero;
                Vector3 pos = t.position + (vel[i] + sway) * dt;
                Vector3 d = pos - p;
                if (Mathf.Abs(d.x) > Range || Mathf.Abs(d.z) > Range || pos.y < 0.05f || pos.y > Dungeon.WallH) { Respawn(i, p, false); continue; }
                t.position = pos;
            }
        }
    }
}
