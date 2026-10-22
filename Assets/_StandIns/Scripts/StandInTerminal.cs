// STAND-IN (delete with the _StandIns folder) -> replaced by Gunitha's HackTerminal
// (feature/interactions), which raises GameEvents.HackStarted / TerminalOverridden.
//
// GDD hacking rules:
//   - Hold E within 2 m of the terminal for about 8 s (the Core console: 30 s).
//   - Taking damage PAUSES progress but doesn't reset it.
//   - The first press tells the Director a hack has started (defence wave / final wave).
//   - Finishing tells the Director the zone's subsystem is gone.
//   - The Core console only works after the other three terminals are done.
// Added to each terminal placeholder at Play time by StandInGame.

using Override.Director;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Override.StandIns
{
    public sealed class StandInTerminal : MonoBehaviour
    {
        public string zone;           // "Labs", "Maintenance", "ServerHall" or "Core"
        public string label;          // "Terminal 1"...
        public float holdSeconds = 8f;
        public float range = 2.5f;
        public bool needsOtherTerminals;   // the Core console

        public bool Done { get; private set; }
        public float Progress01 => Mathf.Clamp01(progress / holdSeconds);
        public bool PlayerInRange { get; private set; }
        public bool Hacking { get; private set; }
        public bool Paused { get; private set; }
        public bool Locked => needsOtherTerminals && !game.OtherTerminalsDone;

        public StandInGame game;
        float progress;
        bool started;
        Transform player;
        StandInPlayerHealth health;
        DirectorController director;
        Renderer screen;

        void Start()
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) { player = p.transform; health = p.GetComponent<StandInPlayerHealth>(); }
            director = FindFirstObjectByType<DirectorController>();
            var s = transform.Find("Screen");
            if (s != null) screen = s.GetComponent<Renderer>();
        }

        void Update()
        {
            if (Done || player == null) return;
            Vector3 d = player.position - transform.position; d.y = 0f;
            PlayerInRange = d.magnitude <= range;

            var kb = Keyboard.current;
            Hacking = PlayerInRange && !Locked && kb != null && kb.eKey.isPressed;
            Paused = Hacking && health != null && Time.time - health.LastDamageTime < 0.75f;
            if (!Hacking) return;

            if (!started)
            {
                started = true;
                if (director != null) director.ReportHackStarted(zone);
                game.Show($"{label}: hack started. ARGUS knows you're here.");
            }
            if (!Paused) progress += Time.deltaTime;

            if (progress >= holdSeconds)
            {
                Done = true;
                Hacking = false;
                if (screen != null)
                {
                    // .material (not .sharedMaterial) makes a private copy for THIS terminal only,
                    // so the other terminals stay red.
                    var teal = new Color(0.2f, 0.9f, 0.8f);
                    screen.material.SetColor("_BaseColor", teal);
                    screen.material.SetColor("_EmissionColor", teal * 2f);
                }
                if (director != null) director.ReportTerminalOverridden(zone);
                game.OnTerminalDone(this);
            }
        }

        public string Prompt()
        {
            if (Done || !PlayerInRange) return null;
            if (Locked) return $"{label} is sealed: override Terminals 1-3 first";
            if (!Hacking) return $"Hold E to override {label}" + (progress > 0f ? $" ({Progress01:P0})" : "");
            return Paused ? $"{label}: taking fire, hack paused" : $"Overriding {label}... {Progress01:P0}";
        }
    }
}
