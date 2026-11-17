using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// How another player looks on this PC: an 8-direction billboard sprite in their colour (it shows which
    /// way they face), walking and casting frames, and a glow in their colour while they cast. The sprite
    /// follows the player smoothly, so bursts of network updates don't make it jump.
    /// </summary>
    public class PlayerAvatar : MonoBehaviour
    {
        Player player;
        Transform root, quad, glow;
        Renderer rend, glowRend;
        MaterialPropertyBlock mpb, glowMpb;
        Vector3 shown, lastShown;
        float animTime, speed;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        /// <summary>The view drawn last (0 front .. 4 back) and whether it was mirrored (tests).</summary>
        public int View { get; private set; }
        public bool Mirrored { get; private set; }
        public int FrameShown { get; private set; }
        public Vector3 ShownPosition => shown;

        void Start()
        {
            player = GetComponent<Player>();
            root = new GameObject("PlayerSprite").transform;
            shown = lastShown = transform.position;
            root.position = shown;

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(root, false);
            float h = PlayerSprites.CellH / PlayerSprites.PixelsPerUnit, w = PlayerSprites.CellW / PlayerSprites.PixelsPerUnit;
            q.transform.localScale = new Vector3(w, h, 1f);
            q.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            q.AddComponent<Billboard>().Cylindrical = true;
            rend = q.GetComponent<Renderer>();
            rend.sharedMaterial = Materials.Sprite;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad = q.transform;
            mpb = new MaterialPropertyBlock();

            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root, false);
            g.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            g.transform.localScale = Vector3.one * 1.4f;
            g.AddComponent<Billboard>().Cylindrical = false;
            glowRend = g.GetComponent<Renderer>();
            glowRend.sharedMaterial = Materials.Fx;
            glowRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow = g.transform;
            glowMpb = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            if (root) Destroy(root.gameObject);
        }

        // Host: the client's copy jumps forward a batch at a time; remember where it was when, and draw it a moment behind.
        readonly List<(double t, Vector3 pos)> trail = new List<(double, Vector3)>();
        const double TrailDelay = 0.1;

        Vector3 SimulatedPosition()
        {
            double now = Time.unscaledTimeAsDouble;
            Vector3 cur = transform.position;
            if (trail.Count == 0 || (trail[trail.Count - 1].pos - cur).sqrMagnitude > 1e-6f) trail.Add((now, cur));
            if (trail.Count > 60) trail.RemoveRange(0, trail.Count - 60);
            double t = now - TrailDelay;
            int b = trail.FindIndex(s => s.t >= t);
            if (b <= 0) return trail[b < 0 ? trail.Count - 1 : 0].pos;
            var s0 = trail[b - 1]; var s1 = trail[b];
            float k = s1.t > s0.t ? Mathf.Clamp01((float)((t - s0.t) / (s1.t - s0.t))) : 1f;
            if (b > 2) trail.RemoveRange(0, b - 2);
            return Vector3.Lerp(s0.pos, s1.pos, k);
        }

        void LateUpdate()
        {
            if (!player || !root) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            // A client's copy on the host: played back smoothly from its recent positions.
            // On a client, other players already move smoothly (snapshot blending): follow them exactly.
            Vector3 target = player.Mode == Player.Control.Simulated ? SimulatedPosition() : transform.position;
            if ((target - shown).sqrMagnitude > 9f) trail.Clear(); // teleport
            shown = target;
            root.position = shown;
            Vector3 moved = shown - lastShown;
            moved.y = 0f;
            speed = Mathf.Lerp(speed, moved.magnitude / dt, 1f - Mathf.Exp(-10f * dt));
            lastShown = shown;

            // Which side of them we see: compare their facing with the direction to our camera.
            var cam = Player.Instance ? Player.Instance.Cam.transform : null;
            int view = 0; bool mirror = false;
            if (cam)
            {
                Vector3 toCam = cam.position - shown;
                toCam.y = 0f;
                float camYaw = Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg;
                float rel = Mathf.DeltaAngle(player.Yaw, camYaw);
                int i = Mathf.RoundToInt(rel / 45f); // -4..4: + = we're on their right
                view = Mathf.Min(4, Mathf.Abs(i));
                mirror = i < 0;
            }

            int frame = PlayerSprites.Idle;
            if (player.NetCasting) frame = PlayerSprites.Cast;
            else if (speed > 0.6f)
            {
                animTime += dt * Mathf.Clamp(speed / 3f, 0.8f, 2.2f);
                frame = Mathf.Repeat(animTime * 3f, 1f) < 0.5f ? PlayerSprites.WalkA : PlayerSprites.WalkB;
            }
            View = view; Mirrored = mirror; FrameShown = frame;

            bool dead = player.IsDead;
            mpb.SetTexture(MainTexId, PlayerSprites.Atlas(player.ColorIndex));
            mpb.SetVector(MainTexStId, PlayerSprites.Frame(view, frame, mirror));
            mpb.SetColor(ColorId, dead ? new Color(0.45f, 0.45f, 0.5f, 0.75f) : Color.white);
            mpb.SetFloat(FlashId, player.HurtFlash > 0.5f ? 1f : 0f);
            rend.SetPropertyBlock(mpb);
            float h = PlayerSprites.CellH / PlayerSprites.PixelsPerUnit;
            quad.localPosition = new Vector3(0f, dead ? 0.3f : h * 0.5f, 0f); // fallen players sink (downed state is Phase 5)

            bool casting = player.NetCasting && !dead;
            glow.gameObject.SetActive(casting);
            if (casting)
            {
                var c = player.Color;
                c.a = 0.55f + 0.25f * Mathf.Sin(Time.time * 9f);
                glowMpb.SetColor(ColorId, c);
                glowRend.SetPropertyBlock(glowMpb);
            }
        }
    }
}
