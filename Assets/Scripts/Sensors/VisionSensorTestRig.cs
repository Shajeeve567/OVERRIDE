using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Test-scene-only controls and diagnostics for manually exercising a VisionSensor.
/// </summary>
[DisallowMultipleComponent]
public sealed class VisionSensorTestRig : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VisionSensor _sensor;
    [SerializeField] private Transform _target;
    [SerializeField] private Renderer _targetRenderer;

    [Header("Target Controls")]
    [SerializeField, Min(0f)] private float _moveSpeed = 4f;
    [SerializeField, Min(1f)] private float _fastMultiplier = 2.5f;
    [SerializeField, Min(0f)] private float _minimumHeight = 1f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();

    private Vector3 _startPosition;
    private GUIStyle _panelStyle;
    private GUIStyle _headingStyle;
    private GUIStyle _labelStyle;

    private void Awake()
    {
        if (_target != null) _startPosition = _target.position;
        UpdateTargetColour();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || _target == null) return;

        Vector3 movement = Vector3.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement.z += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement.z -= 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement.x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement.x += 1f;
        if (keyboard.eKey.isPressed) movement.y += 1f;
        if (keyboard.qKey.isPressed) movement.y -= 1f;

        if (movement.sqrMagnitude > 0f)
        {
            float multiplier = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
                ? _fastMultiplier
                : 1f;
            Vector3 nextPosition = _target.position +
                                   movement.normalized * (_moveSpeed * multiplier * Time.deltaTime);
            nextPosition.y = Mathf.Max(_minimumHeight, nextPosition.y);
            _target.position = nextPosition;
        }

        if (keyboard.rKey.wasPressedThisFrame) _target.position = _startPosition;
        UpdateTargetColour();
    }

    private void UpdateTargetColour()
    {
        if (_targetRenderer == null || _sensor == null) return;

        Color colour = _sensor.CanSeeTarget
            ? new Color(0.15f, 1f, 0.25f, 1f)
            : new Color(1f, 0.25f, 0.1f, 1f);

        _targetRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(BaseColorId, colour);
        _propertyBlock.SetColor(ColorId, colour);
        _targetRenderer.SetPropertyBlock(_propertyBlock);
    }

    private void OnGUI()
    {
        EnsureStyles();
        GUILayout.BeginArea(new Rect(16f, 16f, 370f, 215f), _panelStyle);
        GUILayout.Label("WARDEN VISION TEST", _headingStyle);

        if (_sensor == null)
        {
            GUILayout.Label("VisionSensor reference is missing.", _labelStyle);
        }
        else
        {
            GUILayout.Label($"CanSeeTarget:  {_sensor.CanSeeTarget}", _labelStyle);
            GUILayout.Label($"HasSeenTarget: {_sensor.HasSeenTarget}", _labelStyle);
            GUILayout.Label($"LastSeenPosition: {_sensor.LastSeenPosition:F2}", _labelStyle);
            string lastSeen = float.IsNegativeInfinity(_sensor.LastSeenTime)
                ? "Never"
                : $"{_sensor.LastSeenTime:F2} s";
            GUILayout.Label($"LastSeenTime: {lastSeen}", _labelStyle);
        }

        GUILayout.Space(7f);
        GUILayout.Label("Move: WASD / Arrow keys   Height: Q / E", _labelStyle);
        GUILayout.Label("Fast: hold Shift   Reset target: R", _labelStyle);
        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (_panelStyle != null) return;

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(14, 14, 12, 12),
            alignment = TextAnchor.UpperLeft
        };
        _headingStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = Color.white }
        };
    }
}
