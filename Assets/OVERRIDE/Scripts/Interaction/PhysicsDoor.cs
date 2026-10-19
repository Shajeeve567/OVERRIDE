// PhysicsDoor.cs
// A door that swings on a real hinge. E toggles where the hinge's spring wants to go (closed or open);
// physics does the actual swinging, so a crate in the doorway physically stops it.
// It always swings AWAY from whoever opens it, so you can't trap yourself behind your own door.
//
// SETUP: put this on the door panel (a Box Collider + Rigidbody). The script adds and configures the
// HingeJoint itself, so the numbers live here, in code, where they can be tuned and justified.
// The hinge is on the panel's left edge (local -X side) unless hingeOnRightEdge is ticked.

using Override.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Override.Interaction
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public class PhysicsDoor : MonoBehaviour, IInteractable, IDoor
    {
        [Header("Hinge (starting values)")]
        [SerializeField] bool hingeOnRightEdge = false;
        [SerializeField] float openAngle = 95f;      // degrees, either way
        [SerializeField] float spring = 300f;        // how hard it pulls toward the target angle
        [SerializeField] float damper = 30f;         // stops it bouncing
        [SerializeField] float limit = 100f;         // hinge can't swing past ±this
        [SerializeField] float mass = 30f;           // kg

        [Header("Noise (GDD)")]
        [SerializeField] float noiseRadius = 10f;

        [Header("Agents")]
        [Tooltip("Counts as open past this angle (agents' paths treat it as passable).")]
        [SerializeField] float openThreshold = 45f;
        [Tooltip("Optional: carves a hole in the NavMesh while the door is closed.")]
        [SerializeField] NavMeshObstacle navObstacle;

        Rigidbody body;
        BoxCollider box;
        HingeJoint hinge;
        bool wantsOpen;

        Quaternion closedRotation;
        // +1 if the hinge's angle grows the same way as Unity's rotation maths (Quaternion.AngleAxis), -1 if not.
        // Learned the first time the door moves, so the swing-away choice is right whatever PhysX's convention.
        float hingeSign = 1f;
        bool hingeSignKnown;

        public bool IsOpen => Mathf.Abs(hinge.angle) > openThreshold;
        public string Prompt => wantsOpen ? "Close door" : "Open door";
        public bool CanInteract => true;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            box = GetComponent<BoxCollider>();

            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.None;               // the camera never follows a door
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;  // doors swing slowly

            hinge = GetComponent<HingeJoint>();
            if (hinge == null) hinge = gameObject.AddComponent<HingeJoint>();

            // Anchor on the panel's edge, axis straight up.
            float edgeX = (hingeOnRightEdge ? 0.5f : -0.5f) * box.size.x + box.center.x;
            hinge.anchor = new Vector3(edgeX, box.center.y, box.center.z);
            hinge.axis = Vector3.up;
            hinge.useLimits = true;
            hinge.limits = new JointLimits { min = -limit, max = limit };
            hinge.useSpring = true;

            closedRotation = transform.rotation;
            ApplyTarget(0f);
        }

        public void Interact(PlayerInteractor player) => SetOpen(!wantsOpen, player.transform);

        public void SetOpen(bool open, Transform opener = null)
        {
            if (open == wantsOpen) return;
            wantsOpen = open;
            ApplyTarget(open ? SwingAwayFrom(opener) * openAngle : 0f);
            NoiseBus.Emit(transform.position, noiseRadius, NoiseKind.Door);
        }

        /// +1 or -1: which way to swing so the free edge moves away from the opener.
        float SwingAwayFrom(Transform opener)
        {
            if (opener == null) return hingeSign;
            Vector3 axis = transform.TransformDirection(hinge.axis);
            Vector3 hingePoint = transform.TransformPoint(hinge.anchor);
            Vector3 toCentre = box.bounds.center - hingePoint;
            // A small positive rotation moves the panel along cross(axis, toCentre).
            Vector3 panelMoves = Vector3.Cross(axis, toCentre);
            Vector3 toOpener = opener.position - box.bounds.center;
            float rotationSign = Vector3.Dot(panelMoves, toOpener) > 0f ? -1f : 1f;
            return rotationSign * hingeSign;
        }

        void ApplyTarget(float angle)
        {
            hinge.spring = new JointSpring { spring = spring, damper = damper, targetPosition = angle };
            body.WakeUp();   // react even if the body was asleep
        }

        void FixedUpdate()
        {
            LearnHingeSign();
            // Closed doors block agent paths. Only touch the obstacle when the state flips.
            if (navObstacle != null && navObstacle.carving == IsOpen) navObstacle.carving = !IsOpen;
        }

        void LearnHingeSign()
        {
            if (hingeSignKnown || Mathf.Abs(hinge.angle) < 10f) return;
            Vector3 axis = transform.TransformDirection(hinge.axis);
            Vector3 closedForward = closedRotation * Vector3.forward;
            float actual = Vector3.SignedAngle(closedForward, transform.forward, axis);
            if (Mathf.Abs(actual) < 10f) return;
            hingeSign = Mathf.Sign(actual) == Mathf.Sign(hinge.angle) ? 1f : -1f;
            hingeSignKnown = true;
        }
    }
}
