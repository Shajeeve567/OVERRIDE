// DebugOverlay.cs
// The team-wide F1 debug switch (GDD Section 10): every agent's debug view checks DebugOverlay.Visible.
// The player's PlayerInputReader toggles it when F1 is pressed.

using System;
using UnityEngine;

namespace Override.Core
{
    public static class DebugOverlay
    {
        public static bool Visible { get; private set; }
        public static event Action<bool> Changed;

        public static void Toggle()
        {
            Visible = !Visible;
            Changed?.Invoke(Visible);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            Visible = false;
            Changed = null;
        }
    }
}
