// DirectorBrain.cs
// ARGUS Core: the Director agent. Plain C#: no MonoBehaviour, no scene access.
//
// ONE DECISION (called every 3-5 s by the controller):
//   1. Tension    : how much pressure the player is under right now (0..1), from the GDD's three
//                   inputs: recent damage, time since the last fight, and the alarm.
//   2. Need       : how much NEW pressure to add to pull tension back into the target band.
//   3. Search     : local search over plans for the cheapest one under DirectorCost.
//   4. Remember   : keep the plan, so next time door locks change only when worth it.
//
// The search is either simulated annealing (the real one) or hill descent (the baseline it's compared
// against). Both get the same cost function and the same moves.

using System;
using UnityEngine;

namespace Override.DirectorAI
{
    [Serializable]
    public sealed class DirectorSettings
    {
        [Header("Tension = weighted mix of the three inputs (weights add up to 1)")]
        public float DamageWeight = 0.5f;
        public float AlarmWeight = 0.3f;
        public float EngagementWeight = 0.2f;
        /// After this many seconds without a fight, the "engagement" input has fully cooled to 0.
        public float EngagementCooldown = 30f;

        [Header("Target band: keep tension between these")]
        public float BandLow = 0.35f;
        public float BandHigh = 0.65f;
        /// How many drones' worth of pressure to add per unit of tension below the band's centre.
        /// Tension 0 -> 0.5 x 6 = 3 drones' worth.
        public float PressurePerTension = 6f;

        [Header("Search")]
        public bool UseAnnealing = true;
        public AnnealingSettings Annealing = new AnnealingSettings();
        public DirectorWeights Weights = new DirectorWeights();

        public float BandCentre => (BandLow + BandHigh) / 2f;
    }

    /// Everything about one decision, for the controller to act on and the debug panel to show.
    public sealed class DirectorDecision
    {
        public DirectorPlan Plan;
        public float Tension;
        public float NeededPressure;
        public CostBreakdown Cost;
        public SearchResult Search;
        public string Algorithm;
    }

    public sealed class DirectorBrain
    {
        public readonly DirectorSettings Settings;
        readonly System.Random rng;
        DirectorPlan previous;

        /// seed: the same seed gives the same decisions for the same snapshots (tests, recordings).
        public DirectorBrain(DirectorSettings settings, int seed)
        {
            Settings = settings ?? new DirectorSettings();
            rng = new System.Random(seed);
        }

        /// 0 = bored, 1 = overwhelmed.
        public float Tension(DirectorSnapshot s)
        {
            // "Heat" is 1 right after a fight and fades linearly to 0 over EngagementCooldown seconds.
            float heat = 1f - Mathf.Clamp01(s.SecondsSinceEngagement / Settings.EngagementCooldown);
            float t = Settings.DamageWeight * Mathf.Clamp01(s.RecentDamage)
                    + Settings.AlarmWeight * Mathf.Clamp01(s.Alarm / 100f)
                    + Settings.EngagementWeight * heat;
            return Mathf.Clamp01(t);
        }

        /// How much pressure (in "drones arriving now") the plan should add.
        public float NeededPressure(DirectorSnapshot s, float tension)
        {
            // The final wave ignores tension: ARGUS throws everything its budget allows.
            if (s.FinalWave) return Mathf.Max(0, s.DroneBudget - s.ActiveDrones);

            // Below the centre of the band -> push. At or above it -> add nothing (the cost function
            // then prefers the empty plan, so the Director "backs off" without a special case).
            return Mathf.Max(0f, (Settings.BandCentre - tension) * Settings.PressurePerTension);
        }

        public DirectorDecision Decide(DirectorSnapshot s)
        {
            float tension = Tension(s);
            float needed = NeededPressure(s, tension);
            var cost = new DirectorCost(s, needed, Settings.Weights, previous);

            // Start point: no drones, and the door locks as they are now. (Spawns are one-off events,
            // so last decision's spawns are no guide; locks are standing states, so they carry over.)
            var start = new DirectorPlan(s.Bays.Length, s.Doors.Length);
            if (previous != null && previous.LockDoor.Length == start.LockDoor.Length)
                Array.Copy(previous.LockDoor, start.LockDoor, start.LockDoor.Length);

            SearchResult result = Settings.UseAnnealing
                ? PlanSearch.SimulatedAnnealing(start, cost.Evaluate, Settings.Annealing, rng)
                : PlanSearch.HillDescent(start, cost.Evaluate);

            previous = result.Best;
            return new DirectorDecision
            {
                Plan = result.Best,
                Tension = tension,
                NeededPressure = needed,
                Cost = cost.Explain(result.Best),
                Search = result,
                Algorithm = Settings.UseAnnealing ? "Simulated annealing" : "Hill descent",
            };
        }
    }
}
