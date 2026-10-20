// WarehouseKitDresser.cs
// Dresses the greybox's three big rooms (Labs, Server Hall, Maintenance) with the SciFi Warehouse Kit.
// Run it from the menu bar: OVERRIDE > Dress With Warehouse Kit. Run it again any time: it clears its
// old dressing first, so the result is always the same.
//
// THE BIG IDEA: LOOKS FROM THE KIT, COLLISION FROM THE GREYBOX
//   The greybox boxes stay exactly where they are. Only their RENDERERS are switched off, so they
//   become invisible but still block the player, the drones' raycasts and the NavMesh bake.
//   The kit pieces sit on top as pure decoration with their colliders switched off.
//   Why: the greybox's colliders are simple boxes that match the level plan exactly. The kit's
//   colliders are detailed meshes that would snag the player and slow down physics. And if the kit
//   is ever removed, the game still plays exactly the same.
//
// WHERE THE SIZES COME FROM
//   Nothing here repeats the level plan. The script reads each room's floor and wall boxes from the
//   open scene. So if LevelSize.Plan changes and the greybox is rebuilt, re-running this still fits.
//
// WHAT IS LEFT AS GREYBOX: the Core (it's round; the kit has no curved walls), the corridors, the
// airlock, the lintels above doorways and Maintenance's interior walls. They keep your textures.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Override.EditorTools
{
    public static class WarehouseKitDresser
    {
        // ------------------------------------------------------------------
        // NAMES AND PATHS
        // ------------------------------------------------------------------
        // "internal" = visible to other scripts in the same assembly (WarehouseKitProps uses these).
        internal const string GreyboxRoot = "OVERRIDE_Greybox";        // made by GreyboxBuilder
        internal const string DressingRoot = "OVERRIDE_WarehouseKit";   // everything this script makes goes in here
        const string Kit = "Assets/SciFi Warehouse Kit/Prefabs/Structures/";

        static readonly string[] DressedRooms = { "Labs", "ServerHall", "Maintenance" };

        // The kit is built on a 6 m grid: walls, floor tiles and ceiling tiles are all 6 m wide.
        // Each room is cut into a whole number of cells close to 6 m, so pieces stretch by a few %.
        const float Grid = 6f;

        // Wall segments shorter than this (thin slices between two doorways) stay greybox:
        // squeezing a 6 m panel into 1 m would look broken.
        const float MinPanelLength = 2f;

        // The random choices (which floor tile, which pipe) use a fixed seed, so every run gives the
        // SAME level. A random level each run would make your git diffs noisy and confuse teammates.
        const int Seed = 2026;

        // The pieces used. Paths are relative to Kit and without ".prefab".
        static readonly string[] FloorTiles = { "Floor/Floor Tile 01", "Floor/Floor Tile 02", "Floor/Floor Tile 03", "Floor/Floor Tile 04" };
        const string WallPlain       = "Walls/Wall Plain";
        const string WallSealedDoor  = "Walls/Wall DoorClosed"; // a locked door: fits ARGUS sealing the facility
        const string CeilingClosed   = "Ceiling/Ceiling Closed";
        const string CeilingSkylight = "Ceiling/Ceiling Skylight";
        const string Beam            = "Pillars and Beams/Ceiling Support";
        const string WallSupport     = "Pillars and Beams/Wall Support";
        const string Fusebox         = "Structure Props/Fusebox 01";
        // Only the pipes that are modelled from the FLOOR up. Pipes 03, 04 and 06 hang from the
        // ceiling instead (measured from the FBX files), so they'd float if used on a wall seam.
        static readonly string[] SeamPipes  = { "Structure Props/Pipes 01", "Structure Props/Pipes 02", "Structure Props/Pipes 05" };
        // Sprinkler 06 is 3 m long rather than 6, so it would be stretched to double length.
        static readonly string[] Sprinklers = { "Structure Props/Sprinkler 01", "Structure Props/Sprinkler 02",
                                                "Structure Props/Sprinkler 03", "Structure Props/Sprinkler 04", "Structure Props/Sprinkler 05" };

        // The same static flags as the greybox, for lightmaps, occlusion culling and batching.
        const StaticEditorFlags LevelStatic =
            StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;

        static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
        static System.Random rng;
        static int panelCount;

        // ==================================================================
        // ENTRY POINTS
        // ==================================================================
        [MenuItem("OVERRIDE/Dress With Warehouse Kit")]
        static void Dress()
        {
            var greybox = GameObject.Find(GreyboxRoot);
            if (greybox == null)
            {
                Debug.LogError("[Kit] Open the greybox scene first (no '" + GreyboxRoot + "' in this scene).");
                return;
            }

            // Every change below goes into ONE undo step, so a single Ctrl+Z takes it all back.
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Dress With Warehouse Kit");
            int undoGroup = Undo.GetCurrentGroup();

            RemoveDressing(greybox);           // start clean, so running twice never doubles up
            prefabCache.Clear();
            rng = new System.Random(Seed);
            panelCount = 0;

            var root = new GameObject(DressingRoot);
            Undo.RegisterCreatedObjectUndo(root, "Dress With Warehouse Kit");

            foreach (string roomName in DressedRooms)
            {
                var room = greybox.transform.Find(roomName);
                if (room == null) { Debug.LogWarning("[Kit] No room called " + roomName + " in the greybox."); continue; }
                DressRoom(room, NewChild(roomName, root.transform));
            }

            int pieces = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, LevelStatic);
                if (t.GetComponent<MeshRenderer>() != null) pieces++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[Kit] Dressed {DressedRooms.Length} rooms with {pieces} kit meshes. Save the scene to keep it.");
        }

        [MenuItem("OVERRIDE/Remove Warehouse Kit Dressing")]
        static void Remove()
        {
            var greybox = GameObject.Find(GreyboxRoot);
            Undo.SetCurrentGroupName("Remove Warehouse Kit Dressing");
            RemoveDressing(greybox);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // Deletes the kit pieces and makes the greybox visible again.
        static void RemoveDressing(GameObject greybox)
        {
            var old = GameObject.Find(DressingRoot);
            if (old != null) Undo.DestroyObjectImmediate(old);
            if (greybox == null) return;

            foreach (string roomName in DressedRooms)
            {
                var room = greybox.transform.Find(roomName);
                if (room == null) continue;
                foreach (var r in room.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Undo.RecordObject(r, "Show greybox");
                    r.enabled = true;
                }
            }
        }

        // ==================================================================
        // ONE ROOM
        // ==================================================================
        static void DressRoom(Transform room, Transform output)
        {
            var floor = room.Find("Floor");
            var walls = room.Find("Walls");
            if (floor == null || walls == null) { Debug.LogWarning("[Kit] " + room.name + " has no Floor or Walls."); return; }

            // Read the room's size from its greybox floor box.
            Bounds f = BoxOf(floor);
            float x0 = f.min.x, x1 = f.max.x, z0 = f.min.z, z1 = f.max.z;

            // The room's height is the top of its tallest wall piece.
            float height = 0f;
            foreach (var mf in walls.GetComponentsInChildren<MeshFilter>()) height = Mathf.Max(height, BoxOf(mf.transform).max.y);

            // Cut the room into cells as close to 6 m as possible. RoundToInt picks the nearest whole
            // number, so 70 m becomes 12 cells of 5.83 m (squeezed 3%) rather than 11 of 6.36 m.
            int nx = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / Grid));
            int nz = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / Grid));
            float cx = (x1 - x0) / nx, cz = (z1 - z0) / nz;
            bool longX = (x1 - x0) >= (z1 - z0);   // which way the room is longer
            cellX0 = x0; cellZ0 = z0; cellW = cx; cellD = cz;   // remembered for Cell() below

            // Pivot = which point of a piece's box gets pinned to the target. (0.5, 1, 0.5) means
            // "middle of the TOP face", (0.5, 0, 0.5) "middle of the BOTTOM face".
            var top = new Vector3(0.5f, 1f, 0.5f);
            var bottom = new Vector3(0.5f, 0f, 0.5f);
            var stretchXZ = new Vector3(1f, 0f, 1f);  // stretch across the floor, keep the thickness

            // ---------------- Floor ----------------
            // Tile tops at y = 0, exactly where the greybox floor's top (and its collider) is,
            // so the player's feet line up with what they see.
            var floorGroup = NewChild("Floor", output);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    // Mostly the plain tile, with a variant now and then so the floor isn't a repeating grid.
                    string tile = rng.NextDouble() < 0.75 ? FloorTiles[0] : FloorTiles[1 + rng.Next(FloorTiles.Length - 1)];
                    Place(tile, floorGroup, true, Cell(i, j, 0f), stretchXZ, top);
                }
            Hide(floor);

            // ---------------- Ceiling ----------------
            // Bottom face at the room height, so it rests on the tops of the walls.
            // A line of skylights down the middle lets the sun in as light shafts.
            var ceilingGroup = NewChild("Ceiling", output);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    bool skylight = i % 4 == 2 && j == nz / 2;
                    Place(skylight ? CeilingSkylight : CeilingClosed, ceilingGroup, true, Cell(i, j, height), stretchXZ, bottom);
                }

            // ---------------- Beams ----------------
            // On every second grid line, running ACROSS the room (the short way), hanging under the
            // ceiling along the tile seams. One 6 m beam per cell, stretched like the tiles.
            var beamGroup = NewChild("Beams", output);
            if (longX)
            {
                for (int i = 2; i < nx; i += 2)
                    for (int j = 0; j < nz; j++)
                        Place(Beam, beamGroup, false, Line(x0 + i * cx, height, z0 + (j + 0.5f) * cz, 0f, cz), new Vector3(0f, 0f, 1f), top);
            }
            else
            {
                for (int j = 2; j < nz; j += 2)
                    for (int i = 0; i < nx; i++)
                        Place(Beam, beamGroup, true, Line(x0 + (i + 0.5f) * cx, height, z0 + j * cz, cx, 0f), new Vector3(1f, 0f, 0f), top);
            }

            // ---------------- Sprinklers ----------------
            // Sprinkler runs go the LONG way, under the beams. In the kit, beams are 0.6 m deep and
            // sprinklers start where beams end, so their tops sit 0.6 m below the ceiling.
            var sprinklerGroup = NewChild("Sprinklers", output);
            float sprinklerTop = height - 0.6f;
            if (longX)
            {
                for (int j = 1; j < nz; j += 2)
                    for (int i = 0; i < nx; i++)
                        Place(Pick(Sprinklers), sprinklerGroup, true, Line(x0 + (i + 0.5f) * cx, sprinklerTop, z0 + (j + 0.5f) * cz, cx, 0f), new Vector3(1f, 0f, 0f), top);
            }
            else
            {
                for (int i = 1; i < nx; i += 2)
                    for (int j = 0; j < nz; j++)
                        Place(Pick(Sprinklers), sprinklerGroup, false, Line(x0 + (i + 0.5f) * cx, sprinklerTop, z0 + (j + 0.5f) * cz, 0f, cz), new Vector3(0f, 0f, 1f), top);
            }

            // ---------------- Walls ----------------
            // GreyboxBuilder names the four outer walls Wall_North / South / East / West, and splits each
            // into "Solid_n" pieces (full height, between doorways) and "Lintel_n" pieces (above doorways).
            // Only the Solid pieces are dressed, so doorways stay exactly where the greybox cut them.
            var wallGroup = NewChild("Walls", output);
            foreach (Transform side in walls)
            {
                // Which way is "into the room" from this wall? Attachments must face that way.
                Vector3 inward;
                switch (side.name)
                {
                    case "Wall_North": inward = Vector3.back;    break;
                    case "Wall_South": inward = Vector3.forward; break;
                    case "Wall_East":  inward = Vector3.left;    break;
                    case "Wall_West":  inward = Vector3.right;   break;
                    default: continue;   // Wall_Interior_n: Maintenance's maze walls stay greybox
                }
                bool alongX = inward.x == 0f;   // north and south walls run along X

                foreach (Transform piece in side)
                    if (piece.name.StartsWith("Solid_"))
                        DressWallSegment(piece, alongX, inward, NewChild(side.name, wallGroup));
            }
        }

        // ------------------------------------------------------------------
        // ONE STRAIGHT PIECE OF WALL -> a row of 6 m panels
        // ------------------------------------------------------------------
        static void DressWallSegment(Transform solid, bool alongX, Vector3 inward, Transform output)
        {
            Bounds w = BoxOf(solid);
            float from = alongX ? w.min.x : w.min.z;
            float to   = alongX ? w.max.x : w.max.z;
            float length = to - from;
            if (length < MinPanelLength) return;   // too short: leave the greybox piece showing

            int n = Mathf.Max(1, Mathf.RoundToInt(length / Grid));
            float step = length / n;

            for (int k = 0; k < n; k++)
            {
                float mid = from + (k + 0.5f) * step;
                // The target box: one panel wide, full wall height, zero thickness (= centred on the wall line).
                var centre = alongX ? new Vector3(mid, w.center.y, w.center.z) : new Vector3(w.center.x, w.center.y, mid);
                var size   = alongX ? new Vector3(step, w.size.y, 0f)       : new Vector3(0f, w.size.y, step);

                // Patterns, counted across the whole level so neighbouring walls don't all match:
                //   - every 9th panel is a sealed door (never the first or last, which touch corners/doorways)
                //   - the seam at the start of each panel gets a wall support, except every 3rd
                //     inner seam, which gets a pipe run instead (some pipes reach into the neighbouring
                //     panels, so only inner seams, never next to a doorway)
                //   - every 5th plain panel gets a fusebox
                int p = panelCount++;
                bool inner = k > 0 && k < n - 1;
                bool sealedDoor = inner && p % 9 == 4;
                bool pipeSeam = inner && !sealedDoor && k % 3 == 1;
                bool fusebox = !sealedDoor && !pipeSeam && p % 5 == 2;

                WallPanel(sealedDoor ? WallSealedDoor : WallPlain,
                          pipeSeam ? Pick(SeamPipes) : WallSupport,
                          fusebox ? Fusebox : null,
                          output, alongX, inward, new Bounds(centre, size));
            }
            Hide(solid);
        }

        // A wall panel plus the things bolted to it.
        //
        // The kit's wall pieces, supports, pipes and fuseboxes all share the SAME origin point: the
        // artist modelled each attachment in the right spot relative to a wall panel placed at (0,0,0).
        // So they're spawned together at that shared origin inside one "Turn" object, then that whole
        // group is turned, stretched and moved as one. The attachments land where the artist meant.
        static void WallPanel(string wallPath, string seamPath, string extraPath, Transform parent,
                              bool alongX, Vector3 inward, Bounds target)
        {
            var wallPrefab = Load(wallPath);
            if (wallPrefab == null) return;

            var slot = NewChild(wallPrefab.name, parent);  // stretched and moved
            var turn = NewChild("Turn", slot);             // rotated
            var wall = Spawn(wallPrefab, turn);
            var seam = Spawn(Load(seamPath), turn);
            var extra = Spawn(Load(extraPath), turn);

            // 1. Turn so the panel runs along the wall. A wall panel is long one way and thin the other;
            //    if its long side doesn't match the wall's direction, a quarter turn fixes it.
            Bounds b = VisualBounds(wall);
            if ((b.size.x >= b.size.z) != alongX) turn.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // 2. Turn it round if the attachments are on the outside. The seam piece (support or pipe)
            //    always sits on the decorated face, so its offset from the wall's centre says which way
            //    that face points. Dot product > 0 means "same direction as inward".
            if (seam != null)
            {
                float side = Vector3.Dot(VisualBounds(seam).center - VisualBounds(wall).center, inward);
                if (side < 0f) turn.localRotation = Quaternion.Euler(0f, 180f, 0f) * turn.localRotation;
            }

            // 3. Stretch the slot so the WALL exactly fills the target's length and height.
            //    The slot itself is never rotated, so its scale always acts along world X / Y / Z.
            b = VisualBounds(wall);
            var scale = Vector3.one;
            if (alongX) scale.x = target.size.x / b.size.x; else scale.z = target.size.z / b.size.z;
            scale.y = target.size.y / b.size.y;
            slot.localScale = scale;

            // 4. Move it so the wall's centre lands on the target's centre (on the greybox wall line).
            slot.position += target.center - VisualBounds(wall).center;

            // 5. A fusebox is modelled at the panel's edge, where it would clash with the support.
            //    Slide it to the middle of the panel.
            if (extra != null)
            {
                Vector3 d = target.center - VisualBounds(extra).center;
                extra.transform.position += alongX ? new Vector3(d.x, 0f, 0f) : new Vector3(0f, 0f, d.z);
            }

            // Decoration only... except the wall support, which sticks 0.4 m out from the wall.
            // Without its collider the player could walk half into it, so it keeps one (if it has one).
            DisableColliders(wall);
            DisableColliders(extra);
            if (seamPath != WallSupport) DisableColliders(seam);
        }

        // ==================================================================
        // GENERAL PLACEMENT
        // ==================================================================
        // Puts one kit piece into the `target` box:
        //   longAlongX  - turn the piece so its long side runs along X (true) or Z (false)
        //   stretch     - per axis: 1 = stretch to the target's size, 0 = keep the piece's own size
        //   pivot       - the point of the piece's box (0..1 per axis) that is pinned to the same point
        //                 of the target box. A target with size 0 on an axis is just a position there.
        //
        // It works from the piece's VISIBLE box, not its pivot, because kit pivots sit in different
        // places (wall panels at one end, beams at the other). The visible box is always right.
        internal static GameObject Place(string path, Transform parent, bool longAlongX, Bounds target, Vector3 stretch, Vector3 pivot)
        {
            var prefab = Load(path);
            if (prefab == null) return null;

            var slot = NewChild(prefab.name, parent);
            var piece = Spawn(prefab, slot);

            Bounds b = VisualBounds(piece);
            if ((b.size.x >= b.size.z) != longAlongX)
                piece.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * piece.transform.localRotation;

            b = VisualBounds(piece);
            var scale = Vector3.one;
            for (int axis = 0; axis < 3; axis++)                 // Vector3 can be indexed: [0]=x [1]=y [2]=z
                if (stretch[axis] > 0f && b.size[axis] > 0.001f)
                    scale[axis] = target.size[axis] / b.size[axis];
            slot.localScale = scale;

            b = VisualBounds(piece);
            Vector3 want = target.min + Vector3.Scale(target.size, pivot);
            Vector3 have = b.min + Vector3.Scale(b.size, pivot);
            slot.position += want - have;

            DisableColliders(piece);
            return slot.gameObject;
        }

        // The box a piece takes up in the world, worked out from its meshes.
        // Each mesh knows its own box in local space; its 8 corners are moved into world space and
        // a box is grown around them. (Done by hand rather than with Renderer.bounds so it's always
        // up to date straight after a transform change, with no wait for the next editor frame.)
        internal static Bounds VisualBounds(GameObject go)
        {
            bool any = false;
            var result = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds local = mf.sharedMesh.bounds;
                Matrix4x4 toWorld = mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    // i's three bits pick - or + on each axis: all 8 corners of the box.
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = toWorld.MultiplyPoint3x4(corner);
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        // A greybox piece is a stretched unit cube, so its box is simply position +/- scale / 2.
        internal static Bounds BoxOf(Transform t)
        {
            Vector3 s = t.lossyScale;
            return new Bounds(t.position, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        // ==================================================================
        // SMALL HELPERS
        // ==================================================================
        // Cell (i, j) of the room being dressed, at height y: one grid cell wide, zero thick.
        // DressRoom sets the room's corner and cell size before using it.
        static float cellX0, cellZ0, cellW, cellD;
        static Bounds Cell(int i, int j, float y) =>
            new Bounds(new Vector3(cellX0 + (i + 0.5f) * cellW, y, cellZ0 + (j + 0.5f) * cellD), new Vector3(cellW, 0f, cellD));

        // A thin target at (x, y, z), `lengthX` long along X or `lengthZ` long along Z.
        static Bounds Line(float x, float y, float z, float lengthX, float lengthZ) =>
            new Bounds(new Vector3(x, y, z), new Vector3(lengthX, 0f, lengthZ));

        static void Hide(Transform greyboxPiece)
        {
            var r = greyboxPiece.GetComponent<MeshRenderer>();
            if (r == null) return;
            Undo.RecordObject(r, "Hide greybox");
            r.enabled = false;   // invisible, but its collider still works
        }

        // Switched off rather than deleted: deleting a component from a prefab instance is fiddly,
        // while "enabled = false" is a normal override, and the scene file stays small.
        internal static void DisableColliders(GameObject go)
        {
            if (go == null) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        // InstantiatePrefab (not Instantiate) keeps each piece linked to its kit prefab, so the scene
        // only stores "which prefab, and where", not a full copy of every mesh and material.
        internal static GameObject Spawn(GameObject prefab, Transform parent) =>
            prefab == null ? null : (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

        static GameObject Load(string path)
        {
            if (path == null) return null;
            if (prefabCache.TryGetValue(path, out var cached)) return cached;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + path + ".prefab");
            if (prefab == null) Debug.LogWarning("[Kit] Missing " + Kit + path + ".prefab (skipped).");
            prefabCache[path] = prefab;   // cache misses too, so the warning shows once
            return prefab;
        }

        static string Pick(string[] options) => options[rng.Next(options.Length)];

        internal static Transform NewChild(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }
    }
}
