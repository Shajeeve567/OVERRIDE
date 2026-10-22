// PropBuilder.cs
// Dresses the OVERRIDE greybox with props: lab benches, server racks, crates, pipes, barricades,
// security cameras, health stations, terminal placeholders, ceiling lights and door frames.
// Run it from the menu bar with Greybox_v1 open: OVERRIDE > Dress Greybox Level.
//
// DESIGN DECISIONS
// 1. Every prop is a PREFAB built from Unity primitives. When the Core Developer delivers a real
//    model (e.g. the override terminal), they edit the prefab once and every copy in the level updates.
//    Prefabs and materials are only created if missing, so hand edits to them survive re-runs.
// 2. All props live under one root, "OVERRIDE_Props". Re-running the tool deletes and rebuilds only
//    that root, never the level geometry or anything else you added to the scene.
// 3. Colours follow the GDD: cool greys and white panels, a teal facility accent, red for ARGUS.
// 4. Each zone gets its OWN ceiling-light material (Light_Labs, Light_ServerHall, ...). They all start
//    ARGUS red. Your zone lighting switch can later turn one zone white/teal by changing one material.
// 5. Positions come from the zone mockups and use the same coordinates as GreyboxBuilder
//    (1 unit = 1 m, X east, Z north). Keep the two in sync if you move a room.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Override.EditorTools
{
    public static class PropBuilder
    {
        // ------------------------------------------------------------------
        // WHERE THINGS ARE SAVED
        // ------------------------------------------------------------------
        const string PropFolder   = "Assets/OVERRIDE/Props";
        const string PrefabFolder = PropFolder + "/Prefabs";
        const string MatFolder    = PropFolder + "/Materials";
        const string RootName     = "OVERRIDE_Props";

        // ------------------------------------------------------------------
        // LAYOUT (copied from GreyboxBuilder)
        // ------------------------------------------------------------------
        // Positions in this file are written in the same DESIGN UNITS as GreyboxBuilder's layout and
        // turned into metres with S(...) or P(...), which multiply by LevelSize.Plan. Prop SIZES are not
        // scaled: a bench stays a bench. So in a bigger level the props simply spread out.
        const float WallHalf      = 0.15f; // half the greybox wall thickness; walls sit centred on room edges
        const float RoomHeight    = LevelSize.RoomHeight;
        const float AirlockHeight = LevelSize.AirlockHeight;
        const float CoreHeight    = LevelSize.CoreHeight;
        static readonly float CoreRadius   = S(8f);
        static readonly float PillarRadius = S(1.5f);
        static readonly Vector3 CoreCentre = P(30f, 50f);

        // Mounting heights, raised with the taller rooms so cameras and pipes sit up near the ceiling.
        const float CameraHeight = RoomHeight - 1.5f;
        const float PipeHeight   = RoomHeight - 1.5f;
        const float RackHeight   = 3.0f; // server racks (was 2.2)

        static readonly Area Airlock     = Scaled(26f,  0f, 34f,  6f);
        static readonly Area Labs        = Scaled( 0f,  8f, 60f, 20f);
        static readonly Area ServerHall  = Scaled( 0f, 24f, 28f, 38f);
        static readonly Area Maintenance = Scaled(32f, 24f, 60f, 38f);

        // Design units -> metres.
        static float S(float designUnits) => LevelSize.S(designUnits);
        // A floor point (y = 0) from design units.
        static Vector3 P(float x, float z) => new Vector3(S(x), 0f, S(z));
        static Area Scaled(float xMin, float zMin, float xMax, float zMax) =>
            new Area(S(xMin), S(zMin), S(xMax), S(zMax));

        // Static flags: this geometry never moves, so it can be lightmapped, occlusion-culled and batched.
        // (Pushable barricades are the exception: they have a Rigidbody, so they must NOT be static.)
        const StaticEditorFlags LevelStatic =
            StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;

        // ------------------------------------------------------------------
        // MATERIALS
        // ------------------------------------------------------------------
        static readonly Color Teal     = new Color(0.18f, 0.77f, 0.71f);
        static readonly Color ArgusRed = new Color(0.90f, 0.12f, 0.12f);

        // Name -> look. Emission > 0 makes the surface glow and lets the lightmapper use it as a light.
        static readonly Dictionary<string, MatSpec> MaterialSpecs = new Dictionary<string, MatSpec>
        {
            { "Prop_WhitePanel", Spec(new Color(0.84f, 0.86f, 0.88f), 0.0f, 0.45f) },
            { "Prop_Metal",      Spec(new Color(0.42f, 0.45f, 0.48f), 0.6f, 0.45f) },
            { "Prop_DarkMetal",  Spec(new Color(0.13f, 0.14f, 0.16f), 0.5f, 0.35f) },
            { "Prop_Pipe",       Spec(new Color(0.36f, 0.31f, 0.26f), 0.7f, 0.30f) },
            { "Prop_Hazard",     Spec(new Color(0.85f, 0.65f, 0.08f), 0.1f, 0.30f) },
            { "Prop_TealGlow",   Spec(new Color(0.05f, 0.20f, 0.18f), 0.0f, 0.60f, Teal * 2f) },
            { "Prop_RedGlow",    Spec(new Color(0.20f, 0.02f, 0.02f), 0.0f, 0.60f, ArgusRed * 2.5f) },
            { "Prop_Screen",     Spec(new Color(0.02f, 0.05f, 0.05f), 0.0f, 0.85f, Teal * 1f) },
            // One light material per zone, all starting in ARGUS emergency red (GDD: "red emergency
            // lighting in all zones" until that zone's terminal is overridden).
            { "Light_Airlock",     Spec(new Color(0.2f, 0.05f, 0.05f), 0f, 0.5f, ArgusRed * 3f) },
            { "Light_Labs",        Spec(new Color(0.2f, 0.05f, 0.05f), 0f, 0.5f, ArgusRed * 3f) },
            { "Light_ServerHall",  Spec(new Color(0.2f, 0.05f, 0.05f), 0f, 0.5f, ArgusRed * 3f) },
            { "Light_Maintenance", Spec(new Color(0.2f, 0.05f, 0.05f), 0f, 0.5f, ArgusRed * 2f) }, // dimmer
            { "Light_Core",        Spec(new Color(0.2f, 0.05f, 0.05f), 0f, 0.5f, ArgusRed * 3f) },
        };

        static readonly Dictionary<string, Material>   materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, GameObject> prefabs   = new Dictionary<string, GameObject>();

        // ==================================================================
        // ENTRY POINT
        // ==================================================================
        [MenuItem("OVERRIDE/Dress Greybox Level")]
        static void Dress()
        {
            // Only dress a scene that actually contains the greybox level.
            if (GameObject.Find("OVERRIDE_Greybox") == null)
            {
                EditorUtility.DisplayDialog("Open the greybox first",
                    "Open Assets/OVERRIDE/Greybox/Greybox_v1.unity, then run this again.", "OK");
                return;
            }

            EnsureFolder(PrefabFolder);
            EnsureFolder(MatFolder);
            LoadOrCreateMaterials();
            LoadOrCreatePrefabs();

            // Group everything into ONE undo step, so a single Ctrl+Z removes the whole dressing pass.
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Dress Greybox Level");

            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var root = new GameObject(RootName).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Dress Greybox Level");

            DressAirlock(Group(root, "Airlock"));
            DressLabs(Group(root, "Labs"));
            DressServerHall(Group(root, "ServerHall"));
            DressMaintenance(Group(root, "Maintenance"));
            DressCore(Group(root, "Core"));
            BuildDoorFrames(Group(root, "DoorFrames"));

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene); // shows the * so you remember to save
            Selection.activeGameObject = root.gameObject;
            Debug.Log("[Level] Greybox dressed: " + root.GetComponentsInChildren<MeshRenderer>(true).Length +
                      " prop meshes under " + RootName + ". Save the scene to keep them.");
        }

        // ==================================================================
        // ZONES
        // ==================================================================

        // Start room: lockers and a bench sell "staff entrance". No combat here, so no cover.
        static void DressAirlock(Transform zone)
        {
            // Lockers stand side by side 0.7 m apart, so only their starting point is scaled.
            for (int i = 0; i < 3; i++)
                PlaceOnWall("Locker", zone, Airlock, Wall.East, S(1.0f) + i * 0.7f, 0f);
            PlaceOnWall("Bench", zone, Airlock, Wall.West, S(3f), 0f);
            CeilingLight(zone, P(30f, 3f) + Vector3.up * AirlockHeight, 0f, "Light_Airlock");
        }

        // Open benches with long sightlines (GDD: "open benches and glass walls").
        static void DressLabs(Transform zone)
        {
            // Six 4 m benches from the Labs mockup. The middle strip stays clear so the airlock door
            // has a straight view into the room.
            var benches = new (float x, float z, float yaw)[]
            {
                (8.4f, 16.3f, 0f), (8.4f, 11.7f, 180f), (20.7f, 14.0f, 0f),
                (33.8f, 16.6f, 0f), (33.8f, 11.4f, 180f), (43.0f, 14.0f, 180f),
            };
            foreach (var b in benches) Place("LabBench", zone, P(b.x, b.z), b.yaw);

            // Pushable barricade at the Maintenance corridor mouth.
            Place("CrateBarricade", zone, P(47.5f, 18.3f), 15f);

            // Terminal T1, facing west so the player approaches it across the room.
            Place("Terminal_Placeholder", zone, P(55.6f, 14.2f), -90f, "Terminal_T1 (placeholder)");

            SecurityCamera(zone, Labs, Wall.North, S(1.0f), CameraHeight);
            SecurityCamera(zone, Labs, Wall.North, S(59.0f), CameraHeight);
            PlaceOnWall("HealthStation", zone, Labs, Wall.South, S(15.8f), 0f);
            PlaceOnWall("HealthStation", zone, Labs, Wall.North, S(27.0f), 0f); // between the two vents

            foreach (float z in new[] { 11f, 17f })
                for (float x = 5f; x < 60f; x += 10f)
                    CeilingLight(zone, P(x, z) + Vector3.up * RoomHeight, 0f, "Light_Labs");
        }

        // Rows of racks make lanes for the Wardens' cover fights.
        static void DressServerHall(Transform zone)
        {
            // Six rows from the Server Hall mockup: (start x, end x, z) in design units. The rows get
            // LONGER with the plan, so more 0.6 m racks fit in each; the aisles between rows get wider.
            var rows = new (float x0, float x1, float z)[]
            {
                (2.3f, 11.9f, 35.0f), (14.2f, 24.2f, 35.0f),
                (4.2f, 13.1f, 31.2f), (15.4f, 24.2f, 31.2f),
                (2.3f, 11.9f, 27.3f), (14.2f, 24.2f, 27.3f),
            };
            foreach (var r in rows)
            {
                float start = S(r.x0), end = S(r.x1);
                int count = Mathf.FloorToInt((end - start) / 0.62f); // 2 cm gap between racks
                for (int i = 0; i < count; i++)
                    Place("ServerRack", zone, new Vector3(start + 0.31f + i * 0.62f, 0f, S(r.z)), 0f);
            }

            // Barricade beside door A into the Core, leaving most of the doorway open.
            Place("CrateBarricade", zone, P(26.9f, 36.4f), -10f);

            // Terminal T3 near the east wall, south of the Maintenance doorway.
            Place("Terminal_Placeholder", zone, P(27.0f, 28.5f), -90f, "Terminal_T3 (placeholder)");

            SecurityCamera(zone, ServerHall, Wall.North, S(1.0f), CameraHeight);
            SecurityCamera(zone, ServerHall, Wall.East, S(25.0f), CameraHeight);
            PlaceOnWall("HealthStation", zone, ServerHall, Wall.West, S(33.2f), 0f);
            PlaceOnWall("HealthStation", zone, ServerHall, Wall.East, S(35.5f), 0f);

            // Lights over the aisles, not over the racks.
            foreach (float z in new[] { 33.1f, 29.25f })
                for (float x = 4f; x < 28f; x += 6f)
                    CeilingLight(zone, P(x, z) + Vector3.up * RoomHeight, 0f, "Light_ServerHall");
            foreach (float z in new[] { 36.6f, 25.6f })
                foreach (float x in new[] { 7f, 19f })
                    CeilingLight(zone, P(x, z) + Vector3.up * RoomHeight, 0f, "Light_ServerHall");
        }

        // Pipes, crates and dim light: the Seeker's territory.
        static void DressMaintenance(Transform zone)
        {
            // Cover crates, one per pocket of the maze, kept clear of the doorways and wall gaps.
            var crates = new (float x, float z, float yaw)[]
            {
                (35.0f, 35.2f, 8f), (41.5f, 34.5f, -5f), (42.3f, 27.3f, 12f),
                (47.3f, 33.8f, 0f), (54.2f, 29.6f, -8f), (55.8f, 26.2f, 20f),
            };
            foreach (var c in crates) Place("CoverCrate", zone, P(c.x, c.z), c.yaw);

            // Barricade beside the dark tunnel mouth to the Core.
            Place("CrateBarricade", zone, P(36.6f, 36.8f), 5f);

            Place("Terminal_Placeholder", zone, P(58.6f, 31.4f), -90f, "Terminal_T2 (placeholder)");

            // Pipe runs high on the walls, split around the interior walls and the tunnel opening.
            PipeRun(zone, Maintenance, Wall.North, S(35.4f), S(38.2f), PipeHeight);
            PipeRun(zone, Maintenance, Wall.North, S(38.8f), S(44.2f), PipeHeight);
            PipeRun(zone, Maintenance, Wall.North, S(44.8f), S(51.2f), PipeHeight);
            PipeRun(zone, Maintenance, Wall.North, S(51.8f), S(59.6f), PipeHeight);
            PipeRun(zone, Maintenance, Wall.South, S(38.8f), S(44.2f), PipeHeight);
            PipeRun(zone, Maintenance, Wall.South, S(51.8f), S(59.6f), PipeHeight);
            PipeRun(zone, Maintenance, Wall.East,  S(24.4f), S(37.6f), PipeHeight);

            SecurityCamera(zone, Maintenance, Wall.West, S(37.0f), CameraHeight);
            SecurityCamera(zone, Maintenance, Wall.South, S(59.2f), CameraHeight);
            PlaceOnWall("HealthStation", zone, Maintenance, Wall.West, S(26.0f), 0f);
            PlaceOnWall("HealthStation", zone, Maintenance, Wall.East, S(35.7f), 0f);

            // Fewer lights than other zones, so the room reads as dim.
            foreach (float x in new[] { 35f, 41.5f, 48f, 55.5f })
                CeilingLight(zone, P(x, 31f) + Vector3.up * RoomHeight, 90f, "Light_Maintenance");
            CeilingLight(zone, P(48f, 26f) + Vector3.up * RoomHeight, 90f, "Light_Maintenance");
            CeilingLight(zone, P(48f, 36f) + Vector3.up * RoomHeight, 90f, "Light_Maintenance");
        }

        // Round final room: a ring of cover around the pillar, the override console, ARGUS red everywhere.
        static void DressCore(Transform zone)
        {
            // Six cover blocks at 3/4 of the radius, at 30°, 90°, 150°... The 270° one sits between the
            // two entrances, so neither doorway is blocked.
            for (int i = 0; i < 6; i++)
            {
                float a = (30f + 60f * i) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var cover = Place("CoverBlock", zone, CoreCentre + outward * S(6f), 0f);
                cover.transform.rotation = Quaternion.LookRotation(-outward); // long side along the circle
            }

            // The final override console on the north side of the pillar, facing north.
            Place("Terminal_Placeholder", zone,
                  CoreCentre + new Vector3(0f, 0f, PillarRadius + 0.5f), 0f, "Core_OverrideConsole (placeholder)");

            // A glowing red ring on the floor around the pillar (GDD: "pulsing red until the final override").
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "CoreGlowRing";
            ring.transform.SetParent(zone, false);
            ring.transform.position = CoreCentre + new Vector3(0f, 0.01f, 0f);
            ring.transform.localScale = new Vector3((PillarRadius + 0.3f) * 2f, 0.02f, (PillarRadius + 0.3f) * 2f);
            Object.DestroyImmediate(ring.GetComponent<Collider>()); // decoration only: never trips the player
            ring.GetComponent<MeshRenderer>().sharedMaterial = Mat("Prop_RedGlow");
            GameObjectUtility.SetStaticEditorFlags(ring, LevelStatic);

            // Two cameras on the north side of the ring wall, health stations east and west.
            foreach (float deg in new[] { 78f, 102f })
                CoreWallMount(zone, "SecurityCamera", deg, CoreHeight - 2.5f, true);
            foreach (float deg in new[] { 0f, 180f })
                CoreWallMount(zone, "HealthStation", deg, 0f, false);

            // Eight lights in a circle, each turned along the circle.
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var light = CeilingLight(zone, CoreCentre + outward * S(5f) + Vector3.up * CoreHeight, 0f, "Light_Core");
                light.transform.rotation = Quaternion.LookRotation(outward);
            }
        }

        // Frames around every doorway make exits readable at a glance: teal for doors, hazard yellow
        // for crouch-only vents and the dark tunnel. Sizes vary per doorway, so these are built
        // directly instead of as prefabs. Positions are design units; heights come from LevelSize,
        // so they always match the doorway heights GreyboxBuilder cut.
        static void BuildDoorFrames(Transform group)
        {
            const float door = LevelSize.CorridorHeight, airlock = LevelSize.AirlockDoorHeight,
                        tunnel = LevelSize.TunnelHeight, vent = LevelSize.VentHeight;
            //        name                          alongX  wall z/x  from   to     height   strip
            DoorFrame(group, "Airlock_North",        true,   6f,     29f,   31f,   airlock, "Prop_TealGlow");
            DoorFrame(group, "Labs_South",           true,   8f,     29f,   31f,   airlock, "Prop_TealGlow");
            DoorFrame(group, "Labs_To_ServerHall",   true,  20f,     11f,   15f,   door,    "Prop_TealGlow");
            DoorFrame(group, "Labs_To_Maintenance",  true,  20f,     45f,   49f,   door,    "Prop_TealGlow");
            DoorFrame(group, "ServerHall_South",     true,  24f,     11f,   15f,   door,    "Prop_TealGlow");
            DoorFrame(group, "Maintenance_South",    true,  24f,     45f,   49f,   door,    "Prop_TealGlow");
            DoorFrame(group, "ServerHall_East",      false, 28f,     31f,   33.5f, door,    "Prop_TealGlow");
            DoorFrame(group, "Maintenance_West",     false, 32f,     31f,   33.5f, door,    "Prop_TealGlow");
            DoorFrame(group, "ServerHall_To_Core",   true,  38f,     24f,   27.5f, door,    "Prop_TealGlow");
            DoorFrame(group, "Tunnel_To_Core",       true,  38f,     33f,   35f,   tunnel,  "Prop_Hazard");
            DoorFrame(group, "Vent_Labs_SH_North",   true,  20f,     21.5f, 23f,   vent,    "Prop_Hazard");
            DoorFrame(group, "Vent_Labs_SH_South",   true,  24f,     21.5f, 23f,   vent,    "Prop_Hazard");
            DoorFrame(group, "Vent_Labs_Maint_North",true,  20f,     36.5f, 38f,   vent,    "Prop_Hazard");
            DoorFrame(group, "Vent_Labs_Maint_South",true,  24f,     36.5f, 38f,   vent,    "Prop_Hazard");
        }

        // ==================================================================
        // PLACEMENT HELPERS
        // ==================================================================
        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // InstantiatePrefab (not Instantiate) keeps each copy linked to its prefab asset.
        static GameObject Place(string prefab, Transform parent, Vector3 position, float yaw, string label = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[prefab], parent);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (label != null) go.name = label;
            return go;
        }

        // Wall-mounted prefabs have their pivot on their BACK face and point +Z out of the wall.
        // So "on a wall" just means: stand on the wall's inner face and turn to face into the room.
        static GameObject PlaceOnWall(string prefab, Transform parent, Area room, Wall wall, float along, float y)
        {
            var go = Place(prefab, parent, Vector3.zero, 0f);
            go.transform.position = OnWall(room, wall, along, y, out Quaternion facing);
            go.transform.rotation = facing;
            return go;
        }

        static Vector3 OnWall(Area a, Wall wall, float along, float y, out Quaternion facing)
        {
            switch (wall)
            {
                case Wall.North: facing = Quaternion.LookRotation(Vector3.back);    return new Vector3(along, y, a.zMax - WallHalf);
                case Wall.South: facing = Quaternion.LookRotation(Vector3.forward); return new Vector3(along, y, a.zMin + WallHalf);
                case Wall.East:  facing = Quaternion.LookRotation(Vector3.left);    return new Vector3(a.xMax - WallHalf, y, along);
                default:         facing = Quaternion.LookRotation(Vector3.right);   return new Vector3(a.xMin + WallHalf, y, along);
            }
        }

        // A camera on a wall whose head turns to look at the middle of the room's floor.
        static void SecurityCamera(Transform parent, Area room, Wall wall, float along, float height)
        {
            var cam = PlaceOnWall("SecurityCamera", parent, room, wall, along, height);
            AimHead(cam, room.Centre(0.5f));
        }

        static void AimHead(GameObject cam, Vector3 target)
        {
            var head = cam.transform.Find("Head");
            head.rotation = Quaternion.LookRotation(target - head.position);
            // Changing a child of a prefab copy from code must be recorded, or Unity forgets it on save.
            PrefabUtility.RecordPrefabInstancePropertyModifications(head);
        }

        // Mount something on the inside of the Core's round wall, at an angle (0° = east, 90° = north).
        static void CoreWallMount(Transform parent, string prefab, float degrees, float y, bool aimAtCentre)
        {
            float a = degrees * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var go = Place(prefab, parent, CoreCentre + outward * (CoreRadius - WallHalf) + Vector3.up * y, 0f);
            go.transform.rotation = Quaternion.LookRotation(-outward);
            if (aimAtCentre) AimHead(go, CoreCentre + Vector3.up * 0.5f);
        }

        // The PipeRun prefab is 4 m long along its local X. Stretching only X keeps the pipes round.
        static void PipeRun(Transform parent, Area room, Wall wall, float from, float to, float y)
        {
            var go = PlaceOnWall("PipeRun", parent, room, wall, (from + to) / 2f, y);
            go.transform.localScale = new Vector3((to - from) / 4f, 1f, 1f);
        }

        // Each zone's lights get that zone's material, so the lighting switch can change one zone at a time.
        static GameObject CeilingLight(Transform parent, Vector3 position, float yaw, string zoneMaterial)
        {
            var go = Place("LightFixture", parent, position, yaw);
            var panel = go.transform.Find("Panel").GetComponent<MeshRenderer>();
            panel.sharedMaterial = Mat(zoneMaterial);
            PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
            return go;
        }

        // Two posts and a header around a doorway, sticking out 15 cm from both faces of the wall,
        // plus a glowing strip across the header.
        static void DoorFrame(Transform parent, string name, bool alongX, float wallCoord,
                              float from, float to, float height, string stripMat)
        {
            var frame = Group(parent, name);
            const float post = 0.25f, depth = 0.6f, header = 0.2f;

            // The doorway's position comes in design units; the frame's own sizes above stay in metres.
            wallCoord = S(wallCoord); from = S(from); to = S(to);

            Piece("Post_A", from - post, from, 0f, height + header, "Prop_Metal", depth, true);
            Piece("Post_B", to, to + post, 0f, height + header, "Prop_Metal", depth, true);
            Piece("Header", from - post, to + post, height, height + header, "Prop_Metal", depth, true);
            Piece("Strip", from, to, height + 0.07f, height + 0.13f, stripMat, depth + 0.02f, false);

            void Piece(string n, float a0, float a1, float y0, float y1, string mat, float d, bool solid)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = n;
                go.transform.SetParent(frame, false);
                float mid = (a0 + a1) / 2f, len = a1 - a0;
                go.transform.position = alongX ? new Vector3(mid, (y0 + y1) / 2f, wallCoord)
                                               : new Vector3(wallCoord, (y0 + y1) / 2f, mid);
                go.transform.localScale = alongX ? new Vector3(len, y1 - y0, d) : new Vector3(d, y1 - y0, len);
                if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
                go.GetComponent<MeshRenderer>().sharedMaterial = Mat(mat);
                GameObjectUtility.SetStaticEditorFlags(go, LevelStatic);
            }
        }

        // ==================================================================
        // PROP PREFABS
        // Each builder makes the prop at the origin out of primitives. Front = +Z.
        // Floor props have their pivot at floor level; wall props have it on their back face.
        // ==================================================================
        static void LoadOrCreatePrefabs()
        {
            prefabs.Clear();
            Make("LabBench",             BuildLabBench,      collider: true,  isStatic: true);
            // rebuild: true = always regenerate, so a size change here (RackHeight) reaches the asset.
            Make("ServerRack",           BuildServerRack,    collider: true,  isStatic: true, rebuild: true);
            Make("CoverCrate",           BuildCoverCrate,    collider: true,  isStatic: true);
            Make("CrateBarricade",       BuildBarricade,     collider: true,  isStatic: false);
            Make("CoverBlock",           BuildCoverBlock,    collider: true,  isStatic: true);
            Make("Terminal_Placeholder", BuildTerminal,      collider: true,  isStatic: true);
            Make("HealthStation",        BuildHealthStation, collider: true,  isStatic: true);
            Make("SecurityCamera",       BuildCamera,        collider: false, isStatic: false); // head is aimed per copy
            Make("Locker",               BuildLocker,        collider: true,  isStatic: true);
            Make("Bench",                BuildBench,         collider: true,  isStatic: true);
            Make("PipeRun",              BuildPipeRun,       collider: false, isStatic: true);  // overhead: nothing to bump into
            Make("LightFixture",         BuildLightFixture,  collider: false, isStatic: true);
        }

        static void Make(string name, Func<GameObject> build, bool collider, bool isStatic, bool rebuild = false)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            // Normally keep whatever is there (it may have been swapped for a real model).
            // With rebuild, saving over the same path keeps the prefab's GUID, so every copy already
            // placed in a scene updates to the new shape instead of going missing.
            if (existing != null && !rebuild) { prefabs[name] = existing; return; }

            var temp = build();
            temp.name = name;

            // ONE box collider around the whole prop instead of one per part: cheaper for physics and
            // simpler for agents' cover raycasts. The prop is built at the origin, so world bounds = local.
            if (collider)
            {
                var bounds = new Bounds(temp.transform.position, Vector3.zero);
                foreach (var r in temp.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                var box = temp.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }
            if (isStatic)
                foreach (var t in temp.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, LevelStatic);

            prefabs[name] = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
        }

        static GameObject BuildLabBench()
        {
            var root = new GameObject();
            Part(root, "Cabinet", PrimitiveType.Cube, new Vector3(0f, 0.425f, 0f), new Vector3(3.8f, 0.85f, 0.8f), "Prop_Metal");
            Part(root, "Top",     PrimitiveType.Cube, new Vector3(0f, 0.89f, 0f),  new Vector3(4.0f, 0.08f, 1.0f), "Prop_WhitePanel");
            Part(root, "KickStrip", PrimitiveType.Cube, new Vector3(0f, 0.1f, 0.41f), new Vector3(3.8f, 0.04f, 0.02f), "Prop_TealGlow");
            foreach (float x in new[] { -1f, 1f })
            {
                Part(root, "MonitorFrame", PrimitiveType.Cube, new Vector3(x, 1.15f, -0.25f), new Vector3(0.6f, 0.4f, 0.05f), "Prop_DarkMetal");
                Part(root, "MonitorScreen", PrimitiveType.Cube, new Vector3(x, 1.15f, -0.222f), new Vector3(0.54f, 0.34f, 0.01f), "Prop_Screen");
            }
            return root;
        }

        static GameObject BuildServerRack()
        {
            var root = new GameObject();
            // 3.0 m tall (was 2.2): taller than a person, so the lanes really hide you and the Warden,
            // but still well under the 10 m ceiling so drones can fly over the rows.
            Part(root, "Body", PrimitiveType.Cube, new Vector3(0f, RackHeight / 2f, 0f), new Vector3(0.6f, RackHeight, 1.0f), "Prop_DarkMetal");
            foreach (float side in new[] { 0.505f, -0.505f }) // LED rows on both faces of the rack
            {
                // One LED strip every 0.3 m, from 0.4 m up to just under the top.
                for (float y = 0.4f; y < RackHeight - 0.35f; y += 0.3f)
                    Part(root, "LED", PrimitiveType.Cube, new Vector3(0f, y, side), new Vector3(0.4f, 0.03f, 0.01f), "Prop_TealGlow");
                Part(root, "StatusLight", PrimitiveType.Cube, new Vector3(0.22f, RackHeight - 0.2f, side), new Vector3(0.05f, 0.05f, 0.01f), "Prop_RedGlow");
            }
            return root;
        }

        static GameObject BuildCoverCrate()
        {
            var root = new GameObject();
            Part(root, "Body",       PrimitiveType.Cube, new Vector3(0f, 0.65f, 0f), new Vector3(1.3f, 1.3f, 1.3f),   "Prop_Metal");
            Part(root, "TopBand",    PrimitiveType.Cube, new Vector3(0f, 1.3f, 0f),  new Vector3(1.4f, 0.1f, 1.4f),   "Prop_DarkMetal");
            Part(root, "BottomBand", PrimitiveType.Cube, new Vector3(0f, 0.05f, 0f), new Vector3(1.4f, 0.1f, 1.4f),   "Prop_DarkMetal");
            Part(root, "Stripe",     PrimitiveType.Cube, new Vector3(0f, 0.9f, 0f),  new Vector3(1.31f, 0.12f, 1.31f), "Prop_TealGlow");
            return root;
        }

        // The pushable barricade: a real Rigidbody, so the Systems Engineer's physics and noise work on it.
        static GameObject BuildBarricade()
        {
            var root = new GameObject();
            Part(root, "Body",     PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f),  new Vector3(1.2f, 1.2f, 1.2f),   "Prop_Hazard");
            Part(root, "BandLow",  PrimitiveType.Cube, new Vector3(0f, 0.25f, 0f), new Vector3(1.22f, 0.15f, 1.22f), "Prop_DarkMetal");
            Part(root, "BandHigh", PrimitiveType.Cube, new Vector3(0f, 0.95f, 0f), new Vector3(1.22f, 0.15f, 1.22f), "Prop_DarkMetal");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 80f; // heavy: the player can shove it, but it doesn't fly when bumped
            return root;
        }

        static GameObject BuildCoverBlock()
        {
            var root = new GameObject();
            Part(root, "Body",     PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(2.0f, 1.1f, 0.5f),   "Prop_WhitePanel");
            Part(root, "RedStrip", PrimitiveType.Cube, new Vector3(0f, 1.08f, 0f), new Vector3(2.02f, 0.06f, 0.52f), "Prop_RedGlow");
            return root;
        }

        // Stand-in for the Core Developer's custom terminal model (same footprint, so swapping is easy).
        static GameObject BuildTerminal()
        {
            var root = new GameObject();
            var tilt = new Vector3(-20f, 0f, 0f); // screen leans back towards the player
            Part(root, "Base",   PrimitiveType.Cube, new Vector3(0f, 0.5f, 0f),    new Vector3(0.9f, 1.0f, 0.6f),  "Prop_DarkMetal");
            Part(root, "Frame",  PrimitiveType.Cube, new Vector3(0f, 1.25f, 0.05f), new Vector3(0.8f, 0.55f, 0.06f), "Prop_Metal", tilt);
            Part(root, "Screen", PrimitiveType.Cube, new Vector3(0f, 1.25f, 0.09f), new Vector3(0.7f, 0.45f, 0.01f), "Prop_RedGlow", tilt);
            return root;
        }

        static GameObject BuildHealthStation()
        {
            var root = new GameObject();
            Part(root, "Panel",  PrimitiveType.Cube, new Vector3(0f, 1.3f, 0.06f),  new Vector3(0.7f, 0.9f, 0.12f),  "Prop_WhitePanel");
            Part(root, "CrossV", PrimitiveType.Cube, new Vector3(0f, 1.35f, 0.13f), new Vector3(0.12f, 0.45f, 0.02f), "Prop_TealGlow");
            Part(root, "CrossH", PrimitiveType.Cube, new Vector3(0f, 1.35f, 0.13f), new Vector3(0.45f, 0.12f, 0.02f), "Prop_TealGlow");
            Part(root, "Tray",   PrimitiveType.Cube, new Vector3(0f, 0.95f, 0.1f),  new Vector3(0.5f, 0.06f, 0.2f),   "Prop_Metal");
            return root;
        }

        // The camera body sits on a "Head" child, so each copy can be aimed without turning the bracket.
        static GameObject BuildCamera()
        {
            var root = new GameObject();
            Part(root, "Bracket", PrimitiveType.Cube, new Vector3(0f, 0f, 0.02f), new Vector3(0.2f, 0.25f, 0.04f), "Prop_DarkMetal");
            Part(root, "Arm",     PrimitiveType.Cube, new Vector3(0f, 0f, 0.17f), new Vector3(0.06f, 0.06f, 0.3f), "Prop_DarkMetal");
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 0f, 0.32f);
            Part(head.gameObject, "Body", PrimitiveType.Cube,     new Vector3(0f, 0f, 0.12f),     new Vector3(0.18f, 0.16f, 0.35f), "Prop_WhitePanel");
            Part(head.gameObject, "Lens", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.31f),     new Vector3(0.1f, 0.03f, 0.1f),   "Prop_DarkMetal", new Vector3(90f, 0f, 0f));
            Part(head.gameObject, "LED",  PrimitiveType.Cube,     new Vector3(0.06f, 0.05f, 0.30f), new Vector3(0.03f, 0.03f, 0.01f), "Prop_RedGlow");
            return root;
        }

        static GameObject BuildLocker()
        {
            var root = new GameObject();
            Part(root, "Body",   PrimitiveType.Cube, new Vector3(0f, 1.0f, 0.25f),   new Vector3(0.6f, 2.0f, 0.5f),   "Prop_Metal");
            Part(root, "Handle", PrimitiveType.Cube, new Vector3(0.22f, 1.0f, 0.51f), new Vector3(0.03f, 0.15f, 0.03f), "Prop_DarkMetal");
            for (int i = 0; i < 3; i++)
                Part(root, "Vent", PrimitiveType.Cube, new Vector3(0f, 1.7f - i * 0.06f, 0.505f), new Vector3(0.4f, 0.03f, 0.01f), "Prop_DarkMetal");
            return root;
        }

        static GameObject BuildBench()
        {
            var root = new GameObject();
            Part(root, "Seat", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0.25f), new Vector3(2.0f, 0.08f, 0.45f), "Prop_WhitePanel");
            foreach (float x in new[] { -0.85f, 0.85f })
                Part(root, "Leg", PrimitiveType.Cube, new Vector3(x, 0.225f, 0.25f), new Vector3(0.08f, 0.45f, 0.4f), "Prop_Metal");
            return root;
        }

        // Three pipes along local X, 4 m long. Unity's cylinder is 2 units tall along Y, so it's turned
        // 90° onto its side and scaled by 2 in Y to make it 4 m long.
        static GameObject BuildPipeRun()
        {
            var root = new GameObject();
            var onSide = new Vector3(0f, 0f, 90f);
            Part(root, "PipeLarge",  PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.2f),     new Vector3(0.30f, 2f, 0.30f), "Prop_Pipe", onSide);
            Part(root, "PipeMedium", PrimitiveType.Cylinder, new Vector3(0f, 0.32f, 0.18f), new Vector3(0.20f, 2f, 0.20f), "Prop_Pipe", onSide);
            Part(root, "PipeSmall",  PrimitiveType.Cylinder, new Vector3(0f, -0.28f, 0.16f), new Vector3(0.15f, 2f, 0.15f), "Prop_Pipe", onSide);
            foreach (float x in new[] { -1.8f, 1.8f })
                Part(root, "Bracket", PrimitiveType.Cube, new Vector3(x, 0f, 0.12f), new Vector3(0.06f, 0.8f, 0.25f), "Prop_DarkMetal");
            return root;
        }

        // Pivot on the top face, so it can be placed at ceiling height directly.
        static GameObject BuildLightFixture()
        {
            var root = new GameObject();
            Part(root, "Housing", PrimitiveType.Cube, new Vector3(0f, -0.03f, 0f),  new Vector3(1.4f, 0.06f, 0.35f), "Prop_DarkMetal");
            Part(root, "Panel",   PrimitiveType.Cube, new Vector3(0f, -0.065f, 0f), new Vector3(1.3f, 0.02f, 0.25f), "Light_Labs");
            return root;
        }

        // One primitive part of a prop. Its own collider is removed: the prop gets one collider on its root.
        static void Part(GameObject parent, string name, PrimitiveType type, Vector3 position, Vector3 scale,
                         string mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(mat);
        }

        // ==================================================================
        // MATERIALS AND FOLDERS
        // ==================================================================
        static Material Mat(string name) => materials[name];

        static void LoadOrCreateMaterials()
        {
            materials.Clear();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            foreach (var entry in MaterialSpecs)
            {
                string path = MatFolder + "/" + entry.Key + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    var s = entry.Value;
                    mat = new Material(shader);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", s.colour); else mat.color = s.colour;
                    if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", s.metallic);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", s.smoothness);
                    if (s.emission.maxColorComponent > 0f)
                    {
                        // URP only reads _EmissionColor when the _EMISSION keyword is on.
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", s.emission);
                        // BakedEmissive: the lightmapper treats the glowing surface as a light source.
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    }
                    AssetDatabase.CreateAsset(mat, path);
                }
                materials[entry.Key] = mat;
            }
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        // ==================================================================
        // DATA TYPES
        // ==================================================================
        enum Wall { North, South, East, West }

        struct MatSpec { public Color colour; public float metallic, smoothness; public Color emission; }

        static MatSpec Spec(Color colour, float metallic, float smoothness, Color emission = default) =>
            new MatSpec { colour = colour, metallic = metallic, smoothness = smoothness, emission = emission };

        struct Area
        {
            public float xMin, zMin, xMax, zMax;
            public Area(float xMin, float zMin, float xMax, float zMax)
            { this.xMin = xMin; this.zMin = zMin; this.xMax = xMax; this.zMax = zMax; }
            public Vector3 Centre(float y) => new Vector3((xMin + xMax) / 2f, y, (zMin + zMax) / 2f);
        }
    }
}
