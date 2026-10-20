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
//     Cameras         SecurityCameraSensor on Shajeeve's camera prefab (sightings for the Seeker)
//     Seeker          one Seeker in Maintenance, built by HunterSetup if the prefab doesn't exist yet
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
            InstallDoors(log);
            InstallDirector(log);
            log.AppendLine(HunterSetup.EnsureCameraSensors());
            log.AppendLine(HunterSetup.PlaceInMaintenance());
            InstallDebug(log);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(log.ToString());
            EditorUtility.DisplayDialog("Integration", "Done. Check the Console for details, then save the scene (Cmd+S).", "OK");
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
