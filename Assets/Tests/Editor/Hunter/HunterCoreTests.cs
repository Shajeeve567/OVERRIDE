// HunterCoreTests.cs
// Edit-mode tests for the Hunter's algorithm (Override.HunterAI): the belief grid's Bayes filter,
// A*, goal choice and the heap. Plain C#, so no scene or Play mode is needed.
// Run them in Window > General > Test Runner > EditMode.
//
// Each test builds a tiny hand-made grid, so the expected answer can be worked out on paper.

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Override.HunterAI.Tests
{
    public class HunterCoreTests
    {
        const float Cell = 2f;

        // ------------------------------------------------------------------ helpers

        /// An open w x h grid: every cell walkable by both agents and linked to all 8 neighbours.
        static HunterGrid OpenGrid(int w, int h)
        {
            var g = new HunterGrid(Vector2.zero, w, h, Cell);
            for (int i = 0; i < g.Count; i++) { g.PlayerWalkable[i] = true; g.HunterWalkable[i] = true; }
            for (int i = 0; i < g.Count; i++)
                for (int k = 0; k < 8; k++)
                    if (g.Neighbour(i, k) >= 0) Link(g, i, k, true);
            return g;
        }

        static void Link(HunterGrid g, int i, int k, bool open)
        {
            int n = g.Neighbour(i, k);
            byte bit = (byte)(1 << k), back = (byte)(1 << (7 - k));
            if (open)
            {
                g.PlayerLinks[i] |= bit; g.HunterLinks[i] |= bit;
                g.PlayerLinks[n] |= back; g.HunterLinks[n] |= back;
            }
            else
            {
                g.PlayerLinks[i] &= (byte)~bit; g.HunterLinks[i] &= (byte)~bit;
                g.PlayerLinks[n] &= (byte)~back; g.HunterLinks[n] &= (byte)~back;
            }
        }

        /// A thin wall on the vertical line between column x-1 and column x, for every row except `gapZ`
        /// (pass -1 for no gap). It cuts every link that crosses that line, as a baked wall would.
        static void WallBetweenColumns(HunterGrid g, int x, int gapZ)
        {
            for (int z = 0; z < g.Height; z++)
                for (int k = 0; k < 8; k++)
                {
                    int i = g.Index(x - 1, z);
                    int n = g.Neighbour(i, k);
                    if (n < 0 || g.X(n) != x) continue;                 // only links crossing the line
                    if (z == gapZ && g.Z(n) == gapZ) continue;          // straight through the gap stays open
                    Link(g, i, k, false);
                }
        }

        static float Sum(BeliefGrid b, int count)
        {
            float s = 0f;
            for (int i = 0; i < count; i++) s += b[i];
            return s;
        }

        static float PathCost(HunterGrid g, int start, List<int> path)
        {
            float cost = 0f;
            int prev = start;
            foreach (int c in path)
            {
                bool diagonal = g.X(c) != g.X(prev) && g.Z(c) != g.Z(prev);
                cost += diagonal ? Mathf.Sqrt(2f) : 1f;
                prev = c;
            }
            return cost;
        }

        // ------------------------------------------------------------------ belief: predict

        [Test]
        public void Uniform_start_sums_to_one_over_walkable_cells()
        {
            var g = OpenGrid(5, 4);
            g.PlayerWalkable[3] = false;
            var b = new BeliefGrid(g);
            Assert.AreEqual(1f, Sum(b, g.Count), 1e-5f);
            Assert.AreEqual(0f, b[3], "a cell the player can't stand in must hold no probability");
        }

        [Test]
        public void Predict_conserves_probability()
        {
            var g = OpenGrid(6, 6);
            var b = new BeliefGrid(g);
            b.ApplyGaussian(g.Centre(g.Index(1, 1)), 1f, 0f);
            b.Normalize();
            for (int step = 0; step < 20; step++) b.Predict(0.3f);
            Assert.AreEqual(1f, Sum(b, g.Count), 1e-4f, "spreading moves probability, it never creates or loses it");
        }

        [Test]
        public void Predict_does_not_leak_through_a_wall()
        {
            var g = OpenGrid(6, 3);
            WallBetweenColumns(g, 3, -1);   // solid wall, no gap
            var b = new BeliefGrid(g);
            b.ApplyGaussian(g.Centre(g.Index(0, 1)), 0.5f, 0f);   // all mass on the left side
            b.Normalize();
            for (int step = 0; step < 50; step++) b.Predict(0.3f);

            float right = 0f;
            for (int z = 0; z < 3; z++) for (int x = 3; x < 6; x++) right += b[g.Index(x, z)];
            Assert.AreEqual(0f, right, 1e-6f);
        }

        // ------------------------------------------------------------------ belief: update

        [Test]
        public void Noise_evidence_puts_the_peak_at_the_noise()
        {
            var g = OpenGrid(10, 10);
            var b = new BeliefGrid(g);
            int noiseCell = g.Index(7, 2);
            b.ApplyGaussian(g.Centre(noiseCell), 3f, 0.02f);
            b.Normalize();

            int best = 0;
            for (int i = 1; i < g.Count; i++) if (b[i] > b[best]) best = i;
            Assert.AreEqual(noiseCell, best);
        }

        [Test]
        public void Epsilon_floor_keeps_far_cells_possible()
        {
            var g = OpenGrid(20, 3);
            var b = new BeliefGrid(g);
            b.ApplyGaussian(g.Centre(g.Index(0, 1)), 1f, 0.02f);   // a (possibly fake) noise far left
            b.Normalize();
            Assert.Greater(b[g.Index(19, 1)], 0f, "with ε > 0 one clue can never rule out the rest of the map");
        }

        [Test]
        public void Not_finding_the_player_drains_the_searched_area()
        {
            var g = OpenGrid(10, 10);
            var b = new BeliefGrid(g);
            int here = g.Index(5, 5);
            float before = b[here];
            b.ApplyNotFound(g.Centre(here), 3f, 0.9f);
            b.Normalize();
            Assert.Less(b[here], before * 0.2f);
            Assert.Greater(b[g.Index(0, 0)], b[here], "unsearched cells become relatively more likely");
        }

        [Test]
        public void Underflow_resets_to_uniform()
        {
            var g = OpenGrid(4, 4);
            var b = new BeliefGrid(g);
            b.ApplyNotFound(g.Centre(g.Index(1, 1)), 100f, 1f);   // p_d = 1 over the whole grid: everything × 0
            Assert.IsFalse(b.Normalize(), "Normalize reports it had to reset");
            Assert.AreEqual(1f / g.Count, b[0], 1e-6f);
        }

        [Test]
        public void Two_far_apart_noises_make_two_peaks()
        {
            var g = OpenGrid(20, 3);
            var b = new BeliefGrid(g);
            b.ApplyGaussian(g.Centre(g.Index(2, 1)), 3f, 0.02f);
            b.Normalize();
            b.ApplyGaussian(g.Centre(g.Index(17, 1)), 3f, 0.02f);   // contradictory evidence
            b.Normalize();
            float middle = b[g.Index(10, 1)];
            Assert.Greater(b[g.Index(2, 1)], middle);
            Assert.Greater(b[g.Index(17, 1)], middle);
        }

        // ------------------------------------------------------------------ cluster mass

        [Test]
        public void Cluster_mass_matches_a_brute_force_sum()
        {
            var g = OpenGrid(8, 7);
            var b = new BeliefGrid(g);
            b.ApplyGaussian(g.Centre(g.Index(5, 2)), 2f, 0.05f);
            b.Normalize();
            b.PrepareClusterSums();

            foreach (int i in new[] { g.Index(0, 0), g.Index(5, 2), g.Index(7, 6), g.Index(3, 3) })
            {
                float brute = 0f;
                for (int z = g.Z(i) - 2; z <= g.Z(i) + 2; z++)
                    for (int x = g.X(i) - 2; x <= g.X(i) + 2; x++)
                        if (g.InBounds(x, z)) brute += b[g.Index(x, z)];
                Assert.AreEqual(brute, b.ClusterMass(i, 2), 1e-5f);
            }
        }

        // ------------------------------------------------------------------ A*

        [Test]
        public void AStar_open_grid_cost_equals_the_octile_distance()
        {
            var g = OpenGrid(12, 9);
            var astar = new GridAStar(g);
            var path = new List<int>();
            int start = g.Index(1, 1), goal = g.Index(10, 5);   // dx 9, dz 4
            Assert.IsTrue(astar.FindPath(start, goal, 0f, path));
            float octile = (9 - 4) + Mathf.Sqrt(2f) * 4;          // the heuristic's own value
            Assert.AreEqual(octile, PathCost(g, start, path), 1e-4f,
                "with no obstacles the octile heuristic is exact, so it can never overestimate");
            Assert.AreEqual(goal, path[path.Count - 1]);
        }

        [Test]
        public void AStar_goes_around_a_wall_through_the_gap()
        {
            var g = OpenGrid(8, 6);
            WallBetweenColumns(g, 4, 5);   // wall with a gap only in the top row
            var astar = new GridAStar(g);
            var path = new List<int>();
            Assert.IsTrue(astar.FindPath(g.Index(1, 0), g.Index(6, 0), 0f, path));

            int prev = g.Index(1, 0);
            foreach (int c in path)
            {
                if (g.X(prev) == 3 && g.X(c) == 4) Assert.AreEqual(5, g.Z(c), "crossed the wall outside the gap");
                prev = c;
            }
        }

        [Test]
        public void AStar_reports_an_unreachable_goal()
        {
            var g = OpenGrid(8, 4);
            WallBetweenColumns(g, 4, -1);   // no gap
            var astar = new GridAStar(g);
            Assert.IsFalse(astar.FindPath(g.Index(0, 0), g.Index(7, 3), 0f, new List<int>()));
        }

        [Test]
        public void AStar_does_not_cut_corners_past_a_crate()
        {
            var g = OpenGrid(5, 5);
            // A crate on cell (2,1). The diagonal step (1,1) -> (2,2) would squeeze past its corner,
            // so A* must go round: (1,1) -> (1,2) -> (2,2).
            g.BlockBox(new Bounds(g.Centre(g.Index(2, 1)), new Vector3(0.5f, 1f, 0.5f)), 0f);
            var astar = new GridAStar(g);
            var path = new List<int>();
            Assert.IsTrue(astar.FindPath(g.Index(1, 1), g.Index(2, 2), 0f, path));
            Assert.AreNotEqual(1, path.Count, "the direct diagonal would cut the crate's corner");
        }

        [Test]
        public void Temporarily_blocked_cell_is_avoided_until_its_time_passes()
        {
            var g = OpenGrid(3, 1);
            g.BlockTemporarily(g.Index(1, 0), 5f);   // the only way through, avoided until t = 5
            var astar = new GridAStar(g);
            Assert.IsFalse(astar.FindPath(g.Index(0, 0), g.Index(2, 0), 4f, new List<int>()));
            Assert.IsTrue(astar.FindPath(g.Index(0, 0), g.Index(2, 0), 6f, new List<int>()));
        }

        // ------------------------------------------------------------------ goal choice

        [Test]
        public void Goal_is_the_reachable_cell_next_to_a_vent_holding_the_player()
        {
            var g = OpenGrid(9, 3);
            int vent = g.Index(8, 1);
            g.HunterWalkable[vent] = false;   // the player fits, the Seeker doesn't
            var b = new BeliefGrid(g);
            b.ApplyGaussian(g.Centre(vent), 0.5f, 0f);
            b.Normalize();
            b.PrepareClusterSums();

            var goals = new GoalChooser(g, 3);
            goals.Rank(b, g.Centre(g.Index(0, 1)), 0.05f, Cell, 0f);
            int best = goals.Candidate(0);
            Assert.AreNotEqual(vent, best, "the Seeker can't enter the vent");
            Assert.AreEqual(7, g.X(best), "it waits at the vent mouth, right next to it");
        }

        [Test]
        public void Distance_discount_prefers_the_nearer_of_two_equal_patches()
        {
            var g = OpenGrid(30, 3);
            // Equal mass in two patches, near (x 5) and far (x 25), as a mixture: L = G(near) + G(far).
            var likelihood = new float[g.Count];
            for (int i = 0; i < g.Count; i++)
            {
                float dNear = g.Centre(i).x - g.Centre(g.Index(5, 1)).x;
                float dFar = g.Centre(i).x - g.Centre(g.Index(25, 1)).x;
                likelihood[i] = Mathf.Exp(-dNear * dNear / 8f) + Mathf.Exp(-dFar * dFar / 8f);
            }
            var b = new BeliefGrid(g);
            b.ApplyLikelihood(likelihood);
            b.Normalize();
            b.PrepareClusterSums();

            var goals = new GoalChooser(g, 3);
            goals.Rank(b, g.Centre(g.Index(3, 1)), 0.05f, Cell, 0f);   // Seeker near the left patch
            Assert.LessOrEqual(Mathf.Abs(g.X(goals.Candidate(0)) - 5), 1, "same mass, so the nearer patch wins");
        }

        // ------------------------------------------------------------------ heap

        [Test]
        public void MinHeap_pops_in_priority_order()
        {
            var heap = new MinHeap(4);   // grows past its starting capacity
            float[] keys = { 5f, 1f, 9f, 3f, 7f, 2f, 8f };
            for (int i = 0; i < keys.Length; i++) heap.Push(i, keys[i]);
            float last = float.NegativeInfinity;
            while (heap.Count > 0)
            {
                int item = heap.Pop();
                Assert.GreaterOrEqual(keys[item], last);
                last = keys[item];
            }
        }
    }
}
