// GameEvents.cs
// Game-wide events that several systems care about, so they don't need references to each other.
// For example: the terminal raises HackStarted, and the Director (Shajeeve) can listen for it to send
// a defence wave without the terminal knowing the Director exists.

using System;
using UnityEngine;

namespace Override.Core
{
    /// The facility's zones. Terminals use these ids; the Director and lighting switch can too.
    public enum ZoneId { Airlock, Labs, ServerHall, Maintenance, Core }

    public static class GameEvents
    {
        /// The player lost health. Argument: damage amount. Terminals pause their hack on this.
        public static event Action<float> PlayerDamaged;

        /// The player started hacking a zone's terminal for the first time (defence-wave trigger).
        public static event Action<ZoneId> HackStarted;

        /// A terminal finished; ARGUS loses that zone's subsystem (GDD Section 7).
        public static event Action<ZoneId> TerminalOverridden;

        public static void RaisePlayerDamaged(float amount) => PlayerDamaged?.Invoke(amount);
        public static void RaiseHackStarted(ZoneId zone) => HackStarted?.Invoke(zone);
        public static void RaiseTerminalOverridden(ZoneId zone) => TerminalOverridden?.Invoke(zone);

        // Clear old listeners each time Play starts (matters when Domain Reload is off).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            PlayerDamaged = null;
            HackStarted = null;
            TerminalOverridden = null;
        }
    }
}
