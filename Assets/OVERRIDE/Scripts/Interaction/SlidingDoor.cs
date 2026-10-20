// SlidingDoor.cs
// A sci-fi facility door that slides sideways when E is pressed.
//
// PHYSICS: the panel is a KINEMATIC Rigidbody moved with MovePosition, so it travels at an exact speed
// and isn't knocked around. Before each step it sweeps its collider ahead (Rigidbody.SweepTest): if a
// crate, a canister or the player is in the way, it stops there. A crate shoved into the doorway
// therefore jams the door open: the barricade physically wins. Static walls are ignored, so the panel
// can slide into the wall beside the doorway.
//
// SETUP: a Cube for the panel, plus a Rigidbody and this script. Set Slide Offset to how far it
// moves in the door's own axes, e.g. (-1.2, 0, 0) to slide 1.2 m to its left.

using Override.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Override.Interaction
{
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class SlidingDoor : MonoBehaviour, IInteractable, IDoor
    {
        [Header("Motion")]
        [Tooltip("Where the open door sits, relative to closed, in the door's own axes (metres, ignores scale).")]
        [SerializeField] Vector3 slideOffset = new Vector3(-1.2f, 0f, 0f);
        [SerializeField] float speed = 2.5f;          // m/s, about half a second to open
        [SerializeField] bool startOpen = false;

        [Header("Noise (GDD)")]
        [SerializeField] float noiseRadius = 10f;

        [Header("Agents")]
        [Tooltip("Counts as open past this fraction of its travel (agents' paths treat it as passable).")]
        [Range(0f, 1f)] [SerializeField] float openThreshold = 0.7f;
        [Tooltip("Optional: carves a hole in the NavMesh while the door is closed.")]
        [SerializeField] NavMeshObstacle navObstacle;

        Rigidbody body;
        Vector3 closedPosition, openPosition;
        bool wantsOpen;

        /// 0 = closed, 1 = fully open.
        public float OpenFraction { get; private set; }
        public bool IsOpen => OpenFraction >= openThreshold;
        public bool IsBlocked { get; private set; }
        LockableDoor lockable;   // optional: Director lock and Core seal

        public string Prompt =>
            lockable != null && lockable.IsSealed ? "Sealed: override Terminals 1-3" :
            lockable != null && lockable.IsDirectorLocked ? "Locked by ARGUS" :
            wantsOpen ? "Close door" : "Open door";
        public bool CanInteract => true;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            lockable = GetComponent<LockableDoor>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;   // smooth motion in front of the camera

            closedPosition = transform.position;
            // Rotation only, not scale: the offset stays in metres even on a stretched cube.
            openPosition = closedPosition + transform.rotation * slideOffset;

            wantsOpen = startOpen;
            if (startOpen)
            {
                transform.position = openPosition;
                OpenFraction = 1f;
            }
        }

        public void Interact(PlayerInteractor player) => SetOpen(!wantsOpen, player.transform);

        public void SetOpen(bool open, Transform opener = null)
        {
            if (open && lockable != null && lockable.IsLocked) return;   // locked doors only close
            if (open == wantsOpen) return;
            wantsOpen = open;
            NoiseBus.Emit(transform.position, noiseRadius, NoiseKind.Door);
        }

        void FixedUpdate()
        {
            float target = wantsOpen ? 1f : 0f;
            if (!Mathf.Approximately(OpenFraction, target)) Step(target);

            if (navObstacle != null && navObstacle.carving == IsOpen) navObstacle.carving = !IsOpen;
        }

        void Step(float target)
        {
            float travel = slideOffset.magnitude;
            if (travel < 0.001f) return;

            float next = Mathf.MoveTowards(OpenFraction, target, speed * Time.fixedDeltaTime / travel);
            Vector3 from = body.position;
            Vector3 to = Vector3.Lerp(closedPosition, openPosition, next);
            Vector3 move = to - from;

            IsBlocked = IsPathBlocked(move);
            if (IsBlocked) return;

            body.MovePosition(to);
            OpenFraction = next;
        }

        bool IsPathBlocked(Vector3 move)
        {
            float distance = move.magnitude;
            if (distance < 0.0001f) return false;
            if (!body.SweepTest(move / distance, out RaycastHit hit, distance + 0.02f, QueryTriggerInteraction.Ignore))
                return false;
            // Only things that can move block it: physics props and the player. Walls don't.
            bool prop = hit.rigidbody != null && !hit.rigidbody.isKinematic;
            bool player = hit.collider is CharacterController;
            return prop || player;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 closed = Application.isPlaying ? closedPosition : transform.position;
            Vector3 open = closed + transform.rotation * slideOffset;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(closed, open);
            Gizmos.DrawWireSphere(open, 0.1f);
        }
    }
}
