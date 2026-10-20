// IHunterSenses.cs
// Everything the Hunter brain is allowed to know, supplied by its body. The brain never reads the
// player's true position; it only gets what the Seeker could actually sense.

using System.Collections.Generic;
using UnityEngine;

namespace Override.Hunter
{
    /// A noise the Seeker heard. Muffled = it came through a closed door (harder to place).
    public readonly struct HeardNoise
    {
        public readonly Vector3 Position;
        public readonly bool Muffled;

        public HeardNoise(Vector3 position, bool muffled)
        {
            Position = position;
            Muffled = muffled;
        }
    }

    public interface IHunterSenses
    {
        Vector3 Position { get; }

        /// Noises heard since the brain's last tick (already filtered by range and door damping).
        IReadOnlyList<HeardNoise> HeardNoises { get; }

        /// Camera-network sightings since the last tick (empty once the cameras go offline).
        IReadOnlyList<Vector3> CameraSightings { get; }

        /// True if the 3 m proximity sense touches the player; then `playerPosition` is where.
        bool SenseContact(out Vector3 playerPosition);

        bool CamerasOnline { get; }

        /// The Director's alarm, 0-100 (0 if there's no Director).
        float Alarm { get; }

        /// Boxes covering closed doors and crates right now (refreshed before each plan).
        IReadOnlyList<Bounds> DynamicObstacles { get; }
    }
}
