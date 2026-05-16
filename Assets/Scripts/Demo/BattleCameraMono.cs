using UnityEngine;
using UnityEngine.InputSystem;

namespace Demo
{
    // Attached to the Main Camera in BattleScene.
    // Scroll wheel zooms by moving the camera along its forward axis.
    // Middle-mouse drag pans on the XZ plane.
    public class BattleCameraMono : MonoBehaviour
    {
        [Tooltip("World units moved per scroll notch.")]
        public float ZoomSpeed = 20f;

        [Tooltip("Minimum distance from the ground (Y = 0 plane).")]
        public float MinHeight = 20f;

        [Tooltip("Maximum distance the camera can pull back to.")]
        public float MaxHeight = 400f;

        [Tooltip("Panning speed in world units per pixel dragged.")]
        public float PanSpeed = 0.3f;

        Vector2 _lastMousePos;

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            HandleZoom(mouse);
            HandlePan(mouse);
        }

        void HandleZoom(Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll == 0f) return;

            // Normalise: Input System reports pixels; divide to get notch-like feel.
            scroll *= 0.01f;

            Vector3 newPos = transform.position + transform.forward * (scroll * ZoomSpeed);
            newPos.y = Mathf.Clamp(newPos.y, MinHeight, MaxHeight);
            transform.position = newPos;
        }

        void HandlePan(Mouse mouse)
        {
            if (mouse.middleButton.wasPressedThisFrame)
                _lastMousePos = mouse.position.ReadValue();

            if (mouse.middleButton.isPressed)
            {
                Vector2 pos = mouse.position.ReadValue();
                Vector2 delta = pos - _lastMousePos;
                _lastMousePos = pos;

                Vector3 move = -transform.right * delta.x * PanSpeed
                             + -Vector3.forward * delta.y * PanSpeed;
                transform.position += move;
            }
        }
    }
}
