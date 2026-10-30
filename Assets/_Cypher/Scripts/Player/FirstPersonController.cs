using System;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Player
{
    /// <summary>
    /// First-person character: a CharacterController driven by Core's PlayerMotor, with the camera
    /// as a child at eye height. The transform origin is the character's feet. Movement rules and
    /// constants live in Core; this component reads input, performs the physics move and applies
    /// the result to the camera.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class FirstPersonController : MonoBehaviour
    {
        /// <summary>Climbable kerb height; the prototype's autostep. Kerbs are 0.185 m.</summary>
        public const float StepOffset = 0.42f;
        public const float SlopeLimit = 50f;
        public const float SkinWidth = 0.02f;

        [SerializeField] Transform cameraPivot;
        [SerializeField] float sensitivity = (float)LookState.DefaultSensitivity;
        [SerializeField] bool invertY;
        [SerializeField] bool lockCursor = true;
        [SerializeField] LayerMask headroomMask = ~0;
        [SerializeField, Tooltip("Tick from Update. Tests turn this off and call Tick with a fixed step.")] bool autoTick = true;

        readonly Collider[] _headroomHits = new Collider[8];
        CharacterController _controller;
        InputSystemCommandSource _ownedSource;
        float _appliedHeight = -1f;

        public PlayerMotor Motor { get; } = new PlayerMotor();
        public LookState Look { get; } = new LookState();

        /// <summary>Gun recoil and shake: added to the view and decaying back, never moving the aim itself.</summary>
        public CameraKick Kick { get; } = new CameraKick();

        /// <summary>Where commands come from. Defaults to keyboard, mouse and gamepad.</summary>
        public IPlayerCommandSource CommandSource { get; set; }

        /// <summary>The player's keyboard, mouse and gamepad controls for the default command source; null means the defaults.</summary>
        public InputBindings Bindings { get; set; }

        public bool AutoTick
        {
            get => autoTick;
            set => autoTick = value;
        }

        /// <summary>Radians of turn per look-input unit; scripted drivers need it to turn a wanted angle into input.</summary>
        public float Sensitivity => sensitivity;

        public bool InvertY => invertY;

        /// <summary>Take the player's look settings (sensitivity multiplier and invert-Y) from the Options.</summary>
        public void ApplyGameplay(GameplayOptions gameplay)
        {
            if (gameplay == null) throw new ArgumentNullException(nameof(gameplay));
            sensitivity = (float)gameplay.LookSensitivity;
            invertY = gameplay.InvertY;
        }

        public Transform CameraPivot
        {
            get => cameraPivot;
            set => cameraPivot = value;
        }

        public CharacterController Controller => _controller ? _controller : (_controller = GetComponent<CharacterController>());

        void Awake()
        {
            ConfigureController();
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (CommandSource == null)
            {
                _ownedSource = new InputSystemCommandSource(Bindings ?? new InputBindings());
                CommandSource = _ownedSource;
            }
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void OnDisable()
        {
            if (_ownedSource == null) return;
            if (ReferenceEquals(CommandSource, _ownedSource)) CommandSource = null;
            _ownedSource.Dispose();
            _ownedSource = null;
        }

        void Update()
        {
            // A frame that took no time has nothing to simulate. It happens on the first frame after unpausing, when the controller is
            // switched on in a frame whose delta was already fixed at zero. An explicit Tick with a bad dt still throws.
            float dt = Time.deltaTime;
            if (autoTick && dt > 0f) Tick(dt);
        }

        /// <summary>Advance the player by one frame: read input, move, update camera.</summary>
        public void Tick(float dt)
        {
            if (CommandSource == null) throw new InvalidOperationException("FirstPersonController has no CommandSource.");
            if (_appliedHeight < 0f) ConfigureController();

            PlayerCommand cmd = CommandSource.Poll(dt);
            Look.Apply(cmd.LookX, cmd.LookY, sensitivity, invertY);
            transform.rotation = Quaternion.Euler(0f, (float)(Look.Yaw * Mathf.Rad2Deg), 0f);

            Vec3 move = Motor.Step(cmd, Look.Yaw, dt, Motor.Crouching && HeadBlocked());
            ApplyStance();

            CollisionFlags flags = Controller.Move(new Vector3((float)move.X, (float)move.Y, (float)move.Z));
            Motor.ReportResult(Controller.isGrounded, (flags & CollisionFlags.Above) != 0);

            Kick.Step(dt);
            UpdateCamera();
        }

        /// <summary>Put the character at a new position at rest. The controller must be disabled while it is moved.</summary>
        public void Teleport(Vector3 feetPosition, float yawDegrees = 0f)
        {
            Controller.enabled = false;
            transform.position = feetPosition;
            Controller.enabled = true;
            Look.Set(yawDegrees * Mathf.Deg2Rad, Look.Pitch);
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            Motor.ResetMotion();
        }

        void ConfigureController()
        {
            CharacterController c = Controller;
            c.radius = (float)PlayerMotor.Radius;
            c.stepOffset = StepOffset;
            c.slopeLimit = SlopeLimit;
            c.skinWidth = SkinWidth;
            c.minMoveDistance = 0f;   // gravity alone moves ~6 mm per frame; the default would swallow it
            _appliedHeight = -1f;
            ApplyStance();
            UpdateCamera();
        }

        /// <summary>Resize the capsule about the feet when the stance changes.</summary>
        void ApplyStance()
        {
            float height = (float)Motor.ColliderHeight;
            if (Mathf.Approximately(height, _appliedHeight)) return;
            Controller.height = height;
            Controller.center = new Vector3(0f, height / 2f, 0f);
            _appliedHeight = height;
        }

        void UpdateCamera()
        {
            if (cameraPivot == null) return;
            (double shakeX, double shakeY) = Kick.ShakeOffset(Time.timeAsDouble);
            cameraPivot.localPosition = new Vector3((float)shakeX, (float)(Motor.EyeHeight + shakeY), 0f);
            // the kick looks up (positive pitch) and aside; Unity's pitch is negative upwards
            cameraPivot.localRotation = Quaternion.Euler((float)(-(Look.Pitch + Kick.Pitch) * Mathf.Rad2Deg), (float)(-Kick.Yaw * Mathf.Rad2Deg), 0f);
        }

        /// <summary>
        /// True when something other than this character occupies the space a standing capsule would
        /// need above a crouched one. The probe is slightly narrower than the capsule so brushing a
        /// wall beside the player does not count as a ceiling.
        /// </summary>
        bool HeadBlocked()
        {
            const float probeRadius = (float)PlayerMotor.Radius * 0.9f;
            Vector3 feet = transform.position;
            Vector3 low = feet + Vector3.up * ((float)PlayerMotor.HeightCrouch + probeRadius);
            Vector3 high = feet + Vector3.up * ((float)PlayerMotor.HeightStand - probeRadius);

            int count = Physics.OverlapCapsuleNonAlloc(low, high, probeRadius, _headroomHits, headroomMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (_headroomHits[i] != Controller) return true;
            }
            return false;
        }
    }
}
