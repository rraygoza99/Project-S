using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// The DOOM-style first-person hands/staff sprite: idle bob, casting pose, glow, fire kick.
    /// Drawn by the HUD on top of the upscaled world image.
    /// </summary>
    public class WizardHands : MonoBehaviour
    {
        public static WizardHands Instance { get; private set; }

        /// <summary>Raised casting pose (set by SpellSystem while channeling).</summary>
        public bool Casting;
        /// <summary>Glow colour on the staff orb / between the hands (alpha 0 = none).</summary>
        public Color Glow = new Color(0.6f, 0.85f, 1f, 0.35f);

        Player player;
        float bobPhase, moveAmount, raise, kick, time;

        void Awake()
        {
            Instance = this;
            player = GetComponent<Player>();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            time += dt;
            float speed01 = Mathf.Clamp01(player.PlanarSpeed / player.MoveSpeed);
            moveAmount = Mathf.MoveTowards(moveAmount, speed01, dt * 4f);
            bobPhase += dt * Mathf.Lerp(1.5f, 9f, moveAmount);
            raise = Mathf.MoveTowards(raise, Casting ? 1f : 0f, dt * 7f);
            kick = Mathf.MoveTowards(kick, 0f, dt * 6f);

            // Quickening: light motes stream off the hands (more while running), drifting down and back.
            bool quick = player.Run && player.Run.Quickened;
            if (quick)
            {
                moteTimer -= dt * Mathf.Lerp(10f, 26f, moveAmount);
                while (moteTimer <= 0f)
                {
                    moteTimer += 1f;
                    motes.Add(new Mote
                    {
                        pos = new Vector2(Random.Range(-14f, 14f), Random.Range(-6f, 6f)),
                        vel = new Vector2(Random.Range(-10f, 10f), Random.Range(14f, 30f) * (0.6f + moveAmount)),
                        life = Random.Range(0.35f, 0.6f),
                    });
                }
            }
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                var m = motes[i];
                m.age += dt;
                m.pos += m.vel * dt;
                if (m.age >= m.life) motes.RemoveAt(i); else motes[i] = m;
            }
        }

        struct Mote { public Vector2 pos, vel; public float age, life; }
        readonly List<Mote> motes = new List<Mote>();
        float moteTimer;
        /// <summary>Quickening motes on screen (tests).</summary>
        public int Motes => motes.Count;

        public void Kick() => kick = 1f;

        /// <param name="px">Screen pixels per low-res pixel.</param>
        public void Draw(float px)
        {
            float s = px * 1.35f;
            bool castPose = raise > 0.5f;
            Texture tex = castPose ? Sprites.HandsCast : Sprites.HandsIdle;
            float w = Sprites.HandsW * s, h = Sprites.HandsH * s;

            // During the pose swap the sprite dips down and comes back up.
            float swapDip = (castPose ? 1f - raise : raise) * 2f * h * 0.45f;
            float bobX = Mathf.Sin(bobPhase) * 7f * px * moveAmount;
            float bobY = Mathf.Abs(Mathf.Cos(bobPhase)) * 5f * px * moveAmount + Mathf.Sin(time * 2f) * 1.2f * px;
            float kickY = kick * 9f * px;
            float x = Screen.width * 0.5f - w * 0.5f + (castPose ? 0f : w * 0.22f) + bobX;
            float y = Screen.height - h + 2f * px + bobY + swapDip + kickY;

            var prev = GUI.color;
            GUI.DrawTexture(new Rect(x, y, w, h), tex);

            if (Glow.a > 0.01f)
            {
                Vector2 focus = castPose ? Sprites.CastFocus : Sprites.IdleFocus;
                float pulse = 1f + 0.15f * Mathf.Sin(time * 10f) + kick * 0.8f;
                float g = (castPose ? 34f : 18f) * s * pulse;
                float gx = x + focus.x * s, gy = y + (Sprites.HandsH - focus.y) * s;
                GUI.color = Glow;
                GUI.DrawTexture(new Rect(gx - g / 2, gy - g / 2, g, g), PixelArt.Glow);
                if (castPose)
                {
                    var core = Color.Lerp(Glow, Color.white, 0.6f);
                    core.a = Glow.a;
                    GUI.color = core;
                    GUI.DrawTexture(new Rect(gx - g / 6, gy - g / 6, g / 3, g / 3), PixelArt.Glow);
                }
            }
            if (motes.Count > 0)
            {
                Vector2 focus = castPose ? Sprites.CastFocus : Sprites.IdleFocus;
                float fx = x + focus.x * s, fy = y + (Sprites.HandsH - focus.y) * s;
                foreach (var m in motes)
                {
                    float k = 1f - m.age / m.life;
                    float sz = (1.5f + 1.5f * k) * s;
                    GUI.color = new Color(0.6f, 1f, 0.65f, 0.7f * k);
                    GUI.DrawTexture(new Rect(fx + m.pos.x * s - sz / 2f, fy + m.pos.y * s - sz / 2f, sz, sz), PixelArt.Glow);
                }
            }
            GUI.color = prev;
        }
    }
}
