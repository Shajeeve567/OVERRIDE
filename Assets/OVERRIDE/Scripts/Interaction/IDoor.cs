// IDoor.cs
// What every door type offers to other systems: the Director (door locks) and agents' paths
// (a closed door blocks) don't need to know whether it swings or slides.

using UnityEngine;

namespace Override.Interaction
{
    public interface IDoor
    {
        /// True once it's open far enough for an agent to pass.
        bool IsOpen { get; }

        /// Open or close from code. opener = who caused it (null for the Director); doors may use it
        /// to decide which way to swing.
        void SetOpen(bool open, Transform opener = null);
    }
}
