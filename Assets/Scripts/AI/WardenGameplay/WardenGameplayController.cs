using System.Text;
using Override.Combat;
using Override.Core;
using Override.Director;
using Override.WardenAI;
using Override.Motor;
using UnityEngine;

/// <summary>
/// Unity-facing adapter for the pure Warden Expectimax brain. It reads confirmed
/// sensor memory, supplies Warden-specific tactical options, and translates the
/// selected abstract action into the existing motor and Warden weapon.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(AgentMotor))]
public sealed class WardenGameplayController : MonoBehaviour, IAgentBrain
{
    private enum MovementIntent
    {
        None,
        TacticalCover,
        Pursuit,
        Investigation,
        ReturnToGuard
    }

    private const float PushObservationDistance = 1.5f;
    private const float ObservationCooldown = 1.2f;

    [Header("Live references")]
    [SerializeField] private VisionSensor _vision;
    [SerializeField] private HearingSensor _hearing;
    [SerializeField] private WardenWeapon _weapon;
    [SerializeField] private Health _health;
    [SerializeField] private Transform _playerTarget;
    [SerializeField] private DirectorController _director;

    [Header("Cover options")]
    [SerializeField] private WardenCoverPoint _currentCover;
    [SerializeField] private WardenCoverPoint _advanceCover;
    [SerializeField] private WardenCoverPoint _flankCover;
    [SerializeField] private WardenCoverPoint _retreatCover;
    [SerializeField, Min(0.1f)] private float _coverArrivalDistance = 1.5f;

    [Header("Sound investigation")]
    [SerializeField, Min(0.1f)] private float _hearingInvestigationSeconds = 4f;
    [SerializeField, Min(0f)] private float _investigationStandOffDistance = 7f;
    [SerializeField, Min(0.1f)] private float _investigationStepDistance = 6f;
    [SerializeField, Min(0.1f)] private float _maximumRoamDistance = 14f;
    [SerializeField, Range(0f, 1f)] private float _investigationCoverQuality = 0.4f;
    [Header("Territorial pursuit")]
    [SerializeField, Min(0.1f)] private float _chaseActivationDistance = 18f;
    [SerializeField, Min(0f)] private float _chaseStopDistance = 5f;
    [SerializeField, Min(0.1f)] private float _lostTargetPursuitSeconds = 5f;
    [SerializeField, Min(0f)] private float _movementCommitmentSeconds = 1.5f;

    [Header("Decision and presentation")]
    [SerializeField, Min(0.05f)] private float _decisionInterval = 0.4f;
    [SerializeField, Min(0f)] private float _aimRotationSpeed = 8f;
    [SerializeField] private bool _showDebugPanel = true;

    private readonly AgentCommand _command = new AgentCommand();
    private WardenExpectimaxBrain _brain;
    private WardenDecisionResult _lastResult;
    private Health _playerHealth;
    private Collider _playerCollider;
    private WardenCoverPoint _movementTarget;
    private WardenCoverPoint _guardCover;
    private Health _subscribedHealth;
    private Vector3 _lastKnownPosition;
    private float _lastKnownTime = float.NegativeInfinity;
    private float _nextDecisionTime;
    private float _nextPushObservationTime;
    private float _visibleDistanceBaseline;
    private long _observationSequence;
    private bool _visibilitySampled;
    private bool _previousVisibility;
    private bool _hasKnownPosition;
    private string _lastObservation = "No confirmed player observation yet.";
    private Vector3 _guardAnchor;
    private Vector3 _investigationDestination;
    private bool _hasGuardAnchor;
    private bool _hasInvestigationDestination;
    private MovementIntent _movementIntent;
    private float _movementCommitUntil;
    private GUIStyle _bodyStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _selectedStyle;
    private GUIStyle _warningStyle;

