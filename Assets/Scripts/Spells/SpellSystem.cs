using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Stratagem-style casting. Hold Left Ctrl / Right Mouse to channel; WASD or arrow keys enter
    /// directions. A full match readies the spell (one at a time); Left Click fires it. Input that
    /// matches no available spell fizzles with a short lockout. Left Click with nothing readied fires
    /// the basic bolt.
    /// </summary>
    public class SpellSystem : MonoBehaviour
    {
        public static SpellSystem Instance { get; private set; }

        public const float BoltInterval = 0.45f;
        public const float BoltDamage = 10f;
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
        float boltCooldown;

        static readonly (KeyCode key, Arrow arrow)[] Keys =
        {
            (KeyCode.W, Arrow.Up), (KeyCode.UpArrow, Arrow.Up),
            (KeyCode.S, Arrow.Down), (KeyCode.DownArrow, Arrow.Down),
            (KeyCode.A, Arrow.Left), (KeyCode.LeftArrow, Arrow.Left),
            (KeyCode.D, Arrow.Right), (KeyCode.RightArrow, Arrow.Right),
        };

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

            var player = Player.Instance;
            bool canAct = player && player.InputEnabled && GameState.Instance.AcceptsGameplayInput && !AutoTest.Active;
            if (canAct)
            {
                SetChanneling(Input.GetKey(KeyCode.LeftControl) || Input.GetMouseButton(1));
                if (Channeling)
                    foreach (var (key, arrow) in Keys)
                        if (Input.GetKeyDown(key)) PushArrow(arrow);

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
        }

        public float CooldownRemaining(SpellDefinition s) =>
            cooldownEnds.TryGetValue(s, out var end) ? Mathf.Max(0f, end - Time.time) : 0f;

        public bool OnCooldown(SpellDefinition s) => CooldownRemaining(s) > 0f;

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
            ReadyFlash = 1f;
            Sfx.Play("ready", 0.6f);
            Raise("ready " + s.Name);
        }

        public void Fizzle()
        {
            Entered.Clear();
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
            cooldownEnds[s] = Time.time + s.Cooldown;
            Sfx.Play("cast", 0.6f);
            WizardHands.Instance.Kick();
            new SpellCast(s, Player.Instance).Launch();
            Raise("cast " + s.Name);
            return true;
        }

        /// <summary>Clears all cooldowns (debug / tests).</summary>
        public void ResetCooldowns() => cooldownEnds.Clear();

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
            if (Physics.Raycast(cam.position, cam.forward, out var hit, 60f, Layers.WorldMask | Layers.EnemyMask, QueryTriggerInteraction.Ignore))
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
            boltCooldown = BoltInterval;
            Vector3 origin = CastOrigin;
            Projectile.Spawn(origin, AimDirection(origin) * BoltSpeed, BoltColor, 0.45f, 0.12f,
                Layers.WorldMask | Layers.EnemyMask, (point, col) =>
                {
                    var enemy = col ? col.GetComponentInParent<Enemy>() : null;
                    if (enemy) enemy.TakeDamage(BoltDamage);
                    Fx.Flash(point, BoltColor, 0.6f, 0.2f, 1.5f);
                });
            Sfx.Play("bolt", 0.5f, Random.Range(0.95f, 1.05f));
            WizardHands.Instance.Kick();
        }
    }
}
