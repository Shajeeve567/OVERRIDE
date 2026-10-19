using System;
using System.Collections.Generic;

namespace Override.WardenAI
{
    /// <summary>
    /// Pure C# depth-2 Expectimax decision logic for the Warden. It owns no Unity
    /// objects and performs no movement, shooting, physics or scene queries.
    /// </summary>
    public sealed class WardenExpectimaxBrain
    {
        private static readonly WardenAction[] AllActions =
        {
            WardenAction.Hold,
            WardenAction.PeekAndFire,
            WardenAction.AdvanceToCover,
            WardenAction.Flank,
            WardenAction.RetreatAndReload
        };

        private static readonly PlayerResponse[] AllResponses =
        {
            PlayerResponse.Peek,
            PlayerResponse.Push,
            PlayerResponse.Hide
        };

        private readonly WardenExpectimaxSettings _settings;
        private readonly WardenResponseProbabilityModel _responseModel;
        private int _evaluatedLeafCount;

        public WardenExpectimaxBrain(
            WardenExpectimaxSettings settings = null,
            WardenResponseProbabilityModel responseModel = null)
        {
            _settings = settings ?? new WardenExpectimaxSettings();
            _settings.Validate();
            _responseModel = responseModel ?? new WardenResponseProbabilityModel();
        }

        public WardenResponseProbabilityModel ResponseModel
        {
            get { return _responseModel; }
        }

        /// <summary>
        /// Scores every root action. Production uses two Warden decision turns with
        /// a learned player-response chance node after each Warden action.
        /// </summary>
        public WardenDecisionResult Decide(WardenDecisionState state)
        {
            _evaluatedLeafCount = 0;
            WardenResponseProbabilities probabilities = _responseModel.Current;
            var scores = new List<WardenActionScore>(AllActions.Length);

            if (state == null || !state.HasValidCoreValues)
            {
                string reason = state == null
                    ? "Decision state is missing."
                    : "Decision state contains invalid core values.";
                AddAllInvalidScores(scores, reason);
                return CreateFallback(scores, probabilities, reason);
            }

            SimulatedState root = SimulatedState.From(state);
            bool foundValid = false;
            float bestScore = float.NegativeInfinity;
            WardenAction bestAction = WardenAction.Hold;

            foreach (WardenAction action in AllActions)
            {
                string invalidReason;
                if (!TryValidateAction(root, action, out invalidReason))
                {
                    scores.Add(new WardenActionScore(action, invalidReason));
                    continue;
                }

                float score = ScoreAction(root, action, _settings.DecisionTurns, probabilities);
                scores.Add(new WardenActionScore(action, score));
                if (!foundValid || score > bestScore)
                {
                    foundValid = true;
                    bestScore = score;
                    bestAction = action;
                }
            }

            if (!foundValid)
            {
                return CreateFallback(scores, probabilities, "No valid Warden action was available.");
            }

            return new WardenDecisionResult(
                bestAction,
                scores.AsReadOnly(),
                probabilities,
                _settings.DecisionTurns,
                _evaluatedLeafCount,
                false,
                string.Empty);
        }

        private float ScoreAction(
            SimulatedState state,
            WardenAction action,
            int turnsRemaining,
            WardenResponseProbabilities probabilities)
        {
            SimulatedState afterAction = ApplyAction(state, action);
            float expectedValue = 0f;

            foreach (PlayerResponse response in AllResponses)
            {
                SimulatedState afterResponse = ApplyResponse(afterAction, action, response);
                float branchValue;

                if (turnsRemaining <= 1)
                {
                    _evaluatedLeafCount++;
                    branchValue = Evaluate(afterResponse);
                }
                else
                {
                    branchValue = BestFutureAction(afterResponse, turnsRemaining - 1, probabilities);
                }

                expectedValue += probabilities.For(response) * branchValue;
            }

            return expectedValue;
        }

        private float BestFutureAction(
            SimulatedState state,
            int turnsRemaining,
            WardenResponseProbabilities probabilities)
        {
            bool foundValid = false;
            float bestScore = float.NegativeInfinity;

            foreach (WardenAction action in AllActions)
            {
                string ignoredReason;
                if (!TryValidateAction(state, action, out ignoredReason))
                {
                    continue;
                }

                float score = ScoreAction(state, action, turnsRemaining, probabilities);
                if (!foundValid || score > bestScore)
                {
                    foundValid = true;
                    bestScore = score;
                }
            }

            if (foundValid)
            {
                return bestScore;
            }

            _evaluatedLeafCount++;
            return Evaluate(state);
        }

