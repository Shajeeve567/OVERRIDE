// STAND-IN (delete with the _StandIns folder).
// OVERRIDE > Stand-ins > Set Up Test Game      : everything needed to play-test the Director today.
// OVERRIDE > Stand-ins > Remove From Scene     : undoes the scene part (then delete Assets/_StandIns).

using Override.Director;
using Override.StandIns;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Override.EditorTools
{
    public static class StandInSetup
    {
        const string RootName = "_STANDINS";
        const string PrefabFolder = "Assets/_StandIns/Prefabs";
        const string DronePrefabPath = PrefabFolder + "/StandInDrone.prefab";
        const string Mats = "Assets/OVERRIDE/Props/Materials/";

        [MenuItem("OVERRIDE/Stand-ins/Set Up Test Game")]
        static void SetUp()
        {
            if (GameObject.Find("OVERRIDE_Greybox") == null) { Debug.LogError("[StandIns] Open Greybox_v2 first."); return; }
            if (GameObject.FindWithTag("Player") == null) { Debug.LogError("[StandIns] No Player: run OVERRIDE > Create Player Prefab first."); return; }

            // 1. The Director (the real one, not a stand-in).
            var director = Object.FindFirstObjectByType<DirectorController>();
            if (director == null)
            {
                director = new GameObject("ARGUS_Director").AddComponent<DirectorController>();
                Undo.RegisterCreatedObjectUndo(director.gameObject, "Set Up Test Game");
            }

            // 2. The stand-in drone prefab, plugged into the Director's drone slot.
            Undo.RecordObject(director, "Set Up Test Game");
            director.dronePrefab = LoadOrCreateDronePrefab();

            // 3. The one scene object that builds everything else at Play time.
            var root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Set Up Test Game");
            }
            // (Not "??": Unity objects can be "fake null", which ?? doesn't understand. An if does.)
            var game = root.GetComponent<StandInGame>();
            if (game == null) game = root.AddComponent<StandInGame>();
            game.doorMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_DarkMetal.mat");
            game.freeMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_TealGlow.mat");
            game.directorLockMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_RedGlow.mat");
            game.sealedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_Hazard.mat");
            EditorUtility.SetDirty(game);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("[StandIns] Test game ready. Press Play. Hold E at terminals; the Director panel is top-left (F3).");
        }

        [MenuItem("OVERRIDE/Stand-ins/Remove From Scene")]
        static void Remove()
        {
            var root = GameObject.Find(RootName);
            if (root != null) Undo.DestroyObjectImmediate(root);

            var director = Object.FindFirstObjectByType<DirectorController>();
            var standInDrone = AssetDatabase.LoadAssetAtPath<GameObject>(DronePrefabPath);
            if (director != null && director.dronePrefab != null && director.dronePrefab == standInDrone)
            {
                Undo.RecordObject(director, "Remove Stand-ins");
                director.dronePrefab = null;   // assign the real Wisp prefab here
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[StandIns] Removed from the scene. Now delete the Assets/_StandIns folder (see its README).");
        }

        // A red sphere: collider (so bullets hit it), kinematic Rigidbody (so collisions are reported
        // to it while it's moved by script), and the StandInDrone brain.
        static GameObject LoadOrCreateDronePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DronePrefabPath);
            if (existing != null) return existing;
            if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/_StandIns", "Prefabs");

            var drone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            drone.name = "StandInDrone";
            drone.transform.localScale = Vector3.one * 0.7f;
            drone.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_RedGlow.mat");
            var body = drone.AddComponent<Rigidbody>();
            body.isKinematic = true;
            drone.AddComponent<StandInDrone>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(drone, DronePrefabPath);
            Object.DestroyImmediate(drone);
            return prefab;
        }
    }
}
