// ----------------------------------------------------------------------------
// The ArticulatedRig feature's Input System door: compiled only when the
// com.unity.inputsystem package is in the project (its asmdef's versionDefines
// set UNITYTK_INPUTSYSTEM and its defineConstraints require it), so a project
// without the package carries this folder inert.
// ----------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace M3trik.Unitytk.ArticulatedRig.InputSystem
{
    /// <summary>
    /// The Input System's mouse as the pointer every
    /// <c>ArticulatedRigPointerGrab</c> reads, registered as the player loads
    /// where the project's active input handling includes the Input System
    /// (<c>ENABLE_INPUT_SYSTEM</c>); elsewhere the runtime part's legacy pointer
    /// stays.
    /// </summary>
    /// <remarks>
    /// Prior art: the Input System's actions and its UI input module route
    /// bindings to callbacks and UI events, not a ray into scene colliders a
    /// component polls each frame; the grabber needs only a button, the pointer's
    /// position and the scroll, which <c>Mouse.current</c>, the package's own
    /// device API, reads directly.
    /// </remarks>
    sealed class InputSystemPointer : IPointerInput
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
#if ENABLE_INPUT_SYSTEM
            ArticulatedRigPointerGrab.UsePointer(new InputSystemPointer());
#endif
        }

        static ButtonControl Button(int index)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return null;
            return index == 1 ? mouse.rightButton : (index == 2 ? mouse.middleButton : mouse.leftButton);
        }

        public bool Available(int button) => Button(button) != null;

        public bool Pressed(int button) => Button(button).wasPressedThisFrame;

        public bool Held(int button) => Button(button).isPressed;

        public Vector3 Position => Mouse.current.position.ReadValue();

        public float Scroll => Mouse.current.scroll.ReadValue().y;
    }
}
