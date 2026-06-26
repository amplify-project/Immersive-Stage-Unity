// Compiled ONLY for visionOS builds (and the Editor, for scene authoring) -
// enforced by Gaze.VisionOS.asmdef. Never enters Android/Pico builds.
//
// visionOS never exposes continuous eye gaze to apps (privacy): gaze data only
// arrives at the moment of a pinch. Therefore on Apple Vision Pro the dwell
// engine runs on HEAD gaze - which is exactly what Gaze.Core's HeadGazeProvider
// fallback already does, so no provider registration happens here. This
// component owns the AVP-only contingencies instead.

using UnityEngine;
using Gaze.Core;

namespace Gaze.VisionOS
{
    public class VisionOSGazeSetup : MonoBehaviour
    {
        [Header("Proxy-camera fallback (plan risk: Metal compositing may ignore the untracked Proxy Camera)")]
        [Tooltip("Enable ONLY if the AVP build shows a black/wrong view: disables the Proxy Camera and restores HMD rendering on the tracked camera (zoom is lost on AVP - acceptable, there are no controllers).")]
        [SerializeField] bool useTrackedCameraFallback = false;

        [SerializeField] Camera proxyCamera;
        [SerializeField] Camera trackedCamera;

        void Awake()
        {
#if UNITY_VISIONOS
            if (useTrackedCameraFallback)
                ApplyTrackedCameraFallback();
#endif
        }

        /// <summary>
        /// Makes the tracked XR camera render to the HMD instead of the Proxy
        /// Camera. GazeDwellUIClicker re-resolves its projection camera lazily,
        /// so the dwell system follows automatically.
        /// </summary>
        public void ApplyTrackedCameraFallback()
        {
            if (proxyCamera != null)
                proxyCamera.gameObject.SetActive(false);

            if (trackedCamera != null)
            {
                trackedCamera.stereoTargetEye = StereoTargetEyeMask.Both;
                trackedCamera.targetDisplay = 0;
                trackedCamera.enabled = true;
            }

            Debug.Log("[VisionOSGazeSetup] Tracked-camera fallback applied (Proxy Camera disabled).");
        }

        // Optional iteration 2 - pinch as instant click:
        // poll Unity.XR.VisionOS's VisionOSSpatialPointerDevice (phase Began) and
        // call FindFirstObjectByType<GazeDwellUIClicker>().TriggerImmediateClick().
        // Deliberately left out of the first build: the input-device API surface
        // should be verified against the installed com.unity.xr.visionos version
        // on the Mac before wiring it (add "Unity.XR.VisionOS" to this asmdef's
        // references when implementing).
    }
}
