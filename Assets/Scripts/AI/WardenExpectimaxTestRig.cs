using System.Text;
using Override.WardenAI;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Standalone Play Mode harness for demonstrating the pure Warden Expectimax brain.
/// It deliberately simulates integration inputs and never moves or fires the Warden.
/// </summary>
public sealed class WardenExpectimaxTestRig : MonoBehaviour
{
    private const float DecisionInterval = 0.4f;

    [Header("Optional scene feedback")]
    [SerializeField] private Renderer playerTargetRenderer;
    [SerializeField] private Renderer currentCoverRenderer;
    [SerializeField] private Renderer advanceCoverRenderer;
    [SerializeField] private Renderer flankCoverRenderer;
    [SerializeField] private Renderer retreatCoverRenderer;

    private WardenExpectimaxBrain brain;
    private WardenDecisionResult result;
    private MaterialPropertyBlock propertyBlock;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle selectedStyle;
    private GUIStyle invalidStyle;

    private long observationSequence;
    private float nextDecisionTime;
    private bool playerVisible;
    private bool hasKnownPlayerPosition;
    private bool currentCoverUsable;
    private bool advanceAvailable;
    private bool flankOccupied;
    private bool retreatPathAvailable;
    private bool forceFallback;
    private int ammo;
    private float desperation;
    private string lastEvent;

