// WarehouseKitProps.cs
// Spreads the SciFi Warehouse Kit's decorative props through the level.
// Run it from the menu bar: OVERRIDE > Place Warehouse Props. Like the other builders it clears its
// own old output first, so you can re-run it as often as you like.
// Run it AFTER "Dress With Warehouse Kit" (so it can dodge the wall supports and pipes), but it
// also works on the plain greybox.
//
// THE RULES IT FOLLOWS (so props never break the game)
//   1. Floor props (crates, barrels, shelves...) only go in a band along the room's outer walls.
//      The middle of each room is the combat space; PropBuilder already placed the cover there.
//   2. Nothing goes in front of a doorway: each doorway gets a 6 m deep "keep-out" box.
//   3. Nothing overlaps anything already there: PropBuilder's props, the kit's wall supports and
//      pipes, Maintenance's maze walls (padded 2 m so the maze gaps stay open), or another prop.
//   4. Floor props KEEP their colliders: they're solid, so the player and drones must bump into them.
//      Wall-mounted bits (cameras, extinguishers, lights) lose theirs, so the player can't snag on them.
//   5. Kit props with a Rigidbody (the cart, some shelves and pallets) are made kinematic: they stay
//      put instead of being knocked over by the player or by bullets.
//
// It also adds REAL lights. The kit's hanging lights and wall lights are only meshes with glowing
// materials; they light nothing. Every second fixture gets a point light, which also fixes the rooms
// going dark under the kit's ceiling.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Kit = Override.EditorTools.WarehouseKitDresser; // a short alias for the shared helpers

namespace Override.EditorTools
{
    public static class WarehouseKitProps
    {
        // ------------------------------------------------------------------
        // SETTINGS
        // ------------------------------------------------------------------
        const string PropsRoot = "OVERRIDE_WarehouseProps";
        const string Props = "Assets/SciFi Warehouse Kit/Prefabs/Props/";
        const int Seed = 2026;

        const float BackGap = 0.45f;      // floor props stand this far off the wall face (clears the panels)
        const float Padding = 0.15f;      // minimum space between any two things
        const float DoorKeepOut = 6f;     // how far into the room a doorway is kept clear
        const float DoorSideClear = 1.5f; // and how far to each side of it
        const float MazeClear = 2f;       // padding around Maintenance's interior walls

        // Which floor props suit which zone (from the GDD's zone moods), and how full each wall gets
        // (0 = empty, 1 = packed). Labs is clean and sparse; Maintenance is cluttered storage.
        static readonly Dictionary<string, (string[] palette, float fill)> FloorProps = new Dictionary<string, (string[], float)>
        {
            { "Labs", (new[] {
                "Misc Props/Garbage Bin", "Misc Props/Cart", "Crates Barrels Pallets/Crate Short",
                "Crates Barrels Pallets/Crate Long", "Shelves/Shelf Variation 01", "Shelves/Shelves Empty" }, 0.35f) },
            { "ServerHall", (new[] {
                "Crates Barrels Pallets/Crate Short", "Crates Barrels Pallets/Crate Long",
                "Crates Barrels Pallets/Pallet Variations/Pallet Variation 08",
                "Crates Barrels Pallets/Pallet Variations/Pallet Variation 09",
                "Misc Props/Garbage Bin", "Shelves/Shelf Variation 05" }, 0.35f) },
            { "Maintenance", (new[] {
                "Crates Barrels Pallets/Pallet Variations/Pallet Variation 02",
                "Crates Barrels Pallets/Pallet Variations/Pallet Variation 03",
                "Crates Barrels Pallets/Pallet Variations/Pallet Variation 04",
                "Crates Barrels Pallets/Pallet Variations/Pallet Variation 05",
                "Crates Barrels Pallets/Pallet Variations/Pallet Variation 06",
                "Crates Barrels Pallets/Pallet Variations/Pallet Variation 07",
                "Crates Barrels Pallets/Barrel", "Crates Barrels Pallets/Crate Long", "Misc Props/Cart",
                "Shelves/Shelf Variation 03", "Shelves/Shelf Variation 04", "Shelves/Shelf Variation 06" }, 0.6f) },
        };

