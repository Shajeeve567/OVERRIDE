// NoiseDebugView.cs
// Draws every noise on NoiseBus as a coloured wire sphere for a moment, so you can check radii by eye.
// Put it on any object in a test scene. Visible in the Scene view, and in the Game view with Gizmos on.

using UnityEngine;

namespace Override.Core
{
    public class NoiseDebugView : MonoBehaviour
    {
        [SerializeField] float showSeconds = 0.6f;

        // Fixed-size ring buffer: no allocation per noise.
        readonly NoiseEvent[] recent = new NoiseEvent[32];
        int next;

        void OnEnable() => NoiseBus.Emitted += Record;
        void OnDisable() => NoiseBus.Emitted -= Record;

        void Record(NoiseEvent e)
        {
            recent[next] = e;
            next = (next + 1) % recent.Length;
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;
            foreach (var e in recent)
            {
                if (e.Radius <= 0f || Time.time - e.Time > showSeconds) continue;
                Gizmos.color = ColourFor(e.Kind);
                Gizmos.DrawWireSphere(e.Position, e.Radius);
            }
        }

        static Color ColourFor(NoiseKind kind) => kind switch
        {
            NoiseKind.Footstep => new Color(1f, 0.6f, 0f),
            NoiseKind.Door => Color.cyan,
            NoiseKind.Crate => Color.yellow,
            NoiseKind.Canister => Color.magenta,
            NoiseKind.Gunshot => Color.red,
            NoiseKind.TerminalBeep => Color.green,
            _ => Color.white,
        };
    }
}
