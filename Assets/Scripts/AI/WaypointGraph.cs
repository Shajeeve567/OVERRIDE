using System.Collections.Generic;
using UnityEngine;

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
        // We still build it on Start so the AI can use it during gameplay
        BuildGraph();
    }

    // This adds a button to the Inspector so you can click it manually
    [ContextMenu("Build Graph Now")]
    public void BuildGraph()
    {
        nodes.Clear();
        edges.Clear();

        // 1. Find all waypoints
        GameObject[] waypointObjects = GameObject.FindGameObjectsWithTag("Waypoint");
        
        if (waypointObjects.Length == 0)
        {
            Debug.LogWarning("No objects found with the tag 'Waypoint'! Please tag your spheres.");
            return;
        }

        foreach (GameObject obj in waypointObjects)
        {
            nodes.Add(obj.transform);
            edges[obj.transform] = new List<Transform>();
        }

        // 2. Connect waypoints
        foreach (Transform nodeA in nodes)
        {
            foreach (Transform nodeB in nodes)
            {
                if (nodeA == nodeB) continue;

                Vector3 dir = nodeB.position - nodeA.position;
                float dist = dir.magnitude;

                Debug.DrawRay(nodeA.position, dir.normalized * dist, Color.red, 5f);
                
                // Note: For the raycast to work, your wall cube MUST have a Collider!
                if (!Physics.Raycast(nodeA.position, dir.normalized, dist))
                {
                    edges[nodeA].Add(nodeB);
                }
            }
        }
        Debug.Log($"Waypoint Graph Built: {nodes.Count} nodes connected.");
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