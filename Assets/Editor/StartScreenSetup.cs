// StartScreenSetup.cs
// OVERRIDE > Set Up Start Screen: wires the existing start screen (Assets/UI/StartScreen) into the
// MainMenu scene and puts both scenes in the build, so pressing Play in MainMenu shows the menu and
// START OVERRIDE loads the game.
//
// What it does, and why each step is needed:
//   1. A theme (.tss): gives UI Toolkit its default fonts and control looks. Without a theme,
//      labels and buttons can render with no font at all.
//   2. A PanelSettings asset: UI Toolkit draws NOTHING without one. It also sets how the UI scales:
//      laid out for 1920x1080 and scaled to fit any screen, so the 110 px title isn't huge on a
//      small laptop or tiny on a 4K monitor.
//   3. A "StartScreen" object in MainMenu with a UIDocument (shows the UXML) and the existing
//      StartScreenController (makes the buttons work), pointed at the Greybox_v2 scene.
//   4. An EventSystem using the new Input System, because this project has the old Input Manager
//      switched off; this is what turns mouse clicks into button presses.
//   5. Build Settings: MainMenu first (index 0 = what a built game opens), then Greybox_v2.
//      SceneManager.LoadScene only finds scenes that are in this list.
// Safe to run again: it reuses whatever already exists.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace Override.EditorTools
{
    public static class StartScreenSetup
    {
        const string MenuScene = "Assets/Scenes/MainMenu.unity";
        const string GameScene = "Assets/OVERRIDE/Greybox/Greybox_v2.unity";
        const string UxmlPath = "Assets/UI/StartScreen/StartScreen.uxml";
        const string ThemePath = "Assets/UI/OverrideTheme.tss";
        const string PanelPath = "Assets/UI/OverridePanelSettings.asset";

        [MenuItem("OVERRIDE/Set Up Start Screen")]
        static void SetUp()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml == null) { Debug.LogError("[StartScreen] Missing " + UxmlPath); return; }
            if (!File.Exists(GameScene)) { Debug.LogError("[StartScreen] Missing " + GameScene); return; }

            var panel = LoadOrCreatePanelSettings();

            // ---- The menu scene ----
            var scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);

            var go = GameObject.Find("StartScreen") ?? new GameObject("StartScreen");
            var doc = go.GetComponent<UIDocument>();
            if (doc == null) doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = uxml;

            var controller = go.GetComponent<StartScreenController>();
            if (controller == null) controller = go.AddComponent<StartScreenController>();
            // _gameSceneName is private ([SerializeField]), so it's set the way the Inspector does it:
            // through a SerializedObject, which can reach serialized private fields.
            var so = new SerializedObject(controller);
            so.FindProperty("_gameSceneName").stringValue = Path.GetFileNameWithoutExtension(GameScene);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetAsLastSibling();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // ---- Build Settings: menu first, game second, anything else kept after them ----
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MenuScene, true),
                new EditorBuildSettingsScene(GameScene, true),
            };
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => s.path != MenuScene && s.path != GameScene));
            EditorBuildSettings.scenes = scenes.ToArray();

            Selection.activeGameObject = go;
            Debug.Log("[StartScreen] Ready: MainMenu is scene 0, Greybox_v2 is scene 1. Press Play in MainMenu.");
        }

        static PanelSettings LoadOrCreatePanelSettings()
        {
            // The theme file is one line: import Unity's built-in default runtime theme
            // (fonts, scroll bars, text field looks). The start screen's own USS styles on top of it.
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;   // halfway between matching width and height: works for wide and tall screens
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            return panel;
        }
    }
}
