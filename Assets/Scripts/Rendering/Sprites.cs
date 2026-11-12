using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// All character art. Enemy sheets come from Assets/ThirdParty/Resources/Monsters (Pixel Monsters Vol.2,
    /// copied by tools/import-assets.ps1) when present, otherwise from the built-in ASCII pixel art.
    /// </summary>
    public static class Sprites
    {
        static Texture2D LoadSheet(string name)
        {
            var t = Resources.Load<Texture2D>("Monsters/" + name);
            if (t) { t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp; }
            return t;
        }

        /// <summary>Procedural fallback: walk0, walk1, attack packed side by side in one strip.</summary>
        static SpriteSet ProceduralSet(string key, Texture2D walk0, Texture2D walk1, Texture2D attack, float heightUnits, float frameTime)
        {
            var strip = PixelArt.Cached(key + "_strip", () =>
            {
                int w = walk0.width, h = walk0.height;
                var px = new Color32[w * 3 * h];
                var srcs = new[] { walk0, walk1, attack };
                for (int i = 0; i < 3; i++)
                {
                    var src = srcs[i].GetPixels32();
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        px[y * w * 3 + i * w + x] = src[y * w + x];
                }
                return PixelArt.FromPixels(w * 3, h, px);
            });
            int cw = walk0.width, ch = walk0.height;
            var all = SpriteAnim.Sheet(strip, cw, ch, 3, 1f / frameTime);
            return new SpriteSet
            {
                Walk = new SpriteAnim { Texture = strip, Frames = new[] { all.Frames[0], all.Frames[1] }, CellPx = all.CellPx, Fps = 1f / frameTime },
                Attack = new SpriteAnim { Texture = strip, Frames = new[] { all.Frames[2] }, CellPx = all.CellPx, Fps = 1f },
                PixelsPerUnit = ch / heightUnits,
                FeetPx = 0f,
                Flippable = false,
            };
        }
        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        // ---------- Imp ----------
        static readonly Dictionary<char, Color32> ImpPalette = new Dictionary<char, Color32>
        {
            { 'h', C(230, 218, 178) }, { 'd', C(125, 24, 18) }, { 'r', C(196, 52, 30) },
            { 'o', C(240, 120, 52) }, { 'y', C(255, 232, 64) }, { 'w', C(250, 250, 240) }, { 'k', C(24, 8, 8) },
        };

        static readonly string[] ImpHead =
        {
            "..h..........h..",
            "..hh........hh..",
            "...hddddddddh...",
            "....dddddddd....",
            "....dyyddyyd....",
            "....dddddddd....",
            ".....dwkkwd.....",
            "......dddd......",
        };
        static readonly string[] ImpBody =
        {
            "...orrrrrrrro...",
            "..rrrrrrrrrrrr..",
            ".rr.rrrrrrrr.rr.",
            ".r..rrorrorr..r.",
            ".h..rrrrrrrr..h.",
            "....rrrrrrrr....",
        };
        static readonly string[] ImpLegsA = { "....rrr..rrr....", "...rrr....rr....", "..rrr......rr...", "..dd.......dd..." };
        static readonly string[] ImpLegsB = { "....rrr..rrr....", "....rrr..rrr....", ".....rr..rr.....", ".....dd..dd....." };
        static readonly string[] ImpAttack =
        {
            "h.h..........h.h",
            "rr.hh......hh.rr",
            ".r..hddddddh..r.",
            ".rr.dddddddd.rr.",
            "..r.dyyddyyd.r..",
            "..rrddddddddrr..",
            "...rdwkkkkwdr...",
            "....dwkkkkwd....",
            "....orrrrrro....",
            "...rrrrrrrrrr...",
            "...rrrrrrrrrr...",
            "....rrorrorr....",
            "....rrrrrrrr....",
            "....rrrrrrrr....",
            "....rrr..rrr....",
            "...rrr....rrr...",
            "..rrr......rrr..",
            "..dd........dd..",
        };

        static SpriteSet imp;
        /// <summary>Melee imp: Pixel Monsters Vol.2 "monster 8" (96px cells, body 35px tall, feet 34px above cell bottom).</summary>
        public static SpriteSet Imp(float heightUnits) => imp ??= LoadImp(heightUnits) ?? ProceduralSet("imp",
            PixelArt.Cached("imp_walk0", () => PixelArt.FromAscii(Concat(ImpHead, ImpBody, ImpLegsA), ImpPalette)),
            PixelArt.Cached("imp_walk1", () => PixelArt.FromAscii(Concat(ImpHead, ImpBody, ImpLegsB), ImpPalette)),
            PixelArt.Cached("imp_attack", () => PixelArt.FromAscii(ImpAttack, ImpPalette)), heightUnits, 0.16f);

        static SpriteSet LoadImp(float heightUnits)
        {
            var run = LoadSheet("imp_run");
            if (!run) return null;
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(run, 96, 96, 8, 14f),
                Attack = SpriteAnim.Sheet(LoadSheet("imp_attack"), 96, 96, 43, 60f, false),
                Hit = SpriteAnim.Sheet(LoadSheet("imp_hit"), 96, 96, 3, 15f, false),
                Dead = SpriteAnim.Sheet(LoadSheet("imp_dead"), 96, 96, 32, 30f, false),
                PixelsPerUnit = 35f / heightUnits,
                FeetPx = 34f,
                FacesRight = true,
            };
        }

        // ---------- Cultist ----------
        static readonly Dictionary<char, Color32> CultistPalette = new Dictionary<char, Color32>
        {
            { 'p', C(92, 42, 124) }, { 'q', C(56, 22, 82) }, { 'k', C(14, 6, 20) }, { 'g', C(130, 255, 120) },
            { 's', C(206, 176, 152) }, { 'b', C(204, 164, 64) },
        };

        static readonly string[] CultistTop =
        {
            "......pppp......",
            ".....pppppp.....",
            "....ppqqqqpp....",
            "....pqkkkkqp....",
            "....pkgkkgkp....",
            "....pqkkkkqp....",
            ".....pqqqqp.....",
            "....pppppppp....",
            "...pppppppppp...",
        };
        static readonly string[] CultistArms =
        {
            "..ppqppppppqpp..",
            "..pq.pppppp.qp..",
            "..pq.pppppp.qp..",
            "..ss.bbbbbb.ss..",
        };
        static readonly string[] CultistArmsUp =
        {
            ".gg.pppppppp.gg.",
            ".ggsppppppppsgg.",
            "..spp.pppp.pps..",
            ".....bbbbbb.....",
        };
        static readonly string[] CultistRobe =
        {
            ".....pppppp.....",
            ".....pqppqp.....",
            "....ppqppqpp....",
            "....ppqppqpp....",
            "...pppqppqppp...",
            "...pppqppqppp...",
            "..ppppqppqpppp..",
        };
        static readonly string[] CultistFeetA = { "..qqqqqqqqqqqq..", "...kk......kk..." };
        static readonly string[] CultistFeetB = { "...qqqqqqqqqq...", "....kk....kk...." };

        static SpriteSet cultist;
        /// <summary>Ranged caster: Pixel Monsters Vol.2 "monster 16", a front-facing floating eye (175x170 cells, body 93px).</summary>
        public static SpriteSet Cultist(float heightUnits) => cultist ??= LoadWatcher(heightUnits) ?? ProceduralSet("cult",
            PixelArt.Cached("cult_walk0", () => PixelArt.FromAscii(Concat(CultistTop, CultistArms, CultistRobe, CultistFeetA), CultistPalette)),
            PixelArt.Cached("cult_walk1", () => PixelArt.FromAscii(Concat(CultistTop, CultistArms, CultistRobe, CultistFeetB), CultistPalette)),
            PixelArt.Cached("cult_attack", () => PixelArt.FromAscii(Concat(CultistTop, CultistArmsUp, CultistRobe, CultistFeetA), CultistPalette)), heightUnits, 0.3f);

        static SpriteSet LoadWatcher(float heightUnits)
        {
            var idle = LoadSheet("watcher_idle");
            if (!idle) return null;
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(idle, 175, 170, 10, 10f),
                Attack = SpriteAnim.Sheet(LoadSheet("watcher_attack"), 175, 170, 29, 30f, false),
                Hit = SpriteAnim.Sheet(LoadSheet("watcher_hit"), 175, 170, 5, 18f, false),
                Dead = SpriteAnim.Sheet(LoadSheet("watcher_dead"), 175, 170, 19, 20f, false),
                PixelsPerUnit = 93f / heightUnits,
                FeetPx = 26f,
                Flippable = false,
            };
        }

        // ---------- Newer enemies (Pixel Monsters Vol.2; recoloured built-in art as fallback) ----------

        static Dictionary<char, Color32> Recolor(Dictionary<char, Color32> pal, Dictionary<char, Color32> changes)
        {
            var d = new Dictionary<char, Color32>(pal);
            foreach (var kv in changes) d[kv.Key] = kv.Value;
            return d;
        }

        static SpriteSet ImpLike(string key, Dictionary<char, Color32> pal, float heightUnits) => ProceduralSet(key,
            PixelArt.Cached(key + "_walk0", () => PixelArt.FromAscii(Concat(ImpHead, ImpBody, ImpLegsA), pal)),
            PixelArt.Cached(key + "_walk1", () => PixelArt.FromAscii(Concat(ImpHead, ImpBody, ImpLegsB), pal)),
            PixelArt.Cached(key + "_attack", () => PixelArt.FromAscii(ImpAttack, pal)), heightUnits, 0.14f);

        static SpriteSet CultistLike(string key, Dictionary<char, Color32> pal, float heightUnits) => ProceduralSet(key,
            PixelArt.Cached(key + "_walk0", () => PixelArt.FromAscii(Concat(CultistTop, CultistArms, CultistRobe, CultistFeetA), pal)),
            PixelArt.Cached(key + "_walk1", () => PixelArt.FromAscii(Concat(CultistTop, CultistArms, CultistRobe, CultistFeetB), pal)),
            PixelArt.Cached(key + "_attack", () => PixelArt.FromAscii(Concat(CultistTop, CultistArmsUp, CultistRobe, CultistFeetA), pal)), heightUnits, 0.2f);

        /// <summary>Flame Skull: "monster 18", a flaming skull (96px cells, body ~30px).</summary>
        public static SpriteSet Skull(float heightUnits)
        {
            var fly = LoadSheet("skull_fly");
            if (!fly) return ImpLike("skull", Recolor(ImpPalette, new Dictionary<char, Color32> { { 'r', C(250, 140, 40) }, { 'd', C(236, 226, 200) }, { 'o', C(255, 220, 80) } }), heightUnits);
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(fly, 96, 96, 10, 16f),
                Hit = SpriteAnim.Sheet(LoadSheet("skull_hit"), 96, 96, 3, 15f, false),
                Dead = SpriteAnim.Sheet(LoadSheet("skull_dead"), 96, 96, 22, 30f, false),
                PixelsPerUnit = 30f / heightUnits, FeetPx = 30f, FacesRight = false,
            };
        }

        /// <summary>Horned Beetle: "monster 4" (74px cells, body ~33px). Special = the alert pose before a charge.</summary>
        public static SpriteSet Beetle(float heightUnits)
        {
            var walk = LoadSheet("beetle_walk");
            if (!walk) return ImpLike("beetle", Recolor(ImpPalette, new Dictionary<char, Color32> { { 'r', C(40, 70, 110) }, { 'd', C(20, 34, 60) }, { 'o', C(70, 130, 120) }, { 'y', C(255, 120, 40) } }), heightUnits);
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(walk, 74, 74, 8, 10f),
                Run = SpriteAnim.Sheet(LoadSheet("beetle_run"), 74, 74, 8, 18f),
                Special = SpriteAnim.Sheet(LoadSheet("beetle_alert"), 74, 74, 8, 12f),
                Attack = SpriteAnim.Sheet(LoadSheet("beetle_attack"), 74, 74, 10, 20f, false),
                Hit = SpriteAnim.Sheet(LoadSheet("beetle_hit"), 74, 74, 4, 15f, false),
                Dead = SpriteAnim.Sheet(LoadSheet("beetle_dead"), 74, 74, 12, 15f, false),
                PixelsPerUnit = 33f / heightUnits, FeetPx = 21f, FacesRight = false,
            };
        }

        /// <summary>Hornet: "monster 11", a giant wasp (64px cells, body ~35px).</summary>
        public static SpriteSet Hornet(float heightUnits)
        {
            var fly = LoadSheet("hornet_fly");
            if (!fly) return CultistLike("hornet", Recolor(CultistPalette, new Dictionary<char, Color32> { { 'p', C(214, 170, 60) }, { 'q', C(70, 50, 20) }, { 'g', C(255, 60, 40) } }), heightUnits);
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(fly, 64, 64, 4, 18f),
                Attack = SpriteAnim.Sheet(LoadSheet("hornet_attack"), 64, 64, 16, 30f, false),
                Hit = SpriteAnim.Sheet(LoadSheet("hornet_hit"), 64, 64, 4, 15f, false),
                Dead = SpriteAnim.Sheet(LoadSheet("hornet_dead"), 64, 64, 4, 8f, false),
                PixelsPerUnit = 35f / heightUnits, FeetPx = 12f, FacesRight = false,
            };
        }

        /// <summary>Shade: "monster 13", a shadow blob (96px cells, body ~15px tall, 28px wide).</summary>
        public static SpriteSet Shade(float heightUnits)
        {
            var walk = LoadSheet("shade_walk");
            if (!walk) return ImpLike("shade" + heightUnits.ToString("0.0"), Recolor(ImpPalette, new Dictionary<char, Color32> { { 'r', C(40, 30, 60) }, { 'd', C(20, 14, 32) }, { 'o', C(80, 60, 110) }, { 'h', C(60, 50, 90) }, { 'y', C(200, 255, 120) } }), heightUnits);
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(walk, 96, 96, 6, 10f),
                Attack = SpriteAnim.Sheet(LoadSheet("shade_attack"), 96, 96, 6, 12f, false),
                Hit = SpriteAnim.Sheet(LoadSheet("shade_hit"), 96, 96, 3, 15f, false),
                Dead = SpriteAnim.Sheet(LoadSheet("shade_dead"), 96, 96, 13, 20f, false),
                PixelsPerUnit = 15f / heightUnits, FeetPx = 25f, FacesRight = false,
            };
        }

        // ---------- Wizard hands (first-person "weapon" sprite) ----------
        public const int HandsW = 64, HandsH = 48;
        /// <summary>Pixel position (from bottom-left) of the staff orb / casting focus, used for glow.</summary>
        public static readonly Vector2 IdleFocus = new Vector2(52, 42), CastFocus = new Vector2(32, 31);

        static readonly Color32 Wood = C(118, 74, 38), WoodDark = C(72, 44, 22), Orb = C(186, 228, 255), OrbHi = C(255, 255, 255);
        static readonly Color32 Skin = C(226, 182, 142), SkinDark = C(172, 122, 92), Robe = C(84, 42, 134), RobeDark = C(52, 24, 88);

        public static Texture2D HandsIdle => PixelArt.Cached("hands_idle", () =>
        {
            int w = HandsW, h = HandsH;
            var px = new Color32[w * h];
            // staff
            for (int y = 0; y < 40; y++)
            {
                int x = 44 + Mathf.RoundToInt(y * 0.2f);
                PixelArt.Set(px, w, h, x, y, WoodDark);
                PixelArt.Set(px, w, h, x + 1, y, Wood);
                PixelArt.Set(px, w, h, x + 2, y, Wood);
            }
            // prongs + orb
            PixelArt.Rect(px, w, h, 48, 37, 49, 40, WoodDark);
            PixelArt.Rect(px, w, h, 55, 37, 56, 40, WoodDark);
            PixelArt.Ellipse(px, w, h, 52, 42, 4, 4, Orb);
            PixelArt.Rect(px, w, h, 50, 43, 51, 44, OrbHi);
            // sleeve
            PixelArt.ThickLine(px, w, h, new Vector2(50, -4), new Vector2(46, 6), 7, Robe);
            PixelArt.ThickLine(px, w, h, new Vector2(43, -4), new Vector2(40, 4), 2, RobeDark);
            // gripping hand
            PixelArt.Ellipse(px, w, h, 47, 15, 6, 5, Skin);
            PixelArt.Ellipse(px, w, h, 47, 13, 5, 3, SkinDark);
            for (int i = 0; i < 4; i++) PixelArt.Set(px, w, h, 43 + i * 3, 17, SkinDark);
            PixelArt.Outline(px, w, h);
            return PixelArt.FromPixels(w, h, px);
        });

        public static Texture2D HandsCast => PixelArt.Cached("hands_cast", () =>
        {
            int w = HandsW, h = HandsH;
            var px = new Color32[w * h];
            DrawCastingHand(px, w, h, mirror: false);
            DrawCastingHand(px, w, h, mirror: true);
            PixelArt.Outline(px, w, h);
            return PixelArt.FromPixels(w, h, px);
        });

        static void DrawCastingHand(Color32[] px, int w, int h, bool mirror)
        {
            int M(int x) => mirror ? w - 1 - x : x;
            Vector2 V(float x, float y) => new Vector2(mirror ? w - 1 - x : x, y);
            PixelArt.ThickLine(px, w, h, V(6, -6), V(17, 14), 7, Robe);
            PixelArt.ThickLine(px, w, h, V(2, -6), V(10, 8), 2, RobeDark);
            PixelArt.Ellipse(px, w, h, M(21), 22, 6, 7, Skin);
            PixelArt.Ellipse(px, w, h, M(19), 20, 4, 5, SkinDark);
            for (int i = 0; i < 4; i++)
                PixelArt.Rect(px, w, h, Mathf.Min(M(16 + i * 3), M(17 + i * 3)), 28, Mathf.Max(M(16 + i * 3), M(17 + i * 3)), 32 - (i == 0 ? 2 : 0), Skin);
            // thumb pointing toward the centre
            PixelArt.Ellipse(px, w, h, M(27), 22, 2, 3, Skin);
        }

        static string[] Concat(params string[][] parts)
        {
            var list = new List<string>();
            foreach (var p in parts) list.AddRange(p);
            return list.ToArray();
        }
    }
}
