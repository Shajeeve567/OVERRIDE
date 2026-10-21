using System;
using UnityEngine;

/// <summary>
/// Periodically checks whether a configured target is inside a three-dimensional
/// field of view and unobstructed by the configured obstacle layers.
/// </summary>
[DisallowMultipleComponent]
public sealed class VisionSensor : MonoBehaviour
{
    private const float DirectionEpsilon = 0.0001f;

    [Header("References")]
    [SerializeField] private Transform _eye;
    [SerializeField] private Transform _target;

    [Header("Vision")]
    [SerializeField, Min(0f)] private float _range = 25f;
    [SerializeField, Range(0f, 360f)] private float _fieldOfView = 120f;
    [SerializeField] private LayerMask _obstacleLayers;
    [SerializeField, Min(0f)] private float _checkInterval = 0.2f;

    [Header("Diagnostics")]
    [SerializeField] private bool _logObstructions;

    private Transform _cachedTransform;
    private float _nextCheckTime;
    private Collider _lastLoggedObstruction;

    public Transform Eye
    {
        get => _eye;
        set => _eye = value;
    }

    public Transform Target
    {
        get => _target;
        set
        {
            _target = value;
            if (!IsTargetAvailable()) CanSeeTarget = false;
        }
    }

    public float Range
    {
        get => _range;
        set => _range = Mathf.Max(0f, value);
    }

    public float FieldOfView
    {
        get => _fieldOfView;
        set => _fieldOfView = Mathf.Clamp(value, 0f, 360f);
    }

    public LayerMask ObstacleLayers
    {
        get => _obstacleLayers;
        set => _obstacleLayers = value;
    }

    public float CheckInterval
    {
        get => _checkInterval;
        set => _checkInterval = Mathf.Max(0f, value);
    }

    public bool LogObstructions
    {
        get => _logObstructions;
        set => _logObstructions = value;
    }

    public bool CanSeeTarget { get; private set; }
    public bool HasSeenTarget { get; private set; }
    public Vector3 LastSeenPosition { get; private set; }
    public float LastSeenTime { get; private set; } = float.NegativeInfinity;
    public Collider LastObstructionCollider { get; private set; }
    public string LastObstructionName => LastObstructionCollider != null
        ? LastObstructionCollider.name
        : string.Empty;

    private Transform SensorEye => _eye != null ? _eye : _cachedTransform;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        _nextCheckTime = Time.time;
    }

    private void OnDisable()
    {
        CanSeeTarget = false;
    }

    private void Update()
    {
        if (Time.time < _nextCheckTime) return;

        CheckVisibilityNow();
        _nextCheckTime = Time.time + _checkInterval;
    }

    /// <summary>
    /// Immediately evaluates current visibility and updates memory only after confirmed sight.
    /// </summary>
    public bool CheckVisibilityNow()
    {
        CacheReferences();
        CanSeeTarget = EvaluateVisibility();

        if (CanSeeTarget)
        {
            HasSeenTarget = true;
            LastSeenPosition = _target.position;
            LastSeenTime = Time.time;
        }

        return CanSeeTarget;
    }

    private bool EvaluateVisibility()
    {
        SetObstruction(null);
        if (!IsTargetAvailable()) return false;

        Transform eye = SensorEye;
        if (eye == null || !eye.gameObject.activeInHierarchy) return false;

        Vector3 toTarget = _target.position - eye.position;
        float distanceSquared = toTarget.sqrMagnitude;
        float rangeSquared = _range * _range;
        if (distanceSquared > rangeSquared) return false;

        float distance = Mathf.Sqrt(distanceSquared);
        if (distance > DirectionEpsilon)
        {
            Vector3 direction = toTarget / distance;
            float halfFieldOfView = _fieldOfView * 0.5f;
            float minimumDot = Mathf.Cos(halfFieldOfView * Mathf.Deg2Rad);
            if (Vector3.Dot(eye.forward, direction) < minimumDot) return false;

            RaycastHit[] hits = Physics.RaycastAll(
                eye.position,
                direction,
                distance,
                _obstacleLayers,
                QueryTriggerInteraction.Ignore);
            if (hits != null && hits.Length > 0)
            {
                Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));
                for (int i = 0; i < hits.Length; i++)
                {
                    RaycastHit hit = hits[i];
                    if (hit.collider == null || IsSelfTransform(hit.transform))
                        continue;
                    if (IsTargetTransform(hit.transform))
                        return true;

                    SetObstruction(hit.collider);
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsTargetAvailable()
    {
        return _target != null && _target.gameObject.activeInHierarchy;
    }

    private bool IsTargetTransform(Transform candidate)
    {
        return candidate != null && (candidate == _target || candidate.IsChildOf(_target));
    }

    private bool IsSelfTransform(Transform candidate)
    {
        Transform owner = _cachedTransform != null ? _cachedTransform : transform;
        return candidate != null && (candidate == owner || candidate.IsChildOf(owner));
    }

    private void SetObstruction(Collider obstruction)
    {
        LastObstructionCollider = obstruction;
        if (!_logObstructions || obstruction == null || obstruction == _lastLoggedObstruction)
            return;

        _lastLoggedObstruction = obstruction;
        Debug.Log(
            $"[VisionSensor:{name}] Visibility blocked by '{obstruction.name}' " +
            $"on layer {LayerMask.LayerToName(obstruction.gameObject.layer)}.",
            obstruction);
    }

    private void CacheReferences()
    {
        if (_cachedTransform == null) _cachedTransform = transform;
    }

    private void OnValidate()
    {
        _range = Mathf.Max(0f, _range);
        _fieldOfView = Mathf.Clamp(_fieldOfView, 0f, 360f);
        _checkInterval = Mathf.Max(0f, _checkInterval);
    }

    private void OnDrawGizmosSelected()
    {
        Transform eye = _eye != null ? _eye : transform;
        Vector3 origin = eye.position;
        Vector3 forward = eye.forward.normalized;
        float halfAngle = _fieldOfView * 0.5f * Mathf.Deg2Rad;
        float coneLength = Mathf.Max(0f, _range);

        Gizmos.color = new Color(0.2f, 0.65f, 1f, 0.8f);
        Gizmos.DrawWireSphere(origin, coneLength);
        Gizmos.DrawLine(origin, origin + forward * coneLength);
        DrawViewCone(origin, forward, eye.right, eye.up, coneLength, halfAngle);

        if (IsTargetAvailable())
        {
            Gizmos.color = CanSeeTarget ? Color.green : Color.red;
            Gizmos.DrawLine(origin, _target.position);
            Gizmos.DrawWireSphere(_target.position, 0.15f);
        }

        if (HasSeenTarget)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, LastSeenPosition);
            Gizmos.DrawWireSphere(LastSeenPosition, 0.2f);
        }
    }

    private static void DrawViewCone(
        Vector3 origin,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        float length,
        float halfAngle)
    {
        const int segments = 24;
        float ringRadius = Mathf.Sin(halfAngle) * length;
        Vector3 ringCenter = origin + forward * (Mathf.Cos(halfAngle) * length);
        Vector3 previous = ringCenter + right * ringRadius;

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            Vector3 current = ringCenter + radial * ringRadius;
            Gizmos.DrawLine(previous, current);
            if (i % 6 == 0) Gizmos.DrawLine(origin, current);
            previous = current;
        }
    }
}
