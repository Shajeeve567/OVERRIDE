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
        [SerializeField] private float hoverHeight = 2.5f;
        [SerializeField] private float fireInterval = 1f;
        [SerializeField] private float fireDamage = 6f;
        [SerializeField] private Transform shootOrigin;
        [SerializeField] private bool canShoot = false;
        [SerializeField] private bool isFlying = false;

        private IAgentBrain brain;
        private CharacterController controller;
        private Transform playerTransform;
        private WispVisuals visuals;

        private const float StuckTimeout = 3f;
        private const float StuckDistanceEpsilon = 0.05f;
        private const int PathProbeCapacity = 16;

        private static readonly RaycastHit[] pathProbeHits = new RaycastHit[PathProbeCapacity];

        private int currentPathIndex = 0;
        private Vector3 lastPathFirstNode = Vector3.zero;

        private Vector3 lastStuckTarget;
        private float bestDistanceToTarget = float.PositiveInfinity;
        private float stuckTimer;

        private float nextFireTime = 0f;

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

            if (visuals != null)
            {
                controller.height = 1.2f;
                controller.radius = 0.45f;
                controller.center = Vector3.zero;
                controller.skinWidth = 0.04f;
            }

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
            bool isExitingBay = command.AnimationIntent == "ExitBay";

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

                while (currentPathIndex < command.Path.Length - 1 &&
                       HasClearPathTo(command.Path[currentPathIndex + 1]))
                {
                    currentPathIndex++;
                }

                Vector3 targetPoint = command.Path[currentPathIndex];
                Vector3 toTarget = targetPoint - transform.position;
                Vector3 planarDirection = new Vector3(toTarget.x, 0f, toTarget.z);
                float planarDistance = planarDirection.magnitude;

                if (planarDistance > 0.001f)
                {
                    planarDirection /= planarDistance;
                }
                else
                {
                    planarDirection = Vector3.zero;
                }

                Vector3 velocity = planarDirection * moveSpeed;

                if (isFlying)
                {
                    float desiredHeight = isExitingBay
                        ? Mathf.Min(transform.position.y, hoverHeight)
                        : hoverHeight;
                    float verticalSpeed = Mathf.Clamp(
                        (desiredHeight - transform.position.y) * 2f,
                        -moveSpeed,
                        moveSpeed);
                    velocity += Vector3.up * verticalSpeed;
                }

                controller.Move(velocity * Time.deltaTime);

                if (planarDirection != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(planarDirection);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                }

                float distanceToTarget = Vector3.Distance(
                    new Vector3(transform.position.x, 0, transform.position.z),
                    new Vector3(targetPoint.x, 0, targetPoint.z));

                if (isFlying)
                {
                    if (!command.Fire && !isExitingBay)
                    {
                        if ((targetPoint - lastStuckTarget).sqrMagnitude > 0.25f)
                        {
                            lastStuckTarget = targetPoint;
                            bestDistanceToTarget = float.PositiveInfinity;
                            stuckTimer = 0f;
                        }

                        if (distanceToTarget < bestDistanceToTarget - StuckDistanceEpsilon)
                        {
                            bestDistanceToTarget = distanceToTarget;
                            stuckTimer = 0f;
                        }
                        else
                        {
                            stuckTimer += Time.deltaTime;

                            if (stuckTimer >= StuckTimeout)
                            {
                                WispHealth health = GetComponent<WispHealth>();
                                if (health != null)
                                {
                                    health.TakeDamage(999f);
                                }
                                else
                                {
                                    Destroy(gameObject);
                                }

                                return;
                            }
                        }
                    }
                    else
                    {
                        stuckTimer = 0f;
                        bestDistanceToTarget = float.PositiveInfinity;
                    }
                }
                else
                {
                    stuckTimer = 0f;
                    bestDistanceToTarget = float.PositiveInfinity;
                }

                if (distanceToTarget < 1.5f && currentPathIndex < command.Path.Length - 1)
                {
                    currentPathIndex++;
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

        private bool HasClearPathTo(Vector3 point)
        {
            Vector3 delta = point - transform.position;
            float distance = delta.magnitude;

            if (distance < 0.001f) return true;

            float radius = controller != null ? Mathf.Max(0.1f, controller.radius * 0.9f) : 0.45f;
            int hitCount = Physics.SphereCastNonAlloc(
                transform.position,
                radius,
                delta / distance,
                pathProbeHits,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = pathProbeHits[i];
                if (hit.collider == null) continue;

                if (hit.transform.root == transform.root) continue;

                bool isPlayer = hit.transform.CompareTag("Player") ||
                                hit.transform.root.CompareTag("Player");
                if (isPlayer) continue;

                if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;

                return false;
            }

            return true;
        }

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
                    if (damageable != null) continue;
                    beamEnd = hit.point;
                    break;
                }

                beamEnd = hit.point;
                if (damageable != null) damageable.TakeDamage(fireDamage, hit.point, -hit.normal);
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
