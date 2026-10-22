using UnityEngine;

/// <summary>
/// Provides cached attachment transforms exposed by a Warden visual prefab.
/// Gameplay systems can use these references without searching the hierarchy.
/// </summary>
[DisallowMultipleComponent]
public sealed class WardenVisualReferences : MonoBehaviour
{
    [SerializeField] private Transform _eye;
    [SerializeField] private Transform _muzzle;
    [SerializeField] private Transform _rearWeakPoint;

    public Transform Eye => _eye;
    public Transform Muzzle => _muzzle;
    public Transform RearWeakPoint => _rearWeakPoint;

#if UNITY_EDITOR
    /// <summary>
    /// Configures generated visual prefabs while keeping runtime references read-only.
    /// </summary>
    public void Configure(Transform eye, Transform muzzle, Transform rearWeakPoint)
    {
        _eye = eye;
        _muzzle = muzzle;
        _rearWeakPoint = rearWeakPoint;
    }
#endif
}
