// ScrapCanister.cs
// A small physics prop the player picks up (E) and throws (G). Its impact makes a loud noise,
// which lures the Hunter: the "sound propagation" moment in the demo video.

using Override.Core;
using UnityEngine;

namespace Override.Interaction
{
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class ScrapCanister : MonoBehaviour, IInteractable
    {
        [Header("Physics (starting values)")]
        [SerializeField] float mass = 2f;

        [Header("Impact noise (GDD)")]
        [SerializeField] float noiseRadius = 15f;
        [SerializeField] float minImpactSpeed = 3f;   // m/s relative speed; gentle bumps are silent
        [SerializeField] float noiseCooldown = 0.3f;  // one bounce sequence ≠ ten noises

        Rigidbody body;
        Collider col;
        Collider ignoredPlayerCollider;
        float ignoreUntil;
        float lastNoiseTime = -10f;

        public bool IsHeld { get; private set; }
        public string Prompt => "Pick up canister";
        public bool CanInteract => !IsHeld;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            body.mass = mass;
            // Continuous Dynamic: a thrown canister moves ~14 m/s, fast enough to pass through a thin
            // wall between two physics steps with Discrete detection.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Interpolation: it's the only rigidbody the camera follows closely, so smooth it.
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void Interact(PlayerInteractor player)
        {
            if (player.Carry != null) player.Carry.PickUp(this);
        }

        /// Called by PlayerCarry. While held, the canister is kinematic and has no collider,
        /// so it can't shove the player or snag on walls.
        public void AttachTo(Transform holdPoint)
        {
            IsHeld = true;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            col.enabled = false;
            transform.SetParent(holdPoint, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        /// Called by PlayerCarry to drop (velocity = player's) or throw (velocity = throw).
        public void Release(Vector3 velocity, Collider playerCollider)
        {
            IsHeld = false;
            transform.SetParent(null, true);
            col.enabled = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 5f;   // a little tumble looks thrown

            // Don't collide with the thrower for a moment, or it bounces off our own capsule.
            if (playerCollider != null)
            {
                Physics.IgnoreCollision(col, playerCollider, true);
                ignoredPlayerCollider = playerCollider;
                ignoreUntil = Time.time + 0.25f;
            }
        }

        void Update()
        {
            if (ignoredPlayerCollider != null && Time.time >= ignoreUntil)
            {
                Physics.IgnoreCollision(col, ignoredPlayerCollider, false);
                ignoredPlayerCollider = null;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.relativeVelocity.magnitude < minImpactSpeed) return;
            if (Time.time - lastNoiseTime < noiseCooldown) return;

            lastNoiseTime = Time.time;
            NoiseBus.Emit(collision.GetContact(0).point, noiseRadius, NoiseKind.Canister);
        }
    }
}
