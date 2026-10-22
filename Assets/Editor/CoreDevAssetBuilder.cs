using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Override.EditorTools
{
    /// <summary>
    /// Builds reusable art-only prefabs from the Core Developer's FBX files.
    /// Gameplay brains and motors belong on the Agent Controller's outer prefab.
    /// </summary>
    public static class CoreDevAssetBuilder
    {
        private const string ArtRoot = "Assets/_Project/Art";
        private const string ModelRoot = ArtRoot + "/Models";
        private const string MaterialRoot = ArtRoot + "/Materials";
        private const string PrefabRoot = "Assets/_Project/Prefabs/Visuals";
        private const string AutoBuildKey = "Override.CoreDevAssets.AutoBuild.v1";

        private static readonly string[] SharedMaterialNames =
        {
            "Armor", "Dark", "Red", "Rubber", "Screen", "Steel", "Teal"
        };

        [InitializeOnLoadMethod]
        private static void ScheduleFirstBuild()
        {
            EditorApplication.delayCall += BuildOnceAfterImport;
        }

        [MenuItem("OVERRIDE/Core Developer/Rebuild Visual Assets")]
        public static void Build()
        {
            EnsureFolder(MaterialRoot);
            EnsureFolder(PrefabRoot);

            ConfigureTexture("Warden/Textures/Warden_Metallic.png", false);
            ConfigureTexture("Warden/Textures/Warden_Roughness.png", false);
            ConfigureTexture("Rifle/Textures/Rifle_Metallic.png", false);
            ConfigureTexture("Rifle/Textures/Rifle_Roughness.png", false);

            Material warden = CreateLitMaterial(
                "Warden_Atlas",
                TexturePath("Warden", "Warden_BaseColor.png"),
                TexturePath("Warden", "Warden_Metallic.png"),
                TexturePath("Warden", "Warden_Emission.png"),
                1f,
                0.42f,
                new Color(0.2f, 1.1f, 1.3f));

            Material rifle = CreateLitMaterial(
                "Rifle_Atlas",
                TexturePath("Rifle", "Rifle_BaseColor.png"),
                TexturePath("Rifle", "Rifle_Metallic.png"),
                null,
                1f,
                0.38f,
                Color.black);

            Dictionary<string, Material> terminal = CreatePaletteMaterials(
                "Terminal",
                TexturePath("Terminal", "Terminal_Palette.png"),
                TexturePath("Terminal", "Terminal_Screen.png"));

            Dictionary<string, Material> wisp = CreatePaletteMaterials(
                "Wisp",
                TexturePath("Wisp", "Wisp_Palette.png"),
                TexturePath("Wisp", "Wisp_Identity.png"));

            RemapMaterials("Warden", new Dictionary<string, Material> { { "Warden_Atlas", warden } });
            RemapMaterials("Rifle", new Dictionary<string, Material> { { "Rifle_Atlas", rifle } });
            RemapMaterials("Terminal", terminal);
            RemapMaterials("Wisp", wisp);

            BuildVisualPrefab("Rifle", false);
            BuildVisualPrefab("Terminal", false);
            BuildVisualPrefab("Wisp", false);
            BuildVisualPrefab("Warden", true);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Core Developer] Built URP materials and four visual prefabs.");
        }

        private static void BuildOnceAfterImport()
        {
            if (SessionState.GetBool(AutoBuildKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + "/WardenVisual.prefab") != null) return;

            SessionState.SetBool(AutoBuildKey, true);
            try
            {
                Build();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("[Core Developer] Automatic asset build failed. Use OVERRIDE > Core Developer > Rebuild Visual Assets after fixing the error.");
            }
        }

        private static Dictionary<string, Material> CreatePaletteMaterials(
            string model,
            string palettePath,
            string screenPath)
        {
            var result = new Dictionary<string, Material>();
            foreach (string sourceName in SharedMaterialNames)
            {
                bool emissive = sourceName == "Red" || sourceName == "Teal" || sourceName == "Screen";
                string baseMap = sourceName == "Screen" ? screenPath : palettePath;
                Color emission = sourceName == "Red"
                    ? new Color(1.4f, 0.08f, 0.04f)
                    : new Color(0.05f, 1.25f, 1.5f);
                float metallic = sourceName == "Steel" ? 0.8f : sourceName == "Rubber" ? 0.05f : 0.35f;
                float smoothness = sourceName == "Rubber" ? 0.18f : sourceName == "Dark" ? 0.28f : 0.48f;

                result[sourceName] = CreateLitMaterial(
                    model + "_" + sourceName,
                    baseMap,
                    null,
                    emissive ? baseMap : null,
                    metallic,
                    smoothness,
                    emissive ? emission : Color.black);
            }
            return result;
        }

        private static Material CreateLitMaterial(
            string assetName,
            string baseMapPath,
            string metallicMapPath,
            string emissionMapPath,
            float metallic,
            float smoothness,
            Color emissionColor)
        {
            string assetPath = MaterialRoot + "/" + assetName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");

            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseMapPath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);

            Texture2D metallicMap = string.IsNullOrEmpty(metallicMapPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(metallicMapPath);
            material.SetTexture("_MetallicGlossMap", metallicMap);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", metallicMap != null);

            Texture2D emissionMap = string.IsNullOrEmpty(emissionMapPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(emissionMapPath);
            material.SetTexture("_EmissionMap", emissionMap);
            material.SetColor("_EmissionColor", emissionMap == null ? Color.black : emissionColor);
            SetKeyword(material, "_EMISSION", emissionMap != null);
            material.globalIlluminationFlags = emissionMap == null
                ? MaterialGlobalIlluminationFlags.None
                : MaterialGlobalIlluminationFlags.RealtimeEmissive;

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureTexture(string relativePath, bool sRgb)
        {
            string path = ModelRoot + "/" + relativePath;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.sRGBTexture == sRgb) return;
            importer.sRGBTexture = sRgb;
            importer.SaveAndReimport();
        }

        private static void RemapMaterials(string model, Dictionary<string, Material> remaps)
        {
            string path = ModelRoot + "/" + model + "/" + model + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing model importer: " + path);

            foreach (KeyValuePair<string, Material> remap in remaps)
            {
                var source = new AssetImporter.SourceAssetIdentifier(typeof(Material), remap.Key);
                importer.AddRemap(source, remap.Value);
            }
            importer.SaveAndReimport();
        }

        private static void BuildVisualPrefab(string model, bool addWeakPoints)
        {
            string modelPath = ModelRoot + "/" + model + "/" + model + ".fbx";
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null) throw new InvalidOperationException("Missing model asset: " + modelPath);

            var root = new GameObject(model + "Visual");
            GameObject instance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
            if (instance == null) throw new InvalidOperationException("Could not instantiate " + modelPath);

            instance.name = model + "_Model";
            instance.transform.SetParent(root.transform, false);
            if (addWeakPoints) AddWardenWeakPoints(instance.transform);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabRoot + "/" + model + "Visual.prefab");
            Object.DestroyImmediate(root);
        }

        private static void AddWardenWeakPoints(Transform modelRoot)
        {
            AddWeakPoint(modelRoot, "Turret_Sensor_Eye", WardenWeakPoint.WeakPointType.SensorEye, 2f);
            AddWeakPoint(modelRoot, "Turret_Rear_Heat_Core", WardenWeakPoint.WeakPointType.RearVent, 2f);
        }

        private static void AddWeakPoint(
            Transform modelRoot,
            string objectName,
            WardenWeakPoint.WeakPointType type,
            float multiplier)
        {
            Transform target = FindDescendant(modelRoot, objectName);
            if (target == null) throw new InvalidOperationException("Warden weak-point mesh not found: " + objectName);

            MeshFilter filter = target.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Warden weak-point has no mesh: " + objectName);

            MeshCollider collider = target.GetComponent<MeshCollider>();
            if (collider == null) collider = target.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = true;

            WardenWeakPoint weakPoint = target.GetComponent<WardenWeakPoint>();
            if (weakPoint == null) weakPoint = target.gameObject.AddComponent<WardenWeakPoint>();
            weakPoint.Configure(type, multiplier);
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == objectName) return child;
            return null;
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        private static string TexturePath(string model, string fileName)
        {
            return ModelRoot + "/" + model + "/Textures/" + fileName;
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
