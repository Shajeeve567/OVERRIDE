// ILockableDoor.cs
// What the Director needs from a door it can lock. PERMANENT (not a stand-in): any door type can
// implement it: the test doors now, Gunitha's SlidingDoor / PhysicsDoor after the merge (they already
// implement IDoor for opening and closing; this adds the "ARGUS lock" on top).
//
// WHY ZONES BY NAME: the Director must never lock every exit of the zone the player is in (fairness
// rule in DirectorCost). To know which doors are "exits of the player's zone", each door says which
// two zones it joins.

using UnityEngine;

namespace Override.Level
{
    public interface ILockableDoor
    {
        int DoorId { get; }
        Vector3 Position { get; }

        /// True if this door is one of the ways in or out of `zone` ("Labs", "Core"...).
        bool Connects(string zone);

        /// False while something ELSE holds the door shut (e.g. the Core stays sealed until all three
        /// terminals are done). The Director leaves such doors out of its plans.
        bool CanBeLocked { get; }

        /// The Director's lock. Locked doors stay shut even when the player walks up to them.
        void SetDirectorLock(bool locked);
    }
}
