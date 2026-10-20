// DirectorBrainTests.cs
// Edit-mode tests for the Director (Window > General > Test Runner > EditMode > Run All).
// No scene needed: every test builds a DirectorSnapshot by hand.
//
// The bay positions are the REAL ones from the level (DroneBayBuilder, LevelSize.Plan = 2.5), so the
// tests check the brain against the layout the game actually uses.
// Each test name says the behaviour; the GDD's Director edge cases are marked "Edge case".

using System;
using NUnit.Framework;
using Override.DirectorAI;
using UnityEngine;

namespace Override.DirectorAI.Tests
{
    public sealed class DirectorBrainTests
    {
        // ---- The level, in metres ----
        static readonly (string zone, float x, float z)[] LevelBays =
        {
            ("Labs", 134f, 46f), ("Labs", 134f, 25f),
            ("Maintenance", 138f, 92f), ("Maintenance", 136f, 62.5f),
            ("ServerHall", 61f, 82.75f), ("ServerHall", 55f, 91.5f),
            ("Core", 93.3f, 125f), ("Core", 56.7f, 125f), ("Core", 75f, 143.3f), ("Core", 75f, 106.7f),
        };
        static readonly Vector3 NearT1 = new Vector3(126f, 0f, 35.5f);   // walking up to Terminal 1
        static readonly Vector3 InCore = new Vector3(75f, 0f, 131f);     // at the override console

        static DirectorSnapshot Snapshot(Vector3 player, string zone, float damage = 0f, float alarm = 20f,
                                         float sinceFight = 60f, int active = 0, int budget = 4)
        {
            var s = new DirectorSnapshot
            {
                PlayerPosition = player, PlayerZone = zone, RecentDamage = damage, Alarm = alarm,
                SecondsSinceEngagement = sinceFight, ActiveDrones = active, DroneBudget = budget,
                Bays = new BayInfo[LevelBays.Length],
            };
            for (int i = 0; i < LevelBays.Length; i++)
                s.Bays[i] = new BayInfo { Id = i, Zone = LevelBays[i].zone, Position = new Vector3(LevelBays[i].x, 0.7f, LevelBays[i].z) };
            return s;
        }

        static DirectorBrain Brain(bool annealing = true, int seed = 1) =>
            new DirectorBrain(new DirectorSettings { UseAnnealing = annealing }, seed);

        // ==================================================================
        // Tension and pressure
        // ==================================================================
        [Test]
        public void IdlePlayer_GetsDronesSent()   // Edge case: player standing idle
        {
            var d = Brain().Decide(Snapshot(NearT1, "Labs", damage: 0f, alarm: 10f, sinceFight: 120f));
            Assert.Less(d.Tension, 0.35f, "an idle player should read as below the band");
            Assert.GreaterOrEqual(d.Plan.TotalDrones, 2, "low tension should launch drones: " + d.Plan);
        }

        [Test]
        public void OverwhelmedPlayer_GetsNothing()
        {
            var d = Brain().Decide(Snapshot(NearT1, "Labs", damage: 0.8f, alarm: 90f, sinceFight: 0f));
            Assert.Greater(d.Tension, 0.65f);
            Assert.AreEqual(0, d.Plan.TotalDrones, "high tension should back off: " + d.Plan);
        }

        [Test]
        public void NearbyBaysArePreferred()
        {
            var d = Brain().Decide(Snapshot(NearT1, "Labs"));
            var s = Snapshot(NearT1, "Labs");
            for (int i = 0; i < d.Plan.DronesPerBay.Length; i++)
                if (d.Plan.DronesPerBay[i] > 0)
                    Assert.AreEqual("Labs", s.Bays[i].Zone, "a drone from far away arrives too late: " + d.Plan);
        }

        [Test]
        public void TwoDrones_ComeFromBothFlanks()
        {
            // Bays 0 and 1 are north and south of the player. Two drones from one bay = one direction;
            // one from each = a pincer, which the Flank term rewards.
            var d = Brain().Decide(Snapshot(NearT1, "Labs", alarm: 30f, sinceFight: 40f));
            if (d.Plan.TotalDrones >= 2)
                Assert.IsTrue(d.Plan.DronesPerBay[0] > 0 && d.Plan.DronesPerBay[1] > 0, "expected a pincer: " + d.Plan);
        }

