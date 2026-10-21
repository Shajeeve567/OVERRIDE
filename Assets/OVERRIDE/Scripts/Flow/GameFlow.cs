// GameFlow.cs
// The run from start to finish (GDD Section 3): pause, death and respawn, and victory.
//
//   Esc            pause: time stops, the cursor is freed, RESUME / MAIN MENU
//   Health hits 0  game over: RESPAWN AT LAST TERMINAL / MAIN MENU
//                  GDD respawn rule: back at the last completed terminal with progress kept; destroyed
//                  Seekers and Wardens stay destroyed (they're disabled, not reset); living drones are cleared.
//   Core done      victory: time taken and deaths, MAIN MENU / QUIT
//
// The screens are built in code with UI Toolkit, using Shajeeve's start-screen style sheet and panel
// settings, so they match the main menu. Put this on one object in the gameplay scene.

using Override.Combat;
using Override.Core;
using Override.Director;
using Override.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Override.Flow
{
    public class GameFlow : MonoBehaviour
    {
        [SerializeField] PanelSettings panelSettings;     // Assets/UI/OverridePanelSettings
        [SerializeField] StyleSheet styleSheet;           // Assets/UI/StartScreen/StartScreen.uss
        [SerializeField] string mainMenuScene = "MainMenu";
        [SerializeField] float victoryDelay = 2f;         // let the "ARGUS OFFLINE" moment land first

        enum State { Playing, Paused, Dead, Won }
        State state = State.Playing;

        Transform player;
        CharacterController playerBody;
        FirstPersonController controller;
        PlayerInputReader input;
        PlayerDeath death;
        DirectorController director;

        Vector3 respawnPosition;
        Quaternion respawnRotation;
        string respawnLabel = "the Airlock";
        float startTime;
        int deaths;

        VisualElement overlay, pauseScreen, deadScreen, wonScreen;
        Label deadInfo, wonInfo;

        void Start()
        {
            Time.timeScale = 1f;
            startTime = Time.time;

            var p = GameObject.FindWithTag("Player");
            if (p != null)
            {
                player = p.transform;
                playerBody = p.GetComponent<CharacterController>();
                controller = p.GetComponent<FirstPersonController>();
                input = p.GetComponent<PlayerInputReader>();
                death = p.GetComponent<PlayerDeath>();
                respawnPosition = player.position;
                respawnRotation = player.rotation;
            }
            else Debug.LogWarning("[GameFlow] No Player in the scene.");
            director = FindFirstObjectByType<DirectorController>();

            BuildScreens();
        }

        void OnEnable()
        {
            GameEvents.PlayerDied += OnPlayerDied;
            GameEvents.TerminalOverridden += OnTerminalOverridden;
        }

        void OnDisable()
        {
            GameEvents.PlayerDied -= OnPlayerDied;
            GameEvents.TerminalOverridden -= OnTerminalOverridden;
        }

        void OnDestroy() => Time.timeScale = 1f;   // never leave the next scene frozen

        void Update()
        {
            if (input == null || !input.PausePressed) return;
            if (state == State.Playing) Pause();
            else if (state == State.Paused) Resume();
        }

        // ---------------------------------------------------------------- events

        void OnTerminalOverridden(ZoneId zone)
        {
            if (zone == ZoneId.Core)
            {
                Invoke(nameof(Win), victoryDelay);
                return;
            }
            // The player is standing at the terminal they just finished: respawn here from now on.
            if (player != null)
            {
                respawnPosition = player.position;
                respawnRotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
                respawnLabel = "the " + zone + " terminal";
            }
        }

        void OnPlayerDied()
        {
            if (state == State.Won) return;
            state = State.Dead;
            deaths++;
            deadInfo.text = $"Respawn at {respawnLabel}. Terminal progress is kept.";
            Show(deadScreen);
        }

        // ---------------------------------------------------------------- actions

        void Pause()
        {
            state = State.Paused;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            if (controller != null) controller.InputEnabled = false;
            Show(pauseScreen);
        }

        void Resume()
        {
            state = State.Playing;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (controller != null) controller.InputEnabled = true;
            HideAll();
            FirstPersonController.SetCursorLocked(true);
        }

        void Respawn()
        {
            if (player == null) return;
            // A CharacterController overrides transform moves while enabled, so switch it off to teleport.
            if (playerBody != null) playerBody.enabled = false;
            player.SetPositionAndRotation(respawnPosition, respawnRotation);
            if (playerBody != null) playerBody.enabled = true;

            if (death != null) death.Revive();     // full health, input and rifle back, cursor locked
            if (director != null) director.ClearDrones();

            state = State.Playing;
            HideAll();
        }

        void Win()
        {
            state = State.Won;
            if (controller != null) controller.InputEnabled = false;
            float seconds = Time.time - startTime;
            wonInfo.text = $"Time {Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}    Deaths {deaths}";
            Show(wonScreen);
        }

        void GoToMainMenu()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(mainMenuScene);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- screens

        void BuildScreens()
        {
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = 10;   // above the HUD
            if (panelSettings == null) Debug.LogWarning("[GameFlow] No PanelSettings assigned: screens won't show.");

            overlay = document.rootVisualElement;
            if (styleSheet != null) overlay.styleSheets.Add(styleSheet);
            overlay.pickingMode = PickingMode.Ignore;   // never block the game while nothing is shown

            pauseScreen = BuildScreen("PAUSED", "ARGUS: SURVEILLANCE CONTINUES", out _,
                                 ("RESUME", Resume, true), ("MAIN MENU", GoToMainMenu, false));
            deadScreen = BuildScreen("YOU DIED", "ARGUS: INTRUDER NEUTRALISED", out deadInfo,
                                ("RESPAWN", Respawn, true), ("MAIN MENU", GoToMainMenu, false));
            wonScreen = BuildScreen("ARGUS OFFLINE", "FACILITY CONTROL RESTORED", out wonInfo,
                               ("MAIN MENU", GoToMainMenu, true), ("QUIT", Quit, false));
            HideAll();
        }

        VisualElement BuildScreen(string title, string argusLine, out Label info,
                             params (string text, System.Action action, bool primary)[] buttons)
        {
            var screen = new VisualElement();
            screen.AddToClassList("root");
            screen.style.position = Position.Absolute;
            screen.style.left = screen.style.right = screen.style.top = screen.style.bottom = 0;
            screen.style.backgroundColor = new Color(0.04f, 0.05f, 0.06f, 0.85f);   // the game shows through

            var menu = new VisualElement();
            menu.AddToClassList("menu");
            screen.Add(menu);

            var heading = new Label(title);
            heading.AddToClassList("title");
            heading.style.fontSize = 64;
            menu.Add(heading);

            var accent = new VisualElement();
            accent.AddToClassList("accent");
            menu.Add(accent);

            var argus = new Label(argusLine);
            argus.AddToClassList("argus");
            argus.style.marginBottom = 16;
            menu.Add(argus);

            info = new Label("");
            info.AddToClassList("facility");
            info.style.marginBottom = 24;
            menu.Add(info);

            foreach (var (text, action, primary) in buttons)
            {
                var button = new Button(action) { text = text };
                button.AddToClassList("menu-button");
                if (primary) button.AddToClassList("primary");
                menu.Add(button);
            }

            overlay.Add(screen);
            return screen;
        }

        void Show(VisualElement screen)
        {
            HideAll();
            screen.style.display = DisplayStyle.Flex;
            FirstPersonController.SetCursorLocked(false);   // the mouse must reach the buttons
        }

        void HideAll()
        {
            foreach (var s in new[] { pauseScreen, deadScreen, wonScreen })
                if (s != null) s.style.display = DisplayStyle.None;
        }
    }
}
