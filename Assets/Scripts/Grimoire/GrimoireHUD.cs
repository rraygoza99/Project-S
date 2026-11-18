using System.Collections.Generic;
using UnityEngine;
using C = Spellcaster.Grimoire.GrimoireConfig;

namespace Spellcaster.Grimoire
{
    /// <summary>
    /// Everything the Grimoire draws (IMGUI, through the HUD hooks): the hand next to the wizard's hands
    /// (Next card bigger and highlighted), draw / discard pile counts, draw / play / discard / reshuffle
    /// animations, the room reward panel, the pause-screen deck list and the debug panel.
    /// </summary>
    public static class GrimoireHUD
    {
        // ---------------- animation state ----------------

        static GrimoireRun attached;
        static readonly Dictionary<Card, Rect> shown = new Dictionary<Card, Rect>();
        struct Flying { public CardDef Def; public Rect From; public Vector2 To; public float T0, Duration; public bool Shrink; }
        static readonly List<Flying> flying = new List<Flying>();
        static float reshuffleT0 = -10f, discardPulseT0 = -10f, lastTime;
        const float ReshuffleDuration = 0.45f;

        static Rect lastDrawPile, lastDiscardPile;
        static Vector2 lastHandsPoint;

        static void Attach(GrimoireRun g)
        {
            if (attached == g) return;
            if (attached != null) attached.DeckChanged -= OnDeckChanged;
            attached = g;
            shown.Clear();
            flying.Clear();
            if (g != null) g.DeckChanged += OnDeckChanged;
        }

        static void OnDeckChanged(Deck.Change change, Card card)
        {
            float now = Time.unscaledTime;
            switch (change)
            {
                case Deck.Change.Play:
                case Deck.Change.Cycle:
                    if (card != null && shown.TryGetValue(card, out var from))
                    {
                        bool play = change == Deck.Change.Play;
                        flying.Add(new Flying
                        {
                            Def = card.Def, From = from, T0 = now, Duration = play ? 0.3f : 0.25f, Shrink = play,
                            To = play ? lastHandsPoint : lastDiscardPile.center,
                        });
                        shown.Remove(card);
                    }
                    if (change == Deck.Change.Cycle) discardPulseT0 = now + 0.2f;
                    break;
                case Deck.Change.Reshuffle: reshuffleT0 = now; break;
                case Deck.Change.Added: discardPulseT0 = now; break;
                case Deck.Change.Removed:
                    if (card != null) shown.Remove(card);
                    break;
                case Deck.Change.Cleared: shown.Clear(); break;
            }
        }

        /// <summary>Test hook: is a card animation (fly or reshuffle) playing right now.</summary>
        public static bool Animating => flying.Count > 0 || Time.unscaledTime - reshuffleT0 < ReshuffleDuration;

        // ---------------- styles and helpers ----------------

        static readonly Dictionary<(int, bool, TextAnchor, bool), GUIStyle> styles = new Dictionary<(int, bool, TextAnchor, bool), GUIStyle>();

        static GUIStyle Style(float size, bool bold = true, TextAnchor align = TextAnchor.MiddleCenter, bool wrap = false)
        {
            HUD.EnsureStyles();
            int s = Mathf.Max(8, Mathf.RoundToInt(size));
            var key = (s, bold, align, wrap);
            if (!styles.TryGetValue(key, out var st))
            {
                st = new GUIStyle(HUD.Label) { fontSize = s, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = align, wordWrap = wrap, clipping = wrap ? TextClipping.Clip : TextClipping.Overflow };
                styles[key] = st;
            }
            st.normal.textColor = Color.white;
            return st;
        }

