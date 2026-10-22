using System;
using System.Collections;
using Override.Core;
using UnityEngine;

/// <summary>
/// Warden-only hitscan burst weapon. It uses the team's IDamageable and NoiseBus
/// contracts without depending on the player-input rifle.
/// </summary>
[DisallowMultipleComponent]
public sealed class WardenWeapon : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _muzzle;

    [Header("Burst")]
    [SerializeField, Min(0f)] private float _damagePerShot = 8f;
    [SerializeField, Min(1)] private int _shotsPerBurst = 3;
    [SerializeField, Min(0.01f)] private float _burstCooldown = 1.2f;
    [SerializeField, Min(0f)] private float _intraBurstDelay = 0.1f;
    [SerializeField, Min(0.1f)] private float _range = 30f;
    [SerializeField] private LayerMask _hitLayers = ~0;

    [Header("Ammo")]
    [SerializeField, Min(1)] private int _magazineSize = 12;
    [SerializeField, Min(0.01f)] private float _reloadDuration = 2f;

    [Header("Noise")]
    [SerializeField, Min(0f)] private float _gunshotNoiseRadius = 30f;

    [Header("Test Feedback")]
    [SerializeField, Min(0.01f)] private float _tracerDuration = 0.12f;
    [SerializeField, Min(0.001f)] private float _tracerWidth = 0.025f;

    private int _ammo;
    private float _nextBurstTime;
    private float _reloadFinishTime;
    private Coroutine _burstRoutine;
    private LineRenderer _tracer;
    private Material _tracerMaterial;
    private float _hideTracerTime;
    private Transform _owner;

    public int Ammo => _ammo;
    public int MagazineSize => _magazineSize;
    public float Range => _range;
    public int RequiredAmmoForBurst => _shotsPerBurst;
    public bool IsReloading { get; private set; }
    public bool IsFiring => _burstRoutine != null;
    public Transform Muzzle => _muzzle;

    public event Action ShotFired;
    public event Action ReloadStarted;
    public event Action ReloadFinished;

    private Transform ShotOrigin => _muzzle != null ? _muzzle : transform;

    private void Awake()
    {
        CacheOwner();
        _ammo = _magazineSize;
    }

    private void Update()
    {
        if (IsReloading && Time.time >= _reloadFinishTime)
            FinishReload();
        if (_tracer != null && _tracer.enabled && Time.time >= _hideTracerTime)
            _tracer.enabled = false;
    }

    public bool RequestBurst(Vector3 aimPoint)
    {
        if (!isActiveAndEnabled || IsReloading || IsFiring || _ammo <= 0 || Time.time < _nextBurstTime)
            return false;

        Vector3 toTarget = aimPoint - ShotOrigin.position;
        if (toTarget.sqrMagnitude > _range * _range || toTarget.sqrMagnitude <= Mathf.Epsilon)
            return false;

        _nextBurstTime = Time.time + _burstCooldown;
        _burstRoutine = StartCoroutine(FireBurst(aimPoint));
        return true;
    }

    public bool StartReload()
    {
        if (!isActiveAndEnabled || IsReloading || IsFiring || _ammo >= _magazineSize)
            return false;

        IsReloading = true;
        _reloadFinishTime = Time.time + _reloadDuration;
        ReloadStarted?.Invoke();
        return true;
    }

    public void CancelActions()
    {
        if (_burstRoutine != null)
        {
            StopCoroutine(_burstRoutine);
            _burstRoutine = null;
        }

        IsReloading = false;
    }

    private IEnumerator FireBurst(Vector3 aimPoint)
    {
        int shotCount = Mathf.Min(_shotsPerBurst, _ammo);
        for (int i = 0; i < shotCount; i++)
        {
            FireShot(aimPoint);
            if (i + 1 < shotCount && _intraBurstDelay > 0f)
                yield return new WaitForSeconds(_intraBurstDelay);
        }

        _burstRoutine = null;
    }

    private void FireShot(Vector3 aimPoint)
    {
        if (_ammo <= 0)
            return;

        Transform origin = ShotOrigin;
        Vector3 direction = (aimPoint - origin.position).normalized;
        _ammo--;

        Vector3 tracerEnd = origin.position + direction * _range;
        RaycastHit[] hits = Physics.RaycastAll(
            origin.position,
            direction,
            _range,
            _hitLayers,
            QueryTriggerInteraction.Ignore);

        if (hits != null && hits.Length > 0)
        {
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit candidate = hits[i];
                if (candidate.collider != null && IsOwnerCollider(candidate.collider.transform))
                    continue;

                tracerEnd = candidate.point;
                IDamageable damageable = candidate.collider.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(_damagePerShot, candidate.point, candidate.normal);
                break;
            }
        }

        ShowTracer(origin.position, tracerEnd);
        NoiseBus.Emit(origin.position, _gunshotNoiseRadius, NoiseKind.Gunshot);
        ShotFired?.Invoke();
    }

    private void ShowTracer(Vector3 start, Vector3 end)
    {
        if (_tracer == null)
        {
            _tracer = gameObject.AddComponent<LineRenderer>();
            _tracer.useWorldSpace = true;
            _tracer.positionCount = 2;
            _tracer.startWidth = _tracerWidth;
            _tracer.endWidth = _tracerWidth * 0.5f;
            _tracer.startColor = Color.yellow;
            _tracer.endColor = Color.red;
            _tracer.numCapVertices = 2;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _tracerMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _tracerMaterial.color = Color.white;
                _tracer.sharedMaterial = _tracerMaterial;
            }
        }

        _tracer.SetPosition(0, start);
        _tracer.SetPosition(1, end);
        _tracer.enabled = true;
        _hideTracerTime = Time.time + _tracerDuration;
    }

    private void FinishReload()
    {
        IsReloading = false;
        _ammo = _magazineSize;
        ReloadFinished?.Invoke();
    }

    private void CacheOwner()
    {
        WardenGameplayController controller = GetComponentInParent<WardenGameplayController>();
        _owner = controller != null ? controller.transform : transform;
    }

    private bool IsOwnerCollider(Transform candidate)
    {
        if (candidate == null)
            return false;
        if (_owner == null)
            CacheOwner();
        return candidate == _owner || candidate.IsChildOf(_owner);
    }

#if UNITY_EDITOR
    public void Configure(Transform muzzle, LayerMask hitLayers)
    {
        _muzzle = muzzle;
        _hitLayers = hitLayers;
    }
#endif

    private void OnDisable()
    {
        CancelActions();
    }

    private void OnValidate()
    {
        _damagePerShot = Mathf.Max(0f, _damagePerShot);
        _shotsPerBurst = Mathf.Max(1, _shotsPerBurst);
        _burstCooldown = Mathf.Max(0.01f, _burstCooldown);
        _intraBurstDelay = Mathf.Max(0f, _intraBurstDelay);
        _range = Mathf.Max(0.1f, _range);
        _magazineSize = Mathf.Max(1, _magazineSize);
        _reloadDuration = Mathf.Max(0.01f, _reloadDuration);
        _gunshotNoiseRadius = Mathf.Max(0f, _gunshotNoiseRadius);
        _tracerDuration = Mathf.Max(0.01f, _tracerDuration);
        _tracerWidth = Mathf.Max(0.001f, _tracerWidth);
    }

    private void OnDrawGizmosSelected()
    {
        Transform origin = _muzzle != null ? _muzzle : transform;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin.position, origin.position + origin.forward * _range);
    }

    private void OnDestroy()
    {
        if (_tracerMaterial == null) return;
        if (Application.isPlaying) Destroy(_tracerMaterial);
        else DestroyImmediate(_tracerMaterial);
    }
}
