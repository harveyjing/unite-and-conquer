using UnityEngine;
using UnityEngine.InputSystem;

namespace Demo
{
    // Attached to the Main Camera in BattleScene. Camera is orthographic.
    // Scroll wheel zooms by changing orthographicSize.
    // Left-mouse drag pans: the world point under the cursor at press time
    // stays pinned under the cursor for the entire drag.
    [RequireComponent(typeof(Camera))]
    public class BattleCameraMono : MonoBehaviour
    {
        [Tooltip("Ortho size units changed per scroll notch.")]
        public float ZoomSpeed = 20f;

        [Tooltip("Minimum orthographic size (closest zoom).")]
        public float MinOrthoSize = 10f;

        [Tooltip("Maximum orthographic size (farthest zoom).")]
        public float MaxOrthoSize = 200f;

        Camera _cam;
        bool _dragging;
        Vector3 _dragAnchor;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
        }

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

            float size = _cam.orthographicSize - scroll * ZoomSpeed;
            _cam.orthographicSize = Mathf.Clamp(size, MinOrthoSize, MaxOrthoSize);
        }

        void HandlePan(Mouse mouse)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (TryScreenToGround(mouse.position.ReadValue(), out var anchor))
                {
                    _dragAnchor = anchor;
                    _dragging = true;
                }
            }

            if (mouse.leftButton.wasReleasedThisFrame)
                _dragging = false;

            if (_dragging && TryScreenToGround(mouse.position.ReadValue(), out var current))
            {
                // Translate so the anchor stays under the cursor in world space.
                transform.position += _dragAnchor - current;
            }
        }

        // Intersect a ray from the cursor with the Y = 0 plane.
        bool TryScreenToGround(Vector2 screenPos, out Vector3 worldPos)
        {
            Ray ray = _cam.ScreenPointToRay(screenPos);
            if (Mathf.Abs(ray.direction.y) < 1e-5f)
            {
                worldPos = default;
                return false;
            }
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f)
            {
                worldPos = default;
                return false;
            }
            worldPos = ray.origin + ray.direction * t;
            return true;
        }
    }
}
