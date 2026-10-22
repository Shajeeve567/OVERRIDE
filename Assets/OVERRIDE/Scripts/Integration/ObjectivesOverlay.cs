// ObjectivesOverlay.cs
// Temporary game HUD for integration testing (replaces Shajeeve's stand-in HUD; the real HUD and menus
// come in feature/game-flow): the terminal checklist, Director announcements, final-wave and win/death
// messages. Put it on any object in the scene.

using Override.Core;
using Override.Director;
using Override.Interaction;
using UnityEngine;

namespace Override.Integration
{
    public class ObjectivesOverlay : MonoBehaviour
    {
        [SerializeField] float messageSeconds = 5f;

        HackTerminal[] terminals;
        DirectorController director;
        string message = "Find the terminals and hold E to override them.";
        float messageUntil;
        bool won;

        void Start()
        {
            terminals = FindObjectsByType<HackTerminal>(FindObjectsSortMode.None);
            System.Array.Sort(terminals, (a, b) => a.Zone.CompareTo(b.Zone));
            director = FindFirstObjectByType<DirectorController>();
            if (director != null) director.Announced += Show;
            messageUntil = Time.time + messageSeconds;
        }

        void OnEnable()
        {
            GameEvents.TerminalOverridden += OnTerminalOverridden;
        }

        void OnDisable()
        {
            GameEvents.TerminalOverridden -= OnTerminalOverridden;
            if (director != null) director.Announced -= Show;
        }

        void OnTerminalOverridden(ZoneId zone)
        {
            if (zone == ZoneId.Core) { won = true; return; }
            Show(HackTerminal.TerminalsDone >= 3
                ? "All three terminals overridden: the Core is open. Hold E at the Core console for 30 s."
                : $"{zone} terminal overridden ({HackTerminal.TerminalsDone}/3).");
        }

        void Show(string text)
        {
            message = text;
            messageUntil = Time.time + messageSeconds;
        }

        void OnGUI()
        {
            float w = Screen.width;

            GUILayout.BeginArea(new Rect(w - 290, 10, 280, 150), GUI.skin.box);
            GUILayout.Label("Objectives");
            if (terminals != null)
                foreach (var t in terminals)
                {
                    string box = t.IsOverridden ? "[x]" : t.IsLockedByPrerequisites ? "[locked]" : "[ ]";
                    GUILayout.Label($"{box} {t.Zone} terminal {(t.Progress > 0f && !t.IsOverridden ? $"{t.Progress:P0}" : "")}");
                }
            if (director != null && director.FinalWave) GUILayout.Label(">> FINAL WAVE <<");
            GUILayout.EndArea();

            // Death and victory have their own screens (GameFlow); here only the passing messages.
            string top = won ? null : Time.time < messageUntil ? message : null;
            if (top != null) GUI.Box(new Rect(w / 2 - 280, 40, 560, 30), top);
        }
    }
}
