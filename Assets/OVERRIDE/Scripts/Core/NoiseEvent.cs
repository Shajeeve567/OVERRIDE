// NoiseEvent.cs
// A sound the AI can hear. Emitters (footsteps, doors, crates, canister, gunshots, terminals)
// build one of these and send it through NoiseBus; agents' hearing decides who hears it.

using UnityEngine;

namespace Override.Core
{
    /// What made the sound. Agents may weigh kinds differently (the Hunter treats them all as evidence).
    public enum NoiseKind
    {
        Footstep,
        Door,
        Crate,
        Canister,
        Gunshot,
        TerminalBeep,
    }

    /// One sound at one moment. Radius is in metres before door damping (GDD noise table).
    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly NoiseKind Kind;
        public readonly float Time;

        public NoiseEvent(Vector3 position, float radius, NoiseKind kind, float time)
        {
            Position = position;
            Radius = radius;
            Kind = kind;
            Time = time;
        }
    }
}
