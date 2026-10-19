// IDamageable.cs
// Anything that can take a hit: the player, enemies, and hitbox colliders that forward to them.

using UnityEngine;

namespace Override.Core
{
    public interface IDamageable
    {
        void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal);
    }
}
