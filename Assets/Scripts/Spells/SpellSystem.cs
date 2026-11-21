using System.Linq;
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
        /// <summary>Storm Crown: every Nth spell hits this much harder (heals this much more).</summary>
        public const float StormCrownPower = 1.5f;
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

        /// <summary>
        /// Hook: a spell or basic bolt is about to go off; handlers may modify the request (Grimoire cards).
        /// With no handler, casting is exactly as before.
        /// </summary>
        public static event System.Action<CastRequest> BeforeCast;
        /// <summary>Hook: overrides the hands' glow colour while a spell is readied (null = spell colour).</summary>
        public static System.Func<Color?> GlowTint;
        /// <summary>Seconds during which nothing can be cast (set from outside, e.g. Grimoire card cycling).</summary>
        public float CastLock;

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

        void Awake()
        {
            Instance = this;
            SaveSystem.Register(new SaveSection<SpellsSave>("spells", 20, SaveState, LoadState));
        }

        public SpellsSave SaveState()
        {
            var s = new SpellsSave { castCount = castCount };
            foreach (var sp in SpellBook.Known) s.known.Add(SpellCodec.ToData(sp));
            return s;
        }

        /// <summary>The spell book in its saved order; cooldowns start fresh.</summary>
        public void LoadState(SpellsSave s)
        {
            SpellBook.Known.Clear();
            foreach (var d in s.known) SpellBook.Known.Add(SpellCodec.FromData(d));
            // A forged spell from an older version whose rune a new spell now blocks (0.19: Blink Dash ←→) gets a new rune.
            foreach (var name in SpellForge.FixClashes(SpellBook.Known))
            {
                var sp = SpellBook.Known.First(k => k.Name == name);
                if (Run.Instance) Run.Instance.Toast($"New rune for {name}", $"A new spell took its old rune. Now {Arrows.Glyphs(sp.Sequence)}", sp.Color, 5f);
                Debug.Log($"[Spellcaster] {name} got a new rune {Arrows.ToText(sp.Sequence)} (its old one clashed with a new spell)");
            }
            castCount = s.castCount;
            Readied = null;
            ResetCooldowns();
            cooldownLengths.Clear();
        }

        public IReadOnlyList<SpellDefinition> Book => SpellBook.Known;

        void Update()
        {
            float dt = Time.deltaTime;
            boltCooldown -= dt;
            if (CooldownsTick) TickCooldowns(dt);
            Lockout = Mathf.Max(0f, Lockout - dt);
            CastLock = Mathf.Max(0f, CastLock - dt);
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
                // A style change applies from the next cast: a cast in progress keeps its style.
                var style = Channeling ? ActiveStyle : Settings.CastingStyle;
                if (style == CastingStyle.Momentum)
                {
                    if (HUD.MapOpen) Block(); // the map stops casting mode like a panel does
                    else
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            arrowPressed[i] = Bindings.Pressed(ArrowActions[i]);
                            arrowHeld[i] = Bindings.Held(ArrowActions[i]);
                        }
                        ProcessMomentumInput(held, arrowPressed, arrowHeld, dt);
                    }
                }
                else ProcessChannelInput(held, new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")), dt);

                if (CastLock > 0f) { }
                else if (Readied != null && Bindings.Pressed(GameAction.Cast)) CastReadied();
                else if (Readied == null && Bindings.Held(GameAction.Cast) && boltCooldown <= 0f) FireBasic();
            }
            else if (!AutoTest.Active) Block();

            UpdateHands();
        }

        // ---------------- Momentum casting style ----------------

        /// <summary>The style of the cast in progress (set when casting mode starts).</summary>
        public CastingStyle ActiveStyle { get; private set; }
        /// <summary>In Momentum casting mode right now (running the way you were moving, W A S D type the rune).</summary>
        public bool InMomentum => Channeling && ActiveStyle == CastingStyle.Momentum;
        /// <summary>Momentum was cancelled (panel, pause, map, focus): it waits for the cast key to be pressed again.</summary>
        public bool WaitingForFreshPress { get; private set; }
        /// <summary>This Momentum cast started from standing still: you stay put and only type the rune.</summary>
        public bool CastFromRest { get; private set; }

        /// <summary>W A S D (the movement bindings) type up, left, down, right in Momentum casting mode.</summary>
        static readonly GameAction[] ArrowActions = { GameAction.MoveForward, GameAction.MoveLeft, GameAction.MoveBack, GameAction.MoveRight };
        static readonly Arrow[] ArrowDirs = { Arrow.Up, Arrow.Left, Arrow.Down, Arrow.Right };
        readonly bool[] arrowPressed = new bool[4], arrowHeld = new bool[4], arrowIgnored = new bool[4];

        /// <summary>
        /// One frame of Momentum input (also the test hook): the cast key, and for up / left / down / right
        /// whether the key went down this frame and whether it is held. Keys already held when casting mode
        /// starts don't count until they are released and pressed again; a held key is one input.
        /// </summary>
        public void ProcessMomentumInput(bool held, bool[] pressed, bool[] keysHeld, float dt)
        {
            if (WaitingForFreshPress)
            {
                if (held) return; // still the press from before the cancel
                WaitingForFreshPress = false;
            }
            if (held)
            {
                if (!Channeling)
                {
                    // Standing still (slow, no movement key held) when the key goes down: this cast doesn't run.
                    // The run keeps the direction you were going: the movement keys held, or else how you
                    // were moving (sliding on ice, the last keys just let go).
                    var pl = Player.Instance;
                    var dir = new Vector3((keysHeld[3] ? 1f : 0f) - (keysHeld[1] ? 1f : 0f), 0f,
                                          (keysHeld[0] ? 1f : 0f) - (keysHeld[2] ? 1f : 0f));
                    if (dir.sqrMagnitude < 0.01f && pl && pl.PlanarSpeed >= MomentumConfig.StillSpeed) dir = pl.LocalMoveDir;
                    CastFromRest = dir.sqrMagnitude < 0.01f;
                    if (pl) pl.AutoDir = CastFromRest ? Vector3.forward : dir.normalized;
                    ActiveStyle = CastingStyle.Momentum;
                    SetChanneling(true);
                    for (int i = 0; i < 4; i++) arrowIgnored[i] = keysHeld[i];
                }
                releaseTimer = 0f;
                if (Player.Instance)
                {
                    Player.Instance.AutoForward = !CastFromRest;
                    Player.Instance.HoldStill = CastFromRest;
                }
                for (int i = 0; i < 4; i++)
                {
                    if (!keysHeld[i] && !pressed[i]) { arrowIgnored[i] = false; continue; }
                    if (pressed[i] && !arrowIgnored[i]) PushArrow(ArrowDirs[i]);
                }
                return;
            }
            if (!Channeling) return;
            if (Player.Instance) Player.Instance.AutoForward = false; // running stops at once; the rune survives a short slip
            releaseTimer += dt;
            if (releaseTimer >= ReleaseGrace) SetChanneling(false);
        }

        /// <summary>
        /// Gameplay input is blocked (pause, a panel, the map, focus lost). Classic just ends casting as before;
        /// Momentum also stops you at once and waits for a fresh press of the cast key.
        /// </summary>
        public void Block()
        {
            bool momentum = Channeling ? ActiveStyle == CastingStyle.Momentum : Settings.CastingStyle == CastingStyle.Momentum;
            if (momentum)
            {
                if (InMomentum && Player.Instance) Player.Instance.StopNow();
                if (InMomentum || Bindings.Held(GameAction.Channel)) WaitingForFreshPress = true;
            }
            SetChanneling(false);
        }

        /// <summary>Test hook: as if the cast key were released before the next press.</summary>
        public void ClearFreshPressForTest() => WaitingForFreshPress = false;

        void UpdateHands()
        {
            var hands = WizardHands.Instance;
            if (!hands) return;
            hands.Casting = Channeling || Readied != null;
            Color channelGlow = ChannelGlow;
            if (GameState.Multiplayer && Player.Instance) { channelGlow = Player.Instance.Color; channelGlow.a = 0.5f; } // your colour in co-op
            Color glow = Readied != null ? Readied.Color : (Channeling ? channelGlow : IdleGlow);
            if (Readied != null && GlowTint != null) { var tint = GlowTint(); if (tint.HasValue) glow = tint.Value; }
            if (Readied != null) glow.a = 0.75f + 0.25f * ReadyFlash;
            if (FizzleFlash > 0f) glow = Color.Lerp(glow, new Color(1f, 0.2f, 0.1f, 0.9f), FizzleFlash);
            hands.Glow = glow;
        }

        // ---------------- public API (also used by AutoTest) ----------------

        public void SetChanneling(bool on)
        {
            if (on == Channeling) return;
            Channeling = on;
            if (Player.Instance)
            {
                // Classic: the view stops and you slow down. Momentum: free look, keep running.
                bool momentum = ActiveStyle == CastingStyle.Momentum;
                Player.Instance.Channeling = on && !momentum;
                Player.Instance.AutoForward = on && momentum && !CastFromRest;
                Player.Instance.HoldStill = on && momentum && CastFromRest;
            }
            if (!on) { ActiveStyle = CastingStyle.Classic; CastFromRest = false; }
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
                    ActiveStyle = CastingStyle.Classic;
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
            GameState.PracticeMode ? 0f : CooldownConfig.BaseCooldown(s) * (Run.Instance ? Run.Instance.CooldownMult : 1f);

        /// <summary>
        /// The cast's Grimoire card on the cooldown. A spell with a cooldown: Quicken drops it, Overload doubles it.
        /// A spell without one (most of them, CooldownConfig): Quicken gives a short burst of speed instead, and
        /// Overload stops you casting spells for a moment (castLock seconds).
        /// </summary>
        public static float CardCooldown(float cd, bool hasCooldown, CastRequest req, Player caster, out float castLock)
        {
            castLock = 0f;
            if (req == null) return cd;
            if (hasCooldown) return req.ResetCooldown ? 0f : cd * req.CooldownMult;
            if (req.ResetCooldown && caster && caster.Run) caster.Run.Sprint(Grimoire.GrimoireConfig.QuickenSprintSeconds, Grimoire.GrimoireConfig.QuickenSprintBonus);
            if (req.CooldownMult > 1f) castLock = Grimoire.GrimoireConfig.OverloadCastLock;
            return 0f;
        }

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
                // The strokes belong to a spell that's cooling down: say so instead of a plain fizzle.
                var cooling = Book.FirstOrDefault(sp => OnCooldown(sp) && Arrows.HasPrefix(sp.Sequence, Entered));
                if (cooling != null) OnCooldownFizzle(cooling);
                else Fizzle();
                return;
            }
            // Rising pitch with each arrow, and a different base pitch per direction.
            float pitch = 0.85f + 0.1f * (int)arrow + 0.06f * (Entered.Count - 1);
            Sfx.Play("tick", 0.8f, pitch);
            Raise("arrow " + arrow);
            if (exact == null) return;
            if (exact.IsInstant) CastInstant(exact);
            else Ready(exact);
        }

        /// <summary>
        /// An Instant spell (Blink Dash, Quickening) goes off the moment its rune is complete. A spell already
        /// readied stays readied.
        /// </summary>
        void CastInstant(SpellDefinition s)
        {
            Entered.Clear();
            Echo(s.Color);
            if (CastLock > 0f)
            {
                Lockout = FizzleLockout;
                Flash("Can't cast yet");
                Raise("locked " + s.Name);
                return;
            }
            Raise("instant " + s.Name);
            Cast(s, 0f);
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

        /// <summary>You drew the rune of a spell that's on cooldown.</summary>
        public void OnCooldownFizzle(SpellDefinition s)
        {
            Entered.Clear();
            Echo(new Color(1f, 0.6f, 0.2f));
            Lockout = FizzleLockout;
            FizzleFlash = 0.6f;
            Sfx.Play("fizzle", 0.45f, 1.25f);
            Flash($"Spell on CD ({s.Name} {CooldownRemaining(s):0.0}s)");
            Raise("cooldown " + s.Name);
        }

        public bool CastReadied()
        {
            var s = Readied;
            if (s == null || !Player.Instance) return false;
            Readied = null;
            return Cast(s, Time.time - readiedAt);
        }

        /// <summary>Casts a spell (readied, or instant): `held` is how long it was readied (the Overcharge relic).</summary>
        bool Cast(SpellDefinition s, float held)
        {
            if (Net.NetSession.IsClientOnly && Net.NetPlayers.Running)
            {
                // Multiplayer client: the rune was drawn here; the host checks and casts it.
                float ccd = EffectiveCooldown(s);
                bool chasCd = CooldownConfig.HasCooldown(s);
                var nextCard = Net.NetGrimoire.ClientMirror && Grimoire.GrimoireRun.Instance && Grimoire.GrimoireRun.Instance.Deck != null
                    ? Grimoire.GrimoireRun.Instance.Deck.Next : null; // the host plays it; predict its cooldown here
                string nextId = nextCard != null ? nextCard.Def.Id : null;
                if (chasCd)
                {
                    if (nextId == "quicken" || nextId == "fumble") ccd = 0f;
                    else if (nextId == "overload") ccd *= Grimoire.GrimoireConfig.OverloadCooldownMult;
                }
                else
                {
                    // No cooldown: Quicken is a burst of speed, Overload a short casting lock (the host does the same).
                    if (nextId == "quicken" && Run.Instance) Run.Instance.PredictSprint(Grimoire.GrimoireConfig.QuickenSprintSeconds, Grimoire.GrimoireConfig.QuickenSprintBonus);
                    if (nextId == "overload") Lockout = Mathf.Max(Lockout, Grimoire.GrimoireConfig.OverloadCastLock);
                }
                if (ccd > 0f) cooldownLeft[s] = ccd; else cooldownLeft.Remove(s);
                cooldownLengths[s] = ccd;
                castCount++;
                if (Stats.FreeCastEvery > 0 && castCount % Stats.FreeCastEvery == 0) Flash("Storm Crown!"); // the host makes it hit harder
                Progress.Add("spells_cast");
                Sfx.Play("cast", 0.6f);
                WizardHands.Instance.Kick();
                if (SelfSpellCards.IsDash(s))
                {
                    // Blink Dash moves you at once (predicted with the Next card); the host plays the cast on the
                    // same movement frame, checks it and corrects you if it disagrees.
                    SelfSpellCards.Predict(nextCard != null ? nextCard.Def.Id : null, out float dd, out int dn, out float de);
                    Player.Instance.PendingDash = (dd, (byte)dn, de);
                    if (dn > 0) Sfx.Play("whoosh", 0.7f);
                }
                else
                {
                    if (SelfSpellCards.IsQuickening(s) && Run.Instance && (nextCard == null || nextCard.Def.Id != "fumble"))
                        Run.Instance.PredictQuicken(new QuickenBuff { seconds = SelfSpellConfig.QuickenDuration, speed = SelfSpellConfig.QuickenSpeedBonus, regen = SelfSpellConfig.QuickenRegen });
                    Net.NetCombat.SendCast(s, held);
                }
                Raise("cast " + s.Name);
                return true;
            }
            var req = Request(s);
            if (req != null && req.Fizzle)
            {
                // The spell is lost, but no cooldown is spent.
                FizzleFlash = 1f;
                Sfx.Play("fizzle", 0.7f, 0.8f);
                Flash("Fumble!");
                Raise("fumble " + s.Name);
                return true;
            }
            float cd = EffectiveCooldown(s);
            castCount++;
            Progress.Add("spells_cast");
            var stats = Stats;
            float power = stats.Power;
            if (stats.FreeCastEvery > 0 && castCount % stats.FreeCastEvery == 0) { power *= StormCrownPower; Flash("Storm Crown!"); }
            if (stats.Overcharge && held >= 1.5f) { power *= 1.5f; Flash("Overcharged!"); }
            cd = CardCooldown(cd, CooldownConfig.HasCooldown(s), req, Player.Instance, out float castLock);
            if (castLock > 0f) Lockout = Mathf.Max(Lockout, castLock);
            if (cd > 0f) cooldownLeft[s] = cd; else cooldownLeft.Remove(s);
            cooldownLengths[s] = cd;
            Sfx.Play("cast", 0.6f);
            WizardHands.Instance.Kick();
            if (req == null) new SpellCast(s, Player.Instance) { Power = power }.Launch();
            else LaunchCopies(s, power, req, false);
            Raise("cast " + s.Name);
            // Echo Stone relic: sometimes the spell goes off a second time, a little weaker.
            if (Stats.EchoChance > 0f && !SelfSpellCards.IsDash(s) && Random.value < Stats.EchoChance) StartCoroutine(Echo(s)); // no surprise second dash
            if (req != null && req.EchoDelay > 0f) StartCoroutine(RequestEcho(s, power, req));
            return true;
        }

        /// <summary>Asks the BeforeCast handlers about a cast; null when there are none.</summary>
        static CastRequest Request(SpellDefinition s)
        {
            if (BeforeCast == null) return null;
            var req = new CastRequest { Spell = s };
            BeforeCast(req);
            return req;
        }

        static void LaunchCopies(SpellDefinition s, float power, CastRequest req, bool echo)
        {
            foreach (var m in req.Copies)
            {
                var mods = echo ? m.Clone() : m;
                if (echo) mods.NoLifesteal = true; // echoes never lifesteal (balance unchanged)
                new SpellCast(s, Player.Instance) { Power = power * mods.DamageMult, Mods = mods }.Launch();
            }
            if (!echo) req.AfterLaunch?.Invoke();
        }

        System.Collections.IEnumerator RequestEcho(SpellDefinition s, float power, CastRequest req)
        {
            yield return new WaitForSeconds(req.EchoDelay);
            if (!Player.Instance || Player.Instance.IsDead) yield break;
            if (s == null) FireBolts(req, true);
            else LaunchCopies(s, power, req, true);
            Sfx.Play("cast", 0.5f, 1.25f);
            Flash("Echo!");
            Raise("echo " + (s != null ? s.Name : "bolt"));
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

        /// <summary>A spell left the book (forgotten in the Forge): drop any state that refers to it.</summary>
        public void OnForgotten(SpellDefinition s)
        {
            if (Readied == s) Readied = null;
            cooldownLeft.Remove(s);
            cooldownLengths.Remove(s);
            UpdateHands();
        }

        /// <summary>Drops a readied spell (debug / tests).</summary>
        public void ClearReadied() => Readied = null;

        /// <summary>Test hook: casts a spell as if its rune had just been drawn (co-op test client).</summary>
        public bool CastForTest(SpellDefinition s) => s != null && Player.Instance && Cast(s, 0f);

        /// <summary>Test hook: readies a spell without drawing its rune.</summary>
        public void ReadyForTest(SpellDefinition s) => Ready(s);

        /// <summary>Whether anything is attached to <see cref="BeforeCast"/> (tests: the Grimoire is fully off).</summary>
        public static bool HasCastHooks => BeforeCast != null;

        public void Flash(string message)
        {
            LastMessage = message;
            MessageTimer = 1.2f;
        }

        void Raise(string e) => Event?.Invoke(e);

        // ---------------- basic attack ----------------

        /// <summary>Aim from a point toward whatever is under the crosshair.</summary>
        public static Vector3 AimDirection(Vector3 origin) => AimDirectionFor(Player.Instance, origin);
        public static Vector3 CastOrigin => CastOriginFor(Player.Instance);

        /// <summary>Aim for any player (the host casts for others with their own view).</summary>
        public static Vector3 AimDirectionFor(Player caster, Vector3 origin)
        {
            var cam = (caster ? caster : Player.Instance).Cam.transform;
            Vector3 target = cam.position + cam.forward * 60f;
            if (Physics.Raycast(cam.position, cam.forward, out var hit, 60f, Layers.PlayerShotMask, QueryTriggerInteraction.Ignore))
                target = hit.point;
            return (target - origin).normalized;
        }

        public static Vector3 CastOriginFor(Player caster)
        {
            var cam = (caster ? caster : Player.Instance).Cam.transform;
            return cam.position + cam.forward * 0.6f + cam.right * 0.2f - cam.up * 0.18f;
        }

        public void FireBasic()
        {
            var stats = Stats;
            boltCooldown = stats.BoltInterval;
            if (Net.NetSession.IsClientOnly && Net.NetPlayers.Running)
            {
                Net.NetCombat.SendCast(null); // the host fires it
                WizardHands.Instance.Kick();
                return;
            }
            CastRequest req = null;
            if (BeforeCast != null)
            {
                req = new CastRequest();
                BeforeCast(req);
                if (req.Fizzle)
                {
                    FizzleFlash = 1f;
                    Sfx.Play("fizzle", 0.6f, 0.9f);
                    Flash("Fumble!");
                    Raise("fumble bolt");
                    return;
                }
                boltCooldown = req.ResetCooldown ? 0f : boltCooldown * req.CooldownMult;
            }
            FireBolts(req, false);
            WizardHands.Instance.Kick();
            if (req != null && req.EchoDelay > 0f) StartCoroutine(RequestEcho(null, 1f, req));
        }

        void FireBolts(CastRequest req, bool echo) => FireBoltsFrom(Player.Instance, Stats, req, echo);

        /// <summary>The magic bolt from any player (the host fires it for others with their stats).</summary>
        public static void FireBoltsFrom(Player caster, PlayerStats stats, CastRequest req = null, bool echo = false)
        {
            float freezeChance = stats.BoltFreezeChance;
            Vector3 origin = CastOriginFor(caster);
            Vector3 aim = AimDirectionFor(caster, origin);
            var copies = req != null ? req.Copies : null;
            int n = copies != null ? copies.Count : 1;
            for (int c = 0; c < n; c++)
            {
                var mods = copies != null ? copies[c] : CastMods.None;
                float damage = stats.BoltDamage * mods.DamageMult;
                Vector3 dir = mods.AimYaw != 0f ? Quaternion.AngleAxis(mods.AimYaw, Vector3.up) * aim : aim;
                for (int i = 0; i <= stats.BoltSplit; i++) // Bolt Splitter: extra bolts fanned out
                {
                    float ang = i == 0 ? 0f : ((i + 1) / 2) * 8f * (i % 2 == 1 ? 1f : -1f);
                    SpawnBolt(origin, Quaternion.AngleAxis(ang, Vector3.up) * dir, damage, freezeChance, mods);
                }
            }
            Sfx.Play("bolt", 0.5f, Random.Range(0.95f, 1.05f));
            if (req != null && !echo) req.AfterLaunch?.Invoke();
        }

        static void SpawnBolt(Vector3 origin, Vector3 dir, float damage, float freezeChance, CastMods mods)
        {
            Projectile.HitHandler hit = (point, col) =>
            {
                Combat.HitCollider(col, damage, Element.Arcane, bolt: true);
                var enemy = col ? col.GetComponentInParent<Enemy>() : null;
                if (enemy && enemy.IsAlive && freezeChance > 0f && Random.value < freezeChance)
                {
                    enemy.Freeze(1.5f);
                    Fx.Flash(enemy.Center, new Color(0.6f, 0.9f, 1f), 1.4f, 0.3f, 0.8f);
                }
                if (enemy && enemy.IsAlive) mods.OnHit?.Invoke(enemy);
                Fx.Flash(point, BoltColor, 0.6f, 0.2f, 1.5f);
            };
            var p = Projectile.Spawn(origin, dir * BoltSpeed, BoltColor, 0.45f * mods.SizeMult, 0.12f * mods.SizeMult,
                Layers.PlayerShotMask, hit);
            if (mods.Pierce) p.PassThroughEnemies(hit);
        }
    }
}
