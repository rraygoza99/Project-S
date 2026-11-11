using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Draws, in order: the upscaled low-res world image, the wizard hands, damage overlay, crosshair,
    /// HP bar, wave info, spell panel, casting input, banners and full-screen states. IMGUI keeps it code-only.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        public static GUIStyle Label, LabelRight, Small, Centered, Title;
        static int styleFontSize = -1;
        float fps = 60f;

        void Update()
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            fps = Mathf.Lerp(fps, 1f / dt, 0.05f);
        }

        public static void EnsureStyles()
        {
            int size = Mathf.Max(10, Mathf.RoundToInt(Screen.height * 0.024f));
            if (size == styleFontSize && Label != null) return;
            styleFontSize = size;
            Label = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
            Label.normal.textColor = Color.white;
            LabelRight = new GUIStyle(Label) { alignment = TextAnchor.UpperRight };
            Small = new GUIStyle(Label) { fontSize = Mathf.RoundToInt(size * 0.75f), fontStyle = FontStyle.Normal };
            Centered = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(size * 1.3f), wordWrap = true };
            Title = new GUIStyle(Centered) { fontSize = Mathf.RoundToInt(size * 3.2f) };
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            float W = Screen.width, H = Screen.height;
            float px = H / RetroView.Height;
            float u = H / 60f; // layout unit
            var player = Player.Instance;
            var gs = GameState.Instance;
            var spells = SpellSystem.Instance;

            if (RetroView.RT) GUI.DrawTexture(new Rect(0, 0, W, H), RetroView.RT, ScaleMode.StretchToFill, false);
            if (gs.Phase == Phase.Title) { DrawTitle(W, H); return; }

            if (WizardHands.Instance) WizardHands.Instance.Draw(px);

            if (player)
            {
                if (player.HurtFlash > 0f) Fill(new Rect(0, 0, W, H), new Color(0.8f, 0f, 0f, 0.35f * player.HurtFlash));
                if (player.HealFlash > 0f) Fill(new Rect(0, 0, W, H), new Color(0.2f, 1f, 0.3f, 0.25f * player.HealFlash));
            }
            if (spells && spells.FizzleFlash > 0f) Fill(new Rect(0, 0, W, H), new Color(1f, 0.3f, 0.1f, 0.12f * spells.FizzleFlash));

            DrawCrosshair(W, H, px);
            if (player) DrawHealth(H, px, player);

            float pad = 4f * px;
            Shadowed(new Rect(pad, pad, W, 3 * u), $"WAVE {gs.Wave}", Label);
            Shadowed(new Rect(pad, pad + 2.2f * u, W, 3 * u), $"Enemies left {gs.RemainingInWave}   Kills {gs.Kills}", Small);
            Shadowed(new Rect(0, H - pad - 1.6f * u, W - pad, 2 * u), $"{fps:0} FPS", Small.Right());

            if (spells)
            {
                DrawSpellPanel(W, H, u, spells);
                DrawCastingInput(W, H, u, spells);
            }

            Shadowed(new Rect(0, H - pad - 1.6f * u, W, 2 * u),
                "Hold Ctrl / RMB: channel   Arrows: input   LMB: cast / bolt   Tab: Spell Forge", Small.Center());

            if (gs.Phase == Phase.Intermission)
                Shadowed(new Rect(0, H * 0.28f, W, 3 * u),
                    $"Next wave in {Mathf.CeilToInt(gs.BreakTimeLeft)}   (Enter: start now, Tab: Spell Forge)", Label.Center());
            if (gs.BannerTimer > 0f)
            {
                var c = Title.normal.textColor;
                Title.normal.textColor = new Color(1f, 0.85f, 0.4f, Mathf.Clamp01(gs.BannerTimer * 2f));
                Shadowed(new Rect(0, H * 0.12f, W, 6 * u), gs.Banner, Title);
                Title.normal.textColor = c;
            }

            if (SpellForgeUI.Instance) SpellForgeUI.Instance.Draw();

            if (gs.Phase == Phase.Dead) DrawDeath(W, H, u, gs);
            else if (gs.Paused)
            {
                Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.5f));
                Shadowed(new Rect(0, 0, W, H), "Paused - click to resume", Centered);
            }
        }

        void DrawTitle(float W, float H)
        {
            Fill(new Rect(0, 0, W, H), new Color(0.03f, 0.01f, 0.05f, 0.65f));
            Shadowed(new Rect(0, H * 0.18f, W, H * 0.15f), "SPELLCASTER", Title);
            Shadowed(new Rect(W * 0.15f, H * 0.36f, W * 0.7f, H * 0.4f),
                "Hold Ctrl or Right Mouse to channel, then type the arrows with the arrow keys.\n" +
                "A full sequence readies the spell - Left Click to cast it.\n" +
                "Left Click with nothing readied fires a magic bolt.\n" +
                "Tab opens the Spell Forge: fuse two spells into a new one.\n\nClick to begin", Centered);
        }

        void DrawDeath(float W, float H, float u, GameState gs)
        {
            Fill(new Rect(0, 0, W, H), new Color(0.35f, 0f, 0f, 0.55f));
            var c = Title.normal.textColor;
            Title.normal.textColor = new Color(1f, 0.25f, 0.2f);
            Shadowed(new Rect(0, H * 0.25f, W, H * 0.15f), "YOU DIED", Title);
            Title.normal.textColor = c;
            Shadowed(new Rect(0, H * 0.45f, W, 4 * u), $"You reached wave {gs.Wave} with {gs.Kills} kills", Centered);
            Shadowed(new Rect(0, H * 0.55f, W, 4 * u), "Press R to restart", Centered);
        }

        static void DrawCrosshair(float W, float H, float px)
        {
            float c = Mathf.Max(2f, Mathf.Round(px));
            var col = new Color(1f, 1f, 1f, 0.85f);
            Fill(new Rect(W / 2 - c * 3, H / 2 - c / 2, c * 2, c), col);
            Fill(new Rect(W / 2 + c, H / 2 - c / 2, c * 2, c), col);
            Fill(new Rect(W / 2 - c / 2, H / 2 - c * 3, c, c * 2), col);
            Fill(new Rect(W / 2 - c / 2, H / 2 + c, c, c * 2), col);
        }

        static void DrawHealth(float H, float px, Player player)
        {
            float pad = 4f * px, bw = 70f * px, bh = 7f * px;
            var bar = new Rect(pad, H - pad - bh - 4f * px, bw, bh);
            Fill(new Rect(bar.x - px, bar.y - px, bar.width + 2 * px, bar.height + 2 * px), new Color(0, 0, 0, 0.7f));
            Fill(bar, new Color(0.25f, 0.05f, 0.05f, 0.9f));
            float k = player.HP / Player.MaxHP;
            Fill(new Rect(bar.x, bar.y, bar.width * k, bar.height), Color.Lerp(new Color(0.9f, 0.1f, 0.1f), new Color(0.2f, 0.85f, 0.25f), k));
            Shadowed(new Rect(bar.x + 2 * px, bar.y - 1.25f * bh, bw, bh * 1.2f), "HP " + Mathf.CeilToInt(player.HP), Label);
        }

        void DrawSpellPanel(float W, float H, float u, SpellSystem ss)
        {
            var book = ss.Book;
            float rowH = 3.6f * u, icon = 3f * u, arrow = 1.35f * u, width = 17f * u;
            float x0 = W - width - u, y = 2.5f * u;
            bool filtering = ss.Channeling && ss.Entered.Count > 0;

            for (int i = 0; i < book.Count; i++)
            {
                var s = book[i];
                float cd = ss.CooldownRemaining(s);
                bool candidate = ss.IsCandidate(s);
                bool readied = ss.Readied == s;
                float alpha = cd > 0f ? 0.45f : (filtering && !candidate ? 0.22f : 1f);
                var row = new Rect(x0, y, width, rowH - 0.3f * u);

                Color bg = readied ? new Color(s.Color.r, s.Color.g, s.Color.b, 0.45f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f))
                    : new Color(0f, 0f, 0f, 0.45f * alpha);
                Fill(row, bg);
                if (readied) Frame(row, s.Color, Mathf.Max(2f, u * 0.15f));

                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                var iconRect = new Rect(row.x + 0.3f * u, row.y + 0.15f * u, icon, icon);
                GUI.DrawTexture(iconRect, s.Icon, ScaleMode.ScaleToFit);
                if (s.SecondaryIcon) GUI.DrawTexture(new Rect(iconRect.xMax - icon * 0.45f, iconRect.yMax - icon * 0.45f, icon * 0.45f, icon * 0.45f), s.SecondaryIcon);
                GUI.color = prev;
                if (cd > 0f)
                {
                    float k = cd / s.Cooldown;
                    Fill(new Rect(iconRect.x, iconRect.y, iconRect.width, iconRect.height * k), new Color(0, 0, 0, 0.65f));
                    Shadowed(iconRect, cd.ToString(cd < 10f ? "0.0" : "0"), Small.Center());
                }

                float tx = iconRect.xMax + 0.5f * u;
                SetAlpha(Small, alpha);
                Shadowed(new Rect(tx, row.y, width, 1.6f * u), $"{i + 1}. {s.Name}", Small);
                SetAlpha(Small, 1f);

                // Arrow sequence: entered prefix lights up in the spell colour.
                for (int a = 0; a < s.Sequence.Length; a++)
                {
                    bool lit = candidate && filtering && a < ss.Entered.Count;
                    Color c = lit ? Color.Lerp(s.Color, Color.white, 0.35f) : new Color(0.85f, 0.85f, 0.85f);
                    c.a = alpha;
                    var r = new Rect(tx + a * (arrow + 0.15f * u), row.y + 1.75f * u, arrow, arrow);
                    if (lit) Fill(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(s.Color.r, s.Color.g, s.Color.b, 0.35f));
                    DrawTinted(r, PixelArt.ArrowTex(s.Sequence[a]), c);
                }
                y += rowH;
            }
        }

        void DrawCastingInput(float W, float H, float u, SpellSystem ss)
        {
            float cy = H * 0.62f;
            if (ss.Readied != null)
            {
                var s = ss.Readied;
                var st = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = Color.Lerp(s.Color, Color.white, 0.3f);
                Shadowed(new Rect(0, cy + 2.5f * u, W, 2 * u), $"{s.Name} READY - Left Click", st);
            }
            if (ss.Channeling)
            {
                float a = 2.4f * u;
                int n = ss.Entered.Count;
                float total = Mathf.Max(1, n) * (a + 0.3f * u);
                float x = W / 2 - total / 2;
                if (n == 0)
                    Shadowed(new Rect(0, cy, W, a), ss.Lockout > 0f ? "..." : "channeling", Small.Center());
                for (int i = 0; i < n; i++)
                    DrawTinted(new Rect(x + i * (a + 0.3f * u), cy, a, a), PixelArt.ArrowTex(ss.Entered[i]), new Color(1f, 0.95f, 0.6f));
            }
            if (ss.MessageTimer > 0f)
            {
                var st = new GUIStyle(Centered);
                st.normal.textColor = new Color(1f, 0.4f, 0.25f, Mathf.Clamp01(ss.MessageTimer * 2f));
                Shadowed(new Rect(0, cy - 3f * u, W, 3 * u), ss.LastMessage, st);
            }
        }

        // ---------------- helpers ----------------

        public static void Fill(Rect r, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, PixelArt.White);
            GUI.color = prev;
        }

        public static void Frame(Rect r, Color color, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), color);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), color);
            Fill(new Rect(r.x, r.y, t, r.height), color);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), color);
        }

        public static void DrawTinted(Rect r, Texture tex, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
        }

        public static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = new Color(0, 0, 0, 0.85f * prev.a);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = prev;
            GUI.Label(r, text, style);
        }

        static void SetAlpha(GUIStyle s, float a)
        {
            var c = s.normal.textColor;
            c.a = a;
            s.normal.textColor = c;
        }
    }

    static class GUIStyleExt
    {
        static readonly Dictionary<(GUIStyle, TextAnchor), GUIStyle> cache = new Dictionary<(GUIStyle, TextAnchor), GUIStyle>();

        static GUIStyle Aligned(GUIStyle s, TextAnchor a)
        {
            if (!cache.TryGetValue((s, a), out var r)) cache[(s, a)] = r = new GUIStyle(s) { alignment = a };
            return r;
        }

        public static GUIStyle Center(this GUIStyle s) => Aligned(s, TextAnchor.MiddleCenter);
        public static GUIStyle Right(this GUIStyle s) => Aligned(s, TextAnchor.UpperRight);
    }
}
