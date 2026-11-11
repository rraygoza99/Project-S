using UnityEngine;

namespace Spellcaster
{
    /// <summary>First-person movement, mouse look, health.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class Player : MonoBehaviour
    {
        public static Player Instance { get; private set; }

        public float MaxHP => Run.Instance ? Run.Instance.Stats.MaxHP : 100f;
        public float HP { get; private set; } = 100f;
        public bool IsDead => HP <= 0f;
        public bool God;

        public float MoveSpeed = 6.5f;
        public float Acceleration = 50f;
        public float MouseSensitivity = 2f;
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

        /// <summary>True when gameplay input should be read (cursor captured, not the click that captured it).</summary>
        public bool InputEnabled => AutoTest.Active || (Cursor.lockState == CursorLockMode.Locked && Time.frameCount != lockClickFrame && !IsDead);

        CharacterController cc;
        Vector3 planarVelocity;
        float verticalVelocity;
        int lockClickFrame = -1;

        public static Player Create(Vector3 position)
        {
            var go = new GameObject("Player") { layer = Layers.Player };
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
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<RetroView>();

            var p = go.AddComponent<Player>();
            p.Cam = cam;
            go.AddComponent<WizardHands>();
            return p;
        }

        void Awake()
        {
            Instance = this;
            cc = GetComponent<CharacterController>();
            Yaw = transform.eulerAngles.y;
        }

        void Start()
        {
            if (!AutoTest.Active && GameState.SkipTitleOnLoad) LockCursor();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            HurtFlash = Mathf.Max(0f, HurtFlash - dt * 2.5f);
            HealFlash = Mathf.Max(0f, HealFlash - dt * 1.5f);
            HandleCursor();
            if (IsDead) return;

            bool gameplay = GameState.Instance.AcceptsGameplayInput;
            // While channeling, the mouse draws the rune instead of turning the view.
            if (Cursor.lockState == CursorLockMode.Locked && InputEnabled && gameplay && !Channeling)
            {
                Yaw += Input.GetAxisRaw("Mouse X") * MouseSensitivity;
                Pitch = Mathf.Clamp(Pitch - Input.GetAxisRaw("Mouse Y") * MouseSensitivity, -85f, 85f);
            }
            ApplyLook();

            // WASD always moves (arrow keys are the spell inputs); channeling slows you down.
            Vector3 wish = Vector3.zero;
            if (InputEnabled && gameplay)
            {
                if (Input.GetKey(KeyCode.W)) wish.z += 1;
                if (Input.GetKey(KeyCode.S)) wish.z -= 1;
                if (Input.GetKey(KeyCode.D)) wish.x += 1;
                if (Input.GetKey(KeyCode.A)) wish.x -= 1;
            }
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            if (Channeling) wish *= ChannelSpeedFactor;

            float speed = Run.Instance ? Run.Instance.MoveSpeed : MoveSpeed;
            Vector3 target = transform.TransformDirection(wish) * speed;
            planarVelocity = Vector3.MoveTowards(planarVelocity, target, Acceleration * dt);
            verticalVelocity = cc.isGrounded ? -2f : verticalVelocity - 20f * dt;
            cc.Move((planarVelocity + Vector3.up * verticalVelocity) * dt);
        }

        void HandleCursor()
        {
            if (AutoTest.Active) return;
            if (Input.GetKeyDown(KeyCode.Escape) && !GameState.Instance.ForgeOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0) && !IsDead)
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
            if (Run.Instance) amount *= Run.Instance.Stats.DamageTaken;
            HurtFlash = 1f;
            Sfx.Play("hurt", 0.8f, Random.Range(0.9f, 1.1f));
            HP = Mathf.Max(God ? 1f : 0f, HP - amount);
            if (HP <= 0f) GameState.Instance.OnPlayerDied();
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            HP = Mathf.Min(MaxHP, HP + amount);
            HealFlash = 1f;
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
