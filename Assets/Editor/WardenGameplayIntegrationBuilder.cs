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
    public const string PrefabPath = "Assets/_Project/Prefabs/WardenGameplay.prefab";
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
            Scene sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(sourceScene, ScenePath, true))
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
        Vector3 playerPos = legacyPlayer != null ? legacyPlayer.transform.position : new Vector3(75f, 1.1f, 30f);
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

        Health playerHealth = player.GetComponent<Health>();
        if (playerHealth != null)
        {
            SerializedObject playerHealthData = new SerializedObject(playerHealth);
            playerHealthData.FindProperty("maxHealth").floatValue = 200f;
            playerHealthData.ApplyModifiedPropertiesWithoutUndo();
            playerHealth.Configure(200f, false);
            EditorUtility.SetDirty(playerHealth);
        }

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
        healthData.FindProperty("maxHealth").floatValue = 350f;
        healthData.FindProperty("disableOnDeath").boolValue = false;
        healthData.ApplyModifiedPropertiesWithoutUndo();
        health.Configure(350f, false);
        EditorUtility.SetDirty(health);

        WardenVisualReferences visualReferences = warden.GetComponentInChildren<WardenVisualReferences>();

        HearingSensor hearing = GetOrAdd<HearingSensor>(warden);
        hearing.Listener = visualReferences != null && visualReferences.Eye != null ? visualReferences.Eye : warden.transform;
        hearing.HearingRange = 25f;
        hearing.SensitivityMultiplier = 2.5f;
        hearing.ObstacleLayers = vision.ObstacleLayers;
        hearing.ObstructedRangeMultiplier = 0.8f;

        WardenWeapon weapon = GetOrAdd<WardenWeapon>(warden);
        weapon.Configure(
            visualReferences != null && visualReferences.Muzzle != null ? visualReferences.Muzzle : warden.transform,
            ~0);

        AgentMotor motor = GetOrAdd<AgentMotor>(warden);
        SerializedObject motorData = new SerializedObject(motor);
        motorData.FindProperty("moveSpeed").floatValue = 3.5f;
        motorData.FindProperty("rotationSpeed").floatValue = 8f;
        motorData.ApplyModifiedPropertiesWithoutUndo();

        Transform parent = warden.transform.parent;
        WardenCoverPoint current = CreateCoverPoint("Warden Cover - Current", new Vector3(75f, 0.35f, 38f), 0.75f, parent);
        WardenCoverPoint advance = CreateCoverPoint("Warden Cover - Advance", new Vector3(75f, 0.35f, 34f), 0.85f, parent);
        WardenCoverPoint flank = CreateCoverPoint("Warden Cover - Flank", new Vector3(68.5f, 0.35f, 34f), 0.85f, parent);
        WardenCoverPoint retreat = CreateCoverPoint("Warden Cover - Retreat", new Vector3(75f, 0.35f, 42f), 0.70f, parent);

        DirectorController director = FindComponent<DirectorController>(scene);
        if (director != null)
        {
            SerializedObject directorData = new SerializedObject(director);
            var playerMaxHealthProp = directorData.FindProperty("playerMaxHealth");
            if (playerMaxHealthProp != null) playerMaxHealthProp.floatValue = 200f;
            directorData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(director);
        }
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

        ExportPortablePrefab(warden);
    }

    private static void ExportPortablePrefab(GameObject sceneWarden)
    {
        GameObject portable = Object.Instantiate(sceneWarden);
        portable.name = "WardenGameplay";
        portable.transform.SetParent(null, false);
        portable.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        try
        {
            VisionSensor vision = portable.GetComponent<VisionSensor>();
            HearingSensor hearing = portable.GetComponent<HearingSensor>();
            WardenWeapon weapon = portable.GetComponent<WardenWeapon>();
            Health health = portable.GetComponent<Health>();
            WardenGameplayController controller = portable.GetComponent<WardenGameplayController>();

            if (vision == null || hearing == null || weapon == null || health == null || controller == null)
                throw new InvalidOperationException("Portable Warden clone is missing required components.");

            WardenVisualReferences references = portable.GetComponentInChildren<WardenVisualReferences>(true);
            WardenWeakPointColliderUtility.Configure(portable);
            vision.Target = null;
            hearing.HearingRange = 15f;
            hearing.SensitivityMultiplier = 1f;
            hearing.ObstructedRangeMultiplier = 0.5f;
            health.Configure(200f, false);

            SerializedObject healthData = new SerializedObject(health);
            healthData.FindProperty("maxHealth").floatValue = 200f;
            healthData.FindProperty("disableOnDeath").boolValue = true;
            healthData.ApplyModifiedPropertiesWithoutUndo();

            controller.Configure(
                vision, hearing, weapon, health, null, null,
                null, null, null, null);

            if (references != null && references.Muzzle != null)
                weapon.Configure(references.Muzzle, ~0);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(portable, PrefabPath, out bool success);
            if (!success || saved == null)
                throw new InvalidOperationException("Could not save portable Warden prefab: " + PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(portable);
        }

        ValidatePrefab();
        Debug.Log("[Warden Gameplay] Exported and validated portable prefab: " + PrefabPath);
    }

    private static void ValidatePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Missing exported Warden prefab: " + PrefabPath);

        WardenGameplayController controller = prefab.GetComponent<WardenGameplayController>();
        VisionSensor vision = prefab.GetComponent<VisionSensor>();
        HearingSensor hearing = prefab.GetComponent<HearingSensor>();
        WardenWeapon weapon = prefab.GetComponent<WardenWeapon>();
        Health health = prefab.GetComponent<Health>();
        WardenVisualReferences references = prefab.GetComponentInChildren<WardenVisualReferences>(true);

        if (controller == null || vision == null || hearing == null || weapon == null || health == null)
            throw new InvalidOperationException("Exported Warden prefab is missing required gameplay components.");
        if (controller.PlayerTarget != null || controller.Director != null ||
            controller.CurrentCover != null || controller.AdvanceCover != null ||
            controller.FlankCover != null || controller.RetreatCover != null || vision.Target != null)
        {
            throw new InvalidOperationException("Exported Warden prefab retained scene-owned references.");
        }
        if (references == null || references.Eye == null || references.Muzzle == null ||
            vision.Eye == null || weapon.Muzzle == null)
        {
            throw new InvalidOperationException("Exported Warden prefab lost an internal eye or muzzle reference.");
        }
        if (!Mathf.Approximately(health.Max, 200f) || !Mathf.Approximately(hearing.HearingRange, 15f))
            throw new InvalidOperationException("Exported Warden prefab contains integration-test balance values.");

        WardenWeakPointColliderUtility.Validate(prefab);
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

internal static class SeekerWeakPointColliderUtility
{
    private const string Path = "Assets/_Project/Prefabs/Visuals/SeekerVisual.prefab";
    private const string SourceName = "SensorCore_WeakPoint";
    private const string ProxyName = "SensorCore_WeakPoint_Collider";

    [MenuItem("OVERRIDE/Core Developer/Fix Seeker Weak-Point Collider")]
    public static void FixPrefabAsset()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            Transform source = Find(root.transform, SourceName);
            MeshFilter filter = source != null ? source.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Seeker weak point has no source mesh.");

            Transform proxy = root.transform.Find(ProxyName);
            if (proxy == null)
            {
                proxy = new GameObject(ProxyName).transform;
                proxy.SetParent(root.transform, false);
            }
            proxy.localPosition = root.transform.InverseTransformPoint(source.position);
            proxy.localRotation = Quaternion.Inverse(root.transform.rotation) * source.rotation;
            Vector3 sourceScale = source.lossyScale;
            Vector3 rootScale = root.transform.lossyScale;
            proxy.localScale = new Vector3(
                sourceScale.x / rootScale.x,
                sourceScale.y / rootScale.y,
                sourceScale.z / rootScale.z);

            MeshCollider collider = proxy.GetComponent<MeshCollider>();
            if (collider == null) collider = proxy.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = true;
            SeekerWeakPoint weakPoint = proxy.GetComponent<SeekerWeakPoint>();
            if (weakPoint == null) weakPoint = proxy.gameObject.AddComponent<SeekerWeakPoint>();
            weakPoint.Configure(2f);

            SeekerVisualReferences refs = root.GetComponent<SeekerVisualReferences>();
            if (refs == null) throw new InvalidOperationException("Seeker references are missing.");
            refs.Configure(
                Require(root.transform, "SensorCore"),
                Require(root.transform, "SensorHousing"),
                proxy,
                Require(root.transform, "LeftThruster"),
                Require(root.transform, "RightThruster"));
            if (PrefabUtility.SaveAsPrefabAsset(root, Path) == null)
                throw new InvalidOperationException("Could not save corrected Seeker prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Seeker] Corrected the live sensor-core weak-point collider.");
    }

    public static void FixFromCommandLine() { FixPrefabAsset(); }

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static Transform Require(Transform root, string name)
    {
        Transform result = Find(root, name);
        if (result == null)
            throw new InvalidOperationException("Seeker transform is missing: " + name);
        return result;
    }
}

