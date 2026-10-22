using System.Collections;
using Override.Combat;
using UnityEngine;

namespace Override.Hunter
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class SeekerVisuals : MonoBehaviour
    {
        [Header("Hover")]
        [SerializeField] private Transform visual;
        [SerializeField] private Transform sensorCore;
        [SerializeField] private float hoverAmplitude = 0.08f;
        [SerializeField] private float hoverFrequency = 1.5f;

        [Header("Movement")]
        [SerializeField] private float maxLean = 10f;
        [SerializeField] private float maxBank = 14f;
        [SerializeField] private float leanResponse = 7f;
        [SerializeField] private float bankResponse = 8f;
        [SerializeField] private float referenceSpeed = 6f;

        [Header("Attack")]
        [SerializeField] private float chargeResponse = 9f;
        [SerializeField] private float lungeDuration = 0.22f;
        [SerializeField] private float lungeDistance = 0.22f;
        [SerializeField] private float lungeSquash = 0.18f;
        [SerializeField] private float sensorChargeScale = 0.22f;

        [Header("Hurt")]
        [SerializeField] private float hurtDuration = 0.18f;
        [SerializeField] private float hurtShake = 0.07f;

        [Header("Death")]
        [SerializeField] private float deathDuration = 1.6f;
        [SerializeField] private float deathGravity = 22f;

        private HunterBody body;
        private Health health;
        private HunterDebugView debugView;
        private Vector3 visualRestPosition;
        private Quaternion visualRestRotation;
        private Vector3 visualRestScale;
        private Vector3 sensorRestScale;
        private Vector3 previousRootPosition;
        private float lastRootYaw;
        private float currentPitch;
        private float currentBank;
        private float charge;
        private float lungeTimer = -1f;
        private float hurtTimer = -1f;
        private bool isDead;

        private void Awake()
        {
            body = GetComponent<HunterBody>();
            health = GetComponent<Health>();
            debugView = GetComponent<HunterDebugView>();

            if (visual == null) visual = transform.Find("Visual");
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual == null) visual = transform;

            if (sensorCore == null) sensorCore = visual.Find("SensorCore");

            visualRestPosition = visual.localPosition;
            visualRestRotation = visual.localRotation;
            visualRestScale = visual.localScale;
            if (sensorCore != null) sensorRestScale = sensorCore.localScale;

            previousRootPosition = transform.position;
            lastRootYaw = transform.eulerAngles.y;
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.Damaged += OnDamaged;
                health.Died += OnDied;
            }

            if (body != null) body.Lunged += OnAttacked;
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
            }

            if (body != null) body.Lunged -= OnAttacked;
        }

        private void LateUpdate()
        {
            if (isDead || visual == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 rootDelta = transform.position - previousRootPosition;
            previousRootPosition = transform.position;
            Vector3 localVelocity = transform.InverseTransformDirection(rootDelta / dt);
            localVelocity.y = 0f;
            float speed = localVelocity.magnitude;
            float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, referenceSpeed));

            float rootYaw = transform.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(lastRootYaw, rootYaw);
            lastRootYaw = rootYaw;

            float targetBank = Mathf.Clamp(yawDelta * 4f + localVelocity.x * 2f, -maxBank, maxBank);
            currentBank = Mathf.Lerp(currentBank, targetBank, 1f - Mathf.Exp(-bankResponse * dt));

            float targetPitch = Mathf.Clamp(localVelocity.z / Mathf.Max(0.01f, referenceSpeed), -1f, 1f) * maxLean;
            currentPitch = Mathf.Lerp(currentPitch, targetPitch, 1f - Mathf.Exp(-leanResponse * dt));

            bool charging = body != null && body.CurrentCommand != null && body.CurrentCommand.Fire;
            charge = Mathf.Lerp(charge, charging ? 1f : 0f, 1f - Mathf.Exp(-chargeResponse * dt));

            float hover = Mathf.Sin(Time.time * hoverFrequency * 2f * Mathf.PI) * hoverAmplitude;
            hover *= Mathf.Lerp(1f, 0.55f, speed01) * Mathf.Lerp(1f, 0.35f, charge);

            Vector3 offset = Vector3.up * hover;
            float lungeKick = 0f;
            if (lungeTimer >= 0f)
            {
                lungeTimer += dt;
                float t = Mathf.Clamp01(lungeTimer / Mathf.Max(0.01f, lungeDuration));
                lungeKick = 1f - t;
                offset += visualRestRotation * Vector3.forward * (lungeDistance * lungeKick);
                if (t >= 1f) lungeTimer = -1f;
            }

            if (hurtTimer >= 0f)
            {
                hurtTimer += dt;
                float t = Mathf.Clamp01(hurtTimer / Mathf.Max(0.01f, hurtDuration));
                float shake = hurtShake * (1f - t);
                offset += new Vector3(
                    Random.Range(-shake, shake),
                    Random.Range(-shake, shake),
                    Random.Range(-shake, shake));
                if (t >= 1f) hurtTimer = -1f;
            }

            float chargePulse = Mathf.Sin(Time.time * 18f) * 0.035f * charge;
            Vector3 scale = visualRestScale;
            scale.x *= 1f - lungeSquash * lungeKick + chargePulse;
            scale.y *= 1f - lungeSquash * lungeKick + chargePulse;
            scale.z *= 1f + lungeSquash * 1.8f * lungeKick - chargePulse * 0.5f;

            visual.localPosition = visualRestPosition + offset;
            visual.localRotation = visualRestRotation * Quaternion.Euler(currentPitch, 0f, -currentBank);
            visual.localScale = scale;

            if (sensorCore != null)
            {
                float sensorPulse = 1f + Mathf.Sin(Time.time * 7f) * 0.04f + charge * sensorChargeScale;
                sensorCore.localScale = sensorRestScale * sensorPulse;
            }
        }

        private void OnDamaged(float amount, Vector3 hitPoint)
        {
            if (isDead) return;
            hurtTimer = 0f;
        }

        private void OnAttacked()
        {
            if (isDead) return;
            lungeTimer = 0f;
        }

        private void OnDied()
        {
            if (isDead) return;
            isDead = true;

            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
            }

            if (body != null)
            {
                body.Lunged -= OnAttacked;
                body.enabled = false;
            }

            if (debugView != null) debugView.enabled = false;

            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            StartCoroutine(DeathSequence());
        }

        private IEnumerator DeathSequence()
        {
            float elapsed = 0f;
            float fallSpeed = 0f;
            Vector3 spin = new Vector3(520f, 360f, 720f);

            while (elapsed < deathDuration)
            {
                float dt = Time.deltaTime;
                elapsed += dt;
                transform.Rotate(spin * dt, Space.World);
                fallSpeed += deathGravity * dt;
                transform.position += Vector3.down * fallSpeed * dt;
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
