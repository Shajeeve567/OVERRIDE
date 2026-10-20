// GridAStar.cs
// A* over the Seeker's grid: 8-connected, a straight step costs one cell and a diagonal √2 cells,
// and no diagonal may cut the corner of a blocked cell.
//
// HEURISTIC: octile distance, h = (dmax − dmin) + √2 · dmin cells. It's exactly the cost of the best
// 8-connected path with no obstacles, so it never overestimates (admissible) and never drops by more
// than one step's cost (consistent): A* therefore returns a shortest path and expands each cell once.
//
// All arrays are allocated once and reused; a "generation" stamp marks which entries belong to the
// current search, so nothing has to be cleared between searches.

using System.Collections.Generic;
using UnityEngine;

namespace Override.HunterAI
{
    public sealed class GridAStar
    {
        const float Sqrt2 = 1.41421356f;

        readonly HunterGrid grid;
        readonly float[] g;
        readonly int[] cameFrom;
        readonly int[] seenStamp;
        readonly int[] closedStamp;
        readonly MinHeap open;
        int stamp;

        public int LastExpanded { get; private set; }

        public GridAStar(HunterGrid grid)
        {
            this.grid = grid;
            g = new float[grid.Count];
            cameFrom = new int[grid.Count];
            seenStamp = new int[grid.Count];
            closedStamp = new int[grid.Count];
            open = new MinHeap(grid.Count);
        }

        /// Fills `path` with cell indices from start (exclusive) to goal (inclusive).
        /// The start cell is always allowed, even if blocked, so the Seeker can leave a bad spot.
        public bool FindPath(int start, int goal, float time, List<int> path)
        {
            path.Clear();
            LastExpanded = 0;
            if (start < 0 || goal < 0 || !grid.Passable(goal, time)) return false;
            if (start == goal) return true;

            stamp++;
            open.Clear();
            g[start] = 0f;
            seenStamp[start] = stamp;
            cameFrom[start] = -1;
            open.Push(start, Heuristic(start, goal));

            int gx = grid.X(goal), gz = grid.Z(goal);
            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closedStamp[current] == stamp) continue;   // stale heap entry (lazy deletion)
                closedStamp[current] = stamp;
                LastExpanded++;

                if (current == goal)
                {
                    for (int c = goal; c != start; c = cameFrom[c]) path.Add(c);
                    path.Reverse();
                    return true;
                }

                int cx = grid.X(current), cz = grid.Z(current);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx | dz) == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (!grid.InBounds(nx, nz)) continue;
                        int n = grid.Index(nx, nz);
                        if (closedStamp[n] == stamp || !grid.Passable(n, time)) continue;

                        bool diagonal = dx != 0 && dz != 0;
                        // No corner cutting: both side cells of a diagonal must be open.
                        if (diagonal && (!grid.Passable(grid.Index(cx + dx, cz), time) ||
                                         !grid.Passable(grid.Index(cx, cz + dz), time))) continue;

                        float tentative = g[current] + (diagonal ? Sqrt2 : 1f);
                        if (seenStamp[n] == stamp && tentative >= g[n]) continue;

                        seenStamp[n] = stamp;
                        g[n] = tentative;
                        cameFrom[n] = current;
                        open.Push(n, tentative + Octile(nx, nz, gx, gz));
                    }
            }
            return false;   // goal unreachable from here (e.g. the only way in is blocked by a crate)
        }

        float Heuristic(int a, int b) => Octile(grid.X(a), grid.Z(a), grid.X(b), grid.Z(b));

        static float Octile(int ax, int az, int bx, int bz)
        {
            int dx = Mathf.Abs(ax - bx), dz = Mathf.Abs(az - bz);
            int min = Mathf.Min(dx, dz), max = Mathf.Max(dx, dz);
            return (max - min) + Sqrt2 * min;
        }
    }

    /// A binary min-heap of (cell, priority). Written here rather than relying on .NET's PriorityQueue,
    /// which Unity's .NET profile may not have. Duplicates are allowed; A* skips stale ones.
    public sealed class MinHeap
    {
        int[] items;
        float[] keys;
        public int Count { get; private set; }

        public MinHeap(int capacity)
        {
            items = new int[Mathf.Max(16, capacity)];
            keys = new float[items.Length];
        }

        public void Clear() => Count = 0;

        public void Push(int item, float key)
        {
            if (Count == items.Length)
            {
                System.Array.Resize(ref items, items.Length * 2);
                System.Array.Resize(ref keys, keys.Length * 2);
            }
            int i = Count++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (keys[parent] <= key) break;
                items[i] = items[parent];
                keys[i] = keys[parent];
                i = parent;
            }
            items[i] = item;
            keys[i] = key;
        }

        public int Pop()
        {
            int top = items[0];
            int lastItem = items[--Count];
            float lastKey = keys[Count];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= Count) break;
                if (child + 1 < Count && keys[child + 1] < keys[child]) child++;
                if (keys[child] >= lastKey) break;
                items[i] = items[child];
                keys[i] = keys[child];
                i = child;
            }
            if (Count > 0)
            {
                items[i] = lastItem;
                keys[i] = lastKey;
            }
            return top;
        }
    }
}
