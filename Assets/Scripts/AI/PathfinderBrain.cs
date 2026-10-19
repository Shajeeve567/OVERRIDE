using UnityEngine;
using Override.Core;

namespace Override.AI
{
    public class PathfinderBrain : MonoBehaviour, IAgentBrain
    {
        public AgentCommand Tick(WorldSnapshot snapshot)
        {
            AgentCommand cmd = new AgentCommand();
            
            // EMERGENCY STUB: Just draw a straight line to the player.
            // TODO: Replace with your custom A* on the waypoint graph!
            cmd.Path = new Vector3[] { snapshot.PlayerPosition };
            cmd.LookTarget = snapshot.PlayerPosition;
            cmd.AnimationIntent = "Hover";
            
            return cmd;
        }
    }
}