    public WardenDecisionResult LastResult => _lastResult;
    public Vector3 LastKnownPosition => _lastKnownPosition;
    public bool HasKnownPosition => _hasKnownPosition;
    public WardenCoverPoint CurrentCover => _currentCover;
    public Transform PlayerTarget => _playerTarget;
    public DirectorController Director => _director;
    public WardenCoverPoint AdvanceCover => _advanceCover;
    public WardenCoverPoint FlankCover => _flankCover;
    public WardenCoverPoint RetreatCover => _retreatCover;
    public bool IsInvestigatingNoise => _hasInvestigationDestination;
    public Vector3 InvestigationDestination => _investigationDestination;
    public bool IsPursuing => _movementIntent == MovementIntent.Pursuit;
    public bool IsReturningToGuard => _movementIntent == MovementIntent.ReturnToGuard;
    public Vector3 GuardAnchor => _guardAnchor;

    private void Awake()
    {
        EnsureBrain();
        _command.Path = null;
        CacheLocalReferences();
        CacheGuardAnchor();
    }

    private void EnsureBrain()
    {
        if (_brain == null)
            _brain = new WardenExpectimaxBrain();
    }

    private void Start()
    {
        if (_currentCover != null)
            _currentCover.TryOccupy(gameObject);
    }

    private void OnEnable()
    {
        CacheLocalReferences();
    }

    private void OnDisable()
    {
        _currentCover?.Release(gameObject);
        _movementTarget?.Release(gameObject);
        _weapon?.CancelActions();
        SetHealthSubscription(null);
    }

    private void LateUpdate()
    {
        UpdateFacing(Time.deltaTime);
    }

