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
        private const float BayExitRadius = 1.5f;
        private const float DirectChaseRadius = 0.45f;
        private const float LostSightToAmbushDelay = 2f;
        private const float TargetSwitchCooldown = 1.5f;
        private const float TerminalAmbushRange = 15f;
        private const int LineOfSightHitCapacity = 32;

        [SerializeField] private bool allowDirectChaseFallback = true;

        private WispVisuals wispVisuals;
        private CharacterController playerController;

        private bool isLeavingBay;
        private Vector3 bayExitWaypoint;

        private static readonly RaycastHit[] lineOfSightHits = new RaycastHit[LineOfSightHitCapacity];
        private static readonly RaycastHit[] directChaseHits = new RaycastHit[LineOfSightHitCapacity];

        private List<Vector3> currentPath = new List<Vector3>();
        private float lastPathTime = 0f;
        private float pathRefreshRate = 0.5f;
        private Transform[] terminals;

        private Vector3 lastSeenPlayerPosition;
        private Vector3 currentTarget;
        private float timeSinceLosLost;
        private float lastTargetChangeTime;
        private bool isTerminalAmbush;

        private Vector3 lastPlayerPosition;
        private Vector3 lastPlayerForward = Vector3.forward;
        private bool showPathDebug;

        private bool ShowPathDebug => showPathDebug || DebugOverlay.Visible;

        public void Launch(Vector3 waypoint, Transform playerTransform)
        {
            playerController = playerTransform != null
                ? playerTransform.GetComponent<CharacterController>()
                : null;

            lastSeenPlayerPosition = playerTransform != null ? playerTransform.position : waypoint;
            currentTarget = lastSeenPlayerPosition;
            timeSinceLosLost = 0f;
            lastTargetChangeTime = Time.time;
            isTerminalAmbush = false;

            if (currentPath == null) currentPath = new List<Vector3>();
            else currentPath.Clear();

            currentPath.Add(waypoint);
            lastPathTime = Time.time;
            bayExitWaypoint = waypoint;
            isLeavingBay = true;

            if (wispVisuals == null) wispVisuals = GetComponentInChildren<WispVisuals>(true);
            if (wispVisuals != null) wispVisuals.PlaySpawn();
        }

        private void Awake()
        {
            wispVisuals = GetComponentInChildren<WispVisuals>(true);
            lastSeenPlayerPosition = transform.position;
            currentTarget = transform.position;
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

            if (isLeavingBay)
            {
                Vector3 exitOffset = bayExitWaypoint - transform.position;
                exitOffset.y = 0f;

                if (exitOffset.sqrMagnitude <= BayExitRadius * BayExitRadius)
                {
                    isLeavingBay = false;
                    currentPath.Clear();
                    lastPathTime = -999f;
                }
                else
                {
                    AgentCommand exitCommand = new AgentCommand
                    {
                        Path = new Vector3[] { bayExitWaypoint },
                        LookTarget = bayExitWaypoint,
                        Fire = false,
                        AnimationIntent = "ExitBay"
                    };

                    DrawCurrentPath();
                    return exitCommand;
                }
            }

            AgentCommand cmd = new AgentCommand();
            float distToPlayer = Vector3.Distance(transform.position, snapshot.PlayerPosition);
            bool hasLineOfSight = HasLineOfSight(snapshot.PlayerPosition);

            UpdateTargetKnowledge(snapshot.PlayerPosition, hasLineOfSight);

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

            bool wantsTerminalAmbush = ShouldUseTerminalAmbush(
                snapshot.PlayerPosition,
                hasLineOfSight,
                out Transform ambushTerminal);

            if (wantsTerminalAmbush != isTerminalAmbush &&
                Time.time - lastTargetChangeTime >= TargetSwitchCooldown)
            {
                isTerminalAmbush = wantsTerminalAmbush;
                lastTargetChangeTime = Time.time;
                lastPathTime = -999f;
            }

            if (isTerminalAmbush)
            {
                if (ambushTerminal != null) currentTarget = ambushTerminal.position;
            }
            else
            {
                currentTarget = lastSeenPlayerPosition;
            }

            if (Time.time - lastPathTime > pathRefreshRate)
            {
                lastPathTime = Time.time;

                currentPath = hasLineOfSight && HasClearDirectPath(snapshot.PlayerPosition)
                    ? new List<Vector3> { snapshot.PlayerPosition }
                    : FindPath(transform.position, currentTarget, snapshot.PlayerForward) ?? new List<Vector3>();
            }

            if (currentPath != null && currentPath.Count > 0)
            {
                cmd.Path = currentPath.ToArray();
            }
            else
            {
                cmd.Path = allowDirectChaseFallback &&
                           HasClearDirectPathTo(lastSeenPlayerPosition + Vector3.up * 1f)
                    ? new Vector3[] { lastSeenPlayerPosition }
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

                if (hit.transform.root == transform.root) continue;

                bool isPlayer = hit.transform.CompareTag("Player") ||
                                hit.transform.root.CompareTag("Player");

                if (isPlayer)
                {
                    nearestPlayerHit = Mathf.Min(nearestPlayerHit, hit.distance);
                    continue;
                }

                if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;

                nearestBlockerHit = Mathf.Min(nearestBlockerHit, hit.distance);
            }

            return nearestPlayerHit < nearestBlockerHit;
        }

        private bool HasClearDirectPath(Vector3 playerPos)
        {
            Vector3 target = playerController != null
                ? playerController.bounds.center
                : playerPos + Vector3.up * 1f;
            return HasClearDirectPathTo(target);
        }

        private bool HasClearDirectPathTo(Vector3 target)
        {
            Vector3 delta = target - transform.position;
            float distance = delta.magnitude;

            if (distance < 0.001f) return true;

            int hitCount = Physics.SphereCastNonAlloc(
                transform.position,
                DirectChaseRadius,
                delta / distance,
                directChaseHits,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = directChaseHits[i];
                if (hit.collider == null) continue;

                if (hit.transform.root == transform.root) continue;

                bool isPlayer = hit.transform.CompareTag("Player") ||
                                hit.transform.root.CompareTag("Player");
                if (isPlayer) continue;

                if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;

                return false;
            }

            return true;
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

        private void UpdateTargetKnowledge(Vector3 playerPos, bool hasLineOfSight)
        {
            if (hasLineOfSight)
            {
                lastSeenPlayerPosition = playerPos;
                timeSinceLosLost = 0f;
            }
            else
            {
                timeSinceLosLost += Time.deltaTime;
            }
        }

        private bool ShouldUseTerminalAmbush(
            Vector3 playerPos,
            bool hasLineOfSight,
            out Transform closestTerminal)
        {
            closestTerminal = null;

            if (hasLineOfSight || timeSinceLosLost < LostSightToAmbushDelay) return false;

            closestTerminal = GetClosestTerminal(playerPos);
            return closestTerminal != null &&
                   Vector3.Distance(playerPos, closestTerminal.position) < TerminalAmbushRange;
        }

        private Transform GetClosestTerminal(Vector3 playerPos)
        {
            if (terminals == null || terminals.Length == 0) return null;

            Transform closest = null;
            float minDist = Mathf.Infinity;

            foreach (Transform term in terminals)
            {
                if (term == null) continue;

                float dist = Vector3.Distance(playerPos, term.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = term;
                }
            }

            return closest;
        }
    }
}
