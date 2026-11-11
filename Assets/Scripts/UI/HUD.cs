using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Draws, in order: the upscaled low-res world image, the wizard hands, damage overlay, crosshair,
    /// HP / gold / item slots, minimap, shop prices, boss bar, spell panel, casting input, prompts,
    /// banners, the full map (hold M) and full-screen states. IMGUI keeps it code-only.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        public static GUIStyle Label, LabelRight, Small, Centered, Title;
        static int styleFontSize = -1;
        float fps = 60f;

        /// <summary>Test hook: draw the full map as if M were held.</summary>
        public static bool ShowMap;

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
            var run = Run.Instance;
            var dungeon = Dungeon.Instance;

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
            if (player) DrawHealth(H, px, u, player, run);
            if (run) DrawSlots(H, px, u, run);
            if (dungeon) DrawMinimap(u, dungeon, run);
            if (dungeon && player) DrawWorldLabels(W, H, u, player);
            if (dungeon && dungeon.Boss && dungeon.Boss.IsAlive) DrawBossBar(W, u, dungeon.Boss);

            float pad = 4f * px;
            Shadowed(new Rect(0, H - pad - 1.6f * u, W - pad, 2 * u), $"{fps:0} FPS", Small.Right());

            if (spells)
            {
                DrawSpellPanel(W, H, u, spells);
                DrawCastingInput(W, H, u, spells);
            }

            if (player && gs.InRun) DrawInteractPrompt(W, H, u, player);
            if (run && run.ToastTimer > 0f) DrawToast(W, H, u, run);

            Shadowed(new Rect(0, H - pad - 1.6f * u, W, 2 * u),
                "Ctrl/RMB + 1-4: spell   LMB: cast   Z X C: items   E: take   M: map   Tab: Forge", Small.Center());

            if (gs.BannerTimer > 0f)
            {
                var c = Title.normal.textColor;
                Title.normal.textColor = new Color(1f, 0.85f, 0.4f, Mathf.Clamp01(gs.BannerTimer * 2f));
                Shadowed(new Rect(0, H * 0.12f, W, 6 * u), gs.Banner, Title);
                Title.normal.textColor = c;
            }

            bool showMap = ShowMap || (gs.InRun && !gs.Paused && !gs.ForgeOpen && Input.GetKey(KeyCode.M));
            if (showMap && dungeon) DrawBigMap(W, H, u, dungeon, run);

            if (SpellForgeUI.Instance) SpellForgeUI.Instance.Draw();

            if (gs.Phase == Phase.Dead) DrawEnd(W, H, u, gs, false);
            else if (gs.Phase == Phase.Victory) DrawEnd(W, H, u, gs, true);
            else if (gs.Paused)
            {
                Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.5f));
                Shadowed(new Rect(0, 0, W, H), "Paused - click to resume", Centered);
            }
        }

        void DrawTitle(float W, float H)
        {
            Fill(new Rect(0, 0, W, H), new Color(0.03f, 0.01f, 0.05f, 0.65f));
            Shadowed(new Rect(0, H * 0.14f, W, H * 0.15f), "SPELLCASTER", Title);
            Shadowed(new Rect(W * 0.12f, H * 0.32f, W * 0.76f, H * 0.5f),
                "Fight through three dungeon floors, room by room. Doors lock until the room is clear.\n" +
                "Hold Ctrl or Right Mouse, type the spell's keys with 1-4, Left Click to cast.\n" +
                "Find spell runes, relics and items; spend gold in shops; Tab fuses spells in the Forge.\n" +
                "Z X C use items, E picks up, hold M for the map.\n\nClick to begin", Centered);
        }

        void DrawEnd(float W, float H, float u, GameState gs, bool won)
        {
            Fill(new Rect(0, 0, W, H), won ? new Color(0.1f, 0.05f, 0.25f, 0.6f) : new Color(0.35f, 0f, 0f, 0.55f));
            var c = Title.normal.textColor;
            Title.normal.textColor = won ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.25f, 0.2f);
            Shadowed(new Rect(0, H * 0.2f, W, H * 0.15f), won ? "YOU ESCAPED" : "YOU DIED", Title);
            Title.normal.textColor = c;
            var d = Dungeon.Instance;
            var run = Run.Instance;
            int floor = d ? d.Floor : 1, rooms = d ? d.RoomsCleared : 0;
            int gold = run ? run.GoldEarned : 0, items = run ? run.Items.Count : 0;
            int t = Mathf.FloorToInt(gs.RunTime);
            string where = won ? $"All {Dungeon.FinalFloor} floors cleared" : $"Fell on floor {floor}";
            Shadowed(new Rect(0, H * 0.42f, W, 4 * u), where, Centered);
            Shadowed(new Rect(0, H * 0.5f, W, 3 * u),
                $"{gs.Kills} kills   {rooms} rooms cleared   {gold} gold found   {items} items   {SpellBook.Known.Count} spells   {t / 60}:{t % 60:00}", Label.Center());
            Shadowed(new Rect(0, H * 0.6f, W, 4 * u), "Press R to start a new run", Centered);
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

        static void DrawHealth(float H, float px, float u, Player player, Run run)
        {
            float pad = 4f * px, bw = 70f * px, bh = 7f * px;
            var bar = new Rect(pad, H - pad - bh - 4f * px, bw, bh);
            Fill(new Rect(bar.x - px, bar.y - px, bar.width + 2 * px, bar.height + 2 * px), new Color(0, 0, 0, 0.7f));
            Fill(bar, new Color(0.25f, 0.05f, 0.05f, 0.9f));
            float k = Mathf.Clamp01(player.HP / player.MaxHP);
            Fill(new Rect(bar.x, bar.y, bar.width * k, bar.height), Color.Lerp(new Color(0.9f, 0.1f, 0.1f), new Color(0.2f, 0.85f, 0.25f), k));
            Shadowed(new Rect(bar.x + 2 * px, bar.y - 1.25f * bh, bw, bh * 1.2f), $"HP {Mathf.CeilToInt(player.HP)}/{Mathf.RoundToInt(player.MaxHP)}", Label);
            if (!run) return;
            float cy = bar.y - 1.25f * bh - 2.8f * u;
            GUI.DrawTexture(new Rect(bar.x, cy + 0.2f * u, 1.8f * u, 1.8f * u), Items.Coin.Icon);
            var gold = new GUIStyle(Label);
            gold.normal.textColor = new Color(1f, 0.85f, 0.3f);
            Shadowed(new Rect(bar.x + 2.2f * u, cy, bw, 2.4f * u), run.Gold.ToString(), gold);
            if (run.HasteTimer > 0f)
            {
                var hs = new GUIStyle(Small);
                hs.normal.textColor = new Color(1f, 0.9f, 0.3f);
                Shadowed(new Rect(bar.x + 7f * u, cy + 0.4f * u, bw, 2 * u), $"HASTE {run.HasteTimer:0}s", hs);
            }
        }

        static void DrawSlots(float H, float px, float u, Run run)
        {
            float pad = 4f * px, size = 3.4f * u;
            float x = pad + 70f * px + 2f * u, y = H - pad - size - 1.5f * px;
            for (int i = 0; i < Run.Slots; i++)
            {
                var r = new Rect(x + i * (size + 0.5f * u), y, size, size);
                Fill(r, new Color(0, 0, 0, 0.55f));
                Frame(r, new Color(1f, 1f, 1f, 0.25f), Mathf.Max(1f, px * 0.5f));
                var item = run.Consumables[i];
                if (item != null) GUI.DrawTexture(new Rect(r.x + 0.3f * u, r.y + 0.3f * u, size - 0.6f * u, size - 0.6f * u), item.Icon, ScaleMode.ScaleToFit);
                Shadowed(new Rect(r.x + 0.2f * u, r.y, size, 1.6f * u), Run.SlotKeyName(i), Small);
            }
        }

        // ---------------- map ----------------

        static readonly Color MapCurrent = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color MapVisited = new Color(0.62f, 0.6f, 0.68f, 0.85f);
        static readonly Color MapUnknown = new Color(0.25f, 0.24f, 0.3f, 0.8f);
        static GUIStyle mapIcon;

        static (string, Color) RoomIcon(Room r) => r.Type switch
        {
            RoomType.Boss => ("B", new Color(1f, 0.3f, 0.25f)),
            RoomType.Treasure => ("T", new Color(1f, 0.85f, 0.3f)),
            RoomType.Shop => ("$", new Color(1f, 0.9f, 0.4f)),
            RoomType.Shrine => ("R", new Color(0.6f, 0.8f, 1f)),
            _ => (null, Color.white),
        };

        static void DrawRooms(Rect area, float cell, Vector2Int origin, Dungeon d, int range)
        {
            Vector2 center = area.center;
            mapIcon ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            foreach (var room in d.Rooms.Values)
            {
                if (!d.IsRevealed(room)) continue;
                var rel = room.Cell - origin;
                if (range > 0 && (Mathf.Abs(rel.x) > range || Mathf.Abs(rel.y) > range)) continue;
                Vector2 c = center + new Vector2(rel.x, -rel.y) * cell;
                float s = cell * 0.78f;
                var rect = new Rect(c.x - s / 2, c.y - s / 2, s, s);
                Fill(rect, room == d.Current ? MapCurrent : (room.Visited ? MapVisited : MapUnknown));
                foreach (var door in room.Doors)
                {
                    var o = door.Other(room);
                    if (!room.Visited && !o.Visited) continue;
                    var dir = o.Cell - room.Cell;
                    Vector2 mid = c + new Vector2(dir.x, -dir.y) * cell * 0.5f;
                    float t = Mathf.Max(2f, cell * 0.18f);
                    Fill(new Rect(mid.x - t / 2, mid.y - t / 2, t, t), door.Locked ? new Color(0.9f, 0.2f, 0.2f) : MapVisited);
                }
                var (icon, ic) = RoomIcon(room);
                if (icon != null)
                {
                    mapIcon.normal.textColor = room == d.Current ? Color.black : ic;
                    mapIcon.fontSize = Mathf.Max(8, Mathf.RoundToInt(cell * 0.55f));
                    GUI.Label(rect, icon, mapIcon);
                }
            }
        }

        static void DrawMinimap(float u, Dungeon d, Run run)
        {
            float cell = 1.9f * u;
            const int range = 3;
            var area = new Rect(u, u, cell * (range * 2 + 1), cell * (range * 2 + 1));
            Fill(area, new Color(0, 0, 0, 0.45f));
            if (d.Current != null) DrawRooms(area, cell, d.Current.Cell, d, range);
            string label = d.Current?.Label;
            Shadowed(new Rect(area.x, area.yMax + 0.3f * u, area.width * 2, 2.5f * u), $"FLOOR {d.Floor}" + (label != null ? $"  {label}" : ""), Label);
            if (run && run.Items.Count > 0)
            {
                float s = 2.2f * u, x = area.x, y = area.yMax + 3f * u;
                int perRow = Mathf.Max(1, Mathf.FloorToInt(area.width / (s + 0.2f * u)));
                for (int i = 0; i < run.Items.Count; i++)
                    GUI.DrawTexture(new Rect(x + (i % perRow) * (s + 0.2f * u), y + (i / perRow) * (s + 0.2f * u), s, s), run.Items[i].Icon, ScaleMode.ScaleToFit);
            }
        }

        static void DrawBigMap(float W, float H, float u, Dungeon d, Run run)
        {
            Fill(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.04f, 0.82f));
            float cell = 4.2f * u;
            var area = new Rect(W * 0.03f, H * 0.12f, W * 0.52f, H * 0.76f);
            Shadowed(new Rect(area.x, H * 0.04f, area.width, 3 * u), $"FLOOR {d.Floor} / {Dungeon.FinalFloor}", Label.Center());
            DrawRooms(area, cell, Vector2Int.zero, d, 0);
            Shadowed(new Rect(area.x, area.yMax, area.width, 2 * u), "T treasure   $ shop   R rune shrine   B boss", Small.Center());

            if (!run) return;
            float x = W * 0.58f, y = H * 0.08f, lh = 2.1f * u, w = W * 0.4f;
            var st = run.Stats;
            string[] lines =
            {
                $"Max HP        {st.MaxHP:0}",
                $"Speed         {run.MoveSpeed:0.0}",
                $"Spell power   {st.Power * 100f:0}%",
                $"Cooldowns     {run.CooldownMult * 100f:0}%",
                $"Bolt          {st.BoltDamage:0} dmg, {1f / st.BoltInterval:0.0}/s",
                $"Damage taken  {st.DamageTaken * 100f:0}%",
                $"Luck          +{st.Luck * 100f:0}%",
            };
            Shadowed(new Rect(x, y, w, 2.5f * u), "STATS", Label);
            y += 2.8f * u;
            foreach (var l in lines) { Shadowed(new Rect(x, y, w, lh), l, Small); y += lh * 0.85f; }
            y += u;
            Shadowed(new Rect(x, y, w, 2.5f * u), "ITEMS & RELICS", Label);
            y += 2.8f * u;
            if (run.Items.Count == 0) Shadowed(new Rect(x, y, w, lh), "none yet", Small);
            foreach (var it in run.Items)
            {
                GUI.DrawTexture(new Rect(x, y, 1.8f * u, 1.8f * u), it.Icon, ScaleMode.ScaleToFit);
                Shadowed(new Rect(x + 2.3f * u, y, w, lh), $"{it.Name}: {it.Desc}", Small);
                y += lh;
            }
        }

        // ---------------- world-anchored labels and prompts ----------------

        static bool ToScreen(Player player, Vector3 world, float W, float H, out Vector2 screen)
        {
            var v = player.Cam.WorldToViewportPoint(world);
            screen = new Vector2(v.x * W, (1f - v.y) * H);
            return v.z > 0.1f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }

        static void DrawWorldLabels(float W, float H, float u, Player player)
        {
            var st = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            foreach (var p in Pickup.All)
            {
                if (!p || p.Price <= 0) continue;
                float dist = Vector3.Distance(p.transform.position, player.transform.position);
                if (dist > 20f || !ToScreen(player, p.LabelPosition, W, H, out var s)) continue;
                bool afford = Run.Instance && Run.Instance.Gold >= p.Price;
                st.normal.textColor = afford ? new Color(1f, 0.85f, 0.3f) : new Color(0.8f, 0.45f, 0.4f);
                Shadowed(new Rect(s.x - 5 * u, s.y - u, 10 * u, 2 * u), $"{p.Price}g", st);
            }
        }

        static void DrawInteractPrompt(float W, float H, float u, Player player)
        {
            var p = Pickup.NearestInteractable(player.transform.position);
            if (!p) return;
            var st = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = Color.Lerp(p.Item.Color, Color.white, 0.5f);
            Shadowed(new Rect(0, H * 0.56f, W, 2.5f * u), p.Prompt, st);
            Shadowed(new Rect(0, H * 0.56f + 2.4f * u, W, 2 * u), p.Item.Desc ?? "", Small.Center());
        }

        static void DrawToast(float W, float H, float u, Run run)
        {
            float a = Mathf.Clamp01(run.ToastTimer * 2f);
            var t = new GUIStyle(Centered);
            var tc = Color.Lerp(run.ToastColor, Color.white, 0.4f);
            tc.a = a;
            t.normal.textColor = tc;
            Shadowed(new Rect(0, H * 0.26f, W, 3.5f * u), run.ToastTitle, t);
            var d = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            d.normal.textColor = new Color(1f, 1f, 1f, a);
            Shadowed(new Rect(0, H * 0.26f + 3.3f * u, W, 2.5f * u), run.ToastText, d);
        }

        static void DrawBossBar(float W, float u, Enemy boss)
        {
            float bw = W * 0.4f, bh = 1.4f * u;
            var bar = new Rect((W - bw) / 2, 3.2f * u, bw, bh);
            Fill(new Rect(bar.x - 2, bar.y - 2, bar.width + 4, bar.height + 4), new Color(0, 0, 0, 0.75f));
            Fill(bar, new Color(0.2f, 0.02f, 0.05f, 0.9f));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(boss.HP / boss.MaxHP), bar.height), new Color(0.85f, 0.15f, 0.35f));
            Shadowed(new Rect(0, 0.8f * u, W, 2.2f * u), boss.Type.Name.ToUpper(), Label.Center());
        }

        // ---------------- spells ----------------

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
                    float k = ss.CooldownFraction(s);
                    Fill(new Rect(iconRect.x, iconRect.y, iconRect.width, iconRect.height * k), new Color(0, 0, 0, 0.65f));
                    Shadowed(iconRect, cd.ToString(cd < 10f ? "0.0" : "0"), Small.Center());
                }

                float tx = iconRect.xMax + 0.5f * u;
                SetAlpha(Small, alpha);
                Shadowed(new Rect(tx, row.y, width, 1.6f * u), s.Name, Small);
                SetAlpha(Small, 1f);

                // Arrow sequence: entered prefix lights up in the spell colour.
                for (int a = 0; a < s.Sequence.Length; a++)
                {
                    bool lit = candidate && filtering && a < ss.Entered.Count;
                    Color c = lit ? Color.Lerp(s.Color, Color.white, 0.35f) : new Color(0.85f, 0.85f, 0.85f);
                    c.a = alpha;
                    var r = new Rect(tx + a * (arrow + 0.15f * u), row.y + 1.75f * u, arrow, arrow);
                    DrawKey(r, s.Sequence[a], c, lit ? new Color(s.Color.r * 0.6f, s.Color.g * 0.6f, s.Color.b * 0.6f, 0.9f * alpha) : new Color(0f, 0f, 0f, 0.6f * alpha));
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
                    DrawKey(new Rect(x + i * (a + 0.3f * u), cy, a, a), ss.Entered[i], new Color(1f, 0.95f, 0.6f), new Color(0f, 0f, 0f, 0.65f));
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

        static GUIStyle keyStyle;

        /// <summary>A keycap showing the number key (1-4) that enters this arrow.</summary>
        public static void DrawKey(Rect r, Arrow arrow, Color textColor, Color bg)
        {
            Fill(r, bg);
            Frame(r, new Color(textColor.r, textColor.g, textColor.b, 0.35f * textColor.a), Mathf.Max(1f, r.height * 0.06f));
            keyStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            keyStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(r.height * 0.8f));
            keyStyle.normal.textColor = textColor;
            GUI.Label(r, Arrows.KeyNumber(arrow).ToString(), keyStyle);
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
