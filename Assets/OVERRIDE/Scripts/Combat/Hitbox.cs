// Hitbox.cs
// A collider on part of an enemy that forwards hits to the enemy's Health with a multiplier.
// Weak points (Seeker sensor core, Warden heat vent and eye) use ×2 (GDD).
// Put it on a child collider; the Health sits on the enemy's root.

using Override.Core;
using UnityEngine;

namespace Override.Combat
{
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour, IDamageable
    {
        [SerializeField] float multiplier = 2f;

        Health owner;

        public bool IsWeakPoint => multiplier > 1f;
        public Health Owner => owner;

        void Awake() => owner = GetComponentInParent<Health>();

        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (owner != null) owner.TakeDamage(amount * multiplier, hitPoint, hitNormal);
        }
    }
}
