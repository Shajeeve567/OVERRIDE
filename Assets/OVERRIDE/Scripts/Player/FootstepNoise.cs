// FootstepNoise.cs
// Emits a footstep noise at a fixed interval while the player moves on the ground.
// The radius depends on movement mode (GDD noise table): sprinting is loud, crouching nearly silent.
// This is what lets the Hunter track a careless player by sound.

using Override.Core;
using UnityEngine;

namespace Override.Player
{
    [RequireComponent(typeof(FirstPersonController))]
    public class FootstepNoise : MonoBehaviour
    {
        [Header("Noise radius (m, GDD)")]
        [SerializeField] float crouchRadius = 1.5f;
        [SerializeField] float walkRadius = 6f;
        [SerializeField] float sprintRadius = 12f;

        [Header("Step interval (s, starting values)")]
        [SerializeField] float crouchInterval = 0.6f;
        [SerializeField] float walkInterval = 0.5f;
        [SerializeField] float sprintInterval = 0.33f;

        [SerializeField] float minSpeed = 0.5f;   // below this we count as standing still

        [Header("Debug")]
        [SerializeField] bool drawGizmo = true;

        FirstPersonController fps;
        float stepTimer;
        float lastRadius;
        float lastStepTime = -10f;

        void Awake() => fps = GetComponent<FirstPersonController>();

        void Update()
        {
            Vector3 v = fps.Velocity;
            float speed = new Vector2(v.x, v.z).magnitude;

            if (!fps.IsGrounded || speed < minSpeed)
            {
                stepTimer = 0f;   // next step fires as soon as we start moving again
                return;
            }

            stepTimer -= Time.deltaTime;
            if (stepTimer > 0f) return;

            (float radius, float interval) = fps.Mode switch
            {
                MovementMode.Crouch => (crouchRadius, crouchInterval),
                MovementMode.Sprint => (sprintRadius, sprintInterval),
                _ => (walkRadius, walkInterval),
            };

            NoiseBus.Emit(transform.position, radius, NoiseKind.Footstep);
            stepTimer = interval;
            lastRadius = radius;
            lastStepTime = Time.time;
        }

        // Draws the last step's noise radius in the Scene view for 0.3 s (enable Gizmos in Game view to see it there).
        void OnDrawGizmos()
        {
            if (!drawGizmo || !Application.isPlaying || Time.time - lastStepTime > 0.3f) return;
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, lastRadius);
        }
    }
}