        static void Text(Rect r, string text, GUIStyle st, Color c)
        {
            st.normal.textColor = c;
            HUD.Shadowed(r, text, st);
            st.normal.textColor = Color.white;
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        static Rect Scale(Rect r, float k) => new Rect(r.center.x - r.width * k * 0.5f, r.center.y - r.height * k * 0.5f, r.width * k, r.height * k);

        /// <summary>One card: dark body, colour band, frame, big symbol, name.</summary>
        public static void DrawCard(Rect r, CardDef d, float alpha, bool highlight = false, bool showName = true)
        {
            if (r.width < 2f || alpha <= 0.01f) return;
            Color body = d.IsCurse ? new Color(0.16f, 0.05f, 0.14f) : new Color(0.07f, 0.065f, 0.1f);
            HUD.Fill(r, A(body, 0.93f * alpha));
            HUD.Fill(new Rect(r.x, r.y, r.width, r.height * 0.2f), A(Color.Lerp(d.Color, Color.black, 0.35f), 0.95f * alpha));
            float t = Mathf.Max(2f, r.width * (highlight ? 0.07f : 0.04f));
            Color frame = highlight ? Color.Lerp(d.Color, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f)) : d.Color;
            HUD.Frame(r, A(frame, alpha), t);
            Text(new Rect(r.x, r.y + r.height * 0.2f, r.width, r.height * 0.5f), d.Symbol, Style(r.height * 0.28f), A(Color.Lerp(d.Color, Color.white, 0.35f), alpha));
            if (!showName) return;
            float fs = Mathf.Min(r.height * 0.13f, r.width * 1.75f / Mathf.Max(4, d.Name.Length));
            if (fs >= 8f) Text(new Rect(r.x - r.width, r.y + r.height * 0.7f, r.width * 3f, r.height * 0.24f), d.Name, Style(fs), A(Color.white, alpha));
        }

        static void DrawCardBack(Rect r, float alpha)
        {
            HUD.Fill(r, new Color(0.18f, 0.12f, 0.3f, 0.95f * alpha));
            HUD.Frame(r, new Color(0.65f, 0.55f, 0.95f, alpha), Mathf.Max(1.5f, r.width * 0.06f));
            HUD.Fill(Scale(r, 0.35f), new Color(0.65f, 0.55f, 0.95f, 0.6f * alpha));
        }

        static bool Hover(Rect r)
        {
            Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return r.Contains(m);
        }

        static void Button(Rect r, string label, bool enabled, Color accent, float u)
        {
            bool hot = enabled && Hover(r);
            HUD.Fill(r, enabled ? new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, hot ? 0.98f : 0.85f) : new Color(0.12f, 0.12f, 0.14f, 0.8f));
            HUD.Frame(r, enabled ? (hot ? Color.white : accent) : new Color(0.4f, 0.4f, 0.45f), Mathf.Max(2f, 0.12f * u));
            Text(r, label, Style(1.35f * u), enabled ? Color.white : new Color(0.55f, 0.55f, 0.6f));
        }

        // ---------------- hand overlay ----------------

        /// <summary>Card slot rects (0 = Next) and pile boxes, to the right of the wizard's hands.</summary>
        static void Layout(float W, float H, float u, out Rect[] slots, out Rect drawPile, out Rect discardPile, out Vector2 handsPoint)
        {
            float px = H / RetroView.Height;
            float hw = Sprites.HandsW * px * 1.35f;
            float x = W * 0.5f + hw * 0.72f + u;
            float bottom = H - 3f * u;
            int n = Mathf.Max(1, C.HandSize);
            slots = new Rect[n];
            for (int i = 0; i < n; i++)
            {
                float w = i == 0 ? 5f * u : 3.8f * u, h = w * 1.4f;
                slots[i] = new Rect(x, bottom - h, w, h);
                x += w + 0.6f * u;
            }
            x += 0.8f * u;
            float pw = 2.8f * u, ph = pw * 1.4f;
            drawPile = new Rect(x, bottom - ph - 2.4f * u, pw, ph);
            discardPile = new Rect(x + pw + 2.2f * u, bottom - ph - 2.4f * u, pw, ph);
            handsPoint = new Vector2(W * 0.5f + hw * 0.1f, H - Sprites.HandsH * px * 1.35f * 0.55f);
        }

