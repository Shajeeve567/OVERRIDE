// PlayerCarry.cs
// Holds one canister in front of the camera and throws it with G. Goes on the Player.

using System;
using Override.Player;
using UnityEngine;

namespace Override.Interaction
{
    [RequireComponent(typeof(PlayerInputReader), typeof(FirstPersonController))]
    public class PlayerCarry : MonoBehaviour
    {
        [Tooltip("Empty child of the camera, about 0.6 m in front of it.")]
        [SerializeField] Transform holdPoint;
        [SerializeField] float throwSpeed = 14f;   // m/s along the view direction
        [SerializeField] float throwLift = 2f;     // m/s extra upward, so it arcs

        PlayerInputReader input;
        FirstPersonController controller;
        Collider playerCollider;
        ScrapCanister held;

        /// Raised when the player throws. The rifle listens to cancel a reload.
        public event Action Thrown;

        public bool IsCarrying => held != null;

        void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            controller = GetComponent<FirstPersonController>();
            playerCollider = GetComponent<CharacterController>();
        }

        void Update()
        {
            if (held != null && controller.InputEnabled && input.ThrowPressed) Throw();
        }

        public void PickUp(ScrapCanister canister)
        {
            if (held != null || holdPoint == null) return;
            held = canister;
            held.AttachTo(holdPoint);
        }

        public void Throw()
        {
            if (held == null) return;
            Vector3 velocity = controller.Velocity
                             + holdPoint.forward * throwSpeed
                             + Vector3.up * throwLift;
            held.Release(velocity, playerCollider);
            held = null;
            Thrown?.Invoke();
        }

        /// Drops the canister where it is (E while carrying, or when the player dies).
        public void Drop()
        {
            if (held == null) return;
            held.Release(controller.Velocity, playerCollider);
            held = null;
        }

        void OnDisable() => Drop();
    }
}
