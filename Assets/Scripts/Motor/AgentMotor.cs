using UnityEngine;
using Override.Core;

namespace Override.Motor
{
    [RequireComponent(typeof(CharacterController))]
    public class AgentMotor : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float rotationSpeed = 10f;

        private IAgentBrain brain;
        private CharacterController controller;
        private Transform playerTransform;
        
        private int currentPathIndex = 0; 
        private Vector3 lastPathFirstNode = Vector3.zero; // Tracks the FIRST node only

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            brain = GetComponent<IAgentBrain>(); 
        }

        private void Start()
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
        }

        private void Update()
        {
            if (brain == null || playerTransform == null) return;

            WorldSnapshot currentSnapshot = new WorldSnapshot
            {
                PlayerPosition = playerTransform.position,
                PlayerForward = playerTransform.forward
            };

            AgentCommand command = brain.Tick(currentSnapshot);

            if (command.Path != null && command.Path.Length > 0)
            {
                // Reset index ONLY if the first node of the path has changed
                if (lastPathFirstNode != command.Path[0])
                {
                    currentPathIndex = 0;
                    lastPathFirstNode = command.Path[0];
                }

                // Clamp index to prevent out of bounds
                if (currentPathIndex >= command.Path.Length) 
                {
                    currentPathIndex = command.Path.Length - 1;
                }

                Vector3 targetPoint = command.Path[currentPathIndex];
                
                Vector3 direction = targetPoint - transform.position;
                direction.y = 0; 
                direction = direction.normalized;
                
                controller.Move(direction * moveSpeed * Time.deltaTime);

                if (direction != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                }

                float distanceToTarget = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), 
                                                        new Vector3(targetPoint.x, 0, targetPoint.z));

                if (distanceToTarget < 1.5f)
                {
                    if (currentPathIndex < command.Path.Length - 1)
                    {
                        currentPathIndex++; 
                    }
                }
            }
        }
    }
}