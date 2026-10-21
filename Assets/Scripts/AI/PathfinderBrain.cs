using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Override.Core;
using Override.Director;
using Override.Motor;

namespace Override.AI
{
    public class PathfinderBrain : MonoBehaviour, IAgentBrain, IDirectorDrone
    {
        private const float AttackRange = 15f;
        private const int LineOfSightHitCapacity = 32;

        [SerializeField] private bool allowDirectChaseFallback = true;

        private WispVisuals wispVisuals;
        private CharacterController playerController;

        private static readonly RaycastHit[] lineOfSightHits = new RaycastHit[LineOfSightHitCapacity];

        private List<Vector3> currentPath = new List<Vector3>();
        private float lastPathTime = 0f;
        private float pathRefreshRate = 0.5f;
        private Transform[] terminals;

        private Vector3 lastPlayerPosition;
        private Vector3 lastPlayerForward = Vector3.forward;
        private bool showPathDebug;

        private bool ShowPathDebug => showPathDebug || DebugOverlay.Visible;

        public void Launch(Vector3 waypoint, Transform playerTransform)
        {
            playerController = playerTransform != null
                ? playerTransform.GetComponent<CharacterController>()
                : null;

            if (currentPath == null) currentPath = new List<Vector3>();
            else currentPath.Clear();

            currentPath.Add(waypoint);
            lastPathTime = Time.time;

            if (wispVisuals == null) wispVisuals = GetComponentInChildren<WispVisuals>(true);
            if (wispVisuals != null) wispVisuals.PlaySpawn();
        }

        private void Awake()
        {
            wispVisuals = GetComponentInChildren<WispVisuals>(true);
        }

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
            if (snapshot == null)
            {
                return new AgentCommand
                {
                    Path = new Vector3[] { transform.position },
                    LookTarget = transform.position,
                    Fire = false,
                    AnimationIntent = "Hover"
                };
            }

            lastPlayerPosition = snapshot.PlayerPosition;
            lastPlayerForward = snapshot.PlayerForward;
            HandleDebugToggle();

            AgentCommand cmd = new AgentCommand();
            float distToPlayer = Vector3.Distance(transform.position, snapshot.PlayerPosition);
            bool hasLineOfSight = HasLineOfSight(snapshot.PlayerPosition);

            if (distToPlayer < AttackRange && hasLineOfSight)
            {
                currentPath.Clear();
                lastPathTime = -999f;

                cmd.Path = new Vector3[] { transform.position };
                cmd.LookTarget = snapshot.PlayerPosition;
                cmd.Fire = true;
                cmd.AnimationIntent = "Attack";

                DrawCurrentPath();
                return cmd;
            }

            Vector3 predictedTarget = GetPredictedTarget(snapshot.PlayerPosition, hasLineOfSight);

            if (Time.time - lastPathTime > pathRefreshRate)
            {
                lastPathTime = Time.time;
                // [Integration] FindPath returns null when the graph has no route; keep an empty list instead,
                // or currentPath.Clear() above throws every frame once the player is in attack range.
                currentPath = FindPath(transform.position, predictedTarget, snapshot.PlayerForward) ?? new List<Vector3>();
            }

            if (currentPath != null && currentPath.Count > 0)
            {
                cmd.Path = currentPath.ToArray();
            }
            else
            {
                cmd.Path = allowDirectChaseFallback
                    ? new Vector3[] { snapshot.PlayerPosition }
                    : new Vector3[] { transform.position };
            }

            cmd.LookTarget = snapshot.PlayerPosition;
            cmd.Fire = false;
            cmd.AnimationIntent = "Hover";

            DrawCurrentPath();
            return cmd;
        }

