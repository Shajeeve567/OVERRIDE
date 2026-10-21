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

    private void Awake()
    {
        EnsureBrain();
        _command.Path = null;
        CacheLocalReferences();
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
        CompleteCoverMovementIfArrived();

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
            AdvanceOption = BuildOption(_advanceCover, optionReference),
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

        if (_weapon != null &&
            action != WardenAction.RetreatAndReload &&
            _weapon.Ammo < _weapon.RequiredAmmoForBurst)
        {
            StopMovement();
            _command.AnimationIntent = "Reload";
            if (!_weapon.IsReloading && !_weapon.IsFiring)
                _weapon.StartReload();
            return;
        }

        switch (action)
        {
            case WardenAction.PeekAndFire:
                StopMovement();
                if (_vision != null && _vision.CanSeeTarget && _playerTarget != null && _weapon != null)
                {
                    _command.Fire = _weapon.RequestBurst(GetPlayerAimPoint());
                }
                break;

            case WardenAction.AdvanceToCover:
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
    }

    private void StopMovement()
    {
        _movementTarget = null;
        _command.Path = null;
    }

    private void CompleteCoverMovementIfArrived()
    {
        if (_movementTarget == null)
            return;

        Vector3 offset = _movementTarget.Position - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > _coverArrivalDistance * _coverArrivalDistance)
            return;

        if (_movementTarget.TryOccupy(gameObject))
        {
            if (_currentCover != _movementTarget)
                _currentCover?.Release(gameObject);
            _currentCover = _movementTarget;
        }

        StopMovement();
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
