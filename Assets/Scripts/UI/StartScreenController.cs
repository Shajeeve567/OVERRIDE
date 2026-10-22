using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Drives the OVERRIDE start screen: wires the menu buttons, fills the controls
/// panel, flickers the ARGUS status line and loads the game scene.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class StartScreenController : MonoBehaviour
{
    [Tooltip("Exact name of the gameplay scene. It must be added to Build Settings / Build Profiles.")]
    [SerializeField] private string _gameSceneName = "Main";

    [Tooltip("Milliseconds between flicker checks on the ARGUS line.")]
    [SerializeField] private long _flickerIntervalMs = 80;

    [Tooltip("Chance (0-1) per check that the ARGUS line dims.")]
    [Range(0f, 1f)]
    [SerializeField] private float _flickerChance = 0.08f;

    // Controls from GDD Section 5. Edit here if the bindings change.
    private static readonly (string Action, string Key)[] Controls =
    {
        ("Move", "W A S D"),
        ("Look", "Mouse"),
        ("Fire", "Left Mouse"),
        ("Reload", "R"),
        ("Interact / Hack / Push", "E"),
        ("Throw", "G"),
        ("Sprint", "Shift"),
        ("Crouch", "Ctrl"),
        ("Jump", "Space"),
        ("Debug Overlay", "F1"),
        ("Pause", "Esc"),
    };

    private VisualElement _menu;
    private VisualElement _controlsPanel;
    private Label _argusLine;
    private Button _startButton;
    private Button _controlsButton;
    private Button _quitButton;
    private Button _backButton;
    private IVisualElementScheduledItem _flicker;

    private void OnEnable()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;

        _menu = root.Q<VisualElement>("menu");
        _controlsPanel = root.Q<VisualElement>("controls-panel");
        _argusLine = root.Q<Label>("argus-line");
        _startButton = root.Q<Button>("start-button");
        _controlsButton = root.Q<Button>("controls-button");
        _quitButton = root.Q<Button>("quit-button");
        _backButton = root.Q<Button>("back-button");

        _startButton.clicked += StartGame;
        _controlsButton.clicked += ShowControls;
        _quitButton.clicked += QuitGame;
        _backButton.clicked += ShowMenu;

        BuildControlsList(root.Q<VisualElement>("controls-list"));

        _flicker = _argusLine.schedule.Execute(Flicker).Every(_flickerIntervalMs);

        // The gameplay scene locks the cursor; make sure it is usable here.
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        Time.timeScale = 1f;
    }

    private void OnDisable()
    {
        if (_startButton != null) _startButton.clicked -= StartGame;
        if (_controlsButton != null) _controlsButton.clicked -= ShowControls;
        if (_quitButton != null) _quitButton.clicked -= QuitGame;
        if (_backButton != null) _backButton.clicked -= ShowMenu;
        _flicker?.Pause();
    }

    /// <summary>Loads the gameplay scene, with a clear error if it isn't in the build.</summary>
    private void StartGame()
    {
        if (!Application.CanStreamedLevelBeLoaded(_gameSceneName))
        {
            Debug.LogError($"[StartScreen] Scene '{_gameSceneName}' is not in Build Settings / Build Profiles.");
            return;
        }

        SceneManager.LoadScene(_gameSceneName);
    }

    private void ShowControls()
    {
        _menu.AddToClassList("hidden");
        _controlsPanel.RemoveFromClassList("hidden");
    }

    private void ShowMenu()
    {
        _controlsPanel.AddToClassList("hidden");
        _menu.RemoveFromClassList("hidden");
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>Creates one row per control so the list lives in a single array.</summary>
    private static void BuildControlsList(VisualElement list)
    {
        list.Clear();
        foreach ((string action, string key) in Controls)
        {
            var row = new VisualElement();
            row.AddToClassList("control-row");

            var actionLabel = new Label(action);
            actionLabel.AddToClassList("control-action");

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("control-key");

            row.Add(actionLabel);
            row.Add(keyLabel);
            list.Add(row);
        }
    }

    /// <summary>Occasionally dims the ARGUS line to sell the "unstable system" look.</summary>
    private void Flicker()
    {
        _argusLine.style.opacity = Random.value < _flickerChance ? Random.Range(0.2f, 0.5f) : 1f;
    }
}
