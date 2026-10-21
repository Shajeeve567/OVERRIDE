// IntegrationSetup.cs
// One menu item that installs the Systems Engineer's real gameplay into Shajeeve's level, replacing the
// jobs his Play-mode stand-ins did. Run it with Greybox_v2 open:  OVERRIDE > Integration > Install Team Systems
//
// It is safe to run again: every step checks whether it's already done.
//
//   PREFABS (all instances update at once)
//     CrateBarricade  + CrateBarricade (mass 0 = keep each Rigidbody's mass, so bay crates stay light)
//     HealthStation   + HealthStation
//     StandInDrone    + Health (60 HP, destroyed on death) so the hitscan rifle can kill it
//   SCENE
//     Player          Shajeeve's player swapped for Gunitha's Player prefab, same spot
//     Terminals       HackTerminal + progress ring on the four placeholders (Core: 30 s, needs T1-T3)
//     Doors           a SlidingDoor + LockableDoor in each main corridor (same table as the stand-in doors)
//     Director        DirectorBridge, and the stand-in drone as its drone prefab until the Wisp exists
//     Ammo crates     one beside each terminal (GDD: "ammo crates at each terminal"), prefab built if missing
//     Cameras         SecurityCameraSensor on Shajeeve's camera prefab (sightings for the Seeker)
//     Seeker          one Seeker in Maintenance, built by HunterSetup if the prefab doesn't exist yet
//     Wardens         Ilzam's WardenGameplay: 1 guarding the Labs terminal, 2 the Server Hall (GDD Section 5),
//                     each with current / advance / flank / retreat cover points
//     Game flow       pause, death + respawn, victory screens (GameFlow, Shajeeve's UI style)
//     Debug           NoiseDebugView + ObjectivesOverlay
//     _STANDINS       removed if present (it would add duplicate health, terminals and doors)