        // Rooms that get cameras and extinguishers (the airlock too, but no floor props: it's the spawn).
        static readonly string[] WallMountRooms = { "Airlock", "Labs", "ServerHall", "Maintenance" };

        const string HangingLight = "Misc Props/Hanging Light";
        const string WallLight    = "Misc Props/Wall Light";
        const string SecurityCam  = "Misc Props/Security Cam";
        const string Extinguisher = "Misc Props/Fire Extinguisher";
        const string DuctStraight = "Ducts/Duct Straight";

        static readonly Color LampColour = new Color(0.85f, 0.93f, 1f); // cool white: clinical facility

        // The things a new prop must not overlap, and the keep-out boxes in front of doorways.
        static readonly List<Bounds> blockers = new List<Bounds>();
        static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
        static System.Random rng;
        static int lightCount, camCount;

        // ==================================================================
        [MenuItem("OVERRIDE/Place Warehouse Props")]
        static void Place()
        {
            var greybox = GameObject.Find(Kit.GreyboxRoot);
            if (greybox == null) { Debug.LogError("[Props] Open the greybox scene first."); return; }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Place Warehouse Props");
            int undoGroup = Undo.GetCurrentGroup();

            var old = GameObject.Find(PropsRoot);
            if (old != null) Undo.DestroyObjectImmediate(old);

            rng = new System.Random(Seed);
            prefabCache.Clear();
            blockers.Clear();
            lightCount = camCount = 0;
            CollectBlockers();

            var root = new GameObject(PropsRoot).transform;

            foreach (string roomName in WallMountRooms)
            {
                var roomT = greybox.transform.Find(roomName);
                if (roomT == null) continue;
                var room = ReadRoom(roomT);
                var output = Kit.NewChild(roomName, root);

                AddDoorKeepOuts(room);
                if (FloorProps.TryGetValue(roomName, out var theme))
                {
                    var floorGroup = Kit.NewChild("Floor Props", output);
                    foreach (var side in room.sides) FloorPropsAlong(side, theme.palette, theme.fill, room.height, floorGroup);
                    Ducts(room, Kit.NewChild("Ducts", output));
                    HangingLights(room, Kit.NewChild("Hanging Lights", output));
                }
                WallMounts(room, Kit.NewChild("Wall Mounted", output));
            }

            CorridorLights(greybox.transform.Find("Corridors"), Kit.NewChild("Corridors", root));

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);

            // Registered at the END, once the whole tree exists, so Ctrl+Z removes all of it at once
            // and Ctrl+Y brings all of it back.
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Place Warehouse Props");
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log($"[Props] Placed {root.GetComponentsInChildren<MeshRenderer>().Length} prop meshes, " +
                      $"{lightCount} lights and {camCount} security cameras. Save the scene to keep them.");
        }

        // ==================================================================
        // READING THE GREYBOX
        // ==================================================================
        // Everything about one outer wall that the placement code needs.
        class WallSide
        {
            public bool alongX;        // the wall runs along X (north/south walls) or Z (east/west)
            public float sign;         // +1 or -1: which way "into the room" is along the other axis
            public float face;         // the inner face's coordinate on that other axis
            public float from = float.MaxValue, to = float.MinValue;  // the wall's extent along itself
            public readonly List<Vector2> solids = new List<Vector2>(); // full-height pieces, as [from, to]
            public readonly List<Bounds> doors = new List<Bounds>();    // lintel boxes = doorway spans

            // A point `offset` metres into the room from the face, at `along` along the wall, height y.
            public Vector3 Point(float along, float y, float offset) =>
                alongX ? new Vector3(along, y, face + sign * offset) : new Vector3(face + sign * offset, y, along);

            // Is [a, b] all on solid wall (not over a doorway)? Wall-mounted things need that.
            public bool OnSolid(float a, float b) => solids.Exists(s => a >= s.x && b <= s.y);
        }

        class RoomInfo
        {
            public string name;
            public Bounds floor;
            public float height;
            public readonly List<WallSide> sides = new List<WallSide>();
        }

