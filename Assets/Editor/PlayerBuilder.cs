// PlayerBuilder.cs
// Rebuilds the player "bean" from the old Test project as a prefab, then drops it into the open scene.
// Run it from the menu bar: OVERRIDE > Create Player Prefab.
//
// WHY A PREFAB: a prefab is a saved, reusable GameObject. The workplan says everyone hands the
// World Builder prefabs and only the World Builder edits the main scene, so the player should be
// a prefab that any scene (your greybox, teammates' sandboxes, the main scene) can use.
//
// Every value below was read from Test/Assets/Scenes/GameScene.unity, so the rebuilt player
// behaves like the old one. The two deliberate changes are marked CHANGED.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Override.EditorTools
{
    public static class PlayerBuilder
    {
        // ------------------------------------------------------------------
        // WHERE THINGS ARE
        // ------------------------------------------------------------------
        const string PrefabFolder     = "Assets/OVERRIDE/Prefabs";
        const string PrefabPath       = PrefabFolder + "/Player.prefab";
        const string MaterialFolder   = "Assets/OVERRIDE/Materials";
        const string MaterialPath     = MaterialFolder + "/Player_Bean.mat";
        const string InputActionsPath = "Assets/Input/PlayerInput.inputactions";
        const string GunPrefabPath    = "Assets/LowPolyWeapons_LITE/Prefabs/AR.prefab";

        // ------------------------------------------------------------------
        // VALUES FROM THE OLD SCENE
        // ------------------------------------------------------------------
        const float MoveSpeed        = 10f;
        const float JumpForce        = 5f;
        const float GroundDistance   = 1f;   // radius of the ground-check sphere at the feet
        const float MouseSensitivity = 1.2f;
        const float GunFireRate      = 10f;  // shots per second (the AR prefab's own default is 1)

        static readonly Vector3 GroundCheckOffset = new Vector3(0f, -1f, 0f);    // bottom of a 2 m capsule
        static readonly Vector3 CameraOffset      = new Vector3(0f, 0.6f, 0f);   // eye height above the centre
        static readonly Vector3 GunOffset         = new Vector3(0.34f, -0.3f, 0.524f); // right, down, forward
        static readonly Vector3 GunRotation       = new Vector3(0f, 90f, 0f);
        static readonly Vector3 BulletSpawnOffset = new Vector3(-0.721f, 0.031f, 0.063f); // the muzzle

        // Where the player goes if the greybox level is open: inside the airlock, feet on the floor.
        static readonly Vector3 AirlockSpawn = new Vector3(30f, 1f, 3f);

        // ==================================================================
        [MenuItem("OVERRIDE/Create Player Prefab")]
        static void Build()
        {
            // Load the assets the player depends on first, and stop with a clear message if any is missing.
            var actions   = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            var gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GunPrefabPath);
            if (actions == null || gunPrefab == null)
            {
                Debug.LogError("[Player] Missing " + (actions == null ? InputActionsPath : GunPrefabPath) +
                               ". Copy it from the Test project together with its .meta file.");
                return;
            }

            // The ground check finds the Ground layer BY NAME, not by number. In the old project Ground
            // was layer 6; here it may be a different slot. Asking by name avoids that mismatch.
            int groundLayer = EnsureLayer("Ground");

            // ---------------- Root: the bean ----------------
            // Unity's capsule primitive is 2 m tall and 0.5 m in radius, the same size as the old one,
            // and it comes with a CapsuleCollider already.
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.tag  = "Player";
            player.GetComponent<MeshRenderer>().sharedMaterial = LoadOrCreateMaterial();

            // CHANGED: the old player collided with a BoxCollider, and its CapsuleCollider was a trigger
            // (so it never blocked anything). A box's corners snag on walls and twist the body. A capsule
            // slides along walls. So this player only has the capsule, as a normal solid collider.

            var rb = player.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.constraints = RigidbodyConstraints.FreezeRotation; // physics may never spin the bean
            // CHANGED: interpolation smooths the camera. The body moves in FixedUpdate (50 times a second)
            // but the screen draws more often; without interpolation the view stutters between steps.
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // ---------------- Children ----------------
            var groundCheck = NewChild("GroundCheck", player.transform, GroundCheckOffset);

            var cameraT = NewChild("Main Camera", player.transform, CameraOffset);
            cameraT.gameObject.tag = "MainCamera"; // lets other scripts find it with Camera.main
            var cam = cameraT.gameObject.AddComponent<Camera>(); // field of view 60, the default, as before
            cameraT.gameObject.AddComponent<AudioListener>();   // the player's "ears"

            // The gun hangs off the camera, so it turns up and down with the view.
            var gunHolder = NewChild("GunHolder", cameraT, Vector3.zero);

            // InstantiatePrefab (not Instantiate) keeps the link to AR.prefab, so the gun becomes a
            // NESTED prefab: edit AR.prefab later and the player's gun updates too.
            var gunObject = (GameObject)PrefabUtility.InstantiatePrefab(gunPrefab, gunHolder);
            gunObject.transform.localPosition = GunOffset;
            gunObject.transform.localRotation = Quaternion.Euler(GunRotation);

            var gun = gunObject.GetComponentInChildren<Gun>();
            gun.fireRate = GunFireRate;
            gun.bulletSpawnPoint.localPosition = BulletSpawnOffset;

            // Changing a prefab instance from code doesn't automatically count as an "override".
            // These calls tell Unity to remember the changes, otherwise they'd be lost on save.
            PrefabUtility.RecordPrefabInstancePropertyModifications(gunObject.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(gun);
            PrefabUtility.RecordPrefabInstancePropertyModifications(gun.bulletSpawnPoint);

            // ---------------- Your scripts ----------------
            // All their fields are public, so they can be set directly, exactly as the Inspector would.
            var movement = player.AddComponent<PlayerMovement>();
            movement.moveSpeed      = MoveSpeed;
            movement.jumpForce      = JumpForce;
            movement.groundCheck    = groundCheck;
            movement.groundDistance = GroundDistance;
            movement.groundMask     = 1 << groundLayer; // a LayerMask is a set of bits, one per layer

            var look = player.AddComponent<PlayerLook>();
            look.mouseSensitivity = MouseSensitivity;
            look.cam = cameraT;

            var shooting = player.AddComponent<PlayerShooting>();
            shooting.gun = gun;

            // ---------------- Input ----------------
            // The FULL name UnityEngine.InputSystem.PlayerInput is needed here. Your project also has a
            // generated class called PlayerInput (Assets/Input/PlayerInput.cs, made from the
            // .inputactions file). Plain "PlayerInput" would mean that generated class, not the component.
            var input = player.AddComponent<UnityEngine.InputSystem.PlayerInput>();
            input.actions = actions;
            input.notificationBehavior = PlayerNotifications.SendMessages; // calls OnMovement, OnJump, OnShoot...
            input.defaultActionMap = "OnFoot";
            input.camera = cam;

            // ---------------- Save as a prefab ----------------
            EnsureFolder(PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(player, PrefabPath, out bool saved);
            Object.DestroyImmediate(player); // the temporary build object is no longer needed
            if (!saved)
            {
                Debug.LogError("[Player] Could not save " + PrefabPath);
                return;
            }

            PlaceInOpenScene(prefab);
            Debug.Log("[Player] Player prefab saved to " + PrefabPath);
        }

        // Drops one copy of the prefab into whatever scene is open, so you can press Play straight away.
        static void PlaceInOpenScene(GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            bool greyboxOpen = GameObject.Find("OVERRIDE_Greybox") != null;
            instance.transform.position = greyboxOpen ? AirlockSpawn : Vector3.up;
            Undo.RegisterCreatedObjectUndo(instance, "Place Player"); // Ctrl+Z removes it again

            // A scene should have one camera and one AudioListener. The player brings its own, so any
            // other camera already in the scene (like the greybox scene's default one) is switched off.
            // Switched off, not deleted, and Ctrl+Z brings it back.
            foreach (var root in instance.scene.GetRootGameObjects())
            {
                if (root == instance) continue;
                foreach (var other in root.GetComponentsInChildren<Camera>())
                {
                    Undo.RecordObject(other.gameObject, "Disable scene camera");
                    other.gameObject.SetActive(false);
                    Debug.Log("[Player] Switched off '" + other.name + "': the player has its own camera.");
                }
            }

            EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;
        }

        static Transform NewChild(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        // Teal: the GDD's facility accent colour, so the player stands out against grey walls.
        static Material LoadOrCreateMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            var teal = new Color(0.18f, 0.77f, 0.71f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", teal);
            else mat.color = teal;

            EnsureFolder(MaterialFolder);
            AssetDatabase.CreateAsset(mat, MaterialPath);
            return mat;
        }

        // Creates "Assets/A/B/C" one folder at a time, because CreateFolder only makes one level.
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        // Returns the Ground layer's number, creating the layer if the project doesn't have it yet.
        // Layers live in ProjectSettings/TagManager.asset; slots 0-7 are reserved by Unity.
        static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing != -1) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = name;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    return i;
                }
            }
            Debug.LogWarning("[Player] No free layer slot for '" + name + "'. Ground check will use Default.");
            return 0;
        }
    }
}
