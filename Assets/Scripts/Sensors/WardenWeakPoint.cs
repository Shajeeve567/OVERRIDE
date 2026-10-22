using UnityEngine;

/// <summary>
/// Identifies a Warden collider that should amplify incoming damage.
/// The weapon/health system can query this component from a raycast hit.
/// </summary>
[DisallowMultipleComponent]
public sealed class WardenWeakPoint : MonoBehaviour
{
    public enum WeakPointType
    {
        SensorEye,
        RearVent
    }

    [SerializeField] private WeakPointType type;
    [SerializeField, Min(1f)] private float damageMultiplier = 2f;

    public WeakPointType Type => type;
    public float DamageMultiplier => damageMultiplier;

    /// <summary>
    /// Multiplies a non-negative damage value by this weak point's configured multiplier.
    /// </summary>
    public float ScaleDamage(float baseDamage)
    {
        return Mathf.Max(0f, baseDamage) * damageMultiplier;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Configures generated visual prefabs without exposing mutable runtime setters.
    /// </summary>
    public void Configure(WeakPointType weakPointType, float multiplier)
    {
        type = weakPointType;
        damageMultiplier = Mathf.Max(1f, multiplier);
    }

    private void OnValidate()
    {
        damageMultiplier = Mathf.Max(1f, damageMultiplier);
    }
#endif
}
