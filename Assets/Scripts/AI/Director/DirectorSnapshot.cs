// DirectorSnapshot.cs
// Everything the Director is allowed to know when it makes one decision, as plain data.
//
// WHY A SNAPSHOT: the brain never looks at the scene itself. The controller (a MonoBehaviour) fills
// one of these from the game, then hands it over. That gives three things:
//   1. Tests can build fake snapshots in one line: no scene, no Play mode, no teammates' code.
//   2. A decision is reproducible: same snapshot + same random seed = same plan.
//   3. The brain can't accidentally depend on something it shouldn't (e.g. reading enemy HP).
//
// Zones are strings ("Labs", "Core"...) rather than the team's ZoneId enum because this assembly
// can't see GameEvents.cs yet (it lives in Assembly-CSharp). The controller converts.

using UnityEngine;

namespace Override.DirectorAI
{
    /// One drone bay, as the brain sees it.
    public struct BayInfo
    {
        public int Id;            // 0-9, the bay table number
        public string Zone;
        public Vector3 Position;  // the bay's spawn point
        public bool Blocked;      // a crate is in front of the hatch: drones can't get out
    }

    /// One door ARGUS could lock.
    public struct DoorInfo
    {
        public int Id;
        public Vector3 Position;
        public bool PlayerZoneExit;   // one of the ways out of the zone the player is in right now
    }

    public sealed class DirectorSnapshot
    {
        public float Time;

        // ---- The player ----
        public Vector3 PlayerPosition;
        public string PlayerZone;

        // ---- The three tension inputs from the GDD ----
        /// 0..1: share of max health lost recently. The controller makes it fade over a few seconds,
        /// so one old hit doesn't keep the Director backing off forever.
        public float RecentDamage;
        /// Seconds since the player last shot at or was shot by an ARGUS unit.
        public float SecondsSinceEngagement;
        /// 0..100: how aware ARGUS is of the intruder (raised by cameras, noise, hacking).
        public float Alarm;

        // ---- Resources and rules ----
        /// Drones alive right now.
        public int ActiveDrones;
        /// Most drones allowed alive at once. GDD: up to 4 Wisps; Terminal 3 halves it.
        public int DroneBudget = 4;
        /// False after Terminal 2: ARGUS can no longer lock doors.
        public bool DoorControlOnline = true;
        /// The Core override is running: send everything.
        public bool FinalWave;

        // ---- What the plan can use ----
        public BayInfo[] Bays = new BayInfo[0];
        public DoorInfo[] Doors = new DoorInfo[0];
    }
}
