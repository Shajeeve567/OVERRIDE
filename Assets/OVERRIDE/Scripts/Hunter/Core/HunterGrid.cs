// HunterGrid.cs
// The level as a flat grid of square cells, seen from above. Plain C#: no GameObjects.
//
// Two walkability masks, because the player and the Seeker fit through different gaps:
//   PlayerWalkable  where the PLAYER might be (includes crouch-only vents), so belief may live there.
//   HunterWalkable  where the SEEKER can travel (a 0.8 m orb hovering 1.2 m up: no vents).
// LINKS: a thin wall can run BETWEEN two cell centres without touching either, so walkable cells are not
// enough: each cell also stores which of its 8 neighbours it can actually move to (one bit each),
// tested once at bake time. Both A* and the belief's spreading only use open links.
// On top of that, Blocked marks cells covered right now by a closed door or a crate, refreshed before
// each plan, plus cells blocked for a few seconds after the Seeker got stuck there.

using UnityEngine;

namespace Override.HunterAI
{
    public sealed class HunterGrid
    {
        public readonly int Width, Height;
        public readonly float CellSize;
        public readonly Vector2 Origin;          // world (x, z) of the grid's south-west corner

        public readonly bool[] PlayerWalkable;
        public readonly bool[] HunterWalkable;
        public readonly float[] FloorY;          // floor height at each cell centre
        public readonly byte[] PlayerLinks;      // bit k set = the player can move to neighbour k
        public readonly byte[] HunterLinks;      // bit k set = the Seeker can move to neighbour k

        /// Neighbour k is at (x + DX[k], z + DZ[k]). Ordered so the opposite of k is 7 − k.
        public static readonly int[] DX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        public static readonly int[] DZ = { -1, -1, -1, 0, 0, 1, 1, 1 };

        readonly bool[] dynamicBlocked;
        readonly float[] blockedUntil;           // stuck recovery: cell avoided until this time

        public int Count => Width * Height;

        public HunterGrid(Vector2 origin, int width, int height, float cellSize)
        {
            Origin = origin;
            Width = width;
            Height = height;
            CellSize = cellSize;
            PlayerWalkable = new bool[width * height];
            HunterWalkable = new bool[width * height];
            FloorY = new float[width * height];
            PlayerLinks = new byte[width * height];
            HunterLinks = new byte[width * height];
            dynamicBlocked = new bool[width * height];
            blockedUntil = new float[width * height];
        }

        public int Index(int x, int z) => z * Width + x;
        public int X(int i) => i % Width;
        public int Z(int i) => i / Width;
        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Height;

        public Vector3 Centre(int i) =>
            new Vector3(Origin.x + (X(i) + 0.5f) * CellSize, FloorY[i], Origin.y + (Z(i) + 0.5f) * CellSize);

        /// The cell under a world position, or -1 if it's off the grid.
        public int CellOf(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x - Origin.x) / CellSize);
            int z = Mathf.FloorToInt((p.z - Origin.y) / CellSize);
            return InBounds(x, z) ? Index(x, z) : -1;
        }

        /// Can the Seeker enter this cell right now?
        public bool Passable(int i, float time) =>
            HunterWalkable[i] && !dynamicBlocked[i] && time >= blockedUntil[i];

        public bool IsDynamicallyBlocked(int i) => dynamicBlocked[i];

        /// Neighbour k of cell i, or -1 if it's off the grid.
        public int Neighbour(int i, int k)
        {
            int x = X(i) + DX[k], z = Z(i) + DZ[k];
            return InBounds(x, z) ? Index(x, z) : -1;
        }

        public bool PlayerLinked(int i, int k) => (PlayerLinks[i] & (1 << k)) != 0;
        public bool HunterLinked(int i, int k) => (HunterLinks[i] & (1 << k)) != 0;

        public void ClearDynamic() => System.Array.Clear(dynamicBlocked, 0, dynamicBlocked.Length);

        /// Marks every cell whose centre lies inside the box (seen from above), grown by `margin`.
        public void BlockBox(Bounds box, float margin)
        {
            int x0 = Mathf.FloorToInt((box.min.x - margin - Origin.x) / CellSize);
            int x1 = Mathf.FloorToInt((box.max.x + margin - Origin.x) / CellSize);
            int z0 = Mathf.FloorToInt((box.min.z - margin - Origin.y) / CellSize);
            int z1 = Mathf.FloorToInt((box.max.z + margin - Origin.y) / CellSize);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(Height - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Width - 1, x1); x++)
                    dynamicBlocked[Index(x, z)] = true;
        }

        public void BlockTemporarily(int i, float until)
        {
            if (i >= 0) blockedUntil[i] = Mathf.Max(blockedUntil[i], until);
        }

        /// The nearest cell (by grid rings) that satisfies the mask, searching up to maxRing cells out.
        public int NearestWhere(int from, bool[] mask, int maxRing)
        {
            if (from < 0) return -1;
            if (mask[from]) return from;
            int fx = X(from), fz = Z(from);
            for (int r = 1; r <= maxRing; r++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;   // ring only
                        int x = fx + dx, z = fz + dz;
                        if (!InBounds(x, z)) continue;
                        int i = Index(x, z);
                        if (!mask[i]) continue;
                        float d = dx * dx + dz * dz;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                if (best >= 0) return best;
            }
            return -1;
        }
    }
}
