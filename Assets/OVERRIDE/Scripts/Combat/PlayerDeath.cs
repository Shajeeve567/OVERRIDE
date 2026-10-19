// PlayerDeath.cs
// What happens to the player's body when health hits 0: input stops, the carried canister drops,
// and the rifle goes quiet. The lose screen and respawn belong to game flow (feature/game-flow).

using Override.Interaction;
using Override.Player;
using UnityEngine;

namespace Override.Combat
{
    [RequireComponent(typeof(Health), typeof(FirstPersonController))]
    public class PlayerDeath : MonoBehaviour
    {
        Health health;
        FirstPersonController controller;
        PlayerCarry carry;
        PlayerInteractor interactor;
        HitscanRifle rifle;

        void Awake()
        {
            health = GetComponent<Health>();
            controller = GetComponent<FirstPersonController>();
            carry = GetComponent<PlayerCarry>();
            interactor = GetComponent<PlayerInteractor>();
            rifle = GetComponentInChildren<HitscanRifle>();
        }

        void OnEnable() => health.Died += OnDied;
        void OnDisable() => health.Died -= OnDied;

        void OnDied()
        {
            controller.InputEnabled = false;
            if (interactor != null) interactor.StopHolding();   // dying mid-hack keeps the terminal's progress
            if (carry != null) carry.Drop();
            if (rifle != null) rifle.enabled = false;
            FirstPersonController.SetCursorLocked(false);
        }

        /// Undo the above, for respawn.
        public void Revive()
        {
            health.ResetHealth();
            controller.InputEnabled = true;
            if (rifle != null) rifle.enabled = true;
            FirstPersonController.SetCursorLocked(true);
        }
    }
}
