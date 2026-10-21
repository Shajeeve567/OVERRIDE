using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Override.Level;

namespace Override.EditorTools
{
    public static class WaypointGraphValidator
    {
        [MenuItem("Tools/OVERRIDE/Validate Waypoint Graph")]
        public static void Validate()
        {
            WaypointNode[] nodes = Object.FindObjectsByType<WaypointNode>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            int isolatedCount = 0;
            int missingReciprocalCount = 0;

            foreach (WaypointNode node in nodes)
            {
                if (node == null) continue;

                int neighbourCount = 0;

                if (node.neighbours != null)
                {
                    foreach (WaypointNode neighbour in node.neighbours)
                    {
                        if (neighbour == null) continue;

                        neighbourCount++;

                        if (neighbour == node) continue;

                        if (neighbour.neighbours == null || !neighbour.neighbours.Contains(node))
                        {
                            missingReciprocalCount++;
                            Debug.LogWarning(
                                $"[WaypointGraphValidator] Missing reciprocal edge: " +
                                $"{node.name} -> {neighbour.name}, but {neighbour.name} does not list {node.name}.",
                                neighbour);
                        }
                    }
                }

                if (neighbourCount == 0)
                {
                    isolatedCount++;
                    Debug.LogWarning(
                        $"[WaypointGraphValidator] Isolated node: {node.name} has no neighbours.",
                        node);
                }
            }

            Debug.Log(
                $"[WaypointGraphValidator] Checked {nodes.Length} waypoint nodes. " +
                $"Isolated: {isolatedCount}. Missing reciprocal edges: {missingReciprocalCount}.");
        }
    }
}
