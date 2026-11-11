using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Rune casting. Hold Right Mouse (or Left Ctrl) to channel: mouse look stops and the mouse draws a
    /// rune in front of you (see <see cref="GestureRecognizer"/>); each up/down/left/right stroke is one
    /// arrow of the spell's sequence. A full match readies the spell (one at a time); Left Click fires it.
    /// A stroke that matches no available spell fizzles with a short lockout. Left Click with nothing
    /// readied fires the basic bolt. WASD keeps moving while channeling, slowed.
    /// </summary>
    public class SpellSystem : MonoBehaviour
    {
        public static SpellSystem Instance { get; private set; }

        public const float BoltSpeed = 28f;
        public const float FizzleLockout = 0.3f;
        public static readonly Color BoltColor = new Color(0.75f, 0.55f, 1f);
        static readonly Color IdleGlow = new Color(0.6f, 0.85f, 1f, 0.35f);
        static readonly Color ChannelGlow = new Color(0.85f, 0.85f, 1f, 0.45f);

        public bool Channeling { get; private set; }
        public readonly List<Arrow> Entered = new List<Arrow>();
        /// <summary>The strokes of the last attempt that readied or fizzled (practice feedback).</summary>
        public List<Arrow> LastStrokes { get; private set; } = new List<Arrow>();
        public SpellDefinition Readied { get; private set; }
        public float Lockout { get; private set; }
        public float FizzleFlash { get; private set; }
        public float ReadyFlash { get; private set; }
        public string LastMessage { get; private set; }
        public float MessageTimer { get; private set; }

        /// <summary>Raised whenever an arrow is accepted, a spell readies, fizzles or casts (for tests/UI).</summary>
        public event System.Action<string> Event;

        readonly Dictionary<SpellDefinition, float> cooldownLeft = new Dictionary<SpellDefinition, float>();
        readonly List<SpellDefinition> cooldownKeys = new List<SpellDefinition>();
        readonly Dictionary<SpellDefinition, float> cooldownLengths = new Dictionary<SpellDefinition, float>();
        static PlayerStats Stats => Run.Instance ? Run.Instance.Stats : new PlayerStats();
        float boltCooldown;

        /// <summary>The rune being drawn right now.</summary>
        public readonly GestureRecognizer Gesture = new GestureRecognizer();

        /// <summary>The last finished rune, left glowing briefly (spell colour when it readied, red when it fizzled).</summary>
        public List<Vector2> EchoTrail { get; private set; } = new List<Vector2>();
        public Color EchoColor { get; private set; }
        public float EchoTimer { get; private set; }
        public const float EchoDuration = 0.6f;

        void Awake() => Instance = this;

        public IReadOnlyList<SpellDefinition> Book => SpellBook.Known;

        void Update()
        {
            float dt = Time.deltaTime;
            boltCooldown -= dt;
            if (CooldownsTick) TickCooldowns(dt);
            Lockout = Mathf.Max(0f, Lockout - dt);
            FizzleFlash = Mathf.Max(0f, FizzleFlash - dt * 3f);
            ReadyFlash = Mathf.Max(0f, ReadyFlash - dt * 2f);
            MessageTimer = Mathf.Max(0f, MessageTimer - dt);
            EchoTimer = Mathf.Max(0f, EchoTimer - dt);
            Gesture.StrokeLength = Settings.RuneStroke;

            var player = Player.Instance;
            bool canAct = player && player.InputEnabled && GameState.Instance.AcceptsGameplayInput && !AutoTest.Active;
            if (canAct)
            {
                bool held = Bindings.Held(GameAction.Channel);
                ProcessChannelInput(held, new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")), dt);

                if (Readied != null && Bindings.Pressed(GameAction.Cast)) CastReadied();
                else if (Readied == null && Bindings.Held(GameAction.Cast) && boltCooldown <= 0f) FireBasic();
            }
            else if (!AutoTest.Active) SetChanneling(false);

            UpdateHands();
        }

        void UpdateHands()
        {
            var hands = WizardHands.Instance;
            if (!hands) return;
            hands.Casting = Channeling || Readied != null;
            Color glow = Readied != null ? Readied.Color : (Channeling ? ChannelGlow : IdleGlow);
            if (Readied != null) glow.a = 0.75f + 0.25f * ReadyFlash;
            if (FizzleFlash > 0f) glow = Color.Lerp(glow, new Color(1f, 0.2f, 0.1f, 0.9f), FizzleFlash);
            hands.Glow = glow;
        }

        // ---------------- public API (also used by AutoTest) ----------------

        public void SetChanneling(bool on)
        {
            if (on == Channeling) return;
            Channeling = on;
            if (Player.Instance) Player.Instance.Channeling = on;
            if (on) Sfx.Play("channel", 0.4f);
            Entered.Clear(); // a partial sequence is lost when you let go; a readied spell is kept
            Gesture.Reset();
        }

        /// <summary>A rune survives the button being up this long (a slipped finger or a dropped input frame).</summary>
        public const float ReleaseGrace = 0.18f;
        /// <summary>Mouse movement this soon after pressing the button is still the aiming hand, not the rune.</summary>
        public const float StartSettle = 0.1f;
        float releaseTimer, settleTimer;

        /// <summary>
        /// One frame of channel input. Pressing starts a rune; the first moments of mouse movement are
        /// ignored (you were probably still aiming); letting go only ends the rune after a short grace
        /// period, so a momentary slip of the button doesn't wipe a half-drawn rune. Mouse look comes
        /// back the moment the button is up.
        /// </summary>
        public void ProcessChannelInput(bool held, Vector2 mouseDelta, float dt)
        {
            if (held)
            {
                if (!Channeling)
                {
                    SetChanneling(true);
                    settleTimer = StartSettle;
                }
                releaseTimer = 0f;
                if (Player.Instance) Player.Instance.Channeling = true;
                if (settleTimer > 0f)
                {
                    settleTimer -= dt;
                    Gesture.Restart();
                    return;
                }
                FeedGesture(mouseDelta);
                return;
            }
            if (!Channeling) return;
            if (Player.Instance) Player.Instance.Channeling = false; // look and full speed come back at once
            releaseTimer += dt;
            if (releaseTimer >= ReleaseGrace) SetChanneling(false);
        }

        /// <summary>Moves the rune pen (mouse delta while channeling) and enters any stroke it completes.</summary>
        public void FeedGesture(Vector2 delta)
        {
            if (!Channeling) return;
            if (Lockout > 0f) { Gesture.Restart(); return; }
            var stroke = Gesture.Feed(delta);
            if (stroke.HasValue) PushArrow(stroke.Value);
        }

        void Echo(Color color)
        {
            EchoTrail = new List<Vector2>(Gesture.Trail);
            EchoColor = color;
            EchoTimer = EchoDuration;
            Gesture.Restart(); // overshooting the final stroke must not start a new rune with it
        }

        /// <summary>
        /// Spell cooldowns only count down during a fight (a locked room, live enemies or the tutorial),
        /// so waiting in a cleared room can't refill Heal over and over.
        /// </summary>
        public static bool CooldownsTick =>
            GameState.TutorialMode || Enemy.All.Count > 0 || (Dungeon.Instance && Dungeon.Instance.InCombat);

        void TickCooldowns(float dt)
        {
            if (cooldownLeft.Count == 0) return;
            cooldownKeys.Clear();
            cooldownKeys.AddRange(cooldownLeft.Keys);
            foreach (var k in cooldownKeys)
            {
                float left = cooldownLeft[k] - dt;
                if (left <= 0f) cooldownLeft.Remove(k);
                else cooldownLeft[k] = left;
            }
        }

        public float CooldownRemaining(SpellDefinition s) =>
            cooldownLeft.TryGetValue(s, out var left) ? Mathf.Max(0f, left) : 0f;

        public bool OnCooldown(SpellDefinition s) => CooldownRemaining(s) > 0f;

        /// <summary>0..1 share of the cooldown still to go (for the HUD sweep).</summary>
        public float CooldownFraction(SpellDefinition s) =>
            cooldownLengths.TryGetValue(s, out var len) && len > 0f ? CooldownRemaining(s) / len : 0f;

        /// <summary>A spell's cooldown after items and buffs.</summary>
        public static float EffectiveCooldown(SpellDefinition s) =>
            GameState.PracticeMode ? 0f : s.Cooldown * (Run.Instance ? Run.Instance.CooldownMult : 1f);

        /// <summary>Spells still reachable with the arrows entered so far.</summary>
        public bool IsCandidate(SpellDefinition s) => !OnCooldown(s) && Arrows.HasPrefix(s.Sequence, Entered);

        public void PushArrow(Arrow arrow)
        {
            if (!Channeling || Lockout > 0f) return;
            Entered.Add(arrow);

            SpellDefinition exact = null;
            bool any = false;
            foreach (var s in Book)
            {
                if (!IsCandidate(s)) continue;
                any = true;
                if (s.Sequence.Length == Entered.Count) exact = s;
            }

            if (!any || exact != null) LastStrokes = new List<Arrow>(Entered);
            if (!any)
            {
                Fizzle();
                return;
            }
            // Rising pitch with each arrow, and a different base pitch per direction.
            float pitch = 0.85f + 0.1f * (int)arrow + 0.06f * (Entered.Count - 1);
            Sfx.Play("tick", 0.8f, pitch);
            Raise("arrow " + arrow);
            if (exact != null) Ready(exact);
        }

        float readiedAt;
        int castCount;

        void Ready(SpellDefinition s)
        {
            Readied = s;
            readiedAt = Time.time;
            Entered.Clear();
            Echo(s.Color);
            ReadyFlash = 1f;
            Sfx.Play("ready", 0.6f);
            Raise("ready " + s.Name);
        }

        public void Fizzle()
        {
            Entered.Clear();
            Echo(new Color(1f, 0.25f, 0.15f));
            Lockout = FizzleLockout;
            FizzleFlash = 1f;
            Sfx.Play("fizzle", 0.7f);
            Flash("Fizzle!");
            Raise("fizzle");
        }

        public bool CastReadied()
        {
            var s = Readied;
            if (s == null || !Player.Instance) return false;
            Readied = null;
            float cd = EffectiveCooldown(s);
            castCount++;
            Progress.Add("spells_cast");
            var stats = Stats;
            if (stats.FreeCastEvery > 0 && castCount % stats.FreeCastEvery == 0) { cd = 0f; Flash("Free cast!"); } // Storm Crown
            float power = stats.Power;
            if (stats.Overcharge && Time.time - readiedAt >= 1.5f) { power *= 1.5f; Flash("Overcharged!"); }
            if (cd > 0f) cooldownLeft[s] = cd; else cooldownLeft.Remove(s);
            cooldownLengths[s] = cd;
            Sfx.Play("cast", 0.6f);
            WizardHands.Instance.Kick();
            new SpellCast(s, Player.Instance) { Power = power }.Launch();
            Raise("cast " + s.Name);
            // Echo Stone relic: sometimes the spell goes off a second time, a little weaker.
            if (Stats.EchoChance > 0f && Random.value < Stats.EchoChance) StartCoroutine(Echo(s));
            return true;
        }

        System.Collections.IEnumerator Echo(SpellDefinition s)
        {
            yield return new WaitForSeconds(0.3f);
            if (!Player.Instance || Player.Instance.IsDead) yield break;
            new SpellCast(s, Player.Instance) { Power = Stats.Power * 0.7f }.Launch();
            Sfx.Play("cast", 0.5f, 1.25f);
            Flash("Echo!");
            Raise("echo " + s.Name);
        }

        /// <summary>Clears all cooldowns (debug / tests).</summary>
        public void ResetCooldowns() => cooldownLeft.Clear();

        /// <summary>Drops a readied spell (debug / tests).</summary>
        public void ClearReadied() => Readied = null;

        public void Flash(string message)
        {
            LastMessage = message;
            MessageTimer = 1.2f;
        }

        void Raise(string e) => Event?.Invoke(e);

        // ---------------- basic attack ----------------

        /// <summary>Aim from a point toward whatever is under the crosshair.</summary>
        public static Vector3 AimDirection(Vector3 origin)
        {
            var cam = Player.Instance.Cam.transform;
            Vector3 target = cam.position + cam.forward * 60f;
            if (Physics.Raycast(cam.position, cam.forward, out var hit, 60f, Layers.PlayerShotMask, QueryTriggerInteraction.Ignore))
                target = hit.point;
            return (target - origin).normalized;
        }

        public static Vector3 CastOrigin
        {
            get
            {
                var cam = Player.Instance.Cam.transform;
                return cam.position + cam.forward * 0.6f + cam.right * 0.2f - cam.up * 0.18f;
            }
        }

        public void FireBasic()
        {
            var stats = Stats;
            boltCooldown = stats.BoltInterval;
            float damage = stats.BoltDamage, freezeChance = stats.BoltFreezeChance;
            Vector3 origin = CastOrigin;
            Vector3 aim = AimDirection(origin);
            for (int i = 0; i <= stats.BoltSplit; i++) // Bolt Splitter: extra bolts fanned out
            {
                float ang = i == 0 ? 0f : ((i + 1) / 2) * 8f * (i % 2 == 1 ? 1f : -1f);
                SpawnBolt(origin, Quaternion.AngleAxis(ang, Vector3.up) * aim, damage, freezeChance);
            }
            Sfx.Play("bolt", 0.5f, Random.Range(0.95f, 1.05f));
            WizardHands.Instance.Kick();
        }

        void SpawnBolt(Vector3 origin, Vector3 dir, float damage, float freezeChance)
        {
            Projectile.Spawn(origin, dir * BoltSpeed, BoltColor, 0.45f, 0.12f,
                Layers.PlayerShotMask, (point, col) =>
                {
                    Combat.HitCollider(col, damage, Element.Arcane, bolt: true);
                    var enemy = col ? col.GetComponentInParent<Enemy>() : null;
                    if (enemy && enemy.IsAlive && freezeChance > 0f && Random.value < freezeChance)
                    {
                        enemy.Freeze(1.5f);
                        Fx.Flash(enemy.Center, new Color(0.6f, 0.9f, 1f), 1.4f, 0.3f, 0.8f);
                    }
                    Fx.Flash(point, BoltColor, 0.6f, 0.2f, 1.5f);
                });
        }
    }
}