    /// <summary>Rotates the Warden toward confirmed player/noise memory whether stationary or repositioning.</summary>
    public void UpdateFacing(float deltaTime)
    {
        if (!_hasKnownPosition || _aimRotationSpeed <= 0f || (_health != null && _health.IsDead))
            return;

        Vector3 direction = _lastKnownPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            _aimRotationSpeed * deltaTime);
    }

    /// <summary>Called by the existing shared AgentMotor every frame.</summary>
    public AgentCommand Tick(WorldSnapshot snapshot)
    {
        CacheLocalReferences();
        UpdateConfirmedKnowledge();
        ObserveVisiblePlayerBehaviour();
        CompleteMovementIfArrived();

        if (_lastResult == null || Time.time >= _nextDecisionTime)
            DecideAndApply();

        return _command;
    }

    public WardenDecisionState BuildDecisionStateNow()
    {
        CacheLocalReferences();
        UpdateConfirmedKnowledge();

        bool visible = _vision != null && _vision.CanSeeTarget;
        float distance = _hasKnownPosition
            ? Vector3.Distance(transform.position, _lastKnownPosition)
            : _vision != null ? _vision.Range : 35f;
        Vector3 optionReference = _hasKnownPosition
            ? _lastKnownPosition
            : transform.position + transform.forward * distance;

        bool operational = isActiveAndEnabled && (_health == null || !_health.IsDead);
        bool currentCoverUsable = _currentCover != null && _currentCover.CanUse(gameObject);
        int maximumAmmo = _weapon != null ? _weapon.MagazineSize : 1;
        int ammo = _weapon != null ? _weapon.Ammo : 0;

        WardenTacticalOption audibleAdvance = BuildAudibleAdvanceOption();

        return new WardenDecisionState
        {
            IsOperational = operational,
            CanHoldPosition = operational,
            PlayerVisible = visible,
            HasKnownPlayerPosition = _hasKnownPosition,
            DistanceToPlayer = Mathf.Max(0f, distance),
            Ammo = Mathf.Clamp(ammo, 0, maximumAmmo),
            MaxAmmo = maximumAmmo,
            CurrentCoverUsable = currentCoverUsable,
            CurrentCoverQuality = currentCoverUsable ? _currentCover.Quality : 0f,
            Desperation = CalculateDesperation(),
            AdvanceOption = audibleAdvance ?? BuildOption(_advanceCover, optionReference),
            FlankOption = BuildOption(_flankCover, optionReference),
            RetreatOption = BuildOption(_retreatCover, optionReference)
        };
    }

    private void DecideAndApply()
    {
        EnsureBrain();
        _lastResult = _brain.Decide(BuildDecisionStateNow());
        _nextDecisionTime = Time.time + _decisionInterval;
        ApplyDecision(_lastResult.SelectedAction);
    }

    private bool TryApplyTerritorialMovement()
    {
        CacheGuardAnchor();
        bool visible = _vision != null && _vision.CanSeeTarget && _playerTarget != null;
        float distanceFromGuard = PlanarDistance(transform.position, _guardAnchor);

        if (visible)
        {
            float playerDistance = PlanarDistance(_playerTarget.position, transform.position);
            bool playerCloseEnoughToChase = playerDistance <= _chaseActivationDistance;

            if (playerCloseEnoughToChase &&
                playerDistance > _chaseStopDistance + _coverArrivalDistance &&
                distanceFromGuard < _maximumRoamDistance - _coverArrivalDistance)
            {
                Vector3 destination = BuildLeashedStandOffDestination(
                    _playerTarget.position,
                    _chaseStopDistance);
                if (PlanarDistance(destination, transform.position) > _coverArrivalDistance)
                {
                    MoveToPosition(destination, MovementIntent.Pursuit);
                    TryFireAtVisiblePlayer();
                    return true;
                }
            }

            if (playerCloseEnoughToChase)
            {
                // At firing distance or at the leash boundary, hold here and fight. Do not hand
                // control back to a lateral cover choice that makes pursuit look directionless.
                StopMovement();
                TryFireAtVisiblePlayer();
                return true;
            }

            if (distanceFromGuard > _coverArrivalDistance)
            {
                MoveToPosition(_guardAnchor, MovementIntent.ReturnToGuard);
                TryFireAtVisiblePlayer();
                return true;
            }

            if (_movementIntent == MovementIntent.Pursuit ||
                _movementIntent == MovementIntent.Investigation ||
                _movementIntent == MovementIntent.ReturnToGuard)
            {
                StopMovement();
            }

            return false;
        }

        float knowledgeAge = Time.time - _lastKnownTime;
        bool knowledgeIsRecent = _hasKnownPosition &&
                                 knowledgeAge >= 0f &&
                                 knowledgeAge <= _lostTargetPursuitSeconds;
        bool recentVisualMemory = knowledgeIsRecent &&
                                  _vision != null &&
                                  _vision.HasSeenTarget &&
                                  _vision.LastSeenTime >= _lastKnownTime - 0.01f;

        if (recentVisualMemory)
        {
            WardenCoverPoint revealCover = SelectRevealCover();
            if (revealCover != null)
            {
                MoveToCover(revealCover);
                return true;
            }

            // No authored cover angle is available. Move to the confirmed last-seen position,
            // rather than subtracting the hearing stand-off distance (which could send the
            // Warden backwards when it was already close to the player).
            Vector3 destination = BuildLeashedStandOffDestination(_lastKnownPosition, 0f);
            if (PlanarDistance(destination, transform.position) > _coverArrivalDistance)
            {
                _investigationDestination = destination;
                _hasInvestigationDestination = true;
                MoveToPosition(destination, MovementIntent.Investigation);
                return true;
            }
        }

        if (knowledgeIsRecent && _hasInvestigationDestination)
        {
            MoveToPosition(_investigationDestination, MovementIntent.Investigation);
            return true;
        }

        if (distanceFromGuard > _coverArrivalDistance)
        {
            MoveToPosition(_guardAnchor, MovementIntent.ReturnToGuard);
            return true;
        }

        if (_movementIntent == MovementIntent.Pursuit ||
            _movementIntent == MovementIntent.Investigation ||
            _movementIntent == MovementIntent.ReturnToGuard)
        {
            StopMovement();
        }

        return false;
    }

    private WardenCoverPoint SelectRevealCover()
    {
        WardenCoverPoint best = null;
        float bestScore = float.NegativeInfinity;
        ScoreRevealCover(_flankCover, 2f, ref best, ref bestScore);
        ScoreRevealCover(_advanceCover, 1f, ref best, ref bestScore);
        ScoreRevealCover(_retreatCover, 0f, ref best, ref bestScore);
        return best;
    }

    private void ScoreRevealCover(
        WardenCoverPoint cover,
        float rolePreference,
        ref WardenCoverPoint best,
        ref float bestScore)
    {
        if (cover == null || cover == _currentCover || !cover.CanUse(gameObject) ||
            PlanarDistance(cover.Position, _guardAnchor) > _maximumRoamDistance)
        {
            return;
        }

        float currentDistance = PlanarDistance(transform.position, _lastKnownPosition);
        float candidateDistance = PlanarDistance(cover.Position, _lastKnownPosition);
        float clearSightBonus = HasClearSearchLine(cover.Position) ? 100f : 0f;
        float score = clearSightBonus + currentDistance - candidateDistance + rolePreference;
        if (score <= bestScore)
            return;

        best = cover;
        bestScore = score;
    }

    private bool HasClearSearchLine(Vector3 candidatePosition)
    {
        if (_vision == null)
            return false;

        float eyeHeight = _vision.Eye != null
            ? Mathf.Max(0.5f, _vision.Eye.position.y - transform.position.y)
            : 1.5f;
        Vector3 origin = candidatePosition + Vector3.up * eyeHeight;
        Vector3 target = _lastKnownPosition;
        Vector3 toTarget = target - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.01f)
            return true;

        return !Physics.Raycast(
            origin,
            toTarget / distance,
            distance,
            _vision.ObstacleLayers,
            QueryTriggerInteraction.Ignore);
    }

    private void ApplyDecision(WardenAction action)
    {
        _command.Fire = false;
        _command.LookTarget = _hasKnownPosition ? _lastKnownPosition : transform.position + transform.forward;
        _command.AnimationIntent = action.ToString();

        if (_health != null && _health.IsDead)
        {
            StopMovement();
            _weapon?.CancelActions();
            return;
        }

        if (_weapon != null && _weapon.Ammo < _weapon.RequiredAmmoForBurst)
        {
            if (action == WardenAction.RetreatAndReload)
                MoveToCover(_retreatCover);
            else
                StopMovement();
            _command.AnimationIntent = "Reload";
            if (!_weapon.IsReloading && !_weapon.IsFiring)
                _weapon.StartReload();
            return;
        }

        // Territorial movement may own the current path, but it must never bypass death or
        // mandatory reload handling above. In particular, an empty Warden used to keep trying
        // RequestBurst forever without ever entering its reload state.
        if (TryApplyTerritorialMovement())
            return;

        switch (action)
        {
            case WardenAction.PeekAndFire:
                if (!ShouldPreserveCommittedCoverMovement())
                    StopMovement();
                TryFireAtVisiblePlayer();
                break;

            case WardenAction.AdvanceToCover:
                if (_hasInvestigationDestination)
                    MoveToPosition(_investigationDestination, MovementIntent.Investigation);
                else
                    MoveToCover(_advanceCover);
                break;

            case WardenAction.Flank:
                MoveToCover(_flankCover);
                break;

            case WardenAction.RetreatAndReload:
                MoveToCover(_retreatCover);
                _weapon?.StartReload();
                break;

            default:
                if (!ShouldPreserveCommittedCoverMovement())
                    StopMovement();
                if (_weapon != null && !_weapon.IsReloading)
                {
                    bool outOfAmmo = _weapon.Ammo == 0;
                    bool cannotFireFullBurst = _weapon.Ammo < _weapon.RequiredAmmoForBurst;
                    bool safeToReload = _weapon.Ammo < _weapon.MagazineSize && (_vision == null || !_vision.CanSeeTarget);
                    if (outOfAmmo || cannotFireFullBurst || safeToReload)
                    {
                        _weapon.StartReload();
                    }
                }
                break;
        }
    }

    private void MoveToCover(WardenCoverPoint cover)
    {
        if (cover == null || !cover.CanUse(gameObject))
        {
            StopMovement();
            return;
        }

        _movementTarget = cover;
        _command.Path = new[] { cover.Position };
        SetMovementIntent(MovementIntent.TacticalCover);
    }

    private void MoveToPosition(Vector3 position, MovementIntent intent = MovementIntent.Investigation)
    {
        _movementTarget = null;
        _command.Path = new[] { position };
        SetMovementIntent(intent);
    }

    private void SetMovementIntent(MovementIntent intent)
    {
        if (_movementIntent != intent)
            _movementCommitUntil = Time.time + _movementCommitmentSeconds;
        _movementIntent = intent;
    }

    private bool ShouldPreserveCommittedCoverMovement()
    {
        if (_movementIntent != MovementIntent.TacticalCover ||
            _command.Path == null ||
            _command.Path.Length == 0)
        {
            return false;
        }

        return Time.time < _movementCommitUntil || _movementTarget != null;
    }

    private void TryFireAtVisiblePlayer()
    {
        if (_vision != null && _vision.CanSeeTarget && _playerTarget != null && _weapon != null)
            _command.Fire = _weapon.RequestBurst(GetPlayerAimPoint());
    }

    private void StopMovement()
    {
        _movementTarget = null;
        _command.Path = null;
        _movementIntent = MovementIntent.None;
        _movementCommitUntil = 0f;
    }

    private void CompleteMovementIfArrived()
    {
        if (_command.Path == null || _command.Path.Length == 0)
            return;

        Vector3 destination = _movementTarget != null
            ? _movementTarget.Position
            : _command.Path[_command.Path.Length - 1];
        if (PlanarDistance(destination, transform.position) > _coverArrivalDistance)
            return;

        if (_movementTarget != null && _movementTarget.TryOccupy(gameObject))
        {
            if (_currentCover != _movementTarget)
                _currentCover?.Release(gameObject);
            _currentCover = _movementTarget;
        }
        else if (_movementIntent == MovementIntent.ReturnToGuard &&
                 _guardCover != null &&
                 _guardCover.TryOccupy(gameObject))
        {
            if (_currentCover != _guardCover)
                _currentCover?.Release(gameObject);
            _currentCover = _guardCover;
        }

        StopMovement();
    }

    private Vector3 BuildLeashedStandOffDestination(Vector3 targetPosition, float standOffDistance)
    {
        Vector3 fromWarden = targetPosition - transform.position;
        fromWarden.y = 0f;
        Vector3 destination = targetPosition;
        if (fromWarden.sqrMagnitude > 0.001f)
            destination -= fromWarden.normalized * Mathf.Max(0f, standOffDistance);

        Vector3 fromGuard = destination - _guardAnchor;
        fromGuard.y = 0f;
        if (fromGuard.magnitude > _maximumRoamDistance)
            destination = _guardAnchor + fromGuard.normalized * _maximumRoamDistance;

        destination.y = transform.position.y;
        return destination;
    }

    private static float PlanarDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }

    private void UpdateConfirmedKnowledge()
    {
        if (_vision != null && _vision.CanSeeTarget && _playerTarget != null)
        {
            Remember(_playerTarget.position, Time.time);
            return;
        }

        float bestTime = _lastKnownTime;
        Vector3 bestPosition = _lastKnownPosition;
        bool found = _hasKnownPosition;

        if (_vision != null && _vision.HasSeenTarget && _vision.LastSeenTime >= bestTime)
        {
            bestTime = _vision.LastSeenTime;
            bestPosition = _vision.LastSeenPosition;
            found = true;
        }

        if (_hearing != null && _hearing.HasHeardNoise && _hearing.LastHeardTime > bestTime)
        {
            bestTime = _hearing.LastHeardTime;
            bestPosition = _hearing.LastHeardPosition;
            found = true;
        }

        if (found)
        {
            Remember(bestPosition, bestTime);
        }
    }

    private void Remember(Vector3 position, float time)
    {
        _lastKnownPosition = position;
        _lastKnownTime = time;
        _hasKnownPosition = true;
    }

    private void ObserveVisiblePlayerBehaviour()
    {
        if (_vision == null || _playerTarget == null)
            return;

        bool visible = _vision.CanSeeTarget;
        float distance = Vector3.Distance(transform.position, _playerTarget.position);
        if (!_visibilitySampled)
        {
            _visibilitySampled = true;
            _previousVisibility = visible;
            if (visible) _visibleDistanceBaseline = distance;
            return;
        }

        if (visible && !_previousVisibility)
        {
            _visibleDistanceBaseline = distance;
            RecordObservedResponse(PlayerResponse.Peek, "Vision reacquired the player");
        }
        else if (!visible && _previousVisibility)
        {
            RecordObservedResponse(PlayerResponse.Hide, "Confirmed sight was lost");
        }
        else if (visible)
        {
            if (distance > _visibleDistanceBaseline + 0.5f)
                _visibleDistanceBaseline = distance;

            if (Time.time >= _nextPushObservationTime &&
                _visibleDistanceBaseline - distance >= PushObservationDistance)
            {
                _visibleDistanceBaseline = distance;
                _nextPushObservationTime = Time.time + ObservationCooldown;
                RecordObservedResponse(PlayerResponse.Push, "Visible player advanced");
            }
        }

        _previousVisibility = visible;
    }

    private void RecordObservedResponse(PlayerResponse response, string source)
    {
        EnsureBrain();
        _observationSequence++;
        if (_brain.ResponseModel.TryRecordObservedResponse(response, _observationSequence, true))
            _lastObservation = source + ": " + response;
    }

    private WardenTacticalOption BuildOption(WardenCoverPoint cover, Vector3 knownPlayerPosition)
    {
        return cover != null && cover != _currentCover
            ? cover.BuildOption(gameObject, knownPlayerPosition)
            : null;
    }

    private WardenTacticalOption BuildAudibleAdvanceOption()
    {
        _hasInvestigationDestination = false;
        if (_hearing == null || !_hearing.HasHeardNoise ||
            (_vision != null && _vision.CanSeeTarget) ||
            _hearing.LastNoiseWasObstructed)
        {
            return null;
        }

        float age = Time.time - _hearing.LastHeardTime;
        if (age < 0f || age > _hearingInvestigationSeconds)
            return null;

        Vector3 toNoise = _hearing.LastHeardPosition - transform.position;
        toNoise.y = 0f;
        float distance = toNoise.magnitude;
        if (distance <= _investigationStandOffDistance + _coverArrivalDistance)
            return null;

        CacheGuardAnchor();
        Vector3 direction = toNoise / distance;
        float travel = Mathf.Min(
            _investigationStepDistance,
            distance - _investigationStandOffDistance);
        Vector3 destination = transform.position + direction * travel;
        Vector3 fromAnchor = destination - _guardAnchor;
        fromAnchor.y = 0f;
        if (fromAnchor.magnitude > _maximumRoamDistance)
            destination = _guardAnchor + fromAnchor.normalized * _maximumRoamDistance;
        destination.y = transform.position.y;

        if ((destination - transform.position).sqrMagnitude <=
            _coverArrivalDistance * _coverArrivalDistance)
        {
            return null;
        }

        _investigationDestination = destination;
        _hasInvestigationDestination = true;
        return new WardenTacticalOption
        {
            PathAvailable = true,
            CoverAvailable = true,
            CoverOccupied = false,
            CoverQuality = _investigationCoverQuality,
            ResultingDistanceToPlayer = Vector3.Distance(destination, _hearing.LastHeardPosition)
        };
    }

    private void CacheGuardAnchor()
    {
        if (_currentCover != null)
        {
            if (!_hasGuardAnchor)
            {
                _guardCover = _currentCover;
                _guardAnchor = _currentCover.Position;
                _hasGuardAnchor = true;
            }
        }
        else if (!_hasGuardAnchor)
        {
            _guardAnchor = transform.position;
            _hasGuardAnchor = true;
        }
    }

    private float CalculateDesperation()
    {
        if (_director == null) return 0.25f;
        if (_director.FinalWave) return 1f;

        int lost = 0;
        if (!_director.CamerasOnline) lost++;
        if (!_director.DoorControlOnline) lost++;
        if (_director.DroneBudgetNow < _director.droneBudget) lost++;
        return lost / 3f;
    }

    private void CacheLocalReferences()
    {
        if (_vision == null) _vision = GetComponent<VisionSensor>();
        if (_hearing == null) _hearing = GetComponent<HearingSensor>();
        if (_weapon == null) _weapon = GetComponent<WardenWeapon>();
        if (_health == null) _health = GetComponent<Health>();
        SetHealthSubscription(_health);
        CachePlayerReferences();
    }

    private void CachePlayerReferences()
    {
        if (_playerTarget == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                _playerTarget = player.transform;
        }

        if (_playerTarget == null) return;
        if (_playerHealth == null) _playerHealth = _playerTarget.GetComponentInParent<Health>();
        if (_playerCollider == null) _playerCollider = _playerTarget.GetComponentInParent<Collider>();
        if (_vision != null && _vision.Target != _playerTarget)
            _vision.Target = _playerTarget;
    }

    /// <summary>
    /// Supplies scene-owned dependencies without coupling the reusable Warden prefab to a
    /// particular player, Director, or set of cover markers.
    /// </summary>
    public void InitializeExternalReferences(
        Transform playerTarget,
        DirectorController director,
        WardenCoverPoint currentCover,
        WardenCoverPoint advanceCover,
        WardenCoverPoint flankCover,
        WardenCoverPoint retreatCover)
    {
        _playerTarget = playerTarget;
        _director = director;
        _currentCover = currentCover;
        _advanceCover = advanceCover;
        _flankCover = flankCover;
        _retreatCover = retreatCover;
        _hasGuardAnchor = false;
        _guardCover = null;
        CacheGuardAnchor();
        _playerHealth = null;
        _playerCollider = null;

        if (_vision != null)
            _vision.Target = playerTarget;

        if (playerTarget != null)
            CachePlayerReferences();
    }

    private void SetHealthSubscription(Health health)
    {
        if (_subscribedHealth == health)
            return;

        if (_subscribedHealth != null)
            _subscribedHealth.Died -= HandleDeath;

        _subscribedHealth = health;
        if (_subscribedHealth != null)
            _subscribedHealth.Died += HandleDeath;
    }

    private void HandleDeath()
    {
        StopMovement();
        _command.Fire = false;
        _command.AnimationIntent = "Destroyed";
        _weapon?.CancelActions();
    }

    private Vector3 GetPlayerAimPoint()
    {
        return _playerCollider != null ? _playerCollider.bounds.center : _playerTarget.position;
    }

