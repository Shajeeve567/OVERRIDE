using UnityEngine;

namespace Override.Motor
{
    public class WispVisuals : MonoBehaviour
    {
        [Header("Hover Settings")]
        public float hoverAmplitude = 0.2f;
        public float hoverFrequency = 2f;

        [Header("Banking Settings")]
        public float bankAmount = 15f;
        public float bankSpeed = 5f;

        [Header("Locomotion Lean")]
        public float leanAmount = 12f;
        public float leanSpeed = 6f;
        public float referenceSpeed = 5f;

        [Header("Animation Culling")]
        public float cullDistance = 30f;

        [Header("Spawn Animation")]
        public float spawnDuration = 0.35f;
        public float spawnStartScale = 0.15f;

        [Header("Attack Animation")]
        public float attackDuration = 0.18f;
        public float attackRecoil = 0.25f;
        public float attackPitch = 10f;
        public float attackScalePulse = 0.08f;

        [Header("Hurt Animation")]
        public float hurtDuration = 0.15f;
        public float hurtShake = 0.06f;

        [Header("Death Animation")]
        public float deathDuration = 0.6f;
        public float deathScale = 0.35f;
        public float deathTilt = 35f;

        [Header("Hover Audio")]
        [SerializeField] private AudioSource hoverAudio;
        [SerializeField] private float hoverHumVolume = 0.08f;
        [SerializeField] private float hoverHumMinDistance = 2f;
        [SerializeField] private float hoverHumMaxDistance = 20f;

        [Header("Optional FX")]
        [SerializeField] private TrailRenderer trail;
        [SerializeField] private Light glowLight;
        [SerializeField] private float glowBaseIntensity = 0.8f;
        [SerializeField] private float glowAttackIntensity = 2f;

        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;
        private Vector3 baseLocalScale;
        private Transform wispRoot;
        private AgentMotor motor;
        private float lastRootYaw;
        private float currentBank;
        private float currentLean;
        private float currentRoll;

        private static AudioClip hoverHumClip;

        private float spawnTimer = -1f;
        private float attackTimer = -1f;
        private float hurtTimer = -1f;
        private float deathTimer = -1f;
        private bool isDead;
        private bool isCulled;

        private Transform player;
        private MeshRenderer[] meshRenderers;

        private void Awake()
        {
            baseLocalPosition = transform.localPosition;
            baseLocalRotation = transform.localRotation;
            baseLocalScale = transform.localScale;
            wispRoot = transform.parent != null ? transform.parent : transform;
            motor = GetComponentInParent<AgentMotor>();
            lastRootYaw = wispRoot.eulerAngles.y;

            meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            EnsureWispIsHittable();
            EnsureHoverAudio();
        }

        private void Start()
        {
            FindPlayer();
        }

