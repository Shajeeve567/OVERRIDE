namespace Override.Core
{
    public interface IAgentBrain
    {
        AgentCommand Tick(WorldSnapshot snapshot);
    }
}