// NoiseBus.cs
// One global channel for sounds. Emitters call Emit; listeners (agent hearing) subscribe to Emitted.
// A static event keeps emitters and listeners from needing references to each other.

using System;
using UnityEngine;

namespace Override.Core
{
    public static class NoiseBus
    {
        public static event Action<NoiseEvent> Emitted;

        public static void Emit(NoiseEvent e) => Emitted?.Invoke(e);

        /// Shortcut for the common case: a noise here, now.
        public static void Emit(Vector3 position, float radius, NoiseKind kind) =>
            Emit(new NoiseEvent(position, radius, kind, Time.time));

        // Static events survive between Play sessions when Domain Reload is off,
        // so clear old listeners each time Play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Emitted = null;
    }
}
