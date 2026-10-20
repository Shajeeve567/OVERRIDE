using Override.WardenAI;
using UnityEngine;

/// <summary>
/// Minimal semantic cover data owned by the Warden integration. The level can
/// replace these markers with a shared cover service later without changing the brain.
/// </summary>
[DisallowMultipleComponent]
public sealed class WardenCoverPoint : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float _quality = 0.75f;
    [SerializeField] private bool _coverAvailable = true;
    [SerializeField] private bool _pathAvailable = true;

    private GameObject _occupant;

    public Vector3 Position => transform.position;
    public float Quality => Mathf.Clamp01(_quality);
    public bool CoverAvailable => _coverAvailable && isActiveAndEnabled;
    public bool PathAvailable => _pathAvailable && isActiveAndEnabled;
    public bool IsOccupied => _occupant != null;
    public GameObject Occupant => _occupant;

    public bool CanUse(GameObject claimant)
    {
        return CoverAvailable && PathAvailable && (_occupant == null || _occupant == claimant);
    }

    public bool TryOccupy(GameObject claimant)
    {
        if (claimant == null || !CanUse(claimant))
            return false;

        _occupant = claimant;
        return true;
    }

    public void Release(GameObject claimant)
    {
        if (_occupant == claimant)
            _occupant = null;
    }

    public WardenTacticalOption BuildOption(GameObject claimant, Vector3 knownPlayerPosition)
    {
        return new WardenTacticalOption
        {
            PathAvailable = PathAvailable,
            CoverAvailable = CoverAvailable,
            CoverOccupied = _occupant != null && _occupant != claimant,
            CoverQuality = Quality,
            ResultingDistanceToPlayer = Vector3.Distance(Position, knownPlayerPosition)
        };
    }

#if UNITY_EDITOR
    public void Configure(float quality, bool coverAvailable = true, bool pathAvailable = true)
    {
        _quality = Mathf.Clamp01(quality);
        _coverAvailable = coverAvailable;
        _pathAvailable = pathAvailable;
    }
#endif

    private void OnDisable()
    {
        _occupant = null;
    }

    private void OnValidate()
    {
        _quality = Mathf.Clamp01(_quality);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = !_coverAvailable
            ? Color.red
            : _occupant != null
                ? Color.yellow
                : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.35f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward);
    }
}
