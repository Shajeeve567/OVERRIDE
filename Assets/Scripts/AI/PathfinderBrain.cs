using System.Collections.Generic;
using UnityEngine;
using Override.Core;

namespace Override.AI
{
    public class PathfinderBrain : MonoBehaviour, IAgentBrain
    {
        private List<Vector3> currentPath = new List<Vector3>();
        private float lastPathTime = 0f;
        private float pathRefreshRate = 0.5f; // Recalculate path twice a second

        public AgentCommand Tick(WorldSnapshot snapshot)
        {
            AgentCommand cmd = new AgentCommand();
            
            // Recalculate path periodically to save performance
            if (Time.time - lastPathTime > pathRefreshRate)
            {
                lastPathTime = Time.time;
                currentPath = FindPath(transform.position, snapshot.PlayerPosition);
            }

            // Send the path to the Motor
            if (currentPath != null && currentPath.Count > 0)
            {
                cmd.Path = currentPath.ToArray();
            }
            else
            {
                // Fallback: If no path found, just go straight (for debugging)
                cmd.Path = new Vector3[] { snapshot.PlayerPosition };
            }

            cmd.LookTarget = snapshot.PlayerPosition;
            cmd.AnimationIntent = "Hover";
            
            return cmd;
        }

        // --- THE A* ALGORITHM ---
        private List<Vector3> FindPath(Vector3 startPos, Vector3 targetPos)
        {
            if (WaypointGraph.Instance == null) return null;

            // 1. Find the closest waypoint to the start and the target
            Transform startNode = GetClosestNode(startPos);
            Transform targetNode = GetClosestNode(targetPos);

            if (startNode == null || targetNode == null) return null;

            // 2. A* Setup
            List<Transform> openSet = new List<Transform> { startNode };
            HashSet<Transform> closedSet = new HashSet<Transform>();
            
            Dictionary<Transform, Transform> cameFrom = new Dictionary<Transform, Transform>();
            Dictionary<Transform, float> gScore = new Dictionary<Transform, float>();
            Dictionary<Transform, float> fScore = new Dictionary<Transform, float>();

            foreach (Transform node in WaypointGraph.Instance.nodes)
            {
                gScore[node] = float.MaxValue;
                fScore[node] = float.MaxValue;
            }
            gScore[startNode] = 0;
            fScore[startNode] = Vector3.Distance(startNode.position, targetNode.position);

            // 3. The A* Loop
            while (openSet.Count > 0)
            {
                // Get node with lowest F score
                Transform current = openSet[0];
                for (int i = 1; i < openSet.Count; i++)
                {
                    if (fScore[openSet[i]] < fScore[current]) current = openSet[i];
                }

                if (current == targetNode)
                {
                    List<Vector3> finalPath = RetracePath(startNode, targetNode, cameFrom);
                    finalPath.Add(targetPos);

                    return finalPath;
                }

                openSet.Remove(current);
                closedSet.Add(current);

                // Check neighbors
                if (WaypointGraph.Instance.edges.ContainsKey(current))
                {
                    foreach (Transform neighbor in WaypointGraph.Instance.edges[current])
                    {
                        if (closedSet.Contains(neighbor)) continue;

                        // Note: Later, I need to add Exposure here!
                        float tentativeGScore = gScore[current] + Vector3.Distance(current.position, neighbor.position);

                        if (tentativeGScore < gScore[neighbor])
                        {
                            cameFrom[neighbor] = current;
                            gScore[neighbor] = tentativeGScore;
                            fScore[neighbor] = tentativeGScore + Vector3.Distance(neighbor.position, targetNode.position);

                            if (!openSet.Contains(neighbor)) openSet.Add(neighbor);
                        }
                    }
                }
            }

            return null; // No path found
        }

        private List<Vector3> RetracePath(Transform start, Transform end, Dictionary<Transform, Transform> cameFrom)
        {
            List<Vector3> path = new List<Vector3>();
            Transform current = end;

            while (current != start)
            {
                path.Add(current.position);
                current = cameFrom[current];
            }
            path.Reverse();
            return path;
        }

        private Transform GetClosestNode(Vector3 position)
        {
            Transform closest = null;
            float minDist = Mathf.Infinity;

            foreach (Transform node in WaypointGraph.Instance.nodes)
            {
                float dist = Vector3.Distance(position, node.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = node;
                }
            }
            return closest;
        }
    }
}