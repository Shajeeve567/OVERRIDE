using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class AgentMotor : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 10f;

    private IAgentBrain brain;
    private WorldSnapshot currentSnapshot;
    private CharacterController controller;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        // Find a component that implements IAgentBrain on this object
        brain = GetComponent<IAgentBrain>(); 
    }

    private void Update()
    {
        if (brain == null) return;

        // 1. Gather snapshot (Simplified for now. Later, get this from a shared GameManager)
        currentSnapshot = new WorldSnapshot
        {
            PlayerPosition = GameObject.FindWithTag("Player")?.transform.position ?? Vector3.zero
        };

        // 2. Ask the brain what to do
        AgentCommand command = brain.Tick(currentSnapshot);

        // 3. Execute Movement
        if (command.Path != null && command.Path.Length > 0)
        {
            Vector3 targetPoint = command.Path[0];
            Vector3 direction = (targetPoint - transform.position).normalized;
            
            // Move
            controller.Move(direction * moveSpeed * Time.deltaTime);

            // Rotate
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            // Arrival check
            if (Vector3.Distance(transform.position, targetPoint) < 0.5f)
            {
                // Remove this node from the path (in a real scenario, you'd shift the array)
                // For now, just stop.
            }
        }
    }
}