// DirectorController.cs
// The Director's "eyes and hands" in the scene. DirectorBrain (plain C#) does the thinking; this
// MonoBehaviour does everything that touches Unity:
//
//   EVERY FRAME  : keeps the three tension inputs up to date (damage fades, time since the last fight
//                  grows, the alarm cools down).
//   EVERY 3-5 s  : fills a DirectorSnapshot from the game -> brain.Decide() -> carries out the plan
//                  (launches drones from the chosen bays, locks/unlocks doors).
//                  During the final wave it decides every 1.5 s, so killed drones are replaced fast.
//   ON EVENTS    : ReportPlayerDamaged / ReportEngagement / ReportHackStarted / ReportTerminalOverridden.
//                  Public, so whatever owns those events can call them. Today that's the stand-in
//                  game in Assets/_StandIns; after the merge it's the team's GameEvents:
//                    GameEvents.PlayerDamaged      += ReportPlayerDamaged;
//                    GameEvents.HackStarted        += zone => ReportHackStarted(zone.ToString());
//                    GameEvents.TerminalOverridden += zone => ReportTerminalOverridden(zone.ToString());
//
// WHY A RANDOM 3-5 s INTERVAL: a fixed rhythm is something players notice and learn to exploit
// ("a wave every 4 seconds"). A little randomness hides the machinery.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Override.DirectorAI;
using Override.Level;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Override.Director
{
    public sealed class DirectorController : MonoBehaviour
    {
        [Header("Brain")]
        public DirectorSettings settings = new DirectorSettings();
        [Tooltip("Same seed + same play = same decisions. Handy for recording the demo.")]
        public int seed = 2026;
        public float minDecisionInterval = 3f;
        public float maxDecisionInterval = 5f;
        [Tooltip("During the final wave the Director re-plans this often, so killed drones are replaced quickly.")]
        public float finalWaveInterval = 1.5f;

        [Header("Drones")]
        [Tooltip("What to launch. 'OVERRIDE > Stand-ins > Set Up Test Game' puts the stand-in drone here; " +
                 "swap in the real Wisp prefab when it exists.")]
        public GameObject dronePrefab;
        [Tooltip("Most drones alive at once in normal play (GDD: up to 4). Terminal 3 halves it.")]
        public int droneBudget = 4;
        [Tooltip("Most drones alive at once during the Core's final wave. Terminal 3 halves this too.")]
        public int finalWaveBudget = 6;
        [Tooltip("Seconds between drones launched from the same bay, so they don't spawn inside each other.")]
        public float launchStagger = 0.6f;

        [Header("Tension inputs")]
        public float playerMaxHealth = 100f;
        [Tooltip("Recent damage fades with this time constant (seconds).")]
        public float damageFadeSeconds = 6f;
        [Tooltip("Alarm points lost per second when nothing is happening.")]
        public float alarmCoolPerSecond = 0.5f;
        public float alarmOnHackStarted = 40f;
        public float alarmPerDamage = 0.5f;   // alarm points per HP lost

        [Header("Debug")]
        public bool showPanel = true;

        // ---- Read-only state for HUDs and other systems ----
        public bool FinalWave => finalWave;
        public bool Defeated => defeated;
        public bool CamerasOnline => camerasOnline;
        public bool DoorControlOnline => doorControlOnline;
        public int DroneBudgetNow => finalWave ? finalWaveBudgetNow : budgetNow;
        public int ActiveDrones => drones.Count;
        public float CurrentTension => last != null ? last.Tension : 0f;
        /// Short messages for an on-screen HUD ("Final wave incoming", "Doors locked"...).
        public event Action<string> Announced;

        // ---- Live state ----
        float recentDamage;              // 0..1
        float lastEngagementTime = -999f;
        float alarm;                     // 0..100
        bool camerasOnline = true, doorControlOnline = true, finalWave, defeated;
        int budgetNow, finalWaveBudgetNow;

        DirectorBrain brain;
        ZoneLocator zones;
        Transform player;
        DroneBay[] bays = new DroneBay[0];
        readonly List<GameObject> drones = new List<GameObject>();
        readonly List<ILockableDoor> plannedDoors = new List<ILockableDoor>();
        readonly HashSet<ILockableDoor> lockedDoors = new HashSet<ILockableDoor>();
        float nextDecision;
        DirectorDecision last;
        readonly Queue<string> history = new Queue<string>();
        bool warnedNoPrefab;

        // ==================================================================
        void Start()
        {
            brain = new DirectorBrain(settings, seed);
            zones = new ZoneLocator();
            budgetNow = droneBudget;
            finalWaveBudgetNow = finalWaveBudget;

            var p = GameObject.FindWithTag("Player");
            if (p != null) player = p.transform;
            else Debug.LogWarning("[Director] No object tagged 'Player' in the scene.");

            // Bays are found once and sorted by id, so plan index i is always bay i.
            bays = FindObjectsByType<DroneBay>(FindObjectsSortMode.None).OrderBy(b => b.bayId).ToArray();
            Debug.Log($"[Director] Found {bays.Length} drone bays. Using {(settings.UseAnnealing ? "simulated annealing" : "hill descent")}.");

            ScheduleNext();
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Recent damage fades exponentially (multiply by e^(-dt/fade) every frame, so the fade
            // speed doesn't depend on frame rate).
            recentDamage *= Mathf.Exp(-dt / damageFadeSeconds);
            alarm = Mathf.Max(0f, alarm - alarmCoolPerSecond * dt);
            drones.RemoveAll(d => d == null);   // destroyed drones leave null entries behind

            HandleDebugKeys();

            if (!defeated && Time.time >= nextDecision && player != null)
            {
                Decide();
                ScheduleNext();
            }
        }

        void ScheduleNext() => nextDecision = Time.time + (finalWave
            ? finalWaveInterval
            : UnityEngine.Random.Range(minDecisionInterval, maxDecisionInterval));

        // ==================================================================
        // ONE DECISION
        // ==================================================================
        void Decide()
        {
            var snapshot = BuildSnapshot();
            last = brain.Decide(snapshot);
            Execute(last.Plan);

            string line = $"{Time.time,6:0.0}s  {snapshot.PlayerZone,-11} T={last.Tension:0.00} need={last.NeededPressure:0.0}  {last.Plan}  cost={last.Cost.Total:0.00}";
            history.Enqueue(line);
            while (history.Count > 8) history.Dequeue();
            if (last.Plan.TotalDrones > 0 || last.Plan.LockCount > 0) Debug.Log("[Director] " + line);
        }

        DirectorSnapshot BuildSnapshot()
        {
            string zone = zones.ZoneOf(player.position);

            // Doors are looked up every decision: they may be created after the Director starts
            // (the stand-in game builds them at Play time), and some can't be locked right now.
            plannedDoors.Clear();
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is ILockableDoor door && door.CanBeLocked) plannedDoors.Add(door);
            plannedDoors.Sort((a, b) => a.DoorId.CompareTo(b.DoorId));

            var s = new DirectorSnapshot
            {
                Time = Time.time,
                PlayerPosition = player.position,
                PlayerZone = zone,
                RecentDamage = recentDamage,
                SecondsSinceEngagement = Time.time - lastEngagementTime,
                Alarm = alarm,
                ActiveDrones = drones.Count,
                DroneBudget = DroneBudgetNow,
                DoorControlOnline = doorControlOnline,
                FinalWave = finalWave,
                Bays = new BayInfo[bays.Length],
                Doors = new DoorInfo[plannedDoors.Count],
            };
            for (int i = 0; i < bays.Length; i++)
                s.Bays[i] = new BayInfo
                {
                    Id = bays[i].bayId,
                    Zone = bays[i].zone,
                    Position = bays[i].spawnPoint.position,
                    Blocked = bays[i].IsBlocked,   // a physics check: is a crate in front of the hatch?
                };
            for (int i = 0; i < plannedDoors.Count; i++)
                s.Doors[i] = new DoorInfo
                {
                    Id = plannedDoors[i].DoorId,
                    Position = plannedDoors[i].Position,
                    PlayerZoneExit = plannedDoors[i].Connects(zone),
                };
            return s;
        }

        void Execute(DirectorPlan plan)
        {
            // Drones.
            for (int i = 0; i < plan.DronesPerBay.Length && i < bays.Length; i++)
                for (int k = 0; k < plan.DronesPerBay[i]; k++)
                    StartCoroutine(LaunchAfter(bays[i], k * launchStagger));

            // Doors. Only the ones in this decision's snapshot (the plan's indexes match plannedDoors).
            int newlyLocked = 0;
            for (int i = 0; i < plan.LockDoor.Length && i < plannedDoors.Count; i++)
            {
                var door = plannedDoors[i];
                bool wantLocked = plan.LockDoor[i] && doorControlOnline;
                if (wantLocked && lockedDoors.Add(door)) newlyLocked++;
                if (!wantLocked) lockedDoors.Remove(door);
                door.SetDirectorLock(wantLocked);
            }
            if (newlyLocked > 0) Announce($"ARGUS locked {newlyLocked} door{(newlyLocked > 1 ? "s" : "")}");
        }

        System.Collections.IEnumerator LaunchAfter(DroneBay bay, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (bay.IsBlocked || defeated) yield break;   // blocked (or game won) in the meantime
            if (dronePrefab == null)
            {
                if (!warnedNoPrefab) Debug.LogWarning("[Director] No drone prefab assigned: run OVERRIDE > Stand-ins > Set Up Test Game, or assign the Wisp.");
                warnedNoPrefab = true;
                yield break;
            }

            var drone = Instantiate(dronePrefab, bay.spawnPoint.position, bay.mouth.rotation);
            Vector3 waypoint = bay.waypoint != null ? bay.waypoint.transform.position : bay.mouth.position + bay.mouth.forward * 2.5f;
            // GetComponent works with interfaces: any component implementing IDirectorDrone answers.
            var brainOfDrone = drone.GetComponent<IDirectorDrone>();
            if (brainOfDrone != null) brainOfDrone.Launch(waypoint, player);
            drones.Add(drone);
        }

        void Announce(string message)
        {
            Debug.Log("[Director] " + message);
            Announced?.Invoke(message);
        }

        // ==================================================================
        // EVENTS (public: the stand-in game calls these now, GameEvents after the merge)
        // ==================================================================
        public void ReportPlayerDamaged(float amount)
        {
            recentDamage = Mathf.Clamp01(recentDamage + amount / playerMaxHealth);
            lastEngagementTime = Time.time;
            // With the cameras gone (Terminal 1), ARGUS notices less: alarm rises at half speed.
            alarm = Mathf.Min(100f, alarm + amount * alarmPerDamage * (camerasOnline ? 1f : 0.5f));
        }

        /// The player shot at, or hit, an ARGUS unit (no damage taken).
        public void ReportEngagement() => lastEngagementTime = Time.time;

        public void ReportHackStarted(string zone)
        {
            alarm = Mathf.Min(100f, alarm + alarmOnHackStarted * (camerasOnline ? 1f : 0.5f));
            // GDD: "the final core override is a 30-second hold while the Director sends its final wave".
            if (zone == "Core") SetFinalWave(true);
            else Announce($"Hack detected in {zone}: alarm {alarm:0}");
            nextDecision = Time.time;   // a hack is the big moment: react now, not in 5 s
        }

        /// ARGUS loses that zone's subsystem (GDD Section 7).
        public void ReportTerminalOverridden(string zone)
        {
            switch (zone)
            {
                case "Labs":
                    camerasOnline = false;
                    Announce("Terminal 1 overridden: ARGUS camera network offline");
                    break;
                case "Maintenance":
                    doorControlOnline = false;
                    foreach (var d in lockedDoors) d.SetDirectorLock(false);
                    lockedDoors.Clear();
                    Announce("Terminal 2 overridden: ARGUS door-lock control offline");
                    break;
                case "ServerHall":
                    budgetNow = Mathf.Max(1, droneBudget / 2);
                    finalWaveBudgetNow = Mathf.Max(1, finalWaveBudget / 2);
                    Announce($"Terminal 3 overridden: drone fabrication halved (max {budgetNow} drones)");
                    break;
                case "Core":
                    Defeat();
                    return;
            }
            nextDecision = Time.time;   // re-plan immediately: the GDD's "visible re-plan" hook
        }

        public void SetFinalWave(bool on)
        {
            if (finalWave == on) return;
            finalWave = on;
            if (on) Announce($"FINAL WAVE: ARGUS sends everything ({DroneBudgetNow} drones at once)");
            nextDecision = Time.time;
        }

        /// The Core override finished: ARGUS is gone. Drones drop, doors open, the Director stops.
        void Defeat()
        {
            defeated = true;
            finalWave = false;
            foreach (var d in drones) if (d != null) Destroy(d);
            foreach (var d in lockedDoors) d.SetDirectorLock(false);
            lockedDoors.Clear();
            Announce("ARGUS OFFLINE");
        }

        // ==================================================================
        // DEBUG KEYS
        //   F3  show/hide panel        F4  decide now
        //   F5  player takes 15 damage F6  hack starts here     F7  terminal here overridden
        //   F8  kill all drones        F9  final wave on/off    F10 switch SA <-> hill descent
        // ==================================================================
        void HandleDebugKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            string here = player != null ? zones.ZoneOf(player.position) : "Unknown";

            if (kb.f3Key.wasPressedThisFrame) showPanel = !showPanel;
            if (kb.f4Key.wasPressedThisFrame) nextDecision = Time.time;
            if (kb.f5Key.wasPressedThisFrame) ReportPlayerDamaged(15f);
            if (kb.f6Key.wasPressedThisFrame) ReportHackStarted(here);
            if (kb.f7Key.wasPressedThisFrame) ReportTerminalOverridden(here);
            if (kb.f8Key.wasPressedThisFrame) { foreach (var d in drones) if (d != null) Destroy(d); ReportEngagement(); }
            if (kb.f9Key.wasPressedThisFrame) SetFinalWave(!finalWave);
            if (kb.f10Key.wasPressedThisFrame)
            {
                settings.UseAnnealing = !settings.UseAnnealing;   // the brain holds the same settings object
                Announce("Now using " + (settings.UseAnnealing ? "simulated annealing" : "hill descent"));
            }
        }

        // ==================================================================
        // DEBUG PANEL (OnGUI: crude, but needs no Canvas or UI setup)
        // ==================================================================
        void OnGUI()
        {
            if (!showPanel) return;
            GUILayout.BeginArea(new Rect(10, 10, 580, 540), GUI.skin.box);
            GUILayout.Label("<b>ARGUS Director</b>   F3 hide  F4 decide  F5 dmg  F6 hack  F7 override  F8 kill  F9 final  F10 SA/HD", Rich());

            string zone = player != null ? zones.ZoneOf(player.position) : "-";
            GUILayout.Label($"Player zone: {zone}    Drones alive: {drones.Count}/{DroneBudgetNow}    Next decision in {Mathf.Max(0, nextDecision - Time.time):0.0}s");
            GUILayout.Label($"Damage {recentDamage:0.00}   Since fight {Mathf.Min(999f, Time.time - lastEngagementTime):0}s   Alarm {alarm:0}");
            GUILayout.Label($"Cameras {(camerasOnline ? "ON" : "off")}   Door control {(doorControlOnline ? "ON" : "off")}   Doors locked {lockedDoors.Count}" +
                            $"   Final wave {(finalWave ? "<b>YES</b>" : "no")}{(defeated ? "   <b>ARGUS OFFLINE</b>" : "")}", Rich());

            if (last != null)
            {
                TensionBar(last.Tension);
                var c = last.Cost;
                var r = last.Search;
                GUILayout.Label($"<b>{last.Algorithm}</b>: plan {last.Plan}", Rich());
                GUILayout.Label($"Needed pressure {last.NeededPressure:0.00}, plan gives {c.PredictedPressure:0.00}");
                GUILayout.Label($"Cost {c.Total:0.000} = band {c.Band:0.000} + budget {c.Budget:0} + blocked {c.Blocked:0} + close {c.TooClose:0.0}" +
                                $" + flank {c.Flank:0.000} + drones {c.Drones:0.0} + doors {c.Change:0.0} + trap {c.TrapPlayer:0}");
                GUILayout.Label($"Evaluations {r.Evaluations}   Steps {r.Steps}   Worse moves accepted {r.WorseAccepted}   T0 {r.StartTemperature:0.000}");
            }

            GUILayout.Label("<b>Recent decisions</b>", Rich());
            var sb = new StringBuilder();
            foreach (var h in history) sb.AppendLine(h);
            GUILayout.Label(sb.ToString());
            GUILayout.EndArea();
        }

        // A bar from 0 to 1 with the target band marked, so you can SEE tension leave and return to it.
        void TensionBar(float t)
        {
            GUILayout.Label($"Tension {t:0.00}  (band {settings.BandLow:0.00}-{settings.BandHigh:0.00})");
            Rect bar = GUILayoutUtility.GetRect(540, 14);
            GUI.DrawTexture(bar, Texture2D.grayTexture);
            var band = new Rect(bar.x + bar.width * settings.BandLow, bar.y, bar.width * (settings.BandHigh - settings.BandLow), bar.height);
            var old = GUI.color;
            GUI.color = new Color(0.2f, 0.8f, 0.7f, 0.6f); GUI.DrawTexture(band, Texture2D.whiteTexture);
            GUI.color = Color.red; GUI.DrawTexture(new Rect(bar.x + bar.width * Mathf.Clamp01(t) - 2, bar.y - 2, 4, bar.height + 4), Texture2D.whiteTexture);
            GUI.color = old;
        }

        static GUIStyle rich;
        static GUIStyle Rich()
        {
            // Made once, on first use: GUI.skin only exists while OnGUI is running.
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true };
            return rich;
        }

        // Lines from each bay used in the last plan to the player, visible in the Scene view.
        void OnDrawGizmos()
        {
            if (last == null || player == null) return;
            Gizmos.color = Color.red;
            for (int i = 0; i < last.Plan.DronesPerBay.Length && i < bays.Length; i++)
                if (last.Plan.DronesPerBay[i] > 0 && bays[i] != null)
                    Gizmos.DrawLine(bays[i].spawnPoint.position, player.position);
        }
    }
}