        private bool TryValidateAction(
            SimulatedState state,
            WardenAction action,
            out string invalidReason)
        {
            switch (action)
            {
                case WardenAction.Hold:
                    if (!state.CanHoldPosition)
                    {
                        invalidReason = "The current position cannot be held safely.";
                        return false;
                    }
                    break;

                case WardenAction.PeekAndFire:
                    if (!state.PlayerVisible)
                    {
                        invalidReason = "The player is hidden; firing would use hidden information.";
                        return false;
                    }
                    if (state.Ammo < _settings.BurstAmmoCost)
                    {
                        invalidReason = "Not enough ammo for a burst.";
                        return false;
                    }
                    if (state.DistanceToPlayer > _settings.MaximumFireRange)
                    {
                        invalidReason = "The visible player is outside firing range.";
                        return false;
                    }
                    break;

                case WardenAction.AdvanceToCover:
                    if (!state.HasKnownPlayerPosition)
                    {
                        invalidReason = "No observed or remembered player position is available.";
                        return false;
                    }
                    if (!TryValidateTacticalOption(state.AdvanceOption, "advance", out invalidReason))
                    {
                        return false;
                    }
                    break;

                case WardenAction.Flank:
                    if (!state.HasKnownPlayerPosition)
                    {
                        invalidReason = "No observed or remembered player position is available.";
                        return false;
                    }
                    if (!TryValidateTacticalOption(state.FlankOption, "flank", out invalidReason))
                    {
                        return false;
                    }
                    break;

                case WardenAction.RetreatAndReload:
                    if (!TryValidateTacticalOption(state.RetreatOption, "retreat", out invalidReason))
                    {
                        return false;
                    }
                    break;

                default:
                    invalidReason = "Unknown action.";
                    return false;
            }

            invalidReason = string.Empty;
            return true;
        }

        private static bool TryValidateTacticalOption(
            WardenTacticalOption option,
            string label,
            out string invalidReason)
        {
            if (option == null)
            {
                invalidReason = "The " + label + " option is missing.";
                return false;
            }
            if (!option.PathAvailable)
            {
                invalidReason = "The " + label + " path is unavailable.";
                return false;
            }
            if (!option.CoverAvailable)
            {
                invalidReason = "The " + label + " cover was destroyed or is unavailable.";
                return false;
            }
            if (option.CoverOccupied)
            {
                invalidReason = "The " + label + " cover is occupied.";
                return false;
            }
            if (!option.IsUsable)
            {
                invalidReason = "The " + label + " option contains invalid values.";
                return false;
            }

            invalidReason = string.Empty;
            return true;
        }

        private SimulatedState ApplyAction(SimulatedState original, WardenAction action)
        {
            SimulatedState state = original.Copy();
            switch (action)
            {
                case WardenAction.Hold:
                    state.AggressiveMomentum -= 0.1f;
                    break;
                case WardenAction.PeekAndFire:
                    state.Ammo -= _settings.BurstAmmoCost;
                    state.AggressiveMomentum += 0.35f;
                    break;
                case WardenAction.AdvanceToCover:
                    ApplyTacticalOption(state, state.AdvanceOption);
                    state.AggressiveMomentum += 0.55f;
                    break;
                case WardenAction.Flank:
                    ApplyTacticalOption(state, state.FlankOption);
                    state.AggressiveMomentum += 0.75f;
                    break;
                case WardenAction.RetreatAndReload:
                    ApplyTacticalOption(state, state.RetreatOption);
                    state.Ammo = state.MaxAmmo;
                    state.AggressiveMomentum -= 0.8f;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }

            return state;
        }

        private static void ApplyTacticalOption(SimulatedState state, WardenTacticalOption option)
        {
            state.DistanceToPlayer = option.ResultingDistanceToPlayer;
            state.CurrentCoverQuality = WardenMath.Clamp01(option.CoverQuality);
            state.CurrentCoverUsable = true;
        }

