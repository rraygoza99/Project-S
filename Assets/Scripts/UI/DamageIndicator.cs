using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>When the hit-direction arcs show: never, for hits from outside your view, or for every hit.</summary>
    public enum HitDirectionMode { Off, OutsideView, All }

    /// <summary>
    /// Damage direction: a thin red arc on a ring around the crosshair, pointing at where a hit came from
    /// (the attacker for melee, the shot's origin for ranged). It keeps pointing at that spot as you turn
    /// and fades out over <see cref="Lifetime"/>. Each attacker has one arc (another hit refreshes it);
    /// different attackers get their own. Traps and blasts with no clear source show nothing. Subtle on
    /// purpose: no full-screen red. Each player sees their own (in co-op the host sends the source).
    /// </summary>
    public static class DamageIndicator
    {
        /// <summary>Seconds an arc lasts (it fades over the last part).</summary>
        public static float Lifetime = 1f;
        /// <summary>A hit counts as "in view" inside this share of the camera's half field of view.</summary>
        public static float InViewShare = 0.85f;
        /// <summary>Ring radius around the crosshair (share of the screen height) and the strongest arc's alpha.</summary>
        public static float RadiusShare = 0.13f, MaxAlpha = 0.8f;
        static readonly Color ArcColor = new Color(0.95f, 0.12f, 0.08f);

        class Arc { public int key; public Vector3 from; public float born, strength; }
        static readonly List<Arc> arcs = new List<Arc>();

        /// <summary>Arcs showing now, and arcs added since the game started (tests).</summary>
        public static int Count { get { Expire(); return arcs.Count; } }
        public static int Added { get; private set; }

        /// <summary>
        /// The local player was hit from this spot. key: the attacker (another hit from it refreshes its arc;
        /// 0 = always a new arc). amount: damage taken (a bigger hit draws a stronger arc).
        /// </summary>
        public static void Hit(Player me, Vector3 from, int key, float amount)
        {
            if (!me || !me.IsLocal) return;
            var mode = Settings.HitDirection;
            if (mode == HitDirectionMode.Off) return;
            if (mode == HitDirectionMode.OutsideView && InView(me, from)) return;
            Expire();
            float s = Mathf.Clamp01(amount / Mathf.Max(1f, me.MaxHP * 0.25f));
            if (key != 0)
                foreach (var a in arcs)
                    if (a.key == key) { a.from = from; a.born = Time.time; a.strength = Mathf.Max(a.strength, s); return; }
            arcs.Add(new Arc { key = key, from = from, born = Time.time, strength = s });
            Added++;
        }

        /// <summary>The spot is inside the camera's view (left-right), so you can see who hit you.</summary>
        public static bool InView(Player me, Vector3 from)
        {
            var cam = me.Cam;
            float half = cam ? Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect) * 0.5f : 45f;
            return Mathf.Abs(HUD.AngleTo(me, from)) < half * InViewShare;
        }

        public static void Clear() => arcs.Clear();

        static void Expire()
        {
            float now = Time.time;
            arcs.RemoveAll(a => now - a.born >= Lifetime || now < a.born); // now < born: the clock restarted
        }

        /// <summary>Test hook: the angle (degrees, 0 ahead, 90 right) of the arc for this attacker, or NaN.</summary>
        public static float AngleForTest(Player me, int key)
        {
            Expire();
            foreach (var a in arcs) if (a.key == key) return HUD.AngleTo(me, a.from);
            return float.NaN;
        }

        public static void Draw(float W, float H, Player me)
        {
            Expire();
            if (arcs.Count == 0 || !me) return;
            float R = H * RadiusShare;
            var rect = new Rect(W / 2f - R, H / 2f - R, 2f * R, 2f * R);
            foreach (var a in arcs)
            {
                float k = 1f - (Time.time - a.born) / Lifetime;           // 1 -> 0
                float alpha = Mathf.Clamp01(k * 1.6f) * MaxAlpha * (0.45f + 0.55f * a.strength);
                HUD.DrawRotated(ArcTexture, rect, HUD.AngleTo(me, a.from), new Color(ArcColor.r, ArcColor.g, ArcColor.b, alpha));
            }
        }

        static Texture2D arcTex;
        /// <summary>A soft arc at the top of a ring (about 50 degrees wide), white; tinted and rotated when drawn.</summary>
        static Texture2D ArcTexture
        {
            get
            {
                if (arcTex) return arcTex;
                const int n = 192;
                arcTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f; // rows go bottom-up: +dy is the top
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Abs(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);
                    float band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.80f, 0.85f, r)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.90f, 0.96f, r)));
                    float sweep = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, 26f, ang));
                    float a = band * sweep * (0.65f + 0.35f * (1f - Mathf.Clamp01(ang / 26f)));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
                arcTex.SetPixels32(px);
                arcTex.Apply();
                return arcTex;
            }
        }
    }
}
