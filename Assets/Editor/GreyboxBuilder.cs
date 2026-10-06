// GreyboxBuilder.cs
// Builds the OVERRIDE greybox level shell: floors, walls, corridors, vents and the Core ring.
// Run it from the menu bar: OVERRIDE > Build Greybox Level.
//
// WHY AN EDITOR SCRIPT (instead of placing cubes by hand):
//   The layout lives as numbers in one place (the "LAYOUT" section below). If playtesting shows a
//   room is too big, you change one number and rebuild, instead of dragging dozens of cubes.
//   It also lives in an "Editor" folder, so Unity never includes it in the built game.
//
// COORDINATES: 1 Unity unit = 1 metre. X points east, Z points north, Y points up.
//   The origin (0,0,0) is the south-west corner of the Labs' row. The airlock is at the bottom
//   (south) and the Core at the top (north), exactly like the overview mockup.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Override.EditorTools
{
    public static class GreyboxBuilder
    {
        // ------------------------------------------------------------------
        // WHERE THINGS ARE SAVED
        // ------------------------------------------------------------------
        const string RootFolder = "Assets/OVERRIDE/Greybox";
        const string MatFolder  = RootFolder + "/Materials";
        const string ScenePath  = RootFolder + "/Greybox_v1.unity";

        // ------------------------------------------------------------------
        // SIZES (metres). These are starting values: tune them by walking the level.
        // ------------------------------------------------------------------
        const float WallThickness  = 0.3f;
        const float FloorThickness = 0.2f;

        const float RoomHeight        = 3.5f; // Labs, Server Hall, Maintenance
        const float AirlockHeight     = 3.0f;
        const float CoreHeight        = 6.0f; // taller, so the final room feels different
        const float CorridorHeight    = 3.0f;
        const float AirlockDoorHeight = 2.5f;
        const float TunnelHeight      = 2.2f; // dark maintenance tunnel: walkable but cramped
        const float VentHeight        = 1.2f; // crouch-only: set this just above your crouch height

        static readonly Vector2 CoreCentre = new Vector2(30f, 50f); // (x, z)
        const float CoreRadius   = 8f;   // 16 m across
        const float PillarRadius = 1.5f;
        const int   CoreWallSegments = 96; // a circle made of straight wall pieces; more = smoother

        // The Core floor sits 1 cm lower than everything else. Corridor floors poke slightly into
        // the Core, and two surfaces at exactly the same height flicker ("z-fighting").
        const float CoreFloorDrop = 0.01f;

        // ------------------------------------------------------------------
        // LAYOUT
        // ------------------------------------------------------------------
        // Rooms are rectangles. Corridors join two rooms (or a room and the Core).
        // You never cut doorways by hand: the builder finds where each corridor touches a room
        // and leaves an opening in that wall, with a "lintel" (header) above it if the corridor
        // is lower than the room. That lintel is what makes a vent crouch-only.

        static List<Room> BuildRoomList()
        {
            var maintenance = new Room("Maintenance", 32f, 24f, 60f, 38f, RoomHeight, "Floor_Maintenance");
            // Maintenance is a maze of tight corners, so it gets interior walls (from the mockup).
            maintenance.interiorWalls.Add(WallAlongZ(38.5f, 31.85f, 38f));
            maintenance.interiorWalls.Add(WallAlongZ(38.5f, 24f,    29.5f));
            maintenance.interiorWalls.Add(WallAlongZ(44.5f, 24f,    33.4f));
            maintenance.interiorWalls.Add(WallAlongZ(44.5f, 35.3f,  38f));
            maintenance.interiorWalls.Add(WallAlongZ(51.5f, 27.6f,  35.3f));
            maintenance.interiorWalls.Add(WallAlongZ(51.5f, 24f,    25.7f));

            return new List<Room>
            {
                new Room("Airlock",    26f,  0f, 34f,  6f, AirlockHeight, "Floor_Airlock"),
                new Room("Labs",        0f,  8f, 60f, 20f, RoomHeight,    "Floor_Labs"),
                new Room("ServerHall",  0f, 24f, 28f, 38f, RoomHeight,    "Floor_ServerHall"),
                maintenance,
            };
        }

        static List<Corridor> BuildCorridorList()
        {
            return new List<Corridor>
            {
                //            name                       xMin   zMin  xMax   zMax   height             alongZ dark
                new Corridor("Airlock_Labs",            29f,   6f,  31f,    8f,   AirlockDoorHeight, true),
                new Corridor("Labs_ServerHall",         11f,  20f,  15f,   24f,   CorridorHeight,    true),
                new Corridor("Labs_Maintenance",        45f,  20f,  49f,   24f,   CorridorHeight,    true),
                new Corridor("Vent_Labs_ServerHall",    21.5f,20f,  23f,   24f,   VentHeight,        true,  true),
                new Corridor("Vent_Labs_Maintenance",   36.5f,20f,  38f,   24f,   VentHeight,        true,  true),
                new Corridor("ServerHall_Maintenance",  28f,  31f,  32f,   33.5f, CorridorHeight,    false),
                // These two run north from z = 38 until they hit the Core's circular wall.
                Corridor.IntoCore("ServerHall_Core (door A)",   24f, 38f, 27.5f, CorridorHeight, false),
                Corridor.IntoCore("Tunnel_Maintenance_Core",    33f, 38f, 35f,   TunnelHeight,   true),
            };
        }

        // ------------------------------------------------------------------
        // MATERIALS: one flat colour per zone, so you can tell zones apart at a glance.
        // They are saved as assets and only created if missing, so colours you tweak survive rebuilds.
        // ------------------------------------------------------------------
        static readonly Dictionary<string, Color> MaterialColours = new Dictionary<string, Color>
        {
            { "Floor_Airlock",     new Color(0.55f, 0.57f, 0.60f) },
            { "Floor_Labs",        new Color(0.72f, 0.78f, 0.82f) }, // clean and bright
            { "Floor_ServerHall",  new Color(0.32f, 0.38f, 0.45f) }, // cool blue-grey
            { "Floor_Maintenance", new Color(0.38f, 0.35f, 0.30f) }, // grimy brown
            { "Floor_Core",        new Color(0.28f, 0.18f, 0.18f) }, // dark red
            { "Floor_Corridor",    new Color(0.45f, 0.45f, 0.47f) },
            { "Wall",              new Color(0.62f, 0.62f, 0.62f) },
            { "Vent",              new Color(0.10f, 0.10f, 0.10f) }, // dark routes read as black holes
            { "Ceiling",           new Color(0.50f, 0.50f, 0.50f) },
            { "Pillar",            new Color(0.30f, 0.30f, 0.33f) },
        };

        // Static flags tell Unity this geometry never moves. That unlocks lightmap baking
        // (ContributeGI), occlusion culling (Occluder/Occludee) and static batching: the
        // things your optimization evidence will come from later.
        const StaticEditorFlags LevelStatic =
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.ReflectionProbeStatic;

        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static int groundLayer;

        // ==================================================================
        // ENTRY POINT
        // ==================================================================
        [MenuItem("OVERRIDE/Build Greybox Level")]
        static void Build()
        {
            // Give the user a chance to save whatever scene is open, because we replace it.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (System.IO.File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Rebuild greybox?",
                    ScenePath + " already exists and will be replaced.\nAnything placed in it by hand will be lost.",
                    "Rebuild", "Cancel"))
                return;

            EnsureFolder(MatFolder);
            LoadOrCreateMaterials();
            groundLayer = EnsureLayer("Ground");

            // A fresh scene with Unity's default camera and directional light.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var rooms     = BuildRoomList();
            var corridors = BuildCorridorList();
            CutOpenings(rooms, corridors);

            var level    = new GameObject("OVERRIDE_Greybox").transform;
            var ceilings = new GameObject("Ceilings (enable for lighting work)").transform;
            ceilings.SetParent(level, false);

            foreach (var room in rooms) BuildRoom(room, level, ceilings);

            var corridorRoot = new GameObject("Corridors").transform;
            corridorRoot.SetParent(level, false);
            foreach (var corridor in corridors) BuildCorridor(corridor, corridorRoot);

            BuildCore(level, ceilings, corridors);

            int pieces = 0;
            foreach (var t in level.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, LevelStatic);
                if (t.GetComponent<MeshRenderer>() != null) pieces++;
            }

            // Room ceilings start hidden so the directional light reaches the floors and you can
            // look down into the level. Corridor and vent ceilings stay on: vents need them.
            ceilings.gameObject.SetActive(false);

            // Put the camera in the airlock looking north into the level.
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(30f, 1.7f, 2f);
                cam.transform.rotation = Quaternion.identity;
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = level.gameObject;
            SceneView.FrameLastActiveSceneView();
            Debug.Log($"[Level] Greybox built: {pieces} pieces, saved to {ScenePath}");
        }

        // ==================================================================
        // OPENINGS: work out where each corridor meets a room wall
        // ==================================================================
        static void CutOpenings(List<Room> rooms, List<Corridor> corridors)
        {
            foreach (var c in corridors)
            {
                foreach (var r in rooms)
                {
                    if (c.alongZ)
                    {
                        // A north-south corridor must share some X range with the room.
                        if (!Overlaps(c.area.xMin, c.area.xMax, r.area.xMin, r.area.xMax)) continue;
                        if (Same(r.area.zMax, c.area.zMin))                 // corridor starts at the room's north wall
                            r.openings.Add(new Opening(Side.North, c.area.xMin, c.area.xMax, c.height));
                        if (!c.intoCore && Same(r.area.zMin, c.area.zMax))  // corridor ends at the room's south wall
                            r.openings.Add(new Opening(Side.South, c.area.xMin, c.area.xMax, c.height));
                    }
                    else
                    {
                        if (!Overlaps(c.area.zMin, c.area.zMax, r.area.zMin, r.area.zMax)) continue;
                        if (Same(r.area.xMax, c.area.xMin))
                            r.openings.Add(new Opening(Side.East, c.area.zMin, c.area.zMax, c.height));
                        if (Same(r.area.xMin, c.area.xMax))
                            r.openings.Add(new Opening(Side.West, c.area.zMin, c.area.zMax, c.height));
                    }
                }
            }
        }

        // ==================================================================
        // ROOMS
        // ==================================================================
        static void BuildRoom(Room r, Transform parent, Transform ceilings)
        {
            var root = NewChild(r.name, parent);
            var a = r.area;

            // Floor: its top surface sits at y = 0, so the box's centre is half a thickness below.
            var floor = Box("Floor", root, a.Centre(-FloorThickness / 2f),
                            new Vector3(a.Width, FloorThickness, a.Depth), Mat(r.floorMat));
            floor.layer = groundLayer;

            // Walls sit centred on the room's edge. North/south walls are stretched by half a wall
            // thickness at each end so the corners close without a gap.
            var walls = NewChild("Walls", root);
            float half = WallThickness / 2f;
            BuildWall(walls, "Wall_South", true,  a.zMin, a.xMin - half, a.xMax + half, r.height, r.OpeningsOn(Side.South));
            BuildWall(walls, "Wall_North", true,  a.zMax, a.xMin - half, a.xMax + half, r.height, r.OpeningsOn(Side.North));
            BuildWall(walls, "Wall_West",  false, a.xMin, a.zMin,        a.zMax,        r.height, r.OpeningsOn(Side.West));
            BuildWall(walls, "Wall_East",  false, a.xMax, a.zMin,        a.zMax,        r.height, r.OpeningsOn(Side.East));

            for (int i = 0; i < r.interiorWalls.Count; i++)
            {
                var w = r.interiorWalls[i];
                Box("Wall_Interior_" + (i + 1), walls, w.Centre(r.height / 2f),
                    new Vector3(w.Width, r.height, w.Depth), Mat("Wall"));
            }

            Box(r.name + "_Ceiling", ceilings, a.Centre(r.height + FloorThickness / 2f),
                new Vector3(a.Width + WallThickness, FloorThickness, a.Depth + WallThickness), Mat("Ceiling"));
        }

        // Builds one straight wall from `start` to `end`, leaving gaps for openings.
        //   alongX = true  -> the wall runs east-west at z = fixedCoord
        //   alongX = false -> the wall runs north-south at x = fixedCoord
        static void BuildWall(Transform parent, string name, bool alongX, float fixedCoord,
                              float start, float end, float height, List<Opening> openings)
        {
            openings.Sort((p, q) => p.from.CompareTo(q.from));
            var wall = NewChild(name, parent);
            int index = 0;

            // Walk along the wall: solid piece up to each opening, a lintel above the opening, repeat.
            float cursor = start;
            foreach (var o in openings)
            {
                if (o.from > cursor) Piece(cursor, o.from, 0f, height, "Solid");
                if (o.height < height) Piece(o.from, o.to, o.height, height, "Lintel");
                cursor = o.to;
            }
            if (end > cursor) Piece(cursor, end, 0f, height, "Solid");

            // A local function: a small helper that only BuildWall needs, so it lives inside it.
            void Piece(float from, float to, float bottom, float top, string kind)
            {
                float length = to - from;
                float mid    = (from + to) / 2f;
                float midY   = (bottom + top) / 2f;
                var centre = alongX ? new Vector3(mid, midY, fixedCoord) : new Vector3(fixedCoord, midY, mid);
                var size   = alongX ? new Vector3(length, top - bottom, WallThickness)
                                    : new Vector3(WallThickness, top - bottom, length);
                Box(kind + "_" + (++index), wall, centre, size, Mat("Wall"));
            }
        }

        // ==================================================================
        // CORRIDORS AND VENTS
        // ==================================================================
        static void BuildCorridor(Corridor c, Transform parent)
        {
            var root = NewChild(c.name, parent);
            var a = c.area;
            var wallMat = Mat(c.dark ? "Vent" : "Wall");

            var floor = Box("Floor", root, a.Centre(-FloorThickness / 2f),
                            new Vector3(a.Width, FloorThickness, a.Depth), Mat(c.dark ? "Vent" : "Floor_Corridor"));
            floor.layer = groundLayer;

            float half = WallThickness / 2f;
            if (c.alongZ)
            {
                // Side walls on the west and east edges. A corridor into the Core meets a curved
                // wall, so each side wall stops where that wall's circle crosses it.
                float westEnd = c.intoCore ? CoreWallZ(a.xMin) + half : a.zMax;
                float eastEnd = c.intoCore ? CoreWallZ(a.xMax) + half : a.zMax;
                Box("Wall_West", root, new Vector3(a.xMin, c.height / 2f, (a.zMin + westEnd) / 2f),
                    new Vector3(WallThickness, c.height, westEnd - a.zMin), wallMat);
                Box("Wall_East", root, new Vector3(a.xMax, c.height / 2f, (a.zMin + eastEnd) / 2f),
                    new Vector3(WallThickness, c.height, eastEnd - a.zMin), wallMat);
                Box("Ceiling", root, a.Centre(c.height + FloorThickness / 2f),
                    new Vector3(a.Width + WallThickness, FloorThickness, a.Depth), Mat("Ceiling"));
            }
            else
            {
                Box("Wall_South", root, new Vector3((a.xMin + a.xMax) / 2f, c.height / 2f, a.zMin),
                    new Vector3(a.Width, c.height, WallThickness), wallMat);
                Box("Wall_North", root, new Vector3((a.xMin + a.xMax) / 2f, c.height / 2f, a.zMax),
                    new Vector3(a.Width, c.height, WallThickness), wallMat);
                Box("Ceiling", root, a.Centre(c.height + FloorThickness / 2f),
                    new Vector3(a.Width, FloorThickness, a.Depth + WallThickness), Mat("Ceiling"));
            }
        }

        // ==================================================================
        // CORE: a round room built from a cylinder floor and a ring of straight wall pieces
        // ==================================================================
        static void BuildCore(Transform parent, Transform ceilings, List<Corridor> corridors)
        {
            var root = NewChild("Core", parent);
            var centre = new Vector3(CoreCentre.x, 0f, CoreCentre.y);

            var floor = Cylinder("Floor", root,
                                 centre + Vector3.down * (FloorThickness / 2f + CoreFloorDrop),
                                 CoreRadius * 2f + WallThickness, FloorThickness, Mat("Floor_Core"));
            floor.layer = groundLayer;

            var ring = NewChild("Wall_Ring", root);
            float step = 2f * Mathf.PI / CoreWallSegments;            // angle per piece, in radians
            float pieceLength = CoreRadius * step * 1.05f;            // 5% overlap hides hairline cracks

            for (int i = 0; i < CoreWallSegments; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step, mid = (a0 + a1) / 2f;
                Vector3 p0 = centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * CoreRadius;
                Vector3 p1 = centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * CoreRadius;
                Vector3 pm = centre + new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid)) * CoreRadius;

                // Does a corridor come in through this piece? Only check the south half of the ring.
                Corridor entering = null;
                if (pm.z < centre.z)
                    foreach (var c in corridors)
                        if (c.intoCore && Overlaps(Mathf.Min(p0.x, p1.x), Mathf.Max(p0.x, p1.x), c.area.xMin, c.area.xMax))
                            entering = c;

                // Face the piece along the circle: its local Z axis points along the tangent.
                var rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(mid), 0f, Mathf.Cos(mid)));

                float bottom = entering == null ? 0f : entering.height; // doorway: only build above it
                if (bottom >= CoreHeight) continue;
                var piece = Box((entering == null ? "Solid_" : "Lintel_") + (i + 1), ring,
                                pm + Vector3.up * ((bottom + CoreHeight) / 2f),
                                new Vector3(WallThickness, CoreHeight - bottom, pieceLength), Mat("Wall"));
                piece.transform.localRotation = rotation;
            }

            Cylinder("Pillar", root, centre + Vector3.up * (CoreHeight / 2f),
                     PillarRadius * 2f, CoreHeight, Mat("Pillar"));

            Cylinder("Core_Ceiling", ceilings, centre + Vector3.up * (CoreHeight + FloorThickness / 2f),
                     CoreRadius * 2f + WallThickness, FloorThickness, Mat("Ceiling"));
        }

        // The z where the Core's circular wall is, at a given x on its south side.
        // From the circle equation (x - cx)^2 + (z - cz)^2 = r^2, solved for the lower z.
        static float CoreWallZ(float x)
        {
            float dx = x - CoreCentre.x;
            return CoreCentre.y - Mathf.Sqrt(CoreRadius * CoreRadius - dx * dx);
        }

        // ==================================================================
        // SMALL HELPERS
        // ==================================================================
        static Transform NewChild(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // A cube primitive, moved and stretched. Scaling a cube also scales its BoxCollider,
        // so the collision shape always matches what you see.
        static GameObject Box(string name, Transform parent, Vector3 centre, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // Unity's built-in cylinder is 1 unit wide and 2 units tall, hence height / 2.
        // It comes with a CapsuleCollider (rounded ends), which is wrong for a flat floor,
        // so it's swapped for a MeshCollider that matches the cylinder exactly.
        static GameObject Cylinder(string name, Transform parent, Vector3 centre, float diameter, float height, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = new Vector3(diameter, height / 2f, diameter);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            return go;
        }

        static Material Mat(string name) => materials[name];

        static void LoadOrCreateMaterials()
        {
            materials.Clear();

            // Shader.Find returns null if the shader isn't found. The project uses URP, so
            // "Standard" is only a safety net in case this script is copied to a Built-in project.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            foreach (var entry in MaterialColours)
            {
                string path = MatFolder + "/" + entry.Key + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", entry.Value); // URP
                    else mat.color = entry.Value;                                               // Built-in
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
                    AssetDatabase.CreateAsset(mat, path);
                }
                materials[entry.Key] = mat;
            }
            AssetDatabase.SaveAssets();
        }

        // Creates "Assets/A/B/C" one folder at a time, because CreateFolder only makes one level.
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        // Floors go on a "Ground" layer so PlayerMovement's groundMask can select them.
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
            Debug.LogWarning("[Level] No free layer slot for '" + name + "'. Floors stay on Default.");
            return 0;
        }

        static bool Overlaps(float aMin, float aMax, float bMin, float bMax) => aMin < bMax && bMin < aMax;
        static bool Same(float a, float b) => Mathf.Abs(a - b) < 0.01f;

        static Area WallAlongZ(float x, float zFrom, float zTo) =>
            new Area(x - WallThickness / 2f, zFrom, x + WallThickness / 2f, zTo);

        // ==================================================================
        // DATA TYPES
        // ==================================================================
        enum Side { North, South, East, West }

        // A rectangle on the ground, in metres. (Unity's Rect uses x/y, which gets confusing
        // when "y" means north here, so this small struct names things after X and Z.)
        struct Area
        {
            public float xMin, zMin, xMax, zMax;
            public Area(float xMin, float zMin, float xMax, float zMax)
            { this.xMin = xMin; this.zMin = zMin; this.xMax = xMax; this.zMax = zMax; }
            public float Width => xMax - xMin;
            public float Depth => zMax - zMin;
            public Vector3 Centre(float y) => new Vector3((xMin + xMax) / 2f, y, (zMin + zMax) / 2f);
        }

        struct Opening
        {
            public Side side;
            public float from, to;   // along the wall
            public float height;     // the doorway's height; the lintel fills the rest
            public Opening(Side side, float from, float to, float height)
            { this.side = side; this.from = from; this.to = to; this.height = height; }
        }

        class Room
        {
            public readonly string name;
            public readonly Area area;
            public readonly float height;
            public readonly string floorMat;
            public readonly List<Opening> openings = new List<Opening>();
            public readonly List<Area> interiorWalls = new List<Area>();

            public Room(string name, float xMin, float zMin, float xMax, float zMax, float height, string floorMat)
            {
                this.name = name; area = new Area(xMin, zMin, xMax, zMax);
                this.height = height; this.floorMat = floorMat;
            }

            public List<Opening> OpeningsOn(Side side) => openings.FindAll(o => o.side == side);
        }

        class Corridor
        {
            public readonly string name;
            public readonly Area area;
            public readonly float height;
            public readonly bool alongZ;   // true = runs north-south
            public readonly bool dark;     // vents and the tunnel: black material
            public bool intoCore;          // ends at the Core's round wall instead of a room

            public Corridor(string name, float xMin, float zMin, float xMax, float zMax,
                            float height, bool alongZ, bool dark = false)
            {
                this.name = name; area = new Area(xMin, zMin, xMax, zMax);
                this.height = height; this.alongZ = alongZ; this.dark = dark;
            }

            // A north-south corridor whose far end is the Core wall. Its floor runs until the
            // further of its two edges reaches that wall, so there's no gap at the doorway.
            public static Corridor IntoCore(string name, float xMin, float zMin, float xMax, float height, bool dark)
            {
                float zMax = Mathf.Max(CoreWallZ(xMin), CoreWallZ(xMax));
                return new Corridor(name, xMin, zMin, xMax, zMax, height, true, dark) { intoCore = true };
            }
        }
    }
}
