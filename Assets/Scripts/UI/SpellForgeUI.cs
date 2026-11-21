using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Spell Forge screen (Tab, between fights; the game is paused while it's open).
    /// Click spells (or press their number) to pick two: the top row shows A + B = result and what's
    /// still missing. Forge button / Enter learns the fusion. With one spell picked, Forget / Delete
    /// (pressed twice to confirm) removes it from the book and drops its rune at your feet.
    /// </summary>
    public class SpellForgeUI : MonoBehaviour
    {
        public static SpellForgeUI Instance { get; private set; }
        public bool IsOpen { get; private set; }

        /// <summary>You must keep at least this many spells (forging needs two).</summary>
        public const int MinSpells = 2;

        readonly List<int> picks = new List<int>();
        SpellDefinition preview;
        string previewError, message;
        float messageTimer;
        int forgetArmed = -1;

        // Clickable areas, laid out by Draw (GUI coordinates) and hit-tested in Update.
        readonly List<Rect> rowRects = new List<Rect>();
        readonly Rect[] slotRects = new Rect[2];
        Rect forgeButton, forgetButton;

        void Awake() => Instance = this;

        void Update()
        {
            messageTimer = Mathf.Max(0f, messageTimer - Time.unscaledDeltaTime);
            var gs = GameState.Instance;
            if (!gs.InRun || AutoTest.Active) { if (IsOpen && !AutoTest.Active) Close(); return; }

            if (Bindings.Pressed(GameAction.Forge) && !SpellSystem.Instance.Channeling)
            {
                if (IsOpen) Close();
                else if (GameState.TutorialMode)
                {
                    SpellSystem.Instance.Flash("Finish the tutorial first");
                    Sfx.Play("fizzle", 0.4f, 1.3f);
                }
                else if (Dungeon.Instance && Dungeon.Instance.InCombat)
                {
                    SpellSystem.Instance.Flash("Can't forge during a fight");
                    Sfx.Play("fizzle", 0.4f, 1.3f);
                }
                else Open();
                return;
            }
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            for (int i = 0; i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) Toggle(i);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.F)) Forge();
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)) Forget();

            if (Input.GetMouseButtonDown(0)) Click(MouseGui());
        }

        static string CdText(SpellDefinition s) { float c = CooldownConfig.BaseCooldown(s); return c > 0f ? $"{c:0.#}s" : "no cooldown"; }

        static Vector2 MouseGui() => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        /// <summary>Handles a click at a GUI position (also a test hook).</summary>
        public void Click(Vector2 p)
        {
            for (int i = 0; i < rowRects.Count; i++)
                if (rowRects[i].Contains(p)) { Toggle(i); return; }
            for (int i = 0; i < 2; i++)
                if (i < picks.Count && slotRects[i].Contains(p)) { Toggle(picks[i]); return; }
            if (forgeButton.Contains(p)) { Forge(); return; }
            if (forgetButton.Contains(p)) Forget();
        }

        /// <summary>Test hooks: where the clickable things were last drawn.</summary>
        public Rect RowRect(int i) => i < rowRects.Count ? rowRects[i] : default;
        public Rect ForgeButtonRect => forgeButton;
        public Rect ForgetButtonRect => forgetButton;
        public int PickCount => picks.Count;

        public void Open()
        {
            IsOpen = true;
            picks.Clear();
            forgetArmed = -1;
            Refresh();
            if (SpellSystem.Instance) SpellSystem.Instance.SetChanneling(false);
            if (!AutoTest.Active) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        /// <summary>Frame the Forge last closed on (so the same Esc doesn't also pause).</summary>
        public int ClosedFrame { get; private set; } = -10;

        public void Close()
        {
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            forgetArmed = -1;
            if (!AutoTest.Active && GameState.Instance && GameState.Instance.InRun)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void Toggle(int index)
        {
            if (index >= SpellBook.Known.Count) return;
            if (picks.Contains(index)) picks.Remove(index);
            else
            {
                if (picks.Count == 2) picks.RemoveAt(0);
                picks.Add(index);
            }
            forgetArmed = -1;
            Sfx.Play("tick", 0.6f, 1.1f + 0.1f * picks.Count);
            Refresh();
        }

        void Refresh()
        {
            preview = null;
            previewError = null;
            if (picks.Count < 2) return;
            var book = SpellBook.Known;
            SpellForge.TryCombine(book[picks[0]], book[picks[1]], book, out preview, out previewError);
        }

        /// <summary>What's still needed before Forge works (null when ready).</summary>
        public string Missing
        {
            get
            {
                if (preview != null) return null;
                if (picks.Count == 0) return "Pick two spells to fuse.";
                if (picks.Count == 1) return "Pick a second spell to fuse with it.";
                return previewError ?? "These two can't be fused.";
            }
        }

        /// <summary>Forges the current preview into the spell book. Returns the new spell (or null).</summary>
        public SpellDefinition Forge()
        {
            forgetArmed = -1;
            if (preview == null)
            {
                Say(Missing);
                Sfx.Play("fizzle", 0.5f);
                return null;
            }
            var made = preview;
            var run = Run.Instance;
            if (run && !run.CanAffordForge)
            {
                Say($"Not enough Essence: forging costs {run.ForgeCost} (you have {run.Essence}).");
                Sfx.Play("fizzle", 0.5f);
                return null;
            }
            if (Net.NetSession.IsClientOnly && Net.NetPlayers.Running)
            {
                // The host forges it into your book (it keeps everyone's) and sends the book back.
                Net.NetLoot.SendFuse(picks[0], picks[1]);
                Progress.Add("forged");
                Sfx.Play("forge", 0.8f);
                Say($"Forging {made.Name}...");
                picks.Clear();
                Refresh();
                return made;
            }
            if (run) run.PayForge(made);
            SpellBook.Known.Add(made);
            Progress.Add("forged");
            Sfx.Play("forge", 0.8f);
            Say($"Learned {made.Name}: rune {Arrows.Glyphs(made.Sequence)}");
            picks.Clear();
            Refresh();
            return made;
        }

        /// <summary>Why the picked spell can't be forgotten (null when it can).</summary>
        public string ForgetBlocked =>
            picks.Count != 1 ? "Pick one spell to forget it."
            : SpellBook.Known.Count <= MinSpells ? $"You must keep at least {MinSpells} spells."
            : null;

        /// <summary>First call arms, second call (same spell) forgets it. Returns the forgotten spell.</summary>
        public SpellDefinition Forget()
        {
            var blocked = ForgetBlocked;
            if (blocked != null) { Say(blocked); Sfx.Play("fizzle", 0.5f); forgetArmed = -1; return null; }
            int index = picks[0];
            var s = SpellBook.Known[index];
            if (forgetArmed != index)
            {
                forgetArmed = index;
                int back = EssenceConfig.Refund(s.EssencePaid);
                Say($"Forget {s.Name}? You get {back} Essence back. Click Forget (or Delete) again to confirm.");
                Sfx.Play("tick", 0.6f, 0.8f);
                return null;
            }
            forgetArmed = -1;
            if (Net.NetSession.IsClientOnly && Net.NetPlayers.Running)
            {
                Net.NetLoot.SendForget(index); // the host drops its rune at your feet
                Sfx.Play("portal", 0.5f, 1.4f);
                Say($"Forgot {s.Name}. Its rune lies at your feet if you change your mind.");
                picks.Clear();
                Refresh();
                return s;
            }
            SpellBook.Known.RemoveAt(index);
            if (SpellSystem.Instance) SpellSystem.Instance.OnForgotten(s);
            int refund = Run.Instance ? Run.Instance.RefundForget(s) : 0; // its rune remembers it: taking it back returns it
            var player = Player.Instance;
            if (player)
            {
                Vector3 at = player.transform.position + player.transform.forward * 1.3f;
                var room = Dungeon.Instance ? Dungeon.Instance.RoomAt(player.transform.position) : null;
                if (room != null && !room.Contains(at, 1f)) at = player.transform.position;
                Pickup.Spawn(Items.ForgottenRune(s), at);
            }
            Sfx.Play("portal", 0.5f, 1.4f);
            Say($"Forgot {s.Name}" + (refund > 0 ? $" (+{refund} Essence)" : "") + ". Its rune lies at your feet if you change your mind.");
            picks.Clear();
            Refresh();
            return s;
        }

        void Say(string m)
        {
            message = m;
            messageTimer = 3.5f;
        }

        /// <summary>Called by the HUD as its last draw step so the Forge is always on top.</summary>
        public void Draw()
        {
            if (!IsOpen) return;
            float W = Screen.width, H = Screen.height, u = H / 60f;
            Vector2 mouse = MouseGui();
            HUD.Fill(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.04f, 0.8f));

            var panel = new Rect(W * 0.08f, H * 0.08f, W * 0.84f, H * 0.84f);
            HUD.Frame(panel, new Color(0.8f, 0.55f, 0.2f, 0.9f), Mathf.Max(2f, u * 0.2f));
            HUD.Shadowed(new Rect(panel.x, panel.y + u, panel.width, 3 * u), "SPELL FORGE", HUD.Label.Center());
            HUD.Shadowed(new Rect(panel.x, panel.y + 3.5f * u, panel.width, 2 * u),
                "Click or press 1-9 to pick spells   Forge: Enter   Forget: Delete   Close: Tab / Esc", Faint().Center());

            // Left: known spells (clickable).
            var book = SpellBook.Known;
            rowRects.Clear();
            float x = panel.x + 2 * u, y = panel.y + 7 * u, rowH = 3.4f * u, listW = panel.width * 0.42f;
            for (int i = 0; i < book.Count; i++)
            {
                var s = book[i];
                int pick = picks.IndexOf(i);
                var row = new Rect(x, y, listW, rowH - 0.4f * u);
                rowRects.Add(row);
                bool hover = row.Contains(mouse);
                HUD.Fill(row, pick >= 0 ? new Color(s.Color.r, s.Color.g, s.Color.b, 0.45f) : new Color(1f, 1f, 1f, hover ? 0.14f : 0.06f));
                if (pick >= 0) HUD.Frame(row, Color.Lerp(s.Color, Color.white, 0.4f), Mathf.Max(2f, u * 0.15f));
                if (pick >= 0) HUD.Shadowed(new Rect(row.xMax - 3 * u, row.y, 3 * u, row.height), pick == 0 ? "A" : "B", HUD.Label.Center());
                GUI.DrawTexture(new Rect(row.x + 0.3f * u, row.y + 0.2f * u, rowH - 0.8f * u, rowH - 0.8f * u), s.Icon, ScaleMode.ScaleToFit);
                string area = s.AreaTag != null ? "   " + s.AreaTag : "";
                HUD.Shadowed(new Rect(row.x + rowH, row.y + 0.1f * u, row.width, 1.6f * u), $"{i + 1}. {s.Name}  ({CdText(s)}){area}", HUD.Small);
                HUD.Shadowed(new Rect(row.x + rowH, row.y + 1.5f * u, row.width - rowH - 3 * u, 1.6f * u), s.Describe(), Faint());
                y += rowH;
            }
            HUD.Shadowed(new Rect(x, y + 0.3f * u, listW, 2 * u),
                $"Spell book: {book.Count}/{SpellBook.MaxSpells}" + (book.Count >= SpellBook.MaxSpells ? "  (full: forget one to make room)" : ""), Faint());

            // Right: A + B = result.
            float px0 = panel.x + panel.width * 0.5f, pw = panel.width * 0.46f, py = panel.y + 7 * u;
            float slotW = pw * 0.3f, slotH = 7.5f * u, gap = (pw - slotW * 3f) / 2f;
            for (int i = 0; i < 2; i++)
            {
                var r = new Rect(px0 + i * (slotW + gap), py, slotW, slotH);
                slotRects[i] = r;
                DrawSlot(r, i < picks.Count ? book[picks[i]] : null, i == 0 ? "A" : "B", r.Contains(mouse) && i < picks.Count, u);
            }
            HUD.Shadowed(new Rect(px0 + slotW, py, gap, slotH), "+", HUD.Label.Center());
            HUD.Shadowed(new Rect(px0 + 2 * slotW + gap, py, gap, slotH), "=", HUD.Label.Center());
            DrawSlot(new Rect(px0 + 2 * (slotW + gap), py, slotW, slotH), preview, "?", false, u);

            // What's ready / missing.
            float sy = py + slotH + 0.8f * u;
            var status = new GUIStyle(HUD.Small) { wordWrap = true };
            status.normal.textColor = preview != null ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.8f, 0.35f);
            HUD.Shadowed(new Rect(px0, sy, pw, 2 * u), preview != null ? $"Ready: forge {preview.Name}" : Missing, status);

            float ly = sy + 2.6f * u;
            if (preview != null)
            {
                HUD.Fill(new Rect(px0, ly - 0.4f * u, pw, 14.5f * u), new Color(preview.Color.r, preview.Color.g, preview.Color.b, 0.12f));
                HUD.Shadowed(new Rect(px0 + u, ly, pw, 2 * u), $"Cooldown {CdText(preview)}   Tier {preview.Tier}   Rune {Arrows.Glyphs(preview.Sequence)}", HUD.Small);
                HUD.DrawGlyph(preview.Sequence, new Rect(px0 + pw - 6.5f * u, ly, 5.5f * u, 5.5f * u), preview.Color, 1f, preview.Sequence.Length, Mathf.Max(2f, 0.25f * u));
                ly += 2 * u;
                HUD.Shadowed(new Rect(px0 + u, ly, pw, 2 * u), "Delivery: " + preview.Delivery.Name + (preview.AreaTag != null ? "   " + preview.AreaTag : ""), HUD.Small);
                ly += 2 * u;
                foreach (var e in preview.Payload)
                {
                    HUD.Shadowed(new Rect(px0 + 2 * u, ly, pw, 2 * u), "- " + e.Describe(), HUD.Small);
                    ly += 1.7f * u;
                }
                var syn = new List<string>();
                foreach (var r in SpellSynergies.Rules) if (preview.Synergies.Contains(r.Name)) syn.Add(r.Name);
                if (syn.Count > 0)
                {
                    var st = new GUIStyle(HUD.Small);
                    st.normal.textColor = new Color(1f, 0.8f, 0.3f);
                    HUD.Shadowed(new Rect(px0 + u, ly + 0.5f * u, pw, 2 * u), "Synergy: " + string.Join(", ", syn), st);
                }
            }
            else if (picks.Count < 2)
            {
                HUD.Shadowed(new Rect(px0, ly, pw, 8 * u),
                    "The first spell's delivery carries both payloads.\nReacting elements unlock synergies:\n" +
                    "Fire+Frost = Shatter   Fire+Storm = Ignite\nFrost+Storm = Thunderclap   Life+damage = Lifesteal", Faint());
            }

            // Buttons.
            float by = panel.yMax - 7.5f * u, bw = pw * 0.48f, bh = 3.4f * u;
            forgeButton = new Rect(px0, by, bw, bh);
            forgetButton = new Rect(px0 + pw - bw, by, bw, bh);
            var runE = Run.Instance;
            bool afford = !runE || runE.CanAffordForge;
            DrawCost(new Rect(px0, by - 2.6f * u, pw, 2.2f * u), runE, afford, u);
            Button(forgeButton, preview == null ? "FORGE (pick two)" : afford ? "FORGE" : "FORGE (need Essence)", preview != null && afford, new Color(0.95f, 0.6f, 0.2f), mouse, u);
            bool canForget = ForgetBlocked == null;
            string forgetLabel = !canForget ? "FORGET (pick one)"
                : forgetArmed == picks[0] ? $"CONFIRM (+{EssenceConfig.Refund(book[picks[0]].EssencePaid)} ESSENCE)"
                : $"FORGET {book[picks[0]].Name.ToUpperInvariant()}";
            Button(forgetButton, forgetLabel, canForget, new Color(0.85f, 0.3f, 0.35f), mouse, u);

            if (messageTimer > 0f)
                HUD.Shadowed(new Rect(panel.x, panel.yMax - 3 * u, panel.width, 2 * u), message, HUD.Label.Center());
        }

        /// <summary>"Cost 5 Essence   You have 7   Next: 7" (the cost in red with "Not enough Essence" if short).</summary>
        static void DrawCost(Rect r, Run run, bool afford, float u)
        {
            if (!run) return;
            var icon = Items.Essence.Icon;
            GUI.DrawTexture(new Rect(r.x, r.y + 0.1f * u, 2f * u, 2f * u), icon, ScaleMode.ScaleToFit);
            // One style per part (rich-text colours would also tint the shadow).
            string cost = Essence.Free ? "Forge cost: free (Practice)"
                : afford ? $"Forge cost: {run.ForgeCost} Essence" : $"Forge cost: {run.ForgeCost} Essence - Not enough Essence";
            string have = $"You have: {run.Essence}" + (Essence.Free ? "" : $"   Next: {run.NextForgeCost}");
            var cs = new GUIStyle(HUD.Small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            cs.normal.textColor = afford ? new Color(0.85f, 0.8f, 1f) : new Color(1f, 0.35f, 0.3f);
            var hs = new GUIStyle(HUD.Small) { alignment = TextAnchor.MiddleLeft };
            hs.normal.textColor = new Color(0.8f, 0.8f, 0.85f);
            float cx = r.x + 2.5f * u;
            HUD.Shadowed(new Rect(cx, r.y, r.width - 2.5f * u, r.height), cost, cs);
            cx += cs.CalcSize(new GUIContent(cost)).x + 1.5f * u;
            HUD.Shadowed(new Rect(cx, r.y, r.xMax - cx, r.height), have, hs);
        }

        static void DrawSlot(Rect r, SpellDefinition s, string letter, bool hover, float u)
        {
            if (s == null)
            {
                HUD.Fill(r, new Color(1f, 1f, 1f, 0.04f));
                HUD.Frame(r, new Color(1f, 1f, 1f, 0.25f), Mathf.Max(1f, u * 0.1f));
                HUD.Shadowed(r, letter, Faint().Center());
                return;
            }
            HUD.Fill(r, new Color(s.Color.r, s.Color.g, s.Color.b, hover ? 0.4f : 0.25f));
            HUD.Frame(r, Color.Lerp(s.Color, Color.white, 0.4f), Mathf.Max(2f, u * 0.15f));
            float ic = r.height - 3f * u;
            var iconRect = new Rect(r.center.x - ic * 0.5f, r.y + 0.4f * u, ic, ic);
            GUI.DrawTexture(iconRect, s.Icon, ScaleMode.ScaleToFit);
            if (s.SecondaryIcon) GUI.DrawTexture(new Rect(iconRect.xMax - ic * 0.45f, iconRect.yMax - ic * 0.45f, ic * 0.45f, ic * 0.45f), s.SecondaryIcon);
            var name = new GUIStyle(HUD.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = true, clipping = TextClipping.Clip };
            HUD.Shadowed(new Rect(r.x + 0.2f * u, r.yMax - 2.6f * u, r.width - 0.4f * u, 2.4f * u), s.Name, name);
        }

        static void Button(Rect r, string label, bool enabled, Color c, Vector2 mouse, float u)
        {
            bool hover = enabled && r.Contains(mouse);
            HUD.Fill(r, enabled ? new Color(c.r, c.g, c.b, hover ? 0.75f : 0.5f) : new Color(0.3f, 0.3f, 0.35f, 0.35f));
            HUD.Frame(r, enabled ? Color.Lerp(c, Color.white, 0.5f) : new Color(0.5f, 0.5f, 0.55f, 0.6f), Mathf.Max(2f, u * 0.15f));
            var st = new GUIStyle(enabled ? HUD.Label : HUD.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
            if (!enabled) st.normal.textColor = new Color(0.65f, 0.65f, 0.7f);
            HUD.Shadowed(r, label, st);
        }

        static GUIStyle faint;
        static GUIStyle Faint()
        {
            if (faint == null || faint.fontSize != HUD.Small.fontSize)
            {
                faint = new GUIStyle(HUD.Small);
                faint.normal.textColor = new Color(0.75f, 0.75f, 0.8f);
            }
            return faint;
        }
    }
}
