// DirectorBridge.cs
// Connects the team's game events to Shajeeve's Director, replacing the calls his stand-ins made:
//   GameEvents.PlayerDamaged      -> director.ReportPlayerDamaged
//   GameEvents.HackStarted        -> director.ReportHackStarted
//   GameEvents.TerminalOverridden -> director.ReportTerminalOverridden
//   rifle hits an ARGUS unit      -> director.ReportEngagement
// Neither side references the other directly; only this small class knows both.
// Put it on the same object as the DirectorController.

using Override.Combat;
using Override.Core;
using Override.Director;
using UnityEngine;

namespace Override.Integration
{
    [RequireComponent(typeof(DirectorController))]
    public class DirectorBridge : MonoBehaviour
    {
        DirectorController director;
        HitscanRifle rifle;

        void Awake() => director = GetComponent<DirectorController>();

        void OnEnable()
        {
            GameEvents.PlayerDamaged += director.ReportPlayerDamaged;
            GameEvents.HackStarted += OnHackStarted;
            GameEvents.TerminalOverridden += OnTerminalOverridden;
        }

        void Start()
        {
            // The rifle is on the player, which may be spawned after us, so look it up here once.
            var player = GameObject.FindWithTag("Player");
            rifle = player != null ? player.GetComponent<HitscanRifle>() : null;
            if (rifle != null) rifle.Fired += OnFired;
            else Debug.LogWarning("[DirectorBridge] No HitscanRifle on the Player: engagements won't reach the Director.");
        }

        void OnDisable()
        {
            GameEvents.PlayerDamaged -= director.ReportPlayerDamaged;
            GameEvents.HackStarted -= OnHackStarted;
            GameEvents.TerminalOverridden -= OnTerminalOverridden;
            if (rifle != null) rifle.Fired -= OnFired;
        }

        // ZoneId names match the Director's zone strings ("Labs", "ServerHall", "Maintenance", "Core").
        void OnHackStarted(ZoneId zone) => director.ReportHackStarted(zone.ToString());
        void OnTerminalOverridden(ZoneId zone) => director.ReportTerminalOverridden(zone.ToString());

        void OnFired(ShotResult shot)
        {
            if (shot.HitTarget) director.ReportEngagement();
        }
    }
}
