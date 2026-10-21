// PlayerInputReader.cs
// Reads the player's keys and mouse with the Input System and exposes them as simple values.
//
// WHY THE ACTIONS ARE BUILT IN CODE (instead of editing Assets/InputSystem_Actions.inputactions):
//   That asset is shared by the whole team, and the GDD controls (Ctrl crouch, R, G, F1, Esc)
//   differ from Unity's template. Building our own actions here means no merge conflicts on a
//   shared file, and every binding is readable in one place.

using Override.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Override.Player
{
    public class PlayerInputReader : MonoBehaviour
    {
        InputAction move, look, fire, aim, reload, interact, throwAction, sprint, crouch, jump,
                    weapon1, weapon2, debugOverlay, pause;

        public Vector2 Move => move.ReadValue<Vector2>();
        public Vector2 LookDelta => look.ReadValue<Vector2>();   // pixels moved this frame

        public bool FireHeld => fire.IsPressed();
        public bool AimHeld => aim.IsPressed();
        public bool SprintHeld => sprint.IsPressed();
        public bool CrouchHeld => crouch.IsPressed();
        public bool InteractHeld => interact.IsPressed();

        // "Pressed" = true only on the frame the key went down.
        public bool JumpPressed => jump.WasPressedThisFrame();
        public bool ReloadPressed => reload.WasPressedThisFrame();
        public bool InteractPressed => interact.WasPressedThisFrame();
        public bool InteractReleased => interact.WasReleasedThisFrame();
        public bool ThrowPressed => throwAction.WasPressedThisFrame();
        public bool Weapon1Pressed => weapon1.WasPressedThisFrame();
        public bool Weapon2Pressed => weapon2.WasPressedThisFrame();
        public bool DebugOverlayPressed => debugOverlay.WasPressedThisFrame();
        public bool PausePressed => pause.WasPressedThisFrame();

        void Awake()
        {
            // Controls from GDD Section 5.
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            fire = new InputAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            aim = new InputAction("Aim", InputActionType.Button, "<Mouse>/rightButton");   // hold to aim down sights
            reload = new InputAction("Reload", InputActionType.Button, "<Keyboard>/r");
            interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            throwAction = new InputAction("Throw", InputActionType.Button, "<Keyboard>/g");
            sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");

            crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            crouch.AddBinding("<Keyboard>/c");   // Mac laptops: Ctrl+click is awkward, so C works too

            jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            weapon1 = new InputAction("Weapon1", InputActionType.Button, "<Keyboard>/1");
            weapon2 = new InputAction("Weapon2", InputActionType.Button, "<Keyboard>/2");
            debugOverlay = new InputAction("DebugOverlay", InputActionType.Button, "<Keyboard>/f1");
            pause = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
        }

        void OnEnable() => SetAll(true);

        // F1 is the team-wide debug overlay (heatmap, paths, scores), whatever else the player is doing.
        void Update()
        {
            if (DebugOverlayPressed) DebugOverlay.Toggle();
        }
        void OnDisable() => SetAll(false);

        void OnDestroy()
        {
            foreach (var a in All()) a.Dispose();
        }

        void SetAll(bool on)
        {
            foreach (var a in All())
            {
                if (on) a.Enable();
                else a.Disable();
            }
        }

        InputAction[] All() => new[]
        {
            move, look, fire, aim, reload, interact, throwAction, sprint, crouch, jump,
            weapon1, weapon2, debugOverlay, pause,
        };
    }
}
