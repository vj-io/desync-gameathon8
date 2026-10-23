using UnityEngine.InputSystem;

namespace Desync
{
    /// All input actions, built in code (keyboard/mouse + gamepad). Read e.g. GameInput.Flip.WasPressedThisFrame().
    public static class GameInput
    {
        public static readonly InputAction Move = new InputAction("Move", InputActionType.Value);
        public static readonly InputAction Look = new InputAction("Look", InputActionType.Value);       // mouse delta (pixels) or stick
        public static readonly InputAction Flip = new InputAction("Flip", InputActionType.Button, "<Keyboard>/space");
        public static readonly InputAction Interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
        public static readonly InputAction Up = new InputAction("Up", InputActionType.Button, "<Keyboard>/upArrow");
        public static readonly InputAction Down = new InputAction("Down", InputActionType.Button, "<Keyboard>/downArrow");
        public static readonly InputAction Left = new InputAction("Left", InputActionType.Button, "<Keyboard>/leftArrow");
        public static readonly InputAction Right = new InputAction("Right", InputActionType.Button, "<Keyboard>/rightArrow");
        public static readonly InputAction Restart = new InputAction("Restart", InputActionType.Button, "<Keyboard>/r");
        public static readonly InputAction Pause = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
        public static readonly InputAction LevelSelect = new InputAction("LevelSelect", InputActionType.Button, "<Keyboard>/f2");
        public static readonly InputAction DemoMode = new InputAction("DemoMode", InputActionType.Button, "<Keyboard>/f9");

        static GameInput()
        {
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddBinding("<Gamepad>/leftStick");
            Look.AddBinding("<Mouse>/delta");
            Look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=12,y=12)");
            Flip.AddBinding("<Gamepad>/buttonSouth");
            Interact.AddBinding("<Gamepad>/buttonWest");
            Up.AddBinding("<Gamepad>/dpad/up");
            Down.AddBinding("<Gamepad>/dpad/down");
            Left.AddBinding("<Gamepad>/dpad/left");
            Right.AddBinding("<Gamepad>/dpad/right");
            Restart.AddBinding("<Gamepad>/select");
            Pause.AddBinding("<Gamepad>/start");
            foreach (var a in new[] { Move, Look, Flip, Interact, Up, Down, Left, Right, Restart, Pause, LevelSelect, DemoMode })
                a.Enable();
        }

        /// Touch the class early so the static ctor enables actions before first Update.
        public static void Init() { }
    }
}
