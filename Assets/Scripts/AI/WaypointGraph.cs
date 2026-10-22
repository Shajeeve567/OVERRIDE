using System.Collections.Generic;
using UnityEngine;
using Override.Level; 

public class WaypointGraph : MonoBehaviour
{
    public static WaypointGraph Instance { get; private set; }

    public List<Transform> nodes = new List<Transform>();
    public Dictionary<Transform, List<Transform>> edges = new Dictionary<Transform, List<Transform>>();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        BuildGraph();
    }

    [ContextMenu("Build Graph Now")]
    public void BuildGraph()
    {
        nodes.Clear();
        edges.Clear();

        // 1. Find all WaypointNode components in the scene (even inactive ones)
        WaypointNode[] allNodes = FindObjectsByType<WaypointNode>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (allNodes.Length == 0)
        {
            Debug.LogWarning("No WaypointNode scripts found in the scene!");
            return;
        }

        // 2. Register all nodes
        foreach (WaypointNode node in allNodes)
        {
            nodes.Add(node.transform);
            edges[node.transform] = new List<Transform>();
        }

        // 3. Build the edges from their manual neighbour lists
        foreach (WaypointNode node in allNodes)
        {
            foreach (WaypointNode neighbour in node.neighbours)
            {
                if (neighbour != null)
                {
                    edges[node.transform].Add(neighbour.transform);
                }
            }
        }
        
        Debug.Log($"Waypoint Graph Built: {nodes.Count} nodes connected manually.");
    }

    private void OnDrawGizmos()
    {
        if (nodes == null || edges == null) return;
        
        Gizmos.color = Color.cyan;
        foreach (var node in nodes)
        {
            if (node == null) continue;
            Gizmos.DrawSphere(node.position, 0.5f);
            
            if (edges.ContainsKey(node))
            {
                foreach (var neighbor in edges[node])
                {
                    if (neighbor != null)
                        Gizmos.DrawLine(node.position, neighbor.position);
                }
            }
        }
    }
}