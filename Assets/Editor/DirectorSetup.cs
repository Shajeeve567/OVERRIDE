// DirectorSetup.cs
// OVERRIDE > Add Director To Scene: puts one "ARGUS_Director" object with a DirectorController in the
// open scene (or selects the existing one). The Director is abstract in the GDD (no body), so it's
// just an empty GameObject carrying the script.

using Override.Director;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Override.EditorTools
{
    public static class DirectorSetup
    {
        [MenuItem("OVERRIDE/Add Director To Scene")]
        static void Add()
        {
            var existing = Object.FindFirstObjectByType<DirectorController>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[Director] The scene already has one: selected it.");
                return;
            }

            var go = new GameObject("ARGUS_Director");
            go.AddComponent<DirectorController>();
            Undo.RegisterCreatedObjectUndo(go, "Add Director");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);

            if (Object.FindObjectsByType<Override.Level.DroneBay>(FindObjectsSortMode.None).Length == 0)
                Debug.LogWarning("[Director] No drone bays in this scene yet: run OVERRIDE > Place Drone Bays.");
            if (GameObject.FindWithTag("Player") == null)
                Debug.LogWarning("[Director] No Player in this scene yet: run OVERRIDE > Create Player Prefab.");
        }
    }
}