#if UNITY_EDITOR
    public void Configure(
        VisionSensor vision,
        HearingSensor hearing,
        WardenWeapon weapon,
        Health health,
        Transform playerTarget,
        DirectorController director,
        WardenCoverPoint currentCover,
        WardenCoverPoint advanceCover,
        WardenCoverPoint flankCover,
        WardenCoverPoint retreatCover)
    {
        _vision = vision;
        _hearing = hearing;
        _weapon = weapon;
        _health = health;
        _playerTarget = playerTarget;
        _director = director;
        _currentCover = currentCover;
        _advanceCover = advanceCover;
        _flankCover = flankCover;
        _retreatCover = retreatCover;
        SetHealthSubscription(_health);
        InitializeExternalReferences(
            playerTarget,
            director,
            currentCover,
            advanceCover,
            flankCover,
            retreatCover);
    }
#endif

    private void OnValidate()
    {
        _decisionInterval = Mathf.Max(0.05f, _decisionInterval);
        _aimRotationSpeed = Mathf.Max(0f, _aimRotationSpeed);
        _coverArrivalDistance = Mathf.Max(0.1f, _coverArrivalDistance);
        _hearingInvestigationSeconds = Mathf.Max(0.1f, _hearingInvestigationSeconds);
        _investigationStandOffDistance = Mathf.Max(0f, _investigationStandOffDistance);
        _investigationStepDistance = Mathf.Max(0.1f, _investigationStepDistance);
        _maximumRoamDistance = Mathf.Max(0.1f, _maximumRoamDistance);
        _investigationCoverQuality = Mathf.Clamp01(_investigationCoverQuality);
        _chaseActivationDistance = Mathf.Max(0.1f, _chaseActivationDistance);
        _chaseStopDistance = Mathf.Max(0f, _chaseStopDistance);
        _lostTargetPursuitSeconds = Mathf.Max(0.1f, _lostTargetPursuitSeconds);
        _movementCommitmentSeconds = Mathf.Max(0f, _movementCommitmentSeconds);
    }

    private void OnGUI()
    {
        if (!_showDebugPanel || _lastResult == null)
            return;

        EnsureStyles();
        float width = Mathf.Min(620f, Screen.width - 20f);
        GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, 390f), GUI.skin.box);
        GUILayout.Label("WARDEN LIVE EXPECTIMAX", _titleStyle);
        GUILayout.Label("Selected: " + _lastResult.SelectedAction, _selectedStyle);
        GUILayout.Label(
            $"Vision now:{(_vision != null && _vision.CanSeeTarget)}  Heard memory:{(_hearing != null && _hearing.HasHeardNoise)}  " +
            $"Ammo:{(_weapon != null ? _weapon.Ammo : 0)}/{(_weapon != null ? _weapon.MagazineSize : 0)}  " +
            $"Leaves:{_lastResult.EvaluatedLeafCount}",
            _bodyStyle);
        string playerHp = _playerHealth != null
            ? (_playerHealth.IsDead ? "0 (DEAD)" : Mathf.CeilToInt(_playerHealth.Current).ToString())
            : "unavailable";
        string wardenHp = _health != null
            ? (_health.IsDead ? "0 (DESTROYED)" : Mathf.CeilToInt(_health.Current).ToString())
            : "unavailable";
        GUILayout.Label($"Player HP:{playerHp}  Warden HP:{wardenHp}", _bodyStyle);
        if (_health != null && _health.IsDead)
            GUILayout.Label("[WARDEN DESTROYED - CEASED OPERATIONS]", _warningStyle);

        WardenResponseProbabilities probabilities = _lastResult.ResponseProbabilities;
        GUILayout.Label($"Peek {probabilities.Peek:P1}  Push {probabilities.Push:P1}  Hide {probabilities.Hide:P1}", _bodyStyle);

        var text = new StringBuilder();
        foreach (WardenActionScore score in _lastResult.CandidateScores)
        {
            text.AppendLine(score.IsValid
                ? $"{score.Action,-20} {score.Score,8:0.000}"
                : $"{score.Action,-20} INVALID - {score.InvalidReason}");
        }
        GUILayout.Label(text.ToString(), _bodyStyle);
        GUILayout.Label("Last learned event: " + _lastObservation, _warningStyle);
        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (_bodyStyle != null) return;
        _bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            wordWrap = true,
            normal = { textColor = new Color(0.9f, 0.95f, 1f) }
        };
        _titleStyle = new GUIStyle(_bodyStyle)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.cyan }
        };
        _selectedStyle = new GUIStyle(_titleStyle)
        {
            fontSize = 22,
            normal = { textColor = Color.green }
        };
        _warningStyle = new GUIStyle(_bodyStyle)
        {
            normal = { textColor = new Color(1f, 0.65f, 0.25f) }
        };
    }
}
