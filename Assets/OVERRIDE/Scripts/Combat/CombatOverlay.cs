// CombatOverlay.cs
// Temporary combat feedback drawn with IMGUI until the real HUD (feature/game-flow) replaces it:
// crosshair, hit marker (white), kill marker (red, bigger), red damage flash,
// health and ammo text, and the "spotted" warning (red screen edges) while a security camera sees you.
// Put it on the Player.

using Override.Core;
using Override.Hunter;
using UnityEngine;

namespace Override.Combat
{
    public class CombatOverlay : MonoBehaviour
    {
        [SerializeField] HitscanRifle rifle;
        [SerializeField] Health playerHealth;

        [SerializeField] float markerTime = 0.15f;
        [SerializeField] float killMarkerTime = 0.4f;
        [SerializeField] float damageFlashTime = 0.25f;
        [SerializeField] float spottedTime = 0.8f;   // cameras report twice a second, so this stays lit while seen

        float markerUntil;
        bool markerWasKill;
        bool markerWasWeak;
        float damageFlashUntil;
        float spottedUntil;
        string ammoText = "";
        string healthText = "";

        void OnEnable()
        {
            if (rifle != null)
            {
                rifle.Fired += OnFired;
                rifle.AmmoChanged += RefreshAmmo;
                rifle.ReloadStarted += RefreshAmmo;
                rifle.ReloadCancelled += RefreshAmmo;
            }
            GameEvents.PlayerDamaged += OnPlayerDamaged;
            CameraNetwork.Sighted += OnSpotted;
            if (playerHealth != null) playerHealth.Healed += RefreshHealth;
        }

        void OnDisable()
        {
            if (rifle != null)
            {
                rifle.Fired -= OnFired;
                rifle.AmmoChanged -= RefreshAmmo;
                rifle.ReloadStarted -= RefreshAmmo;
                rifle.ReloadCancelled -= RefreshAmmo;
            }
            GameEvents.PlayerDamaged -= OnPlayerDamaged;
            CameraNetwork.Sighted -= OnSpotted;
            if (playerHealth != null) playerHealth.Healed -= RefreshHealth;
        }

        void Start()
        {
            RefreshAmmo();
            RefreshHealth();
        }

        void OnFired(ShotResult shot)
        {
            if (!shot.HitTarget) return;
            markerWasKill = shot.Kill;
            markerWasWeak = shot.WeakPoint;
            markerUntil = Time.time + (shot.Kill ? killMarkerTime : markerTime);
        }

        void OnSpotted(Vector3 at) => spottedUntil = Time.time + spottedTime;

        void OnPlayerDamaged(float amount)
        {
            damageFlashUntil = Time.time + damageFlashTime;
            RefreshHealth();
        }

        // Strings are rebuilt only when the numbers change, not every frame.
        void RefreshAmmo() => ammoText = rifle == null ? "" :
            rifle.IsReloading ? "Reloading..." : $"{rifle.Magazine} / {rifle.Reserve}";

        void RefreshHealth() => healthText = playerHealth == null ? "" : $"HP {Mathf.CeilToInt(playerHealth.Current)}";

        void OnGUI()
        {
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            var white = Texture2D.whiteTexture;

            // Damage flash: red tint over the whole screen, fading out.
            float flash = Mathf.Clamp01((damageFlashUntil - Time.time) / damageFlashTime);
            if (flash > 0f)
            {
                GUI.color = new Color(1f, 0f, 0f, 0.35f * flash);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
            }

            // Spotted: red bars on all four screen edges and ARGUS's line (GDD "you've been spotted" feedback).
            if (Time.time < spottedUntil)
            {
                float pulse = 0.35f + 0.25f * Mathf.Sin(Time.time * 12f);
                GUI.color = new Color(1f, 0.1f, 0.05f, pulse);
                const float edge = 14f;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, edge), white);
                GUI.DrawTexture(new Rect(0, Screen.height - edge, Screen.width, edge), white);
                GUI.DrawTexture(new Rect(0, 0, edge, Screen.height), white);
                GUI.DrawTexture(new Rect(Screen.width - edge, 0, edge, Screen.height), white);
                GUI.color = new Color(1f, 0.25f, 0.2f, 1f);
                GUI.Label(new Rect(cx - 120f, 80f, 240f, 24f), "ARGUS: TARGET ACQUIRED");
            }

            // Crosshair: four ticks.
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            const float gap = 6f, len = 8f, thick = 2f;
            GUI.DrawTexture(new Rect(cx - gap - len, cy - thick / 2, len, thick), white);
            GUI.DrawTexture(new Rect(cx + gap, cy - thick / 2, len, thick), white);
            GUI.DrawTexture(new Rect(cx - thick / 2, cy - gap - len, thick, len), white);
            GUI.DrawTexture(new Rect(cx - thick / 2, cy + gap, thick, len), white);

            // Hit marker: an X; red and larger on a kill, yellow on a weak point.
            if (Time.time < markerUntil)
            {
                GUI.color = markerWasKill ? Color.red : markerWasWeak ? Color.yellow : Color.white;
                float s = markerWasKill ? 12f : 8f;
                DrawDiagonal(cx, cy, s, 45f);
                DrawDiagonal(cx, cy, s, -45f);
            }

            // Text, bottom corners.
            GUI.color = Color.white;
            GUI.Label(new Rect(20, Screen.height - 40, 200, 30), healthText);
            GUI.Label(new Rect(Screen.width - 160, Screen.height - 40, 150, 30), ammoText);
        }

        static void DrawDiagonal(float cx, float cy, float size, float angle)
        {
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, new Vector2(cx, cy));
            GUI.DrawTexture(new Rect(cx - size, cy - 1f, size * 2f, 2f), Texture2D.whiteTexture);
            GUI.matrix = m;
        }
    }
}
