// HackTerminal.cs
// An override terminal. Hold E within range for 8 s to hack it (GDD Section 3).
//   - Taking damage pauses progress for a moment but never resets it.
//   - Letting go, walking away or dying keeps the progress made so far.
//   - Every 2 s of hacking it beeps: a 20 m noise that draws the Hunter.
//   - The first touch raises HackStarted (the Director's defence-wave trigger);
//     finishing raises TerminalOverridden once.

using Override.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Override.Interaction
{
    public class HackTerminal : MonoBehaviour, IHoldInteractable
    {
        [SerializeField] ZoneId zone = ZoneId.Labs;

        [Header("Hack (GDD)")]
        [SerializeField] float hackDuration = 8f;     // seconds of holding E
        [SerializeField] float useRange = 2f;         // metres from the terminal's surface
        [SerializeField] float damagePause = 1f;      // seconds progress stops after each hit

        [Header("Beep noise (GDD)")]
        [SerializeField] float beepRadius = 20f;
        [SerializeField] float beepInterval = 2f;

        [Header("Feedback")]
        [SerializeField] ProgressRing ring;           // optional world-space progress ring
        [SerializeField] Color hackingColour = new Color(1f, 0.8f, 0.2f);
        [SerializeField] Color pausedColour = new Color(1f, 0.25f, 0.2f);
        [SerializeField] Color doneColour = new Color(0.3f, 1f, 0.5f);

        [Tooltip("Hook up zone effects here (lights turning white, ARGUS screen text).")]
        public UnityEvent onOverridden;

        Collider body;           // the terminal's model collider; range is measured to its surface
        float progress;          // 0 to 1
        float beepTimer;
        float pausedUntil;
        bool hacking;
        bool started;

        public ZoneId Zone => zone;
        public float Progress => progress;
        public bool IsHacking => hacking;
        public bool IsOverridden { get; private set; }
        public bool IsPausedByDamage => Time.time < pausedUntil;

        public string Prompt => started ? "Resume hack (hold)" : "Hack terminal (hold)";
        public bool CanInteract => !IsOverridden;

        void Awake() => body = GetComponentInChildren<Collider>();

        void OnEnable() => GameEvents.PlayerDamaged += OnPlayerDamaged;
        void OnDisable() => GameEvents.PlayerDamaged -= OnPlayerDamaged;

        void OnPlayerDamaged(float amount)
        {
            if (hacking) pausedUntil = Time.time + damagePause;
        }

        public void Interact(PlayerInteractor player)
        {
            if (IsOverridden) return;
            hacking = true;
            beepTimer = 0f;   // beep straight away: starting a hack is loud
            if (!started)
            {
                started = true;
                GameEvents.RaiseHackStarted(zone);
            }
        }

        public bool HoldTick(PlayerInteractor player, float deltaTime)
        {
            if (IsOverridden) return false;
            if (DistanceTo(player.transform.position) > useRange) return false;

            beepTimer -= deltaTime;
            if (beepTimer <= 0f)
            {
                Vector3 beepAt = body != null ? body.bounds.center : transform.position;   // from the model itself
                NoiseBus.Emit(beepAt, beepRadius, NoiseKind.TerminalBeep);
                beepTimer = beepInterval;
            }

            if (!IsPausedByDamage) progress = Mathf.Min(1f, progress + deltaTime / hackDuration);
            if (progress >= 1f)
            {
                Complete();
                return false;
            }
            return true;
        }

        public void EndHold(PlayerInteractor player) => hacking = false;

        /// Distance to the nearest point of the terminal's model, so moving the model (not just its parent)
        /// still works, and the range doesn't depend on where the pivot sits.
        float DistanceTo(Vector3 position)
        {
            Vector3 nearest = body != null ? body.ClosestPoint(position) : transform.position;
            nearest.y = position.y;   // ignore height: the player's pivot is at their feet
            return Vector3.Distance(nearest, position);
        }

        void Complete()
        {
            IsOverridden = true;
            hacking = false;
            GameEvents.RaiseTerminalOverridden(zone);
            onOverridden?.Invoke();
        }

        void Update()
        {
            if (ring == null) return;
            ring.SetVisible(progress > 0f);
            ring.SetProgress(progress);
            ring.SetColour(IsOverridden ? doneColour : IsPausedByDamage ? pausedColour : hackingColour);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            var c = GetComponentInChildren<Collider>();
            Gizmos.DrawWireSphere(c != null ? c.bounds.center : transform.position, useRange);
        }
    }
}
