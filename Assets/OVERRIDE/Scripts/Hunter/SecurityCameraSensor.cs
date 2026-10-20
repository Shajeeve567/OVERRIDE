// SecurityCameraSensor.cs
// Makes one of Shajeeve's security camera props actually watch: if the player is inside its view cone,
// within range and not behind a wall, it reports a sighting to the CameraNetwork twice a second.
// It looks along its "Head" child's forward axis (the part Shajeeve's PropBuilder aims at the room).

using UnityEngine;

namespace Override.Hunter
{
    public class SecurityCameraSensor : MonoBehaviour
    {
        [SerializeField] Transform lens;            // auto: the "Head" child
        [SerializeField] float fieldOfView = 70f;   // degrees, full cone
        [SerializeField] float range = 20f;         // metres (the level is 2.5x scale)
        [SerializeField] float checkInterval = 0.5f;

        Transform player;
        Collider playerCollider;
        float nextCheck;

        public bool SeesPlayer { get; private set; }

        void Awake()
        {
            if (lens == null) lens = transform.Find("Head");
            if (lens == null) lens = transform;
        }

        void Start()
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) { player = p.transform; playerCollider = p.GetComponent<Collider>(); }
            nextCheck = Time.time + Random.value * checkInterval;   // spread the cameras' checks over frames
        }

        void Update()
        {
            if (player == null || Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;

            SeesPlayer = CameraNetwork.Online && CanSee();
            if (SeesPlayer) CameraNetwork.Report(player.position);
        }

        bool CanSee()
        {
            Vector3 eye = lens.position;
            Vector3 target = player.position + Vector3.up * 1.2f;   // chest height
            Vector3 toTarget = target - eye;
            if (toTarget.sqrMagnitude > range * range) return false;
            if (Vector3.Angle(lens.forward, toTarget) > fieldOfView * 0.5f) return false;
            // Line of sight: the first thing in the way must be the player.
            if (!Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider == playerCollider;
        }

        void OnDrawGizmosSelected()
        {
            var l = lens != null ? lens : transform.Find("Head") ?? transform;
            Gizmos.color = Application.isPlaying && SeesPlayer ? Color.red : new Color(1f, 0.6f, 0f);
            float half = fieldOfView * 0.5f;
            foreach (var axis in new[] { l.up, l.right })
            {
                Gizmos.DrawRay(l.position, Quaternion.AngleAxis(half, axis) * l.forward * range);
                Gizmos.DrawRay(l.position, Quaternion.AngleAxis(-half, axis) * l.forward * range);
            }
        }
    }
}
