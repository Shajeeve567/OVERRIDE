using System.Text;
using Override.Director;
using Override.WardenAI;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Connects the standalone Warden brain and vision sensor to the live player in
/// the ARGUS greybox. This harness deliberately previews decisions without
/// moving or firing until the team-owned motor, cover and weapon APIs exist.
/// </summary>
[DisallowMultipleComponent]
public sealed class WardenArgusIntegrationRig : MonoBehaviour
{
    private const float PushObservationDistance = 1.5f;
    private const float ObservationCooldown = 1.2f;

    [Header("Live scene references")]
    [SerializeField] private VisionSensor visionSensor;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private DirectorController director;
    [SerializeField] private Renderer visibilityBeacon;

    [Header("Decision preview")]
    [SerializeField, Min(0.05f)] private float decisionInterval = 0.4f;
    [SerializeField, Min(1)] private int maximumAmmo = 12;
    [SerializeField] private bool currentCoverUsable = true;
    [SerializeField] private bool advanceOptionAvailable = true;
    [SerializeField] private bool flankOptionAvailable = true;
    [SerializeField] private bool retreatOptionAvailable = true;

    private WardenExpectimaxBrain brain;
    private WardenDecisionResult result;
    private MaterialPropertyBlock propertyBlock;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle selectedStyle;
    private GUIStyle warningStyle;

    private long observationSequence;
    private float nextDecisionTime;
    private float nextPushObservationTime;
    private float visibleDistanceBaseline;
    private bool visibilitySampled;
    private bool previousVisibility;
    private bool showPanel = true;
    private int ammo;
    private string lastObservation = "Waiting for a confirmed sight event.";

    public VisionSensor Sensor => visionSensor;
    public Transform PlayerTarget => playerTarget;
    public WardenDecisionResult LastResult => result;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        ResetDecisionPreview();
    }

    private void Update()
    {
        HandleControls();
        ObserveVisibleBehaviour();

        if (Time.unscaledTime >= nextDecisionTime)
            Evaluate();
    }

#if UNITY_EDITOR
    public void Configure(
        VisionSensor sensor,
        Transform target,
        DirectorController argusDirector,
        Renderer beacon)
    {
        visionSensor = sensor;
        playerTarget = target;
        director = argusDirector;
        visibilityBeacon = beacon;
    }
