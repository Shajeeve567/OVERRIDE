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
        private Transform[] terminals;

        private void Start()
        {
            GameObject[] termObjs = GameObject.FindGameObjectsWithTag("Terminal");
            terminals = new Transform[termObjs.Length];

            for (int i = 0; i < termObjs.Length; i++)
            {
                terminals[i] = termObjs[i].transform;
            }
        }

        public AgentCommand Tick(WorldSnapshot snapshot)
        {
            AgentCommand cmd = new AgentCommand();

            // 1. Predict where the player is going (Terminal or Player)
            Vector3 predictedTarget = GetPredictedTarget(snapshot.PlayerPosition);

            // 2. Recalculate path periodically to save performance
            if (Time.time - lastPathTime > pathRefreshRate)
            {
                lastPathTime = Time.time;
                currentPath = FindPath(transform.position, predictedTarget, snapshot.PlayerForward);
            }

            // 3. Send the path to the Motor
            if (currentPath != null && currentPath.Count > 0)
            {
                cmd.Path = currentPath.ToArray();
            }
            else
            {
                // Fallback: If no path found, just go straight
                cmd.Path = new Vector3[] { transform.position };
            }

            cmd.LookTarget = snapshot.PlayerPosition;
            cmd.AnimationIntent = "Hover";
            
            return cmd;
        }

        // --- THE A* ALGORITHM ---
        private List<Vector3> FindPath(Vector3 startPos, Vector3 targetPos, Vector3 playerForward)
        {
            if (WaypointGraph.Instance == null) return null;

            Transform startNode = GetClosestNode(startPos);
            Transform targetNode = GetClosestNode(targetPos);

            if (startNode == null || targetNode == null) return null;

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

            while (openSet.Count > 0)
            {
                Transform current = openSet[0];
                for (int i = 1; i < openSet.Count; i++)
                {
                    if (fScore[openSet[i]] < fScore[current]) current = openSet[i];
                }

                if (current == targetNode)
                {
                    List<Vector3> finalPath = RetracePath(startNode, targetNode, cameFrom);
                    
                    // CRITICAL FIX: Always make sure the path includes the target node
                    if (finalPath.Count == 0) finalPath.Add(targetNode.position); 
                    
                    // Only add the exact targetPos if it's the player (so the Wisp can touch them)
                    // If it's a Terminal, we just want the Wisp to get to the waypoint near it so it doesn't crash into the Terminal collider.
                    finalPath.Add(targetPos); 
                    return finalPath;
                }

                openSet.Remove(current);
                closedSet.Add(current);

                if (WaypointGraph.Instance.edges.ContainsKey(current))
                {
                    foreach (Transform neighbor in WaypointGraph.Instance.edges[current])
                    {
                        if (closedSet.Contains(neighbor)) continue;

                        float distanceCost = Vector3.Distance(current.position, neighbor.position);
                        float exposureCost = CalculateExposure(neighbor.position, targetPos, playerForward);
                        float exposureWeight = 3.0f;

                        float tentativeGScore = gScore[current] + distanceCost + (exposureCost * exposureWeight);

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

            return null; 
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

        private float CalculateExposure(Vector3 nodePos, Vector3 playerPos, Vector3 playerForward)
        {
            Vector3 dirToNode = (nodePos - playerPos).normalized;
            
            // Dot product: 1 if player is looking directly at the node, -1 if looking away
            float dot = Vector3.Dot(playerForward, dirToNode);
            
            // Convert to 0 (safe) to 1 (exposed)
            float exposure = Mathf.Clamp01((dot + 1f) / 2f); 
            
            return exposure;
        }

        private Vector3 GetPredictedTarget(Vector3 playerPos)
        {
            if (terminals == null || terminals.Length == 0) return playerPos;

            Transform closestTerminal = null;
            float minDist = Mathf.Infinity;

            // Find the terminal the player is closest to
            foreach (Transform term in terminals)
            {
                float dist = Vector3.Distance(playerPos, term.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    closestTerminal = term;
                }
            }

            // If the player is very close to a terminal, the Wisp goes there to ambush.
            // Otherwise, it still targets the player.
            if (minDist < 15f) 
            {
                return closestTerminal.position;
            }
            
            return playerPos;
        }
    }
}