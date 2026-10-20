// HunterGridBaker.cs
// Builds the Hunter's grid from the level's own colliders when Play starts.
//
// WHY PHYSICS, NOT THE NAVMESH: the level has no NavMesh bake, and a NavMesh would only describe ONE
// agent size. The Seeker needs two answers per cell (could the player be here? can the Seeker get
// here?), so each cell is tested directly:
//   1. A ray straight down finds the floor (ignoring props that move, like crates and doors).
//   2. A capsule standing on that floor checks the space is clear of static geometry:
//        1.0 m tall  -> the player could be here, crouching (vents are 1.2 m)
//        2.0 m tall  -> the Seeker fits (a 0.8 m orb hovering 1.2 m up), so vents are excluded
//      Walls are found the same way, so no zone or vent volumes have to be placed by hand.
// Doors and crates (anything with a Rigidbody) are skipped here and handled as dynamic obstacles.

using Override.HunterAI;
using UnityEngine;

namespace Override.Hunter
{
    public static class HunterGridBaker
    {
        static readonly RaycastHit[] rayHits = new RaycastHit[16];
        static readonly Collider[] overlaps = new Collider[16];

        public struct Options
        {
            public float CellSize;
            public float PlayerClearance;   // metres of headroom the crouching player needs
            public float HunterClearance;   // metres the Seeker needs
            public float Radius;            // half-width of both capsules
            public Transform LevelRoot;     // optional: only floors under this; otherwise the whole scene
        }

        public static HunterGrid Bake(Options o, out float floorTop)
        {
            var floors = FindFloors(o.LevelRoot);
            if (floors.Length == 0)
            {
                Debug.LogWarning("[Hunter] No floor colliders found (objects named 'Floor...'). The grid is empty.");
                floorTop = 0f;
                return new HunterGrid(Vector2.zero, 1, 1, o.CellSize);
            }

            Bounds area = floors[0].bounds;
            floorTop = float.NegativeInfinity;
            foreach (var f in floors)
            {
                area.Encapsulate(f.bounds);
                floorTop = Mathf.Max(floorTop, f.bounds.max.y);
            }

            int w = Mathf.Max(1, Mathf.CeilToInt(area.size.x / o.CellSize));
            int h = Mathf.Max(1, Mathf.CeilToInt(area.size.z / o.CellSize));
            var grid = new HunterGrid(new Vector2(area.min.x, area.min.z), w, h, o.CellSize);

            Physics.SyncTransforms();
            int playerCells = 0, hunterCells = 0;
            for (int i = 0; i < grid.Count; i++)
            {
                Vector3 c = grid.Centre(i);
                if (!FindFloor(new Vector3(c.x, floorTop + 2f, c.z), floorTop, out float y)) continue;
                grid.FloorY[i] = y;

                Vector3 foot = new Vector3(c.x, y, c.z);
                if (Clear(foot, o.PlayerClearance, o.Radius)) { grid.PlayerWalkable[i] = true; playerCells++; }
                if (grid.PlayerWalkable[i] && Clear(foot, o.HunterClearance, o.Radius)) { grid.HunterWalkable[i] = true; hunterCells++; }
            }

            Debug.Log($"[Hunter] Grid {w} x {h} cells of {o.CellSize} m: {playerCells} player-walkable, " +
                      $"{hunterCells} Seeker-walkable ({playerCells - hunterCells} crouch-only, e.g. vents).");
            return grid;
        }

        static Collider[] FindFloors(Transform root)
        {
            var all = root != null ? root.GetComponentsInChildren<Collider>() :
                      Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            var list = new System.Collections.Generic.List<Collider>();
            foreach (var c in all)
                if (!c.isTrigger && c.attachedRigidbody == null && c.enabled &&
                    c.name.StartsWith("Floor", System.StringComparison.OrdinalIgnoreCase))
                    list.Add(c);
            return list.ToArray();
        }

        static bool IsStatic(Collider c) => c.attachedRigidbody == null && !(c is CharacterController);

        /// The highest floor-like surface below `from`, no higher than a step above the level's floors.
        static bool FindFloor(Vector3 from, float floorTop, out float y)
        {
            y = 0f;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, rayHits, 4f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int k = 0; k < n; k++)
            {
                var hit = rayHits[k];
                if (!IsStatic(hit.collider) || hit.normal.y < 0.7f) continue;
                if (hit.point.y > floorTop + 0.6f) continue;   // a table or rack top, not the floor
                if (hit.point.y > best) best = hit.point.y;
            }
            if (float.IsNegativeInfinity(best)) return false;
            y = best;
            return true;
        }

        /// Is there `height` metres of clear space above this floor point (static geometry only)?
        static bool Clear(Vector3 foot, float height, float radius)
        {
            Vector3 bottom = foot + Vector3.up * (0.15f + radius);
            Vector3 top = foot + Vector3.up * Mathf.Max(0.15f + radius, height - radius);
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
                if (IsStatic(overlaps[k])) return false;
            return true;
        }
    }
}
