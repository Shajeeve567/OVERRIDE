// PerformanceOverlay.cs
// An in-game readout of the numbers the optimization evidence needs, so before/after screenshots can be
// taken without the Profiler window (which itself costs time). Uses ProfilerRecorder, which reads the
// same counters the Profiler shows.
//
//   `  (backquote)   show / hide
//   -                fixed timestep 0.02 s (50 Hz, Unity's default)
//   =                fixed timestep 0.0167 s (60 Hz)
//
// Counters a given Unity version doesn't provide are shown as "n/a" instead of failing.

using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Override.Core
{
    public class PerformanceOverlay : MonoBehaviour
    {
        [SerializeField] bool visibleAtStart;

        ProfilerRecorder mainThread, physics, dynamicBodies, kinematicBodies, contacts, gcAlloc, hunterTick;
        bool visible;
        float smoothedFps;
        string text = "";
        float nextRefresh;

        void OnEnable()
        {
            visible = visibleAtStart;
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            physics = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate", 15);
            dynamicBodies = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Active Dynamic Bodies");
            kinematicBodies = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Active Kinematic Bodies");
            contacts = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Contacts");
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            hunterTick = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Hunter.Tick", 15);
        }

        void OnDisable()
        {
            mainThread.Dispose(); physics.Dispose(); dynamicBodies.Dispose(); kinematicBodies.Dispose();
            contacts.Dispose(); gcAlloc.Dispose(); hunterTick.Dispose();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.backquoteKey.wasPressedThisFrame) visible = !visible;
            if (kb.minusKey.wasPressedThisFrame) Time.fixedDeltaTime = 0.02f;
            if (kb.equalsKey.wasPressedThisFrame) Time.fixedDeltaTime = 1f / 60f;

            float fps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            smoothedFps = Mathf.Lerp(smoothedFps, fps, 0.1f);

            // Building the text allocates, so it's rebuilt only twice a second: the GC figure on the other
            // frames shows the game's own allocations (use the Profiler for a per-script breakdown).
            if (visible && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.5f;
                text = BuildText();
            }
        }

        /// Average over the recorder's samples (nanoseconds -> milliseconds), or -1 if unavailable.
        static double AverageMs(ProfilerRecorder r)
        {
            if (!r.Valid || r.Count == 0) return -1;
            double sum = 0;
            for (int i = 0; i < r.Count; i++) sum += r.GetSample(i).Value;
            return sum / r.Count * 1e-6;
        }

        static string Ms(ProfilerRecorder r)
        {
            double v = AverageMs(r);
            return v < 0 ? "n/a" : v.ToString("0.00") + " ms";
        }

        static string Count(ProfilerRecorder r) => r.Valid ? r.LastValue.ToString() : "n/a";

        string BuildText() =>
                $"PERFORMANCE   ` hide   - 50 Hz   = 60 Hz\n" +
                $"FPS {smoothedFps:0}    main thread {Ms(mainThread)}\n" +
                $"physics step {Ms(physics)}    fixed dt {Time.fixedDeltaTime * 1000f:0.0} ms ({1f / Time.fixedDeltaTime:0} Hz)\n" +
                $"active bodies: dynamic {Count(dynamicBodies)}  kinematic {Count(kinematicBodies)}  contacts {Count(contacts)}\n" +
                $"GC alloc this frame {(gcAlloc.Valid ? gcAlloc.LastValue + " B" : "n/a")}\n" +
                $"Hunter.Tick {Ms(hunterTick)}";

        void OnGUI()
        {
            // Bottom left, above the HUD's health panel (the ammo panel owns the bottom right).
            if (visible) GUI.Box(new Rect(24, Screen.height - 250, 410, 120), text);
        }
    }
}
