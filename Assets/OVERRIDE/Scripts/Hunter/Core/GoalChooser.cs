// GoalChooser.cs
// Where should the Seeker go? Score every cell it can reach by the probability mass around it,
// discounted by how far away it is:
//
//     score(c) = M(c) · exp(−λ · distance(Seeker, c))
//
// M(c) sums belief over a small square around c (ClusterRadius), so a broad patch of likely cells beats a
// single spike. Only Seeker-walkable cells are scored, but M(c) counts ALL cells, vents included: when
// the player hides in a vent, the best cell becomes the vent's mouth, and the Seeker waits there.
// It keeps the best few, so if A* can't reach the first (a crate in the way) the next is tried.

using UnityEngine;

namespace Override.HunterAI
{
    public sealed class GoalChooser
    {
        readonly HunterGrid grid;
        readonly int[] bestCells;
        readonly float[] bestScores;
        readonly float[] bestMass;

        public int CandidateCount { get; private set; }

        public GoalChooser(HunterGrid grid, int candidates)
        {
            this.grid = grid;
            bestCells = new int[Mathf.Max(1, candidates)];
            bestScores = new float[bestCells.Length];
            bestMass = new float[bestCells.Length];
        }

        public int Candidate(int k) => bestCells[k];
        public float CandidateMass(int k) => bestMass[k];

        /// Ranks the best cells; belief.PrepareClusterSums() must have run this tick.
        public void Rank(BeliefGrid belief, Vector3 seeker, float lambda, float clusterRadius, float time)
        {
            int radiusCells = Mathf.Max(0, Mathf.RoundToInt(clusterRadius / grid.CellSize));
            CandidateCount = 0;
            for (int k = 0; k < bestScores.Length; k++) bestScores[k] = float.NegativeInfinity;

            for (int i = 0; i < grid.Count; i++)
            {
                if (!grid.Passable(i, time)) continue;
                float mass = belief.ClusterMass(i, radiusCells);
                if (mass <= 0f) continue;
                Vector3 c = grid.Centre(i);
                float dx = c.x - seeker.x, dz = c.z - seeker.z;
                float score = mass * Mathf.Exp(-lambda * Mathf.Sqrt(dx * dx + dz * dz));
                Insert(i, score, mass);
            }
        }

        void Insert(int cell, float score, float mass)
        {
            int n = bestScores.Length;
            if (score <= bestScores[n - 1]) return;
            int k = n - 1;
            while (k > 0 && bestScores[k - 1] < score)
            {
                bestScores[k] = bestScores[k - 1];
                bestCells[k] = bestCells[k - 1];
                bestMass[k] = bestMass[k - 1];
                k--;
            }
            bestScores[k] = score;
            bestCells[k] = cell;
            bestMass[k] = mass;
            CandidateCount = Mathf.Min(n, CandidateCount + 1);
        }
    }
}
