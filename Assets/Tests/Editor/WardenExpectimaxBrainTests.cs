using System;
using System.Linq;
using NUnit.Framework;
using Override.WardenAI;

namespace Override.WardenAI.Tests
{
    public sealed class WardenExpectimaxBrainTests
    {
        [Test]
        public void ExpectedValue_UsesLearnedChanceProbabilities()
        {
            var model = new WardenResponseProbabilityModel();
            Assert.That(model.TryRecordObservedResponse(PlayerResponse.Peek, 0, true), Is.True);
            Assert.That(model.TryRecordObservedResponse(PlayerResponse.Peek, 1, true), Is.True);
            Assert.That(model.TryRecordObservedResponse(PlayerResponse.Push, 2, true), Is.True);

            // Counts are Peek=3, Push=2, Hide=1 after pseudocounts.
            float value = model.CalculateExpectedValue(12f, 6f, 0f);

            Assert.That(value, Is.EqualTo(8f).Within(0.0001f));
        }

        [Test]
        public void DepthTwo_ExploresMoreLeavesAndCanValueSetupAction()
        {
            WardenDecisionState state = CreateBaselineState();
            state.PlayerVisible = false;
            state.HasKnownPlayerPosition = true;
            state.Ammo = 6;
            state.CurrentCoverQuality = 0.2f;
            state.AdvanceOption = Option(0.9f, 18f);
            state.FlankOption = null;
            state.RetreatOption = null;

            var oneTurn = new WardenExpectimaxBrain(new WardenExpectimaxSettings
            {
                DecisionTurns = 1,
                CoverWeight = 1f,
                RangeWeight = 1f,
                AmmoWeight = 0f,
                DamageDealtWeight = 1f,
                DamageTakenWeight = 0f,
                DesperationAggressionWeight = 0f
            }).Decide(state);

            var twoTurns = new WardenExpectimaxBrain(new WardenExpectimaxSettings
            {
                DecisionTurns = 2,
                CoverWeight = 1f,
                RangeWeight = 1f,
                AmmoWeight = 0f,
                DamageDealtWeight = 1f,
                DamageTakenWeight = 0f,
                DesperationAggressionWeight = 0f
            }).Decide(state);

            float oneTurnAdvance = ScoreOf(oneTurn, WardenAction.AdvanceToCover);
            float twoTurnAdvance = ScoreOf(twoTurns, WardenAction.AdvanceToCover);

            Assert.That(twoTurns.DecisionTurns, Is.EqualTo(2));
            Assert.That(twoTurns.EvaluatedLeafCount, Is.GreaterThan(oneTurn.EvaluatedLeafCount));
            Assert.That(twoTurnAdvance, Is.GreaterThan(oneTurnAdvance + 1f));
            Assert.That(WardenDecisionResult.DepthConvention, Does.Contain("Warden action -> player chance response"));
        }

