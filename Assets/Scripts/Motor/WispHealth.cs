using System.Collections;
using UnityEngine;
using Override.Core;
using Override.AI;

namespace Override.Motor
{
    public class WispHealth : MonoBehaviour, IDamageable
    {
        public float maxHealth = 60f;

        private float currentHealth;
        private bool isDead;

        private AgentMotor agentMotor;
        private PathfinderBrain pathfinderBrain;
        private WispVisuals wispVisuals;
        private CharacterController characterController;
        private Transform wispRoot;

        public float CurrentHealth => currentHealth;
        public bool IsDead => isDead;

        private void Awake()
        {
            agentMotor = GetComponentInParent<AgentMotor>();
            pathfinderBrain = GetComponentInParent<PathfinderBrain>();
            characterController = GetComponentInParent<CharacterController>();

            wispRoot = agentMotor != null ? agentMotor.transform : transform.root;
            wispVisuals = wispRoot.GetComponentInChildren<WispVisuals>(true);

            currentHealth = maxHealth;
            EnsureHittable();
        }

        public void TakeDamage(float amount)
        {
            ApplyDamage(amount);
        }

        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
        {
            ApplyDamage(amount);
        }

        private void ApplyDamage(float amount)
        {
            if (isDead || amount <= 0f) return;

            currentHealth = Mathf.Max(0f, currentHealth - amount);
            Debug.Log("[Wisp] Took " + amount + " damage. Health: " + currentHealth);

            if (wispVisuals != null) wispVisuals.PlayHurt();

            if (currentHealth <= 0f) Die();
        }

        private void EnsureHittable()
        {
            if (wispRoot.GetComponent<Collider>() != null) return;
            wispRoot.gameObject.AddComponent<CapsuleCollider>();
        }

        private void Die()
        {
            if (isDead) return;
            isDead = true;

            if (agentMotor != null)
            {
                agentMotor.isDead = true;
                agentMotor.enabled = false;
            }

            if (pathfinderBrain != null) pathfinderBrain.enabled = false;

            if (wispVisuals != null)
            {
                wispVisuals.SetVisible(true);
                wispVisuals.PlayDeath();
            }

            if (characterController != null) characterController.enabled = false;

            foreach (Collider collider in wispRoot.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            Debug.Log("[Wisp] Destroyed.");
            StartCoroutine(DeathSequence());
        }

        private IEnumerator DeathSequence()
        {
            float elapsed = 0f;
            float fallSpeed = 0f;
            const float duration = 2f;
            const float gravity = 25f;
            Vector3 spinSpeed = new Vector3(720f, 360f, 900f);

            while (elapsed < duration)
            {
                float dt = Time.deltaTime;
                elapsed += dt;

                wispRoot.Rotate(spinSpeed * dt, Space.World);

                fallSpeed += gravity * dt;
                wispRoot.position += Vector3.down * fallSpeed * dt;

                yield return null;
            }

            Destroy(wispRoot.gameObject);
        }
    }
}
