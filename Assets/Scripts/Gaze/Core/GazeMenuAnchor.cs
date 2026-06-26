using UnityEngine;

namespace Gaze.Core
{
    /// <summary>
    /// Keeps this world-space canvas in front of the camera that actually renders
    /// at runtime. The menu is placed in the editor relative to the edit-time
    /// camera, but PlatformManager switches rigs at startup and the ControllerZoom
    /// arm can move the Proxy Camera far from its edit-time position, leaving the
    /// menu behind the camera (a one-sided canvas is then invisible).
    ///
    /// When <see cref="headLock"/> is on (eyes-only deployments) the menu re-anchors
    /// every frame so it always stays in view - a locked-in user cannot turn to
    /// find a world-fixed panel. When off, it re-anchors only for the startup
    /// settle window and then freezes in world space.
    /// </summary>
    public class GazeMenuAnchor : MonoBehaviour
    {
        [Tooltip("Camera that renders the user's view (Proxy Camera in VR). Falls back to Camera.main, then any active camera.")]
        [SerializeField] Camera projectionCamera;

        [Tooltip("Distance in meters from the camera at which the menu is placed.")]
        [SerializeField] float distance = 3.5f;

        [Tooltip("Re-anchor every frame so the menu always follows the head (eyes-only). When off, only re-anchors for settleFrames at startup, then freezes in world space.")]
        [SerializeField] bool headLock = true;

        [Tooltip("Frames to keep re-anchoring after enable when headLock is off, so the platform rig switch and zoom arm have settled.")]
        [SerializeField] int settleFrames = 10;

        int m_FramesLeft;

        void OnEnable()
        {
            m_FramesLeft = settleFrames;
        }

        void LateUpdate()
        {
            if (!headLock && m_FramesLeft-- <= 0)
            {
                enabled = false;
                return;
            }

            Camera cam = ResolveCamera();
            if (cam == null)
                return;

            Vector3 flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flatForward == Vector3.zero)
                return;

            transform.SetPositionAndRotation(
                cam.transform.position + flatForward * distance,
                Quaternion.LookRotation(flatForward, Vector3.up));
        }

        Camera ResolveCamera()
        {
            if (projectionCamera != null && projectionCamera.isActiveAndEnabled)
                return projectionCamera;
            if (Camera.main != null && Camera.main.isActiveAndEnabled)
                return Camera.main;
            return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
        }
    }
}
