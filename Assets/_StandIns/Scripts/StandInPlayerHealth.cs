// STAND-IN (delete with the _StandIns folder) -> replaced by Gunitha's Health / PlayerDeath
// (feature/rifle), which raise GameEvents.PlayerDamaged and PlayerDied.
//
// 100 HP, slow regeneration after 5 s without damage, and on death the player respawns at the last
// terminal they overrode (GDD: "Respawn at the last completed terminal, with progress kept").
// Added to the Player at Play time by StandInGame; nothing is saved into the scene.

using Override.Director;
using UnityEngine;

namespace Override.StandIns
{
    public sealed class StandInPlayerHealth : MonoBehaviour
    {
        public float maxHealth = 100f;
        public float regenDelay = 5f;
        public float regenPerSecond = 4f;
        public float respawnInvulnerability = 2f;

        public float Health { get; private set; }
        public float LastDamageTime { get; private set; } = -999f;
        public Vector3 RespawnPoint { get; set; }
        public System.Action Died;

        DirectorController director;
        Rigidbody body;
        float invulnerableUntil;

        void Start()
        {
            Health = maxHealth;
            RespawnPoint = transform.position;
            director = FindFirstObjectByType<DirectorController>();
            body = GetComponent<Rigidbody>();
        }

        void Update()
        {
            if (Time.time - LastDamageTime > regenDelay && Health < maxHealth)
                Health = Mathf.Min(maxHealth, Health + regenPerSecond * Time.deltaTime);
        }

        public void TakeDamage(float amount)
        {
            if (Time.time < invulnerableUntil || Health <= 0f) return;
            Health -= amount;
            LastDamageTime = Time.time;
            if (director != null) director.ReportPlayerDamaged(amount);   // the Director's tension input
            if (Health <= 0f) Die();
        }

        void Die()
        {
            // Teleport a Rigidbody: set both the body and the transform, and stop its motion,
            // otherwise physics carries the old velocity into the new spot.
            if (body != null) { body.linearVelocity = Vector3.zero; body.position = RespawnPoint; }
            transform.position = RespawnPoint;
            Health = maxHealth;
            invulnerableUntil = Time.time + respawnInvulnerability;
            Died?.Invoke();
        }
    }
}