        static RoomInfo ReadRoom(Transform roomT)
        {
            var room = new RoomInfo { name = roomT.name, floor = Kit.BoxOf(roomT.Find("Floor")) };
            var walls = roomT.Find("Walls");
            foreach (var mf in walls.GetComponentsInChildren<MeshFilter>())
                room.height = Mathf.Max(room.height, Kit.BoxOf(mf.transform).max.y);

            foreach (Transform sideT in walls)
            {
                var side = new WallSide();
                switch (sideT.name)
                {
                    case "Wall_North": side.alongX = true;  side.sign = -1f; break;
                    case "Wall_South": side.alongX = true;  side.sign = +1f; break;
                    case "Wall_East":  side.alongX = false; side.sign = -1f; break;
                    case "Wall_West":  side.alongX = false; side.sign = +1f; break;
                    default: continue;
                }

                foreach (Transform piece in sideT)
                {
                    Bounds b = Kit.BoxOf(piece);
                    float a0 = side.alongX ? b.min.x : b.min.z, a1 = side.alongX ? b.max.x : b.max.z;
                    side.from = Mathf.Min(side.from, a0);
                    side.to = Mathf.Max(side.to, a1);
                    // The inner face is the box's side that points into the room.
                    float acrossMin = side.alongX ? b.min.z : b.min.x, acrossMax = side.alongX ? b.max.z : b.max.x;
                    side.face = side.sign > 0f ? acrossMax : acrossMin;

                    if (piece.name.StartsWith("Solid_")) side.solids.Add(new Vector2(a0, a1));
                    else if (piece.name.StartsWith("Lintel_")) side.doors.Add(b);
                }
                if (side.solids.Count > 0) room.sides.Add(side);
            }
            return room;
        }

        // Everything already in the scene that a new prop must not overlap.
        static void CollectBlockers()
        {
            // PropBuilder's props (cover, racks, door frames...).
            var propBuilder = GameObject.Find("OVERRIDE_Props");
            if (propBuilder != null)
                foreach (var r in propBuilder.GetComponentsInChildren<MeshRenderer>()) blockers.Add(r.bounds);

            // The drone bays and their blocking crates: a bay's hatch must never get buried.
            var bays = GameObject.Find("OVERRIDE_DroneBays");
            if (bays != null)
                foreach (var bay in bays.GetComponentsInChildren<Override.Level.DroneBay>())
                {
                    var keepClear = new Bounds(bay.transform.position + Vector3.up * 1.5f, new Vector3(3f, 3f, 3f));
                    keepClear.Encapsulate(bay.mouth.position + bay.mouth.forward * 3.5f); // and 3.5 m in front of the hatch
                    blockers.Add(keepClear);
                    if (bay.blocker != null) blockers.Add(new Bounds(bay.blocker.position + Vector3.up * 0.6f, Vector3.one * 1.4f));
                }

            // The kit's wall attachments. Only the "Walls" groups: the kit FLOOR tiles would "overlap"
            // every prop standing on them, since touching counts as overlapping.
            var dressing = GameObject.Find(Kit.DressingRoot);
            if (dressing != null)
                foreach (Transform room in dressing.transform)
                {
                    var walls = room.Find("Walls");
                    if (walls == null) continue;
                    foreach (var r in walls.GetComponentsInChildren<MeshRenderer>()) blockers.Add(r.bounds);
                }

            // Maintenance's maze walls, padded so the gaps between them stay wide enough to walk through.
            var maze = GameObject.Find(Kit.GreyboxRoot + "/Maintenance/Walls");
            if (maze != null)
                foreach (Transform w in maze.transform)
                    if (w.name.StartsWith("Wall_Interior"))
                    {
                        var b = Kit.BoxOf(w);
                        b.Expand(new Vector3(MazeClear * 2f, 0f, MazeClear * 2f)); // Expand grows the SIZE, so x2
                        blockers.Add(b);
                    }
        }

