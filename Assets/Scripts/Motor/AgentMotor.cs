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

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            brain = GetComponent<IAgentBrain>(); 
        }

        private void Start()
        {
            // CACHE IT ONCE at the start
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
            }
        }

        private void Update()
        {
            if (brain == null || playerTransform == null) return;

            // No more Find() inside Update!
            WorldSnapshot currentSnapshot = new WorldSnapshot
            {
                PlayerPosition = playerTransform.position,
                PlayerForward = playerTransform.forward
            };

            AgentCommand command = brain.Tick(currentSnapshot);

            if (command.Path != null && command.Path.Length > 0)
            {
                Vector3 targetPoint = command.Path[0];
                Vector3 direction = (targetPoint - transform.position).normalized;
                
                controller.Move(direction * moveSpeed * Time.deltaTime);

                if (direction != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                }
            }
        }
    }
}