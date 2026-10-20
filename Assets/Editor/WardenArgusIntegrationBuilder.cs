using System;
using Override.Director;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Override.EditorTools
{
    /// <summary>Builds a Warden integration copy of the team-owned ARGUS greybox.</summary>
    public static class WardenArgusIntegrationBuilder
    {
        public const string ScenePath = "Assets/Scenes/WardenArgusIntegrationTest.unity";
        private const string SourceScenePath = "Assets/OVERRIDE/Greybox/Greybox_v2.unity";
        private const string WardenPrefabPath = "Assets/_Project/Prefabs/Visuals/WardenVisual.prefab";
        private const int VisionObstacleLayer = 9;
        private const float PlayerMouseSensitivity = 0.08f;
        private const float PlayerSizedWardenScale = 0.77f;

        [MenuItem("OVERRIDE/Core Developer/Rebuild Warden ARGUS Integration Test")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
                throw new InvalidOperationException("Missing ARGUS source scene: " + SourceScenePath);

            GameObject wardenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WardenPrefabPath);
            if (wardenPrefab == null)
                throw new InvalidOperationException("Missing Warden visual prefab: " + WardenPrefabPath);

            if (!Application.isBatchMode && HasDirtyScene())
            {
                throw new InvalidOperationException(
                    "Save open scene changes before rebuilding the Warden ARGUS integration test.");
            }

            SceneSetup[] previousSetup = Application.isBatchMode
                ? Array.Empty<SceneSetup>()
                : EditorSceneManager.GetSceneManagerSetup();

            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                    AssetDatabase.DeleteAsset(ScenePath);
                if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                    throw new InvalidOperationException("Could not copy the ARGUS scene to " + ScenePath);

                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                BuildIntegrationObjects(scene, wardenPrefab);
                ValidateScene(scene);

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save integration scene: " + ScenePath);

                AssetDatabase.SaveAssets();
                Debug.Log("[Warden Integration] Built and validated: " + ScenePath);
            }
            finally
            {
                if (!Application.isBatchMode && previousSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }

        public static void BuildFromCommandLine()
        {
            Build();
        }

        private static void BuildIntegrationObjects(Scene scene, GameObject wardenPrefab)
        {
            GameObject player = FindTaggedObject(scene, "Player");
            if (player == null)
                throw new InvalidOperationException("The ARGUS scene has no active Player-tagged object.");

            // Use the open Labs room instead of the narrow airlock/doorway so the
            // stationary Warden cannot obstruct the route into the rest of the map.
            player.transform.SetPositionAndRotation(new Vector3(75f, 1.1f, 27f), Quaternion.identity);
            PlayerLook playerLook = player.GetComponent<PlayerLook>();
            if (playerLook != null)
            {
                // PlayerLook reads raw mouse delta, so the prefab's value is much
                // too sensitive for a controlled sensor demonstration.
                playerLook.mouseSensitivity = PlayerMouseSensitivity;
                EditorUtility.SetDirty(playerLook);
            }
            MarkSceneObstacles(scene, player.transform);

            var integrationRoot = new GameObject("WARDEN_ARGUS_INTEGRATION_TEST");
            SceneManager.MoveGameObjectToScene(integrationRoot, scene);

            GameObject warden = PrefabUtility.InstantiatePrefab(wardenPrefab, scene) as GameObject;
            if (warden == null)
                throw new InvalidOperationException("Could not instantiate the Warden visual prefab.");
            warden.name = "Stationary Warden - Decision Preview Only";
            warden.transform.SetParent(integrationRoot.transform, true);
            warden.transform.SetPositionAndRotation(
                new Vector3(75f, 0.35f, 39f),
                Quaternion.Euler(0f, 180f, 0f));
            // Scene-only visual override: approximately two metres high, matching
            // the player. The shared visual prefab remains at its approved scale.
            warden.transform.localScale = Vector3.one * PlayerSizedWardenScale;

            VisionSensor sensor = warden.GetComponent<VisionSensor>();
            if (sensor == null)
                throw new InvalidOperationException("The Warden prefab has no VisionSensor.");
            sensor.Target = player.transform;
            sensor.Range = 35f;
            sensor.FieldOfView = 100f;
            sensor.CheckInterval = 0.2f;
            sensor.ObstacleLayers = 1 << VisionObstacleLayer;
            EditorUtility.SetDirty(sensor);

            CreateCover("Left Vision Cover", new Vector3(69.5f, 1.25f, 33f), integrationRoot.transform);
            CreateCover("Right Vision Cover", new Vector3(80.5f, 1.25f, 33f), integrationRoot.transform);

            GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beacon.name = "Visibility Beacon - Green Seen, Red Hidden";
            beacon.transform.SetParent(integrationRoot.transform, true);
            beacon.transform.position = warden.transform.position + Vector3.up * 2.1f;
            beacon.transform.localScale = Vector3.one * 0.35f;
            Object.DestroyImmediate(beacon.GetComponent<Collider>());

            DirectorController director = FindComponent<DirectorController>(scene);
            if (director != null)
            {
                director.showPanel = false;
                EditorUtility.SetDirty(director);
            }

            WardenArgusIntegrationRig rig = warden.AddComponent<WardenArgusIntegrationRig>();
            rig.Configure(sensor, player.transform, director, beacon.GetComponent<Renderer>());
            EditorUtility.SetDirty(rig);
        }

        private static void CreateCover(string name, Vector3 position, Transform parent)
        {
            GameObject cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cover.name = name;
            cover.layer = VisionObstacleLayer;
            cover.transform.SetParent(parent, true);
            cover.transform.position = position;
            cover.transform.localScale = new Vector3(2.1f, 2.5f, 1.1f);
        }

        private static void MarkSceneObstacles(Scene scene, Transform player)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null || collider.isTrigger) continue;
                    if (collider.transform == player || collider.transform.IsChildOf(player)) continue;
                    if (collider.gameObject.layer == LayerMask.NameToLayer("Ground")) continue;
                    collider.gameObject.layer = VisionObstacleLayer;
                }
            }
        }

        private static void ValidateScene(Scene scene)
        {
            GameObject player = FindTaggedObject(scene, "Player");
            WardenArgusIntegrationRig rig = FindComponent<WardenArgusIntegrationRig>(scene);
            if (player == null || rig == null)
                throw new InvalidOperationException("Integration scene is missing its player or Warden rig.");
            if (rig.Sensor == null || rig.Sensor.Target != player.transform)
                throw new InvalidOperationException("VisionSensor is not wired to the live player.");
            if ((rig.Sensor.ObstacleLayers.value & (1 << VisionObstacleLayer)) == 0)
                throw new InvalidOperationException("VisionObstacle is absent from the obstruction mask.");
            if (FindComponent<DirectorController>(scene) == null)
                throw new InvalidOperationException("The copied scene has no ARGUS DirectorController.");
        }

        private static bool HasDirtyScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return true;
            return false;
        }

        private static GameObject FindTaggedObject(Scene scene, string tag)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                    if (candidate.CompareTag(tag)) return candidate.gameObject;
            }
            return null;
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null) return component;
            }
            return null;
        }
    }
}
