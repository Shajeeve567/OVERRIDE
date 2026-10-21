using UnityEngine;
using Override.Core;

namespace Override.Motor
{
    [RequireComponent(typeof(CharacterController))]
    public class AgentMotor : MonoBehaviour
    {
        public bool isDead = false;

        public Vector3 Velocity => controller != null ? controller.velocity : Vector3.zero;

        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private float fireInterval = 1f;
        [SerializeField] private float fireDamage = 6f;
        [SerializeField] private Transform shootOrigin;
        [SerializeField] private bool canShoot = false;

        private IAgentBrain brain;
        private CharacterController controller;
        private Transform playerTransform;
        private WispVisuals visuals;

        private int currentPathIndex = 0;
        private Vector3 lastPathFirstNode = Vector3.zero;

        private float nextFireTime = 0f;

        // [Integration] A short visible beam for each shot (it used to be a Scene-view debug line only).
        [SerializeField] private Color beamColour = new Color(0.3f, 0.9f, 1f, 0.9f);
        [SerializeField] private float beamSeconds = 0.08f;
        private LineRenderer beam;
        private float beamHideTime;
        private CharacterController playerBody;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            brain = GetComponent<IAgentBrain>();
            visuals = GetComponentInChildren<WispVisuals>(true);
            if (canShoot) CreateBeam();
        }

        private void CreateBeam()
        {
            var go = new GameObject("ShotBeam");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.startWidth = 0.05f;
            beam.endWidth = 0.02f;
            beam.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            beam.startColor = beamColour;
            beam.endColor = new Color(beamColour.r, beamColour.g, beamColour.b, 0.2f);
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.enabled = false;
        }

        private void Start()
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
                playerBody = playerObj.GetComponent<CharacterController>();
            }
        }

        private void Update()
        {
            if (beam != null && beam.enabled && (isDead || Time.time >= beamHideTime)) beam.enabled = false;
            if (isDead) return;
            if (brain == null || playerTransform == null) return;

            WorldSnapshot currentSnapshot = new WorldSnapshot
            {
                PlayerPosition = playerTransform.position,
                PlayerForward = playerTransform.forward
            };

            AgentCommand command = brain.Tick(currentSnapshot);

            if (canShoot && command.Fire && Time.time >= nextFireTime)
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

        // [Integration] Aims at the player's chest from the Wisp's centre (it used to aim at the feet from
        // above the Wisp, so the floor or any prop near the feet stopped the shot). Other agents in the way
        // are ignored; anything else that blocks the shot is logged.
        private void ShootPlayer()
        {
            if (playerTransform == null) return;

            Vector3 origin = shootOrigin != null ? shootOrigin.position : transform.position;
            Vector3 target = playerBody != null ? playerBody.bounds.center : playerTransform.position + Vector3.up;
            Vector3 toPlayer = target - origin;

            if (toPlayer.sqrMagnitude < 0.0001f) return;

            if (visuals != null) visuals.PlayAttack();

            Vector3 direction = toPlayer.normalized;
            float distance = toPlayer.magnitude;
            Vector3 beamEnd = target;

            RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance + 0.5f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.root == transform.root) continue;

                bool isPlayer = hit.transform.root == playerTransform.root || hit.transform.CompareTag("Player");
                IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();

                if (!isPlayer)
                {
                    if (damageable != null) continue;   // another agent (Wisp, Warden, Seeker) in the way
                    beamEnd = hit.point;
                    Debug.Log("[Wisp] Shot blocked by " + hit.collider.name);
                    break;
                }

                beamEnd = hit.point;
                if (damageable != null) damageable.TakeDamage(fireDamage, hit.point, -hit.normal);
                Debug.Log("[Wisp] Hit the player for " + fireDamage + " damage!");
                break;
            }

            if (beam != null)
            {
                beam.SetPosition(0, origin);
                beam.SetPosition(1, beamEnd);
                beam.enabled = true;
                beamHideTime = Time.time + beamSeconds;
            }
        }
    }
}