        public static void DrawOverlay(float W, float H, float u)
        {
            var g = GrimoireRun.Instance;
            Attach(g);
            if (g == null) return;
            if (g.DebugOpen) DrawDebug(W, H, u, g);
            if (!g.Active || g.Deck == null) return;
            var deck = g.Deck;
            float now = Time.unscaledTime;
            float dt = Mathf.Clamp(now - lastTime, 0f, 0.1f);
            DrawEliteMarkers(W, H, u);
            lastTime = now;

            Layout(W, H, u, out var slots, out var drawPile, out var discardPile, out var handsPoint);
            lastDrawPile = drawPile;
            lastDiscardPile = discardPile;
            lastHandsPoint = handsPoint;

            // Piles
            float shuffleK = Mathf.Clamp01((now - reshuffleT0) / ReshuffleDuration);
            bool shuffling = shuffleK < 1f;
            float drawPulse = shuffling ? 1f + 0.15f * Mathf.Sin(shuffleK * Mathf.PI) : 1f;
            float discPulse = Mathf.Clamp01(1f - (now - discardPulseT0) / 0.3f);
            DrawPile(Scale(drawPile, drawPulse), deck.DrawPile.Count, "DRAW", u, shuffling ? new Color(0.85f, 0.75f, 1f) : Color.white);
            DrawPile(Scale(discardPile, 1f + 0.12f * discPulse), deck.DiscardPile.Count, "DISCARD", u, Color.white);
            if (shuffling)
            {
                // Reshuffle: a few card backs hop from the discard pile into the draw pile.
                for (int i = 0; i < 4; i++)
                {
                    float k = Mathf.Clamp01(shuffleK * 1.6f - i * 0.18f);
                    if (k <= 0f || k >= 1f) continue;
                    Vector2 p = Vector2.Lerp(discardPile.center, drawPile.center, k) + Vector2.down * Mathf.Sin(k * Mathf.PI) * 2.5f * u;
                    DrawCardBack(new Rect(p.x - drawPile.width * 0.3f, p.y - drawPile.height * 0.3f, drawPile.width * 0.6f, drawPile.height * 0.6f), 1f);
                }
                Text(new Rect(drawPile.x - 2f * u, drawPile.y - 2.6f * u, discardPile.xMax - drawPile.x + 4f * u, 2f * u), "Reshuffle!", Style(1.2f * u), new Color(0.85f, 0.75f, 1f, 1f - shuffleK * 0.5f));
            }

            // Hand: cards ease toward their slot; new cards slide in from the draw pile.
            var seen = new HashSet<Card>();
            float ease = 1f - Mathf.Exp(-14f * dt);
            for (int i = deck.Hand.Count - 1; i >= 0; i--)
            {
                var card = deck.Hand[i];
                seen.Add(card);
                Rect target = slots[Mathf.Min(i, slots.Length - 1)];
                if (!shown.TryGetValue(card, out var r)) r = new Rect(drawPile.x, drawPile.y, drawPile.width, drawPile.height);
                r = new Rect(Mathf.Lerp(r.x, target.x, ease), Mathf.Lerp(r.y, target.y, ease), Mathf.Lerp(r.width, target.width, ease), Mathf.Lerp(r.height, target.height, ease));
                shown[card] = r;
                bool next = i == 0;
                DrawCard(r, card.Def, 1f, next);
                if (next)
                {
                    Text(new Rect(r.x - u, r.y - 2f * u, r.width + 2f * u, 1.8f * u), "NEXT", Style(1.1f * u), Color.Lerp(card.Def.Color, Color.white, 0.4f));
                    var ss = SpellSystem.Instance;
                    if (ss && ss.CastLock > 0f) HUD.Fill(new Rect(r.x, r.y, r.width, r.height * Mathf.Clamp01(ss.CastLock / Mathf.Max(0.01f, C.CycleLockout))), new Color(0f, 0f, 0f, 0.6f));
                }
            }
            if (shown.Count > seen.Count)
            {
                var stale = new List<Card>();
                foreach (var k in shown.Keys) if (!seen.Contains(k)) stale.Add(k);
                foreach (var k in stale) shown.Remove(k);
            }
            if (deck.Hand.Count == 0) Text(slots[0], "empty", Style(1.1f * u, false), new Color(1f, 1f, 1f, 0.6f));

            // Played cards fly into the hands; cycled cards fly to the discard pile.
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var f = flying[i];
                float k = (now - f.T0) / f.Duration;
                if (k >= 1f) { flying.RemoveAt(i); continue; }
                float e = 1f - (1f - k) * (1f - k);
                Vector2 c = Vector2.Lerp(f.From.center, f.To, e);
                float s = f.Shrink ? Mathf.Lerp(1f, 0.35f, e) : Mathf.Lerp(1f, 0.55f, e);
                var r = new Rect(c.x - f.From.width * s * 0.5f, c.y - f.From.height * s * 0.5f, f.From.width * s, f.From.height * s);
                DrawCard(r, f.Def, f.Shrink ? 1f - e * 0.7f : 1f);
                if (f.Shrink) HUD.DrawTinted(Scale(r, 2.2f), PixelArt.Glow, A(f.Def.Color, 0.6f * (1f - e)));
            }

