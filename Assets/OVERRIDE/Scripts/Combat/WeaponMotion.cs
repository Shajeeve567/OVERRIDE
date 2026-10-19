// WeaponMotion.cs
// Procedural first-person gun motion: recoil kick, look sway, walk bob, a lowered sprint pose and a
// reload dip. All computed in code from the player's state, so no rig or animation files are needed
// (GDD: "procedural, in code"). Put it on GunPivot, the empty that holds the gun model under the camera.

using Override.Player;
using UnityEngine;

namespace Override.Combat
{
    public class WeaponMotion : MonoBehaviour
    {
        [SerializeField] HitscanRifle rifle;
        [SerializeField] FirstPersonController controller;
        [SerializeField] PlayerInputReader input;

        [Header("Recoil kick (per shot)")]
        [SerializeField] float kickBack = 0.04f;        // metres toward the camera
        [SerializeField] float kickPitch = 3f;          // degrees muzzle up
        [SerializeField] float kickReturn = 14f;        // higher = snaps back faster

        [Header("Look sway")]
        [SerializeField] float swayPerPixel = 0.04f;    // degrees of lag per pixel of mouse movement
        [SerializeField] float swayMax = 4f;
        [SerializeField] float swaySmooth = 10f;

        [Header("Walk bob")]
        [SerializeField] float bobAmount = 0.012f;      // metres at walking speed
        [SerializeField] float bobFrequency = 1.6f;     // cycles per metre walked... scaled by speed below

        [Header("Sprint pose")]
        [SerializeField] Vector3 sprintOffset = new Vector3(0.05f, -0.06f, -0.05f);
        [SerializeField] Vector3 sprintRotation = new Vector3(10f, -25f, 5f);
        [SerializeField] float poseSmooth = 8f;

        [Header("Reload dip")]
        [SerializeField] float reloadDrop = 0.12f;      // metres down at the middle of the reload
        [SerializeField] float reloadRoll = 30f;        // degrees tilted

        Vector3 basePosition;
        Quaternion baseRotation;

        float kick;              // 0 to ~1, decays after each shot
        Vector2 sway;            // current look sway (degrees)
        float bobPhase;
        float sprintBlend;       // 0 = ready, 1 = fully lowered

        void Awake()
        {
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
        }

        void OnEnable() { if (rifle != null) rifle.Fired += OnFired; }
        void OnDisable() { if (rifle != null) rifle.Fired -= OnFired; }

        void OnFired(ShotResult _) => kick = Mathf.Min(kick + 1f, 2f);

        void LateUpdate()
        {
            float dt = Time.deltaTime;

            // Recoil: jump on each shot, then decay exponentially back to rest.
            kick = Mathf.Lerp(kick, 0f, 1f - Mathf.Exp(-kickReturn * dt));

            // Sway: the gun lags behind the mouse a little, clamped and smoothed.
            Vector2 look = input != null ? input.LookDelta : Vector2.zero;
            Vector2 targetSway = Vector2.ClampMagnitude(new Vector2(-look.y, look.x) * swayPerPixel, swayMax);
            sway = Vector2.Lerp(sway, targetSway, 1f - Mathf.Exp(-swaySmooth * dt));

            // Bob: a figure-of-eight while walking on the ground, scaled by speed.
            Vector3 v = controller.Velocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            if (controller.IsGrounded && speed > 0.1f) bobPhase += speed * bobFrequency * dt * Mathf.PI;
            float bobScale = Mathf.Clamp01(speed / 4f) * bobAmount;
            Vector3 bob = new Vector3(Mathf.Sin(bobPhase) * bobScale, Mathf.Sin(bobPhase * 2f) * bobScale * 0.5f, 0f);

            // Sprint: blend into a lowered pose (you can't fire while sprinting, so show it).
            sprintBlend = Mathf.Lerp(sprintBlend, controller.IsSprinting ? 1f : 0f, 1f - Mathf.Exp(-poseSmooth * dt));

            // Reload: dip down and roll, peaking halfway through.
            float reloadCurve = rifle != null && rifle.IsReloading ? Mathf.Sin(rifle.ReloadProgress * Mathf.PI) : 0f;

            Vector3 position = basePosition
                             + bob
                             + new Vector3(0f, 0f, -kickBack * kick)
                             + sprintOffset * sprintBlend
                             + new Vector3(0f, -reloadDrop * reloadCurve, 0f);

            Quaternion rotation = baseRotation
                                * Quaternion.Euler(sprintRotation * sprintBlend)
                                * Quaternion.Euler(-kickPitch * kick + sway.x, sway.y, reloadRoll * reloadCurve);

            transform.localPosition = position;
            transform.localRotation = rotation;
        }
    }
}
