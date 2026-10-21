// HunterSettings.cs
// Every number the Hunter's algorithm uses, in one place, so each can be tuned and justified.
// GDD = from the Game Design Document; the rest are starting values (see the design log for changes).

using System;

namespace Override.HunterAI
{
    [Serializable]
    public sealed class HunterSettings
    {
        // ---- Timing
        /// Seconds between decisions (GDD: every 0.25 s, plus immediately on each heard noise).
        public float DecisionInterval = 0.25f;
        /// Replan at least this often even if the goal hasn't changed.
        public float ReplanInterval = 1f;

        // ---- Predict: the player may have moved since the last tick
        /// Fraction of each cell's probability that spreads to its walkable neighbours per 0.25 s tick.
        /// A sprinting player covers 7 m/s × 0.25 s = 1.75 m per tick, less than one 2 m cell.
        public float Diffusion = 0.3f;

        // ---- Update: Bayes rule, L(c) = ε + (1 − ε)·exp(−d² / 2σ²)
        public float NoiseSigma = 3f;            // noise heard directly: rough direction and distance
        public float MuffledNoiseSigma = 6f;     // through a closed door: harder to place
        public float NoiseFloor = 0.02f;         // ε: keeps every cell possible after a misleading noise
        public float CameraSigma = 1f;           // cameras see the player directly
        public float CameraFloor = 0.001f;
        public float ContactSigma = 0.5f;        // proximity contact: near-certain
        public float ContactFloor = 1e-6f;

        // ---- Negative evidence: searching an empty area drains it
        public float ProximityRadius = 3f;       // GDD: 3 m proximity sense
        public float DetectionProbability = 0.9f; // p_d: chance the sense notices a player who is there

        // ---- Hearing (GDD)
        /// Hears a noise if distance ≤ HearingSensitivity × radius (halved through a closed door).
        public float HearingSensitivity = 1.5f;

        // ---- Goal choice: score(c) = M(c) · exp(−λ · distance)
        public float ClusterRadius = 4f;         // metres summed around each cell for M(c)
        public float DistanceDiscount = 0.05f;   // λ per metre
        /// At alarm 100 the effective λ is multiplied by (1 − this): a high alarm makes it search further afield.
        public float AlarmReach = 0.5f;
        public int GoalCandidates = 3;           // try this many best cells if A* fails on the first

        // ---- Intents
        public float InvestigateWindow = 3f;     // seconds after a noise it counts as investigating
        public float SearchThreshold = 0.25f;    // M(c) at least this = a confident search
        public float AttackRange = 2.5f;         // GDD lunge range
        public float AttackDamage = 25f;         // GDD
        public float AttackCooldown = 1.2f;      // GDD
        public float SearchSpeed = 3.5f;         // GDD m/s
        public float PursueSpeed = 6f;           // GDD m/s

        // ---- Final phase recall (GDD Section 7)
        /// The recall is treated as one more piece of evidence ("the intruder will come to the Core"),
        /// applied every tick: wide and with a high floor, so it pulls the Seeker there but real noises still win.
        public float RecallSigma = 10f;
        public float RecallFloor = 0.15f;

        // ---- Recovery
        public float StuckSeconds = 2f;          // no progress this long = stuck
        public float StuckBlockSeconds = 3f;     // the next path cell is avoided this long
    }
}
