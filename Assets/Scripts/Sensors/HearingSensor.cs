using System;
using Override.Core;
using UnityEngine;

/// <summary>
/// Reusable listener for noises published through the shared NoiseBus.
/// Keeps only confirmed audible information and never searches the scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class HearingSensor : MonoBehaviour
{
    private const int RaycastBufferSize = 16;

    [Header("References")]
    [SerializeField] private Transform _listener;

    [Header("Hearing")]
    [SerializeField, Min(0f)] private float _hearingRange = 15f;
    [SerializeField] private int _noiseKindMask = -1;

    [Header("Obstruction")]
    [Tooltip("Layers that dampen a noise. Leave empty to disable obstruction checks.")]
    [SerializeField] private LayerMask _obstacleLayers;
    [SerializeField, Range(0f, 1f)] private float _obstructedRangeMultiplier = 0.5f;

    private readonly RaycastHit[] _raycastHits = new RaycastHit[RaycastBufferSize];
    private Transform _cachedTransform;

    public Transform Listener
    {
        get => _listener;
        set => _listener = value;
    }

    public float HearingRange
    {
        get => _hearingRange;
        set => _hearingRange = Mathf.Max(0f, value);
    }

    public int NoiseKindMask
    {
        get => _noiseKindMask;
        set => _noiseKindMask = value;
    }

    public LayerMask ObstacleLayers
    {
        get => _obstacleLayers;
        set => _obstacleLayers = value;
    }

    public float ObstructedRangeMultiplier
    {
        get => _obstructedRangeMultiplier;
        set => _obstructedRangeMultiplier = Mathf.Clamp01(value);
    }

    public bool HasHeardNoise { get; private set; }
    public Vector3 LastHeardPosition { get; private set; }
    public float LastHeardTime { get; private set; } = float.NegativeInfinity;
    public NoiseKind LastHeardKind { get; private set; }
    public bool LastNoiseWasObstructed { get; private set; }

    /// <summary>Raised only after a noise passes every hearing check.</summary>
    public event Action<NoiseEvent> NoiseHeard;

    private Transform SensorListener => _listener != null ? _listener : _cachedTransform;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        NoiseBus.Emitted -= OnNoiseEmitted;
        NoiseBus.Emitted += OnNoiseEmitted;
    }

    private void OnDisable()
    {
        NoiseBus.Emitted -= OnNoiseEmitted;
    }

    private void OnDestroy()
    {
        NoiseBus.Emitted -= OnNoiseEmitted;
    }

    private void OnNoiseEmitted(NoiseEvent noise)
    {
        ProcessNoise(noise);
    }

    /// <summary>
    /// Evaluates one noise immediately. This is public for explicit integrations and tests;
    /// ordinary emitters should publish through NoiseBus.
    /// </summary>
    public bool ProcessNoise(NoiseEvent noise)
    {
        if (!isActiveAndEnabled || !IsValid(noise) || !Accepts(noise.Kind))
            return false;

        CacheReferences();
        Transform listener = SensorListener;
        if (listener == null || !listener.gameObject.activeInHierarchy)
            return false;

        Vector3 listenerPosition = listener.position;
        Vector3 toNoise = noise.Position - listenerPosition;
        float distance = toNoise.magnitude;
        float audibleRange = Mathf.Min(_hearingRange, noise.Radius);
        bool obstructed = IsObstructed(listener, listenerPosition, toNoise, distance);
        if (obstructed)
            audibleRange *= _obstructedRangeMultiplier;

        if (distance > audibleRange)
            return false;

        HasHeardNoise = true;
        LastHeardPosition = noise.Position;
        LastHeardTime = noise.Time;
        LastHeardKind = noise.Kind;
        LastNoiseWasObstructed = obstructed;
        NoiseHeard?.Invoke(noise);
        return true;
    }

    public void ClearMemory()
    {
        HasHeardNoise = false;
        LastHeardPosition = default;
        LastHeardTime = float.NegativeInfinity;
        LastHeardKind = default;
        LastNoiseWasObstructed = false;
    }

    private bool IsValid(NoiseEvent noise)
    {
        if (!IsFinite(noise.Position) || !IsFinite(noise.Radius) || !IsFinite(noise.Time))
            return false;
        if (noise.Radius <= 0f)
            return false;
        return !HasHeardNoise || noise.Time >= LastHeardTime;
    }

    private bool Accepts(NoiseKind kind)
    {
        int value = (int)kind;
        return value >= 0 && value < 32 && (_noiseKindMask & (1 << value)) != 0;
    }

    private bool IsObstructed(
        Transform listener,
        Vector3 listenerPosition,
        Vector3 toNoise,
        float distance)
    {
        if (_obstacleLayers.value == 0 || distance <= Mathf.Epsilon)
            return false;

        int hitCount = Physics.RaycastNonAlloc(
            listenerPosition,
            toNoise / distance,
            _raycastHits,
            distance,
            _obstacleLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Transform hit = _raycastHits[i].transform;
            if (hit != null && hit != listener && !hit.IsChildOf(listener))
                return true;
        }

        return false;
    }

    private void CacheReferences()
    {
        if (_cachedTransform == null)
            _cachedTransform = transform;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private void OnValidate()
    {
        _hearingRange = Mathf.Max(0f, _hearingRange);
        _obstructedRangeMultiplier = Mathf.Clamp01(_obstructedRangeMultiplier);
    }

    private void OnDrawGizmosSelected()
    {
        Transform listener = _listener != null ? _listener : transform;
        Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(listener.position, Mathf.Max(0f, _hearingRange));

        if (!HasHeardNoise)
            return;

        Gizmos.color = LastNoiseWasObstructed ? Color.yellow : Color.cyan;
        Gizmos.DrawLine(listener.position, LastHeardPosition);
        Gizmos.DrawWireSphere(LastHeardPosition, 0.2f);
    }
}
