// AmmoCrate.cs
// An ammo crate next to each terminal (GDD). E adds reserve rounds; limited charges, not used up
// if the player's reserve is already full.

using Override.Interaction;
using UnityEngine;

namespace Override.Combat
{
    public class AmmoCrate : MonoBehaviour, IInteractable
    {
        [SerializeField] int rounds = 60;   // two magazines; starting value
        [SerializeField] int charges = 2;
        [SerializeField] GameObject availableVisual;

        public string Prompt => $"Take ammo (+{rounds})";
        public bool CanInteract => charges > 0;

        public void Interact(PlayerInteractor player)
        {
            var rifle = player.GetComponent<HitscanRifle>();
            if (rifle == null || rifle.AddAmmo(rounds) <= 0) return;

            charges--;
            if (charges <= 0 && availableVisual != null) availableVisual.SetActive(false);
        }
    }
}