        // A box in front of each doorway (plus a bit to each side) where no floor prop may go.
        static void AddDoorKeepOuts(RoomInfo room)
        {
            foreach (var side in room.sides)
                foreach (var door in side.doors)
                {
                    float a0 = (side.alongX ? door.min.x : door.min.z) - DoorSideClear;
                    float a1 = (side.alongX ? door.max.x : door.max.z) + DoorSideClear;
                    Vector3 p = side.Point(a0, 0f, 0f), q = side.Point(a1, room.height, DoorKeepOut);
                    var keepOut = new Bounds();
                    keepOut.SetMinMax(Vector3.Min(p, q), Vector3.Max(p, q)); // Min/Max: the corners in any order
                    blockers.Add(keepOut);
                }
        }

        static bool Blocked(Bounds b)
        {
            b.Expand(Padding * 2f);
            foreach (var other in blockers) if (b.Intersects(other)) return true;
            return false;
        }

        // ==================================================================
        // FLOOR PROPS: walk along a wall, dropping props with gaps between them
        // ==================================================================
        static void FloorPropsAlong(WallSide side, string[] palette, float fill, float height, Transform output)
        {
            float cursor = side.from + 1f;
            while (cursor < side.to - 1f)
            {
                // `fill` is the chance of trying a prop here; otherwise leave an empty stretch.
                if (rng.NextDouble() > fill) { cursor += Range(2f, 6f); continue; }

                var prefab = Load(Pick(palette));
                if (prefab == null) { cursor += 2f; continue; }
                var slot = Kit.NewChild(prefab.name, output);
                var piece = Kit.Spawn(prefab, slot);

                // Long side along the wall, like things pushed against a wall in a real warehouse.
                // Then a random half turn, so repeated props don't all look identical.
                Bounds b = Kit.VisualBounds(piece);
                float yaw = ((b.size.x >= b.size.z) != side.alongX ? 90f : 0f) + (rng.Next(2) == 0 ? 180f : 0f);
                piece.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * piece.transform.localRotation;

                // Pin the prop's box: its start at the cursor, its back BackGap off the wall, its bottom on the floor.
                b = Kit.VisualBounds(piece);
                float back = side.sign > 0f ? 0f : 1f; // which face of the box is the "back" (0 = min, 1 = max)
                var pivot = side.alongX ? new Vector3(0f, 0f, back) : new Vector3(back, 0f, 0f);
                slot.position += side.Point(cursor, 0f, BackGap) - (b.min + Vector3.Scale(b.size, pivot));

                b = Kit.VisualBounds(piece);
                float end = side.alongX ? b.max.x : b.max.z;
                if (Blocked(b) || end > side.to - 0.5f || b.max.y > height - 0.5f)
                {
                    // Doesn't fit here: throw it away and try a little further along.
                    Object.DestroyImmediate(slot.gameObject);
                    cursor += 1f;
                    continue;
                }

                foreach (var body in piece.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                blockers.Add(b);   // the next prop must not overlap this one
                cursor = end + Range(0.5f, 4f);
            }
        }

        // ==================================================================
        // DUCTS: one straight run high along the room's longest wall
        // ==================================================================
        static void Ducts(RoomInfo room, Transform output)
        {
            WallSide longest = null;
            foreach (var s in room.sides) if (longest == null || s.to - s.from > longest.to - longest.from) longest = s;
            if (longest == null) return;

            // 2.2 m under the ceiling: above every doorway (corridors are 7 m), below the kit's beams.
            float y = room.height - 2.2f;
            float cursor = longest.from + 1f;
            while (cursor + 6f < longest.to - 1f)
            {
                var slot = Kit.NewChild("Duct", output);
                var piece = Kit.Spawn(Load(DuctStraight), slot);
                if (piece == null) { Object.DestroyImmediate(slot.gameObject); return; }

                Bounds b = Kit.VisualBounds(piece);
                if ((b.size.x >= b.size.z) != longest.alongX)
                    piece.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * piece.transform.localRotation;
                b = Kit.VisualBounds(piece);

                // Start at the cursor, centred 1.2 m off the wall, centred on height y.
                var pivot = longest.alongX ? new Vector3(0f, 0.5f, 0.5f) : new Vector3(0.5f, 0.5f, 0f);
                slot.position += longest.Point(cursor, y, 1.2f) - (b.min + Vector3.Scale(b.size, pivot));

                b = Kit.VisualBounds(piece);
                if (Blocked(b)) Object.DestroyImmediate(slot.gameObject);  // e.g. one of PropBuilder's pipes
                else blockers.Add(b);   // ducts keep their colliders: drones fly up here
                cursor += longest.alongX ? b.size.x : b.size.z;
            }
        }

        // ==================================================================
        // HANGING LIGHTS: on the same 6 m grid as the kit ceiling, every other cell
        // ==================================================================
        static void HangingLights(RoomInfo room, Transform output)
        {
            Bounds f = room.floor;
            int nx = Mathf.Max(1, Mathf.RoundToInt(f.size.x / 6f));
            int nz = Mathf.Max(1, Mathf.RoundToInt(f.size.z / 6f));
            float cx = f.size.x / nx, cz = f.size.z / nz;

            // Odd columns and even rows: the cell centres between the kit's beams (even grid lines)
            // and away from the sprinkler runs (odd rows).
            for (int i = 1; i < nx; i += 2)
                for (int j = 0; j < nz; j += 2)
                {
                    var top = new Vector3(f.min.x + (i + 0.5f) * cx, room.height, f.min.z + (j + 0.5f) * cz);
                    var slot = Kit.NewChild("Hanging Light", output);
                    var piece = Kit.Spawn(Load(HangingLight), slot);
                    if (piece == null) { Object.DestroyImmediate(slot.gameObject); return; }

                    Bounds b = Kit.VisualBounds(piece);
                    slot.position += top - new Vector3(b.center.x, b.max.y, b.center.z); // hang from its top
                    b = Kit.VisualBounds(piece);
                    if (Blocked(b)) { Object.DestroyImmediate(slot.gameObject); continue; }
                    blockers.Add(b);

                    // A real light on every second fixture, just under the lamp. Range 16 m reaches the
                    // floor 10 m below and overlaps the neighbours, so there are no dark patches.
                    if ((i / 2 + j / 2) % 2 == 0)
                        AddLight(slot, new Vector3(b.center.x, b.min.y - 0.3f, b.center.z), 16f, 2.5f);
                }
        }

        // ==================================================================
        // WALL-MOUNTED: security cameras and fire extinguishers
        // ==================================================================
        static void WallMounts(RoomInfo room, Transform output)
        {
            foreach (var side in room.sides)
            {
                // Cameras near the top of the wall: one in the middle of a short wall, two on a long one.
                // Named ARGUS_Cam so the Director/Agent Controller code can find them later (the GDD's
                // tier-1 subsystem is ARGUS's cameras). They're decoration for now, not real Cameras.
                float length = side.to - side.from;
                float[] spots = length > 40f ? new[] { 0.25f, 0.75f } : new[] { 0.5f };
                foreach (float t in spots)
                {
                    float a = side.from + t * length;
                    if (!side.OnSolid(a - 0.5f, a + 0.5f)) continue;
                    var cam = MountOnWall(SecurityCam, output, side, a, room.height - 2.5f);
                    if (cam != null) cam.name = "ARGUS_Cam_" + (++camCount);
                }

                // An extinguisher beside every doorway, 1.2 m from its edge, like fire regulations want.
                foreach (var door in side.doors)
                {
                    float a = (side.alongX ? door.max.x : door.max.z) + 1.2f;
                    if (side.OnSolid(a - 0.3f, a + 0.3f)) MountOnWall(Extinguisher, output, side, a, 0.3f);
                }
            }
        }

        // ==================================================================
        // CORRIDORS: wall lights (the corridors keep their greybox ceilings, so they need light)
        // ==================================================================
        static void CorridorLights(Transform corridors, Transform output)
        {
            if (corridors == null) return;
            foreach (Transform c in corridors)
            {
                // The vents and the dark tunnel stay dark: that's their whole point in the GDD.
                if (c.name.StartsWith("Vent_") || c.name.StartsWith("Tunnel_")) continue;
                var floor = c.Find("Floor");
                if (floor == null) continue;
                Vector3 middle = Kit.BoxOf(floor).center;
                var group = Kit.NewChild(c.name, output);

                foreach (Transform wallT in c)
                {
                    if (!wallT.name.StartsWith("Wall_")) continue;
                    Bounds b = Kit.BoxOf(wallT);

                    // A corridor wall is a single box, so "into the corridor" is worked out from where
                    // the corridor's floor is, rather than from the wall's name.
                    var side = new WallSide { alongX = b.size.x > b.size.z };
                    side.sign = Mathf.Sign(side.alongX ? middle.z - b.center.z : middle.x - b.center.x);
                    side.face = side.alongX ? (side.sign > 0 ? b.max.z : b.min.z) : (side.sign > 0 ? b.max.x : b.min.x);
                    side.from = side.alongX ? b.min.x : b.min.z;
                    side.to = side.alongX ? b.max.x : b.max.z;
                    side.solids.Add(new Vector2(side.from, side.to));

                    float y = Mathf.Min(3.5f, b.max.y - 1f);
                    int n = 0;
                    for (float a = side.from + 2.5f; a < side.to - 1f; a += 5f)
                    {
                        var lamp = MountOnWall(WallLight, group, side, a, y);
                        // Every second lamp gets a real light; range 8 m spans the corridor's width.
                        if (lamp != null && n++ % 2 == 0)
                            AddLight(lamp.transform, side.Point(a, y, 0.6f), 8f, 1.5f);
                    }
                }
            }
        }

        // ==================================================================
        // SHARED PLACEMENT HELPERS
        // ==================================================================
        // Fixes a wall-mounted prop to the wall at `along`, with its bottom at height y.
        //
        // Which way does a wall prop face? A wall-mounted model is built with its pivot ON the wall
        // and the body sticking out one side (the camera's body is 0.15-0.54 m out along -X in the
        // FBX). So the arrow from the pivot to the middle of the model points away from the wall.
        // The prop is turned (in quarter turns) until that arrow points into the room.
        static GameObject MountOnWall(string path, Transform parent, WallSide side, float along, float y)
        {
            var prefab = Load(path);
            if (prefab == null) return null;
            var slot = Kit.NewChild(prefab.name, parent);
            var piece = Kit.Spawn(prefab, slot);

            Vector3 inward = side.alongX ? new Vector3(0f, 0f, side.sign) : new Vector3(side.sign, 0f, 0f);
            Vector3 outFromWall = Kit.VisualBounds(piece).center - piece.transform.position;
            outFromWall.y = 0f;
            if (outFromWall.sqrMagnitude > 1e-6f)
            {
                // SignedAngle = how far to turn outFromWall (around the up axis) to reach inward.
                // Rounded to 90 degrees, because walls only ever face along X or Z.
                float angle = Vector3.SignedAngle(outFromWall, inward, Vector3.up);
                angle = Mathf.Round(angle / 90f) * 90f;
                piece.transform.localRotation = Quaternion.Euler(0f, angle, 0f) * piece.transform.localRotation;
            }

            // Back of the box against the wall face, centred on `along`, bottom at y.
            Bounds b = Kit.VisualBounds(piece);
            float back = side.sign > 0f ? 0f : 1f;
            var pivot = side.alongX ? new Vector3(0.5f, 0f, back) : new Vector3(back, 0f, 0.5f);
            slot.position += side.Point(along, y, 0f) - (b.min + Vector3.Scale(b.size, pivot));

            Kit.DisableColliders(piece);
            return slot.gameObject;
        }

        static void AddLight(Transform parent, Vector3 position, float range, float intensity)
        {
            var go = new GameObject("Point Light");
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = LampColour;
            // No shadows: dozens of shadow-casting point lights would wreck the frame rate.
            // Turn shadows on for a few hero lights by hand once the lighting pass starts.
            light.shadows = LightShadows.None;
            lightCount++;
        }

        static GameObject Load(string path)
        {
            if (prefabCache.TryGetValue(path, out var cached)) return cached;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Props + path + ".prefab");
            if (prefab == null) Debug.LogWarning("[Props] Missing " + Props + path + ".prefab (skipped).");
            prefabCache[path] = prefab;
            return prefab;
        }

        static string Pick(string[] options) => options[rng.Next(options.Length)];
        static float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
