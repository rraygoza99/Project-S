using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// First-run tutorial in the start room (doors locked): walk, shoot the magic bolt, start drawing,
    /// draw Fireball, cast it, then a tips card. Runs automatically on the first "Start game" and from
    /// the menu's Tutorial entry; Enter skips it. Key names follow the player's bindings.
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        public static Tutorial Instance { get; private set; }

        public enum Step { Move, Bolt, Channel, Fireball, Cast, Tips, Done }
        public Step Current { get; private set; } = Step.Done;
        public bool Active => GameState.TutorialMode && GameState.Instance && GameState.Instance.InRun;
        const int VisibleSteps = 5;

        Vector3 lastPos;
        float moved, castTimer;
        Enemy dummy;
        readonly List<Enemy> group = new List<Enemy>();
        string hint;
        float hintTimer;
        bool subscribed;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (subscribed && SpellSystem.Instance) SpellSystem.Instance.Event -= OnSpellEvent;
        }

        public void Begin()
        {
            if (!subscribed && SpellSystem.Instance) { SpellSystem.Instance.Event += OnSpellEvent; subscribed = true; }
            var d = Dungeon.Instance;
            if (d && d.Current != null) d.SetGates(d.Current, true);
            moved = 0f;
            lastPos = Player.Instance ? Player.Instance.transform.position : Vector3.zero;
            Current = Step.Move;
        }

        void Update()
        {
            if (!Active) return;
            hintTimer = Mathf.Max(0f, hintTimer - Time.deltaTime);
            if (GameState.Instance.AcceptsGameplayInput && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                Finish();
                return;
            }
            var player = Player.Instance;
            var ss = SpellSystem.Instance;
            switch (Current)
            {
                case Step.Move:
                    Vector3 delta = player.transform.position - lastPos;
                    delta.y = 0f;
                    moved += delta.magnitude;
                    lastPos = player.transform.position;
                    if (moved > 5f) Next(Step.Bolt);
                    break;
                case Step.Bolt:
                    if (!dummy || !dummy.IsAlive) Next(Step.Channel);
                    else if (!dummy.IsFrozen) dummy.Freeze(30f);
                    break;
                case Step.Channel:
                    if (ss.Channeling && (ss.Gesture.Trail.Count > 6 || ss.Entered.Count > 0)) Next(Step.Fireball);
                    break;
                case Step.Cast:
                    foreach (var e in group) if (e && e.IsAlive && !e.IsFrozen) e.Freeze(30f);
                    if (castTimer > 0f && (castTimer -= Time.deltaTime) <= 0f) { Next(Step.Tips); break; }
                    if (castTimer <= 0f && ss.Readied == null && !ss.Channeling && castTimer <= 0f && hintTimer <= 0f)
                        Hint($"Fireball isn't ready: hold {Bindings.Short(GameAction.Channel)} and draw it again");
                    break;
            }
        }

        void OnSpellEvent(string e)
        {
            if (!Active) return;
            if (Current == Step.Fireball && e == "ready Fireball") Next(Step.Cast);
            else if (Current == Step.Fireball && e == "fizzle")
                Hint("Fizzled! Let go and try again: three clear strokes, down, right, up. (Options: rune drawing size)");
            else if (Current == Step.Cast && e == "cast Fireball" && castTimer <= 0f) castTimer = 1.4f;
        }

        void Next(Step s)
        {
            Current = s;
            hintTimer = 0f;
            Sfx.Play("clear", 0.45f, 1.3f);
            var d = Dungeon.Instance;
            var room = d ? d.Current : null;
            var player = Player.Instance;
            switch (s)
            {
                case Step.Bolt:
                    if (room != null) dummy = Spawn(room, player.transform.position + player.transform.forward * 7f);
                    break;
                case Step.Cast:
                    group.Clear();
                    if (room != null)
                    {
                        Vector3 c = player.transform.position + player.transform.forward * 9f;
                        foreach (var off in new[] { Vector3.zero, new Vector3(1.2f, 0, 0.8f), new Vector3(-1.2f, 0, 0.8f) })
                            group.Add(Spawn(room, c + off));
                    }
                    break;
            }
        }

        static Enemy Spawn(Room room, Vector3 at)
        {
            var d = Dungeon.Instance;
            at.x = Mathf.Clamp(at.x, room.Center.x - 10f, room.Center.x + 10f);
            at.z = Mathf.Clamp(at.z, room.Center.z - 10f, room.Center.z + 10f);
            var pos = d.FindFreeSpot(room, new Vector3(at.x, 0f, at.z), 0.8f) + Vector3.up * 0.05f;
            var e = EnemySpawner.Instance.SpawnAt(EnemyType.Imp, pos);
            e.Freeze(30f);
            return e;
        }

        void Hint(string text)
        {
            hint = text;
            hintTimer = 3.5f;
        }

        /// <summary>Ends the tutorial (finished or skipped): clears the dummies and opens the doors.</summary>
        public void Finish()
        {
            if (Current == Step.Done) return;
            if (dummy) Destroy(dummy.gameObject);
            foreach (var e in group) if (e) Destroy(e.gameObject);
            group.Clear();
            Current = Step.Done;
            GameState.Instance.EndTutorial();
            Settings.SetTutorialDone(true);
            var d = Dungeon.Instance;
            if (d && d.Current != null) d.SetGates(d.Current, false);
            if (SpellSystem.Instance) SpellSystem.Instance.ResetCooldowns();
            GameState.Instance.Show("Good luck!", 2f);
            Sfx.Play("door", 0.7f);
        }

        /// <summary>Test hook: what the current step asks for.</summary>
        public string Instruction => Text(Current).title;

        (string title, string body) Text(Step s)
        {
            string ch = Bindings.Short(GameAction.Channel), cast = Bindings.Short(GameAction.Cast);
            string move = Bindings.KeyName(Bindings.Get(GameAction.MoveForward, 0)) + Bindings.KeyName(Bindings.Get(GameAction.MoveLeft, 0))
                + Bindings.KeyName(Bindings.Get(GameAction.MoveBack, 0)) + Bindings.KeyName(Bindings.Get(GameAction.MoveRight, 0));
            return s switch
            {
                Step.Move => ("Walk around", $"Move with {move} and look with the mouse."),
                Step.Bolt => ("Shoot the dummy", $"{cast} fires your magic bolt. Hold it to keep firing."),
                Step.Channel => ("Start a rune", $"Hold {ch}: the view stops and your mouse draws a glowing rune.\nMove the mouse while holding it."),
                Step.Fireball => ("Draw Fireball", $"Hold {ch} and draw a U: down, right, up.\nEach straight stroke counts once. The spell panel on the right shows every rune."),
                Step.Cast => ("Cast it", $"Fireball is ready (your hands glow). Let go of {ch}, aim at the imps and press {cast}."),
                _ => ("You're ready", $"Clear each room to open its doors. Find runes for new spells and relics that change them.\n" +
                    $"{Bindings.Short(GameAction.Item1)}-{Bindings.Short(GameAction.Item3)}: items   {Bindings.Short(GameAction.Interact)}: take / buy   " +
                    $"hold {Bindings.Short(GameAction.Map)}: map   {Bindings.Short(GameAction.Forge)}: Spell Forge (fuse two spells)\n\nPress Enter to begin the run."),
            };
        }

        // ---------------- drawing (from HUD) ----------------

        public void Draw(float W, float H, float u)
        {
            if (!Active || Current == Step.Done) return;
            var (title, body) = Text(Current);
            float w = Mathf.Min(W * 0.62f, 70f * u);
            bool fireball = Current == Step.Fireball;
            var panel = new Rect((W - w) / 2, 6.5f * u, w, (Current == Step.Tips ? 14f : 10f) * u);
            HUD.Fill(panel, new Color(0.02f, 0.01f, 0.05f, 0.72f));
            HUD.Frame(panel, new Color(1f, 0.85f, 0.45f, 0.8f), Mathf.Max(2f, 0.15f * u));

            int stepNo = Mathf.Min((int)Current + 1, VisibleSteps);
            var head = new GUIStyle(HUD.Small);
            head.normal.textColor = new Color(1f, 0.85f, 0.45f, 0.9f);
            HUD.Shadowed(new Rect(panel.x + u, panel.y + 0.4f * u, w, 2f * u),
                Current == Step.Tips ? "TUTORIAL" : $"TUTORIAL  {stepNo} / {VisibleSteps}", head);
            HUD.Shadowed(new Rect(panel.x + u, panel.y + 0.4f * u, w - 2 * u, 2f * u), "Enter: skip", HUD.Small.Right());

            var t = new GUIStyle(HUD.Label) { fontSize = Mathf.RoundToInt(HUD.Label.fontSize * 1.3f) };
            t.normal.textColor = Color.white;
            HUD.Shadowed(new Rect(panel.x + u, panel.y + 2.4f * u, w, 3f * u), title, t);
            var b = new GUIStyle(HUD.Label) { wordWrap = true, fontStyle = FontStyle.Normal };
            b.normal.textColor = new Color(0.85f, 0.85f, 0.9f);
            HUD.Shadowed(new Rect(panel.x + u, panel.y + 5.4f * u, w - (fireball ? 10f * u : 2f * u), panel.height - 5.8f * u), body, b);

            if (fireball)
            {
                var fb = SpellBook.Known.Find(s => s.Id == "fireball");
                var ss = SpellSystem.Instance;
                if (fb != null)
                {
                    int lit = ss && ss.Channeling && Arrows.HasPrefix(fb.Sequence, ss.Entered) ? ss.Entered.Count : 0;
                    float g = 6.2f * u; // below the "Enter: skip" label
                    HUD.DrawGlyph(fb.Sequence, new Rect(panel.xMax - g - 1.5f * u, panel.y + 2.9f * u, g, g), fb.Color, 1f, lit, Mathf.Max(3f, 0.4f * u));
                }
            }
            if (hintTimer > 0f)
            {
                var hs = new GUIStyle(HUD.Label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                hs.normal.textColor = new Color(1f, 0.55f, 0.4f, Mathf.Clamp01(hintTimer * 2f));
                HUD.Shadowed(new Rect(panel.x, panel.yMax + 0.5f * u, w, 3f * u), hint, hs);
            }
        }
    }
}
