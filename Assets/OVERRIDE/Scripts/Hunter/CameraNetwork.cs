// CameraNetwork.cs
// ARGUS's security cameras report sightings here; the Seeker (and anyone else) listens.
// The Labs terminal takes the network offline (GDD Section 7: "Hunter becomes sound-only").

using System;
using Override.Core;
using UnityEngine;

namespace Override.Hunter
{
    public static class CameraNetwork
    {
        public static event Action<Vector3> Sighted;
        public static bool Online { get; private set; } = true;

        public static void Report(Vector3 playerPosition)
        {
            if (Online) Sighted?.Invoke(playerPosition);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            Sighted = null;
            Online = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ListenForLabs() => GameEvents.TerminalOverridden += zone =>
        {
            if (zone == ZoneId.Labs) Online = false;
        };
    }
}
