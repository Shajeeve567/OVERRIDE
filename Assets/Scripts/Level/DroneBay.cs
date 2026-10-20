// DroneBay.cs
// A place where ARGUS can launch Wisp drones. The Director picks WHICH bays to use in its spawn plan;
// this component is the bay's side of that contract: where to spawn, which zone it belongs to,
// the waypoint the drone joins the graph at, and whether the player has blocked it.
//
// RUNTIME script: the Director reads these during play, so it can't live in an Editor folder.

using UnityEngine;

namespace Override.Level
{
    public class DroneBay : MonoBehaviour
    {
        [Tooltip("0-9, matching the Director's bay table.")]
        public int bayId;

        [Tooltip("Labs, ServerHall, Maintenance or Core.")]
        public string zone;

        [Tooltip("Where a new drone appears (inside the booth, hidden from the terminal).")]
        public Transform spawnPoint;

        [Tooltip("The bay's opening. Its forward (blue) axis points out of the bay.")]
        public Transform mouth;

        [Tooltip("The waypoint just outside the mouth: the drone's first node on the graph.")]
        public WaypointNode waypoint;

        [Tooltip("The crate placed beside this bay, which the player can push in front of the mouth.")]
        public Rigidbody blocker;

        // The box in front of the mouth that has to be clear for drones to get out.
        // Half-extents (half the size), because that's what Physics.OverlapBox takes.
        public Vector3 mouthCheckHalfSize = new Vector3(0.7f, 0.7f, 0.6f);

        // Anything pushable this heavy counts as a block. The crate barricade is 80 kg; a drone or a
        // thrown canister is far lighter, so it never makes a bay look blocked.
        const float BlockingMass = 20f;

        // Reused between calls so checking every bay every Director tick allocates no memory.
        static readonly Collider[] hits = new Collider[16];

        Vector3 CheckCentre => mouth.position + mouth.forward * mouthCheckHalfSize.z;

        // True when this bay's own crate, or any other heavy movable object, sits in front of the mouth.
        //   - Its own `blocker` always counts, however light. The bay crates are made LIGHTER than the
        //     player (under 1 kg vs 1 kg) so they're easy to shove, so a weight rule alone would miss them.
        //   - Anything else must weigh BlockingMass or more: then a heavy barricade crate pushed there
        //     also blocks the bay, but a drone or a thrown canister never does.
        // (The Director's "every spawn point blocked" edge case.)
        public bool IsBlocked
        {
            get
            {
                if (mouth == null) return false;
                int count = Physics.OverlapBoxNonAlloc(CheckCentre, mouthCheckHalfSize, hits, mouth.rotation);
                for (int i = 0; i < count; i++)
                {
                    var body = hits[i].attachedRigidbody;
                    if (body == null || body.isKinematic) continue;
                    if (body == blocker || body.mass >= BlockingMass) return true;
                }
                return false;
            }
        }

        void OnDrawGizmos()
        {
            if (spawnPoint != null)
            {
                Gizmos.color = new Color(1f, 0.15f, 0.1f);   // ARGUS red
                Gizmos.DrawWireSphere(spawnPoint.position, 0.4f);
            }
            if (mouth != null)
            {
                // The mouth box: yellow when clear, red when blocked (only checked in Play mode,
                // because physics queries in edit mode would run every repaint).
                Gizmos.color = Application.isPlaying && IsBlocked ? Color.red : Color.yellow;
                Gizmos.matrix = Matrix4x4.TRS(CheckCentre, mouth.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, mouthCheckHalfSize * 2f);
                Gizmos.matrix = Matrix4x4.identity;
            }
            if (waypoint != null && spawnPoint != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(spawnPoint.position, waypoint.transform.position);
            }
        }
    }
}
