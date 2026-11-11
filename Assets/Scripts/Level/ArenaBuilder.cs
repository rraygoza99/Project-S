using UnityEngine;

namespace Spellcaster
{
    /// <summary>Builds the single 40x40 enclosed arena with pillars and low cover walls.</summary>
    public static class ArenaBuilder
    {
        public const float HalfSize = 20f;
        public const float WallHeight = 5f;

        public static readonly Vector3[] SpawnPoints =
        {
            new Vector3(-17, 0, -17), new Vector3(17, 0, -17), new Vector3(-17, 0, 17), new Vector3(17, 0, 17),
            new Vector3(0, 0, 18), new Vector3(0, 0, -18), new Vector3(18, 0, 0), new Vector3(-18, 0, 0),
        };

        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        public static void Build(Transform parent)
        {
            var root = new GameObject("Arena").transform;
            root.SetParent(parent, false);

            var wall = Materials.World("wall", PixelArt.Bricks("tex_wall", C(122, 92, 76), C(46, 36, 34), 11), Color.white, 2f);
            var pillar = Materials.World("pillar", PixelArt.Bricks("tex_pillar", C(92, 98, 112), C(34, 34, 42), 23), Color.white, 2f);
            var floor = Materials.World("floor", PixelArt.Tiles("tex_floor", C(84, 78, 70), C(34, 30, 28), 5), Color.white, 2f);
            var ceiling = Materials.World("ceiling", PixelArt.Tiles("tex_ceiling", C(52, 44, 58), C(20, 16, 24), 7), Color.white, 3f);

            float s = HalfSize * 2f, h = WallHeight;
            Box(root, "Floor", new Vector3(0, -0.5f, 0), new Vector3(s + 2, 1, s + 2), floor);
            Box(root, "Ceiling", new Vector3(0, h + 0.5f, 0), new Vector3(s + 2, 1, s + 2), ceiling);
            Box(root, "WallN", new Vector3(0, h / 2, HalfSize + 0.5f), new Vector3(s + 2, h, 1), wall);
            Box(root, "WallS", new Vector3(0, h / 2, -HalfSize - 0.5f), new Vector3(s + 2, h, 1), wall);
            Box(root, "WallE", new Vector3(HalfSize + 0.5f, h / 2, 0), new Vector3(1, h, s + 2), wall);
            Box(root, "WallW", new Vector3(-HalfSize - 0.5f, h / 2, 0), new Vector3(1, h, s + 2), wall);

            foreach (var p in new[] { new Vector2(-8, -8), new Vector2(8, -8), new Vector2(-8, 8), new Vector2(8, 8) })
                Box(root, "Pillar", new Vector3(p.x, h / 2, p.y), new Vector3(2, h, 2), pillar);

            const float coverH = 2.2f;
            Box(root, "Cover", new Vector3(-13, coverH / 2, 0), new Vector3(1, coverH, 7), wall);
            Box(root, "Cover", new Vector3(13, coverH / 2, 0), new Vector3(1, coverH, 7), wall);
            Box(root, "Cover", new Vector3(0, coverH / 2, 13), new Vector3(7, coverH, 1), wall);
            Box(root, "Cover", new Vector3(0, coverH / 2, -13), new Vector3(7, coverH, 1), wall);

            // glowing summoning sigils on the floor at each spawn point
            foreach (var sp in SpawnPoints)
            {
                var r = Fx.CreateQuad(root, new Color(0.55f, 0.15f, 0.7f, 0.6f), 2.6f, billboard: false);
                r.transform.position = sp + Vector3.up * 0.02f;
                r.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
        }

        static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.layer = Layers.World;
            g.isStatic = true;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
