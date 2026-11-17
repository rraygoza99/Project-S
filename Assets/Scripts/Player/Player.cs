using UnityEngine;

namespace Spellcaster
{
    /// <summary>First-person movement, mouse look, health.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class Player : MonoBehaviour
    {
        /// <summary>The player on this PC (camera, HUD, input). Other players: see <see cref="Players"/>.</summary>
        public static Player Instance { get; private set; }

        /// <summary>This player is controlled on this PC (has the camera, reads the keyboard and mouse).</summary>
        public bool IsLocal { get; private set; }
        /// <summary>This player's run: stats, gold, items, consumables.</summary>
        public Run Run { get; private set; }

        public float MaxHP => Run ? Run.Stats.MaxHP : 100f;
        public float HP { get; private set; } = 100f;
        public bool IsDead => HP <= 0f;
        public bool God;

        public float MoveSpeed = 6.5f;
        public float Acceleration = 50f;
        /// <summary>Speed multiplier applied while channeling a spell.</summary>
        public float ChannelSpeedFactor = 0.3f;

        /// <summary>Set by SpellSystem. While true, movement is slowed to ChannelSpeedFactor.</summary>
        public bool Channeling { get; set; }

        public Camera Cam { get; private set; }
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public float PlanarSpeed => planarVelocity.magnitude;
        public float HurtFlash { get; private set; }
        public float HealFlash { get; private set; }
        /// <summary>HP gained by the latest heal(s), shown as "+N" by the health bar.</summary>
        public float LastHeal { get; private set; }

        /// <summary>True when gameplay input should be read (cursor captured, not the click that captured it).</summary>
        public bool InputEnabled => IsLocal && (AutoTest.Active || (Cursor.lockState == CursorLockMode.Locked && Time.frameCount != lockClickFrame && !IsDead));

        CharacterController cc;
        Vector3 planarVelocity;
        float verticalVelocity;
        int lockClickFrame = -1;

        /// <summary>
        /// Builds a player. The local one gets the camera, sound listener, low-res view and hands; another
        /// player (co-op) gets a camera object for aiming only and no input.
        /// </summary>
        public static Player Create(Vector3 position, bool local = true)
        {
            var go = new GameObject(local ? "Player" : "Player (other)") { layer = Layers.Player };
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.05f;

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.6f, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Bootstrap.FogColor;
            if (local)
            {
                camGo.AddComponent<AudioListener>();
                camGo.AddComponent<RetroView>();
            }
            else cam.enabled = false; // aims spells; nothing renders from it

            var p = go.AddComponent<Player>();
            p.Cam = cam;
            p.IsLocal = local;
            p.Mode = local ? Control.Local : Control.Simulated;
            p.Run = go.AddComponent<Run>();
            p.Run.Bind(p);
            if (local)
            {
                Instance = p;
                SaveSystem.Register(new SaveSection<PlayerSave>("player", 30,
                    () => new PlayerSave { hp = p.HP },
                    s => p.HP = Mathf.Clamp(s.hp, 1f, p.MaxHP)));
                go.AddComponent<WizardHands>();
            }
            else go.AddComponent<PlayerAvatar>(); // how this player looks to everyone else
            Players.Add(p);
            return p;
        }

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            Yaw = transform.eulerAngles.y;
        }

