// BeliefGrid.cs
// The Seeker's belief about where the player is: one probability per cell, summing to 1.
// In textbook terms a grid (histogram) Bayes filter. Each tick:
//   1. Predict:  the player may have moved, so some probability spreads to neighbouring cells.
//   2. Update:   multiply by the likelihood of what was sensed (Bayes rule), then normalise.
// Plain C#; arrays are allocated once, so a tick allocates nothing.

using UnityEngine;

namespace Override.HunterAI
{
    public sealed class BeliefGrid
    {
        readonly HunterGrid grid;
        readonly float[] belief;
        readonly float[] scratch;
        readonly float[] summed;   // summed-area table for fast window sums (cluster mass)

        public BeliefGrid(HunterGrid grid)
        {
            this.grid = grid;
            belief = new float[grid.Count];
            scratch = new float[grid.Count];
            summed = new float[(grid.Width + 1) * (grid.Height + 1)];
            ResetUniform();
        }

        public float this[int i] => belief[i];
        public float Max { get; private set; }

        /// Equal probability on every cell the player could stand in (the Seeker knows nothing yet).
        public void ResetUniform()
        {
            int n = 0;
            for (int i = 0; i < belief.Length; i++) if (grid.PlayerWalkable[i]) n++;
            float p = n > 0 ? 1f / n : 0f;
            for (int i = 0; i < belief.Length; i++) belief[i] = grid.PlayerWalkable[i] ? p : 0f;
            Max = p;
        }

        /// PREDICT. Each walkable cell keeps (1 − α) of its probability and shares α equally among the
        /// neighbours it's linked to. Mass-conserving: probability moves, it's never created or lost.
        public void Predict(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            System.Array.Clear(scratch, 0, scratch.Length);

            for (int i = 0; i < belief.Length; i++)
            {
                float b = belief[i];
                if (b <= 0f) continue;

                // Only neighbours the player could actually walk to: probability doesn't leak through walls.
                int neighbours = 0;
                for (int k = 0; k < 8; k++) if (grid.PlayerLinked(i, k)) neighbours++;
                if (neighbours == 0) { scratch[i] += b; continue; }

                scratch[i] += b * (1f - alpha);
                float share = b * alpha / neighbours;
                for (int k = 0; k < 8; k++)
                    if (grid.PlayerLinked(i, k)) scratch[grid.Neighbour(i, k)] += share;
            }

            System.Array.Copy(scratch, belief, belief.Length);
        }

        /// UPDATE for "the player is near x": L(c) = ε + (1 − ε) · exp(−d² / 2σ²).
        /// The ε floor means one wrong clue can never rule out the rest of the map.
        public void ApplyGaussian(Vector3 x, float sigma, float floor)
        {
            float twoSigmaSq = 2f * sigma * sigma;
            for (int i = 0; i < belief.Length; i++)
            {
                if (belief[i] <= 0f) continue;
                Vector3 c = grid.Centre(i);
                float dx = c.x - x.x, dz = c.z - x.z;
                float likelihood = floor + (1f - floor) * Mathf.Exp(-(dx * dx + dz * dz) / twoSigmaSq);
                belief[i] *= likelihood;
            }
        }

        /// NEGATIVE evidence: "I'm here and I don't sense the player". Cells within the sense radius
        /// are multiplied by (1 − p_d), so searching an empty area drains its probability.
        public void ApplyNotFound(Vector3 at, float radius, float detectionProbability)
        {
            float rSq = radius * radius;
            float keep = 1f - detectionProbability;
            int c0 = grid.CellOf(at);
            if (c0 < 0) return;
            int reach = Mathf.CeilToInt(radius / grid.CellSize);
            int cx = grid.X(c0), cz = grid.Z(c0);
            for (int z = cz - reach; z <= cz + reach; z++)
                for (int x = cx - reach; x <= cx + reach; x++)
                {
                    if (!grid.InBounds(x, z)) continue;
                    int i = grid.Index(x, z);
                    Vector3 c = grid.Centre(i);
                    float dx = c.x - at.x, dz = c.z - at.z;
                    if (dx * dx + dz * dz <= rSq) belief[i] *= keep;
                }
        }

        /// Rescale so the cells sum to 1. If everything underflowed to 0, start again from uniform.
        /// Returns false when it had to reset.
        public bool Normalize()
        {
            double sum = 0;
            for (int i = 0; i < belief.Length; i++) sum += belief[i];
            if (sum < 1e-20)
            {
                ResetUniform();
                return false;
            }
            float inv = (float)(1.0 / sum);
            float max = 0f;
            for (int i = 0; i < belief.Length; i++)
            {
                belief[i] *= inv;
                if (belief[i] > max) max = belief[i];
            }
            Max = max;
            return true;
        }

        /// Builds the summed-area table so ClusterMass is O(1) per cell (call once per tick).
        public void PrepareClusterSums()
        {
            int w = grid.Width, h = grid.Height, sw = w + 1;
            for (int z = 0; z <= h; z++) summed[z * sw] = 0f;
            for (int x = 0; x <= w; x++) summed[x] = 0f;
            for (int z = 1; z <= h; z++)
                for (int x = 1; x <= w; x++)
                    summed[z * sw + x] = belief[grid.Index(x - 1, z - 1)]
                                       + summed[(z - 1) * sw + x] + summed[z * sw + x - 1]
                                       - summed[(z - 1) * sw + x - 1];
        }

        /// M(c): total probability in the square of cells within `radiusCells` of cell i.
        public float ClusterMass(int i, int radiusCells)
        {
            int w = grid.Width, h = grid.Height, sw = w + 1;
            int x0 = Mathf.Max(0, grid.X(i) - radiusCells), x1 = Mathf.Min(w - 1, grid.X(i) + radiusCells);
            int z0 = Mathf.Max(0, grid.Z(i) - radiusCells), z1 = Mathf.Min(h - 1, grid.Z(i) + radiusCells);
            return summed[(z1 + 1) * sw + x1 + 1] - summed[z0 * sw + x1 + 1]
                 - summed[(z1 + 1) * sw + x0] + summed[z0 * sw + x0];
        }
    }
}
