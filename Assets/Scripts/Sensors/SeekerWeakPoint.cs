using UnityEngine;

/// <summary>
/// Identifies a Seeker collider that should amplify incoming damage.
/// The weapon/health system can query this component from a raycast hit.
/// </summary>
[DisallowMultipleComponent]
public sealed class SeekerWeakPoint : MonoBehaviour
{
    [SerializeField, Min(1f)] private float damageMultiplier = 2f;

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
    public void Configure(float multiplier)
    {
        damageMultiplier = Mathf.Max(1f, multiplier);
    }

    private void OnValidate()
    {
        damageMultiplier = Mathf.Max(1f, damageMultiplier);
    }
#endif
}
