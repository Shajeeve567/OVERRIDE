// HealthStation.cs
// A wall station the player uses with E to restore health (GDD: 2 per zone, no regeneration otherwise).
// It has limited charges, and isn't used up if the player is already at full health.

using Override.Interaction;
using UnityEngine;

namespace Override.Combat
{
    public class HealthStation : MonoBehaviour, IInteractable
    {
        [SerializeField] float healAmount = 50f;   // starting value
        [SerializeField] int charges = 1;
        [Tooltip("Optional: hidden when the station is empty (e.g. a glowing panel).")]
        [SerializeField] GameObject availableVisual;

        public string Prompt => $"Use health station (+{healAmount:0})";
        public bool CanInteract => charges > 0;

        public void Interact(PlayerInteractor player)
        {
            var health = player.GetComponent<Health>();
            if (health == null || health.Heal(healAmount) <= 0f) return;   // full health: don't waste it

            charges--;
            if (charges <= 0 && availableVisual != null) availableVisual.SetActive(false);
        }
    }
}