        // ==================================================================
        // Hard rules
        // ==================================================================
        [Test]
        public void EverySpawnBlocked_LaunchesNothing()   // Edge case: every spawn point blocked
        {
            var s = Snapshot(NearT1, "Labs", sinceFight: 120f);
            for (int i = 0; i < s.Bays.Length; i++) s.Bays[i].Blocked = true;
            var d = Brain().Decide(s);
            Assert.AreEqual(0, d.Plan.TotalDrones, d.Plan.ToString());
        }

        [Test]
        public void BlockedBaysAreAvoided_OthersStillUsed()
        {
            var s = Snapshot(NearT1, "Labs", sinceFight: 120f);
            s.Bays[0].Blocked = true;
            var d = Brain().Decide(s);
            Assert.AreEqual(0, d.Plan.DronesPerBay[0], d.Plan.ToString());
            Assert.Greater(d.Plan.TotalDrones, 0, "one blocked bay shouldn't stop the Director: " + d.Plan);
        }

        [Test]
        public void HalvedBudget_IsNeverExceeded()   // Terminal 3: fabrication capacity halved
        {
            var d = Brain().Decide(Snapshot(NearT1, "Labs", sinceFight: 120f, active: 1, budget: 2));
            Assert.LessOrEqual(d.Plan.TotalDrones, 1, d.Plan.ToString());
        }

        [Test]
        public void FinalWave_UsesTheWholeBudget()   // Edge case: final-wave budget
        {
            var s = Snapshot(InCore, "Core", damage: 0.5f, alarm: 100f, sinceFight: 0f, budget: 4);
            s.FinalWave = true;
            var d = Brain().Decide(s);
            Assert.AreEqual(4, d.Plan.TotalDrones, "final wave ignores tension: " + d.Plan);
        }

        [Test]
        public void SubsystemLostMidPlan_PlanStaysLegal()   // Edge case: subsystem lost mid-plan
        {
            var brain = Brain();
            var s = Snapshot(NearT1, "Labs", sinceFight: 120f);
            s.Doors = new[] { new DoorInfo { Id = 0, PlayerZoneExit = true }, new DoorInfo { Id = 1, PlayerZoneExit = true },
                              new DoorInfo { Id = 2 } };
            brain.Decide(s);                         // a plan that may lock doors...
            s.DoorControlOnline = false;             // ...then Terminal 2 is overridden
            s.DroneBudget = 2;                       // ...and Terminal 3 too
            var d = brain.Decide(s);
            Assert.AreEqual(0, d.Plan.LockCount, "no door control -> no locks: " + d.Plan);
            Assert.LessOrEqual(d.Plan.TotalDrones, 2, d.Plan.ToString());
        }

        [Test]
        public void NeverLocksEveryExitOfThePlayersZone()
        {
            var s = Snapshot(NearT1, "Labs", sinceFight: 120f);
            s.Doors = new[] { new DoorInfo { Id = 0, PlayerZoneExit = true }, new DoorInfo { Id = 1, PlayerZoneExit = true } };
            for (int seed = 0; seed < 20; seed++)
            {
                var d = Brain(seed: seed).Decide(s);
                Assert.Less(d.Plan.LockCount, 2, "the player must always have a way out: " + d.Plan);
            }
        }

        [Test]
        public void NoBaysAtAll_DoesNotCrash()
        {
            var s = Snapshot(NearT1, "Labs");
            s.Bays = new BayInfo[0];
            Assert.AreEqual(0, Brain().Decide(s).Plan.TotalDrones);
        }

        [Test]
        public void SameSeed_SameDecision()
        {
            var s = Snapshot(NearT1, "Labs", sinceFight: 120f);
            Assert.AreEqual(Brain(seed: 7).Decide(s).Plan.ToString(), Brain(seed: 7).Decide(s).Plan.ToString());
        }

