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
        [Tooltip("Shows what the interaction ray hits (name and distance) and draws it in the Scene view.")]
        [SerializeField] bool debugRay;

        PlayerInputReader input;

        // The last thing the ray hit. Cached so we only call GetComponentInParent when the target changes.
        Collider lastCollider;
        string debugText = "";
        IInteractable lookedAt;
        IHoldInteractable holding;

        public FirstPersonController Controller { get; private set; }
        public PlayerCarry Carry { get; private set; }
        public Transform CameraTransform => cameraTransform;
        public IInteractable LookedAt => lookedAt;
        public bool IsHolding => holding != null;
        /// The temporary on-screen prompt; the HUD switches it off and shows its own.
        public bool ShowPrompt { get => showPrompt; set => showPrompt = value; }

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
            bool didHit = Physics.Raycast(ray, out RaycastHit hit, reach, rayMask, QueryTriggerInteraction.Ignore);
            if (debugRay) DebugRay(ray, didHit, hit);
            if (didHit)
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

        void DebugRay(Ray ray, bool didHit, RaycastHit hit)
        {
            Debug.DrawRay(ray.origin, ray.direction * (didHit ? hit.distance : reach), didHit ? Color.green : Color.red);
            // Also report the first thing beyond our reach, so "too far away" is obvious.
            if (!didHit && Physics.Raycast(ray, out RaycastHit far, 50f, rayMask, QueryTriggerInteraction.Ignore))
                debugText = $"Out of reach: {far.collider.name} at {far.distance:0.0} m (reach {reach} m)";
            else
                debugText = didHit ? $"Ray hits: {hit.collider.name} at {hit.distance:0.0} m" : "Ray hits nothing";
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
            if (debugRay) GUI.Label(new Rect(20, Screen.height - 70, 600, 25), debugText);   // above the HP text, clear of the Director panel
            if (!showPrompt || lookedAt == null || !lookedAt.CanInteract || holding != null) return;
            const float w = 300f, h = 30f;
            GUI.Label(new Rect((Screen.width - w) / 2f, Screen.height / 2f + 40f, w, h), "[E] " + lookedAt.Prompt);
        }
    }
}
