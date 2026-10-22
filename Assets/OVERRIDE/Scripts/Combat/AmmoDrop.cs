// AmmoDrop.cs
// A small ammo pickup dropped by destroyed enemies (GDD). Collected automatically by walking into it.
// Make a prefab with a trigger collider and this script, and set it as an enemy Health's Drop On Death.

using UnityEngine;

namespace Override.Combat
{
    [RequireComponent(typeof(Collider), typeof(Rigidbody))]
    public class AmmoDrop : MonoBehaviour
    {
        [SerializeField] int rounds = 15;
        [SerializeField] float spinSpeed = 90f;     // degrees per second, so it reads as a pickup
        [SerializeField] float lifetime = 30f;      // cleaned up if nobody takes it

        void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            // A kinematic rigidbody makes trigger events fire reliably against the player's CharacterController.
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            Destroy(gameObject, lifetime);
        }

        void Update() => transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);

        void OnTriggerEnter(Collider other)
        {
            var rifle = other.GetComponentInParent<HitscanRifle>();
            if (rifle != null && rifle.AddAmmo(rounds) > 0) Destroy(gameObject);
        }
    }
}
