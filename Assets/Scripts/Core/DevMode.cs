using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Dev mode (F3, or Options > Dev mode): wireframe hitboxes drawn over the game view, through walls.
    /// Live shapes: enemy colliders, melee reach, projectiles. Brief shapes recorded by the hit code:
    /// spell and enemy blast areas, nova and cloud rings, instant rays and chain jumps.
    /// </summary>
    public static class DevMode
    {
        public const KeyCode ToggleKey = KeyCode.F3;
        public static bool On => Settings.DevMode;

        public static readonly Color EnemyBody = new Color(0.3f, 1f, 0.4f);
        public static readonly Color EnemyReach = new Color(1f, 0.3f, 0.25f);
        public static readonly Color EnemyArea = new Color(1f, 0.15f, 0.15f);
        public static readonly Color EnemyShot = new Color(1f, 0.3f, 0.95f);
        public static readonly Color PlayerShot = new Color(1f, 0.92f, 0.2f);
        public static readonly Color SpellArea = new Color(1f, 0.6f, 0.15f);
        public static readonly Color SpellRay = new Color(0.4f, 0.85f, 1f);

        enum Kind { Sphere, Ring, Line }
        struct Shape { public Kind Kind; public Vector3 A, B; public float R; public Color Color; public float Life, Duration; }
        static readonly List<Shape> shapes = new List<Shape>();

        /// <summary>Brief shapes recorded since start (tests).</summary>
        public static int Recorded { get; private set; }

        /// <summary>A blast or search area: a sphere.</summary>
        public static void Sphere(Vector3 center, float radius, Color color, float duration = 0.6f) => Add(Kind.Sphere, center, center, radius, color, duration);
        /// <summary>A flat area on the floor (novas, clouds, melee swings): a horizontal ring.</summary>
        public static void Ring(Vector3 center, float radius, Color color, float duration = 0.6f) => Add(Kind.Ring, center, center, radius, color, duration);
        /// <summary>An instant ray or chain jump.</summary>
        public static void Line(Vector3 a, Vector3 b, Color color, float duration = 0.6f) => Add(Kind.Line, a, b, 0f, color, duration);

        static void Add(Kind kind, Vector3 a, Vector3 b, float r, Color color, float duration)
        {
            if (!On) return;
            Recorded++;
            shapes.Add(new Shape { Kind = kind, A = a, B = b, R = r, Color = color, Life = duration, Duration = duration });
        }

        public static void Toggle() => Settings.SetDevMode(!Settings.DevMode);

        public static void Tick(float dt)
        {
            if (!On) { shapes.Clear(); return; }
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
                var s = shapes[i];
                s.Life -= dt;
                if (s.Life <= 0f) shapes.RemoveAt(i);
                else shapes[i] = s;
            }
        }

        // ---------------- drawing ----------------

        static Material mat;
        static Camera cam;
        static Matrix4x4 view, proj;
        static float sw, sh;
        static GUIStyle tag, legend;

        /// <summary>Called by the HUD on Repaint, after the world image.</summary>
        public static void DrawOverlay(float W, float H, float u)
        {
            if (!On || Event.current.type != EventType.Repaint) return;
            var player = Player.Instance;
            if (!player || !player.Cam) return;
            if (!mat)
            {
                var shader = Resources.Load<Shader>("Shaders/DevLine");
                if (!shader) return;
                mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            cam = player.Cam;
            view = cam.worldToCameraMatrix;
            proj = cam.projectionMatrix;
            sw = W;
            sh = H;

            mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, W, H, 0);
            GL.Begin(GL.LINES);

            foreach (var e in Enemy.All)
            {
                if (!e || !e.IsAlive) continue;
                var t = e.Type;
                Vector3 feet = e.transform.position;
                Capsule(feet + Vector3.up * t.Hover, t.Radius, Mathf.Max(t.Height, t.Radius * 2f), EnemyBody);
                if (t.Hover > 0f) Circle(feet + Vector3.up * 0.05f, t.Radius, Vector3.right, Vector3.forward, Fade(EnemyBody, 0.35f)); // movement collider footprint
                if (e.IsCharging)
                    Circle(feet + Vector3.up * 0.1f, t.Radius + 1f, Vector3.right, Vector3.forward, EnemyReach); // ram contact
                else if (!t.Ranged)
                    Circle(feet + Vector3.up * 0.1f, t.AttackRange + 0.5f, Vector3.right, Vector3.forward, Fade(EnemyReach, 0.45f)); // melee reach: your feet inside = hit
            }

            foreach (var p in Projectile.All)
            {
                if (!p) continue;
                var c = (p.Mask & Layers.PlayerMask) != 0 ? EnemyShot : PlayerShot;
                Ball(p.transform.position, Mathf.Max(p.Radius, 0.05f), c);
                Seg(p.transform.position, p.transform.position + p.Velocity * 0.08f, Fade(c, 0.6f));
            }

            foreach (var s in shapes)
            {
                var c = Fade(s.Color, Mathf.Clamp01(0.3f + s.Life / s.Duration));
                switch (s.Kind)
                {
                    case Kind.Sphere: Ball(s.A, s.R, c); break;
                    case Kind.Ring: Circle(s.A, s.R, Vector3.right, Vector3.forward, c); break;
                    case Kind.Line: Seg(s.A, s.B, c); Ball(s.B, 0.15f, c); break;
                }
            }

            GL.End();
            GL.PopMatrix();

            // Enemy name + HP tags, and a legend.
            int tagSize = Mathf.RoundToInt(u * 0.9f);
            if (tag == null || tag.fontSize != tagSize)
            {
                tag = new GUIStyle(HUD.Small) { alignment = TextAnchor.LowerCenter, fontSize = tagSize };
                legend = new GUIStyle(HUD.Small) { fontSize = tagSize };
            }
            foreach (var e in Enemy.All)
            {
                if (!e || !e.IsAlive) continue;
                if (!Project(e.transform.position + Vector3.up * (e.Type.Height + 0.25f), out var sp)) continue;
                HUD.Shadowed(new Rect(sp.x - 6 * u, sp.y - 1.6f * u, 12 * u, 1.6f * u), $"{e.Type.Name} {e.HP:0}/{e.MaxHP:0}", tag);
            }
            float x = u * 1.2f, y = H * 0.36f, lh = u * 1.25f;
            HUD.Shadowed(new Rect(x, y, 30 * u, lh), $"DEV MODE ({ToggleKey})", legend);
            Key(ref y, x, lh, u, EnemyBody, "enemy hitbox");
            Key(ref y, x, lh, u, EnemyReach, "melee reach / ram");
            Key(ref y, x, lh, u, PlayerShot, "your shots");
            Key(ref y, x, lh, u, EnemyShot, "enemy shots");
            Key(ref y, x, lh, u, SpellArea, "spell area");
            Key(ref y, x, lh, u, SpellRay, "ray / chain");
            Key(ref y, x, lh, u, EnemyArea, "enemy blast / swing");
        }

        static void Key(ref float y, float x, float lh, float u, Color c, string text)
        {
            y += lh;
            HUD.Fill(new Rect(x, y + lh * 0.3f, u * 0.8f, lh * 0.4f), c);
            HUD.Shadowed(new Rect(x + u * 1.2f, y, 30 * u, lh), text, legend);
        }

        static Color Fade(Color c, float a) { c.a *= a; return c; }

        static void Capsule(Vector3 feet, float r, float h, Color c)
        {
            r = Mathf.Min(r, h * 0.5f);
            Vector3 lo = feet + Vector3.up * r, hi = feet + Vector3.up * (h - r);
            Circle(lo, r, Vector3.right, Vector3.forward, Fade(c, 0.6f));
            Circle(hi, r, Vector3.right, Vector3.forward, Fade(c, 0.6f));
            // Outline as seen from the camera: two sides and the two end caps.
            Vector3 side = cam.transform.right;
            side.y = 0f;
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            Seg(lo + side * r, hi + side * r, c);
            Seg(lo - side * r, hi - side * r, c);
            Arc(hi, r, side, Vector3.up, 0f, 180f, c);
            Arc(lo, r, side, Vector3.up, 180f, 360f, c);
        }

        static void Ball(Vector3 center, float r, Color c)
        {
            Circle(center, r, cam.transform.right, cam.transform.up, c);
            Circle(center, r, Vector3.right, Vector3.forward, Fade(c, 0.6f));
        }

        static void Circle(Vector3 center, float r, Vector3 ax, Vector3 ay, Color c) => Arc(center, r, ax, ay, 0f, 360f, c);

        static void Arc(Vector3 center, float r, Vector3 ax, Vector3 ay, float fromDeg, float toDeg, Color c)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt((toDeg - fromDeg) / 12f), 3, 40);
            Vector3 prev = default;
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)n) * Mathf.Deg2Rad;
                Vector3 p = center + (ax * Mathf.Cos(a) + ay * Mathf.Sin(a)) * r;
                if (i > 0) Seg(prev, p, c);
                prev = p;
            }
        }

        /// <summary>One world-space segment, clipped at the camera's near plane, drawn 2 px thick.</summary>
        static void Seg(Vector3 a, Vector3 b, Color c)
        {
            Vector3 va = view.MultiplyPoint3x4(a), vb = view.MultiplyPoint3x4(b);
            float near = -cam.nearClipPlane;
            if (va.z > near && vb.z > near) return;
            if (va.z > near) va = Vector3.Lerp(vb, va, (near - vb.z) / (va.z - vb.z));
            else if (vb.z > near) vb = Vector3.Lerp(va, vb, (near - va.z) / (vb.z - va.z));
            Vector2 sa = ViewToScreen(va), sb = ViewToScreen(vb);
            GL.Color(c);
            GL.Vertex3(sa.x, sa.y, 0f); GL.Vertex3(sb.x, sb.y, 0f);
            GL.Vertex3(sa.x + 1f, sa.y, 0f); GL.Vertex3(sb.x + 1f, sb.y, 0f);
            GL.Vertex3(sa.x, sa.y + 1f, 0f); GL.Vertex3(sb.x, sb.y + 1f, 0f);
        }

        static Vector2 ViewToScreen(Vector3 v)
        {
            Vector4 clip = proj * new Vector4(v.x, v.y, v.z, 1f);
            float w = Mathf.Abs(clip.w) < 1e-5f ? 1e-5f : clip.w;
            return new Vector2((clip.x / w * 0.5f + 0.5f) * sw, (0.5f - clip.y / w * 0.5f) * sh);
        }

        /// <summary>World point to GUI pixels (top-left origin); false if behind the camera.</summary>
        public static bool Project(Vector3 world, out Vector2 screen)
        {
            screen = default;
            if (!cam) return false;
            Vector3 v = view.MultiplyPoint3x4(world);
            if (v.z > -cam.nearClipPlane) return false;
            screen = ViewToScreen(v);
            return true;
        }
    }
}
