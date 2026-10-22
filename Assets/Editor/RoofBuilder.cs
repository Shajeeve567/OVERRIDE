// RoofBuilder.cs
// Roofs for the two rooms the Warehouse Kit dresser leaves open: the round Core and the Airlock.
// (Labs, Server Hall and Maintenance already get kit ceilings from "Dress With Warehouse Kit".)
// Run it from the menu bar: OVERRIDE > Build Core And Airlock Roofs. It clears its old roofs first.
//
// CORE: a round room can't be tiled exactly with square 6 m kit tiles, so the roof is three layers:
//   1. Kit ceiling tiles on a 6 m grid centred on the pillar. A tile is kept only if it fits inside
//      the circle (with a little overhang hidden behind the wall). The four tiles that meet over
//      the pillar are SKYLIGHTS, so a shaft of light falls onto ARGUS's core.
//   2. A dark metal disc just above the tiles. Where the square grid can't reach the curved wall,
//      you see this disc, which reads as a shadowed recess rather than a hole to the sky.
//   3. Twelve kit beams running out from the pillar to the wall like spokes, hiding the jagged
//      tile edge and giving the room a "reactor dome" look, plus a ring of red glow (ARGUS's colour).
//
// AIRLOCK: kit ceiling tiles, beams on the grid lines and two hanging lamps. It's the safe start,
// so it's bright, clean and ordinary.
//
// COLLISION: the greybox already has an invisible-to-the-eye answer: its ceiling boxes. They were
// switched OFF (so the sun could light the level while greyboxing). Now that every room has a real
// roof, they're switched back ON with their renderers hidden: drones can't fly out of the level
// and bullets can't escape into the sky, but you only ever see the kit roofs.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Kit = Override.EditorTools.WarehouseKitDresser;

namespace Override.EditorTools
{
    public static class RoofBuilder
    {
        const string RoofsRoot = "OVERRIDE_Roofs";
        const string PropMaterials = "Assets/OVERRIDE/Props/Materials/";
        const string HangingLightPath = "Assets/SciFi Warehouse Kit/Prefabs/Props/Misc Props/Hanging Light.prefab";

        // Kit pieces, as paths the dresser's Place() understands (relative to Prefabs/Structures/).
        const string CeilingClosed = "Ceiling/Ceiling Closed";
        const string CeilingSkylight = "Ceiling/Ceiling Skylight";
        const string Beam = "Pillars and Beams/Ceiling Support";

        const float Grid = 6f;
        const int CoreSpokes = 12;          // one beam every 30 degrees
        const float RedRingRadius = 7f;     // metres from the pillar's centre
        static readonly Color LampColour = new Color(0.85f, 0.93f, 1f);

        // Pivots for Place(): which point of a piece's box is pinned to the target.
        static readonly Vector3 Top = new Vector3(0.5f, 1f, 0.5f), Bottom = new Vector3(0.5f, 0f, 0.5f);

        [MenuItem("OVERRIDE/Build Core And Airlock Roofs")]
        static void Build()
        {
            var greybox = GameObject.Find(Kit.GreyboxRoot);
            if (greybox == null) { Debug.LogError("[Roofs] Open the greybox scene first."); return; }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Roofs");
            int undoGroup = Undo.GetCurrentGroup();

            var old = GameObject.Find(RoofsRoot);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RoofsRoot).transform;

            var core = greybox.transform.Find("Core");
            if (core != null) CoreRoof(core, Kit.NewChild("Core", root));
            var airlock = greybox.transform.Find("Airlock");
            if (airlock != null) AirlockRoof(airlock, Kit.NewChild("Airlock", root));

            SealCeilings(greybox.transform);

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Roofs");
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("[Roofs] Core and Airlock roofs built, greybox ceilings switched on as invisible colliders. Save the scene.");
        }

