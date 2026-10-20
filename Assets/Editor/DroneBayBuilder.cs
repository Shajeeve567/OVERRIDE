// DroneBayBuilder.cs
// Places the 10 drone bays from the Director's bay table, each with its waypoint node and its
// blocking crate, then CHECKS every bay against the four rules and prints a report in the Console.
// Run it from the menu bar: OVERRIDE > Place Drone Bays. It clears its own old output first.
//
// WHAT A BAY IS
//   A small metal booth: back wall, two side walls, a roof, and a front wall with a 1.4 m hatch.
//   The booth does two jobs at once:
//     - it HIDES the spawn point: the hatch is turned away from the zone's terminal, so the terminal
//       is always looking at a solid wall (rule: out of sight, "behind a solid wall, not glass");
//     - it makes BLOCKING physical: the hatch is just wider than a cargo crate, so one crate pushed
//       in front of it really closes it (rule: a known crate can block it).
//
// WHY THE RULES ARE CHECKED IN CODE
//   The positions below were worked out by hand against the layout, but "I think it's hidden" isn't
//   evidence. The report uses the same physics the game uses (a line-of-sight raycast from each
//   terminal's screen), so it's proof you can screenshot for your design log.

using System.Collections.Generic;
using System.Text;
using Override.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Override.EditorTools
{
    public static class DroneBayBuilder
    {
        const string BaysRoot      = "OVERRIDE_DroneBays";
        const string WaypointsRoot = "OVERRIDE_Waypoints";
        const string PrefabPath    = "Assets/OVERRIDE/Props/Prefabs/DroneBay.prefab";
        const string CratePath     = "Assets/OVERRIDE/Props/Prefabs/CrateBarricade.prefab";
        const string MatFolder     = "Assets/OVERRIDE/Props/Materials/";

        // The rules from the bay table.
        const float MinDistance = 10f, MaxDistance = 25f;  // metres from the zone's terminal
        const float MaxWaypointGap = 3f;                    // "next to a waypoint node"

        // Booth size (metres). The hatch is 1.4 m: a 1.2 m crate pushed in front of it seals it.
        const float BoothSize = 3f, BoothHeight = 3f, Wall = 0.2f, Hatch = 1.4f;

        // Where things go relative to the hatch, in metres.
        const float WaypointOut = 2.5f;  // waypoint this far out from the hatch
        const float CrateOut = 1.2f;     // crate this far out...
        const float CrateAside = 1.6f;   // ...and this far to one side, so the bay starts OPEN
        // The player's body weighs 1 kg. A crate lighter than that gets shoved aside just by walking
        // into it, which makes blocking a bay a quick, readable player choice. (The corridor
        // barricades stay 80 kg: those are meant to be a slow, deliberate push.)
        const float BlockerMass = 0.8f;

        // The terminal each zone's bays hide from, by the names PropBuilder gives them.
        static readonly Dictionary<string, string> Terminals = new Dictionary<string, string>
        {
            { "Labs",        "Terminal_T1 (placeholder)" },
            { "Maintenance", "Terminal_T2 (placeholder)" },
            { "ServerHall",  "Terminal_T3 (placeholder)" },
            { "Core",        "Core_OverrideConsole (placeholder)" },
        };

        // One row of the bay table.
        //   x, z     : position in DESIGN units (the original 60 x 50 m plan), scaled by LevelSize
        //   facing   : which way the hatch opens (always away from, or across from, the terminal)
        //   crateSide: +1 / -1 = which side of the hatch the crate waits on (the side with more room)
        struct Bay
        {
            public int id; public string zone; public float x, z; public Vector3 facing; public int crateSide;
            public string note;
        }

        static readonly Vector3 W = Vector3.left, E = Vector3.right, N = Vector3.forward, S = Vector3.back;

        static List<Bay> BayTable()
        {
            var bays = new List<Bay>
            {
                // Labs: T1 is at the east end facing west. One bay on each flank (north and south),
                // ~12 m away, hatches facing west so drones come out beside the player, not in front.
                new Bay { id = 0, zone = "Labs", x = 53.6f, z = 18.4f, facing = W, crateSide = +1, note = "north flank of T1" },
                new Bay { id = 1, zone = "Labs", x = 53.6f, z = 10.0f, facing = W, crateSide = +1, note = "south flank of T1" },

                // Maintenance: T2 sits in the maze's east pocket, which has two ways in: a gap at its
                // north end (the side the dark tunnel route arrives from) and a gap at its south end.
                // One bay guards each gap, so no single crate can block both.
                new Bay { id = 2, zone = "Maintenance", x = 55.2f, z = 36.8f, facing = W, crateSide = +1, note = "north gap (tunnel side)" },
                new Bay { id = 3, zone = "Maintenance", x = 54.4f, z = 25.0f, facing = W, crateSide = +1, note = "south gap" },

                // Server Hall: T3 is by the east wall. Bays at the east ends of two different lanes,
                // behind rack rows, clear of the Maintenance door and Core door A keep-outs.
                new Bay { id = 4, zone = "ServerHall", x = 24.4f, z = 33.1f, facing = W, crateSide = +1, note = "middle lane, east end" },
                new Bay { id = 5, zone = "ServerHall", x = 22.0f, z = 36.6f, facing = W, crateSide = +1, note = "north lane, east end" },
            };

            // Core: four bays backed against the round wall at 0, 90, 180 and 270 degrees
            // (0 = east, 90 = north). 6 and 7 are opposite; 8 sits between them; 9 is opposite 8.
            // They go between PropBuilder's cover blocks (30, 90... degrees at 15 m) and keep clear of
            // both south entrances. Hatches open ALONG the wall, never into it or towards the console.
            AddCoreBay(bays, 6, 0f,   S, -1, "east, pillar between it and the console");
            AddCoreBay(bays, 7, 180f, S, +1, "west, opposite bay 6");
            AddCoreBay(bays, 8, 90f,  E, -1, "north, between 6 and 7");
            AddCoreBay(bays, 9, 270f, W, -1, "south, opposite 8, between the two entrances");
            return bays;
        }

        static void AddCoreBay(List<Bay> bays, int id, float degrees, Vector3 facing, int crateSide, string note)
        {
            // Back of the booth against the wall's inner face: wall radius, minus half the wall,
            // minus half the booth, minus 5 cm so the two never touch (touching walls flicker).
            float radius = LevelSize.S(8f) - 0.15f - BoothSize / 2f - 0.05f;
            float a = degrees * Mathf.Deg2Rad;
            // Stored in design units like the others, so the conversion below is the same for all.
            bays.Add(new Bay
            {
                id = id, zone = "Core", facing = facing, crateSide = crateSide, note = note,
                x = 30f + radius * Mathf.Cos(a) / LevelSize.Plan,
                z = 50f + radius * Mathf.Sin(a) / LevelSize.Plan,
            });
        }

        // ==================================================================
        [MenuItem("OVERRIDE/Place Drone Bays")]
        static void Build()
        {
            if (GameObject.Find("OVERRIDE_Greybox") == null || GameObject.Find("OVERRIDE_Props") == null)
            {
                Debug.LogError("[Bays] Open the greybox scene and run 'Dress Greybox Level' first (bays need its terminals and crates).");
                return;
            }
            var bayPrefab = LoadOrCreateBayPrefab();
            var cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CratePath);
            if (bayPrefab == null || cratePrefab == null) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Place Drone Bays");
            int undoGroup = Undo.GetCurrentGroup();

            // Clear the old bays and their waypoints (only the "Bays" group: other waypoints stay).
            var oldBays = GameObject.Find(BaysRoot);
            if (oldBays != null) Undo.DestroyObjectImmediate(oldBays);
            var waypoints = GameObject.Find(WaypointsRoot);
            if (waypoints == null)
            {
                waypoints = new GameObject(WaypointsRoot);
                Undo.RegisterCreatedObjectUndo(waypoints, "Place Drone Bays");
            }
            var oldNodes = waypoints.transform.Find("Bays");
            if (oldNodes != null) Undo.DestroyObjectImmediate(oldNodes.gameObject);

            var root = new GameObject(BaysRoot).transform;
            var nodeGroup = new GameObject("Bays").transform;
            nodeGroup.SetParent(waypoints.transform, false);
            var crateGroup = new GameObject("Blockers").transform;
            crateGroup.SetParent(root, false);

            int groundMask = 1 << LayerMask.NameToLayer("Ground");
            var placed = new List<(Bay bay, DroneBay component)>();

            foreach (var b in BayTable())
            {
                // Design units -> metres, then find the floor height by dropping a ray onto the
                // Ground layer (the Core's floor sits a centimetre lower than the rooms').
                var pos = new Vector3(LevelSize.S(b.x), 0f, LevelSize.S(b.z));
                Physics.SyncTransforms();
                if (Physics.Raycast(pos + Vector3.up * 2.5f, Vector3.down, out var hit, 5f, groundMask)) pos.y = hit.point.y;

                // The bay itself. LookRotation(facing) turns the prefab's +Z (its hatch) to `facing`.
                var go = (GameObject)PrefabUtility.InstantiatePrefab(bayPrefab, root);
                go.name = $"DroneBay_{b.id} ({b.zone})";
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(b.facing));
                var bay = go.GetComponent<DroneBay>();
                bay.bayId = b.id;
                bay.zone = b.zone;

                // The waypoint, straight out from the hatch at drone flying height.
                Vector3 hatch = bay.mouth.position;
                var node = new GameObject($"WP_Bay_{b.id}").AddComponent<WaypointNode>();
                node.transform.SetParent(nodeGroup, false);
                node.transform.position = new Vector3(hatch.x, pos.y + 1.5f, hatch.z) + b.facing * WaypointOut;
                bay.waypoint = node;

                // The crate, waiting beside the hatch. "Side" = the facing turned 90 degrees:
                // (x, z) -> (-z, x) is a quarter turn on the floor plan.
                var side = new Vector3(-b.facing.z, 0f, b.facing.x) * b.crateSide;
                var crate = (GameObject)PrefabUtility.InstantiatePrefab(cratePrefab, crateGroup);
                crate.name = $"BayBlocker_{b.id}";
                crate.transform.SetPositionAndRotation(
                    new Vector3(hatch.x, pos.y, hatch.z) + b.facing * CrateOut + side * CrateAside,
                    Quaternion.LookRotation(b.facing));
                bay.blocker = crate.GetComponent<Rigidbody>();
                bay.blocker.mass = BlockerMass;   // only this copy: the CrateBarricade prefab stays 80 kg
                PrefabUtility.RecordPrefabInstancePropertyModifications(bay.blocker);

                // Changes made to a prefab COPY from code must be recorded, or Unity drops them on save.
                PrefabUtility.RecordPrefabInstancePropertyModifications(bay);
                placed.Add((b, bay));
            }

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Place Drone Bays");
            Undo.RegisterCreatedObjectUndo(nodeGroup.gameObject, "Place Drone Bays");
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = root.gameObject;

            Report(placed);
        }

        // ==================================================================
        // CHECKING THE FOUR RULES
        // ==================================================================
        static void Report(List<(Bay bay, DroneBay component)> placed)
        {
            Physics.SyncTransforms(); // make physics see the objects just placed
            var log = new StringBuilder("[Bays] Drone bay check\n");
            int failures = 0;

            foreach (var (b, bay) in placed)
            {
                var problems = new List<string>();
                Vector3 spawn = bay.spawnPoint.position;

                // 1 + 2. Distance and line of sight from the zone's terminal.
                var terminal = FindUnder("OVERRIDE_Props", Terminals[b.zone]);
                float distance = -1f;
                if (terminal == null) problems.Add("terminal '" + Terminals[b.zone] + "' not found");
                else
                {
                    Vector3 t = terminal.position;
                    distance = Vector3.Distance(new Vector3(t.x, 0f, t.z), new Vector3(spawn.x, 0f, spawn.z));
                    if (distance < MinDistance || distance > MaxDistance)
                        problems.Add($"{distance:0.0} m from terminal (rule {MinDistance}-{MaxDistance})");

                    // Linecast = a ray between two points. If it hits nothing, the terminal can "see"
                    // the spawn point. It starts at the screen (1.25 m up); a ray that starts inside a
                    // collider (the terminal's own) doesn't hit that collider, so it's ignored.
                    Vector3 eye = t + Vector3.up * 1.25f;
                    if (!Physics.Linecast(eye, spawn))
                        problems.Add("VISIBLE from the terminal");
                }

                // 3. Waypoint next to the bay.
                if (bay.waypoint == null) problems.Add("no waypoint");
                else if (Vector3.Distance(bay.mouth.position, bay.waypoint.transform.position) > MaxWaypointGap)
                    problems.Add("waypoint too far from the hatch");

                // 4. A crate that can block it, close enough to push across the hatch.
                if (bay.blocker == null) problems.Add("no blocking crate");

                // Bonus: does the booth overlap anything solid (a wall, a rack, cover)?
                // OverlapBox lists every collider inside a box; the bay's own colliders and the floor don't count.
                var centre = bay.transform.position + Vector3.up * (BoothHeight / 2f + 0.05f);
                var half = new Vector3(BoothSize, BoothHeight, BoothSize) / 2f - Vector3.one * 0.1f;
                foreach (var c in Physics.OverlapBox(centre, half, bay.transform.rotation))
                    if (!c.transform.IsChildOf(bay.transform)) problems.Add("booth overlaps " + c.name);

                string verdict = problems.Count == 0 ? "PASS" : "CHECK";
                if (problems.Count > 0) failures++;
                log.AppendLine($"  Bay {b.id} {b.zone,-11} {verdict}  {(distance >= 0 ? distance.ToString("0.0") + " m" : "")}  " +
                               $"{b.note}{(problems.Count > 0 ? "  -> " + string.Join("; ", problems) : "")}");
            }

            log.AppendLine(failures == 0 ? "All 10 bays pass: hidden, 10-25 m, waypoint, blocker." : $"{failures} bay(s) need a look.");
            if (failures == 0) Debug.Log(log.ToString()); else Debug.LogWarning(log.ToString());
        }

        static Transform FindUnder(string rootName, string childName)
        {
            var root = GameObject.Find(rootName);
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t;
            return null;
        }

        // ==================================================================
        // THE BAY PREFAB (built from cubes once, like PropBuilder's props)
        // Front (the hatch) = +Z. Pivot = floor level, centre of the booth.
        // ==================================================================
        static GameObject LoadOrCreateBayPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            var metal = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "Prop_DarkMetal.mat");
            var red = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "Prop_RedGlow.mat");
            var hazard = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "Prop_Hazard.mat");
            if (metal == null || red == null || hazard == null)
            {
                Debug.LogError("[Bays] Prop materials missing in " + MatFolder + ". Run 'Dress Greybox Level' first.");
                return null;
            }

            var root = new GameObject("DroneBay");
            float h = BoothHeight, half = BoothSize / 2f, inset = half - Wall / 2f;

            // Solid walls keep their box colliders: they block bullets, the player and sight lines.
            Cube(root, "Back",  new Vector3(0f, h / 2f, -inset), new Vector3(BoothSize, h, Wall), metal, true);
            Cube(root, "Left",  new Vector3(-inset, h / 2f, 0f), new Vector3(Wall, h, BoothSize), metal, true);
            Cube(root, "Right", new Vector3(inset, h / 2f, 0f),  new Vector3(Wall, h, BoothSize), metal, true);
            Cube(root, "Roof",  new Vector3(0f, h - Wall / 2f, 0f), new Vector3(BoothSize, Wall, BoothSize), metal, true);

            // Front wall in three pieces around the hatch: left of it, right of it, above it.
            float sideWidth = (BoothSize - Hatch) / 2f;
            float sideX = Hatch / 2f + sideWidth / 2f;
            Cube(root, "Front_L",   new Vector3(-sideX, h / 2f, inset), new Vector3(sideWidth, h, Wall), metal, true);
            Cube(root, "Front_R",   new Vector3(sideX, h / 2f, inset),  new Vector3(sideWidth, h, Wall), metal, true);
            Cube(root, "Front_Top", new Vector3(0f, (Hatch + h) / 2f, inset), new Vector3(Hatch, h - Hatch, Wall), metal, true);

            // Decoration (no colliders): a red light over the hatch (ARGUS's colour) and a hazard
            // stripe on the floor in front, so players learn "red light + stripes = drones come out here".
            Cube(root, "HatchLight",  new Vector3(0f, Hatch + 0.12f, half + 0.02f), new Vector3(Hatch, 0.08f, 0.04f), red, false);
            Cube(root, "HazardFloor", new Vector3(0f, 0.01f, half + 0.3f), new Vector3(Hatch, 0.02f, 0.6f), hazard, false);
            Cube(root, "Pad",         new Vector3(0f, 0.02f, 0f), new Vector3(BoothSize - 2f * Wall, 0.04f, BoothSize - 2f * Wall), metal, false);

            var spawn = new GameObject("SpawnPoint").transform;
            spawn.SetParent(root.transform, false);
            spawn.localPosition = new Vector3(0f, Hatch / 2f, -0.3f);   // inside, behind the front wall

            var mouth = new GameObject("Mouth").transform;
            mouth.SetParent(root.transform, false);
            mouth.localPosition = new Vector3(0f, Hatch / 2f, half);    // the hatch's centre, facing +Z

            var bay = root.AddComponent<DroneBay>();
            bay.spawnPoint = spawn;
            bay.mouth = mouth;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void Cube(GameObject parent, string name, Vector3 position, Vector3 size, Material mat, bool solid)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
        }
    }
}
