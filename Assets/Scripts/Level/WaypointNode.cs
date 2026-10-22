// WaypointNode.cs
// One node of the level's waypoint graph: the navigation graph and decision points the GDD says the
// World Builder marks up for the agents (the Pathfinder's A*, the Director's spawn choices...).
//
// It's deliberately tiny: a position (its Transform) plus a list of the nodes you can walk to
// directly from here. The graph's EDGES are just these lists. Whoever owns the Pathfinder can read
// them, or replace this class, without anything else in the level changing.
//
// This is a RUNTIME script (it lives outside any Editor folder) because the agents need it in the
// built game, not just in the editor.

using System.Collections.Generic;
using UnityEngine;

namespace Override.Level
{
    public class WaypointNode : MonoBehaviour
    {
        [Tooltip("Nodes reachable in a straight, unobstructed line from this one.")]
        public List<WaypointNode> neighbours = new List<WaypointNode>();

        // Gizmos draw only in the Scene view (and in Game view with Gizmos on), never in the build.
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.8f);   // teal: the facility's "friendly" colour
            Gizmos.DrawSphere(transform.position, 0.25f);
            foreach (var n in neighbours)
                if (n != null) Gizmos.DrawLine(transform.position, n.transform.position);
        }
    }
}
