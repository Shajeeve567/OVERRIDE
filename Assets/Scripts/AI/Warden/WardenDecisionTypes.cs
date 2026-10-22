using System;
using System.Collections.Generic;

namespace Override.WardenAI
{
    /// <summary>The five abstract actions considered by the Warden.</summary>
    public enum WardenAction
    {
        Hold,
        PeekAndFire,
        AdvanceToCover,
        Flank,
        RetreatAndReload
    }

    /// <summary>A directly observed player response used at Expectimax chance nodes.</summary>
    public enum PlayerResponse
    {
        Peek,
        Push,
        Hide
    }

    /// <summary>
    /// Describes one navigation/cover result supplied by a future gameplay adapter.
    /// The brain never performs pathfinding or queries scene objects itself.
    /// </summary>
    [Serializable]
    public sealed class WardenTacticalOption
    {
        public bool PathAvailable { get; set; }
        public bool CoverAvailable { get; set; }
        public bool CoverOccupied { get; set; }
        public float CoverQuality { get; set; }
        public float ResultingDistanceToPlayer { get; set; }

        public bool IsUsable
        {
            get
            {
                return PathAvailable
                    && CoverAvailable
                    && !CoverOccupied
                    && WardenMath.IsFinite(CoverQuality)
                    && WardenMath.IsFinite(ResultingDistanceToPlayer)
                    && ResultingDistanceToPlayer >= 0f;
            }
        }
    }

    /// <summary>
    /// Warden-specific input snapshot. An integration adapter can build this from
    /// gameplay systems later without expanding the team's shared contracts.
    /// </summary>
    [Serializable]
    public sealed class WardenDecisionState
    {
        public bool IsOperational { get; set; } = true;
        public bool CanHoldPosition { get; set; } = true;
        public bool PlayerVisible { get; set; }
        public bool HasKnownPlayerPosition { get; set; }
        public float DistanceToPlayer { get; set; }
        public int Ammo { get; set; }
        public int MaxAmmo { get; set; } = 12;
        public bool CurrentCoverUsable { get; set; }
        public float CurrentCoverQuality { get; set; }
        public float Desperation { get; set; }
        public WardenTacticalOption AdvanceOption { get; set; }
        public WardenTacticalOption FlankOption { get; set; }
        public WardenTacticalOption RetreatOption { get; set; }

        public bool HasValidCoreValues
        {
            get
            {
                return IsOperational
                    && MaxAmmo > 0
                    && Ammo >= 0
                    && Ammo <= MaxAmmo
                    && WardenMath.IsFinite(DistanceToPlayer)
                    && DistanceToPlayer >= 0f
                    && WardenMath.IsFinite(CurrentCoverQuality)
                    && WardenMath.IsFinite(Desperation);
            }
        }
    }

    /// <summary>Configurable utility weights and abstract combat estimates.</summary>
    [Serializable]
    public sealed class WardenExpectimaxSettings
    {
        /// <summary>
        /// Production depth is two Warden turns. A one-turn value is permitted only
        /// so focused tests and debug tools can demonstrate the value of lookahead.
        /// </summary>
        public int DecisionTurns { get; set; } = 2;

        public int BurstAmmoCost { get; set; } = 3;
        public float MaximumFireRange { get; set; } = 30f;
        public float PreferredRange { get; set; } = 18f;

        public float DamageDealtWeight { get; set; } = 1f;
        public float DamageTakenWeight { get; set; } = 1.25f;
        public float CoverWeight { get; set; } = 8f;
        public float RangeWeight { get; set; } = 5f;
        public float AmmoWeight { get; set; } = 4f;
        public float DesperationAggressionWeight { get; set; } = 6f;

        public float PeekDamageAgainstPeek { get; set; } = 24f;
        public float PeekDamageAgainstPush { get; set; } = 18f;
        public float ReturnDamageAgainstPeek { get; set; } = 10f;
        public float ReturnDamageAgainstPush { get; set; } = 14f;
        public float PushDistanceChange { get; set; } = 4f;

        public void Validate()
        {
            if (DecisionTurns < 1 || DecisionTurns > 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DecisionTurns),
                    "The standalone Warden supports the one-turn diagnostic baseline or the required depth of two.");
            }

            if (BurstAmmoCost <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(BurstAmmoCost));
            }

            if (!WardenMath.IsFinite(MaximumFireRange) || MaximumFireRange <= 0f
                || !WardenMath.IsFinite(PreferredRange) || PreferredRange <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumFireRange));
            }
        }
    }

    /// <summary>A candidate's root score or the reason it was filtered out.</summary>
    public sealed class WardenActionScore
    {
        public WardenAction Action { get; }
        public bool IsValid { get; }
        public float Score { get; }
        public string InvalidReason { get; }

        public WardenActionScore(WardenAction action, float score)
        {
            Action = action;
            IsValid = true;
            Score = score;
            InvalidReason = string.Empty;
        }

        public WardenActionScore(WardenAction action, string invalidReason)
        {
            Action = action;
            IsValid = false;
            Score = float.NegativeInfinity;
            InvalidReason = invalidReason ?? "Action is unavailable.";
        }
    }

    /// <summary>Debug-friendly output from one complete Expectimax decision.</summary>
    public sealed class WardenDecisionResult
    {
        public const string DepthConvention =
            "Depth 2 = Warden action -> player chance response -> Warden action -> player chance response -> evaluation.";

        public WardenAction SelectedAction { get; }
        public IReadOnlyList<WardenActionScore> CandidateScores { get; }
        public WardenResponseProbabilities ResponseProbabilities { get; }
        public int DecisionTurns { get; }
        public int EvaluatedLeafCount { get; }
        public bool UsedFallback { get; }
        public string FallbackReason { get; }

        internal WardenDecisionResult(
            WardenAction selectedAction,
            IReadOnlyList<WardenActionScore> candidateScores,
            WardenResponseProbabilities responseProbabilities,
            int decisionTurns,
            int evaluatedLeafCount,
            bool usedFallback,
            string fallbackReason)
        {
            SelectedAction = selectedAction;
            CandidateScores = candidateScores;
            ResponseProbabilities = responseProbabilities;
            DecisionTurns = decisionTurns;
            EvaluatedLeafCount = evaluatedLeafCount;
            UsedFallback = usedFallback;
            FallbackReason = fallbackReason ?? string.Empty;
        }
    }

    internal static class WardenMath
    {
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
