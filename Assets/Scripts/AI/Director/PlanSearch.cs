// PlanSearch.cs
// The two local search algorithms, sharing the same neighbour moves and the same cost function,
// so any difference in their results comes from the ALGORITHM alone. That's the fair comparison
// you'll show in the viva.
//
// NEIGHBOUR MOVES (one small change to a plan):
//   Add    : +1 drone at a bay that has room
//   Remove : -1 drone at a bay that has one
//   Move   : take a drone from bay i and launch it from bay j instead
//   Toggle : lock or unlock one door
//
// HILL DESCENT (steepest descent): look at EVERY neighbour, take the cheapest if it beats the current
// plan, repeat until nothing beats it. Greedy, deterministic, and it stops at the first local minimum.
//
// SIMULATED ANNEALING: look at ONE random neighbour. Cheaper? take it. Worse by delta? take it anyway
// with probability exp(-delta / T). T (temperature) starts high, so early on the search wanders and
// can climb out of a local minimum; T shrinks every step, so by the end it only goes downhill.
// It remembers the best plan it ever saw and returns that, not wherever it happened to stop.

using System;
using System.Collections.Generic;

namespace Override.DirectorAI
{
    [Serializable]
    public sealed class AnnealingSettings
    {
        // Tuned by experiment (DirectorBrainTests, 200 random situations vs. the brute-force optimum):
        //   400 steps, cooling 0.98   -> found the true best plan in 151/200 (worse than hill descent's 182)
        //   1000 steps, cooling 0.995 -> 187/200
        //   2000 steps, cooling 0.997 -> 194/200   <- chosen; ~2000 cost evaluations, well under 1 ms
        // Too few steps means T drops before the search has explored, so it "freezes" early.
        public int Iterations = 2000;
        /// T is multiplied by this after every step ("geometric cooling"). 0.997^2000 = 0.0025,
        /// so the last steps are effectively pure downhill.
        public float Cooling = 0.997f;
        /// The share of WORSE moves to accept at the start. Used to pick the starting temperature
        /// from the problem itself, instead of a magic number that only suits one cost scale.
        public float InitialAcceptance = 0.5f;
    }

    /// What a search returns: the plan, plus the numbers you'll want as evidence.
    public sealed class SearchResult
    {
        public DirectorPlan Best;
        public float BestCost;
        public int Evaluations;          // how many times the cost function ran
        public int Steps;                // moves taken (hill descent) or iterations (annealing)
        public int WorseAccepted;        // annealing only: uphill moves it chose to take
        public float StartTemperature;   // annealing only
        public List<float> CostTrace = new List<float>(); // cost after each step, for the debug graph
    }

    public static class PlanSearch
    {
        // ==================================================================
        // HILL DESCENT
        // ==================================================================
        public static SearchResult HillDescent(DirectorPlan start, Func<DirectorPlan, float> cost)
        {
            var current = start.Clone();
            float currentCost = cost(current);
            var result = new SearchResult { Evaluations = 1 };
            result.CostTrace.Add(currentCost);

            while (true)
            {
                DirectorPlan bestNeighbour = null;
                float bestNeighbourCost = currentCost;
                foreach (var n in AllNeighbours(current))
                {
                    float c = cost(n);
                    result.Evaluations++;
                    if (c < bestNeighbourCost) { bestNeighbourCost = c; bestNeighbour = n.Clone(); }
                }
                if (bestNeighbour == null) break;   // no neighbour is cheaper: a local minimum
                current = bestNeighbour;
                currentCost = bestNeighbourCost;
                result.Steps++;
                result.CostTrace.Add(currentCost);
            }

            result.Best = current;
            result.BestCost = currentCost;
            return result;
        }

