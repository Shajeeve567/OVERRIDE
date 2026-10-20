// Health.cs
// Hit points for the player and every enemy (GDD: player 100, Wisp 60, Seeker 150, Warden 200).
// No regeneration. Other scripts react through the events instead of polling.

using System;
using Override.Core;
using UnityEngine;

namespace Override.Combat
{
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] float maxHealth = 100f;
        [Tooltip("Tick on the player only: raises the game-wide PlayerDamaged / PlayerDied events.")]
        [SerializeField] bool isPlayer;

        [Header("On death (enemies)")]
        [Tooltip("Optional pickup spawned where this dies (GDD: ammo drops from destroyed units).")]
        [SerializeField] GameObject dropOnDeath;
        [SerializeField] bool disableOnDeath = true;   // the Agent Controller can swap this for a death animation
        [Tooltip("Destroy instead of disable. Director drones need this: it counts destroyed (null) drones as gone.")]
        [SerializeField] bool destroyOnDeath;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public float Fraction => Current / maxHealth;
        public bool IsDead => Current <= 0f;
        public bool IsPlayer => isPlayer;

        /// amount, hit point. Fired for every hit that lands.
        public event Action<float, Vector3> Damaged;
        public event Action Died;
        public event Action Healed;

        void Awake() => Current = maxHealth;

        /// Set up from code (e.g. the integration tool or a spawner) before the first hit.
        public void Configure(float max, bool destroy)
        {
            maxHealth = max;
            Current = max;
            destroyOnDeath = destroy;
        }

        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (IsDead || amount <= 0f) return;

            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount, hitPoint);
            if (isPlayer) GameEvents.RaisePlayerDamaged(amount);

            if (IsDead) Die();
        }

        /// Returns how much was actually restored (0 if already full), so pickups know whether to be used up.
        public float Heal(float amount)
        {
            if (IsDead) return 0f;
            float before = Current;
            Current = Mathf.Min(maxHealth, Current + amount);
            if (Current > before) Healed?.Invoke();
            return Current - before;
        }

        /// Back to full, e.g. on respawn.
        public void ResetHealth()
        {
            Current = maxHealth;
            Healed?.Invoke();
        }

        void Die()
        {
            Died?.Invoke();
            if (isPlayer)
            {
                GameEvents.RaisePlayerDied();
                return;   // the player object stays; PlayerDeath freezes it and game flow shows the lose screen
            }
            if (dropOnDeath != null) Instantiate(dropOnDeath, transform.position + Vector3.up * 0.3f, Quaternion.identity);
            if (destroyOnDeath) Destroy(gameObject);
            else if (disableOnDeath) gameObject.SetActive(false);
        }
    }
}
