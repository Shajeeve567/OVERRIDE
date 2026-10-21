// HunterBody.cs
// The Seeker in the scene: its senses, its movement and its lunge. All decisions come from HunterBrain;
// this MonoBehaviour only gathers what the Seeker can sense and carries out the brain's command.
//
// SENSES (what the brain may know):
//   Hearing    every NoiseBus noise within 1.5 × its radius (GDD), halved if a closed door is in between,
//              which also marks the noise as muffled (placed less precisely).
//   Proximity  the player within 3 m and not behind a wall (the Seeker has no vision).
//   Cameras    sightings from the security camera network, until the Labs terminal takes it offline.
// MOVEMENT: a simple path follower on a kinematic Rigidbody (hover, turn, bob). It's a stand-in for
// Rithish's team motor: swapping it changes nothing in the brain.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Override.Core;
using Override.Director;
using Override.HunterAI;
using Override.Interaction;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Override.Hunter
{
    [RequireComponent(typeof(Rigidbody))]
    public class HunterBody : MonoBehaviour, IHunterSenses
    {
        [SerializeField] HunterSettings settings = new HunterSettings();

        [Header("Grid")]
        [SerializeField] float cellSize = 2f;            // GDD: 2 m cells
        [SerializeField] float playerClearance = 1.0f;   // crouching player (vents are 1.2 m)
        [SerializeField] float hunterClearance = 2.0f;   // the hovering orb
        [SerializeField] float agentRadius = 0.4f;
        [Tooltip("Optional: only floors under this are baked. Empty = Shajeeve's OVERRIDE_Greybox if present, else the whole scene.")]
        [SerializeField] Transform levelRoot;

        [Header("Movement")]
        [SerializeField] float hoverHeight = 1.2f;       // GDD: hovering 1.2 m up
        [SerializeField] float turnSpeed = 360f;         // degrees per second
        [SerializeField] Transform visual;               // bobbed up and down, purely cosmetic
        [SerializeField] float bobAmplitude = 0.08f;
        [SerializeField] float bobFrequency = 1.5f;
        [SerializeField] float obstacleRefreshInterval = 1f;

        static readonly ProfilerMarker TickMarker = new ProfilerMarker("Hunter.Tick");

        HunterBrain brain;
        Rigidbody body;
        SeekerVisuals seekerVisuals;
        float floorTop;

        // Senses, filled between ticks and cleared after each one.
        readonly List<HeardNoise> heard = new List<HeardNoise>();
        readonly List<Vector3> sightings = new List<Vector3>();
        readonly List<Bounds> obstacles = new List<Bounds>();
        readonly RaycastHit[] doorHits = new RaycastHit[8];
        readonly WorldSnapshot snapshot = new WorldSnapshot();
        readonly Stopwatch stopwatch = new Stopwatch();

        Transform player;
        Collider playerCollider;
        IDamageable playerDamageable;
        DirectorController director;
        readonly List<(Collider collider, IDoor door)> doors = new List<(Collider, IDoor)>();
        readonly List<Collider> crates = new List<Collider>();

        AgentCommand command;
        Vector3[] followingPath;
        int pathIndex;
        float nextTick, nextObstacleRefresh, nextAttack;
        bool tickRequested;
        Vector3 progressAnchor;
        float progressTime;
        Vector3 visualRest;

        public HunterBrain Brain => brain;
        public HunterSettings Settings => settings;
        public float FloorTop => floorTop;
        public double LastTickMilliseconds { get; private set; }
        public Vector3[] CurrentPath => followingPath;
        public AgentCommand CurrentCommand => command;
        public float HoverHeight => hoverHeight;
        public event Action Attacked;

        // ---------------------------------------------------------------- IHunterSenses
        public Vector3 Position => transform.position;
        public IReadOnlyList<HeardNoise> HeardNoises => heard;
        public IReadOnlyList<Vector3> CameraSightings => sightings;
        public bool CamerasOnline => CameraNetwork.Online && (director == null || director.CamerasOnline);
        public float Alarm => director != null ? director.Alarm : 0f;
        public IReadOnlyList<Bounds> DynamicObstacles => obstacles;

        public bool SenseContact(out Vector3 playerPosition)
        {
            playerPosition = default;
            if (player == null) return false;
            Vector3 to = player.position + Vector3.up;
            if ((to - transform.position).sqrMagnitude > settings.ProximityRadius * settings.ProximityRadius) return false;
            if (!LineOfSight(transform.position, to)) return false;   // not through walls
            playerPosition = player.position;
            return true;
        }

        /// The nearest thing between a and b, ignoring the Seeker's own colliders, must be the player.
        bool LineOfSight(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float length = d.magnitude;
            int n = Physics.RaycastNonAlloc(a, d / length, doorHits, length, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider first = null;
            for (int k = 0; k < n; k++)
            {
                if (doorHits[k].collider.attachedRigidbody == body) continue;   // our own orb or core
                if (doorHits[k].distance < nearest) { nearest = doorHits[k].distance; first = doorHits[k].collider; }
            }
            return first == null || first == playerCollider;
        }

        // ---------------------------------------------------------------- setup

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            seekerVisuals = GetComponent<SeekerVisuals>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (visual != null) visualRest = visual.localPosition;

            if (levelRoot == null)
            {
                var greybox = GameObject.Find("OVERRIDE_Greybox");
                if (greybox != null) levelRoot = greybox.transform;
            }
            var grid = HunterGridBaker.Bake(new HunterGridBaker.Options
            {
                CellSize = cellSize,
                PlayerClearance = playerClearance,
                HunterClearance = hunterClearance,
                Radius = agentRadius,
                LevelRoot = levelRoot,
            }, out floorTop);
            brain = new HunterBrain(settings, grid, this);
        }

        void Start()
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null)
            {
                player = p.transform;
                playerCollider = p.GetComponent<CharacterController>();
                playerDamageable = p.GetComponent<IDamageable>();
            }
            director = FindFirstObjectByType<DirectorController>();

            // Everything that can block a path but moves: doors (when closed) and crates. Found once.
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is IDoor d && mb.TryGetComponent(out Collider c)) doors.Add((c, d));
            foreach (var crate in FindObjectsByType<CrateBarricade>(FindObjectsSortMode.None))
                if (crate.TryGetComponent(out Collider c)) crates.Add(c);

            progressAnchor = transform.position;
            progressTime = Time.time;
        }

        void OnEnable()
        {
            NoiseBus.Emitted += OnNoise;
            CameraNetwork.Sighted += OnSighted;
        }

        void OnDisable()
        {
            NoiseBus.Emitted -= OnNoise;
            CameraNetwork.Sighted -= OnSighted;
        }

        // ---------------------------------------------------------------- senses

        void OnNoise(NoiseEvent e)
        {
            float range = settings.HearingSensitivity * e.Radius;
            bool muffled = ClosedDoorBetween(e.Position + Vector3.up * 0.5f, transform.position);
            if (muffled) range *= 0.5f;   // GDD: a closed door halves the noise radius
            if ((e.Position - transform.position).sqrMagnitude > range * range) return;

            heard.Add(new HeardNoise(e.Position, muffled));
            tickRequested = true;   // GDD: decide immediately on each heard noise
        }

        void OnSighted(Vector3 at) => sightings.Add(at);

        bool ClosedDoorBetween(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) return false;
            int n = Physics.RaycastNonAlloc(a, d / length, doorHits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
                foreach (var (collider, door) in doors)
                    if (doorHits[k].collider == collider && !door.IsOpen) return true;
            return false;
        }

        void RefreshObstacles()
        {
            obstacles.Clear();
            foreach (var (collider, door) in doors)
                if (collider != null && !door.IsOpen) obstacles.Add(collider.bounds);
            foreach (var c in crates)
                if (c != null) obstacles.Add(c.bounds);
            brain.RefreshObstacles(obstacles, agentRadius);
        }

        // ---------------------------------------------------------------- decide

        void Update()
        {
            if (Time.time < nextTick && !tickRequested) return;
            tickRequested = false;
            nextTick = Time.time + settings.DecisionInterval;

            if (Time.time >= nextObstacleRefresh)
            {
                nextObstacleRefresh = Time.time + obstacleRefreshInterval;
                RefreshObstacles();
            }

            snapshot.Time = Time.time;
            snapshot.CamerasOnline = CamerasOnline;
            // snapshot.PlayerPosition is deliberately left unset: the Seeker is blind.

            stopwatch.Restart();
            using (TickMarker.Auto()) command = brain.Tick(snapshot);
            stopwatch.Stop();
            LastTickMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

            heard.Clear();
            sightings.Clear();

            if (!ReferenceEquals(command.Path, followingPath))
            {
                followingPath = command.Path;
                pathIndex = 0;
            }

            TryLunge();
        }

        void TryLunge()
        {
            if (!command.Fire || Time.time < nextAttack || playerDamageable == null || player == null) return;
            if (Vector3.Distance(transform.position, player.position + Vector3.up) > settings.AttackRange) return;
            nextAttack = Time.time + settings.AttackCooldown;
            playerDamageable.TakeDamage(settings.AttackDamage, player.position + Vector3.up, transform.forward);
            Attacked?.Invoke();
        }

        // ---------------------------------------------------------------- move

        void FixedUpdate()
        {
            if (brain == null) return;
            float dt = Time.fixedDeltaTime;
            Vector3 pos = body.position;

            Vector3 target = pos;
            if (followingPath != null && pathIndex < followingPath.Length)
            {
                Vector3 p = followingPath[pathIndex];
                target = new Vector3(p.x, p.y + hoverHeight, p.z);
                Vector2 flat = new Vector2(target.x - pos.x, target.z - pos.z);
                if (flat.magnitude < 0.4f) pathIndex++;
            }

            Vector3 next = Vector3.MoveTowards(pos, target, brain.DesiredSpeed * dt);
            body.MovePosition(next);

            // Face where the brain is looking (yaw only), at most turnSpeed degrees per second.
            Vector3 look = command != null ? command.LookTarget - pos : Vector3.zero;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(look), turnSpeed * dt));

            CheckStuck(pos);
        }

        void CheckStuck(Vector3 pos)
        {
            bool travelling = followingPath != null && pathIndex < followingPath.Length;
            if (!travelling || (pos - progressAnchor).sqrMagnitude > 0.2f * 0.2f)
            {
                progressAnchor = pos;
                progressTime = Time.time;
                return;
            }
            if (Time.time - progressTime < settings.StuckSeconds) return;
            // Edge case "stuck with no progress for 2 s": avoid the next path cell for 3 s and replan.
            brain.ReportStuck(followingPath[pathIndex], Time.time);
            tickRequested = true;
            progressTime = Time.time;
        }

        void LateUpdate()
        {
            if (seekerVisuals == null && visual != null)
                visual.localPosition = visualRest + Vector3.up * (Mathf.Sin(Time.time * bobFrequency * 2f * Mathf.PI) * bobAmplitude);
        }
    }
}
