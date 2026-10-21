// HunterBrain.cs
// The Seeker's decision-making: a Bayesian belief grid over where the player might be, plus A* to the
// most promising area. Plain C# behind Rithish's IAgentBrain; it never touches GameObjects.
//
// ONE TICK (every 0.25 s, and immediately when a noise is heard):
//   1. Predict     the player may have moved: belief diffuses to neighbouring cells.
//   2. Update      each piece of evidence multiplies the belief (Bayes rule): heard noises, camera
//                  sightings while the cameras are online, and contact or "not here" from the 3 m sense.
//   3. Goal        score every reachable cell by nearby probability, discounted by distance.
//   4. Plan        A* to the best goal (or the next best, if the first can't be reached).
//   5. Command     path for the motor, where to look, whether to lunge, and an intent label.
//
// WHY NOT A STATE MACHINE: the intent label (Patrol, Search, Investigate, Pursue) is only a description
// of what the belief is doing. Behaviour comes from the maths: contact collapses the belief onto the
// player, so pursuit falls out of the same goal choice. Only the lunge is a separate action.

using System.Collections.Generic;
using Override.Core;
using Override.HunterAI;
using UnityEngine;

namespace Override.Hunter
{
    public sealed class HunterBrain : IAgentBrain
    {
        public const string Attack = "Attack", Pursue = "Pursue", Investigate = "Investigate",
                            Search = "Search", Patrol = "Patrol";

        readonly HunterSettings settings;
        readonly IHunterSenses senses;

        public HunterGrid Grid { get; }
        public BeliefGrid Belief { get; }
        readonly GridAStar astar;
        readonly GoalChooser goals;

        readonly AgentCommand command = new AgentCommand();
        readonly List<int> pathCells = new List<int>();
        Vector3[] pathPoints = new Vector3[0];

        float lastTickTime = float.NegativeInfinity;
        float lastNoiseTime = float.NegativeInfinity;
        float lastPlanTime = float.NegativeInfinity;
        int goalCell = -1;

        // Read by the body and the debug view.
        public string Intent { get; private set; } = Patrol;
        public float DesiredSpeed { get; private set; }
        public int GoalCell => goalCell;
        public float GoalMass { get; private set; }
        public bool HasContact { get; private set; }
        public int CellsExpandedLastPlan => astar.LastExpanded;

        public HunterBrain(HunterSettings settings, HunterGrid grid, IHunterSenses senses)
        {
            this.settings = settings;
            this.senses = senses;
            Grid = grid;
            Belief = new BeliefGrid(grid);
            astar = new GridAStar(grid);
            goals = new GoalChooser(grid, settings.GoalCandidates);
            DesiredSpeed = settings.SearchSpeed;
        }

        public AgentCommand Tick(WorldSnapshot snapshot)
        {
            float now = snapshot.Time;
            float dt = float.IsNegativeInfinity(lastTickTime) ? settings.DecisionInterval : now - lastTickTime;
            lastTickTime = now;

            // 1. PREDICT, scaled so a late tick spreads more and an early (noise-triggered) one less.
            Belief.Predict(settings.Diffusion * Mathf.Clamp(dt / settings.DecisionInterval, 0f, 2f));

            // 2. UPDATE
            bool heardNew = false;
            foreach (var noise in senses.HeardNoises)
            {
                Belief.ApplyGaussian(noise.Position, noise.Muffled ? settings.MuffledNoiseSigma : settings.NoiseSigma,
                                     settings.NoiseFloor);
                heardNew = true;
            }
            if (heardNew) lastNoiseTime = now;

            // Cameras offline (Labs terminal done) = this evidence source simply stops: no new branch.
            if (snapshot.CamerasOnline && senses.CamerasOnline)
                foreach (var seen in senses.CameraSightings)
                    Belief.ApplyGaussian(seen, settings.CameraSigma, settings.CameraFloor);

            // Final phase: ARGUS's recall order joins the evidence. No new behaviour branch: the belief
            // simply leans towards the Core, and the same goal choice takes the Seeker there.
            if (senses.IsRecalled(out Vector3 rally))
                Belief.ApplyGaussian(rally, settings.RecallSigma, settings.RecallFloor);

            HasContact = senses.SenseContact(out Vector3 playerAt);
            if (HasContact) Belief.ApplyGaussian(playerAt, settings.ContactSigma, settings.ContactFloor);
            else Belief.ApplyNotFound(senses.Position, settings.ProximityRadius, settings.DetectionProbability);

            Belief.Normalize();   // resets to uniform if everything underflowed

            // 3 + 4. GOAL and PLAN
            float lambda = settings.DistanceDiscount * (1f - settings.AlarmReach * Mathf.Clamp01(senses.Alarm / 100f));
            Belief.PrepareClusterSums();
            goals.Rank(Belief, senses.Position, lambda, settings.ClusterRadius, now);

            bool goalChanged = goals.CandidateCount > 0 && goals.Candidate(0) != goalCell;
            bool due = now - lastPlanTime >= settings.ReplanInterval;
            if (goalChanged || due || heardNew || HasContact || PathBlocked(now)) Plan(now);

            // 5. COMMAND
            float contactDistance = HasContact ? Vector3.Distance(senses.Position, playerAt) : float.MaxValue;
            Intent = HasContact ? (contactDistance <= settings.AttackRange ? Attack : Pursue)
                   : now - lastNoiseTime < settings.InvestigateWindow ? Investigate
                   : GoalMass >= settings.SearchThreshold ? Search
                   : Patrol;
            DesiredSpeed = HasContact ? settings.PursueSpeed : settings.SearchSpeed;

            command.Path = pathPoints;
            command.LookTarget = HasContact ? playerAt
                               : goalCell >= 0 ? Grid.Centre(goalCell)
                               : senses.Position + Vector3.forward;
            command.Fire = Intent == Attack;
            command.AnimationIntent = Intent;
            return command;
        }

        void Plan(float now)
        {
            lastPlanTime = now;
            int start = Grid.CellOf(senses.Position);

            // Try the best few goals: if a crate cuts off the first, the next one is used this tick.
            for (int k = 0; k < goals.CandidateCount; k++)
            {
                int goal = goals.Candidate(k);
                if (!astar.FindPath(start, goal, now, pathCells)) continue;
                goalCell = goal;
                GoalMass = goals.CandidateMass(k);
                pathPoints = ToWorld(pathCells);
                return;
            }

            // Nothing reachable: stay put, keep listening.
            goalCell = -1;
            GoalMass = 0f;
            pathPoints = new Vector3[0];
        }

        bool PathBlocked(float now)
        {
            foreach (int c in pathCells)
                if (!Grid.Passable(c, now)) return true;
            return false;
        }

        Vector3[] ToWorld(List<int> cells)
        {
            var points = new Vector3[cells.Count];
            for (int i = 0; i < cells.Count; i++) points[i] = Grid.Centre(cells[i]);
            return points;
        }

        /// Called by the body when it hasn't moved for a while: avoid that cell for a few seconds.
        public void ReportStuck(Vector3 at, float now)
        {
            Grid.BlockTemporarily(Grid.CellOf(at), now + settings.StuckBlockSeconds);
            lastPlanTime = float.NegativeInfinity;   // replan on the next tick
        }

        /// Called by the body before planning, with the current closed doors and crates.
        public void RefreshObstacles(IReadOnlyList<Bounds> obstacles, float margin)
        {
            Grid.ClearDynamic();
            foreach (var box in obstacles) Grid.BlockBox(box, margin);
        }
    }
}
