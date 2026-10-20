// STAND-IN (delete with the _StandIns folder) -> its jobs move to the team's real systems:
//   player health   -> Gunitha's Health / PlayerDeath          (feature/rifle)
//   terminals       -> Gunitha's HackTerminal                  (feature/interactions)
//   doors           -> Gunitha's SlidingDoor / PhysicsDoor     (feature/interactions)
//   game flow + HUD -> whoever owns the HUD / menus
//
// Lives on the one "_STANDINS" object in the scene. When Play starts it ADDS the stand-in parts to the
// level (health on the player, a terminal script on each terminal placeholder, a door in each main
// corridor). Everything it creates exists only during Play, so the scene file stays clean, and
// removing the stand-ins is just deleting this object and the _StandIns folder.

using System.Collections.Generic;
using System.Linq;
using Override.Director;
using UnityEngine;

namespace Override.StandIns
{
    public sealed class StandInGame : MonoBehaviour
    {
        [Header("Materials (set by OVERRIDE > Stand-ins > Set Up Test Game)")]
        public Material doorMaterial;
        public Material freeMaterial;
        public Material directorLockMaterial;
        public Material sealedMaterial;

        [Header("Hacking (GDD values)")]
        public float terminalHoldSeconds = 8f;
        public float coreHoldSeconds = 30f;

        // Terminal placeholders (named by PropBuilder) -> zone, HUD label.
        static readonly (string objectName, string zone, string label)[] Terminals =
        {
            ("Terminal_T1 (placeholder)", "Labs", "Terminal 1"),
            ("Terminal_T2 (placeholder)", "Maintenance", "Terminal 2"),
            ("Terminal_T3 (placeholder)", "ServerHall", "Terminal 3"),
            ("Core_OverrideConsole (placeholder)", "Core", "Core override"),
        };

        // Which corridors get a door, the two zones each joins, and whether the level seals it at the start.
        // The vents and the Airlock-Labs corridor's vent twins stay open: they're the sneaky routes.
        static readonly (string corridor, string zoneA, string zoneB, bool sealedAtStart)[] Doors =
        {
            ("Airlock_Labs", "Airlock", "Labs", false),
            ("Labs_ServerHall", "Labs", "ServerHall", false),
            ("Labs_Maintenance", "Labs", "Maintenance", false),
            ("ServerHall_Maintenance", "ServerHall", "Maintenance", false),   // the GDD's "=" lockable door
            ("ServerHall_Core (door A)", "ServerHall", "Core", true),         // Core sealed until T1-T3
            ("Tunnel_Maintenance_Core", "Maintenance", "Core", true),
        };

        readonly List<StandInTerminal> terminals = new List<StandInTerminal>();
        readonly List<StandInDoor> doors = new List<StandInDoor>();
        StandInPlayerHealth health;
        DirectorController director;
        string message;
        float messageUntil;
        bool won;

        public bool OtherTerminalsDone => terminals.Where(t => t.zone != "Core").All(t => t.Done);

        // Awake runs before any Start, so the player has health before a drone can shoot it.
        void Awake()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) { Debug.LogError("[StandIns] No Player in the scene."); enabled = false; return; }
            health = player.GetComponent<StandInPlayerHealth>();
            if (health == null) health = player.AddComponent<StandInPlayerHealth>();
            health.Died += () => Show("You died. Respawning at your last terminal.");

            director = FindFirstObjectByType<DirectorController>();
            if (director != null) director.Announced += Show;
            else Debug.LogWarning("[StandIns] No DirectorController in the scene: nothing will attack you.");

