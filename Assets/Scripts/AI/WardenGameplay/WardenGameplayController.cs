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
    private const float MovementProgressDistance = 0.1f;
    private const float MovementStuckSeconds = 1.5f;
    private const float FailedDestinationCooldown = 3f;

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
    private CharacterController _characterController;
    private AgentMotor _agentMotor;
    private WardenCoverPoint _movementTarget;
    private WardenCoverPoint _guardCover;
    private WardenCoverPoint _plannedAdvanceCover;
    private WardenCoverPoint _failedCover;
    private Collider _lastMovementBlocker;
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
    private WardenAction _movementAction;
    private WardenAction _executedAction;
    private bool _hasExecutedAction;
    private Vector3 _plannedAdvanceDestination;
    private bool _hasPlannedAdvanceDestination;
    private Vector3 _movementProgressPosition;
    private float _lastMovementProgressTime;
    private float _failedCoverUntil;
    private string _executionConstraint = string.Empty;
    private GUIStyle _bodyStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _selectedStyle;
    private GUIStyle _warningStyle;

    public WardenDecisionResult LastResult => _lastResult;
    public AgentCommand CurrentCommand => _command;
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
    public WardenAction ExecutedAction => _executedAction;
    public bool HasExecutedAction => _hasExecutedAction;
    public string ExecutionConstraint => _executionConstraint;
    public string MovementIntentName => _movementIntent.ToString();
    public Vector3? CurrentDestination => _command.Path != null && _command.Path.Length > 0
        ? _command.Path[_command.Path.Length - 1]
        : (Vector3?)null;
    public Vector3 ActualVelocity => _agentMotor != null ? _agentMotor.Velocity : Vector3.zero;
    public string MovementBlockerName => _lastMovementBlocker != null
        ? _lastMovementBlocker.name
        : string.Empty;

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

    /// <summary>
    /// Faces confirmed threat memory while stationary. AgentMotor exclusively owns root rotation
    /// during movement, preventing two systems from fighting over the same transform each frame.
    /// </summary>
    public void UpdateFacing(float deltaTime)
    {
        if (!_hasKnownPosition || HasActiveMovement || _aimRotationSpeed <= 0f ||
            (_health != null && _health.IsDead))
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
        UpdateMovementProgress();
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
        bool hasActionableKnowledge = HasActionableKnowledge(visible);
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

        bool requireReveal = !visible && HasRecentVisualMemory();
        WardenTacticalOption advanceOption = BuildAdvanceOption(
            visible,
            hasActionableKnowledge,
            optionReference,
            requireReveal);
        WardenTacticalOption flankOption = BuildCoverOption(
            _flankCover,
            optionReference,
            requireReveal);
        WardenTacticalOption retreatOption = BuildCoverOption(
            _retreatCover,
            optionReference,
            false);

        return new WardenDecisionState
        {
            IsOperational = operational,
            CanHoldPosition = operational,
            PlayerVisible = visible,
            HasKnownPlayerPosition = hasActionableKnowledge,
            DistanceToPlayer = Mathf.Max(0f, distance),
            Ammo = Mathf.Clamp(ammo, 0, maximumAmmo),
            MaxAmmo = maximumAmmo,
            CurrentCoverUsable = currentCoverUsable,
            CurrentCoverQuality = currentCoverUsable ? _currentCover.Quality : 0f,
            Desperation = CalculateDesperation(),
            AdvanceOption = advanceOption,
            FlankOption = flankOption,
            RetreatOption = retreatOption
        };
    }

    private void DecideAndApply()
    {
        EnsureBrain();
        _lastResult = _brain.Decide(BuildDecisionStateNow());
        _nextDecisionTime = Time.time + _decisionInterval;
        ApplyDecision(_lastResult.SelectedAction);
    }

    private WardenTacticalOption BuildAdvanceOption(
        bool visible,
        bool hasActionableKnowledge,
        Vector3 knownPlayerPosition,
        bool requireReveal)
    {
        _plannedAdvanceCover = null;
        _hasPlannedAdvanceDestination = false;

        WardenTacticalOption audibleAdvance = BuildAudibleAdvanceOption();
        if (audibleAdvance != null)
        {
            _plannedAdvanceDestination = _investigationDestination;
            _hasPlannedAdvanceDestination = true;
            return audibleAdvance;
        }

        WardenTacticalOption authoredAdvance = BuildCoverOption(
            _advanceCover,
            knownPlayerPosition,
            requireReveal);
        if (authoredAdvance != null)
        {
            _plannedAdvanceCover = _advanceCover;
            return authoredAdvance;
        }

        if (!hasActionableKnowledge)
            return null;

        float standOff = visible ? _chaseStopDistance : 0f;
        if (visible && PlanarDistance(transform.position, knownPlayerPosition) > _chaseActivationDistance)
            return null;

        Vector3 destination = BuildLeashedStandOffDestination(knownPlayerPosition, standOff);
        if (PlanarDistance(destination, transform.position) <= _coverArrivalDistance)
            return null;

        _plannedAdvanceDestination = destination;
        _hasPlannedAdvanceDestination = true;
        return BuildPositionOption(destination, knownPlayerPosition, _investigationCoverQuality);
    }

    private WardenTacticalOption BuildCoverOption(
        WardenCoverPoint cover,
        Vector3 knownPlayerPosition,
        bool requireReveal)
    {
        if (cover == null || cover == _currentCover)
            return null;

        WardenTacticalOption option = cover.BuildOption(gameObject, knownPlayerPosition);
        bool failedRecently = cover == _failedCover && Time.time < _failedCoverUntil;
        bool insideTerritory = PlanarDistance(cover.Position, _guardAnchor) <= _maximumRoamDistance;
        bool revealsMemory = !requireReveal || HasClearSearchLine(cover.Position);
        option.PathAvailable = option.PathAvailable &&
                               !failedRecently &&
                               insideTerritory &&
                               revealsMemory &&
                               IsDestinationReachable(cover.Position, out _);
        return option;
    }

    private WardenTacticalOption BuildPositionOption(
        Vector3 destination,
        Vector3 knownPlayerPosition,
        float coverQuality)
    {
        return new WardenTacticalOption
        {
            PathAvailable = IsDestinationReachable(destination, out _),
            CoverAvailable = true,
            CoverOccupied = false,
            CoverQuality = Mathf.Clamp01(coverQuality),
            ResultingDistanceToPlayer = PlanarDistance(destination, knownPlayerPosition)
        };
    }

    private bool HasActionableKnowledge(bool visible)
    {
        if (visible)
            return true;
        if (!_hasKnownPosition)
            return false;

        float age = Time.time - _lastKnownTime;
        return age >= 0f && age <= _lostTargetPursuitSeconds;
    }

    private bool HasRecentVisualMemory()
    {
        if (_vision == null || !_vision.HasSeenTarget)
            return false;

        float age = Time.time - _vision.LastSeenTime;
        return age >= 0f && age <= _lostTargetPursuitSeconds &&
               _vision.LastSeenTime >= _lastKnownTime - 0.01f;
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

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            toTarget / distance,
            distance,
            _vision.ObstacleLayers,
            QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return true;

        System.Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || IsOwnCollider(collider.transform))
                continue;
            return false;
        }

        return true;
    }

    private bool IsDestinationReachable(Vector3 destination, out Collider blocker)
    {
        blocker = null;
        if (_characterController == null)
            return true;

        Vector3 travel = destination - transform.position;
        travel.y = 0f;
        float distance = travel.magnitude;
        float castDistance = distance - _coverArrivalDistance;
        if (castDistance <= 0f)
            return true;

        Vector3 scale = transform.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float radius = Mathf.Max(0.05f, _characterController.radius * radiusScale * 0.9f);
        float height = Mathf.Max(
            radius * 2f,
            _characterController.height * Mathf.Abs(scale.y));
        Vector3 center = transform.TransformPoint(_characterController.center);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 upper = center + Vector3.up * halfSegment;
        Vector3 lower = center - Vector3.up * halfSegment;
        int mask = _vision != null ? _vision.ObstacleLayers.value : ~0;
        Vector3 destinationOffset = destination - transform.position;
        Collider[] destinationHits = Physics.OverlapCapsule(
            upper + destinationOffset,
            lower + destinationOffset,
            radius,
            mask,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < destinationHits.Length; i++)
        {
            Collider candidate = destinationHits[i];
            if (candidate == null || IsOwnCollider(candidate.transform))
                continue;
            if (candidate.bounds.max.y <= destination.y + 0.15f)
                continue;

            blocker = candidate;
            _lastMovementBlocker = candidate;
            return false;
        }

        RaycastHit[] hits = Physics.CapsuleCastAll(
            upper,
            lower,
            radius,
            travel / distance,
            castDistance,
            mask,
            QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            _lastMovementBlocker = null;
            return true;
        }

        System.Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null || IsOwnCollider(candidate.transform))
                continue;

            blocker = candidate;
            _lastMovementBlocker = candidate;
            return false;
        }

        _lastMovementBlocker = null;
        return true;
    }

    private bool IsOwnCollider(Transform candidate)
    {
        return candidate != null && (candidate == transform || candidate.IsChildOf(transform));
    }

    private void ApplyDecision(WardenAction action)
    {
        _command.Fire = false;
        _command.LookTarget = _hasKnownPosition ? _lastKnownPosition : transform.position + transform.forward;
        _command.AnimationIntent = action.ToString();
        SetExecutedAction(action, string.Empty);

        if (_health != null && _health.IsDead)
        {
            StopMovement();
            _weapon?.CancelActions();
            _executionConstraint = "Destroyed: no action executed.";
            return;
        }

        if (_weapon != null && _weapon.Ammo < _weapon.RequiredAmmoForBurst)
        {
            if (action == WardenAction.RetreatAndReload)
                MoveToCover(_retreatCover, action);
            else if (!HasActiveMovement)
                StopMovement();
            _command.AnimationIntent = "Reload";
            if (!_weapon.IsReloading && !_weapon.IsFiring)
                _weapon.StartReload();
            _executionConstraint = "Safety reload: fewer rounds remain than one complete burst.";
            return;
        }

        if (PreserveActiveMovement(action))
            return;

        switch (action)
        {
            case WardenAction.PeekAndFire:
                StopMovement();
                TryFireAtVisiblePlayer();
                break;

            case WardenAction.AdvanceToCover:
                if (_plannedAdvanceCover != null)
                    MoveToCover(_plannedAdvanceCover, action);
                else if (_hasPlannedAdvanceDestination)
                    MoveToPosition(
                        _plannedAdvanceDestination,
                        _hasInvestigationDestination ? MovementIntent.Investigation : MovementIntent.Pursuit,
                        action);
                else
                    RejectExecution(action, "The planned advance destination became unavailable.");
                break;

            case WardenAction.Flank:
                MoveToCover(_flankCover, action);
                break;

            case WardenAction.RetreatAndReload:
                MoveToCover(_retreatCover, action);
                _weapon?.StartReload();
                break;

            default:
                if (PlanarDistance(transform.position, _guardAnchor) > _coverArrivalDistance &&
                    IsDestinationReachable(_guardAnchor, out _))
                {
                    MoveToPosition(_guardAnchor, MovementIntent.ReturnToGuard, action);
                    _executionConstraint = "Hold executes as a return to the authored guard post.";
                }
                else
                {
                    StopMovement();
                }
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

    private void MoveToCover(WardenCoverPoint cover, WardenAction action)
    {
        Collider blocker = null;
        if (cover == null || !cover.CanUse(gameObject) ||
            !IsDestinationReachable(cover.Position, out blocker))
        {
            RejectExecution(
                action,
                blocker != null
                    ? "Movement blocked by " + blocker.name + "."
                    : "The selected cover is unavailable.");
            return;
        }

        if (_movementTarget == cover && HasActiveMovement)
            return;

        _movementTarget = cover;
        _command.Path = new[] { cover.Position };
        SetMovementIntent(MovementIntent.TacticalCover, action);
    }

    private void MoveToPosition(Vector3 position, MovementIntent intent, WardenAction action)
    {
        if (!IsDestinationReachable(position, out Collider blocker))
        {
            RejectExecution(
                action,
                blocker != null
                    ? "Movement blocked by " + blocker.name + "."
                    : "The selected destination is unreachable.");
            return;
        }

        if (_movementTarget == null && HasActiveMovement &&
            PlanarDistance(_command.Path[_command.Path.Length - 1], position) < 0.05f)
        {
            return;
        }

        _movementTarget = null;
        _command.Path = new[] { position };
        SetMovementIntent(intent, action);
    }

    private void SetMovementIntent(MovementIntent intent, WardenAction action)
    {
        _movementIntent = intent;
        _movementAction = action;
        _movementCommitUntil = Time.time + _movementCommitmentSeconds;
        _movementProgressPosition = transform.position;
        _lastMovementProgressTime = Time.time;
    }

    private bool PreserveActiveMovement(WardenAction selectedAction)
    {
        if (!HasActiveMovement || _movementAction == selectedAction)
            return false;

        SetExecutedAction(
            _movementAction,
            "Finishing committed " + _movementAction +
            " movement before switching to " + selectedAction + ".");
        _command.AnimationIntent = _movementAction.ToString();
        return true;
    }

    private bool HasActiveMovement =>
        _command.Path != null &&
        _command.Path.Length > 0;

    private void SetExecutedAction(WardenAction action, string constraint)
    {
        _executedAction = action;
        _hasExecutedAction = true;
        _executionConstraint = constraint ?? string.Empty;
    }

    private void RejectExecution(WardenAction action, string reason)
    {
        StopMovement();
        SetExecutedAction(action, reason);
    }

    private void TryFireAtVisiblePlayer()
    {
        if (_vision != null && _vision.CanSeeTarget && _playerTarget != null && _weapon != null)
            _weapon.RequestBurst(GetPlayerAimPoint());
    }

    private void StopMovement()
    {
        _movementTarget = null;
        _command.Path = null;
        _movementIntent = MovementIntent.None;
        _movementCommitUntil = 0f;
    }

    private void UpdateMovementProgress()
    {
        if (!HasActiveMovement)
            return;

        if (PlanarDistance(transform.position, _movementProgressPosition) >= MovementProgressDistance)
        {
            _movementProgressPosition = transform.position;
            _lastMovementProgressTime = Time.time;
            return;
        }

        if (Time.time < _movementCommitUntil ||
            Time.time - _lastMovementProgressTime < MovementStuckSeconds)
        {
            return;
        }

        WardenCoverPoint failed = _movementTarget;
        if (failed != null)
        {
            _failedCover = failed;
            _failedCoverUntil = Time.time + FailedDestinationCooldown;
        }

        string destination = CurrentDestination.HasValue
            ? CurrentDestination.Value.ToString("F2")
            : "unknown";
        StopMovement();
        _executionConstraint = "Movement aborted after no progress toward " + destination + ".";
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
            PathAvailable = IsDestinationReachable(destination, out _),
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
        if (_characterController == null) _characterController = GetComponent<CharacterController>();
        if (_agentMotor == null) _agentMotor = GetComponent<AgentMotor>();
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
        GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, 470f), GUI.skin.box);
        GUILayout.Label("WARDEN LIVE EXPECTIMAX", _titleStyle);
        GUILayout.Label("Selected: " + _lastResult.SelectedAction, _selectedStyle);
        GUILayout.Label(
            "Executed: " + (_hasExecutedAction ? _executedAction.ToString() : "None") +
            (string.IsNullOrEmpty(_executionConstraint) ? string.Empty : " | " + _executionConstraint),
            _warningStyle);
        GUILayout.Label(
            $"Vision now:{(_vision != null && _vision.CanSeeTarget)}  Heard memory:{(_hearing != null && _hearing.HasHeardNoise)}  " +
            $"Ammo:{(_weapon != null ? _weapon.Ammo : 0)}/{(_weapon != null ? _weapon.MagazineSize : 0)}  " +
            $"Leaves:{_lastResult.EvaluatedLeafCount}",
            _bodyStyle);
        string destination = CurrentDestination.HasValue
            ? CurrentDestination.Value.ToString("F2")
            : "none";
        GUILayout.Label(
            $"Movement:{_movementIntent}  Destination:{destination}  Velocity:{ActualVelocity.magnitude:0.00} m/s",
            _bodyStyle);
        GUILayout.Label(
            $"Vision blocker:{(_vision != null ? _vision.LastObstructionName : string.Empty)}  " +
            $"Route blocker:{MovementBlockerName}  Last seen:{_lastKnownPosition:F2} at {_lastKnownTime:0.00}",
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