    public WardenDecisionResult LastResult => result;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        ResetScenario();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) RecordResponse(PlayerResponse.Peek);
            if (keyboard.digit2Key.wasPressedThisFrame) RecordResponse(PlayerResponse.Push);
            if (keyboard.digit3Key.wasPressedThisFrame) RecordResponse(PlayerResponse.Hide);
            if (keyboard.vKey.wasPressedThisFrame) Toggle(ref playerVisible, "Player visibility");
            if (keyboard.kKey.wasPressedThisFrame) Toggle(ref hasKnownPlayerPosition, "Known player position");
            if (keyboard.mKey.wasPressedThisFrame)
            {
                ammo = ammo == 0 ? 12 : 0;
                StateChanged("Ammo " + (ammo == 0 ? "emptied" : "refilled"));
            }
            if (keyboard.cKey.wasPressedThisFrame) Toggle(ref currentCoverUsable, "Current cover");
            if (keyboard.gKey.wasPressedThisFrame) Toggle(ref advanceAvailable, "Advance cover");
            if (keyboard.fKey.wasPressedThisFrame) Toggle(ref flankOccupied, "Flank cover occupied");
            if (keyboard.tKey.wasPressedThisFrame) Toggle(ref retreatPathAvailable, "Retreat path");
            if (keyboard.dKey.wasPressedThisFrame)
            {
                desperation = desperation < 0.5f ? 1f : 0.25f;
                StateChanged("Desperation set to " + desperation.ToString("0.00"));
            }
            if (keyboard.bKey.wasPressedThisFrame) Toggle(ref forceFallback, "Forced fallback scenario");
            if (keyboard.rKey.wasPressedThisFrame) ResetScenario();
        }

        if (Time.unscaledTime >= nextDecisionTime)
            Evaluate();
    }

    private void ResetScenario()
    {
        brain = new WardenExpectimaxBrain();
        observationSequence = 0;
        playerVisible = true;
        hasKnownPlayerPosition = true;
        currentCoverUsable = true;
        advanceAvailable = true;
        flankOccupied = false;
        retreatPathAvailable = true;
        forceFallback = false;
        ammo = 12;
        desperation = 0.25f;
        lastEvent = "Scenario and learned probabilities reset.";
        Evaluate();
    }

    private void RecordResponse(PlayerResponse response)
    {
        observationSequence++;
        bool accepted = brain.ResponseModel.TryRecordObservedResponse(
            response,
            observationSequence,
            true);
        lastEvent = accepted
            ? "Direct observation recorded: player " + response + "."
            : "Observation rejected as duplicate, stale or hidden.";
        Evaluate();
    }

    private void Toggle(ref bool value, string label)
    {
        value = !value;
        StateChanged(label + ": " + (value ? "ON" : "OFF"));
    }

    private void StateChanged(string message)
    {
        lastEvent = message;
        Evaluate();
    }

    private void Evaluate()
    {
        if (brain == null)
            brain = new WardenExpectimaxBrain();

        result = brain.Decide(BuildState());
        nextDecisionTime = Time.unscaledTime + DecisionInterval;
        UpdateSceneColours();
    }

    private WardenDecisionState BuildState()
    {
        if (forceFallback)
        {
            return new WardenDecisionState
            {
                CanHoldPosition = false,
                PlayerVisible = false,
                HasKnownPlayerPosition = false,
                DistanceToPlayer = 18f,
                Ammo = 0,
                MaxAmmo = 12,
                CurrentCoverUsable = false,
                CurrentCoverQuality = 0f,
                Desperation = desperation
            };
        }

        return new WardenDecisionState
        {
            PlayerVisible = playerVisible,
            HasKnownPlayerPosition = hasKnownPlayerPosition,
            DistanceToPlayer = 18f,
            Ammo = ammo,
            MaxAmmo = 12,
            CurrentCoverUsable = currentCoverUsable,
            CurrentCoverQuality = currentCoverUsable ? 0.7f : 0f,
            Desperation = desperation,
            AdvanceOption = new WardenTacticalOption
            {
                PathAvailable = true,
                CoverAvailable = advanceAvailable,
                CoverOccupied = false,
                CoverQuality = 0.8f,
                ResultingDistanceToPlayer = 14f
            },
            FlankOption = new WardenTacticalOption
            {
                PathAvailable = true,
                CoverAvailable = true,
                CoverOccupied = flankOccupied,
                CoverQuality = 0.75f,
                ResultingDistanceToPlayer = 18f
            },
            RetreatOption = new WardenTacticalOption
            {
                PathAvailable = retreatPathAvailable,
                CoverAvailable = true,
                CoverOccupied = false,
                CoverQuality = 0.9f,
                ResultingDistanceToPlayer = 24f
            }
        };
    }

    private void UpdateSceneColours()
    {
        SetColour(playerTargetRenderer,
            forceFallback || !playerVisible ? new Color(0.65f, 0.12f, 0.12f) : new Color(0.1f, 0.8f, 0.35f));
        SetColour(currentCoverRenderer,
            !forceFallback && currentCoverUsable ? new Color(0.1f, 0.55f, 0.9f) : new Color(0.45f, 0.12f, 0.12f));
        SetColour(advanceCoverRenderer,
            !forceFallback && advanceAvailable ? new Color(0.1f, 0.55f, 0.9f) : new Color(0.45f, 0.12f, 0.12f));
        SetColour(flankCoverRenderer,
            !forceFallback && !flankOccupied ? new Color(0.1f, 0.55f, 0.9f) : new Color(0.9f, 0.45f, 0.05f));
        SetColour(retreatCoverRenderer,
            !forceFallback && retreatPathAvailable ? new Color(0.1f, 0.55f, 0.9f) : new Color(0.45f, 0.12f, 0.12f));
    }

    private void SetColour(Renderer targetRenderer, Color colour)
    {
        if (targetRenderer == null || propertyBlock == null) return;
        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", colour);
        propertyBlock.SetColor("_Color", colour);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }

    private void OnGUI()
    {
        EnsureStyles();
        float width = Mathf.Min(720f, Screen.width - 30f);
        GUILayout.BeginArea(new Rect(15f, 15f, width, Screen.height - 30f), GUI.skin.box);
        GUILayout.Label("WARDEN DEPTH-2 EXPECTIMAX TEST", titleStyle);
        GUILayout.Label(WardenDecisionResult.DepthConvention, bodyStyle);
        GUILayout.Space(6f);

        if (result == null)
        {
            GUILayout.Label("Waiting for first decision...", bodyStyle);
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label("Selected action: " + result.SelectedAction, selectedStyle);
        GUILayout.Label(
            $"Depth: {result.DecisionTurns}   Evaluated leaves: {result.EvaluatedLeafCount}   " +
            $"Fallback: {(result.UsedFallback ? "YES" : "no")}",
            result.UsedFallback ? invalidStyle : bodyStyle);
        if (result.UsedFallback)
            GUILayout.Label("Fallback reason: " + result.FallbackReason, invalidStyle);

        WardenResponseProbabilities probabilities = result.ResponseProbabilities;
        GUILayout.Label(
            $"Learned responses  Peek {probabilities.Peek:P1}   Push {probabilities.Push:P1}   Hide {probabilities.Hide:P1}",
            bodyStyle);
        GUILayout.Label(
            $"State  Visible:{playerVisible}  Known:{hasKnownPlayerPosition}  Ammo:{ammo}/12  " +
            $"Cover:{currentCoverUsable}  Desperation:{desperation:0.00}",
            bodyStyle);
        GUILayout.Space(6f);
        GUILayout.Label("CANDIDATE SCORES", titleStyle);

        foreach (WardenActionScore candidate in result.CandidateScores)
        {
            string line = candidate.IsValid
                ? $"{candidate.Action,-20} {candidate.Score,9:0.000}"
                : $"{candidate.Action,-20} INVALID - {candidate.InvalidReason}";
            GUILayout.Label(line, candidate.IsValid ? bodyStyle : invalidStyle);
        }

        GUILayout.Space(8f);
        GUILayout.Label("CONTROLS", titleStyle);
        GUILayout.Label(BuildControlText(), bodyStyle);
        GUILayout.Space(5f);
        GUILayout.Label("Last event: " + lastEvent, bodyStyle);
        GUILayout.EndArea();
    }

    private static string BuildControlText()
    {
        var text = new StringBuilder();
        text.AppendLine("1 / 2 / 3  record directly observed Peek / Push / Hide");
        text.AppendLine("V visibility   K known position   M empty/refill ammo   C current cover");
        text.AppendLine("G advance cover   F flank occupied   T retreat path   D desperation");
        text.AppendLine("B force/clear no-valid-action fallback   R reset everything");
        return text.ToString();
    }

    private void EnsureStyles()
    {
        if (bodyStyle != null) return;

        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            wordWrap = true,
            normal = { textColor = new Color(0.88f, 0.94f, 1f) }
        };
        titleStyle = new GUIStyle(bodyStyle)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.2f, 0.9f, 1f) }
        };
        selectedStyle = new GUIStyle(titleStyle)
        {
            fontSize = 23,
            normal = { textColor = new Color(0.3f, 1f, 0.45f) }
        };
        invalidStyle = new GUIStyle(bodyStyle)
        {
            normal = { textColor = new Color(1f, 0.42f, 0.32f) }
        };
    }
}
