using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Practice tool (main menu): every spell unlocked, no cooldowns, no damage, three frozen training
    /// dummies that respawn. It picks a target spell, and after each attempt says which strokes it read
    /// and whether they made the target, so runes can be learned without a fight.
    /// </summary>
    public class PracticeTool : MonoBehaviour
    {
        public static PracticeTool Instance { get; private set; }
        public const int DummyCount = 3;

        public bool Active => GameState.PracticeMode && GameState.Instance && GameState.Instance.InRun;
        public SpellDefinition Target { get; private set; }
        public int Attempts { get; private set; }
        public int Successes { get; private set; }
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }
        public string Feedback { get; private set; }
        public Color FeedbackColor { get; private set; }
        public float FeedbackTimer { get; private set; }

        readonly List<Enemy> dummies = new List<Enemy>();
        float respawnTimer;
        bool subscribed;

        static readonly Vector3[] DummySpots = { new Vector3(-4f, 0f, 7f), new Vector3(0f, 0f, 9f), new Vector3(4f, 0f, 7f) };

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (subscribed && SpellSystem.Instance) SpellSystem.Instance.Event -= OnSpellEvent;
        }

        /// <summary>Called when practice starts.</summary>
        public void Begin()
        {
            if (!subscribed && SpellSystem.Instance) { SpellSystem.Instance.Event += OnSpellEvent; subscribed = true; }
            Attempts = Successes = Streak = BestStreak = 0;
            Target = null;
            NextTarget();
            Say("Draw the target rune: hold Right Mouse and move the mouse", new Color(0.8f, 0.85f, 1f), 4f);
            foreach (var e in dummies) if (e) e.TakeDamage(99999f);
            dummies.Clear();
            for (int i = 0; i < DummyCount; i++) SpawnDummy(i);
        }

        void Update()
        {
            if (!Active) return;
            FeedbackTimer = Mathf.Max(0f, FeedbackTimer - Time.deltaTime);
            if (GameState.Instance.AcceptsGameplayInput && Input.GetKeyDown(KeyCode.N)) NextTarget();

            // Dummies stay frozen (Shatter consumes freezes) and come back after being killed.
            for (int i = 0; i < dummies.Count; i++)
                if (dummies[i] && dummies[i].IsAlive && !dummies[i].IsFrozen) dummies[i].Freeze(30f);
            int alive = 0;
            foreach (var e in dummies) if (e && e.IsAlive) alive++;
            if (alive < DummyCount)
            {
                respawnTimer += Time.deltaTime;
                if (respawnTimer > 1.5f)
                {
                    respawnTimer = 0f;
                    for (int i = 0; i < DummyCount; i++)
                        if (i >= dummies.Count || !dummies[i] || !dummies[i].IsAlive) SpawnDummy(i);
                }
            }
        }

        void SpawnDummy(int i)
        {
            var room = Dungeon.Instance ? Dungeon.Instance.Current : null;
            if (room == null || !EnemySpawner.Instance) return;
            var pos = Dungeon.Instance.FindFreeSpot(room, room.Center + DummySpots[i % DummySpots.Length], 0.8f) + Vector3.up * 0.05f;
            var e = EnemySpawner.Instance.SpawnAt(EnemyType.Imp, pos);
            e.Freeze(30f);
            while (dummies.Count <= i) dummies.Add(null);
            dummies[i] = e;
        }

        /// <summary>Picks a different spell to practise (N key).</summary>
        public void NextTarget()
        {
            var book = SpellBook.Known;
            if (book.Count == 0) return;
            SpellDefinition next;
            int guard = 0;
            do next = book[Random.Range(0, book.Count)];
            while (book.Count > 1 && next == Target && guard++ < 20);
            Target = next;
        }

        void OnSpellEvent(string e)
        {
            if (!Active || Target == null) return;
            var ss = SpellSystem.Instance;
            if (e.StartsWith("ready "))
            {
                Attempts++;
                string name = e.Substring(6);
                if (name == Target.Name)
                {
                    Successes++;
                    Streak++;
                    BestStreak = Mathf.Max(BestStreak, Streak);
                    Say($"{name}!  ({Arrows.Glyphs(ss.LastStrokes)})", new Color(0.45f, 1f, 0.5f));
                    Sfx.Play("clear", 0.5f, 1.2f);
                    NextTarget();
                }
                else
                {
                    Streak = 0;
                    Say($"That was {name} ({Arrows.Glyphs(ss.LastStrokes)}). Target: {Target.Name}", new Color(1f, 0.85f, 0.4f));
                }
            }
            else if (e == "fizzle")
            {
                Attempts++;
                Streak = 0;
                Say($"Fizzle: read {Arrows.Glyphs(ss.LastStrokes)} - no spell starts like that", new Color(1f, 0.45f, 0.35f));
            }
        }

        void Say(string text, Color color, float seconds = 3f)
        {
            Feedback = text;
            FeedbackColor = color;
            FeedbackTimer = seconds;
        }

        // ---------------- drawing (from HUD) ----------------

        public void Draw(float W, float H, float u)
        {
            if (!Active) return;
            var panel = new Rect(u, 6f * u, 26f * u, 40f * u);
            HUD.Fill(panel, new Color(0f, 0f, 0f, 0.55f));
            HUD.Frame(panel, new Color(0.55f, 0.7f, 1f, 0.6f), Mathf.Max(2f, 0.12f * u));
            float x = panel.x + u, y = panel.y + 0.6f * u, w = panel.width - 2 * u;
            var title = new GUIStyle(HUD.Label);
            title.normal.textColor = new Color(0.7f, 0.85f, 1f);
            HUD.Shadowed(new Rect(x, y, w, 2.5f * u), "PRACTICE TOOL", title);
            y += 3f * u;

            if (Target != null)
            {
                HUD.Shadowed(new Rect(x, y, w, 2f * u), "Draw:", HUD.Small);
                var name = new GUIStyle(HUD.Label) { fontSize = Mathf.RoundToInt(HUD.Label.fontSize * 1.25f) };
                name.normal.textColor = Color.Lerp(Target.Color, Color.white, 0.3f);
                HUD.Shadowed(new Rect(x + 4f * u, y - 0.4f * u, w, 2.6f * u), Target.Name, name);
                y += 2.8f * u;
                GUI.DrawTexture(new Rect(x, y, 3.2f * u, 3.2f * u), Target.Icon, ScaleMode.ScaleToFit);
                HUD.Shadowed(new Rect(x + 4f * u, y + 0.6f * u, w, 2f * u), Arrows.Glyphs(Target.Sequence), HUD.Label);
                y += 4f * u;
                var ss = SpellSystem.Instance;
                int lit = ss && ss.Channeling && Arrows.HasPrefix(Target.Sequence, ss.Entered) ? ss.Entered.Count : 0;
                float g = 11f * u;
                HUD.DrawGlyph(Target.Sequence, new Rect(panel.center.x - g / 2, y, g, g), Target.Color, 1f, lit, Mathf.Max(3f, 0.45f * u));
                y += g + 1.5f * u;
            }

            HUD.Shadowed(new Rect(x, y, w, 2f * u), $"Hits {Successes} / {Attempts}     Streak {Streak}  (best {BestStreak})", HUD.Small);
            y += 2.4f * u;
            HUD.Shadowed(new Rect(x, y, w, 4f * u), "N: new target   Esc, Q: main menu\nNo cooldowns, no damage, dummies respawn", HUD.Small);
            y += 4.6f * u;

            // Result of the last attempt, inside the panel so the hands never cover it.
            if (FeedbackTimer > 0f)
            {
                var st = new GUIStyle(HUD.Label) { wordWrap = true, fontSize = Mathf.RoundToInt(HUD.Label.fontSize * 0.9f) };
                var c = FeedbackColor;
                c.a = Mathf.Clamp01(FeedbackTimer * 2f);
                st.normal.textColor = c;
                HUD.Shadowed(new Rect(x, y, w, panel.yMax - y - 0.5f * u), Feedback, st);
            }
        }
    }
}
