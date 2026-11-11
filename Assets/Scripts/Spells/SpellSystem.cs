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
        public SpellDefinition Readied { get; private set; }
        public float Lockout { get; private set; }
        public float FizzleFlash { get; private set; }
        public float ReadyFlash { get; private set; }
        public string LastMessage { get; private set; }
        public float MessageTimer { get; private set; }

        /// <summary>Raised whenever an arrow is accepted, a spell readies, fizzles or casts (for tests/UI).</summary>
        public event System.Action<string> Event;

        readonly Dictionary<SpellDefinition, float> cooldownEnds = new Dictionary<SpellDefinition, float>();
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
            Lockout = Mathf.Max(0f, Lockout - dt);
            FizzleFlash = Mathf.Max(0f, FizzleFlash - dt * 3f);
            ReadyFlash = Mathf.Max(0f, ReadyFlash - dt * 2f);
            MessageTimer = Mathf.Max(0f, MessageTimer - dt);
            EchoTimer = Mathf.Max(0f, EchoTimer - dt);

            var player = Player.Instance;
            bool canAct = player && player.InputEnabled && GameState.Instance.AcceptsGameplayInput && !AutoTest.Active;
            if (canAct)
            {
                SetChanneling(Input.GetKey(KeyCode.LeftControl) || Input.GetMouseButton(1));
                if (Channeling)
                    FeedGesture(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")));

                if (Readied != null && Input.GetMouseButtonDown(0)) CastReadied();
                else if (Readied == null && Input.GetMouseButton(0) && boltCooldown <= 0f) FireBasic();
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

        /// <summary>Moves the rune pen (mouse delta while channeling) and enters any stroke it completes.</summary>
        public void FeedGesture(Vector2 delta)
        {
            if (!Channeling) return;
            if (Lockout > 0f) { Gesture.Reset(); return; }
            var stroke = Gesture.Feed(delta);
            if (stroke.HasValue) PushArrow(stroke.Value);
        }

        void Echo(Color color)
        {
            EchoTrail = new List<Vector2>(Gesture.Trail);
            EchoColor = color;
            EchoTimer = EchoDuration;
            Gesture.Reset();
        }

        public float CooldownRemaining(SpellDefinition s) =>
            cooldownEnds.TryGetValue(s, out var end) ? Mathf.Max(0f, end - Time.time) : 0f;

        public bool OnCooldown(SpellDefinition s) => CooldownRemaining(s) > 0f;

        /// <summary>0..1 share of the cooldown still to go (for the HUD sweep).</summary>
        public float CooldownFraction(SpellDefinition s) =>
            cooldownLengths.TryGetValue(s, out var len) && len > 0f ? CooldownRemaining(s) / len : 0f;

        /// <summary>A spell's cooldown after items and buffs.</summary>
        public static float EffectiveCooldown(SpellDefinition s) => s.Cooldown * (Run.Instance ? Run.Instance.CooldownMult : 1f);

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

        void Ready(SpellDefinition s)
        {
            Readied = s;
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
            cooldownEnds[s] = Time.time + cd;
            cooldownLengths[s] = cd;
            Sfx.Play("cast", 0.6f);
            WizardHands.Instance.Kick();
            new SpellCast(s, Player.Instance).Launch();
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
        public void ResetCooldowns() => cooldownEnds.Clear();

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
            Projectile.Spawn(origin, AimDirection(origin) * BoltSpeed, BoltColor, 0.45f, 0.12f,
                Layers.PlayerShotMask, (point, col) =>
                {
                    Combat.HitCollider(col, damage);
                    var enemy = col ? col.GetComponentInParent<Enemy>() : null;
                    if (enemy && enemy.IsAlive && freezeChance > 0f && Random.value < freezeChance)
                    {
                        enemy.Freeze(1.5f);
                        Fx.Flash(enemy.Center, new Color(0.6f, 0.9f, 1f), 1.4f, 0.3f, 0.8f);
                    }
                    Fx.Flash(point, BoltColor, 0.6f, 0.2f, 1.5f);
                });
            Sfx.Play("bolt", 0.5f, Random.Range(0.95f, 1.05f));
            WizardHands.Instance.Kick();
        }
    }
}
