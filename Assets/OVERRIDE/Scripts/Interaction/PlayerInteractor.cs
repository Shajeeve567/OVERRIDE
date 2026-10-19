// PlayerInteractor.cs
// Looks for something usable in front of the camera and uses it when E is pressed or held.
// Goes on the Player, next to FirstPersonController.

using Override.Player;
using UnityEngine;

namespace Override.Interaction
{
    [RequireComponent(typeof(PlayerInputReader), typeof(FirstPersonController))]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] Transform cameraTransform;
        [SerializeField] float reach = 2.5f;   // metres from the camera
        [Tooltip("Layers the interaction ray can hit. Walls must be included so you can't use things through them.")]
        [SerializeField] LayerMask rayMask = ~(1 << 2);   // everything except Ignore Raycast
        [SerializeField] bool showPrompt = true;          // temporary on-screen prompt until the HUD exists

        PlayerInputReader input;

        // The last thing the ray hit. Cached so we only call GetComponentInParent when the target changes.
        Collider lastCollider;
        IInteractable lookedAt;
        IHoldInteractable holding;

        public FirstPersonController Controller { get; private set; }
        public PlayerCarry Carry { get; private set; }
        public Transform CameraTransform => cameraTransform;
        public IInteractable LookedAt => lookedAt;

        void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            Controller = GetComponent<FirstPersonController>();
            Carry = GetComponent<PlayerCarry>();   // optional
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            if (!Controller.InputEnabled)
            {
                StopHolding();
                return;
            }

            UpdateLookTarget();

            // While holding (crate, terminal), keep going until E is released or the object lets go.
            if (holding != null)
            {
                if (!input.InteractHeld || !holding.HoldTick(this, Time.deltaTime)) StopHolding();
                return;
            }

            if (!input.InteractPressed) return;

            // E while carrying a canister drops it, unless we're looking at something else to use.
            if (Carry != null && Carry.IsCarrying && (lookedAt == null || !lookedAt.CanInteract))
            {
                Carry.Drop();
                return;
            }

            if (lookedAt == null || !lookedAt.CanInteract) return;

            lookedAt.Interact(this);
            if (lookedAt is IHoldInteractable h) holding = h;
        }

        void UpdateLookTarget()
        {
            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

            // The ray starts inside our own capsule, and rays ignore colliders they start inside.
            if (Physics.Raycast(ray, out RaycastHit hit, reach, rayMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != lastCollider)
                {
                    lastCollider = hit.collider;
                    lookedAt = hit.collider.GetComponentInParent<IInteractable>();
                }
            }
            else
            {
                lastCollider = null;
                lookedAt = null;
            }
        }

        public void StopHolding()
        {
            if (holding == null) return;
            holding.EndHold(this);
            holding = null;
        }

        void OnDisable() => StopHolding();

        // Placeholder prompt; the HUD (feature/game-flow) replaces this.
        void OnGUI()
        {
            if (!showPrompt || lookedAt == null || !lookedAt.CanInteract || holding != null) return;
            const float w = 300f, h = 30f;
            GUI.Label(new Rect((Screen.width - w) / 2f, Screen.height / 2f + 40f, w, h), "[E] " + lookedAt.Prompt);
        }
    }
}