        private void HandleDebugToggle()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                showPathDebug = !showPathDebug;
            }
        }

        private void DrawCurrentPath()
        {
            if (!ShowPathDebug || currentPath == null || currentPath.Count == 0) return;

            Vector3 previous = transform.position;

            for (int i = 0; i < currentPath.Count; i++)
            {
                Vector3 node = currentPath[i];
                float exposure = CalculateExposure(node, lastPlayerPosition, lastPlayerForward);
                Color color = exposure < 0.5f ? Color.green : Color.red;
                Debug.DrawLine(previous, node, color);
                previous = node;
            }
        }

        private bool HasLineOfSight(Vector3 playerPos)
        {
            Vector3 origin = transform.position;
            Vector3 target = playerController != null
                ? playerController.bounds.center
                : playerPos + Vector3.up * 1f;
            Vector3 toTarget = target - origin;
            float distance = toTarget.magnitude;

            if (distance < 0.001f) return true;

            int hitCount = Physics.RaycastNonAlloc(
                origin,
                toTarget / distance,
                lineOfSightHits,
                distance + 0.5f,
                ~0,
                QueryTriggerInteraction.Ignore);

            float nearestPlayerHit = float.PositiveInfinity;
            float nearestBlockerHit = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = lineOfSightHits[i];
                if (hit.collider == null) continue;

                // The Wisp's own CharacterController and visual colliders are not cover.
                if (hit.transform.root == transform.root) continue;

                bool isPlayer = hit.transform.CompareTag("Player") ||
                                hit.transform.root.CompareTag("Player");

                if (isPlayer)
                {
                    nearestPlayerHit = Mathf.Min(nearestPlayerHit, hit.distance);
                    continue;
                }

                // Other agents are not walls; the shot passes through them.
                if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;

                nearestBlockerHit = Mathf.Min(nearestBlockerHit, hit.distance);
            }

            return nearestPlayerHit < nearestBlockerHit;
        }

        private List<Vector3> FindPath(Vector3 startPos, Vector3 targetPos, Vector3 playerForward)
        {
            if (WaypointGraph.Instance == null) return null;
            if (WaypointGraph.Instance.nodes == null || WaypointGraph.Instance.nodes.Count == 0) return null;

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
                if (node == null) continue;
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

                    if (finalPath == null) finalPath = new List<Vector3>();
                    if (finalPath.Count == 0) finalPath.Add(targetNode.position);

                    finalPath.Add(targetPos);
                    return finalPath;
                }

                openSet.Remove(current);
                closedSet.Add(current);

                if (WaypointGraph.Instance.edges != null &&
                    WaypointGraph.Instance.edges.TryGetValue(current, out List<Transform> neighbours))
                {
                    foreach (Transform neighbor in neighbours)
                    {
                        if (neighbor == null || closedSet.Contains(neighbor)) continue;
                        if (!gScore.ContainsKey(neighbor) || !fScore.ContainsKey(neighbor)) continue;

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
                if (current == null || !cameFrom.ContainsKey(current)) return null;

                path.Add(current.position);
                current = cameFrom[current];
            }

            path.Reverse();
            return path;
        }

        private Transform GetClosestNode(Vector3 position)
        {
            if (WaypointGraph.Instance == null || WaypointGraph.Instance.nodes == null) return null;

            Transform closest = null;
            float minDist = Mathf.Infinity;

            foreach (Transform node in WaypointGraph.Instance.nodes)
            {
                if (node == null) continue;

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
            float dot = Vector3.Dot(playerForward, dirToNode);
            return Mathf.Clamp01((dot + 1f) / 2f);
        }

        private Vector3 GetPredictedTarget(Vector3 playerPos, bool hasLineOfSight)
        {
            if (terminals == null || terminals.Length == 0) return playerPos;

            Transform closestTerminal = null;
            float minDist = Mathf.Infinity;

            foreach (Transform term in terminals)
            {
                if (term == null) continue;

                float dist = Vector3.Distance(playerPos, term.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    closestTerminal = term;
                }
            }

            if (!hasLineOfSight && closestTerminal != null && minDist < 15f)
            {
                return closestTerminal.position;
            }

            return playerPos;
        }
    }
}