        // ==================================================================
        // The algorithm comparison (viva evidence)
        // ==================================================================
        [Test]
        public void Annealing_BeatsHillDescent_AgainstTheTrueOptimum()
        {
            // 200 random situations: random player spot in the level, random tension inputs, random
            // blocked bays, random doors. Both algorithms get the SAME situations, the SAME cost function
            // and the SAME moves, starting from the same empty plan.
            //
            // The space here is small enough to also try EVERY plan (brute force), which gives the true
            // best answer. That turns "SA seems better" into "SA finds the true best X% of the time".
            var world = new System.Random(2026);
            const int Runs = 200;
            double saCost = 0, hdCost = 0, bestCost = 0, saMs = 0, hdMs = 0, bruteMs = 0;
            int saOptimal = 0, hdOptimal = 0, saBetter = 0, hdBetter = 0, saEvals = 0, hdEvals = 0;
            long bruteEvals = 0;
            var clock = new System.Diagnostics.Stopwatch();

            for (int run = 0; run < Runs; run++)
            {
                var s = RandomSituation(world);

                clock.Restart(); var sa = Brain(true, seed: run).Decide(s); saMs += clock.Elapsed.TotalMilliseconds;
                clock.Restart(); var hd = Brain(false, seed: run).Decide(s); hdMs += clock.Elapsed.TotalMilliseconds;
                clock.Restart(); float best = BruteForce(s, out long evals); bruteMs += clock.Elapsed.TotalMilliseconds;

                saCost += sa.Search.BestCost; hdCost += hd.Search.BestCost; bestCost += best;
                saEvals += sa.Search.Evaluations; hdEvals += hd.Search.Evaluations; bruteEvals += evals;
                if (sa.Search.BestCost <= best + 1e-4f) saOptimal++;
                if (hd.Search.BestCost <= best + 1e-4f) hdOptimal++;
                if (sa.Search.BestCost < hd.Search.BestCost - 1e-4f) saBetter++;
                if (hd.Search.BestCost < sa.Search.BestCost - 1e-4f) hdBetter++;
            }

            TestContext.WriteLine($"Over {Runs} random situations (10 real bays, 0-3 doors):");
            TestContext.WriteLine($"  Algorithm            mean cost  found true best  evaluations  ms/decision");
            TestContext.WriteLine($"  Simulated annealing  {saCost / Runs,9:0.000}  {saOptimal,8}/{Runs}  {saEvals / Runs,11}  {saMs / Runs,11:0.000}");
            TestContext.WriteLine($"  Hill descent         {hdCost / Runs,9:0.000}  {hdOptimal,8}/{Runs}  {hdEvals / Runs,11}  {hdMs / Runs,11:0.000}");
            TestContext.WriteLine($"  Brute force          {bestCost / Runs,9:0.000}  {Runs,8}/{Runs}  {bruteEvals / Runs,11}  {bruteMs / Runs,11:0.000}");
            TestContext.WriteLine($"  SA strictly better in {saBetter}, HD strictly better in {hdBetter}, tied in {Runs - saBetter - hdBetter}");

            Assert.LessOrEqual(saCost / Runs, hdCost / Runs, "SA should be at least as good as hill descent on average");
            Assert.GreaterOrEqual(saOptimal, hdOptimal, "SA should reach the true optimum at least as often");
        }

        static DirectorSnapshot RandomSituation(System.Random world)
        {
            var s = Snapshot(new Vector3((float)world.NextDouble() * 150f, 0f, (float)world.NextDouble() * 145f), "?",
                             damage: (float)world.NextDouble() * 0.5f, alarm: (float)world.NextDouble() * 100f,
                             sinceFight: (float)world.NextDouble() * 60f, active: world.Next(3), budget: world.Next(2) == 0 ? 4 : 2);
            for (int i = 0; i < s.Bays.Length; i++) s.Bays[i].Blocked = world.NextDouble() < 0.2;
            s.Doors = new DoorInfo[world.Next(4)];
            for (int i = 0; i < s.Doors.Length; i++) s.Doors[i] = new DoorInfo { Id = i, PlayerZoneExit = world.Next(2) == 0 };
            return s;
        }

        // Tries every plan with at most 4 drones (more always breaks the budget) and every lock combination.
        // Only possible because this space is small (3^10 x 2^3); it's the yardstick, not something to ship.
        static float BruteForce(DirectorSnapshot s, out long evaluations)
        {
            var brain = Brain();
            float tension = brain.Tension(s);
            var cost = new DirectorCost(s, brain.NeededPressure(s, tension), brain.Settings.Weights, null);
            var plan = new DirectorPlan(s.Bays.Length, s.Doors.Length);
            float best = float.MaxValue;
            evaluations = 0;
            int combos = (int)Math.Pow(DirectorPlan.MaxPerBay + 1, s.Bays.Length);
            for (int c = 0; c < combos; c++)
            {
                int x = c, total = 0;
                for (int i = 0; i < s.Bays.Length; i++) { plan.DronesPerBay[i] = x % 3; total += x % 3; x /= 3; }
                if (total > 4) continue;
                for (int mask = 0; mask < (1 << s.Doors.Length); mask++)
                {
                    for (int k = 0; k < s.Doors.Length; k++) plan.LockDoor[k] = ((mask >> k) & 1) == 1;
                    best = Math.Min(best, cost.Evaluate(plan));
                    evaluations++;
                }
            }
            return best;
        }
    }
}
