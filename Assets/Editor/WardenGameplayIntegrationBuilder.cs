using System;
using Override.Combat;
using Override.Director;
using Override.EditorTools;
using Override.Motor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Creates an isolated playable copy of the verified Warden ARGUS scene.</summary>
public static class WardenGameplayIntegrationBuilder
{
    public const string ScenePath = "Assets/Scenes/WardenGameplayIntegrationTest.unity";
    private const string SourceScenePath = "Assets/Scenes/WardenArgusIntegrationTest.unity";
    private const string RealPlayerPrefabPath = "Assets/OVERRIDE/Prefabs/Player.prefab";

    [MenuItem("OVERRIDE/Core Developer/Rebuild Warden Gameplay Integration Test")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            throw new InvalidOperationException("Missing verified Warden integration scene: " + SourceScenePath);
        if (!Application.isBatchMode && HasDirtyScene())
            throw new InvalidOperationException("Save open scene changes before rebuilding the Warden gameplay test.");

        SceneSetup[] previousSetup = Application.isBatchMode
            ? Array.Empty<SceneSetup>()
            : EditorSceneManager.GetSceneManagerSetup();
        if (Array.Exists(previousSetup, setup => string.IsNullOrEmpty(setup.path)))
            previousSetup = Array.Empty<SceneSetup>();

        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                throw new InvalidOperationException("Could not copy scene to " + ScenePath);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BuildGameplayObjects(scene);
            ValidateScene(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save scene: " + ScenePath);

            AssetDatabase.SaveAssets();
            Debug.Log("[Warden Gameplay] Built and validated: " + ScenePath);
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

    private static void BuildGameplayObjects(Scene scene)
    {
        WardenArgusIntegrationRig previewRig = FindComponent<WardenArgusIntegrationRig>(scene);
        if (previewRig == null)
            throw new InvalidOperationException("Source scene is missing preview Warden.");

        // Ensure the scene uses the Systems Engineer's real player prefab (with Health, FootstepNoise, and HUD)
        GameObject realPlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RealPlayerPrefabPath);
        if (realPlayerPrefab == null)
            throw new InvalidOperationException("Missing real Player prefab: " + RealPlayerPrefabPath);

        GameObject legacyPlayer = FindTaggedObject(scene, "Player");
        Vector3 playerPos = legacyPlayer != null ? legacyPlayer.transform.position : new Vector3(75f, 1.1f, 27f);
        Quaternion playerRot = legacyPlayer != null ? legacyPlayer.transform.rotation : Quaternion.identity;
        Transform playerParent = legacyPlayer != null ? legacyPlayer.transform.parent : null;

        if (legacyPlayer != null)
            Object.DestroyImmediate(legacyPlayer);

        GameObject player = PrefabUtility.InstantiatePrefab(realPlayerPrefab, scene) as GameObject;
        if (player == null)
            throw new InvalidOperationException("Could not instantiate Player prefab: " + RealPlayerPrefabPath);

        player.name = "Player";
        player.tag = "Player";
        if (playerParent != null) player.transform.SetParent(playerParent, false);
        player.transform.SetPositionAndRotation(playerPos, playerRot);

        GameObject warden = previewRig.gameObject;
        VisionSensor vision = previewRig.Sensor;
        if (vision != null)
        {
            vision.Target = player.transform;
            EditorUtility.SetDirty(vision);
        }
        Object.DestroyImmediate(previewRig);
        warden.name = "Playable Expectimax Warden";

        CharacterController character = GetOrAdd<CharacterController>(warden);
        character.center = new Vector3(0f, 1.15f, 0f);
        character.height = 2.3f;
        character.radius = 0.62f;
        character.stepOffset = 0.25f;

        Health health = GetOrAdd<Health>(warden);
        SerializedObject healthData = new SerializedObject(health);
        healthData.FindProperty("maxHealth").floatValue = 200f;
        healthData.FindProperty("disableOnDeath").boolValue = true;
        healthData.ApplyModifiedPropertiesWithoutUndo();

        HearingSensor hearing = GetOrAdd<HearingSensor>(warden);
        hearing.Listener = warden.transform;
        hearing.HearingRange = 15f;
        hearing.ObstacleLayers = vision.ObstacleLayers;
        hearing.ObstructedRangeMultiplier = 0.5f;

        WardenVisualReferences visualReferences = warden.GetComponent<WardenVisualReferences>();
        WardenWeapon weapon = GetOrAdd<WardenWeapon>(warden);
        weapon.Configure(
            visualReferences != null ? visualReferences.Muzzle : warden.transform,
            ~0);

        AgentMotor motor = GetOrAdd<AgentMotor>(warden);
        SerializedObject motorData = new SerializedObject(motor);
        motorData.FindProperty("moveSpeed").floatValue = 3.5f;
        motorData.FindProperty("rotationSpeed").floatValue = 8f;
        motorData.ApplyModifiedPropertiesWithoutUndo();

        Transform parent = warden.transform.parent;
        WardenCoverPoint current = CreateCoverPoint("Warden Cover - Current", new Vector3(75f, 0.35f, 39f), 0.70f, parent);
        WardenCoverPoint advance = CreateCoverPoint("Warden Cover - Advance", new Vector3(75f, 0.35f, 35f), 0.80f, parent);
        WardenCoverPoint flank = CreateCoverPoint("Warden Cover - Flank", new Vector3(68.5f, 0.35f, 35f), 0.75f, parent);
        WardenCoverPoint retreat = CreateCoverPoint("Warden Cover - Retreat", new Vector3(75f, 0.35f, 43f), 0.90f, parent);

        DirectorController director = FindComponent<DirectorController>(scene);
        WardenGameplayController controller = GetOrAdd<WardenGameplayController>(warden);
        controller.Configure(
            vision,
            hearing,
            weapon,
            health,
            player.transform,
            director,
            current,
            advance,
            flank,
            retreat);

        EditorUtility.SetDirty(warden);
        EditorUtility.SetDirty(controller);
    }

    private static WardenCoverPoint CreateCoverPoint(
        string name,
        Vector3 position,
        float quality,
        Transform parent)
    {
        var marker = new GameObject(name);
        marker.transform.SetParent(parent, true);
        marker.transform.position = position;
        WardenCoverPoint point = marker.AddComponent<WardenCoverPoint>();
        point.Configure(quality);
        return point;
    }

    private static void ValidateScene(Scene scene)
    {
        WardenGameplayController controller = FindComponent<WardenGameplayController>(scene);
        if (controller == null)
            throw new InvalidOperationException("Gameplay scene has no WardenGameplayController.");
        if (controller.GetComponent<AgentMotor>() == null ||
            controller.GetComponent<CharacterController>() == null ||
            controller.GetComponent<WardenWeapon>() == null ||
            controller.GetComponent<HearingSensor>() == null ||
            controller.GetComponent<VisionSensor>() == null ||
            controller.GetComponent<Health>() == null)
        {
            throw new InvalidOperationException("Playable Warden is missing a required runtime component.");
        }

        GameObject player = FindTaggedObject(scene, "Player");
        if (player == null)
            throw new InvalidOperationException("Gameplay scene has no Player-tagged object.");

        Health playerHealth = player.GetComponent<Health>();
        CombatOverlay overlay = player.GetComponent<CombatOverlay>();
        if (playerHealth == null || !playerHealth.IsPlayer || overlay == null)
            throw new InvalidOperationException("Player must have player Health and CombatOverlay components.");

        SerializedObject overlayData = new SerializedObject(overlay);
        if (overlayData.FindProperty("rifle").objectReferenceValue == null ||
            overlayData.FindProperty("playerHealth").objectReferenceValue != playerHealth)
        {
            throw new InvalidOperationException("Player CombatOverlay references are not configured correctly.");
        }
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
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
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.CompareTag(tag)) return candidate.gameObject;
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
