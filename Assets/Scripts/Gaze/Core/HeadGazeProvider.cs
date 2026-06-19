using UnityEngine;

namespace Gaze.Core
{
    /// <summary>
    /// Gaze ray from the rendering camera's forward direction ("head gaze").
    /// Universal default: the only gaze source allowed on visionOS, the editor
    /// fallback, and the fallback on Pico when eye-tracking data is invalid.
    /// </summary>
    public class HeadGazeProvider : MonoBehaviour, IGazeRayProvider
    {
        [Tooltip("Camera whose forward is the gaze. In this project wire the Proxy Camera (it renders the HMD view). Falls back to Camera.main, then any active camera.")]
        [SerializeField] Camera gazeCamera;

        public bool TryGetGazeRay(out Ray worldRay)
        {
            Camera cam = ResolveCamera();
            if (cam == null)
            {
                worldRay = default;
                return false;
            }

            Transform t = cam.transform;
            worldRay = new Ray(t.position, t.forward);
            return true;
        }

        Camera ResolveCamera()
        {
            if (gazeCamera != null && gazeCamera.isActiveAndEnabled)
                return gazeCamera;
            if (Camera.main != null && Camera.main.isActiveAndEnabled)
                return Camera.main;
            return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
        }
    }
}
