public interface IAgentBrain
{
    // The pure C# logic that calculates what to do. No Unity physics/transforms here.
    AgentCommand Tick(WorldSnapshot snapshot);
}