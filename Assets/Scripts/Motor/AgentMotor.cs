using UnityEngine;
using Override.Core;

namespace Override.Motor
{
    [RequireComponent(typeof(CharacterController))]
    public class AgentMotor : MonoBehaviour
    {
        public bool isDead = false;

        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private float fireInterval = 1f;
        [SerializeField] private float fireDamage = 6f;

        private IAgentBrain brain;
        private CharacterController controller;
        private Transform playerTransform;

        private int currentPathIndex = 0;
        private Vector3 lastPathFirstNode = Vector3.zero;

        private float nextFireTime = 0f;

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
            if (isDead) return;
            if (brain == null || playerTransform == null) return;

            WorldSnapshot currentSnapshot = new WorldSnapshot
            {
                PlayerPosition = playerTransform.position,
                PlayerForward = playerTransform.forward
            };

            AgentCommand command = brain.Tick(currentSnapshot);

            if (command.Fire && Time.time >= nextFireTime)
            {
                nextFireTime = Time.time + fireInterval;
                ShootPlayer();
            }

            if (command.Path != null && command.Path.Length > 0)
            {
                if (lastPathFirstNode != command.Path[0])
                {
                    currentPathIndex = 0;
                    lastPathFirstNode = command.Path[0];
                }

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

                float distanceToTarget = Vector3.Distance(
                    new Vector3(transform.position.x, 0, transform.position.z),
                    new Vector3(targetPoint.x, 0, targetPoint.z));

                if (distanceToTarget < 1.5f)
                {
                    if (currentPathIndex < command.Path.Length - 1)
                    {
                        currentPathIndex++;
                    }
                }
            }

            if (command.Fire)
            {
                Vector3 lookDirection = command.LookTarget - transform.position;
                lookDirection.y = 0;

                if (lookDirection.sqrMagnitude > 0.0001f)
                {
                    Quaternion lookRotation = Quaternion.LookRotation(lookDirection.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, rotationSpeed * Time.deltaTime);
                }
            }
        }

        private void ShootPlayer()
        {
            if (playerTransform == null) return;

            Vector3 origin = transform.position;
            Vector3 toPlayer = playerTransform.position - origin;

            if (toPlayer.sqrMagnitude < 0.0001f) return;

            Vector3 direction = toPlayer.normalized;
            float distance = toPlayer.magnitude;

            Debug.DrawLine(origin, playerTransform.position, Color.red, 0.1f);

            RaycastHit hit;
            if (Physics.Raycast(origin, direction, out hit, distance + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.CompareTag("Player"))
                {
                    hit.transform.SendMessage("TakeDamage", fireDamage, SendMessageOptions.DontRequireReceiver);

                    IDamageable damageable = hit.transform.GetComponentInParent<IDamageable>();
                    if (damageable != null)
                    {
                        damageable.TakeDamage(fireDamage, hit.point, -hit.normal);
                    }

                    Debug.Log("[Wisp] Hit the player for " + fireDamage + " damage!");
                }
            }
        }
    }
}
