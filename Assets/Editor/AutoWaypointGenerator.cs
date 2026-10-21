using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Override.Interaction;
using Override.Level;

public static class AutoWaypointGenerator
{
    private const string GreyboxName = "OVERRIDE_Greybox";
    private const string AutoRootName = "AutoWaypoints";
    private const float GridSpacing = 7f;
    private const float FloorProbeHeight = 0.5f;
    private const float NodeHeight = 2.5f;
    private const float NodeClearance = 0.5f;
    private const float LinkDistance = 10f;
    private const float LinkRadius = 0.45f;

    [MenuItem("Tools/OVERRIDE/Generate Grid Waypoints")]
    public static void GenerateGridWaypoints()
    {
        GameObject greybox = GameObject.Find(GreyboxName);
        if (greybox == null)
        {
            Debug.LogError($"[AutoWaypointGenerator] Could not find '{GreyboxName}' in the active scene.");
            return;
        }

        if (!TryGetBounds(greybox, out Bounds bounds))
        {
            Debug.LogError($"[AutoWaypointGenerator] '{GreyboxName}' has no renderers or colliders to derive bounds from.");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate Grid Waypoints");

        GameObject existingRoot = FindExistingRoot();
        if (existingRoot != null)
        {
            Undo.DestroyObjectImmediate(existingRoot);
        }

        GameObject autoRoot = new GameObject(AutoRootName);
        Undo.RegisterCreatedObjectUndo(autoRoot, "Create AutoWaypoints");

        List<WaypointNode> generatedNodes = new List<WaypointNode>();
        float floorY = bounds.min.y;
        int stepsX = Mathf.Max(0, Mathf.FloorToInt(bounds.size.x / GridSpacing));
        int stepsZ = Mathf.Max(0, Mathf.FloorToInt(bounds.size.z / GridSpacing));

        for (int x = 0; x <= stepsX; x++)
        {
            for (int z = 0; z <= stepsZ; z++)
            {
                float worldX = bounds.min.x + x * GridSpacing;
                float worldZ = bounds.min.z + z * GridSpacing;
                Vector3 probeOrigin = new Vector3(worldX, floorY + FloorProbeHeight, worldZ);

                if (!Physics.Raycast(
                        probeOrigin,
                        Vector3.down,
                        out RaycastHit floorHit,
                        FloorProbeHeight + 0.5f,
                        ~0,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                if (floorHit.normal.y < 0.5f) continue;

                Vector3 nodePosition = floorHit.point + Vector3.up * NodeHeight;
                if (Physics.CheckSphere(nodePosition, NodeClearance, ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                GameObject nodeObject = new GameObject($"AutoWP_{x:00}_{z:00}");
                Undo.RegisterCreatedObjectUndo(nodeObject, "Create Auto Waypoint");
                nodeObject.transform.SetParent(autoRoot.transform, true);
                nodeObject.transform.position = nodePosition;
                generatedNodes.Add(nodeObject.AddComponent<WaypointNode>());
            }
        }

        int generatedLinkCount = 0;
        for (int i = 0; i < generatedNodes.Count; i++)
        {
            for (int j = i + 1; j < generatedNodes.Count; j++)
            {
                WaypointNode a = generatedNodes[i];
                WaypointNode b = generatedNodes[j];

                if ((a.transform.position - b.transform.position).sqrMagnitude > LinkDistance * LinkDistance)
                {
                    continue;
                }

                if (!IsLinkClear(a.transform.position, b.transform.position)) continue;

                AddNeighbour(a, b);
                AddNeighbour(b, a);
                generatedLinkCount++;
            }
        }

        int existingLinkCount = LinkExistingNodes(autoRoot.transform, generatedNodes);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log(
            $"[AutoWaypointGenerator] Generated {generatedNodes.Count} waypoints, " +
            $"{generatedLinkCount} auto links, and {existingLinkCount} existing-node links " +
            $"under '{AutoRootName}'.");
    }

    private static bool TryGetBounds(GameObject greybox, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        foreach (Renderer renderer in greybox.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            Encapsulate(ref bounds, renderer.bounds, ref hasBounds);
        }

        foreach (Collider collider in greybox.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null) continue;
            Encapsulate(ref bounds, collider.bounds, ref hasBounds);
        }

        return hasBounds;
    }

    private static void Encapsulate(ref Bounds bounds, Bounds next, ref bool hasBounds)
    {
        if (!hasBounds)
        {
            bounds = next;
            hasBounds = true;
            return;
        }

        bounds.Encapsulate(next);
    }

    private static GameObject FindExistingRoot()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] objects = Object.FindObjectsByType<GameObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (GameObject candidate in objects)
        {
            if (candidate != null &&
                candidate.scene == activeScene &&
                candidate.name == AutoRootName)
            {
                return candidate;
            }
        }

        return null;
    }

    private static int LinkExistingNodes(Transform autoRoot, List<WaypointNode> generatedNodes)
    {
        if (generatedNodes.Count == 0) return 0;

        WaypointNode[] allNodes = Object.FindObjectsByType<WaypointNode>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        int linkCount = 0;

        foreach (WaypointNode existing in allNodes)
        {
            if (existing == null || existing.transform.IsChildOf(autoRoot)) continue;

            Undo.RecordObject(existing, "Link Existing Waypoints");
            int removedNulls = existing.neighbours.RemoveAll(neighbour => neighbour == null);
            bool changed = removedNulls > 0;

            WaypointNode nearest = null;
            float nearestDistance = float.PositiveInfinity;

            foreach (WaypointNode generated in generatedNodes)
            {
                if (generated == null) continue;

                float distance = Vector3.Distance(existing.transform.position, generated.transform.position);
                if (distance >= nearestDistance) continue;
                if (!IsLinkClear(existing.transform.position, generated.transform.position)) continue;

                nearest = generated;
                nearestDistance = distance;
            }

            if (nearest != null && !existing.neighbours.Contains(nearest))
            {
                existing.neighbours.Add(nearest);
                changed = true;
                linkCount++;
            }

            if (nearest != null && !nearest.neighbours.Contains(existing))
            {
                Undo.RecordObject(nearest, "Link Existing Waypoints");
                nearest.neighbours.Add(existing);
                EditorUtility.SetDirty(nearest);
            }

            if (changed) EditorUtility.SetDirty(existing);
        }

        return linkCount;
    }

    private static void AddNeighbour(WaypointNode from, WaypointNode to)
    {
        if (from == null || to == null) return;
        if (from.neighbours.Contains(to)) return;

        Undo.RecordObject(from, "Link Auto Waypoints");
        from.neighbours.Add(to);
        EditorUtility.SetDirty(from);
    }

    private static bool IsLinkClear(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;

        if (distance <= 0.001f) return true;

        RaycastHit[] hits = Physics.SphereCastAll(
            from,
            LinkRadius,
            delta / distance,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;

            Rigidbody body = hit.rigidbody;
            if (body != null && !body.isKinematic) continue;

            if (hit.collider.GetComponentInParent<IDoor>() != null) continue;
            if (hit.collider.GetComponentInParent<ILockableDoor>() != null) continue;

            return false;
        }

        return true;
    }
}
