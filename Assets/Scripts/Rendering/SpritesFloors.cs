using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Art for the floor 2 and 3 enemies and bosses (Pixel Monsters Vol.2, copied by tools/import-assets.ps1),
    /// each with a recoloured built-in fallback so the game runs without the pack.
    /// </summary>
    public static partial class Sprites
    {
        static SpriteAnim A(string sheet, int cw, int ch, int count, float fps, bool loop = true) =>
            SpriteAnim.Sheet(LoadSheet(sheet), cw, ch, count, fps, loop);

        static Dictionary<char, Color32> ImpTint(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2, byte er = 255, byte eg = 232, byte eb = 64) =>
            Recolor(ImpPalette, new Dictionary<char, Color32> { { 'r', C(r1, g1, b1) }, { 'd', C(r2, g2, b2) }, { 'o', C(r1, g1, b1) }, { 'h', C(r2, g2, b2) }, { 'y', C(er, eg, eb) } });

        static Dictionary<char, Color32> CultTint(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2) =>
            Recolor(CultistPalette, new Dictionary<char, Color32> { { 'p', C(r1, g1, b1) }, { 'q', C(r2, g2, b2) } });

        /// <summary>Frost Slug (floor 2): "monster 1", a spiky blue slug (100px cells, body 16px, feet 41px up).</summary>
        public static SpriteSet Slug(float h)
        {
            var walk = LoadSheet("slug_walk");
            if (!walk) return ImpLike("slug", ImpTint(90, 150, 200, 40, 70, 110), h);
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(walk, 100, 100, 7, 8f),
                Hit = A("slug_hit", 100, 100, 3, 15f, false),
                Dead = A("slug_dead", 100, 100, 12, 20f, false),
                PixelsPerUnit = 16f / h, FeetPx = 41f, FacesRight = false,
            };
        }

        /// <summary>Gloom Moth (floor 2): "monster 2", a flying moth that spits (100px cells, body 38px).</summary>
        public static SpriteSet Moth(float h)
        {
            var fly = LoadSheet("moth_fly");
            if (!fly) return CultistLike("moth", CultTint(170, 120, 220, 70, 40, 110), h);
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(fly, 100, 100, 4, 14f),
                Attack = A("moth_attack", 100, 100, 16, 30f, false),
                Hit = A("moth_hit", 100, 100, 4, 15f, false),
                Dead = A("moth_dead", 100, 100, 12, 20f, false),
                PixelsPerUnit = 38f / h, FeetPx = 34f, FacesRight = false,
            };
        }

        /// <summary>Cave Frog (floor 2): "monster 12" (160x96 cells, body 22px). Special = crouch before a leap.</summary>
        public static SpriteSet Frog(float h)
        {
            var idle = LoadSheet("frog_idle");
            if (!idle) return ImpLike("frog", ImpTint(60, 150, 70, 30, 80, 40), h);
            var run = A("frog_run", 160, 96, 6, 14f);
            return new SpriteSet
            {
                Walk = run,
                Run = run,
                Special = SpriteAnim.Sheet(idle, 160, 96, 6, 12f),
                Attack = A("frog_attack", 160, 96, 6, 14f, false),
                Hit = A("frog_hit", 160, 96, 3, 15f, false),
                Dead = A("frog_dead", 160, 96, 6, 12f, false),
                PixelsPerUnit = 22f / h, FeetPx = 32f, FacesRight = false,
            };
        }

        /// <summary>Cinder Crawler (floor 3): "monster 14", a slug with burning spikes (100px cells, body 19px).</summary>
        public static SpriteSet Crawler(float h)
        {
            var walk = LoadSheet("crawler_walk");
            if (!walk) return ImpLike("crawler", ImpTint(160, 70, 30, 70, 30, 20, 255, 160, 40), h);
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(walk, 100, 100, 8, 10f),
                Attack = A("crawler_attack", 100, 100, 10, 18f, false),
                Hit = A("crawler_hit", 100, 100, 3, 15f, false),
                Dead = A("crawler_dead", 100, 100, 12, 20f, false),
                PixelsPerUnit = 19f / h, FeetPx = 41f, FacesRight = false,
            };
        }

        /// <summary>Forge Sentry (floor 3): "monster 17", a little gunner robot (128px cells, body 31px).</summary>
        public static SpriteSet Robot(float h)
        {
            var walk = LoadSheet("robot_walk");
            if (!walk) return CultistLike("robot", CultTint(130, 140, 150, 60, 66, 76), h);
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(walk, 128, 128, 6, 10f),
                Attack = A("robot_attack", 128, 128, 12, 24f, false),
                Hit = A("robot_hit", 128, 128, 4, 15f, false),
                Dead = A("robot_dead", 128, 128, 14, 20f, false),
                PixelsPerUnit = 31f / h, FeetPx = 48f, FacesRight = false,
            };
        }

        /// <summary>Mimic (floor 3): "monster 19", a treasure chest with teeth (96px cells, body 33px). Special = waking up.</summary>
        public static SpriteSet Mimic(float h)
        {
            var idle = LoadSheet("mimic_idle");
            if (!idle) return ImpLike("mimic", ImpTint(140, 96, 52, 70, 46, 24), h);
            return new SpriteSet
            {
                Walk = A("mimic_run", 96, 96, 8, 14f),
                Idle = SpriteAnim.Sheet(idle, 96, 96, 6, 8f),
                Special = A("mimic_appear", 96, 96, 8, 12f, false),
                Attack = A("mimic_attack", 96, 96, 8, 16f, false),
                Hit = A("mimic_hit", 96, 96, 2, 15f, false),
                Dead = A("mimic_dead", 96, 96, 13, 20f, false),
                PixelsPerUnit = 33f / h, FeetPx = 32f, FacesRight = true,
            };
        }

        /// <summary>The Burrower (floor 2 boss): "monster 20", a giant worm (96px cells, body 29px).</summary>
        public static SpriteSet Burrower(float h)
        {
            var idle = LoadSheet("burrower_idle");
            if (!idle) return ImpLike("burrower", ImpTint(120, 60, 170, 60, 30, 90), h);
            return new SpriteSet
            {
                Walk = SpriteAnim.Sheet(idle, 96, 96, 8, 10f),
                Idle = SpriteAnim.Sheet(idle, 96, 96, 8, 10f),
                Special = A("burrower_appear", 96, 96, 8, 14f, false),
                Run = A("burrower_disappear", 96, 96, 10, 14f, false),
                Attack = A("burrower_attack", 96, 96, 9, 16f, false),
                Hit = A("burrower_hit", 96, 96, 3, 15f, false),
                Dead = A("burrower_dead", 96, 96, 8, 10f, false),
                PixelsPerUnit = 29f / h, FeetPx = 31f, FacesRight = true,
            };
        }

        /// <summary>The Behemoth (floor 3 boss): "monster 7", a huge spiked beast (180x132 cells, body 90px).</summary>
        public static SpriteSet Behemoth(float h)
        {
            var idle = LoadSheet("behemoth_idle");
            if (!idle) return ImpLike("behemoth", ImpTint(40, 90, 80, 20, 50, 44, 255, 90, 30), h);
            var set = new SpriteSet
            {
                Walk = A("behemoth_walk", 180, 132, 20, 14f),
                Idle = SpriteAnim.Sheet(idle, 180, 132, 32, 14f),
                Hit = A("behemoth_hit", 180, 132, 8, 16f, false),
                Dead = A("behemoth_dead", 180, 132, 35, 18f, false),
                PixelsPerUnit = 90f / h, FeetPx = 17f, FacesRight = false,
            };
            set.Extra["roar_wind"] = A("behemoth_roar_wind", 180, 132, 11, 14f, false);
            set.Extra["roar"] = A("behemoth_roar", 180, 132, 4, 10f);
            set.Extra["roll_wind"] = A("behemoth_roll_wind", 180, 132, 31, 30f, false);
            set.Extra["roll"] = A("behemoth_roll", 180, 132, 4, 16f);
            set.Extra["roll_end"] = A("behemoth_roll_end", 180, 132, 11, 16f, false);
            set.Extra["spike_wind"] = A("behemoth_spike_wind", 180, 132, 9, 14f, false);
            set.Extra["spike"] = A("behemoth_spike", 180, 132, 4, 12f, false);
            set.Extra["spike_end"] = A("behemoth_spike_end", 180, 132, 10, 16f, false);
            set.Extra["takeoff"] = A("behemoth_takeoff", 180, 132, 18, 24f, false);
            set.Extra["fall"] = A("behemoth_fall", 180, 132, 1, 1f);
            set.Extra["landing"] = A("behemoth_landing", 180, 132, 7, 14f, false);
            set.Extra["tired"] = A("behemoth_tired", 180, 132, 10, 8f);
            return set;
        }

        /// <summary>Ceiling pod (floor 2 hazard): "monster 6" tentacle strip (93x153 cells) and pod (100px cells).</summary>
        public static SpriteAnim PodTentacle => podTentacle ??= A("pod_tentacle", 93, 153, 6, 12f, false);
        public static SpriteAnim PodBody => podBody ??= A("pod_attack", 100, 100, 11, 12f);
        static SpriteAnim podTentacle, podBody;
    }
}