        // ==================================================================
        // CORE
        // ==================================================================
        static void CoreRoof(Transform core, Transform output)
        {
            // Everything is read from the greybox, so a different LevelSize still fits.
            //   Floor  : a cylinder whose X scale is the diameter (wall included)
            //   Pillar : a cylinder; X scale = diameter, Y scale = HALF its height (Unity's cylinder is 2 m tall)
            var floor = core.Find("Floor");
            var pillar = core.Find("Pillar");
            if (floor == null || pillar == null) { Debug.LogWarning("[Roofs] Core floor or pillar missing."); return; }

            Vector3 centre = pillar.position; centre.y = 0f;
            float pillarRadius = pillar.lossyScale.x / 2f;
            float height = pillar.lossyScale.y * 2f;                  // the pillar reaches the ceiling
            float wallInner = (floor.lossyScale.x - 0.3f) / 2f - 0.15f; // diameter -> radius, minus wall thickness

            // ---- 1. Tiles ----
            // Cell (i, j) spans [centre + i*6, centre + (i+1)*6]. With i and j from -n to n-1, four
            // cells meet exactly over the pillar's centre.
            var tiles = Kit.NewChild("Tiles", output);
            int n = Mathf.CeilToInt(wallInner / Grid);
            for (int i = -n; i < n; i++)
                for (int j = -n; j < n; j++)
                {
                    float x0 = centre.x + i * Grid, z0 = centre.z + j * Grid;
                    // The corner furthest from the centre decides whether the tile fits in the circle.
                    // Up to 1 m of overhang is fine: it's hidden behind (and above) the 0.3 m wall.
                    float fx = Mathf.Max(Mathf.Abs(x0 - centre.x), Mathf.Abs(x0 + Grid - centre.x));
                    float fz = Mathf.Max(Mathf.Abs(z0 - centre.z), Mathf.Abs(z0 + Grid - centre.z));
                    if (Mathf.Sqrt(fx * fx + fz * fz) > wallInner + 1f) continue;

                    bool overPillar = (i == -1 || i == 0) && (j == -1 || j == 0);
                    var cell = new Bounds(new Vector3(x0 + Grid / 2f, height, z0 + Grid / 2f), new Vector3(Grid, 0f, Grid));
                    Kit.Place(overPillar ? CeilingSkylight : CeilingClosed, tiles, true, cell, new Vector3(1f, 0f, 1f), Bottom);
                }

            // ---- 2. Backing disc ----
            // 0.4 m above the tiles' underside, so it sits behind them and never flickers against them.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Backing Disc";
            disc.transform.SetParent(output, false);
            disc.transform.position = centre + Vector3.up * (height + 0.4f);
            disc.transform.localScale = new Vector3((wallInner + 0.15f) * 2f, 0.05f, (wallInner + 0.15f) * 2f);
            Object.DestroyImmediate(disc.GetComponent<Collider>());   // the greybox ceiling does collision
            disc.GetComponent<MeshRenderer>().sharedMaterial = LoadMaterial("Prop_DarkMetal");

            // ---- 3. Spokes ----
            // The dresser's Place() only lines things up with the X or Z axis. So each beam is placed
            // pointing east (+X) inside a "pivot" sitting on the pillar's centre, and then the pivot
            // is turned. Turning a parent turns everything inside it around the parent's position.
            var spokes = Kit.NewChild("Spokes", output);
            float length = wallInner - pillarRadius;
            for (int k = 0; k < CoreSpokes; k++)
            {
                var pivot = Kit.NewChild("Spoke_" + (k + 1), spokes);
                pivot.position = centre;
                var target = new Bounds(centre + new Vector3(pillarRadius + length / 2f, height, 0f), new Vector3(length, 0f, 0f));
                Kit.Place(Beam, pivot, true, target, new Vector3(1f, 0f, 0f), Top);
                pivot.rotation = Quaternion.Euler(0f, k * 360f / CoreSpokes, 0f);
            }

            // ---- 4. Red ring ----
            // A circle made of short glowing strips, just under the ceiling between the spokes.
            var ring = Kit.NewChild("ARGUS Ring", output);
            var red = LoadMaterial("Prop_RedGlow");
            const int segments = 32;
            float segment = 2f * Mathf.PI * RedRingRadius / segments * 1.05f; // 5% overlap closes the gaps
            for (int s = 0; s < segments; s++)
            {
                float a = (s + 0.5f) * 2f * Mathf.PI / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Strip_" + (s + 1);
                strip.transform.SetParent(ring, false);
                strip.transform.position = centre + dir * RedRingRadius + Vector3.up * (height - 0.03f);
                // LookRotation(dir) makes the strip's Z axis point outward, so its X axis runs along the circle.
                strip.transform.rotation = Quaternion.LookRotation(dir);
                strip.transform.localScale = new Vector3(segment, 0.05f, 0.35f);
                Object.DestroyImmediate(strip.GetComponent<Collider>());
                strip.GetComponent<MeshRenderer>().sharedMaterial = red;
            }

            // ---- 5. Lights ----
            // The roof now blocks the sun, so the Core needs its own light: eight lamps in a ring
            // halfway to the wall, under PropBuilder's ceiling fixtures. Faintly warm: ARGUS's room.
            var lights = Kit.NewChild("Lights", output);
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f * Mathf.Deg2Rad;
                var p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (wallInner * 0.6f) + Vector3.up * (height - 3f);
                AddLight(lights, p, 22f, 3.5f, new Color(1f, 0.88f, 0.86f));
            }
        }

        // ==================================================================
        // AIRLOCK
        // ==================================================================
        static void AirlockRoof(Transform airlock, Transform output)
        {
            var floor = airlock.Find("Floor");
            if (floor == null) return;
            Bounds f = Kit.BoxOf(floor);

            float height = 0f;
            foreach (var mf in airlock.GetComponentsInChildren<MeshFilter>()) height = Mathf.Max(height, Kit.BoxOf(mf.transform).max.y);

            int nx = BestCellCount(f.size.x), nz = BestCellCount(f.size.z);
            float cx = f.size.x / nx, cz = f.size.z / nz;

            var tiles = Kit.NewChild("Tiles", output);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var cell = new Bounds(new Vector3(f.min.x + (i + 0.5f) * cx, height, f.min.z + (j + 0.5f) * cz), new Vector3(cx, 0f, cz));
                    Kit.Place(CeilingClosed, tiles, true, cell, new Vector3(1f, 0f, 1f), Bottom);
                }

            // Beams on the inner grid lines, running north-south.
            var beams = Kit.NewChild("Beams", output);
            for (int i = 1; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var target = new Bounds(new Vector3(f.min.x + i * cx, height, f.min.z + (j + 0.5f) * cz), new Vector3(0f, 0f, cz));
                    Kit.Place(Beam, beams, false, target, new Vector3(0f, 0f, 1f), Top);
                }

            // Two lamps over the middle row, each with a real light (the room is only 5 m tall,
            // so a 10 m range covers it).
            var lampPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HangingLightPath);
            var lamps = Kit.NewChild("Lamps", output);
            foreach (float t in new[] { 0.3f, 0.7f })
            {
                var top = new Vector3(f.min.x + t * f.size.x, height, f.center.z);
                if (lampPrefab != null)
                {
                    var slot = Kit.NewChild("Hanging Light", lamps);
                    var lamp = Kit.Spawn(lampPrefab, slot);
                    Bounds b = Kit.VisualBounds(lamp);
                    slot.position += top - new Vector3(b.center.x, b.max.y, b.center.z);
                    Kit.DisableColliders(lamp);
                }
                AddLight(lamps, top + Vector3.down * 1.3f, 10f, 2f, LampColour);
            }
        }

        // How many cells to split a length into so each is as close to 6 m as possible.
        // Compares the two nearest whole numbers by RATIO, so 15 m picks 3 cells of 5 m (17% squash)
        // over 2 of 7.5 m (25% stretch). Plain rounding would have picked 2 (2.5 rounds to the even number).
        static int BestCellCount(float length)
        {
            int low = Mathf.Max(1, Mathf.FloorToInt(length / Grid)), high = low + 1;
            float errLow = Mathf.Abs(Mathf.Log(length / low / Grid)), errHigh = Mathf.Abs(Mathf.Log(length / high / Grid));
            return errLow <= errHigh ? low : high;
        }

        // ==================================================================
        // GREYBOX CEILINGS -> invisible lids
        // ==================================================================
        static void SealCeilings(Transform greybox)
        {
            var ceilings = greybox.Find("Ceilings (enable for lighting work)");
            if (ceilings == null) return;
            Undo.RecordObject(ceilings.gameObject, "Seal ceilings");
            ceilings.gameObject.SetActive(true);
            foreach (var r in ceilings.GetComponentsInChildren<MeshRenderer>(true))
            {
                Undo.RecordObject(r, "Seal ceilings");
                r.enabled = false;   // collider stays, mesh is never drawn
            }
        }

        static void AddLight(Transform parent, Vector3 position, float range, float intensity, Color colour)
        {
            var go = new GameObject("Point Light");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = colour;
            light.shadows = LightShadows.None;
        }

        static Material LoadMaterial(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(PropMaterials + name + ".mat");
    }
}
