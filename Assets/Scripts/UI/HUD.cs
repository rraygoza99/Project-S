using System.Collections.Generic;
using System.Linq;
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
            var gs0 = GameState.Instance;
            if (gs0 && gs0.Phase == Phase.Title && MainMenu.Instance)
            {
                // The menu needs mouse/keyboard events, not just repaints.
                EnsureStyles();
                if (Event.current.type == EventType.Repaint && RetroView.RT)
                    GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), RetroView.RT, ScaleMode.StretchToFill, false);
                MainMenu.Instance.DrawGUI(Screen.width, Screen.height);
                return;
            }
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

            if (WizardHands.Instance) WizardHands.Instance.Draw(px);

            if (player)
            {
                if (player.HurtFlash > 0f) Fill(new Rect(0, 0, W, H), new Color(0.8f, 0f, 0f, 0.35f * player.HurtFlash));
                if (player.HealFlash > 0f) Fill(new Rect(0, 0, W, H), new Color(0.2f, 1f, 0.3f, 0.25f * player.HealFlash));
            }
            if (spells && spells.FizzleFlash > 0f) Fill(new Rect(0, 0, W, H), new Color(1f, 0.3f, 0.1f, 0.12f * spells.FizzleFlash));

            if (!spells || !spells.Channeling) DrawCrosshair(W, H, px); // while drawing, the rune's pen is the focus
            if (player) DrawHealth(H, px, u, player, run);
            if (run) DrawSlots(H, px, u, run);
            bool practice = PracticeTool.Instance && PracticeTool.Instance.Active;
            if (dungeon && !practice) DrawMinimap(u, dungeon, run);
            if (practice) PracticeTool.Instance.Draw(W, H, u);
            if (Tutorial.Instance && Tutorial.Instance.Active) Tutorial.Instance.Draw(W, H, u);
            if (dungeon && player) DrawWorldLabels(W, H, u, player);
            if (dungeon && dungeon.Boss && dungeon.Boss.IsAlive) DrawBossBar(W, u, dungeon.Boss);

            float pad = 4f * px;
            if (Settings.ShowFps) Shadowed(new Rect(0, H - pad - 1.6f * u, W - pad, 2 * u), $"{fps:0} FPS", Small.Right());

            if (spells)
            {
                DrawSpellPanel(W, H, u, spells);
                DrawCastingInput(W, H, u, spells);
            }

            if (player && gs.InRun) DrawInteractPrompt(W, H, u, player);
            if (run && run.ToastTimer > 0f) DrawToast(W, H, u, run);

            Shadowed(new Rect(0, H - pad - 1.6f * u, W, 2 * u),
                $"Hold {Bindings.Short(GameAction.Channel)} + draw: rune   {Bindings.Short(GameAction.Cast)}: cast   " +
                $"{Bindings.Short(GameAction.Item1)}-{Bindings.Short(GameAction.Item3)}: items   {Bindings.Short(GameAction.Interact)}: take   " +
                $"{Bindings.Short(GameAction.Map)}: map   {Bindings.Short(GameAction.Forge)}: Forge", Small.Center());

            if (gs.BannerTimer > 0f)
            {
                var c = Title.normal.textColor;
                Title.normal.textColor = new Color(1f, 0.85f, 0.4f, Mathf.Clamp01(gs.BannerTimer * 2f));
                Shadowed(new Rect(0, H * 0.12f, W, 6 * u), gs.Banner, Title);
                Title.normal.textColor = c;
            }

            bool showMap = ShowMap || (gs.InRun && !gs.Paused && !gs.ForgeOpen && Bindings.Held(GameAction.Map));
            if (showMap && dungeon) DrawBigMap(W, H, u, dungeon, run);

            if (SpellForgeUI.Instance) SpellForgeUI.Instance.Draw();

            if (gs.Phase == Phase.Dead) DrawEnd(W, H, u, gs, false);
            else if (gs.Phase == Phase.Victory) DrawEnd(W, H, u, gs, true);
            else if (gs.Paused)
            {
                Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.5f));
                Shadowed(new Rect(0, 0, W, H), "Paused - click to resume\nQ: main menu", Centered);
            }
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
            if (Progress.NewThisRun.Count > 0)
            {
                var ns = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                ns.normal.textColor = new Color(1f, 0.85f, 0.4f);
                Shadowed(new Rect(W * 0.1f, H * 0.555f, W * 0.8f, 4 * u),
                    "New unlocks: " + string.Join(", ", Progress.NewThisRun.Select(a => a.RewardName)), ns);
            }
            Shadowed(new Rect(0, H * 0.64f, W, 4 * u), "R: new run      Esc: main menu", Centered);
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
            DrawPlayerMarker(center, cell, origin);
        }

        static readonly Color MarkerColor = new Color(1f, 0.25f, 0.2f);

        /// <summary>
        /// The player on the map: a triangle at their position, its tip pointing where they look. Map up is world +Z
        /// (north), the same direction as "the room above", so turning to face up and walking forward
        /// leads to the room above.
        /// </summary>
        static void DrawPlayerMarker(Vector2 center, float cell, Vector2Int origin)
        {
            var player = Player.Instance;
            if (!player) return;
            Vector3 p = player.transform.position;
            Vector2 pos = center + new Vector2(p.x / Dungeon.Spacing - origin.x, -(p.z / Dungeon.Spacing - origin.y)) * cell;
            float size = Mathf.Max(11f, cell * 0.6f);
            var r = new Rect(pos.x - size / 2, pos.y - size / 2, size, size);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(player.Yaw, pos); // yaw 0 = north (map up), clockwise like the screen
            DrawTinted(r, PixelArt.TriangleTex, MarkerColor);
            GUI.matrix = m;
        }

        static void DrawMinimap(float u, Dungeon d, Run run)
        {
            float cell = 1.9f * u;
            const int range = 3;
            var area = new Rect(u, u, cell * (range * 2 + 1), cell * (range * 2 + 1));
            Fill(area, new Color(0, 0, 0, 0.45f));
            if (d.Current != null) DrawRooms(area, cell, d.Current.Cell, d, range);
            var north = new GUIStyle(Small) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            north.normal.textColor = new Color(1f, 0.85f, 0.45f, 0.9f);
            Shadowed(new Rect(area.x, area.y, area.width, 2f * u), "N", north);
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
            float rowH = 3.6f * u, icon = 3f * u, width = 17f * u;
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
                Shadowed(new Rect(tx, row.y + 0.2f * u, width, 1.6f * u), s.Name, Small);
                SetAlpha(Small, 1f);
                var seqStyle = new GUIStyle(Small);
                seqStyle.normal.textColor = new Color(0.8f, 0.8f, 0.85f, 0.8f * alpha);
                Shadowed(new Rect(tx, row.y + 1.7f * u, width, 1.6f * u), Arrows.Glyphs(s.Sequence), seqStyle);

                // The rune's shape; strokes already drawn light up in the spell colour.
                int lit = candidate && filtering ? ss.Entered.Count : 0;
                var glyphBox = new Rect(row.xMax - rowH + 0.1f * u, row.y + 0.35f * u, rowH - 0.9f * u, rowH - 0.9f * u);
                DrawGlyph(s.Sequence, glyphBox, s.Color, alpha, lit, Mathf.Max(1.5f, u * 0.16f));
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
            DrawRuneCanvas(W, H, u, ss);
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

        // ---------------- runes ----------------

        static readonly Color PenColor = new Color(0.75f, 0.88f, 1f);

        /// <summary>
        /// The rune being drawn, glowing in front of the wizard: a faint magic circle, the raw pen trail in
        /// the colour of the spell it is heading for, and a short afterglow when it readies or fizzles.
        /// </summary>
        void DrawRuneCanvas(float W, float H, float u, SpellSystem ss)
        {
            var center = new Vector2(W * 0.5f, H * 0.47f);
            float size = 22f * u;
            var box = new Rect(center.x - size / 2, center.y - size / 2, size, size);

            if (ss.EchoTimer > 0f && ss.EchoTrail.Count > 1)
            {
                float k = ss.EchoTimer / SpellSystem.EchoDuration;
                var c = ss.EchoColor;
                float grow = 1f + (1f - k) * 0.25f;
                var eb = new Rect(center.x - size * grow / 2, center.y - size * grow / 2, size * grow, size * grow);
                DrawPolyline(ss.EchoTrail, eb, 1.1f * u * grow, Mathf.Max(2f, 0.45f * u), new Color(c.r, c.g, c.b, k), true);
            }
            if (!ss.Channeling) return;

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
            DrawTinted(box, PixelArt.RingTex, new Color(0.55f, 0.7f, 1f, 0.12f + 0.06f * pulse));

            Color col = PenColor;
            if (ss.Entered.Count > 0)
                foreach (var s in ss.Book)
                    if (ss.IsCandidate(s)) { col = Color.Lerp(s.Color, Color.white, 0.25f); break; }
            var trail = ss.Gesture.Trail;
            if (trail.Count > 1) DrawPolyline(trail, box, 1.1f * u, Mathf.Max(2f, 0.45f * u), col, true);
            else Shadowed(new Rect(0, box.yMax - 2f * u, W, 2f * u), ss.Lockout > 0f ? "..." : "draw a rune", Small.Center());

            // pen tip
            var tip = Fit(trail, box, 1.1f * u, trail[trail.Count - 1]);
            float t = Mathf.Max(4f, 0.9f * u);
            Fill(new Rect(tip.x - t, tip.y - t, 2 * t, 2 * t), new Color(col.r, col.g, col.b, 0.25f));
            Fill(new Rect(tip.x - t / 2, tip.y - t / 2, t, t), Color.white);
        }

        /// <summary>Draws a spell's ideal rune shape in a box; the first `lit` strokes in the spell colour.</summary>
        public static void DrawGlyph(IList<Arrow> seq, Rect box, Color spellColor, float alpha, int lit, float width)
        {
            var pts = GestureRecognizer.Glyph(seq);
            var bounds = Bounds(pts);
            float span = Mathf.Max(bounds.width, bounds.height, 1f);
            float scale = Mathf.Min(box.width, box.height) / span;
            Vector2 mid = bounds.center;
            Vector2 P(Vector2 p) => new Vector2(box.center.x + (p.x - mid.x) * scale, box.center.y - (p.y - mid.y) * scale);

            // count drawn strokes along the polyline (hairpin steps don't count as strokes)
            int stroke = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                bool isStroke = (pts[i] - pts[i - 1]).sqrMagnitude > 0.5f;
                bool on = stroke < lit;
                Color c = on ? Color.Lerp(spellColor, Color.white, 0.4f) : new Color(0.8f, 0.8f, 0.85f);
                c.a = alpha * (on ? 1f : 0.75f);
                Line(P(pts[i - 1]), P(pts[i]), width, c);
                if (isStroke) stroke++;
            }
            var s = P(pts[0]);
            float d = width * 1.6f;
            Fill(new Rect(s.x - d, s.y - d, 2 * d, 2 * d), new Color(1f, 1f, 1f, alpha)); // start dot
        }

        static Rect Bounds(List<Vector2> pts)
        {
            Vector2 min = pts[0], max = pts[0];
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Maps a pen point (y up) into the box, scaling the whole trail to fit (at most maxScale px per unit).</summary>
        static Vector2 Fit(List<Vector2> pts, Rect box, float maxScale, Vector2 p)
        {
            var b = Bounds(pts);
            float scale = Mathf.Min(maxScale, box.width / Mathf.Max(b.width, 1e-3f), box.height / Mathf.Max(b.height, 1e-3f));
            return new Vector2(box.center.x + (p.x - b.center.x) * scale, box.center.y - (p.y - b.center.y) * scale);
        }

        public static void DrawPolyline(List<Vector2> pts, Rect box, float maxScale, float width, Color c, bool glow)
        {
            var b = Bounds(pts);
            float scale = Mathf.Min(maxScale, box.width / Mathf.Max(b.width, 1e-3f), box.height / Mathf.Max(b.height, 1e-3f));
            Vector2 P(Vector2 p) => new Vector2(box.center.x + (p.x - b.center.x) * scale, box.center.y - (p.y - b.center.y) * scale);
            if (glow)
                for (int i = 1; i < pts.Count; i++) Line(P(pts[i - 1]), P(pts[i]), width * 3.2f, new Color(c.r, c.g, c.b, c.a * 0.22f));
            var core = Color.Lerp(c, Color.white, 0.55f);
            core.a = c.a;
            for (int i = 1; i < pts.Count; i++) Line(P(pts[i - 1]), P(pts[i]), width, core);
        }

        /// <summary>A straight line of the given thickness (IMGUI has no line primitive, so a rotated quad).</summary>
        public static void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x - width * 0.5f, a.y - width * 0.5f, len + width, width), c);
            GUI.matrix = m;
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
