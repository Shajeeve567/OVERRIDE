// FirstPersonController.cs
// First-person movement: walk, sprint, crouch, jump and mouse look.
//
// WHY A CHARACTERCONTROLLER (not a Rigidbody) FOR THE PLAYER:
//   A CharacterController moves exactly where we tell it, never slides or tips over, and handles
//   steps and slopes for us. Physics objects (crates, canisters) are pushed by hand in
//   OnControllerColliderHit, so we keep precise FPS movement AND physical interactions.
//
// HIERARCHY THIS SCRIPT EXPECTS:
//   Player (CharacterController, PlayerInputReader, this script)  <- turns left/right (yaw)
//     CameraRoot                                                   <- tilts up/down (pitch)
//       Main Camera

using UnityEngine;

namespace Override.Player
{
    public enum MovementMode { Crouch, Walk, Sprint }

    [RequireComponent(typeof(CharacterController), typeof(PlayerInputReader))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform cameraRoot;

        [Header("Speeds (m/s, GDD)")]
        [SerializeField] float walkSpeed = 4f;
        [SerializeField] float sprintSpeed = 7f;
        [SerializeField] float crouchSpeed = 2f;
        [SerializeField] float acceleration = 12f;   // how fast we reach target speed; higher = snappier

        [Header("Jump and gravity")]
        [SerializeField] float jumpHeight = 1f;      // GDD: jump is for feel only
        [SerializeField] float gravity = -19.6f;     // 2g: snappier than real

        [Header("Crouch")]
        [SerializeField] float standingHeight = 1.8f;
        [SerializeField] float crouchHeight = 1.0f;  // fits under the 1.2 m vents
        [SerializeField] float standingCameraY = 1.6f;
        [SerializeField] float crouchCameraY = 0.9f;
        [SerializeField] float crouchTransitionSpeed = 8f;
        [Tooltip("Layers that can block standing up (walls, ceilings, vent lintels).")]
        [SerializeField] LayerMask ceilingMask = ~0;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.1f; // degrees per pixel of mouse movement
        [SerializeField] float pitchClamp = 85f;

        [Header("Pushing light rigidbodies")]
        [SerializeField] float pushMassLimit = 10f;  // heavier bodies (crates: 80 kg) use the hold-E push
        [SerializeField] float pushImpulse = 2f;     // N·s along the move direction

        CharacterController controller;
        PlayerInputReader input;

        Vector3 horizontalVelocity;
        float verticalVelocity;
        float pitch;
        bool isCrouching;

        public MovementMode Mode { get; private set; } = MovementMode.Walk;
        public bool IsGrounded => controller.isGrounded;
        public Vector3 Velocity => controller.velocity;
        public bool IsSprinting => Mode == MovementMode.Sprint && horizontalVelocity.sqrMagnitude > 0.1f;

        /// Other systems (crate pushing, menus, death) can cap speed or freeze input.
        public float SpeedCap { get; set; } = float.PositiveInfinity;
        public bool InputEnabled { get; set; } = true;

        /// Camera pitch offset added by weapon recoil (degrees, positive = up).
        public float RecoilPitch { get; set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputReader>();

            controller.height = standingHeight;
            controller.center = new Vector3(0f, standingHeight / 2f, 0f);
        }

        void OnEnable() => SetCursorLocked(true);
        void OnDisable() => SetCursorLocked(false);

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            if (InputEnabled) Look();
            UpdateCrouch();
            Move();
        }

        void Look()
        {
            Vector2 delta = input.LookDelta * mouseSensitivity;
            transform.Rotate(0f, delta.x, 0f);                    // yaw turns the whole body
            pitch = Mathf.Clamp(pitch - delta.y, -pitchClamp, pitchClamp);
            if (cameraRoot != null)
                cameraRoot.localRotation = Quaternion.Euler(pitch - RecoilPitch, 0f, 0f);
        }

        void UpdateCrouch()
        {
            bool wantsCrouch = InputEnabled && input.CrouchHeld;

            // Only stand up if nothing is above our head (e.g. we're inside a vent).
            if (isCrouching && !wantsCrouch && CanStandUp()) isCrouching = false;
            else if (wantsCrouch) isCrouching = true;

            float targetHeight = isCrouching ? crouchHeight : standingHeight;
            float targetCameraY = isCrouching ? crouchCameraY : standingCameraY;
            float t = crouchTransitionSpeed * Time.deltaTime;

            controller.height = Mathf.MoveTowards(controller.height, targetHeight, t);
            controller.center = new Vector3(0f, controller.height / 2f, 0f);

            if (cameraRoot != null)
            {
                Vector3 p = cameraRoot.localPosition;
                p.y = Mathf.MoveTowards(p.y, targetCameraY, t);
                cameraRoot.localPosition = p;
            }
        }

        bool CanStandUp()
        {
            // Cast a sphere from the top of the crouched capsule up to standing height.
            // Casts ignore colliders they start inside, so our own capsule is not hit.
            float radius = controller.radius * 0.95f;
            Vector3 origin = transform.position + Vector3.up * (controller.height - radius);
            float distance = standingHeight - controller.height;
            return !Physics.SphereCast(origin, radius, Vector3.up, out _, distance,
                                       ceilingMask, QueryTriggerInteraction.Ignore);
        }

        void Move()
        {
            Vector2 moveInput = InputEnabled ? input.Move : Vector2.zero;
            bool moving = moveInput.sqrMagnitude > 0.01f;

            // Sprint only forwards-ish and never while crouched.
            bool sprint = InputEnabled && input.SprintHeld && !isCrouching && moveInput.y > 0.1f;
            Mode = isCrouching ? MovementMode.Crouch : sprint ? MovementMode.Sprint : MovementMode.Walk;

            float targetSpeed = Mode switch
            {
                MovementMode.Crouch => crouchSpeed,
                MovementMode.Sprint => sprintSpeed,
                _ => walkSpeed,
            };
            targetSpeed = Mathf.Min(targetSpeed, SpeedCap);

            Vector3 wishDir = transform.right * moveInput.x + transform.forward * moveInput.y;
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();   // diagonals aren't faster
            Vector3 targetVelocity = moving ? wishDir * targetSpeed : Vector3.zero;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity,
                                                     acceleration * targetSpeed * Time.deltaTime);

            if (controller.isGrounded)
            {
                // A small downward push keeps isGrounded true on slopes and steps.
                if (verticalVelocity < 0f) verticalVelocity = -2f;
                if (InputEnabled && input.JumpPressed && !isCrouching)
                    verticalVelocity = Mathf.Sqrt(2f * -gravity * jumpHeight);   // v = √(2gh)
            }
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);
        }

        // Called by Unity when our capsule bumps into a collider during Move().
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.rigidbody;
            if (body == null || body.isKinematic || body.mass > pushMassLimit) return;
            if (hit.moveDirection.y < -0.3f) return;   // standing on it, not pushing it

            Vector3 pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z).normalized;

            // This runs every frame we touch it, so only push while the body is slower than us.
            // That caps its speed instead of adding impulse forever.
            float bodySpeedAlongPush = Vector3.Dot(body.linearVelocity, pushDir);
            if (bodySpeedAlongPush < horizontalVelocity.magnitude)
                body.AddForceAtPosition(pushDir * pushImpulse, hit.point, ForceMode.Impulse);
        }
    }
}
