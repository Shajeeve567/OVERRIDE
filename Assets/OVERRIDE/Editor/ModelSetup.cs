// ModelSetup.cs
// Puts Ilzam's custom Blender models (Core_Dev) onto the gameplay objects, replacing placeholders.
// Gameplay colliders and scripts stay exactly as they are: only what you SEE changes.
//
//   OVERRIDE > Models > Fit Rifle To Player        PulseRifleVisual (Ilzam's rifle, normalised to +Z, with
//                                                  Grip and Muzzle markers) replaces the grey GunModel box
//   OVERRIDE > Models > Dress Terminals In Scene   TerminalVisual on each HackTerminal; placeholder hidden
//   OVERRIDE > Models > Dress Stand-in Drones      WispVisual on the Director's stand-in drone prefab
//   OVERRIDE > Models > Dress Seeker               SeekerVisual (Ilzam's Seeker, with its own weak point)
//                                                  replaces the placeholder orb on the Seeker prefab
//
// Each step can be undone (terminals: Cmd+Z) or re-run; it checks whether it's already done.

using Override.Combat;
using Override.Interaction;
using UnityEditor;
using UnityEngine;

namespace Override.EditorTools
{
    public static class ModelSetup
    {
        const string PlayerPrefab = "Assets/OVERRIDE/Prefabs/Player.prefab";
        const string RifleVisual = "Assets/_Project/Prefabs/Visuals/Weapons/PulseRifleVisual.prefab";
        const string TerminalVisual = "Assets/_Project/Prefabs/Visuals/TerminalVisual.prefab";
        const string WispVisual = "Assets/_Project/Prefabs/Visuals/WispVisual.prefab";
        const string DronePrefab = "Assets/_StandIns/Prefabs/StandInDrone.prefab";
        const string SeekerVisual = "Assets/_Project/Prefabs/Visuals/SeekerVisual.prefab";
        const string SeekerPrefab = "Assets/OVERRIDE/Prefabs/Seeker.prefab";

        // ------------------------------------------------------------------ rifle

        [MenuItem("OVERRIDE/Models/Fit Rifle To Player")]
        public static void FitRifle()
        {
            var visualAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RifleVisual);
            if (visualAsset == null) { Debug.LogWarning("[Models] Missing " + RifleVisual + " (merge Core_Dev first)."); return; }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var pivot = FindDeep(root.transform, "GunPivot");
                if (pivot == null) { Debug.LogWarning("[Models] Player has no GunPivot."); return; }
                if (FindDeep(pivot, "PulseRifleVisual") != null) { Debug.Log("[Models] Rifle model already fitted."); return; }

                // Hide the placeholder box (kept, so you can switch back by ticking it on).
                var box = FindDeep(pivot, "GunModel");
                if (box != null) box.gameObject.SetActive(false);

                var rifle = (GameObject)PrefabUtility.InstantiatePrefab(visualAsset, root.scene);
                rifle.transform.SetParent(pivot, false);
                // The visual's origin is its Grip: put the grip where the hand would be, barrel pointing +Z.
                rifle.transform.localPosition = new Vector3(0f, -0.03f, 0.05f);
                rifle.transform.localRotation = Quaternion.identity;

