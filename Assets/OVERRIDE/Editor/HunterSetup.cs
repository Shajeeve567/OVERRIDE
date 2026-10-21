// HunterSetup.cs
// Builds the Seeker prefab from code, so every part and number is visible here instead of being clicked
// together in the Inspector:  OVERRIDE > Hunter > Create Seeker Prefab
//
//   Seeker                 kinematic Rigidbody, Health 250 (GDD 150, see design log), HunterBody (brain + senses), HunterDebugView
//     Visual               bobbed by HunterBody
//       Shell              0.8 m red emissive sphere (GDD placeholder look); its collider takes body hits
//       SensorCore         glowing core at the front, Hitbox x2 (GDD weak point)
//
// The integration installer also calls PlaceInMaintenance and EnsureCameraSensors.

using Override.Combat;
using Override.Hunter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Override.EditorTools
{
    public static class HunterSetup
    {
        public const string PrefabPath = "Assets/OVERRIDE/Prefabs/Seeker.prefab";
        const string MaterialFolder = "Assets/OVERRIDE/Materials";
        const string CameraPrefab = "Assets/OVERRIDE/Props/Prefabs/SecurityCamera.prefab";

        [MenuItem("OVERRIDE/Hunter/Create Seeker Prefab")]
        public static GameObject CreatePrefab()
        {
            var shellMat = LitMaterial("Seeker_Shell", new Color(0.35f, 0.05f, 0.05f), new Color(0.6f, 0.05f, 0.02f));
            var coreMat = LitMaterial("Seeker_Core", new Color(0.6f, 1f, 1f), new Color(0.4f, 2.5f, 3f));

            var root = new GameObject("Seeker");
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var health = root.AddComponent<Health>();
            health.Configure(250f, false);   // GDD 150, raised to 250 to balance aim down sights; disabled on death, never respawns
            SetAmmoDrop(health);
            var hunter = root.AddComponent<HunterBody>();
            root.AddComponent<HunterDebugView>();

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "Shell";
            shell.transform.SetParent(visual, false);
            shell.transform.localScale = Vector3.one * 0.8f;
            shell.GetComponent<Renderer>().sharedMaterial = shellMat;

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "SensorCore";
            core.transform.SetParent(visual, false);
            core.transform.localPosition = new Vector3(0f, 0f, 0.38f);   // pokes out of the shell's front
            core.transform.localScale = Vector3.one * 0.3f;
            core.GetComponent<Renderer>().sharedMaterial = coreMat;
            core.AddComponent<Hitbox>();   // x2 by default

            var so = new SerializedObject(hunter);
            so.FindProperty("visual").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder("Assets/OVERRIDE/Prefabs");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[Hunter] Saved {PrefabPath}");
            return prefab;
        }

        const string AmmoDropPrefab = "Assets/OVERRIDE/Prefabs/AmmoDrop.prefab";

        /// GDD: destroyed units drop ammo. Returns false if the AmmoDrop prefab is missing.
        static bool SetAmmoDrop(Health health)
        {
            var drop = AssetDatabase.LoadAssetAtPath<GameObject>(AmmoDropPrefab);
            if (drop == null) return false;
            var so = new SerializedObject(health);
            so.FindProperty("dropOnDeath").objectReferenceValue = drop;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// Brings an existing Seeker prefab up to date (ammo drop on death). Returns a log line.
        public static string UpgradePrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) return "  = no Seeker prefab yet";
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var health = root.GetComponent<Health>();
                var so = new SerializedObject(health);
                if (so.FindProperty("dropOnDeath").objectReferenceValue != null) return "  = Seeker already drops ammo";
                if (!SetAmmoDrop(health)) return "  ! no " + AmmoDropPrefab + ": Seeker drops nothing";
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return "  + Seeker now drops an AmmoDrop on death";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// Puts one Seeker in Maintenance (GDD start zone) if the scene has none. Returns a log line.
        public static string PlaceInMaintenance()
        {
            if (Object.FindFirstObjectByType<HunterBody>() != null) return "  = a Seeker is already in the scene";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? CreatePrefab();
            var floor = GameObject.Find("OVERRIDE_Greybox/Maintenance/Floor");
            var col = floor != null ? floor.GetComponent<Collider>() : null;
            Vector3 at = col != null ? col.bounds.center + Vector3.up * (col.bounds.extents.y + 1.2f) : Vector3.up * 1.2f;

            var seeker = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(seeker, "Place Seeker");
            seeker.transform.position = at;
            return col != null ? $"  + Seeker placed in Maintenance at {at}"
                               : "  ! no Maintenance floor found: Seeker placed at the origin, move it";
        }

        /// Gives Shajeeve's SecurityCamera prop a sensor, so all copies in the level start watching.
        public static string EnsureCameraSensors()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CameraPrefab) == null) return "  ! missing " + CameraPrefab;
            var root = PrefabUtility.LoadPrefabContents(CameraPrefab);
            try
            {
                if (root.GetComponent<SecurityCameraSensor>() != null) return "  = SecurityCameraSensor already on the camera prefab";
                root.AddComponent<SecurityCameraSensor>();
                PrefabUtility.SaveAsPrefabAsset(root, CameraPrefab);
                return "  + SecurityCameraSensor on " + CameraPrefab;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Material LitMaterial(string name, Color baseColour, Color emission)
        {
            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", baseColour);
            m.SetFloat("_Smoothness", 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
