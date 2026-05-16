using UnityEngine;

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

        Vector3 _lastMousePos;

        void Update()
        {
            HandleZoom();
            HandlePan();
        }

        void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll == 0f) return;

            var cam = transform;
            Vector3 newPos = cam.position + cam.forward * (scroll * ZoomSpeed);

            // Clamp so camera stays between MinHeight and MaxHeight above Y=0.
            newPos.y = Mathf.Clamp(newPos.y, MinHeight, MaxHeight);
            cam.position = newPos;
        }

        void HandlePan()
        {
            if (Input.GetMouseButtonDown(2))
                _lastMousePos = Input.mousePosition;

            if (Input.GetMouseButton(2))
            {
                Vector3 delta = Input.mousePosition - _lastMousePos;
                _lastMousePos = Input.mousePosition;

                // Pan on XZ plane using the camera's local right and the world X axis.
                Vector3 move = -transform.right * delta.x * PanSpeed
                             + -Vector3.forward * delta.y * PanSpeed;
                transform.position += move;
            }
        }
    }
}
