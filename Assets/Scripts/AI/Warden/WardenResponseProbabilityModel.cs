using System;

namespace Override.WardenAI
{
    /// <summary>An immutable, normalized snapshot used by chance nodes and debug UI.</summary>
    public readonly struct WardenResponseProbabilities
    {
        public float Peek { get; }
        public float Push { get; }
        public float Hide { get; }

        public WardenResponseProbabilities(float peek, float push, float hide)
        {
            float total = peek + push + hide;
            if (!WardenMath.IsFinite(total) || total <= 0f)
            {
                Peek = Push = Hide = 1f / 3f;
                return;
            }

            Peek = Math.Max(0f, peek) / total;
            Push = Math.Max(0f, push) / total;
            Hide = Math.Max(0f, hide) / total;
        }

        public float For(PlayerResponse response)
        {
            switch (response)
            {
                case PlayerResponse.Peek: return Peek;
                case PlayerResponse.Push: return Push;
                case PlayerResponse.Hide: return Hide;
                default: throw new ArgumentOutOfRangeException(nameof(response));
            }
        }
    }

    /// <summary>
    /// Learns response frequencies from discrete, directly observed events.
    /// Laplace-style pseudocounts keep every chance branch possible. A monotonically
    /// increasing event sequence rejects repeated per-frame reports of one event.
    /// </summary>
    public sealed class WardenResponseProbabilityModel
    {
        private float _peekCount;
        private float _pushCount;
        private float _hideCount;
        private long _lastObservationSequence = -1;

        public WardenResponseProbabilityModel(float pseudocountPerResponse = 1f)
        {
            if (!WardenMath.IsFinite(pseudocountPerResponse) || pseudocountPerResponse <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(pseudocountPerResponse));
            }

            _peekCount = pseudocountPerResponse;
            _pushCount = pseudocountPerResponse;
            _hideCount = pseudocountPerResponse;
        }

        public WardenResponseProbabilities Current
        {
            get { return new WardenResponseProbabilities(_peekCount, _pushCount, _hideCount); }
        }

        /// <summary>
        /// Records one gameplay event. Hidden/inferred responses and duplicate or stale
        /// sequence numbers are rejected, so this API is event-driven rather than frame-driven.
        /// </summary>
        public bool TryRecordObservedResponse(
            PlayerResponse response,
            long observationSequence,
            bool wasDirectlyObserved)
        {
            if (!wasDirectlyObserved || observationSequence <= _lastObservationSequence)
            {
                return false;
            }

            switch (response)
            {
                case PlayerResponse.Peek:
                    _peekCount += 1f;
                    break;
                case PlayerResponse.Push:
                    _pushCount += 1f;
                    break;
                case PlayerResponse.Hide:
                    _hideCount += 1f;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(response));
            }

            _lastObservationSequence = observationSequence;
            return true;
        }

        /// <summary>Calculates a chance-node expected value using the current model.</summary>
        public float CalculateExpectedValue(float peekValue, float pushValue, float hideValue)
        {
            WardenResponseProbabilities probabilities = Current;
            return probabilities.Peek * peekValue
                + probabilities.Push * pushValue
                + probabilities.Hide * hideValue;
        }
    }
}
