// DirectorCost.cs
// The cost function: one number saying how BAD a plan is. Local search goes "downhill" on it.
//
//   cost =  Band    * (predictedPressure - neededPressure)^2    <- the actual goal
//         + Budget  * dronesOverBudget                           <- hard rule
//         + Blocked * dronesFromBlockedBays                      <- hard rule
//         + IllegalLock * locksWithoutDoorControl                <- hard rule
//         + TrapPlayer  * (1 if every exit of the player's zone is locked)   <- hard rule (fairness)
//         + TooClose * dronesSpawningCloserThan10m               <- soft rule (fairness)
//         - Flank   * directionSpread                            <- soft reward (interesting fights)
//         + Change  * doorsToggledSinceLastPlan                  <- soft rule (stability)
//         + PerDrone * drones                                     <- economy: no wasted drones
//
// WHY HARD RULES ARE PENALTIES, NOT FILTERS: local search moves one small step at a time. If illegal
// plans were removed from the space, legal plans could form islands with no single-step path between
// them, and the search would get stuck on one. A penalty of 100 (when a good plan costs ~0-5) means it
// may step THROUGH an illegal plan, but the best plan it reports is never illegal in practice.
//
// WHY SQUARED for the band term: being off by 2 drones is more than twice as bad as being off by 1,
// and a square is smooth around the target, so the search can tell "close" from "very close".

using System;
using UnityEngine;

namespace Override.DirectorAI
{
    /// The tuning knobs. [Serializable] so the controller can show them in the Inspector.
    [Serializable]
    public sealed class DirectorWeights
    {
        public float Band = 1f;
        public float Budget = 100f;
        public float Blocked = 100f;
        public float IllegalLock = 100f;
        public float TrapPlayer = 100f;
        public float TooClose = 2f;
        public float Flank = 0.4f;
        public float Change = 0.1f;
        /// A small price per drone: ARGUS shouldn't spend a drone that adds almost nothing.
        public float PerDrone = 0.1f;

        /// Closer than this (metres) counts as "spawning in the player's face".
        public float CloseDistance = 10f;
        /// How quickly a drone's pressure fades with distance: at CloseDistance + ArrivalScale it's worth half.
        public float ArrivalScale = 20f;
        /// Pressure added by each locked door (it cuts the player's options).
        public float LockPressure = 0.3f;
    }

    /// The cost split into its terms, for the debug panel and for explaining decisions in the viva.
    public struct CostBreakdown
    {
        public float Total, Band, Budget, Blocked, IllegalLock, TrapPlayer, TooClose, Flank, Change, Drones;
        public float PredictedPressure, NeededPressure;
    }

    public sealed class DirectorCost
    {
        readonly DirectorWeights w;
        readonly DirectorSnapshot s;
        readonly DirectorPlan previous;
        public readonly float NeededPressure;

        // Worked out ONCE per decision, because they don't depend on the plan. The search then
        // evaluates hundreds of plans, and each evaluation is just a loop over 10 numbers.
        readonly float[] bayPressure;   // pressure ONE drone from this bay puts on the player
        readonly float[] bayDistance;
        readonly Vector3[] bayDirection; // unit vector from the player towards the bay (flat)
        readonly int exitCount;

        public DirectorCost(DirectorSnapshot snapshot, float neededPressure, DirectorWeights weights, DirectorPlan previousPlan)
        {
            s = snapshot;
            w = weights;
            previous = previousPlan;
            NeededPressure = neededPressure;

            int n = s.Bays.Length;
            bayPressure = new float[n];
            bayDistance = new float[n];
            bayDirection = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 offset = s.Bays[i].Position - s.PlayerPosition;
                offset.y = 0f;                                   // height doesn't matter for "how far"
                float d = offset.magnitude;
                bayDistance[i] = d;
                bayDirection[i] = d > 0.001f ? offset / d : Vector3.zero;
                // A drone that arrives soon is worth ~1; one far away is worth less, because by the time
                // it arrives the situation has changed. 1 / (1 + extra/scale) halves at extra = scale.
                bayPressure[i] = 1f / (1f + Mathf.Max(0f, d - w.CloseDistance) / w.ArrivalScale);
            }
            foreach (var door in s.Doors) if (door.PlayerZoneExit) exitCount++;
        }

        /// The number local search minimises. Kept allocation-free: it runs hundreds of times per decision.
        public float Evaluate(DirectorPlan plan) => Explain(plan).Total;

        public CostBreakdown Explain(DirectorPlan plan)
        {
            var c = new CostBreakdown { NeededPressure = NeededPressure };

            float pressure = 0f;
            int drones = 0, blocked = 0, close = 0;
            Vector3 directionSum = Vector3.zero;
            for (int i = 0; i < plan.DronesPerBay.Length; i++)
            {
                int k = plan.DronesPerBay[i];
                if (k == 0) continue;
                drones += k;
                pressure += k * bayPressure[i];
                // Each drone's direction counts as much as the pressure it brings: a drone 80 m away
                // that arrives late doesn't make a real pincer, so it can't buy the Flank reward.
                directionSum += (k * bayPressure[i]) * bayDirection[i];
                if (s.Bays[i].Blocked) blocked += k;
                if (bayDistance[i] < w.CloseDistance) close += k;
            }

            int locks = 0, exitsLocked = 0;
            for (int i = 0; i < plan.LockDoor.Length; i++)
            {
                if (!plan.LockDoor[i]) continue;
                locks++;
                if (s.Doors[i].PlayerZoneExit) exitsLocked++;
            }
            pressure += locks * w.LockPressure;
            c.PredictedPressure = pressure;

            float gap = pressure - NeededPressure;
            c.Band = w.Band * gap * gap;

            int over = Mathf.Max(0, drones + s.ActiveDrones - s.DroneBudget);
            c.Budget = w.Budget * over;
            c.Blocked = w.Blocked * blocked;
            c.IllegalLock = s.DoorControlOnline ? 0f : w.IllegalLock * locks;
            c.TrapPlayer = exitCount > 0 && exitsLocked == exitCount ? w.TrapPlayer : 0f;
            c.TooClose = w.TooClose * close;

            // Spread: average the directions the drones come from (weighted by their pressure). All from
            // one side -> the average is a full-length arrow (length 1) -> spread 0. From opposite sides
            // -> they cancel -> spread 1.
            // Rewarded (negative cost) because a pincer is the Wisps' whole personality in the GDD.
            float spread = drones >= 2 && pressure > 0f ? 1f - directionSum.magnitude / (pressure - locks * w.LockPressure) : 0f;
            c.Flank = -w.Flank * spread;

            // Doors are STATES (they stay locked), so flipping them every few seconds would feel random.
            // Spawns are EVENTS, so there's nothing to keep stable about them.
            c.Change = w.Change * plan.LockChanges(previous);

            c.Drones = w.PerDrone * drones;

            c.Total = c.Band + c.Budget + c.Blocked + c.IllegalLock + c.TrapPlayer + c.TooClose + c.Flank + c.Change + c.Drones;
            return c;
        }
    }
}