                // A first-person gun shouldn't cast shadows into the level or block bullets.
                foreach (var r in rifle.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                foreach (var c in rifle.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);

                // Move our Muzzle (the one RifleEffects uses) onto the model's barrel tip.
                var ourMuzzle = FindDeep(pivot, "Muzzle", exclude: rifle.transform);
                var modelMuzzle = FindDeep(rifle.transform, "Muzzle");
                if (ourMuzzle != null && modelMuzzle != null) ourMuzzle.position = modelMuzzle.position;

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
                Debug.Log("[Models] Fitted Ilzam's rifle to the Player prefab. Play and check its position; " +
                          "nudge Player > CameraRoot > Camera > GunPivot > PulseRifleVisual if needed.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ------------------------------------------------------------------ terminals

        [MenuItem("OVERRIDE/Models/Dress Terminals In Scene")]
        public static void DressTerminals()
        {
            var visualAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TerminalVisual);
            if (visualAsset == null) { Debug.LogWarning("[Models] Missing " + TerminalVisual); return; }

            int done = 0;
            foreach (var terminal in Object.FindObjectsByType<HackTerminal>(FindObjectsSortMode.None))
            {
                var t = terminal.transform;
                if (FindDeep(t, "TerminalVisual") != null) continue;
                // (No ?? here: in the Editor a missing component is a "fake null" that ?? doesn't catch.)
                var col = t.GetComponent<Collider>();
                if (col == null) col = t.GetComponentInChildren<Collider>();
                if (col == null) continue;
                Bounds placeholder = col.bounds;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualAsset, t.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(visual, "Dress terminal");
                visual.transform.SetParent(t, false);
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;

                // Same height as the placeholder, standing on the same floor.
                Bounds model = RendererBounds(visual);
                if (model.size.y > 0.001f)
                    visual.transform.localScale *= placeholder.size.y / model.size.y / Mathf.Max(0.001f, t.lossyScale.y);
                model = RendererBounds(visual);
                visual.transform.position += new Vector3(placeholder.center.x - model.center.x,
                                                         placeholder.min.y - model.min.y,
                                                         placeholder.center.z - model.center.z);
                foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

                // Hide the placeholder's own meshes; keep its collider (interaction and range use it).
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    if (!r.transform.IsChildOf(visual.transform) && !(r is LineRenderer))
                    {
                        Undo.RecordObject(r, "Hide placeholder");
                        r.enabled = false;
                    }
                done++;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[Models] Dressed {done} terminal(s) with Ilzam's TerminalVisual. Check each faces the room; " +
                      "rotate its TerminalVisual child if not. Save the scene.");
        }

        // ------------------------------------------------------------------ stand-in drones

        [MenuItem("OVERRIDE/Models/Dress Stand-in Drones")]
        public static void DressDrones()
        {
            var visualAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WispVisual);
            if (visualAsset == null) { Debug.LogWarning("[Models] Missing " + WispVisual); return; }

            var root = PrefabUtility.LoadPrefabContents(DronePrefab);
            try
            {
                if (FindDeep(root.transform, "WispVisual") != null) { Debug.Log("[Models] Drone already dressed."); return; }
                var sphere = root.GetComponent<MeshRenderer>();
                if (sphere != null) sphere.enabled = false;   // the collider stays: the rifle still hits it

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualAsset, root.scene);
                visual.transform.SetParent(root.transform, false);
                // Fit inside the stand-in's collider, so what you see is what you can shoot.
                // Collider bounds aren't valid in a prefab-editing scene, so use the sphere's own size.
                var sphereCol = root.GetComponent<SphereCollider>();
                float diameter = sphereCol != null ? 2f * sphereCol.radius * root.transform.lossyScale.x : 1f;
                Bounds model = RendererBounds(visual);
                float target = diameter * 1.6f;   // a box's diagonal is about 1.7x its side
                if (model.size.magnitude > 0.001f) visual.transform.localScale *= target / model.size.magnitude;
                model = RendererBounds(visual);
                visual.transform.position -= model.center - root.transform.position;
                foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

                PrefabUtility.SaveAsPrefabAsset(root, DronePrefab);
                Debug.Log("[Models] The Director's stand-in drones now look like Ilzam's Wisp (until Rithish's Wisp replaces them).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ------------------------------------------------------------------ Seeker

        [MenuItem("OVERRIDE/Models/Dress Seeker")]
        public static void DressSeeker()
        {
            var visualAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SeekerVisual);
            if (visualAsset == null) { Debug.LogWarning("[Models] Missing " + SeekerVisual + " (merge Ilzam's branch first)."); return; }

            var root = PrefabUtility.LoadPrefabContents(SeekerPrefab);
            try
            {
                var visualRoot = FindDeep(root.transform, "Visual");
                if (visualRoot == null) { Debug.LogWarning("[Models] Seeker prefab has no Visual child."); return; }
                if (FindDeep(visualRoot, "SeekerVisual") != null) { Debug.Log("[Models] Seeker already dressed."); return; }

                // The placeholder shell keeps its SphereCollider (body hits) but stops drawing.
                // The placeholder core goes entirely: Ilzam's model brings its own weak point.
                var shell = FindDeep(visualRoot, "Shell");
                if (shell != null && shell.TryGetComponent(out Renderer shellRenderer)) shellRenderer.enabled = false;
                var core = FindDeep(visualRoot, "SensorCore");
                if (core != null) core.gameObject.SetActive(false);

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualAsset, root.scene);
                visual.transform.SetParent(visualRoot, false);
                visual.transform.localRotation = Quaternion.identity;

                // About the placeholder's size (0.8 m sphere), centred on it.
                Bounds model = RendererBounds(visual);
                float largest = Mathf.Max(model.size.x, model.size.y, model.size.z);
                if (largest > 0.001f) visual.transform.localScale *= 0.95f / largest;
                model = RendererBounds(visual);
                visual.transform.position -= model.center - visualRoot.position;

                PrefabUtility.SaveAsPrefabAsset(root, SeekerPrefab);
                Debug.Log("[Models] Seeker now uses Ilzam's SeekerVisual. Check it faces where it flies (its sensor forward); " +
                          "rotate Seeker > Visual > SeekerVisual by 180 on Y if it flies backwards.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ------------------------------------------------------------------ helpers

        static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        static Transform FindDeep(Transform root, string name, Transform exclude = null)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name && (exclude == null || !t.IsChildOf(exclude))) return t;
            return null;
        }
    }
}