        [Test]
        public void ProbabilityLearning_AcceptsOnlyUniqueDirectObservations()
        {
            var model = new WardenResponseProbabilityModel();

            Assert.That(model.TryRecordObservedResponse(PlayerResponse.Peek, 10, true), Is.True);
            Assert.That(model.TryRecordObservedResponse(PlayerResponse.Peek, 10, true), Is.False);
            Assert.That(model.TryRecordObservedResponse(PlayerResponse.Push, 11, false), Is.False);
            Assert.That(model.TryRecordObservedResponse(PlayerResponse.Hide, 9, true), Is.False);

            WardenResponseProbabilities probabilities = model.Current;
            Assert.That(probabilities.Peek, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(probabilities.Push, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(probabilities.Hide, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void InvalidActions_AreFilteredWithUsefulReasons()
        {
            WardenDecisionState state = CreateBaselineState();
            state.PlayerVisible = false;
            state.HasKnownPlayerPosition = true;
            state.Ammo = 0;
            state.AdvanceOption = new WardenTacticalOption
            {
                PathAvailable = true,
                CoverAvailable = false,
                ResultingDistanceToPlayer = 15f
            };
            state.FlankOption = new WardenTacticalOption
            {
                PathAvailable = true,
                CoverAvailable = true,
                CoverOccupied = true,
                ResultingDistanceToPlayer = 15f
            };
            state.RetreatOption = new WardenTacticalOption
            {
                PathAvailable = false,
                CoverAvailable = true,
                ResultingDistanceToPlayer = 25f
            };

            WardenDecisionResult result = new WardenExpectimaxBrain().Decide(state);

            AssertInvalid(result, WardenAction.PeekAndFire, "hidden");
            AssertInvalid(result, WardenAction.AdvanceToCover, "destroyed");
            AssertInvalid(result, WardenAction.Flank, "occupied");
            AssertInvalid(result, WardenAction.RetreatAndReload, "path");
            Assert.That(result.SelectedAction, Is.EqualTo(WardenAction.Hold));
            Assert.That(result.UsedFallback, Is.False, "Hold is a valid deliberate action here, not the emergency fallback.");
        }

        [Test]
        public void EmptyAmmo_WithRetreatPath_CanChooseSafeReloadAction()
        {
            WardenDecisionState state = CreateBaselineState();
            state.Ammo = 0;
            state.CanHoldPosition = false;
            state.AdvanceOption = null;
            state.FlankOption = null;
            state.RetreatOption = Option(0.9f, 24f);

            WardenDecisionResult result = new WardenExpectimaxBrain().Decide(state);

            Assert.That(result.SelectedAction, Is.EqualTo(WardenAction.RetreatAndReload));
            Assert.That(result.UsedFallback, Is.False);
        }

        [Test]
        public void NoValidAction_UsesInertHoldFallback()
        {
            WardenDecisionState state = CreateBaselineState();
            state.CanHoldPosition = false;
            state.PlayerVisible = false;
            state.HasKnownPlayerPosition = false;
            state.Ammo = 0;
            state.AdvanceOption = null;
            state.FlankOption = null;
            state.RetreatOption = null;

            WardenDecisionResult result = new WardenExpectimaxBrain().Decide(state);

            Assert.That(result.UsedFallback, Is.True);
            Assert.That(result.SelectedAction, Is.EqualTo(WardenAction.Hold));
            Assert.That(result.FallbackReason, Does.Contain("No valid"));
            Assert.That(result.CandidateScores.All(candidate => !candidate.IsValid), Is.True);
        }

        [Test]
        public void InvalidState_UsesFallbackWithoutThrowing()
        {
            WardenDecisionState state = CreateBaselineState();
            state.DistanceToPlayer = float.NaN;

            Assert.DoesNotThrow(() => new WardenExpectimaxBrain().Decide(state));
            Assert.That(new WardenExpectimaxBrain().Decide(state).UsedFallback, Is.True);
            Assert.That(new WardenExpectimaxBrain().Decide(null).UsedFallback, Is.True);
        }

        private static WardenDecisionState CreateBaselineState()
        {
            return new WardenDecisionState
            {
                PlayerVisible = true,
                HasKnownPlayerPosition = true,
                DistanceToPlayer = 18f,
                Ammo = 12,
                MaxAmmo = 12,
                CurrentCoverUsable = true,
                CurrentCoverQuality = 0.7f,
                Desperation = 0.25f,
                AdvanceOption = Option(0.8f, 14f),
                FlankOption = Option(0.75f, 18f),
                RetreatOption = Option(0.9f, 24f)
            };
        }

        private static WardenTacticalOption Option(float quality, float distance)
        {
            return new WardenTacticalOption
            {
                PathAvailable = true,
                CoverAvailable = true,
                CoverOccupied = false,
                CoverQuality = quality,
                ResultingDistanceToPlayer = distance
            };
        }

        private static float ScoreOf(WardenDecisionResult result, WardenAction action)
        {
            return result.CandidateScores.Single(candidate => candidate.Action == action).Score;
        }

        private static void AssertInvalid(
            WardenDecisionResult result,
            WardenAction action,
            string expectedReasonFragment)
        {
            WardenActionScore score = result.CandidateScores.Single(candidate => candidate.Action == action);
            Assert.That(score.IsValid, Is.False);
            Assert.That(score.InvalidReason, Does.Contain(expectedReasonFragment).IgnoreCase);
        }
    }
}
