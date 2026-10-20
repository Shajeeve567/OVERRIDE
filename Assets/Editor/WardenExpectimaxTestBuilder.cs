using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Override.EditorTools
{
    /// <summary>Builds the isolated, stationary Warden Expectimax demonstration.</summary>
    public static class WardenExpectimaxTestBuilder
    {
        private const string ScenePath = "Assets/Scenes/WardenExpectimaxTest.unity";
        private const string WardenPrefabPath = "Assets/_Project/Prefabs/Visuals/WardenVisual.prefab";
        private const string AutoBuildKey = "Override.WardenExpectimaxTest.AutoBuild.v1";

        [InitializeOnLoadMethod]
        private static void ScheduleFirstBuild()
        {
            EditorApplication.delayCall += BuildOnceAfterImport;
        }

        [MenuItem("OVERRIDE/Core Developer/Rebuild Warden Expectimax Test")]
        public static void Build()
        {
            GameObject wardenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WardenPrefabPath);
            if (wardenPrefab == null)
                throw new InvalidOperationException("Missing Warden visual prefab: " + WardenPrefabPath);

            Scene previousActiveScene = SceneManager.GetActiveScene();
            bool hasSavedActiveScene = previousActiveScene.IsValid()
                && !string.IsNullOrEmpty(previousActiveScene.path);
            if (!hasSavedActiveScene && !Application.isBatchMode)
            {
                throw new InvalidOperationException(
                    "Open or save a normal scene before generating the Expectimax test scene.");
            }

            bool batchBootstrap = !hasSavedActiveScene;
            bool rebuildOpenScene = hasSavedActiveScene && string.Equals(
                previousActiveScene.path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase);
            Scene scene = batchBootstrap
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : rebuildOpenScene
                    ? previousActiveScene
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            try
            {
                if (rebuildOpenScene)
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                        Object.DestroyImmediate(root);
                }

                BuildEnvironment(scene, wardenPrefab);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save scene: " + ScenePath);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[Warden AI] Built standalone Expectimax test scene: " + ScenePath);
            }
            finally
            {
                if (!rebuildOpenScene && !batchBootstrap)
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                        SceneManager.SetActiveScene(previousActiveScene);
                }
            }
        }

        private static void BuildOnceAfterImport()
        {
            if (SessionState.GetBool(AutoBuildKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(WardenPrefabPath) == null) return;

            SessionState.SetBool(AutoBuildKey, true);
            try
            {
                Build();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError(
                    "[Warden AI] Automatic scene build failed. Open a saved scene, then use " +
                    "OVERRIDE > Core Developer > Rebuild Warden Expectimax Test.");
            }
        }

        private static void BuildEnvironment(Scene scene, GameObject wardenPrefab)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            MoveToScene(floor, scene);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(1.25f, 1f, 0.8f);

            GameObject warden = PrefabUtility.InstantiatePrefab(wardenPrefab, scene) as GameObject;
            if (warden == null) throw new InvalidOperationException("Could not instantiate WardenVisual.");
            warden.name = "Stationary WardenVisual";
            warden.transform.position = new Vector3(0f, 0f, 1.25f);
            warden.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            GameObject target = CreatePrimitive(
                PrimitiveType.Capsule,
                "Simulated Player Target",
                new Vector3(0f, 1f, -1.7f),
                new Vector3(0.55f, 1f, 0.55f),
                scene);
            GameObject currentCover = CreatePrimitive(
                PrimitiveType.Cube,
                "Current Cover",
                new Vector3(-1.6f, 0.65f, 0.3f),
                new Vector3(0.5f, 1.3f, 1.8f),
                scene);
            GameObject advanceCover = CreatePrimitive(
                PrimitiveType.Cube,
                "Advance Cover",
                new Vector3(2.3f, 0.55f, -0.4f),
                new Vector3(1.2f, 1.1f, 0.45f),
                scene);
            GameObject flankCover = CreatePrimitive(
                PrimitiveType.Cube,
                "Flank Cover",
                new Vector3(-3.2f, 0.55f, -1.6f),
                new Vector3(1.2f, 1.1f, 0.45f),
                scene);
            GameObject retreatCover = CreatePrimitive(
                PrimitiveType.Cube,
                "Retreat Cover",
                new Vector3(3.3f, 0.55f, 2.2f),
                new Vector3(1.2f, 1.1f, 0.45f),
                scene);

            var rigObject = new GameObject("Warden Expectimax Test Rig");
            MoveToScene(rigObject, scene);
            WardenExpectimaxTestRig rig = rigObject.AddComponent<WardenExpectimaxTestRig>();
            SerializedObject serializedRig = new SerializedObject(rig);
            serializedRig.FindProperty("playerTargetRenderer").objectReferenceValue = target.GetComponent<Renderer>();
            serializedRig.FindProperty("currentCoverRenderer").objectReferenceValue = currentCover.GetComponent<Renderer>();
            serializedRig.FindProperty("advanceCoverRenderer").objectReferenceValue = advanceCover.GetComponent<Renderer>();
            serializedRig.FindProperty("flankCoverRenderer").objectReferenceValue = flankCover.GetComponent<Renderer>();
            serializedRig.FindProperty("retreatCoverRenderer").objectReferenceValue = retreatCover.GetComponent<Renderer>();
            serializedRig.ApplyModifiedPropertiesWithoutUndo();

            CreateLighting(scene);
            CreateCamera(scene);
        }

        private static GameObject CreatePrimitive(
            PrimitiveType primitiveType,
            string name,
            Vector3 position,
            Vector3 scale,
            Scene scene)
        {
            GameObject result = GameObject.CreatePrimitive(primitiveType);
            MoveToScene(result, scene);
            result.name = name;
            result.transform.position = position;
            result.transform.localScale = scale;
            return result;
        }

        private static void CreateLighting(Scene scene)
        {
            var keyObject = new GameObject("Directional Light");
            MoveToScene(keyObject, scene);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            keyObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            var fillObject = new GameObject("Cyan Fill Light");
            MoveToScene(fillObject, scene);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 12f;
            fill.intensity = 4f;
            fill.color = new Color(0.12f, 0.65f, 1f);
            fillObject.transform.position = new Vector3(-2f, 4f, -2f);
        }

        private static void CreateCamera(Scene scene)
        {
            var cameraObject = new GameObject("Main Camera");
            MoveToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            camera.fieldOfView = 48f;
            cameraObject.transform.position = new Vector3(0f, 4.2f, -8.2f);
            cameraObject.transform.LookAt(new Vector3(0f, 1.1f, 0.2f));
        }

        private static void MoveToScene(GameObject gameObject, Scene scene)
        {
            if (gameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(gameObject, scene);
        }
    }
}
