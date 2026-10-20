// IDirectorDrone.cs
// What the Director tells a drone it has just launched. PERMANENT: the stand-in drone implements it
// now; the Agent Controller's real Wisp can implement it too (or ignore it: a prefab without it is
// simply spawned at the bay and left to its own AI).

using UnityEngine;

namespace Override.Director
{
    public interface IDirectorDrone
    {
        /// waypoint: the bay's waypoint node, where the drone joins the level's graph.
        /// player: who to go after.
        void Launch(Vector3 waypoint, Transform player);
    }
}
