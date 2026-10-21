// HitscanRifle.cs
// The player's rifle (GDD Section 5). Hitscan: each shot is an instant raycast from the screen centre,
// with spread, camera recoil, a 30-round magazine and a 2 s reload. Goes on the Player.
//
// Visuals and gun motion listen to the events below (RifleEffects, WeaponMotion, CombatOverlay),
// so this class only handles the rules of shooting.

using System;
using Override.Core;
using Override.Interaction;
using Override.Player;
using UnityEngine;

namespace Override.Combat
{
    /// What one shot hit, for effects and hit markers.
    public struct ShotResult
    {
        public bool Hit;
        public Vector3 Point;
        public Vector3 Normal;
        public bool HitTarget;     // hit something with health
        public bool WeakPoint;
        public bool Kill;
        public bool HitStatic;     // a wall or floor: safe to leave a bullet hole on
    }

    [RequireComponent(typeof(PlayerInputReader), typeof(FirstPersonController))]
    public class HitscanRifle : MonoBehaviour
    {
        [SerializeField] Transform cameraTransform;

        [Header("Stats (GDD)")]
        [SerializeField] float damage = 15f;
        [SerializeField] float fireRate = 8f;          // shots per second
        [SerializeField] float range = 60f;            // metres
        [SerializeField] int magazineSize = 30;
        [SerializeField] float reloadTime = 1.1f;      // seconds (GDD 2.0; shortened after playtesting felt sluggish)
        [SerializeField] float gunshotNoise = 30f;     // metres

        [Header("Ammo (starting values)")]
        [SerializeField] int startingReserve = 90;
        [SerializeField] int maxReserve = 180;

        [Header("Accuracy (starting values)")]
        [SerializeField] float spreadStill = 0.5f;     // degrees
        [SerializeField] float spreadMoving = 2f;
        [SerializeField] float movingSpeed = 0.5f;     // m/s above which "moving" spread applies

        [Header("Aim down sights (GDD nice-to-have, right mouse)")]
        [SerializeField] float spreadAiming = 0.2f;    // degrees, standing still; doubled while moving
        [SerializeField] float aimRecoilScale = 0.6f;  // braced against the shoulder: less kick
        [SerializeField] float aimMoveScale = 0.6f;    // walk slower while aiming
        [SerializeField] float aimLookScale = 0.6f;    // finer mouse look while zoomed in

        [Header("Recoil (starting values)")]
        [SerializeField] float recoilPitch = 1f;       // degrees up per shot
        [SerializeField] float recoilYaw = 0.3f;       // random ± degrees per shot
        [SerializeField] float recoilRecovery = 8f;    // degrees per second back down
        [Tooltip("Recovery waits this long after a shot, so sustained fire climbs (GDD).")]
        [SerializeField] float recoveryDelay = 0.15f;

        [Header("Physics")]
        [SerializeField] float hitImpulse = 2f;        // N·s pushed into rigidbodies at the hit point
        [Tooltip("What bullets can hit. Triggers are always ignored.")]
        [SerializeField] LayerMask hitMask = ~(1 << 2);

        PlayerInputReader input;
        FirstPersonController controller;
        PlayerCarry carry;

        float nextFireTime;
        float lastShotTime = -10f;
        float reloadEndTime;
        bool triggerWasHeld;

        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public int MagazineSize => magazineSize;
        public bool IsReloading { get; private set; }
        public bool IsAiming { get; private set; }
        public float ReloadTime => reloadTime;
        /// 0 to 1 while reloading, otherwise 0.
        public float ReloadProgress => IsReloading ? 1f - Mathf.Clamp01((reloadEndTime - Time.time) / reloadTime) : 0f;
        public Transform CameraTransform => cameraTransform;

        public event Action<ShotResult> Fired;
        public event Action ReloadStarted;
        public event Action ReloadFinished;
        public event Action ReloadCancelled;
        public event Action AmmoChanged;
        public event Action DryFired;

        void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            controller = GetComponent<FirstPersonController>();
            carry = GetComponent<PlayerCarry>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

            Magazine = magazineSize;
            Reserve = startingReserve;
        }

        void OnEnable()
        {
            if (carry != null) carry.Thrown += CancelReload;   // GDD: throwing cancels a reload
        }

        void OnDisable()
        {
            if (carry != null) carry.Thrown -= CancelReload;
            CancelReload();
            IsAiming = false;
            if (controller != null) { controller.SpeedMultiplier = 1f; controller.LookMultiplier = 1f; }
        }

        void Start() => AmmoChanged?.Invoke();

        void Update()
        {
            RecoverRecoil();

            if (IsReloading && Time.time >= reloadEndTime) FinishReload();
            UpdateAiming();
            if (!controller.InputEnabled) return;

            if (input.ReloadPressed) StartReload();

            bool held = input.FireHeld;
            if (held && CanFire())
            {
                if (Magazine > 0) Fire();
                else if (!triggerWasHeld)
                {
                    // Empty: one click, then reload automatically.
                    DryFired?.Invoke();
                    StartReload();
                }
            }
            triggerWasHeld = held;
        }

