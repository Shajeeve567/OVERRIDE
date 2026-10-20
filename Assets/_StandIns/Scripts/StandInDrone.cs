// STAND-IN (delete with the _StandIns folder) -> replaced by the Agent Controller's Wisp.
//
// A red sphere that flies out of its bay to the bay's waypoint, then hovers after the player and
// shoots (pretend hitscan: damage once a second while in range). Three bullet hits destroy it.
// It flies through walls: real pathfinding is the Wisp's job, this only closes the Director's loop.

using Override.Director;
using UnityEngine;

namespace Override.StandIns
{
    public sealed class StandInDrone : MonoBehaviour, IDirectorDrone
    {
        public float speed = 6f;
        public float hoverHeight = 2.5f;
        public float attackRange = 6f;
        public float damagePerShot = 5f;
        public float shotInterval = 1f;
        public int hitPoints = 3;
        public float lifetime = 40f;

        Vector3 firstStop;
        bool leftBay;
        Transform player;
        StandInPlayerHealth health;
        DirectorController director;
        float age, shotTimer;

        public void Launch(Vector3 waypoint, Transform target)
        {
            firstStop = waypoint;
            player = target;
            if (player != null) health = player.GetComponent<StandInPlayerHealth>();
            director = FindFirstObjectByType<DirectorController>();
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > lifetime) { Destroy(gameObject); return; }

            // Phase 1: out of the hatch to the waypoint. Phase 2: hover after the player.
            Vector3 goal = !leftBay || player == null ? firstStop : player.position + Vector3.up * hoverHeight;
            // Stop a little short of the player instead of sitting on their head.
            if (leftBay && player != null && Vector3.Distance(transform.position, goal) < attackRange * 0.6f) goal = transform.position;
            transform.position = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);
            if (!leftBay && (transform.position - firstStop).sqrMagnitude < 0.25f) leftBay = true;

            if (leftBay && player != null)
            {
                transform.LookAt(player.position + Vector3.up * 0.5f);
                if (Vector3.Distance(transform.position, player.position) < attackRange)
                {
                    shotTimer += Time.deltaTime;
                    if (shotTimer >= shotInterval)
                    {
                        shotTimer = 0f;
                        if (health != null) health.TakeDamage(damagePerShot);
                    }
                }
            }
        }

        // Your rifle's bullets are physical Rigidbodies. When one hits this drone's collider, Unity
        // tells BOTH objects (the drone has a kinematic Rigidbody, so collision messages reach it).
        void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.GetComponent<Bullet>() == null) return;
            if (director != null) director.ReportEngagement();   // fighting back counts as engagement
            if (--hitPoints <= 0) Destroy(gameObject);
        }
    }
}
