// IInteractable.cs
// What the player can use with E. PlayerInteractor finds these by raycasting from the camera.
//
// Two kinds:
//   IInteractable      - press E once (doors toggle, canisters get picked up).
//   IHoldInteractable  - hold E (crates are pushed, terminals are hacked). The interactor keeps
//                        calling HoldTick while E is held and the object says the player is still in range.

namespace Override.Interaction
{
    public interface IInteractable
    {
        /// Short text for the on-screen prompt, e.g. "Open door".
        string Prompt { get; }

        /// Can it be used right now? (A finished terminal can't.)
        bool CanInteract { get; }

        /// Called once on the frame E is pressed while looking at this.
        void Interact(PlayerInteractor player);
    }

    public interface IHoldInteractable : IInteractable
    {
        /// Called every frame while E stays held. Return false to end the hold (e.g. player too far away).
        bool HoldTick(PlayerInteractor player, float deltaTime);

        /// Called once when the hold ends for any reason.
        void EndHold(PlayerInteractor player);
    }
}