        void UpdateAiming()
        {
            IsAiming = controller.InputEnabled && input.AimHeld && !controller.IsSprinting && !IsReloading
                       && (carry == null || !carry.IsCarrying);
            controller.SpeedMultiplier = IsAiming ? aimMoveScale : 1f;
            controller.LookMultiplier = IsAiming ? aimLookScale : 1f;
        }

        bool CanFire() =>
            Time.time >= nextFireTime
            && !IsReloading
            && !controller.IsSprinting                       // GDD: no firing while sprinting
            && (carry == null || !carry.IsCarrying);         // hands full with a canister

        void Fire()
        {
            Magazine--;
            nextFireTime = Time.time + 1f / fireRate;
            lastShotTime = Time.time;

            Vector3 dir = SpreadDirection();
            ShotResult result = Shoot(cameraTransform.position, dir);

            NoiseBus.Emit(transform.position, gunshotNoise, NoiseKind.Gunshot);
            ApplyRecoil();

            Fired?.Invoke(result);
            AmmoChanged?.Invoke();
        }

        Vector3 SpreadDirection()
        {
            Vector3 v = controller.Velocity;
            bool moving = new Vector2(v.x, v.z).magnitude > movingSpeed;
            float spread = IsAiming ? spreadAiming * (moving ? 2f : 1f)
                         : moving ? spreadMoving : spreadStill;

            // A random point inside a circle, at the size of the spread cone one metre ahead.
            Vector2 r = UnityEngine.Random.insideUnitCircle * Mathf.Tan(spread * Mathf.Deg2Rad);
            return (cameraTransform.forward + cameraTransform.right * r.x + cameraTransform.up * r.y).normalized;
        }

        ShotResult Shoot(Vector3 origin, Vector3 dir)
        {
            var result = new ShotResult { Point = origin + dir * range };

            // The ray starts inside the player's capsule, and rays ignore colliders they start inside.
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
                return result;

            result.Hit = true;
            result.Point = hit.point;
            result.Normal = hit.normal;

            // Hitbox (on the collider) wins over Health (on the root), so weak points get their multiplier.
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                var health = hit.collider.GetComponentInParent<Health>();
                bool wasAlive = health != null && !health.IsDead;

                // Our Hitbox applies its own multiplier when it forwards the hit; Ilzam's WardenWeakPoint
                // and SeekerWeakPoint (on his models) only mark the collider, so their multiplier is applied here.
                float amount = damage;
                bool weak = false;
                if (hit.collider.TryGetComponent(out Hitbox hitbox)) weak = hitbox.IsWeakPoint;
                else if (hit.collider.TryGetComponent(out WardenWeakPoint wardenWeakPoint))
                {
                    amount = wardenWeakPoint.ScaleDamage(damage);
                    weak = wardenWeakPoint.DamageMultiplier > 1f;
                }
                else if (hit.collider.TryGetComponent(out SeekerWeakPoint seekerWeakPoint))   // Ilzam's Seeker model
                {
                    amount = seekerWeakPoint.ScaleDamage(damage);
                    weak = seekerWeakPoint.DamageMultiplier > 1f;
                }

                target.TakeDamage(amount, hit.point, hit.normal);

                result.HitTarget = true;
                result.WeakPoint = weak;
                result.Kill = wasAlive && health.IsDead;
            }

            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(dir * hitImpulse, hit.point, ForceMode.Impulse);

            // Agents move with a CharacterController (Rithish's AgentMotor) and have no Rigidbody;
            // a bullet hole left on one would float in mid-air once it walks away.
            result.HitStatic = hit.rigidbody == null && target == null && !(hit.collider is CharacterController);
            return result;
        }

        void ApplyRecoil()
        {
            float scale = IsAiming ? aimRecoilScale : 1f;
            controller.RecoilPitch += recoilPitch * scale;
            transform.Rotate(0f, UnityEngine.Random.Range(-recoilYaw, recoilYaw) * scale, 0f);
        }

        void RecoverRecoil()
        {
            if (Time.time - lastShotTime < recoveryDelay) return;
            controller.RecoilPitch = Mathf.MoveTowards(controller.RecoilPitch, 0f, recoilRecovery * Time.deltaTime);
        }

        public void StartReload()
        {
            if (IsReloading || Magazine >= magazineSize || Reserve <= 0) return;
            IsReloading = true;
            reloadEndTime = Time.time + reloadTime;
            ReloadStarted?.Invoke();
        }

        void FinishReload()
        {
            IsReloading = false;
            int taken = Mathf.Min(magazineSize - Magazine, Reserve);
            Magazine += taken;
            Reserve -= taken;
            ReloadFinished?.Invoke();
            AmmoChanged?.Invoke();
        }

        public void CancelReload()
        {
            if (!IsReloading) return;
            IsReloading = false;
            ReloadCancelled?.Invoke();
        }

        /// Adds reserve ammo (pickups). Returns how many rounds were actually taken.
        public int AddAmmo(int amount)
        {
            int taken = Mathf.Clamp(maxReserve - Reserve, 0, amount);
            if (taken == 0) return 0;
            Reserve += taken;
            AmmoChanged?.Invoke();
            return taken;
        }
    }
}