        // ==================================================================
        // SIMULATED ANNEALING
        // ==================================================================
        public static SearchResult SimulatedAnnealing(DirectorPlan start, Func<DirectorPlan, float> cost,
                                                      AnnealingSettings settings, Random rng)
        {
            var current = start.Clone();
            float currentCost = cost(current);
            var best = current.Clone();
            float bestCost = currentCost;
            var candidate = current.Clone();   // reused every step: no garbage
            var result = new SearchResult { Evaluations = 1 };

            float t = StartingTemperature(current, cost, settings, rng, result);
            result.StartTemperature = t;

            for (int step = 0; step < settings.Iterations; step++)
            {
                candidate.CopyFrom(current);
                if (!RandomNeighbour(candidate, rng)) break;   // no moves exist (no bays, no doors)

                float candidateCost = cost(candidate);
                result.Evaluations++;
                float delta = candidateCost - currentCost;

                // The Metropolis rule. delta <= 0 is always accepted (exp of a non-negative number is >= 1).
                // For delta > 0 the chance is high when T is large or the step is only slightly worse.
                bool accept = delta <= 0f || rng.NextDouble() < Math.Exp(-delta / t);
                if (accept)
                {
                    if (delta > 0f) result.WorseAccepted++;
                    current.CopyFrom(candidate);
                    currentCost = candidateCost;
                    if (currentCost < bestCost) { best.CopyFrom(current); bestCost = currentCost; }
                }

                t *= settings.Cooling;
                result.Steps++;
                result.CostTrace.Add(currentCost);
            }

            result.Best = best;
            result.BestCost = bestCost;
            return result;
        }

        // Pick T0 so that a typical worse move is accepted with InitialAcceptance probability:
        //   exp(-avgDelta / T0) = p   ->   T0 = -avgDelta / ln(p)
        // avgDelta is measured by trying a few random moves from the starting plan. Huge deltas
        // (hard-rule breaks, around 100) are left out, so they don't make T0 absurdly hot.
        static float StartingTemperature(DirectorPlan start, Func<DirectorPlan, float> cost,
                                         AnnealingSettings settings, Random rng, SearchResult result)
        {
            float startCost = cost(start);
            var probe = start.Clone();
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < 30; i++)
            {
                probe.CopyFrom(start);
                if (!RandomNeighbour(probe, rng)) break;
                float delta = cost(probe) - startCost;
                result.Evaluations++;
                if (delta > 0f && delta < 50f) { sum += delta; count++; }
            }
            float average = count > 0 ? sum / count : 1f;
            return Math.Max(0.01f, (float)(-average / Math.Log(settings.InitialAcceptance)));
        }

        // ==================================================================
        // NEIGHBOURS
        // ==================================================================
        /// Every plan one move away. Hill descent needs them all.
        /// (Yields the SAME object each time, edited and undone, so it allocates nothing per neighbour.
        /// Callers that want to keep one must Clone it, as HillDescent does.)
        public static IEnumerable<DirectorPlan> AllNeighbours(DirectorPlan plan)
        {
            var p = plan.Clone();
            int[] d = p.DronesPerBay;
            for (int i = 0; i < d.Length; i++)
            {
                if (d[i] < DirectorPlan.MaxPerBay) { d[i]++; yield return p; d[i]--; }   // add
                if (d[i] > 0) { d[i]--; yield return p; d[i]++; }                        // remove
                for (int j = 0; j < d.Length; j++)                                       // move i -> j
                {
                    if (i == j || d[i] == 0 || d[j] >= DirectorPlan.MaxPerBay) continue;
                    d[i]--; d[j]++; yield return p; d[j]--; d[i]++;
                }
            }
            for (int k = 0; k < p.LockDoor.Length; k++)                                  // toggle
            {
                p.LockDoor[k] = !p.LockDoor[k]; yield return p; p.LockDoor[k] = !p.LockDoor[k];
            }
        }

        /// Changes `plan` in place into one random neighbour. False if no move is possible.
        public static bool RandomNeighbour(DirectorPlan plan, Random rng)
        {
            int[] d = plan.DronesPerBay;
            int bays = d.Length, doors = plan.LockDoor.Length;
            if (bays == 0 && doors == 0) return false;

            // Try random moves until one is legal. A move can be illegal (e.g. "remove" from an
            // empty bay); 50 tries is far more than ever needed, and avoids building a move list.
            for (int attempt = 0; attempt < 50; attempt++)
            {
                int move = rng.Next(doors > 0 ? 4 : 3);
                if (move == 3) { int k = rng.Next(doors); plan.LockDoor[k] = !plan.LockDoor[k]; return true; }
                if (bays == 0) continue;
                int i = rng.Next(bays);
                if (move == 0 && d[i] < DirectorPlan.MaxPerBay) { d[i]++; return true; }
                if (move == 1 && d[i] > 0) { d[i]--; return true; }
                if (move == 2 && d[i] > 0)
                {
                    int j = rng.Next(bays);
                    if (j != i && d[j] < DirectorPlan.MaxPerBay) { d[i]--; d[j]++; return true; }
                }
            }
            return false;
        }
    }
}