        private void Update()
        {
            if (player == null) FindPlayer();

            if (player != null)
            {
                float sqrDistance = (player.position - transform.position).sqrMagnitude;
                bool shouldCull = sqrDistance > cullDistance * cullDistance;

                if (shouldCull != isCulled)
                {
                    SetVisible(!shouldCull);
                }
            }

            float dt = Time.deltaTime;
            float rootYaw = wispRoot.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(lastRootYaw, rootYaw);
            lastRootYaw = rootYaw;

            if (isCulled)
            {
                currentBank = Mathf.Lerp(currentBank, 0f, dt * bankSpeed);
                return;
            }

            float targetBank = isDead ? 0f : Mathf.Clamp(yawDelta * 5f, -bankAmount, bankAmount);
            currentBank = Mathf.Lerp(currentBank, targetBank, dt * bankSpeed);

            float speed01 = 0f;
            float targetLean = 0f;
            float targetRoll = 0f;
            if (!isDead && motor != null)
            {
                Vector3 planarVelocity = motor.Velocity;
                planarVelocity.y = 0f;
                speed01 = Mathf.Clamp01(planarVelocity.magnitude / Mathf.Max(0.01f, referenceSpeed));

                Vector3 localVelocity = wispRoot.InverseTransformDirection(planarVelocity);
                targetLean = Mathf.Clamp(localVelocity.z / Mathf.Max(0.01f, referenceSpeed), -1f, 1f) * leanAmount;
                targetRoll = -Mathf.Clamp(localVelocity.x / Mathf.Max(0.01f, referenceSpeed), -1f, 1f) * leanAmount * 0.5f;
            }

            currentLean = Mathf.Lerp(currentLean, targetLean, dt * leanSpeed);
            currentRoll = Mathf.Lerp(currentRoll, targetRoll, dt * leanSpeed);

            float hover = isDead ? 0f : Mathf.Sin(Time.time * hoverFrequency) * hoverAmplitude * Mathf.Lerp(1f, 0.65f, speed01);
            Vector3 offset = new Vector3(0f, hover, 0f);
            float pitch = 0f;
            float yawOffset = 0f;
            float scale = 1f;

            if (spawnTimer >= 0f)
            {
                spawnTimer += dt;
                float t = Mathf.Clamp01(spawnTimer / Mathf.Max(0.01f, spawnDuration));
                scale *= Mathf.SmoothStep(spawnStartScale, 1f, t);
                yawOffset = (1f - t) * 360f;
                if (t >= 1f) spawnTimer = -1f;
            }

            if (attackTimer >= 0f)
            {
                attackTimer += dt;
                float t = Mathf.Clamp01(attackTimer / Mathf.Max(0.01f, attackDuration));
                float kick = 1f - t;

                offset += baseLocalRotation * Vector3.back * (attackRecoil * kick);
                pitch -= attackPitch * kick;
                scale *= 1f + attackScalePulse * kick;

                if (t >= 1f) attackTimer = -1f;
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

            if (deathTimer >= 0f)
            {
                deathTimer += dt;
                float t = Mathf.Clamp01(deathTimer / Mathf.Max(0.01f, deathDuration));
                scale *= Mathf.Lerp(1f, deathScale, t);
                pitch += deathTilt * t;
            }

            pitch += currentLean;

            transform.localPosition = baseLocalPosition + offset;
            transform.localRotation = baseLocalRotation * Quaternion.Euler(pitch, yawOffset, -currentBank + currentRoll);
            transform.localScale = baseLocalScale * scale;

            UpdateGlow();
        }

        public void PlaySpawn()
        {
            if (isDead) return;
            spawnTimer = 0f;
            deathTimer = -1f;

            if (hoverAudio != null && !hoverAudio.isPlaying) hoverAudio.Play();
        }

        public void PlayAttack()
        {
            if (isDead) return;
            attackTimer = 0f;
        }

        public void PlayHurt()
        {
            if (isDead) return;
            hurtTimer = 0f;
        }

        public void PlayDeath()
        {
            if (isDead) return;

            isDead = true;
            spawnTimer = -1f;
            attackTimer = -1f;
            hurtTimer = -1f;
            deathTimer = 0f;

            StopHoverAudio();
        }

        public void SetVisible(bool visible)
        {
            isCulled = !visible;
            SetRenderersVisible(visible);

            if (trail != null)
            {
                trail.enabled = visible;
                if (visible) trail.Clear();
            }

            if (glowLight != null)
            {
                glowLight.enabled = visible;
            }

            if (hoverAudio != null && !isDead)
            {
                if (visible && !hoverAudio.isPlaying) hoverAudio.Play();
                hoverAudio.mute = !visible;
            }
        }

        private void UpdateGlow()
        {
            if (glowLight == null) return;

            float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.15f;
            float attackBoost = attackTimer >= 0f ? glowAttackIntensity : 0f;
            float spawnBoost = 0f;

            if (spawnTimer >= 0f)
            {
                float t = Mathf.Clamp01(spawnTimer / Mathf.Max(0.01f, spawnDuration));
                spawnBoost = (1f - t) * glowAttackIntensity * 0.6f;
            }

            float deathFade = deathTimer >= 0f
                ? 1f - Mathf.Clamp01(deathTimer / Mathf.Max(0.01f, deathDuration))
                : 1f;

            glowLight.intensity = (glowBaseIntensity * pulse + attackBoost + spawnBoost) * deathFade;
        }

        private void EnsureHoverAudio()
        {
            if (hoverAudio == null) hoverAudio = GetComponent<AudioSource>();
            if (hoverAudio == null) hoverAudio = gameObject.AddComponent<AudioSource>();

            if (hoverAudio.clip == null)
            {
                if (hoverHumClip == null) hoverHumClip = CreateHoverHumClip();
                hoverAudio.clip = hoverHumClip;
            }

            hoverAudio.loop = true;
            hoverAudio.playOnAwake = false;
            hoverAudio.spatialBlend = 1f;
            hoverAudio.volume = hoverHumVolume;
            hoverAudio.minDistance = hoverHumMinDistance;
            hoverAudio.maxDistance = hoverHumMaxDistance;
            hoverAudio.rolloffMode = AudioRolloffMode.Linear;
            hoverAudio.dopplerLevel = 0f;

            if (!isDead && !hoverAudio.isPlaying) hoverAudio.Play();
        }

        private void StopHoverAudio()
        {
            if (hoverAudio == null) return;

            hoverAudio.mute = false;
            hoverAudio.Stop();
        }

        private static AudioClip CreateHoverHumClip()
        {
            const int sampleRate = 44100;
            const int sampleCount = sampleRate;
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float tone = Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.55f +
                             Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.2f;
                float pulse = 0.75f + Mathf.Sin(2f * Mathf.PI * 2f * t) * 0.25f;
                samples[i] = tone * pulse * 0.45f;
            }

            AudioClip clip = AudioClip.Create("WispHoverHum", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void EnsureWispIsHittable()
        {
            AgentMotor motor = GetComponentInParent<AgentMotor>();
            GameObject wispRootObject = motor != null ? motor.gameObject : transform.root.gameObject;

            if (wispRootObject.GetComponent<Collider>() == null)
            {
                wispRootObject.AddComponent<CapsuleCollider>();
            }

            if (wispRootObject.GetComponent<WispHealth>() == null)
            {
                wispRootObject.AddComponent<WispHealth>();
            }
        }

        private void FindPlayer()
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null) player = playerObject.transform;
        }

        private void SetRenderersVisible(bool visible)
        {
            if (meshRenderers == null) return;

            foreach (MeshRenderer meshRenderer in meshRenderers)
            {
                if (meshRenderer != null) meshRenderer.enabled = visible;
            }
        }
    }
}