            // Key hint and the optional log.
            Rect lastSlot = slots[slots.Length - 1];
            Text(new Rect(drawPile.x - 0.5f * u, drawPile.yMax + 1.5f * u, discardPile.xMax - drawPile.x + u, 1.6f * u),
                g.CycleBlocked() == null ? $"{C.CycleKey}: cycle" : $"{C.CycleKey}: cycle (not this card)", Style(1f * u, false),
                g.CycleBlocked() == null ? new Color(0.85f, 0.85f, 0.9f, 0.85f) : new Color(0.6f, 0.6f, 0.65f, 0.6f));
            bool showLog = g.ShowLog && (Settings.DevMode || g.DebugOpen);
            if (showLog && g.Log.Count > 0)
            {
                float fade = g.DebugOpen ? 1f : Mathf.Clamp01(4f - (now - g.LogTime));
                if (fade > 0f)
                {
                    float y = slots[0].y - 2.5f * u - g.Log.Count * 1.5f * u;
                    for (int i = 0; i < g.Log.Count; i++)
                        Text(new Rect(slots[0].x, y + i * 1.5f * u, lastSlot.xMax - slots[0].x + 12f * u, 1.5f * u), g.Log[i],
                            Style(1f * u, false, TextAnchor.MiddleLeft), new Color(0.9f, 0.9f, 1f, fade * (0.5f + 0.5f * (i + 1f) / g.Log.Count)));
                }
            }
        }

        /// <summary>"ELITE" over elite enemies (their tint alone is hard to read on red sprites).</summary>
        static void DrawEliteMarkers(float W, float H, float u)
        {
            var p = Player.Instance;
            if (!p) return;
            foreach (var e in Enemy.All)
            {
                if (!e.IsElite || !e.IsAlive) continue;
                var v = p.Cam.WorldToViewportPoint(e.Center + Vector3.up * (e.Type.Height * 0.5f + 0.45f));
                if (v.z < 0.1f || v.x < -0.1f || v.x > 1.1f || v.y < -0.1f || v.y > 1.1f) continue;
                if (Physics.Linecast(p.Cam.transform.position, e.Center, Layers.WorldMask)) continue;
                float sx = v.x * W, sy = (1f - v.y) * H;
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
                HUD.DrawTinted(new Rect(sx - 3f * u, sy - 1.6f * u, 6f * u, 3.2f * u), PixelArt.Glow, new Color(1f, 0.5f, 0.2f, 0.45f * pulse));
                Text(new Rect(sx - 5f * u, sy - u, 10f * u, 2f * u), "ELITE", Style(1.15f * u), new Color(1f, 0.8f, 0.35f, pulse));
            }
        }

        static void DrawPile(Rect r, int count, string label, float u, Color tint)
        {
            DrawCardBack(r, 1f);
            Text(r, count.ToString(), Style(1.6f * u), tint);
            Text(new Rect(r.x - u, r.yMax + 0.1f * u, r.width + 2f * u, 1.3f * u), label, Style(0.85f * u, false), new Color(0.85f, 0.85f, 0.9f));
        }

        // ---------------- reward panel ----------------

        public static void DrawPanels(float W, float H, float u)
        {
            var g = GrimoireRun.Instance;
            if (g == null || !g.Rewards.IsOpen || g.Deck == null) return;
            if (SpellForgeUI.Instance && SpellForgeUI.Instance.IsOpen) return;
            var rw = g.Rewards;
            HUD.Fill(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.04f, 0.78f));
            int gold = Run.Instance ? Run.Instance.Gold : 0;
            Text(new Rect(0, H * 0.06f, W, 3f * u), rw.IsFree ? "Scroll of Erasure: remove a card for free" : rw.Removing ? $"Remove a card ({C.RemoveCost} gold)" : "Room cleared: add a card to your Grimoire", Style(2.2f * u), new Color(0.9f, 0.8f, 1f));
            Text(new Rect(0, H * 0.06f + 3f * u, W, 2f * u), $"Deck: {g.Deck.Count} cards     Gold: {gold}", Style(1.2f * u, false), new Color(1f, 0.9f, 0.6f));

            if (!rw.Removing)
            {
                rw.ChoiceRects.Clear();
                float cw = 9f * u, ch = cw * 1.4f, gap = 4f * u;
                int n = rw.Choices.Count;
                float x = (W - (n * cw + (n - 1) * gap)) * 0.5f, y = H * 0.2f;
                for (int i = 0; i < n; i++)
                {
                    var r = new Rect(x + i * (cw + gap), y, cw, ch);
                    rw.ChoiceRects.Add(new Rect(r.x - u, r.y - u, r.width + 2f * u, r.height + 7f * u));
                    bool hot = Hover(rw.ChoiceRects[i]);
                    var d = rw.Choices[i];
                    if (hot) HUD.DrawTinted(Scale(r, 1.5f), PixelArt.Glow, A(d.Color, 0.35f));
                    DrawCard(hot ? Scale(r, 1.05f) : r, d, 1f, hot);
                    Text(new Rect(r.x - u, r.yMax + 0.6f * u, r.width + 2f * u, 2f * u), $"[{i + 1}] {d.Name}", Style(1.4f * u), Color.Lerp(d.Color, Color.white, 0.4f));
                    Text(new Rect(r.x - 1.5f * u, r.yMax + 2.6f * u, r.width + 3f * u, 4f * u), d.Desc, Style(1.05f * u, false, TextAnchor.UpperCenter, true), new Color(0.9f, 0.9f, 0.95f));
                }
                float by = y + ch + 8.5f * u, bw = 18f * u, bh = 3f * u;
                rw.RemoveButton = new Rect(W * 0.5f - bw - u, by, bw, bh);
                rw.SkipButton = new Rect(W * 0.5f + u, by, bw, bh);
                bool canRemove = rw.RemoveBlocked() == null;
                Button(rw.RemoveButton, $"[4] Remove a card ({C.RemoveCost}g)", canRemove, new Color(1f, 0.5f, 0.4f), u);
                Button(rw.SkipButton, "[5] Skip", true, new Color(0.7f, 0.7f, 0.8f), u);
            }
            else
            {
                rw.RemoveRects.Clear();
                var groups = g.Deck.Grouped();
                float rowH = 3.2f * u, w = Mathf.Min(W * 0.6f, 52f * u), x = (W - w) * 0.5f, y = H * 0.17f;
                for (int i = 0; i < groups.Count; i++)
                {
                    var (d, count) = groups[i];
                    var row = new Rect(x, y + i * rowH, w, rowH - 0.3f * u);
                    rw.RemoveRects.Add(row);
                    bool hot = Hover(row);
                    HUD.Fill(row, hot ? A(d.Color, 0.3f) : new Color(0f, 0f, 0f, 0.45f));
                    if (hot) HUD.Frame(row, d.Color, Mathf.Max(2f, 0.12f * u));
                    DrawCard(new Rect(row.x + 0.4f * u, row.y + 0.2f * u, (rowH - 0.7f * u) / 1.4f, rowH - 0.7f * u), d, 1f, false, false);
                    string key = i < 9 ? $"[{i + 1}] " : "";
                    Text(new Rect(row.x + 3.2f * u, row.y, w, rowH - 0.3f * u), $"{key}{count}x {d.Name}", Style(1.3f * u, true, TextAnchor.MiddleLeft), Color.Lerp(d.Color, Color.white, 0.4f));
                    Text(new Rect(row.x + 16f * u, row.y, w - 16.5f * u, rowH - 0.3f * u), d.Desc, Style(1.05f * u, false, TextAnchor.MiddleLeft), new Color(0.85f, 0.85f, 0.9f));
                }
                rw.BackButton = new Rect(W * 0.5f - 9f * u, y + groups.Count * rowH + u, 18f * u, 3f * u);
                Button(rw.BackButton, "[0] Back", true, new Color(0.7f, 0.7f, 0.8f), u);
            }

            if (rw.Message != null && Time.unscaledTime - rw.MessageTime < 2.5f)
                Text(new Rect(0, H * 0.88f, W, 2.4f * u), rw.Message, Style(1.4f * u), new Color(1f, 0.7f, 0.5f));
            Text(new Rect(0, H - 2.5f * u, W, 2f * u), rw.Removing ? "Click a card or press its number   0 / Esc: back" : "Click a card or press 1-3   4: remove a card   5: skip",
                Style(1.05f * u, false), new Color(0.8f, 0.8f, 0.85f));
        }

        // ---------------- pause screen ----------------

        /// <summary>The pause screen shows the deck (the Grimoire is on in this run).</summary>
        public static bool PauseShown { get { var g = GrimoireRun.Instance; return g != null && g.Active && g.Deck != null; } }
        static float PauseWidth(float W, float u) => Mathf.Min(W * 0.24f, 26f * u);
        /// <summary>Left edge of the deck panel: it sits left of the spell list (which is 17u wide at the right edge).</summary>
        public static float PauseLeft(float W, float u) => W - 19.5f * u - PauseWidth(W, u);

        public static void DrawPause(float W, float H, float u)
        {
            var g = GrimoireRun.Instance;
            if (g == null || !g.Active || g.Deck == null) return;
            var deck = g.Deck;
            float w = PauseWidth(W, u), x = PauseLeft(W, u) + u, y = H * 0.24f;
            var groups = deck.Grouped();
            HUD.Fill(new Rect(x - u, y - u, w + 1.5f * u, 6.8f * u + groups.Count * 3.6f * u), new Color(0.02f, 0.01f, 0.04f, 0.9f));
            Text(new Rect(x, y, w, 2.5f * u), $"GRIMOIRE ({deck.Count} cards)", Style(HUD.Label.fontSize, true, TextAnchor.UpperLeft), Color.white);
            y += 2.6f * u;
            Text(new Rect(x, y, w, 1.8f * u), $"Draw {deck.DrawPile.Count}   Hand {deck.Hand.Count}   Discard {deck.DiscardPile.Count}", Style(1.05f * u, false, TextAnchor.UpperLeft), new Color(0.8f, 0.8f, 0.9f));
            y += 2.2f * u;
            foreach (var (d, count) in groups)
            {
                var icon = new Rect(x, y + 0.1f * u, 2.2f * u, 3.08f * u);
                DrawCard(icon, d, 1f, false, false);
                Text(new Rect(x + 2.8f * u, y, w, 1.8f * u), $"{count}x {d.Name}", Style(1.15f * u, true, TextAnchor.UpperLeft), Color.Lerp(d.Color, Color.white, 0.4f));
                Text(new Rect(x + 2.8f * u, y + 1.6f * u, w - 2.8f * u, 1.8f * u), d.Desc, Style(0.95f * u, false, TextAnchor.UpperLeft), new Color(0.85f, 0.85f, 0.9f));
                y += 3.6f * u;
            }
        }

        // ---------------- debug panel ----------------

        static void DrawDebug(float W, float H, float u, GrimoireRun g)
        {
            var box = new Rect(u, 9f * u, 30f * u, 34f * u);
            HUD.Fill(box, new Color(0f, 0f, 0f, 0.82f));
            HUD.Frame(box, new Color(0.7f, 0.55f, 1f), Mathf.Max(2f, 0.12f * u));
            float x = box.x + u, y = box.y + 0.6f * u, lh = 1.55f * u;
            var st = Style(1.05f * u, false, TextAnchor.UpperLeft);
            void Line(string s, Color c) { Text(new Rect(x, y, box.width - 2f * u, lh), s, st, c); y += lh; }
            Text(new Rect(x, y, box.width, 2f * u), "GRIMOIRE DEBUG (F4)", Style(1.3f * u, true, TextAnchor.UpperLeft), new Color(0.85f, 0.75f, 1f));
            y += 2.2f * u;
            Line($"G  Grimoire: {(Settings.GrimoireEnabled ? "ON" : "OFF")}{(Settings.GrimoireEnabled && !g.Active ? " (inactive here)" : "")}", Settings.GrimoireEnabled ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.5f, 0.45f));
            Line($"B  Bolt uses cards: {C.BoltUse}", Color.white);
            Line($"L  Log: {(g.ShowLog ? "on" : "off")}", Color.white);
            if (g.Deck != null)
            {
                Line($"P  Preset deck: {C.Presets[g.PresetIndex].name}", Color.white);
                Line("C  Clear deck     X  Reshuffle", Color.white);
                Line($"Deck {g.Deck.Count}: draw {g.Deck.DrawPile.Count}, hand {g.Deck.Hand.Count}, discard {g.Deck.DiscardPile.Count}", new Color(0.8f, 0.8f, 0.9f));
            }
            y += 0.4f * u;
            Line("Add as Next (Shift: to discard):", new Color(0.85f, 0.75f, 1f));
            for (int i = 0; i < Cards.All.Count && i < 12; i++)
                Line($"  {GrimoireDebug.KeyName(i)}  {Cards.All[i].Name}", Color.Lerp(Cards.All[i].Color, Color.white, 0.35f));
        }
    }
}