        void OnEnable() { if (Run) Players.Add(this); }
        void OnDisable() => Players.Remove(this);
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (IsLocal && !AutoTest.Active && GameState.SkipTitleOnLoad) LockCursor();
        }

        // ---------------- movement: input, then one simulation step ----------------

        /// <summary>Who moves this player: this PC (local), the host from received inputs, or snapshots (others on a client).</summary>
        public enum Control { Local, Simulated, Interpolated }
        public Control Mode = Control.Local;

        /// <summary>One frame of movement input. The local player makes one per frame; the host replays a client's.</summary>
        public struct MoveInput
        {
            public uint seq;
            public float dt;
            public float yaw, pitch;
            public Vector2 wish;      // x = right, y = forward, length up to 1 (keys already masked)
            public byte flags;
            public Vector2 autoDir;   // Momentum run direction relative to the camera (x, z)
        }
        public const byte FlagChanneling = 1, FlagAutoForward = 2, FlagHoldStill = 4, FlagCasting = 8;

        /// <summary>Everything a replay needs to continue from (host state for reconciliation).</summary>
        public struct SimState
        {
            public Vector3 pos, planar;
            public float vertical, decel, chill;
            public bool wasAuto, wasStill;
        }

        /// <summary>The input this PC made last frame (sent to the host by NetPlayers).</summary>
        public MoveInput LastInput { get; private set; }
        uint inputSeq;
        /// <summary>Casting (hands up) as seen by others: set from input flags for remote players.</summary>
        public bool NetCasting { get; private set; }

        void Update()
        {
            float dt = Time.deltaTime;
            HurtFlash = Mathf.Max(0f, HurtFlash - dt * 2.5f);
            HealFlash = Mathf.Max(0f, HealFlash - dt * 1.5f);
            if (IsLocal) HandleCursor();
            if (IsLocal && Net.NetPlayers.Running) UpdateDownedView();
            if (IsDead || Mode != Control.Local) return; // others are moved by the network

            var input = GatherInput(dt);
            Simulate(input);
            LastInput = input;
            Net.NetPlayers.RecordLocal(this, input);
            if (IsLocal) UpdateCue(dt, lastAutoSpeed);
        }

        /// <summary>Reads mouse look and the movement keys into a MoveInput (local player).</summary>
        MoveInput GatherInput(float dt)
        {
            bool gameplay = GameState.Instance.AcceptsGameplayInput;
            // While channeling, the mouse draws the rune instead of turning the view.
            if (Cursor.lockState == CursorLockMode.Locked && InputEnabled && gameplay && !Channeling)
            {
                float sens = Settings.MouseSensitivity;
                Yaw += Input.GetAxisRaw("Mouse X") * sens;
                Pitch = Mathf.Clamp(Pitch - Input.GetAxisRaw("Mouse Y") * sens * (Settings.InvertY ? -1f : 1f), -85f, 85f);
            }

            // Momentum casting just ended: keys still held don't move you until pressed again
            // (or until the slow-down is over), so letting go never jerks you sideways.
            bool decelStarts = (gatherWasAuto && !AutoForward) || (gatherWasStill && !HoldStill);
            if (decelStarts) MaskHeldKeys();
            else if (!AutoForward && decelTimer <= 0f) for (int i = 0; i < 4; i++) masked[i] = false; // the slow-down is over
            gatherWasAuto = AutoForward;
            gatherWasStill = HoldStill;

            // WASD moves (in Momentum casting mode they type the rune instead).
            Vector2 wish = Vector2.zero;
            if (InputEnabled && gameplay && !AutoForward && !HoldStill)
            {
                for (int i = 0; i < 4; i++) if (masked[i] && !Bindings.Held(MoveActions[i])) masked[i] = false;
                if (Bindings.Held(GameAction.MoveForward) && !masked[0]) wish.y += 1;
                if (Bindings.Held(GameAction.MoveBack) && !masked[2]) wish.y -= 1;
                if (Bindings.Held(GameAction.MoveRight) && !masked[3]) wish.x += 1;
                if (Bindings.Held(GameAction.MoveLeft) && !masked[1]) wish.x -= 1;
            }
            if (ForcedWish.HasValue) wish = ForcedWish.Value; // tests drive movement without a keyboard
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            byte flags = 0;
            if (Channeling) flags |= FlagChanneling;
            if (AutoForward) flags |= FlagAutoForward;
            if (HoldStill) flags |= FlagHoldStill;
            var ss = SpellSystem.Instance;
            if (ss && (ss.Channeling || ss.Readied != null)) flags |= FlagCasting;
            return new MoveInput
            {
                seq = ++inputSeq, dt = dt, yaw = Yaw, pitch = Pitch, wish = wish, flags = flags,
                autoDir = new Vector2(AutoDir.x, AutoDir.z),
            };
        }

        float lastAutoSpeed;
        bool gatherWasAuto, gatherWasStill;
        /// <summary>Test hook: movement input to use instead of the keys (x = right, y = forward).</summary>
        public Vector2? ForcedWish;

        /// <summary>
        /// One movement step from an input: the same code runs for the local player, for the host replaying a
        /// client's inputs, and for a client replaying its own unconfirmed inputs after a correction.
        /// </summary>
        public void Simulate(MoveInput input)
        {
            float dt = Mathf.Clamp(input.dt, 0f, 0.1f);
            Yaw = input.yaw;
            Pitch = Mathf.Clamp(input.pitch, -85f, 85f);
            ApplyLook();
            bool auto = (input.flags & FlagAutoForward) != 0, still = (input.flags & FlagHoldStill) != 0;
            bool chan = (input.flags & FlagChanneling) != 0;
            if (Mode != Control.Local) NetCasting = (input.flags & FlagCasting) != 0;

            if ((wasAuto && !auto) || (wasStill && !still)) decelTimer = MomentumConfig.RampOut;
            wasAuto = auto;
            wasStill = still;
            if (auto) decelTimer = 0f;
            else if (decelTimer > 0f) decelTimer -= dt;

            Vector3 wish = auto || still ? Vector3.zero : new Vector3(input.wish.x, 0f, input.wish.y);
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            if (chan) wish *= ChannelSpeedFactor;

            float speed = Run ? Run.MoveSpeed : MoveSpeed;
            ChillTimer = Mathf.Max(0f, ChillTimer - dt);
            if (ChillTimer > 0f) speed *= ChillSpeedFactor; // a frost crystal burst next to you
            float accel = Acceleration;
            float autoSpeed = speed * MomentumConfig.SpeedMultiplier;
            lastAutoSpeed = autoSpeed;
            if (auto)
            {
                // Momentum: keep running the way you were going when the cast started (relative to the
                // camera, so the mouse steers), with a short speed-up.
                wish = new Vector3(input.autoDir.x, 0f, input.autoDir.y);
                speed = autoSpeed;
                accel = autoSpeed / Mathf.Max(0.01f, MomentumConfig.RampIn);
            }
            else if (decelTimer > 0f) accel = Mathf.Max(autoSpeed, 0.1f) / Mathf.Max(0.01f, MomentumConfig.RampOut);
            Vector3 target = transform.TransformDirection(wish) * speed;
            // Ice patches (floor 2): you keep sliding and steer slowly.
            Grip = IcePatch.GripAt(transform.position);
            planarVelocity = Vector3.MoveTowards(planarVelocity, target, accel * Grip * dt);
            verticalVelocity = cc.isGrounded ? -2f : verticalVelocity - 20f * dt;
            cc.Move((planarVelocity + Vector3.up * verticalVelocity) * dt);
        }

        public SimState GetSimState() => new SimState
        {
            pos = transform.position, planar = planarVelocity, vertical = verticalVelocity,
            decel = decelTimer, chill = ChillTimer, wasAuto = wasAuto, wasStill = wasStill,
        };

        public void SetSimState(SimState s)
        {
            cc.enabled = false;
            transform.position = s.pos;
            cc.enabled = true;
            planarVelocity = s.planar;
            verticalVelocity = s.vertical;
            decelTimer = s.decel;
            ChillTimer = s.chill;
            wasAuto = s.wasAuto;
            wasStill = s.wasStill;
        }

        /// <summary>Snapshot playback for another player on a client: no physics, just where the host says.</summary>
        public void SetInterpolated(Vector3 pos, float yaw, float pitch, bool casting)
        {
            transform.position = pos;
            Yaw = yaw;
            Pitch = Mathf.Clamp(pitch, -85f, 85f);
            ApplyLook();
            NetCasting = casting;
        }

        // ---------------- identity (co-op) ----------------

        /// <summary>The lobby id of this player (-1 offline).</summary>
        public int NetId = -1;
        public string DisplayName = "";
        /// <summary>Who this player is across sessions (SteamID; a name in the debug direct mode).</summary>
        public string NetKey;
        public int ColorIndex;
        /// <summary>This player's colour: their sprite's robe and their spell glow.</summary>
        public Color Color => PlayerColors.Get(ColorIndex);

        // ---------------- downed, revive, spectate (multiplayer) ----------------

        /// <summary>At 0 HP in co-op: can't act, a teammate can revive you before you bleed out.</summary>
        public bool Downed { get; private set; }
        /// <summary>Bled out: you spectate until the room is cleared.</summary>
        public bool BledOut { get; private set; }
        /// <summary>Seconds left before bleeding out, and revive progress (seconds a teammate has stood by you).</summary>
        public float BleedTimer, ReviveProgress;
        /// <summary>Who you watch while bled out (null otherwise).</summary>
        public Player SpectateTarget { get; private set; }

        public void SetDowned(bool on, float bleed)
        {
            Downed = on;
            BledOut = false;
            BleedTimer = bleed;
            ReviveProgress = 0f;
        }

        public void SetBledOut()
        {
            Downed = false;
            BledOut = true;
            ReviveProgress = 0f;
        }

        /// <summary>Back on your feet with this much HP (revived, or the room was cleared).</summary>
        public void StandUp(float hp)
        {
            Downed = BledOut = false;
            ReviveProgress = 0f;
            HP = Mathf.Clamp(hp, 1f, MaxHP);
            HealFlash = 1f;
            LastHeal = HP;
        }

        /// <summary>A client's view of the downed state comes from the host.</summary>
        public void SetNetDowned(bool downed, bool bledOut, float bleed, float revive)
        {
            Downed = downed;
            BledOut = bledOut;
            BleedTimer = bleed;
            ReviveProgress = revive;
        }

        /// <summary>
        /// The local camera: low to the floor while downed; following a teammate's view while bled out
        /// (click to switch); back to normal when you stand.
        /// </summary>
        void UpdateDownedView()
        {
            if (!camBaseSet) { camBase = Cam.transform.localPosition; camBaseSet = true; }
            if (BledOut)
            {
                bool next = Input.GetMouseButtonDown(0) || Bindings.Pressed(GameAction.Cast);
                if (!SpectateTarget || SpectateTarget.IsDead || next) SpectateTarget = NextSpectateTarget(next ? SpectateTarget : null);
                return;
            }
            SpectateTarget = null;
            Cam.transform.localPosition = Downed ? new Vector3(camBase.x, 0.45f, camBase.z) : camBase;
            if (Downed) Cam.transform.localRotation = Quaternion.Euler(-10f, 0f, 25f);
        }

        Player NextSpectateTarget(Player after)
        {
            var alive = Players.All.FindAll(p => p && p != this && !p.IsDead);
            if (alive.Count == 0) return null;
            int i = after ? alive.IndexOf(after) : -1;
            return alive[(i + 1) % alive.Count];
        }

        void LateUpdate()
        {
            if (!IsLocal || !BledOut || !SpectateTarget) return;
            var t = SpectateTarget.Cam.transform;
            Cam.transform.SetPositionAndRotation(t.position, t.rotation);
        }

        /// <summary>A client's view of its HP comes from the host (only the host applies damage).</summary>
        public void SetNetHealth(float hp)
        {
            if (Mathf.Approximately(hp, HP)) return;
            if (hp < HP - 0.01f) { HurtFlash = 1f; if (IsLocal) Sfx.Play("hurt", 0.8f, Random.Range(0.9f, 1.1f)); }
            bool wasAlive = !IsDead;
            HP = Mathf.Max(0f, hp);
            // In co-op the host says when the run is over (players get downed first).
            if (wasAlive && IsDead && GameState.Instance && !Net.NetPlayers.Running) GameState.Instance.OnPlayerDied(this);
        }

        // ---------------- Momentum casting style ----------------

        /// <summary>Set by SpellSystem in Momentum casting mode: keep running in AutoDir.</summary>
        public bool AutoForward { get; set; }
        /// <summary>Set by SpellSystem when a Momentum cast starts from standing still: you don't move at all.</summary>
        public bool HoldStill { get; set; }
        /// <summary>The run direction in Momentum casting, relative to the camera (set when the cast starts).</summary>
        public Vector3 AutoDir { get; set; } = Vector3.forward;
        /// <summary>The current movement direction relative to the camera (zero when not moving).</summary>
        public Vector3 LocalMoveDir
        {
            get
            {
                var v = transform.InverseTransformDirection(planarVelocity);
                v.y = 0f;
                return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
            }
        }
        bool wasStill;
        static readonly GameAction[] MoveActions = { GameAction.MoveForward, GameAction.MoveLeft, GameAction.MoveBack, GameAction.MoveRight };
        readonly bool[] masked = new bool[4];
        bool wasAuto;
        float decelTimer, cue, bobTime;
        Vector3 camBase;
        bool camBaseSet;

        /// <summary>Movement keys held right now are ignored until pressed again (or the slow-down ends).</summary>
        void MaskHeldKeys()
        {
            for (int i = 0; i < 4; i++) masked[i] = Bindings.Held(MoveActions[i]);
        }

        /// <summary>A panel, pause or focus loss cancelled Momentum casting: stop on the spot.</summary>
        public void StopNow()
        {
            AutoForward = false;
            wasAuto = gatherWasAuto = false;
            planarVelocity = Vector3.zero;
            decelTimer = MomentumConfig.RampOut;
            MaskHeldKeys();
        }

        /// <summary>Test hook: how many of the four movement keys are being ignored after Momentum casting.</summary>
        public int MaskedKeys { get { int n = 0; foreach (var m in masked) if (m) n++; return n; } }

        /// <summary>The field of view and a light head bob while running in Momentum casting mode (Options toggle).</summary>
        void UpdateCue(float dt, float autoSpeed)
        {
            if (!camBaseSet) { camBase = Cam.transform.localPosition; camBaseSet = true; }
            float want = AutoForward && Settings.MomentumCue ? Mathf.Clamp01(PlanarSpeed / Mathf.Max(0.1f, autoSpeed)) : 0f;
            bool wasOn = cue > 0f;
            cue = Mathf.MoveTowards(cue, want, dt * 5f);
            Cam.fieldOfView = Settings.FieldOfView + MomentumConfig.CueFov * cue;
            if (cue > 0f)
            {
                bobTime += dt * MomentumConfig.CueBobRate;
                Cam.transform.localPosition = camBase + Vector3.up * (Mathf.Abs(Mathf.Sin(bobTime * Mathf.PI)) - 0.5f) * MomentumConfig.CueBobHeight * cue;
            }
            else if (wasOn) Cam.transform.localPosition = camBase;
        }

        /// <summary>How strong the Momentum cue is right now (0..1, tests).</summary>
        public float Cue => cue;

        /// <summary>Seconds you move slower after a frost burst (floor 2 frost crystals).</summary>
        public float ChillTimer;
        public const float ChillSpeedFactor = 0.55f;
        /// <summary>Grip under your feet this frame (1 = normal, lower on ice).</summary>
        public float Grip { get; private set; } = 1f;
        public Vector3 PlanarVelocity => planarVelocity;

        void HandleCursor()
        {
            if (AutoTest.Active || !GameState.Instance.InRun) return; // the menu manages the cursor
            bool forgeJustClosed = SpellForgeUI.Instance && SpellForgeUI.Instance.ClosedFrame == Time.frameCount;
            // Esc pauses (the pause menu's own Esc resumes: not in the same frame).
            if (Input.GetKeyDown(KeyCode.Escape) && !GameState.Instance.ForgeOpen && !forgeJustClosed
                && Cursor.lockState == CursorLockMode.Locked && Time.frameCount != MainMenu.ResumedFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0) && !IsDead && !GameState.Instance.ForgeOpen
                && !(MainMenu.Instance && MainMenu.Instance.PauseUp))
            {
                LockCursor();
                lockClickFrame = Time.frameCount;
            }
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void ApplyLook()
        {
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
            Cam.transform.localRotation = Quaternion.Euler(Pitch, 0f, 0f);
        }

        public void SetLook(float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = Mathf.Clamp(pitch, -85f, 85f);
            ApplyLook();
        }

        public void LookAt(Vector3 worldPoint)
        {
            Vector3 d = worldPoint - Cam.transform.position;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            SetLook(yaw, pitch);
        }

        public Vector3 ChestPosition => transform.position + Vector3.up * 1.3f;

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;
            if (Net.NetSession.IsClientOnly) return; // in multiplayer the host decides damage; HP comes from it
            if (Run) amount *= Run.Stats.DamageTaken;
            HurtFlash = 1f;
            if (IsLocal) Sfx.Play("hurt", 0.8f, Random.Range(0.9f, 1.1f));
            if (Dungeon.Instance) Dungeon.Instance.NotePlayerHurt();
            if (!God && HP - amount <= 0f && Run && Run.TryRevive())
            {
                HP = MaxHP * 0.5f;
                HealFlash = 1f;
                return;
            }
            HP = Mathf.Max(God ? 1f : 0f, HP - amount);
            if (Run) Run.OnPlayerHurt();
            if (HP <= 0f)
            {
                Net.NetDowned.Down(this); // co-op: downed first (a teammate can revive)
                GameState.Instance.OnPlayerDied(this);
            }
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            float before = HP;
            HP = Mathf.Min(MaxHP, HP + amount);
            float gained = HP - before;
            if (gained > 0.05f)
            {
                LastHeal = HealFlash > 0.3f ? LastHeal + gained : gained;
                HealFlash = 1f;
            }
            healed += HP - before;
            if (healed >= 1f) { Progress.Add("heal", Mathf.FloorToInt(healed)); healed -= Mathf.Floor(healed); }
        }

        float healed;

        /// <summary>Blink: jumps up to `distance` along the view (flattened), stopping short of walls.</summary>
        public void Blink(float distance, Color color)
        {
            Vector3 dir = Cam.transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
            dir.Normalize();
            Vector3 from = transform.position + Vector3.up * 0.9f;
            float d = distance;
            if (Physics.SphereCast(from, cc.radius, dir, out var hit, distance, Layers.SolidMask, QueryTriggerInteraction.Ignore))
                d = Mathf.Max(0f, hit.distance - 0.3f);
            Fx.Flash(ChestPosition, color, 2f, 0.35f, 1.5f);
            cc.enabled = false;
            transform.position += dir * d;
            cc.enabled = true;
            Fx.Flash(ChestPosition, color, 2.4f, 0.4f, 1.2f);
            Sfx.Play("zap", 0.6f, 1.4f);
        }

        /// <summary>Keeps HP within a (possibly lowered) max.</summary>
        public void ClampHP() => HP = Mathf.Min(HP, MaxHP);

        /// <summary>Moves the player instantly (new floor, tests). Yaw in degrees.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            cc.enabled = false;
            transform.position = position;
            cc.enabled = true;
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            SetLook(yaw, 0f);
        }
    }
}
