// HazardZone.cs
// Damages anything with Health standing on or inside it (e.g. sparking cables, a vented steam pipe).
// Also handy for testing damage, death and the terminal's pause-on-damage without enemies.
//
// WHY AN OVERLAP CHECK (not OnTriggerStay): a thin floor slab may never touch the player's capsule,
// which floats a few centimetres up on its skin width, and trigger messages between a CharacterController
// and a trigger without a Rigidbody aren't guaranteed. Checking a box that reaches `height` metres
// above the hazard's footprint works for any shape and needs no Rigidbody.

using UnityEngine;

namespace Override.Combat
{
    [RequireComponent(typeof(Collider))]
    public class HazardZone : MonoBehaviour
    {
        [SerializeField] float damagePerTick = 10f;
        [SerializeField] float tickInterval = 0.5f;
        [Tooltip("How far above the hazard's footprint it hurts (metres).")]
        [SerializeField] float height = 2f;

        Collider zone;
        readonly Collider[] hits = new Collider[8];   // reused: no allocation per tick
        float nextTick;

        void Awake()
        {
            zone = GetComponent<Collider>();
            zone.isTrigger = true;   // walk through it, don't stand on it
        }

        void FixedUpdate()
        {
            if (Time.time < nextTick) return;
            nextTick = Time.time + tickInterval;

            Bounds b = zone.bounds;
            Vector3 centre = new Vector3(b.center.x, b.min.y + height / 2f, b.center.z);
            Vector3 halfExtents = new Vector3(b.extents.x, height / 2f, b.extents.z);
            int count = Physics.OverlapBoxNonAlloc(centre, halfExtents, hits, Quaternion.identity,
                                                   ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var health = hits[i].GetComponentInParent<Health>();
                if (health != null) health.TakeDamage(damagePerTick, hits[i].transform.position, Vector3.up);
            }
        }

        void OnDrawGizmos()
        {
            var c = GetComponent<Collider>();
            if (c == null) return;
            Bounds b = c.bounds;
            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(b.center.x, b.min.y + height / 2f, b.center.z),
                                new Vector3(b.size.x, height, b.size.z));
        }
    }
}