        private SimulatedState ApplyResponse(
            SimulatedState original,
            WardenAction action,
            PlayerResponse response)
        {
            SimulatedState state = original.Copy();
            switch (response)
            {
                case PlayerResponse.Peek:
                    state.PlayerVisible = true;
                    state.HasKnownPlayerPosition = true;
                    if (action == WardenAction.PeekAndFire)
                    {
                        state.DamageDealt += _settings.PeekDamageAgainstPeek;
                        state.DamageTaken += _settings.ReturnDamageAgainstPeek;
                    }
                    break;

                case PlayerResponse.Push:
                    state.PlayerVisible = true;
                    state.HasKnownPlayerPosition = true;
                    state.DistanceToPlayer = Math.Max(0f, state.DistanceToPlayer - _settings.PushDistanceChange);
                    if (action == WardenAction.PeekAndFire)
                    {
                        state.DamageDealt += _settings.PeekDamageAgainstPush;
                        state.DamageTaken += _settings.ReturnDamageAgainstPush;
                    }
                    else
                    {
                        float exposure = 1f - WardenMath.Clamp01(state.CurrentCoverQuality);
                        state.DamageTaken += 6f * exposure;
                    }
                    break;

                case PlayerResponse.Hide:
                    state.PlayerVisible = false;
                    state.HasKnownPlayerPosition = true;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(response));
            }

            return state;
        }

        private float Evaluate(SimulatedState state)
        {
            float cover = state.CurrentCoverUsable
                ? WardenMath.Clamp01(state.CurrentCoverQuality)
                : 0f;
            float ammo = WardenMath.Clamp01((float)state.Ammo / state.MaxAmmo);
            float rangeScore = 0f;
            if (state.HasKnownPlayerPosition)
            {
                float error = Math.Abs(state.DistanceToPlayer - _settings.PreferredRange);
                rangeScore = 1f - WardenMath.Clamp01(error / _settings.PreferredRange);
            }

            return state.DamageDealt * _settings.DamageDealtWeight
                - state.DamageTaken * _settings.DamageTakenWeight
                + cover * _settings.CoverWeight
                + rangeScore * _settings.RangeWeight
                + ammo * _settings.AmmoWeight
                + state.Desperation * state.AggressiveMomentum * _settings.DesperationAggressionWeight;
        }

        private WardenDecisionResult CreateFallback(
            List<WardenActionScore> scores,
            WardenResponseProbabilities probabilities,
            string reason)
        {
            return new WardenDecisionResult(
                WardenAction.Hold,
                scores.AsReadOnly(),
                probabilities,
                _settings.DecisionTurns,
                _evaluatedLeafCount,
                true,
                reason);
        }

        private static void AddAllInvalidScores(List<WardenActionScore> scores, string reason)
        {
            foreach (WardenAction action in AllActions)
            {
                scores.Add(new WardenActionScore(action, reason));
            }
        }

        private sealed class SimulatedState
        {
            public bool CanHoldPosition;
            public bool PlayerVisible;
            public bool HasKnownPlayerPosition;
            public float DistanceToPlayer;
            public int Ammo;
            public int MaxAmmo;
            public bool CurrentCoverUsable;
            public float CurrentCoverQuality;
            public float Desperation;
            public WardenTacticalOption AdvanceOption;
            public WardenTacticalOption FlankOption;
            public WardenTacticalOption RetreatOption;
            public float DamageDealt;
            public float DamageTaken;
            public float AggressiveMomentum;

            public static SimulatedState From(WardenDecisionState source)
            {
                return new SimulatedState
                {
                    CanHoldPosition = source.CanHoldPosition,
                    PlayerVisible = source.PlayerVisible,
                    HasKnownPlayerPosition = source.HasKnownPlayerPosition || source.PlayerVisible,
                    DistanceToPlayer = source.DistanceToPlayer,
                    Ammo = source.Ammo,
                    MaxAmmo = source.MaxAmmo,
                    CurrentCoverUsable = source.CurrentCoverUsable,
                    CurrentCoverQuality = source.CurrentCoverQuality,
                    Desperation = WardenMath.Clamp01(source.Desperation),
                    AdvanceOption = source.AdvanceOption,
                    FlankOption = source.FlankOption,
                    RetreatOption = source.RetreatOption
                };
            }

            public SimulatedState Copy()
            {
                return (SimulatedState)MemberwiseClone();
            }
        }
    }
}
