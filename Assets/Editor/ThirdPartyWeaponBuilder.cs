using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Override.EditorTools
{
    /// <summary>
    /// Creates project-owned URP materials and visual-only wrapper prefabs while
    /// leaving the original Asset Store packages untouched.
    /// </summary>
    public static class ThirdPartyWeaponBuilder
    {
        private const string MaterialRoot = "Assets/_Project/Art/Materials/Weapons";
        private const string PrefabRoot = "Assets/_Project/Prefabs/Visuals/Weapons";
        private const string ScenePath = "Assets/Scenes/WeaponVisualTest.unity";
        private const string AutoBuildKey = "Override.ThirdPartyWeapons.AutoBuild.v2";

        private const string PistolPrefabSource =
            "Assets/Sci Fi Gun Light/Prefabs/SciFiGunLight/SciFiGunLight_Blue.prefab";
        private const string HeavyPrefabSource =
            "Assets/MASH Virtual/Sci Fi Gun/Prefab/Gun.prefab";

        private const string RiflePrefabOutput = PrefabRoot + "/PulseRifleVisual.prefab";
        private const string PistolPrefabOutput = PrefabRoot + "/EnergyPistolVisual.prefab";
        private const string HeavyPrefabOutput = PrefabRoot + "/EmpHeavyGunVisual.prefab";

        [InitializeOnLoadMethod]
        private static void ScheduleFirstBuild()
        {
            EditorApplication.delayCall += BuildOnceAfterImport;
        }

        [MenuItem("OVERRIDE/Core Developer/Rebuild Third-Party Weapon Visuals")]
        public static void Build()
        {
            EnsureFolder(MaterialRoot);
            EnsureFolder(PrefabRoot);

            ConfigureTexture(
                "Assets/Sci Fi Gun Light/Textures/ColorVar/SciFiGunLight_Normal.png",
                false,
                TextureImporterType.NormalMap);
            ConfigureTexture(
                "Assets/Sci Fi Gun Light/Textures/ColorVar/Blue/SciFiGunLight_SpecularSmoothness.png",
                false,
                TextureImporterType.Default);
            ConfigureTexture(
                "Assets/MASH Virtual/Sci Fi Gun/Texture/GunModle_Gun_Normal.png",
                false,
                TextureImporterType.NormalMap);
            ConfigureTexture(
                "Assets/MASH Virtual/Sci Fi Gun/Texture/GunModle_Gun_MetallicSmoothness.png",
                false,
                TextureImporterType.Default);
            ConfigureTexture(
                "Assets/MASH Virtual/Sci Fi Gun/Texture/GunModle_Gun_AlbedoAO.png",
                false,
                TextureImporterType.Default);

            Material pistolMaterial = BuildPistolMaterial();
            Material heavyMaterial = BuildHeavyMaterial();

            // The Blender rifle's barrel axis imports vertically. Keep the assessed
            // RifleVisual untouched and normalize a gameplay/showcase wrapper to Unity +Z.
            BuildWeaponPrefab(
                "Assets/_Project/Prefabs/Visuals/RifleVisual.prefab",
                RiflePrefabOutput,
                "PulseRifleVisual",
                null,
                Quaternion.Euler(-90f, 0f, 0f),
                0.78f,
                null);

            BuildWeaponPrefab(
                PistolPrefabSource,
                PistolPrefabOutput,
                "EnergyPistolVisual",
                pistolMaterial,
                Quaternion.identity,
                0.42f,
                null);

            BuildWeaponPrefab(
                HeavyPrefabSource,
                HeavyPrefabOutput,
                "EmpHeavyGunVisual",
                heavyMaterial,
                Quaternion.Euler(0f, -90f, 0f),
                0.85f,
                "Gun_Bullet_Hole");

            bool sceneBuilt = BuildShowcaseScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(sceneBuilt
                ? "[Weapons] Built URP weapon visuals and WeaponVisualTest scene. Vendor assets were not modified."
                : "[Weapons] Built URP weapon visuals. Save or open a normal scene, then use the rebuild menu to generate WeaponVisualTest.");
        }

        private static void BuildOnceAfterImport()
        {
            if (SessionState.GetBool(AutoBuildKey, false) && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PistolPrefabSource) == null) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HeavyPrefabSource) == null) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RiflePrefabOutput) != null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(PistolPrefabOutput) != null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(HeavyPrefabOutput) != null &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;

            SessionState.SetBool(AutoBuildKey, true);
            try
            {
                Build();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("[Weapons] Automatic build failed. Use OVERRIDE > Core Developer > Rebuild Third-Party Weapon Visuals after resolving the error.");
            }
        }

        private static Material BuildPistolMaterial()
        {
            Material material = GetOrCreateLitMaterial("EnergyPistol_Blue_URP");
            material.SetFloat("_WorkflowMode", 0f);
            material.SetTexture("_BaseMap", LoadTexture(
                "Assets/Sci Fi Gun Light/Textures/ColorVar/Blue/SciFiGunLight_AlbedoTransparency.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", LoadTexture(
                "Assets/Sci Fi Gun Light/Textures/ColorVar/SciFiGunLight_Normal.png"));
            material.SetTexture("_SpecGlossMap", LoadTexture(
                "Assets/Sci Fi Gun Light/Textures/ColorVar/Blue/SciFiGunLight_SpecularSmoothness.png"));
            material.SetTexture("_EmissionMap", LoadTexture(
                "Assets/Sci Fi Gun Light/Textures/ColorVar/SciFiGunLight_Emission2.png"));
            material.SetColor("_EmissionColor", new Color(0.15f, 1.2f, 2.2f));
            material.SetFloat("_Smoothness", 0.55f);
            SetKeyword(material, "_NORMALMAP", true);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", true);
            SetKeyword(material, "_EMISSION", true);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildHeavyMaterial()
        {
            Material material = GetOrCreateLitMaterial("EmpHeavyGun_URP");
            material.SetFloat("_WorkflowMode", 1f);
            material.SetTexture("_BaseMap", LoadTexture(
                "Assets/MASH Virtual/Sci Fi Gun/Texture/GunModle_Gun_AlbedoTransparency.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", LoadTexture(
                "Assets/MASH Virtual/Sci Fi Gun/Texture/GunModle_Gun_Normal.png"));
            material.SetTexture("_MetallicGlossMap", LoadTexture(
                "Assets/MASH Virtual/Sci Fi Gun/Texture/GunModle_Gun_MetallicSmoothness.png"));
            material.SetTexture("_OcclusionMap", LoadTexture(
                "Assets/MASH Virtual/Sci Fi Gun/Texture/GunModle_Gun_AlbedoAO.png"));
            material.SetTexture("_EmissionMap", LoadTexture(
                "Assets/MASH Virtual/Sci Fi Gun/Texture/GunModle_Gun_Emission.png"));
            material.SetColor("_EmissionColor", new Color(0.05f, 1.35f, 2.5f));
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 0.55f);
            SetKeyword(material, "_NORMALMAP", true);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", true);
            SetKeyword(material, "_OCCLUSIONMAP", true);
            SetKeyword(material, "_EMISSION", true);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateLitMaterial(string assetName)
        {
            string path = MaterialRoot + "/" + assetName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");

            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            return material;
        }

        private static void BuildWeaponPrefab(
            string sourcePath,
            string outputPath,
            string rootName,
            Material material,
            Quaternion modelRotation,
            float targetLength,
            string muzzleMarkerName)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException("Missing source prefab: " + sourcePath);

            var root = new GameObject(rootName);
            GameObject visual = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (visual == null) throw new InvalidOperationException("Could not instantiate: " + sourcePath);

            visual.name = "Model";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = modelRotation;

            Transform canvas = FindDescendant(visual.transform, "Canvas");
            if (canvas != null) Object.DestroyImmediate(canvas.gameObject);

            if (material != null)
            {
                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    {
                        Material[] replacements = new Material[renderer.sharedMaterials.Length];
                        for (int index = 0; index < replacements.Length; index++) replacements[index] = material;
                        renderer.sharedMaterials = replacements;
                    }
                }
            }

            Bounds bounds = CalculateLocalBounds(root.transform, visual.GetComponentsInChildren<Renderer>(true));
            float longestAxis = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longestAxis > 0.0001f)
                visual.transform.localScale *= targetLength / longestAxis;

            bounds = CalculateLocalBounds(root.transform, visual.GetComponentsInChildren<Renderer>(true));
            AddReference(root.transform, "Grip", Vector3.zero);

            Vector3 muzzlePosition;
            Transform marker = string.IsNullOrEmpty(muzzleMarkerName)
                ? null
                : FindDescendant(visual.transform, muzzleMarkerName);
            if (marker != null)
                muzzlePosition = root.transform.InverseTransformPoint(marker.position);
            else
                muzzlePosition = new Vector3(bounds.center.x, bounds.center.y + bounds.extents.y * 0.2f, bounds.max.z);

            AddReference(root.transform, "Muzzle", muzzlePosition);
            PrefabUtility.SaveAsPrefabAsset(root, outputPath);
            Object.DestroyImmediate(root);
        }

        private static bool BuildShowcaseScene()
        {
            Scene previousActiveScene = SceneManager.GetActiveScene();
            if (!previousActiveScene.IsValid() || string.IsNullOrEmpty(previousActiveScene.path))
            {
                Debug.LogWarning(
                    "[Weapons] WeaponVisualTest was not generated because the current scene is untitled. " +
                    "Save it or open an existing scene, then use OVERRIDE > Core Developer > " +
                    "Rebuild Third-Party Weapon Visuals. Existing work was not changed.");
                return false;
            }

            bool rebuildOpenTestScene = string.Equals(
                previousActiveScene.path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase);
            Scene scene = rebuildOpenTestScene
                ? previousActiveScene
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                if (rebuildOpenTestScene)
                {
                    foreach (GameObject rootObject in scene.GetRootGameObjects())
                        Object.DestroyImmediate(rootObject);
                }

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                MoveToSceneIfNeeded(floor, scene);
                floor.name = "Floor";
                floor.transform.localScale = new Vector3(1.2f, 1f, 0.65f);

                CreateDisplay("Custom Pulse Rifle", RiflePrefabOutput, -1.5f, scene);
                CreateDisplay("Energy Pistol", PistolPrefabOutput, 0f, scene);
                CreateDisplay("EMP Heavy Gun", HeavyPrefabOutput, 1.5f, scene);

                var lightObject = new GameObject("Directional Light");
                MoveToSceneIfNeeded(lightObject, scene);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

                var fillObject = new GameObject("Fill Light");
                MoveToSceneIfNeeded(fillObject, scene);
                Light fill = fillObject.AddComponent<Light>();
                fill.type = LightType.Point;
                fill.range = 8f;
                fill.intensity = 3f;
                fill.color = new Color(0.25f, 0.65f, 1f);
                fillObject.transform.position = new Vector3(0f, 2.8f, -1f);

                var cameraObject = new GameObject("Main Camera");
                MoveToSceneIfNeeded(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0f, 1.45f, -4.5f);
                cameraObject.transform.LookAt(new Vector3(0f, 0.95f, 0f));
                camera.fieldOfView = 42f;
                camera.clearFlags = CameraClearFlags.Skybox;

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Unity could not save the weapon showcase scene: " + ScenePath);
                return true;
            }
            finally
            {
                if (!rebuildOpenTestScene)
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                        SceneManager.SetActiveScene(previousActiveScene);
                }
            }
        }

        private static void MoveToSceneIfNeeded(GameObject gameObject, Scene scene)
        {
            if (gameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(gameObject, scene);
        }

        private static void CreateDisplay(string name, string prefabPath, float x, Scene scene)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new InvalidOperationException("Missing showcase prefab: " + prefabPath);

            var display = new GameObject(name);
            MoveToSceneIfNeeded(display, scene);
            display.transform.position = new Vector3(x, 0.95f, 0f);
            display.AddComponent<WeaponShowcaseSpin>();

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null) throw new InvalidOperationException("Could not instantiate: " + prefabPath);
            instance.transform.SetParent(display.transform, false);
        }

        private static Bounds CalculateLocalBounds(Transform root, Renderer[] renderers)
        {
            bool initialized = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Renderer renderer in renderers)
            {
                Bounds worldBounds = renderer.bounds;
                Vector3 center = worldBounds.center;
                Vector3 extents = worldBounds.extents;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 local = root.InverseTransformPoint(corner);
                    if (!initialized)
                    {
                        result = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(local);
                    }
                }
            }

            return result;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == objectName) return child;
            return null;
        }

        private static void AddReference(Transform parent, string name, Vector3 localPosition)
        {
            var reference = new GameObject(name);
            reference.transform.SetParent(parent, false);
            reference.transform.localPosition = localPosition;
            reference.transform.localRotation = Quaternion.identity;
        }

        private static Texture2D LoadTexture(string path)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Missing texture: " + path);
            return texture;
        }

        private static void ConfigureTexture(string path, bool sRgb, TextureImporterType textureType)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing texture importer: " + path);
            if (importer.sRGBTexture == sRgb && importer.textureType == textureType) return;
            importer.sRGBTexture = sRgb;
            importer.textureType = textureType;
            importer.SaveAndReimport();
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