            AddTerminals();
            AddDoors();
            Show("Stand-in test game: find the terminals and hold E to override them.");
        }

        void AddTerminals()
        {
            var props = GameObject.Find("OVERRIDE_Props");
            if (props == null) { Debug.LogWarning("[StandIns] No OVERRIDE_Props: run 'Dress Greybox Level'."); return; }
            var all = props.GetComponentsInChildren<Transform>(true);
            foreach (var entry in Terminals)
            {
                var t = all.FirstOrDefault(x => x.name == entry.objectName);
                if (t == null) { Debug.LogWarning("[StandIns] Missing " + entry.objectName); continue; }
                var term = t.gameObject.AddComponent<StandInTerminal>();
                term.zone = entry.zone;
                term.label = entry.label;
                term.game = this;
                term.holdSeconds = entry.zone == "Core" ? coreHoldSeconds : terminalHoldSeconds;
                term.needsOtherTerminals = entry.zone == "Core";
                terminals.Add(term);
            }
        }

        // One door at the middle of each listed corridor, as wide and tall as the corridor.
        // The size is read from the greybox corridor's floor and walls, so it fits any LevelSize.
        void AddDoors()
        {
            var corridors = GameObject.Find("OVERRIDE_Greybox/Corridors");
            if (corridors == null) return;
            var root = new GameObject("StandIn Doors (Play mode only)").transform;
            root.SetParent(transform, false);

            int id = 0;
            foreach (var entry in Doors)
            {
                string name = entry.corridor, zoneA = entry.zoneA, zoneB = entry.zoneB;
                bool sealedAtStart = entry.sealedAtStart;
                var c = corridors.transform.Find(name);
                var floor = c != null ? c.Find("Floor") : null;
                var floorCol = floor != null ? floor.GetComponent<Collider>() : null;
                if (floorCol == null) { Debug.LogWarning("[StandIns] No corridor " + name); continue; }

                Bounds f = floorCol.bounds;
                float height = 0f;
                foreach (var col in c.GetComponentsInChildren<Collider>())
                    if (col.transform.name.StartsWith("Wall")) height = Mathf.Max(height, col.bounds.max.y);
                bool alongZ = f.size.z > f.size.x;          // which way the corridor runs
                float width = alongZ ? f.size.x : f.size.z;  // across the corridor

                var door = new GameObject($"Door_{id} {zoneA}-{zoneB}").AddComponent<StandInDoor>();
                door.transform.SetParent(root, false);
                door.transform.position = new Vector3(f.center.x, f.max.y, f.center.z);
                // LookRotation(along the corridor): the slab's thin side (local Z) lies along the corridor.
                door.transform.rotation = Quaternion.LookRotation(alongZ ? Vector3.forward : Vector3.right);

                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "Slab";
                slab.transform.SetParent(door.transform, false);
                slab.transform.localPosition = new Vector3(0f, height / 2f, 0f);
                slab.transform.localScale = new Vector3(width, height, 0.25f);
                slab.GetComponent<Renderer>().sharedMaterial = doorMaterial;
                // A kinematic body: moved by script, still solid to the player, and physics knows it moves.
                slab.AddComponent<Rigidbody>().isKinematic = true;

                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Strip";
                strip.transform.SetParent(slab.transform, false);
                strip.transform.localPosition = new Vector3(0f, 0.25f, 0f);     // local to the slab (scaled)
                strip.transform.localScale = new Vector3(0.9f, 0.05f, 1.2f);    // sticks out both faces
                Destroy(strip.GetComponent<Collider>());

                door.id = id++;
                door.zoneA = zoneA;
                door.zoneB = zoneB;
                door.gameLocked = sealedAtStart;
                door.slab = slab.transform;
                door.strip = strip.GetComponent<Renderer>();
                door.height = height;
                door.freeMaterial = freeMaterial;
                door.directorMaterial = directorLockMaterial;
                door.sealedMaterial = sealedMaterial;
                doors.Add(door);
            }
        }

        // ==================================================================
        public void OnTerminalDone(StandInTerminal t)
        {
            // Respawn here from now on, a couple of metres in front of the terminal.
            health.RespawnPoint = t.transform.position + t.transform.forward * 2f + Vector3.up;

            if (t.zone == "Core")
            {
                won = true;
                Show("ARGUS OFFLINE. You win!");
                return;
            }
            if (OtherTerminalsDone)
            {
                foreach (var d in doors) d.gameLocked = false;
                Show("All three terminals overridden: the Core is open. Hold E at the Core console for 30 s.");
            }
        }

        public void Show(string text)
        {
            message = text;
            messageUntil = Time.time + 5f;
        }

        // ==================================================================
        // HUD (OnGUI: placeholder until the real HUD exists)
        // ==================================================================
        void OnGUI()
        {
            float w = Screen.width, h = Screen.height;

            // Health bar, bottom left.
            if (health != null)
            {
                GUI.Box(new Rect(20, h - 70, 260, 50), "");
                GUI.Label(new Rect(30, h - 66, 240, 20), $"Health {Mathf.CeilToInt(health.Health)} / {health.maxHealth:0}");
                GUI.DrawTexture(new Rect(30, h - 44, 240, 14), Texture2D.grayTexture);
                var old = GUI.color;
                GUI.color = Color.Lerp(Color.red, new Color(0.2f, 0.9f, 0.8f), health.Health / health.maxHealth);
                GUI.DrawTexture(new Rect(30, h - 44, 240 * Mathf.Clamp01(health.Health / health.maxHealth), 14), Texture2D.whiteTexture);
                GUI.color = old;
            }

            // Objectives, top right.
            GUILayout.BeginArea(new Rect(w - 290, 10, 280, 150), GUI.skin.box);
            GUILayout.Label("Objectives");
            foreach (var t in terminals)
                GUILayout.Label($"{(t.Done ? "[x]" : t.Locked ? "[locked]" : "[ ]")} {t.label} ({t.zone})");
            if (director != null && director.FinalWave) GUILayout.Label(">> FINAL WAVE <<");
            GUILayout.EndArea();

            // Hack prompt and progress, centre bottom.
            var near = terminals.FirstOrDefault(t => t.Prompt() != null);
            if (near != null)
            {
                var r = new Rect(w / 2 - 200, h - 140, 400, 60);
                GUI.Box(r, near.Prompt());
                GUI.DrawTexture(new Rect(r.x + 20, r.y + 32, 360, 12), Texture2D.grayTexture);
                var old = GUI.color;
                GUI.color = near.Paused ? Color.red : new Color(0.2f, 0.9f, 0.8f);
                GUI.DrawTexture(new Rect(r.x + 20, r.y + 32, 360 * near.Progress01, 12), Texture2D.whiteTexture);
                GUI.color = old;
            }

            // Messages, centre top.
            if (won || Time.time < messageUntil)
                GUI.Box(new Rect(w / 2 - 260, 40, 520, 30), won ? "ARGUS OFFLINE. You win!" : message);
        }
    }
}
