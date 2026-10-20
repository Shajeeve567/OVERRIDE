using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Visual-only helper for the standalone weapon showcase scene.
/// It has no dependency on the gameplay weapon system.
/// </summary>
public sealed class WeaponShowcaseSpin : MonoBehaviour
{
    [SerializeField, Min(0f)] private float degreesPerSecond = 25f;

    private Quaternion initialRotation;
    private bool paused;

    private void Awake()
    {
        initialRotation = transform.localRotation;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            paused = !paused;

        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            transform.localRotation = initialRotation;

        if (!paused)
            transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.World);
    }
}