using Override.Combat;
using Override.Core;
using Override.Director;
using Override.Integration;
using Override.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Override.EditorTools
{
    public static class IntegrationSetup
    {
        const string PlayerPrefab = "Assets/OVERRIDE/Prefabs/Player.prefab";
        const string CratePrefab = "Assets/OVERRIDE/Props/Prefabs/CrateBarricade.prefab";
        const string HealthStationPrefab = "Assets/OVERRIDE/Props/Prefabs/HealthStation.prefab";
        const string DronePrefab = "Assets/_StandIns/Prefabs/StandInDrone.prefab";
        const string Mats = "Assets/OVERRIDE/Props/Materials/";

        static readonly (string name, ZoneId zone, bool isCore)[] Terminals =
        {
            ("Terminal_T1 (placeholder)", ZoneId.Labs, false),
            ("Terminal_T2 (placeholder)", ZoneId.Maintenance, false),
            ("Terminal_T3 (placeholder)", ZoneId.ServerHall, false),
            ("Core_OverrideConsole (placeholder)", ZoneId.Core, true),
        };

        // Same corridors, zones and seals as Shajeeve's StandInGame. Vents and the tunnel's twin stay open.
        static readonly (string corridor, string zoneA, string zoneB, bool sealedAtStart)[] Doors =
        {
            ("Airlock_Labs", "Airlock", "Labs", false),
            ("Labs_ServerHall", "Labs", "ServerHall", false),
            ("Labs_Maintenance", "Labs", "Maintenance", false),
            ("ServerHall_Maintenance", "ServerHall", "Maintenance", false),
            ("ServerHall_Core (door A)", "ServerHall", "Core", true),
            ("Tunnel_Maintenance_Core", "Maintenance", "Core", true),
        };

        [MenuItem("OVERRIDE/Integration/Install Team Systems In Open Scene")]
        public static void Install()
        {
            if (GameObject.Find("OVERRIDE_Greybox") == null)
            {
                EditorUtility.DisplayDialog("Integration", "Open Greybox_v2 (the scene with OVERRIDE_Greybox) first.", "OK");
                return;
            }

            Physics.SyncTransforms();   // collider bounds must match the transforms we read below
            var log = new System.Text.StringBuilder("[Integration]\n");

            AddToPrefab<CrateBarricade>(CratePrefab, log, c =>
            {
                var so = new SerializedObject(c);
                so.FindProperty("mass").floatValue = 0f;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            AddToPrefab<HealthStation>(HealthStationPrefab, log, null);
            AddToPrefab<Health>(DronePrefab, log, h => h.Configure(60f, true));

            RemoveStandIns(log);
            SwapPlayer(log);
            InstallTerminals(log);
            InstallAmmoCrates(log);
            InstallDoors(log);
            InstallDirector(log);
            log.AppendLine(HunterSetup.EnsureCameraSensors());
            log.AppendLine(HunterSetup.UpgradePrefab());
            log.AppendLine(HunterSetup.PlaceInMaintenance());
            InstallWardens(log);
            InstallGameFlow(log);
            InstallDebug(log);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(log.ToString());
            EditorUtility.DisplayDialog("Integration", "Done. Check the Console for details, then save the scene (Cmd+S).", "OK");
        }

        /// For teammates' test scenes: only replaces the player (movement, rifle, health), nothing else.
        [MenuItem("OVERRIDE/Integration/Swap In Gunitha's Player Only")]
        public static void SwapPlayerOnly()
        {
            var log = new System.Text.StringBuilder("[Integration]\n");
            SwapPlayer(log);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(log.ToString());
        }

        // ------------------------------------------------------------------ prefabs

        static void AddToPrefab<T>(string path, System.Text.StringBuilder log, System.Action<T> setup) where T : Component
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { log.AppendLine($"  ! missing prefab {path}"); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<T>() != null) { log.AppendLine($"  = {typeof(T).Name} already on {path}"); return; }
                var c = root.AddComponent<T>();
                setup?.Invoke(c);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.AppendLine($"  + {typeof(T).Name} on {path}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ------------------------------------------------------------------ scene

        static void RemoveStandIns(System.Text.StringBuilder log)
        {
            var standIns = GameObject.Find("_STANDINS");
            if (standIns == null) return;
            Undo.DestroyObjectImmediate(standIns);
            log.AppendLine("  - removed _STANDINS (its health, terminals and doors are now the real ones)");
        }

        static void SwapPlayer(System.Text.StringBuilder log)
        {
            var ours = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            var current = GameObject.FindWithTag("Player");
            if (current != null && PrefabUtility.GetCorrespondingObjectFromSource(current) == ours)
            {
                log.AppendLine("  = Player is already Gunitha's prefab");
                return;
            }

            var player = (GameObject)PrefabUtility.InstantiatePrefab(ours, EditorSceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(player, "Install player");
            if (current != null)
            {
                player.transform.SetParent(current.transform.parent, false);
                player.transform.position = current.transform.position;
                player.transform.rotation = Quaternion.Euler(0f, current.transform.eulerAngles.y, 0f);
                Undo.DestroyObjectImmediate(current);
                log.AppendLine("  + swapped Shajeeve's player for Gunitha's Player prefab");
            }
            else log.AppendLine("  + placed Gunitha's Player prefab at the origin (no old player found: move it to the Airlock)");
        }

        static void InstallTerminals(System.Text.StringBuilder log)
        {
            var props = GameObject.Find("OVERRIDE_Props");
            if (props == null) { log.AppendLine("  ! no OVERRIDE_Props: terminals skipped"); return; }
            var all = props.GetComponentsInChildren<Transform>(true);

            foreach (var entry in Terminals)
            {
                Transform t = null;
                foreach (var x in all) if (x.name == entry.name) { t = x; break; }
                if (t == null) { log.AppendLine($"  ! missing {entry.name}"); continue; }
                if (t.GetComponent<HackTerminal>() != null) { log.AppendLine($"  = {entry.zone} terminal already set up"); continue; }

                var terminal = Undo.AddComponent<HackTerminal>(t.gameObject);

                // Ring floating above the terminal, at world scale 1 even if the prop is scaled.
                var ringGo = new GameObject("HackRing");
                Undo.RegisterCreatedObjectUndo(ringGo, "Install terminal ring");
                ringGo.transform.SetParent(t, false);
                var col = t.GetComponentInChildren<Collider>();
                Vector3 top = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y, col.bounds.center.z) : t.position;
                ringGo.transform.position = top + Vector3.up * 0.6f;
                Vector3 s = t.lossyScale;
                ringGo.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
                var ring = ringGo.AddComponent<ProgressRing>();

                terminal.Configure(entry.zone, entry.isCore, entry.isCore ? 30f : 8f, ring);
                EditorUtility.SetDirty(terminal);
                log.AppendLine($"  + HackTerminal on {entry.name} ({entry.zone}{(entry.isCore ? ", 30 s, needs T1-T3" : "")})");
            }
        }

        const string AmmoCratePrefab = "Assets/OVERRIDE/Prefabs/AmmoCrate.prefab";

        /// A small metal ammo case with a teal glowing lid strip (hidden when it's empty), built from code.
        static GameObject BuildAmmoCratePrefab()
        {
            var metal = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_Metal.mat");
            var glow = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_TealGlow.mat");

            var root = new GameObject("AmmoCrate");
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.35f, 0f);
            box.size = new Vector3(1.0f, 0.7f, 0.7f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Case";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            body.transform.localScale = new Vector3(1.0f, 0.7f, 0.7f);
            if (metal != null) body.GetComponent<Renderer>().sharedMaterial = metal;

            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "AvailableStrip";
            Object.DestroyImmediate(strip.GetComponent<Collider>());
            strip.transform.SetParent(root.transform, false);
            strip.transform.localPosition = new Vector3(0f, 0.71f, 0f);
            strip.transform.localScale = new Vector3(0.8f, 0.03f, 0.2f);
            if (glow != null) strip.GetComponent<Renderer>().sharedMaterial = glow;

            var crate = root.AddComponent<AmmoCrate>();
            var so = new SerializedObject(crate);
            so.FindProperty("availableVisual").objectReferenceValue = strip;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, AmmoCratePrefab);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void InstallAmmoCrates(System.Text.StringBuilder log)
        {
            if (Object.FindFirstObjectByType<AmmoCrate>() != null) { log.AppendLine("  = ammo crates already placed"); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AmmoCratePrefab) ?? BuildAmmoCratePrefab();

            var root = new GameObject("OVERRIDE_AmmoCrates").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Install ammo crates");
            foreach (var terminal in Object.FindObjectsByType<HackTerminal>(FindObjectsSortMode.None))
            {
                // Beside the terminal (its right), on the floor under it, facing the same way.
                var t = terminal.transform;
                var col = t.GetComponentInChildren<Collider>();
                float floorY = col != null ? col.bounds.min.y : t.position.y;
                Vector3 side = new Vector3(t.right.x, 0f, t.right.z).normalized;
                float offset = (col != null ? col.bounds.extents.magnitude : 1f) + 1.0f;
                Vector3 at = new Vector3(t.position.x, floorY, t.position.z) + side * offset;

                var crate = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
                crate.transform.SetParent(root, false);
                crate.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, t.eulerAngles.y, 0f));
                crate.name = $"AmmoCrate ({terminal.Zone})";
                log.AppendLine($"  + {crate.name} beside the {terminal.Zone} terminal");
            }
        }

        static void InstallDoors(System.Text.StringBuilder log)
        {
            if (GameObject.Find("OVERRIDE_Doors") != null) { log.AppendLine("  = OVERRIDE_Doors already exists (delete it to rebuild)"); return; }
            var corridors = GameObject.Find("OVERRIDE_Greybox/Corridors");
            if (corridors == null) { log.AppendLine("  ! no OVERRIDE_Greybox/Corridors: doors skipped"); return; }

            var slabMat = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_DarkMetal.mat");
            var freeMat = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_TealGlow.mat");
            var lockMat = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_RedGlow.mat");
            var sealMat = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Prop_Hazard.mat");

            var root = new GameObject("OVERRIDE_Doors").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Install doors");

            int id = 0;
            foreach (var entry in Doors)
            {
                var c = corridors.transform.Find(entry.corridor);
                var floor = c != null ? c.Find("Floor") : null;
                var floorCol = floor != null ? floor.GetComponent<Collider>() : null;
                if (floorCol == null) { log.AppendLine($"  ! no corridor floor for {entry.corridor}"); continue; }

                Bounds f = floorCol.bounds;
                float wallTop = f.max.y;
                foreach (var col in c.GetComponentsInChildren<Collider>())
                    if (col.transform.name.StartsWith("Wall")) wallTop = Mathf.Max(wallTop, col.bounds.max.y);
                float height = wallTop - f.max.y;
                bool alongZ = RunsAlongZ(entry.zoneA, entry.zoneB, f);
                float width = alongZ ? f.size.x : f.size.z;

                // The slab IS the door: SlidingDoor needs its Rigidbody and collider on the same object.
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = $"Door_{id} {entry.zoneA}-{entry.zoneB}";
                slab.transform.SetParent(root, false);
                slab.transform.position = new Vector3(f.center.x, f.max.y + height / 2f, f.center.z);
                slab.transform.rotation = Quaternion.LookRotation(alongZ ? Vector3.forward : Vector3.right);
                slab.transform.localScale = new Vector3(width, height, 0.25f);
                if (slabMat != null) slab.GetComponent<Renderer>().sharedMaterial = slabMat;
                slab.AddComponent<Rigidbody>();   // SlidingDoor makes it kinematic

                var door = slab.AddComponent<SlidingDoor>();
                var so = new SerializedObject(door);
                so.FindProperty("slideOffset").vector3Value = new Vector3(0f, height - 0.2f, 0f);   // up into the ceiling
                so.FindProperty("speed").floatValue = 4f;
                so.ApplyModifiedPropertiesWithoutUndo();

                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Strip";
                Object.DestroyImmediate(strip.GetComponent<Collider>());
                strip.transform.SetParent(slab.transform, false);
                strip.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                strip.transform.localScale = new Vector3(0.9f, 0.05f, 1.2f);   // sticks out of both faces

                var lockable = slab.AddComponent<LockableDoor>();
                lockable.Configure(id, entry.zoneA, entry.zoneB, entry.sealedAtStart,
                                   strip.GetComponent<Renderer>(), freeMat, lockMat, sealMat);
                EditorUtility.SetDirty(lockable);

                log.AppendLine($"  + {slab.name} ({width:0.0} x {height:0.0} m{(entry.sealedAtStart ? ", sealed until T1-T3" : "")})");
                id++;
            }
        }

        /// Which way a corridor runs. The two rooms it joins decide it: the corridor runs along the axis
        /// between their floor centres. (Comparing the corridor's own length and width fails on square
        /// corridors: Airlock-Labs is 5 x 5 m, and that guess stood the door lengthwise.)
        static bool RunsAlongZ(string zoneA, string zoneB, Bounds corridorFloor)
        {
            if (TryRoomCentre(zoneA, out Vector3 a) && TryRoomCentre(zoneB, out Vector3 b))
            {
                Vector3 d = b - a;
                return Mathf.Abs(d.z) > Mathf.Abs(d.x);
            }
            return corridorFloor.size.z > corridorFloor.size.x;   // fallback for unknown rooms
        }

        static bool TryRoomCentre(string zone, out Vector3 centre)
        {
            var floor = GameObject.Find($"OVERRIDE_Greybox/{zone}/Floor");
            var col = floor != null ? floor.GetComponent<Collider>() : null;
            centre = col != null ? col.bounds.center : Vector3.zero;
            return col != null;
        }

        static void InstallDirector(System.Text.StringBuilder log)
        {
            var director = Object.FindFirstObjectByType<DirectorController>();
            if (director == null) { log.AppendLine("  ! no DirectorController in the scene"); return; }

            if (director.GetComponent<DirectorBridge>() == null)
            {
                Undo.AddComponent<DirectorBridge>(director.gameObject);
                log.AppendLine("  + DirectorBridge on the Director (GameEvents and rifle hits -> Director)");
            }
            if (director.dronePrefab == null)
            {
                Undo.RecordObject(director, "Set drone prefab");
                director.dronePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DronePrefab);
                EditorUtility.SetDirty(director);
                log.AppendLine("  + Director drone prefab = StandInDrone (swap for Rithish's Wisp when ready)");
            }
        }

        const string WardenPrefab = "Assets/_Project/Prefabs/WardenGameplay.prefab";

        static void InstallWardens(System.Text.StringBuilder log)
        {
            if (Object.FindFirstObjectByType<WardenGameplayController>() != null) { log.AppendLine("  = Wardens already placed"); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WardenPrefab);
            if (prefab == null) { log.AppendLine("  ! no " + WardenPrefab + " (merge Ilzam's warden-gameplay branch)"); return; }

            var director = Object.FindFirstObjectByType<DirectorController>();
            var player = GameObject.FindWithTag("Player");
            var root = new GameObject("OVERRIDE_Wardens").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Install Wardens");

            foreach (var (zone, count) in new[] { (ZoneId.Labs, 1), (ZoneId.ServerHall, 2) })
            {
                HackTerminal terminal = null;
                foreach (var t in Object.FindObjectsByType<HackTerminal>(FindObjectsSortMode.None))
                    if (t.Zone == zone) terminal = t;
                var floor = GameObject.Find($"OVERRIDE_Greybox/{zone}/Floor");
                var floorCol = floor != null ? floor.GetComponent<Collider>() : null;
                if (terminal == null || floorCol == null) { log.AppendLine($"  ! no terminal or floor for {zone}: its Wardens skipped"); continue; }

                // Guard between the terminal and the room's centre, facing into the room.
                float y = floorCol.bounds.max.y;
                Vector3 term = new Vector3(terminal.transform.position.x, y, terminal.transform.position.z);
                Vector3 centre = new Vector3(floorCol.bounds.center.x, y, floorCol.bounds.center.z);
                Vector3 into = (centre - term).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, into);

                for (int k = 0; k < count; k++)
                {
                    float lateral = (k - (count - 1) / 2f) * 7f;          // two Wardens stand 7 m apart
                    Vector3 at = term + into * 6f + side * lateral;
                    float flankSide = lateral < 0f ? -1f : 1f;

                    var warden = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
                    Undo.RegisterCreatedObjectUndo(warden, "Place Warden");
                    warden.name = $"Warden ({zone}{(count > 1 ? " " + (k + 1) : "")})";
                    warden.transform.SetParent(root, true);
                    warden.transform.SetPositionAndRotation(at, Quaternion.LookRotation(into));

                    var covers = new GameObject(warden.name + " Cover").transform;
                    covers.SetParent(root, true);
                    // Same cover qualities as Ilzam's integration test.
                    var current = Cover("Current", at, 0.75f, covers);
                    var advance = Cover("Advance", at + into * 4f, 0.85f, covers);
                    var flank = Cover("Flank", at + side * (6f * flankSide) + into * 2f, 0.85f, covers);
                    var retreat = Cover("Retreat", at - into * 3f, 0.70f, covers);

                    var controller = warden.GetComponent<WardenGameplayController>();
                    controller.InitializeExternalReferences(player != null ? player.transform : null, director,
                                                            current, advance, flank, retreat);
                    EditorUtility.SetDirty(controller);
                    foreach (var vision in warden.GetComponentsInChildren<VisionSensor>()) EditorUtility.SetDirty(vision);
                    log.AppendLine($"  + {warden.name} at {at}");
                }
            }
        }

        static WardenCoverPoint Cover(string name, Vector3 at, float quality, Transform parent)
        {
            var go = new GameObject("Cover - " + name);
            go.transform.SetParent(parent, true);
            go.transform.position = at + Vector3.up * 0.35f;
            var point = go.AddComponent<WardenCoverPoint>();
            point.Configure(quality);
            return point;
        }

        static void InstallGameFlow(System.Text.StringBuilder log)
        {
            if (Object.FindFirstObjectByType<Override.Flow.GameFlow>() != null) { log.AppendLine("  = GameFlow already in the scene"); return; }
            var go = new GameObject("OVERRIDE_GameFlow");
            Undo.RegisterCreatedObjectUndo(go, "Install game flow");
            var flow = go.AddComponent<Override.Flow.GameFlow>();
            var so = new SerializedObject(flow);
            so.FindProperty("panelSettings").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/UI/OverridePanelSettings.asset");
            so.FindProperty("styleSheet").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>("Assets/UI/StartScreen/StartScreen.uss");
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("  + OVERRIDE_GameFlow (pause, game over + respawn, victory)");
        }

        static void InstallDebug(System.Text.StringBuilder log)
        {
            if (GameObject.Find("OVERRIDE_Debug") != null) return;
            var go = new GameObject("OVERRIDE_Debug");
            Undo.RegisterCreatedObjectUndo(go, "Install debug");
            go.AddComponent<NoiseDebugView>();
            go.AddComponent<ObjectivesOverlay>();
            log.AppendLine("  + OVERRIDE_Debug (noise spheres + objectives overlay)");
        }
    }
}
