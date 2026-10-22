using UnityEngine;

/// <summary>
/// Provides cached attachment transforms exposed by a Seeker visual prefab.
/// Gameplay systems can use these references without searching the hierarchy.
/// </summary>
[DisallowMultipleComponent]
public sealed class SeekerVisualReferences : MonoBehaviour
{
    [SerializeField] private Transform _sensorCore;
    [SerializeField] private Transform _sensorHousing;
    [SerializeField] private Transform _weakPoint;
    [SerializeField] private Transform _leftThruster;
    [SerializeField] private Transform _rightThruster;

    public Transform SensorCore => _sensorCore;
    public Transform SensorHousing => _sensorHousing;
    public Transform WeakPoint => _weakPoint;
    public Transform LeftThruster => _leftThruster;
    public Transform RightThruster => _rightThruster;

#if UNITY_EDITOR
    /// <summary>
    /// Configures generated visual prefabs while keeping runtime references read-only.
    /// </summary>
    public void Configure(
        Transform sensorCore,
        Transform sensorHousing,
        Transform weakPoint,
        Transform leftThruster,
        Transform rightThruster)
    {
        _sensorCore = sensorCore;
        _sensorHousing = sensorHousing;
        _weakPoint = weakPoint;
        _leftThruster = leftThruster;
        _rightThruster = rightThruster;
    }
#endif
}
