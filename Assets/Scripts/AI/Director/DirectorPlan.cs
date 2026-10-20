// DirectorPlan.cs
// One candidate answer: how many drones to launch from each bay, and which doors to lock.
// This is the STATE that local search moves around in.
//
// WHY ARRAYS: local search makes thousands of small edits and copies per decision. An int[] and a
// bool[] copy in nanoseconds, and "one small change" is simply "change one entry", which is
// exactly what a neighbour move needs. Index i in DronesPerBay = Bays[i] in the snapshot.
//
// THE SIZE OF THE SPACE: 10 bays x {0,1,2} drones = 3^10 = 59,049 spawn plans, times 2^doors lock
// combinations. Too many to score every 3-5 seconds, but well suited to local search.

using System;
using System.Text;

namespace Override.DirectorAI
{
    public sealed class DirectorPlan
    {
        /// At most this many drones from one bay per decision (a bay has one hatch).
        public const int MaxPerBay = 2;

        public readonly int[] DronesPerBay;
        public readonly bool[] LockDoor;

        public DirectorPlan(int bayCount, int doorCount)
        {
            DronesPerBay = new int[bayCount];
            LockDoor = new bool[doorCount];
        }

        public DirectorPlan Clone()
        {
            var copy = new DirectorPlan(DronesPerBay.Length, LockDoor.Length);
            Array.Copy(DronesPerBay, copy.DronesPerBay, DronesPerBay.Length);
            Array.Copy(LockDoor, copy.LockDoor, LockDoor.Length);
            return copy;
        }

        /// Overwrites this plan with another of the same size (reuses memory: no garbage per step).
        public void CopyFrom(DirectorPlan other)
        {
            Array.Copy(other.DronesPerBay, DronesPerBay, DronesPerBay.Length);
            Array.Copy(other.LockDoor, LockDoor, LockDoor.Length);
        }

        public int TotalDrones
        {
            get { int n = 0; foreach (int d in DronesPerBay) n += d; return n; }
        }

        public int LockCount
        {
            get { int n = 0; foreach (bool l in LockDoor) if (l) n++; return n; }
        }

        /// How many doors differ from another plan's locks (0 if the other plan doesn't match in size).
        public int LockChanges(DirectorPlan other)
        {
            if (other == null || other.LockDoor.Length != LockDoor.Length) return 0;
            int n = 0;
            for (int i = 0; i < LockDoor.Length; i++) if (LockDoor[i] != other.LockDoor[i]) n++;
            return n;
        }

        /// e.g. "bays[0x1 6x2] locks[3]": readable in the debug panel and in test output.
        public override string ToString()
        {
            var sb = new StringBuilder("bays[");
            bool first = true;
            for (int i = 0; i < DronesPerBay.Length; i++)
                if (DronesPerBay[i] > 0) { sb.Append(first ? "" : " ").Append(i).Append('x').Append(DronesPerBay[i]); first = false; }
            sb.Append("] locks[");
            first = true;
            for (int i = 0; i < LockDoor.Length; i++)
                if (LockDoor[i]) { sb.Append(first ? "" : " ").Append(i); first = false; }
            return sb.Append(']').ToString();
        }
    }
}
