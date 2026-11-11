using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Spell Forge screen (Tab). Pick two known spells with the number keys, see a preview of the fused
    /// spell, press Enter to learn it. The game is paused while the Forge is open.
    /// </summary>
    public class SpellForgeUI : MonoBehaviour
    {
        public static SpellForgeUI Instance { get; private set; }
        public bool IsOpen { get; private set; }

        readonly List<int> picks = new List<int>();
        SpellDefinition preview;
        string previewError, message;
        float messageTimer;

        void Awake() => Instance = this;

        void Update()
        {
            messageTimer = Mathf.Max(0f, messageTimer - Time.unscaledDeltaTime);
            var gs = GameState.Instance;
            if (!gs.InRun || AutoTest.Active) { if (IsOpen && !AutoTest.Active) Close(); return; }

            if (Input.GetKeyDown(KeyCode.Tab) && !SpellSystem.Instance.Channeling)
            {
                if (IsOpen) Close();
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
        }

        public void Open()
        {
            IsOpen = true;
            picks.Clear();
            Refresh();
            if (SpellSystem.Instance) SpellSystem.Instance.SetChanneling(false);
        }

        public void Close() => IsOpen = false;

        public void Toggle(int index)
        {
            if (index >= SpellBook.Known.Count) return;
            if (picks.Contains(index)) picks.Remove(index);
            else
            {
                if (picks.Count == 2) picks.RemoveAt(0);
                picks.Add(index);
            }
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

        /// <summary>Forges the current preview into the spell book. Returns the new spell (or null).</summary>
        public SpellDefinition Forge()
        {
            if (preview == null)
            {
                Say(previewError ?? "Pick two spells first (number keys).");
                Sfx.Play("fizzle", 0.5f);
                return null;
            }
            var made = preview;
            SpellBook.Known.Add(made);
            Sfx.Play("forge", 0.8f);
            Say($"Learned {made.Name}: rune {Arrows.Glyphs(made.Sequence)}");
            picks.Clear();
            Refresh();
            return made;
        }

        void Say(string m)
        {
            message = m;
            messageTimer = 3f;
        }

        /// <summary>Called by the HUD as its last draw step so the Forge is always on top.</summary>
        public void Draw()
        {
            if (!IsOpen) return;
            float W = Screen.width, H = Screen.height, u = H / 60f;
            HUD.Fill(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.04f, 0.8f));

            var panel = new Rect(W * 0.08f, H * 0.08f, W * 0.84f, H * 0.84f);
            HUD.Frame(panel, new Color(0.8f, 0.55f, 0.2f, 0.9f), Mathf.Max(2f, u * 0.2f));
            HUD.Shadowed(new Rect(panel.x, panel.y + u, panel.width, 3 * u), "SPELL FORGE", HUD.Label.Center());
            HUD.Shadowed(new Rect(panel.x, panel.y + 3.5f * u, panel.width, 2 * u),
                "Number keys: pick two spells   Enter: forge   Tab / Esc: close", HUD.Small.Center());

            // Left: known spells.
            var book = SpellBook.Known;
            float x = panel.x + 2 * u, y = panel.y + 7 * u, rowH = 3.4f * u;
            for (int i = 0; i < book.Count; i++)
            {
                var s = book[i];
                int pick = picks.IndexOf(i);
                var row = new Rect(x, y, panel.width * 0.42f, rowH - 0.4f * u);
                HUD.Fill(row, pick >= 0 ? new Color(s.Color.r, s.Color.g, s.Color.b, 0.4f) : new Color(1f, 1f, 1f, 0.06f));
                if (pick >= 0) HUD.Shadowed(new Rect(row.xMax - 3 * u, row.y, 3 * u, row.height), pick == 0 ? "A" : "B", HUD.Label.Center());
                GUI.DrawTexture(new Rect(row.x + 0.3f * u, row.y + 0.2f * u, rowH - 0.8f * u, rowH - 0.8f * u), s.Icon, ScaleMode.ScaleToFit);
                HUD.Shadowed(new Rect(row.x + rowH, row.y + 0.1f * u, row.width, 1.6f * u), $"{i + 1}. {s.Name}  ({s.Cooldown:0.#}s)", HUD.Small);
                HUD.Shadowed(new Rect(row.x + rowH, row.y + 1.5f * u, row.width, 1.6f * u), s.Describe(), Faint());
                y += rowH;
            }
            if (book.Count >= SpellBook.MaxSpells)
                HUD.Shadowed(new Rect(x, y, panel.width * 0.42f, 2 * u), "Spell book full", HUD.Small);

            // Right: preview.
            float px0 = panel.x + panel.width * 0.5f, pw = panel.width * 0.46f, py = panel.y + 7 * u;
            if (preview != null)
            {
                HUD.Fill(new Rect(px0, py, pw, panel.height - 9 * u), new Color(preview.Color.r, preview.Color.g, preview.Color.b, 0.12f));
                float ic = 6 * u;
                GUI.DrawTexture(new Rect(px0 + u, py + u, ic, ic), preview.Icon, ScaleMode.ScaleToFit);
                if (preview.SecondaryIcon) GUI.DrawTexture(new Rect(px0 + u + ic * 0.55f, py + u + ic * 0.55f, ic * 0.45f, ic * 0.45f), preview.SecondaryIcon);
                var nameStyle = new GUIStyle(HUD.Label);
                nameStyle.normal.textColor = Color.Lerp(preview.Color, Color.white, 0.3f);
                HUD.Shadowed(new Rect(px0 + ic + 2 * u, py + u, pw, 2.5f * u), preview.Name, nameStyle);
                HUD.Shadowed(new Rect(px0 + ic + 2 * u, py + 3.3f * u, pw, 2 * u), $"Cooldown {preview.Cooldown:0.#}s   Tier {preview.Tier}", HUD.Small);
                float ax = px0 + ic + 2 * u;
                HUD.Shadowed(new Rect(ax, py + 5.2f * u, pw, 2 * u), "Rune " + Arrows.Glyphs(preview.Sequence), HUD.Small);
                HUD.DrawGlyph(preview.Sequence, new Rect(px0 + pw - 7.5f * u, py + u, 6f * u, 6f * u), preview.Color, 1f, preview.Sequence.Length, Mathf.Max(2f, 0.25f * u));

                float ly = py + 9 * u;
                HUD.Shadowed(new Rect(px0 + u, ly, pw, 2 * u), "Delivery: " + preview.Delivery.Name, HUD.Small);
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
            else
            {
                HUD.Shadowed(new Rect(px0, py, pw, 6 * u),
                    previewError ?? "Pick two spells to preview the fusion.\nThe first spell's delivery carries both payloads;\nreacting elements unlock synergies:\n" +
                    "Fire+Frost = Shatter   Fire+Storm = Ignite\nFrost+Storm = Thunderclap   Life+damage = Lifesteal", HUD.Small);
            }

            if (messageTimer > 0f)
                HUD.Shadowed(new Rect(panel.x, panel.yMax - 3 * u, panel.width, 2 * u), message, HUD.Label.Center());
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
