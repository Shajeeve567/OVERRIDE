using UnityEngine;

public class AgentCommand
{
    public Vector3[] Path;
    public Vector3 LookTarget;
    public bool Fire;
    public string AnimationIntent; // e.g., "Walk", "Attack", "Hover"
}