internal static class WardenWeakPointColliderUtility
{
    internal static readonly Vector3 RearLocalSize = new Vector3(0.00545f, 0.00468f, 0.00260f);
    internal const float EyeLocalRadius = 0.00208f;

    private const string VisualPrefabPath = "Assets/_Project/Prefabs/Visuals/WardenVisual.prefab";

    [MenuItem("OVERRIDE/Core Developer/Fix Warden Weak-Point Colliders")]
    public static void FixPrefabAssets()
    {
        FixPrefab(VisualPrefabPath);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(WardenGameplayIntegrationBuilder.PrefabPath) != null)
            FixPrefab(WardenGameplayIntegrationBuilder.PrefabPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[Warden] Corrected and validated weak-point collider world scale.");
    }

    public static void FixFromCommandLine()
    {
        FixPrefabAssets();
    }

    internal static void Configure(GameObject root)
    {
        foreach (WardenWeakPoint weakPoint in root.GetComponentsInChildren<WardenWeakPoint>(true))
        {
            if (weakPoint.Type == WardenWeakPoint.WeakPointType.SensorEye)
            {
                SphereCollider sphere = weakPoint.GetComponent<SphereCollider>();
                if (sphere == null)
                    throw new InvalidOperationException("Warden sensor-eye weak point requires a SphereCollider.");
                sphere.radius = EyeLocalRadius;
            }
            else
            {
                BoxCollider box = weakPoint.GetComponent<BoxCollider>();
                if (box == null)
                    throw new InvalidOperationException("Warden rear weak point requires a BoxCollider.");
                box.size = RearLocalSize;
            }
        }
    }

    internal static void Validate(GameObject root)
    {
        WardenWeakPoint[] weakPoints = root.GetComponentsInChildren<WardenWeakPoint>(true);
        if (weakPoints.Length != 2)
            throw new InvalidOperationException("Warden prefab must contain exactly two weak points.");

        foreach (WardenWeakPoint weakPoint in weakPoints)
        {
            if (weakPoint.Type == WardenWeakPoint.WeakPointType.SensorEye)
            {
                SphereCollider sphere = weakPoint.GetComponent<SphereCollider>();
                if (sphere == null || !Mathf.Approximately(sphere.radius, EyeLocalRadius))
                    throw new InvalidOperationException("Warden sensor-eye collider has an unsafe local radius.");
            }
            else
            {
                BoxCollider box = weakPoint.GetComponent<BoxCollider>();
                if (box == null || (box.size - RearLocalSize).sqrMagnitude > 0.00000001f)
                    throw new InvalidOperationException("Warden rear collider has an unsafe local size.");
            }
        }
    }

    private static void FixPrefab(string path)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Configure(contents);
            Validate(contents);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