#endif

    private void HandleControls()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.f2Key.wasPressedThisFrame) showPanel = !showPanel;
        if (keyboard.pKey.wasPressedThisFrame) RecordObservedResponse(PlayerResponse.Peek, "Manual test event");
        if (keyboard.uKey.wasPressedThisFrame) RecordObservedResponse(PlayerResponse.Push, "Manual test event");
        if (keyboard.hKey.wasPressedThisFrame) RecordObservedResponse(PlayerResponse.Hide, "Manual test event");
        if (keyboard.mKey.wasPressedThisFrame)
        {
            ammo = ammo == 0 ? maximumAmmo : 0;
            lastObservation = ammo == 0 ? "Ammo emptied for edge-case testing." : "Ammo refilled.";
            Evaluate();
        }
        if (keyboard.cKey.wasPressedThisFrame) Toggle(ref currentCoverUsable, "Current cover");
        if (keyboard.gKey.wasPressedThisFrame) Toggle(ref advanceOptionAvailable, "Advance option");
        if (keyboard.fKey.wasPressedThisFrame) Toggle(ref flankOptionAvailable, "Flank option");
        if (keyboard.tKey.wasPressedThisFrame) Toggle(ref retreatOptionAvailable, "Retreat option");
        if (keyboard.rKey.wasPressedThisFrame) ResetDecisionPreview();
    }

    private void ObserveVisibleBehaviour()
    {
        if (visionSensor == null || playerTarget == null) return;

        bool visible = visionSensor.CanSeeTarget;
        float distance = VisibleDistance();

        if (!visibilitySampled)
        {
            visibilitySampled = true;
            previousVisibility = visible;
            if (visible) visibleDistanceBaseline = distance;
            UpdateBeacon(visible);
            return;
        }

        if (visible && !previousVisibility)
        {
            visibleDistanceBaseline = distance;
            RecordObservedResponse(PlayerResponse.Peek, "Target reacquired by VisionSensor");
        }
        else if (!visible && previousVisibility)
        {
            RecordObservedResponse(PlayerResponse.Hide, "VisionSensor lost confirmed sight");
        }
        else if (visible)
        {
            if (distance > visibleDistanceBaseline + 0.5f)
                visibleDistanceBaseline = distance;

            if (Time.unscaledTime >= nextPushObservationTime
                && visibleDistanceBaseline - distance >= PushObservationDistance)
            {
                visibleDistanceBaseline = distance;
                nextPushObservationTime = Time.unscaledTime + ObservationCooldown;
                RecordObservedResponse(PlayerResponse.Push, "Visible target approached the Warden");
            }
        }

        previousVisibility = visible;
        UpdateBeacon(visible);
    }

    private void RecordObservedResponse(PlayerResponse response, string source)
    {
        if (brain == null) brain = new WardenExpectimaxBrain();

        observationSequence++;
        bool accepted = brain.ResponseModel.TryRecordObservedResponse(
            response,
            observationSequence,
            true);
        lastObservation = accepted
            ? source + ": " + response + " recorded."
            : source + ": event rejected as duplicate or stale.";
        Evaluate();
    }

    private void Toggle(ref bool value, string label)
    {
        value = !value;
        lastObservation = label + " is now " + (value ? "available." : "unavailable.");
        Evaluate();
    }

    private void ResetDecisionPreview()
    {
        brain = new WardenExpectimaxBrain();
        observationSequence = 0;
        ammo = maximumAmmo;
        currentCoverUsable = true;
        advanceOptionAvailable = true;
        flankOptionAvailable = true;
        retreatOptionAvailable = true;
        visibilitySampled = false;
        previousVisibility = false;
        nextPushObservationTime = 0f;
        lastObservation = "Learning and simulated tactical inputs reset.";
        Evaluate();
    }

    private void Evaluate()
    {
        if (brain == null) brain = new WardenExpectimaxBrain();

        result = brain.Decide(BuildState());
        nextDecisionTime = Time.unscaledTime + decisionInterval;
        UpdateBeacon(visionSensor != null && visionSensor.CanSeeTarget);
    }

    private WardenDecisionState BuildState()
    {
        bool visible = visionSensor != null && visionSensor.CanSeeTarget;
        bool known = visionSensor != null && visionSensor.HasSeenTarget;
        float distance = visible
            ? VisibleDistance()
            : known
                ? Vector3.Distance(transform.position, visionSensor.LastSeenPosition)
                : visionSensor != null ? visionSensor.Range : 35f;

        return new WardenDecisionState
        {
            PlayerVisible = visible,
            HasKnownPlayerPosition = known,
            DistanceToPlayer = Mathf.Max(0f, distance),
            Ammo = Mathf.Clamp(ammo, 0, maximumAmmo),
            MaxAmmo = maximumAmmo,
            CurrentCoverUsable = currentCoverUsable,
            CurrentCoverQuality = currentCoverUsable ? 0.7f : 0f,
            Desperation = CalculateDesperation(),
            AdvanceOption = TacticalOption(advanceOptionAvailable, 0.8f, Mathf.Max(4f, distance - 4f)),
            FlankOption = TacticalOption(flankOptionAvailable, 0.75f, Mathf.Max(4f, distance)),
            RetreatOption = TacticalOption(retreatOptionAvailable, 0.9f, distance + 6f)
        };
    }

    private static WardenTacticalOption TacticalOption(bool available, float quality, float distance)
    {
        return new WardenTacticalOption
        {
            PathAvailable = available,
            CoverAvailable = available,
            CoverOccupied = false,
            CoverQuality = quality,
            ResultingDistanceToPlayer = distance
        };
    }

    private float VisibleDistance()
    {
        if (playerTarget == null) return visionSensor != null ? visionSensor.Range : 35f;
        Transform origin = visionSensor != null && visionSensor.Eye != null
            ? visionSensor.Eye
            : transform;
        return Vector3.Distance(origin.position, playerTarget.position);
    }

    private float CalculateDesperation()
    {
        if (director == null) return 0.25f;
        if (director.FinalWave) return 1f;

        int lostSubsystems = 0;
        if (!director.CamerasOnline) lostSubsystems++;
        if (!director.DoorControlOnline) lostSubsystems++;
        if (director.DroneBudgetNow < director.droneBudget) lostSubsystems++;
        return lostSubsystems / 3f;
    }

    private void UpdateBeacon(bool visible)
    {
        if (visibilityBeacon == null || propertyBlock == null) return;
        Color colour = visible ? new Color(0.1f, 1f, 0.25f) : new Color(1f, 0.12f, 0.08f);
        visibilityBeacon.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", colour);
        propertyBlock.SetColor("_Color", colour);
        visibilityBeacon.SetPropertyBlock(propertyBlock);
    }

    private void OnGUI()
    {
        if (!showPanel) return;
        EnsureStyles();

        float width = Mathf.Min(660f, Screen.width - 20f);
        float x = Mathf.Max(10f, Screen.width - width - 10f);
        GUILayout.BeginArea(new Rect(x, 10f, width, Screen.height - 20f), GUI.skin.box);
        GUILayout.Label("WARDEN + ARGUS INTEGRATION", titleStyle);
        GUILayout.Label("DECISION PREVIEW ONLY - movement and firing are intentionally disabled", warningStyle);

        bool visible = visionSensor != null && visionSensor.CanSeeTarget;
        bool seen = visionSensor != null && visionSensor.HasSeenTarget;
        string lastSeen = seen
            ? visionSensor.LastSeenPosition.ToString("F1") + " at " + visionSensor.LastSeenTime.ToString("0.0") + "s"
            : "never";
        GUILayout.Label(
            $"Vision  CanSee:{visible}  HasSeen:{seen}  Last seen: {lastSeen}",
            bodyStyle);

        if (result == null)
        {
            GUILayout.Label("Waiting for the first decision...", bodyStyle);
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label("Selected: " + result.SelectedAction, selectedStyle);
        GUILayout.Label(
            $"Depth:{result.DecisionTurns}  Leaves:{result.EvaluatedLeafCount}  " +
            $"Ammo:{ammo}/{maximumAmmo}  Desperation:{CalculateDesperation():0.00}",
            bodyStyle);

        WardenResponseProbabilities probabilities = result.ResponseProbabilities;
        GUILayout.Label(
            $"Learned response probabilities  Peek {probabilities.Peek:P1}  " +
            $"Push {probabilities.Push:P1}  Hide {probabilities.Hide:P1}",
            bodyStyle);

        GUILayout.Space(4f);
        GUILayout.Label("CANDIDATE SCORES", titleStyle);
        foreach (WardenActionScore candidate in result.CandidateScores)
        {
            string line = candidate.IsValid
                ? $"{candidate.Action,-20} {candidate.Score,9:0.000}"
                : $"{candidate.Action,-20} INVALID - {candidate.InvalidReason}";
            GUILayout.Label(line, candidate.IsValid ? bodyStyle : warningStyle);
        }

        GUILayout.Space(5f);
        GUILayout.Label("LIVE / SIMULATED INPUTS", titleStyle);
        GUILayout.Label(
            "Live: vision, remembered target, distance and ARGUS subsystem desperation.\n" +
            "Simulated: ammo, cover availability and paths. No hidden position is sent to the brain.",
            bodyStyle);
        GUILayout.Label(
            $"Cover current:{currentCoverUsable}  advance:{advanceOptionAvailable}  " +
            $"flank:{flankOptionAvailable}  retreat:{retreatOptionAvailable}",
            bodyStyle);

        GUILayout.Space(5f);
        GUILayout.Label("CONTROLS", titleStyle);
        GUILayout.Label(BuildControlText(), bodyStyle);
        GUILayout.Label("Last observation: " + lastObservation, bodyStyle);
        GUILayout.EndArea();
    }

    private static string BuildControlText()
    {
        var text = new StringBuilder();
        text.AppendLine("WASD + mouse: move/look with the existing player");
        text.AppendLine("F2: Warden panel   F3: ARGUS panel");
        text.AppendLine("P / U / H: manually record observed Peek / Push / Hide");
        text.AppendLine("M: ammo   C: current cover   G/F/T: advance/flank/retreat option");
        text.AppendLine("R: reset Warden learning and simulated tactical inputs");
        return text.ToString();
    }

    private void EnsureStyles()
    {
        if (bodyStyle != null) return;
        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            wordWrap = true,
            normal = { textColor = new Color(0.9f, 0.95f, 1f) }
        };
        titleStyle = new GUIStyle(bodyStyle)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.2f, 0.9f, 1f) }
        };
        selectedStyle = new GUIStyle(titleStyle)
        {
            fontSize = 22,
            normal = { textColor = new Color(0.3f, 1f, 0.45f) }
        };
        warningStyle = new GUIStyle(bodyStyle)
        {
            normal = { textColor = new Color(1f, 0.5f, 0.25f) }
        };
    }
}
