// CrateBarricade.cs
// A heavy cargo crate the player pushes (or pulls) by holding E. Used to block routes and as cover.
//
// HOW THE PUSH WORKS: while E is held, each physics step nudges the crate's velocity toward the
// velocity the player is trying to move at, capped at maxPushSpeed. The crate follows the player's movement,
// so walking backwards pulls it back out. That is why a crate shoved into a doorway can't trap you.
// Enemies have no way to damage it (GDD), so there is no health here.

using Override.Core;
using UnityEngine;

namespace Override.Interaction
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public class CrateBarricade : MonoBehaviour, IHoldInteractable
    {
        [Header("Physics (starting values)")]
        [Tooltip("kg: too heavy to shove by walking into it. 0 = keep the Rigidbody's own mass (Shajeeve's light bay crates).")]
        [SerializeField] float mass = 80f;
        [SerializeField] float linearDamping = 3f;    // stops quickly when released
        [SerializeField] float friction = 0.6f;

        [Header("Pushing")]
        [SerializeField] float maxPushSpeed = 2f;     // m/s, crate and player while pushing
        [SerializeField] float maxAcceleration = 8f;  // m/s², how quickly it reaches that speed
        [SerializeField] float maxHoldDistance = 2.5f; // let go if the player walks away (measured to the crate's surface)

        [Header("Noise (GDD)")]
        [SerializeField] float noiseRadius = 8f;
        [SerializeField] float noiseInterval = 0.5f;
        [SerializeField] float noiseMinSpeed = 0.3f;

        Rigidbody body;
        BoxCollider box;
        PlayerInteractor pusher;
        Collider pusherCollider;
        float noiseTimer;

        public string Prompt => "Push crate (hold)";
        public bool CanInteract => pusher == null;
        public bool IsBeingPushed => pusher != null;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            box = GetComponent<BoxCollider>();

            if (mass > 0f) body.mass = mass;
            body.linearDamping = linearDamping;
            // No rotation at all: it never tips over, and it can't twist and wedge itself in a doorway or corner.
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;   // moves at most 2 m/s
            body.interpolation = RigidbodyInterpolation.None;

            // A shared physics material would be better for many crates; one per crate is fine for a few.
            box.material = new PhysicsMaterial("Crate")
            {
                dynamicFriction = friction,
                staticFriction = friction,
                frictionCombine = PhysicsMaterialCombine.Average,
            };
        }

        public void Interact(PlayerInteractor player)
        {
            pusher = player;
            pusherCollider = player.GetComponent<CharacterController>();
            pusher.Controller.SpeedCap = maxPushSpeed;   // the player slows to the crate's speed
            // No damping while pushed (we control its speed directly); restored on release so it stops fast.
            body.linearDamping = 0f;
            body.WakeUp();
        }

        public bool HoldTick(PlayerInteractor player, float deltaTime)
        {
            Vector3 closest = box.ClosestPoint(player.transform.position);
            return Vector3.Distance(closest, player.transform.position) <= maxHoldDistance;
        }

        public void EndHold(PlayerInteractor player)
        {
            if (pusher != null) pusher.Controller.SpeedCap = float.PositiveInfinity;
            pusher = null;
            body.linearDamping = linearDamping;
        }

        void FixedUpdate()
        {
            if (pusher == null) return;

            // Desired, not actual, velocity: the crate itself blocks the player, so their actual
            // velocity toward it is zero and the crate would never move.
            Vector3 playerVel = pusher.Controller.DesiredVelocity;
            Vector3 target = Vector3.ClampMagnitude(new Vector3(playerVel.x, 0f, playerVel.z), maxPushSpeed);
            target = SlideAlongObstacles(target);
            Vector3 current = body.linearVelocity;
            Vector3 change = target - new Vector3(current.x, 0f, current.z);

            // VelocityChange ignores mass: we set how fast it moves. Floor friction removes about
            // μ·g·Δt of speed every step, so the budget covers that plus our own acceleration;
            // otherwise friction eats the push and the crate barely moves.
            float frictionLoss = friction * -Physics.gravity.y * Time.fixedDeltaTime;
            float budget = maxAcceleration * Time.fixedDeltaTime + frictionLoss;
            change = Vector3.ClampMagnitude(change, budget);
            body.AddForce(change, ForceMode.VelocityChange);

            EmitScrapeNoise();
        }

        /// If a wall is right in front of the crate, drop the part of the push that goes into it and keep the
        /// part along it. Pushing straight into a wall every step pressed the crate against it so hard that
        /// wall friction held it in place; now it slides along walls instead of sticking.
        Vector3 SlideAlongObstacles(Vector3 target)
        {
            float speed = target.magnitude;
            if (speed < 0.01f) return target;

            const float skin = 0.05f;   // look slightly further than this step's movement
            if (!body.SweepTest(target / speed, out RaycastHit hit, speed * Time.fixedDeltaTime + skin,
                                QueryTriggerInteraction.Ignore))
                return target;
            if (hit.collider == pusherCollider) return target;   // the player is not a wall

            Vector3 normal = new Vector3(hit.normal.x, 0f, hit.normal.z);
            if (normal.sqrMagnitude < 0.0001f) return target;     // floor-like hit: ignore
            return Vector3.ProjectOnPlane(target, normal.normalized);
        }

        void EmitScrapeNoise()
        {
            noiseTimer -= Time.fixedDeltaTime;
            Vector3 v = body.linearVelocity;
            if (noiseTimer > 0f || new Vector2(v.x, v.z).magnitude < noiseMinSpeed) return;
            NoiseBus.Emit(transform.position, noiseRadius, NoiseKind.Crate);
            noiseTimer = noiseInterval;
        }

        void OnDisable()
        {
            if (pusher != null) pusher.StopHolding();
        }
    }
}
