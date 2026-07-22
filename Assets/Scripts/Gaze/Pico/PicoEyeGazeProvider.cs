// Compiled ONLY for Android builds with the PICO_XR define and the PICO Unity
// Integration SDK installed (enforced by Gaze.Pico.asmdef: includePlatforms
// Android+Editor, defineConstraints PICO_XR). Never enters visionOS builds.
//
// NOTE: written against the PICO Unity Integration SDK 3.x eye-tracking API
// (PXR_MotionTracking). After importing SDK 3.4.0, if any struct/field name
// drifted between versions, the compiler will point at it here - this is the
// only file that touches the PICO API.

using UnityEngine;
using UnityEngine.Android;
using Unity.XR.PXR;
using Gaze.Core;

namespace Gaze.Pico
{
    /// <summary>
    /// Real eye-gaze ray on PICO 4 Pro/Enterprise via the PICO eye-tracking API.
    /// Registers itself with the core dwell engine at high priority; whenever eye
    /// data is unavailable (permission denied, tracking lost, base PICO 4 device)
    /// it returns false and the engine falls back to head gaze.
    /// </summary>
    public class PicoEyeGazeProvider : MonoBehaviour, IGazeRayProvider
    {
        const string EyeTrackingPermission = "com.picovr.permission.EYE_TRACKING";
        const int ProviderPriority = 100;

        [Tooltip("Tracked head transform (the XR Origin's tracked Main Camera). Eye poses are head-relative and are transformed into world space through this.")]
        [SerializeField] Transform headTransform;

        [Tooltip("Camera that renders the user's view (Proxy Camera). The eye ray is cast from it so the ray matches what is on screen even when the zoom arm displaces the camera from the tracked head. Falls back to headTransform when empty/inactive.")]
        [SerializeField] Camera projectionCamera;

        [Tooltip("Gaze gain: amplifies the off-axis eye angle so peripheral targets are reachable with a small, comfortable eye movement (locked-in users who can't add head rotation). 1 = raw gaze, 1.5-2 = reach the edges with a gentle glance.")]
        [SerializeField, Range(1f, 3f)] float gazeGain = 1.6f;

        bool  m_TrackingStarted;
        Ray   m_LastValidRay;
        bool  m_HasValidRay;
        float m_LastValidTime;
        float m_LastInvalidTime;
        const float BlinkGrace = 0.15f;

        void Start()
        {
            Debug.Log("[PicoEyeGazeProvider] Starting...");
            if (!Permission.HasUserAuthorizedPermission(EyeTrackingPermission))
            {
                Debug.Log("[PicoEyeGazeProvider] Permission not granted, requesting...");
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => StartEyeTracking();
                Permission.RequestUserPermission(EyeTrackingPermission, callbacks);
            }
            else
            {
                Debug.Log("[PicoEyeGazeProvider] Permission already granted, starting eye tracking");
                StartEyeTracking();
            }

            GazeDwellUIClicker.RegisterProvider(this, ProviderPriority);
            Debug.Log("[PicoEyeGazeProvider] Registered with priority " + ProviderPriority);

            // Eyes-only deployment: kill the head-gaze fallback. On a lost/invalid
            // eye frame the reticle now hides rather than snapping to head-forward,
            // which a locked-in user cannot steer.
            var clicker = FindFirstObjectByType<GazeDwellUIClicker>();
            if (clicker != null)
            {
                clicker.SetHeadGazeFallbackEnabled(false);
                Debug.Log("[PicoEyeGazeProvider] Head-gaze fallback disabled (eyes-only).");
            }
            else
            {
                Debug.LogWarning("[PicoEyeGazeProvider] No GazeDwellUIClicker found - head-gaze fallback NOT disabled.");
            }
        }

        void OnDestroy()
        {
            GazeDwellUIClicker.UnregisterProvider(this);
            if (m_TrackingStarted)
            {
                var stopInfo = new EyeTrackingStopInfo();
                PXR_MotionTracking.StopEyeTracking(ref stopInfo);
            }
        }

        void StartEyeTracking()
        {
            var startInfo = new EyeTrackingStartInfo
            {
                // 0 = needs calibration (per PXR_Type.cs' EyeTrackingStartInfo doc:
                // 0 = needs, 1 = does not need) - forces PICO's native calibration
                // flow before eye data starts flowing.
                needCalibration = 0,
                mode = EyeTrackingMode.PXR_ETM_BOTH
            };
            int result = PXR_MotionTracking.StartEyeTracking(ref startInfo);
            m_TrackingStarted = result == 0;
            if (!m_TrackingStarted)
                Debug.LogWarning($"[PicoEyeGazeProvider] StartEyeTracking failed ({result}) - falling back to head gaze.");
        }

        public bool TryGetGazeRay(out Ray worldRay)
        {
            worldRay = default;
            if (!m_TrackingStarted || headTransform == null)
            {
                if (!m_TrackingStarted)
                    Debug.Log("[PicoEyeGazeProvider.TryGetGazeRay] Tracking not started");
                if (headTransform == null)
                    Debug.Log("[PicoEyeGazeProvider.TryGetGazeRay] headTransform is null");
                return false;
            }

            var getInfo = new EyeTrackingDataGetInfo
            {
                displayTime = 0,
                flags = EyeTrackingDataGetFlags.PXR_EYE_DEFAULT
                      | EyeTrackingDataGetFlags.PXR_EYE_POSITION
                      | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION
            };
            var data = new EyeTrackingData();
            bool sdkOk = PXR_MotionTracking.GetEyeTrackingData(ref getInfo, ref data) == 0;

            if (sdkOk)
            {
                var combined = data.eyeDatas[(int)PerEyeUsage.Combined];
                if (combined.isPoseValid != 0)
                {
                    // Head-relative combined gaze pose in OpenXR right-handed space.
                    // Convert to Unity left-handed the same way the SDK's own
                    // Vector3f.ToVector3/Quatf.ToQuat helpers do: negate position Z
                    // and quaternion Z/W. The conversion must happen on the LOCAL
                    // pose, before composing with the head rotation.
                    Vector3 localOrigin = new Vector3(
                        combined.pose.position.x,
                        combined.pose.position.y,
                       -combined.pose.position.z);
                    Quaternion localRotation = new Quaternion(
                        combined.pose.orientation.x,
                        combined.pose.orientation.y,
                       -combined.pose.orientation.z,
                       -combined.pose.orientation.w);
                    Vector3 localDirection = localRotation * Vector3.forward;

                    // Gaze gain: amplify the off-axis eye angle (x,y relative to the
                    // forward z) so peripheral targets need less eye travel. Keeps the
                    // reticle eye-driven but makes everything feel "closer to center"
                    // for users who cannot add head rotation.
                    if (gazeGain != 1f && localDirection.z > 0.0001f)
                        localDirection = new Vector3(
                            localDirection.x * gazeGain,
                            localDirection.y * gazeGain,
                            localDirection.z).normalized;

                    // Cast the gaze ray FROM the projection (Proxy) camera, not the
                    // tracked head. The Proxy Camera shares the head's rotation but
                    // is pushed up to 25 m forward by the ControllerZoom arm; casting
                    // from the head and projecting through that far camera makes a
                    // small head turn sweep the reticle wildly (parallax arm). Using
                    // the projection camera as both ray origin AND rotation frame
                    // cancels head rotation in screen space, so head movement no
                    // longer moves the reticle - only eye movement does. Falls back
                    // to the head eye-pose when no projection camera is wired.
                    Camera projCam = projectionCamera != null && projectionCamera.isActiveAndEnabled
                        ? projectionCamera : null;
                    Quaternion refRotation = projCam != null ? projCam.transform.rotation : headTransform.rotation;
                    Vector3 origin = projCam != null ? projCam.transform.position : headTransform.TransformPoint(localOrigin);
                    Vector3 direction = refRotation * localDirection;

                    worldRay       = new Ray(origin, direction);
                    m_LastValidRay  = worldRay;
                    m_HasValidRay   = true;
                    m_LastValidTime = Time.unscaledTime;
                    return true;
                }
            }

            // Blink grace: reuse last valid ray for up to BlinkGrace seconds so a
            // normal blink (~100-130 ms) does not reset the dwell timer.
            if (m_HasValidRay && (Time.unscaledTime - m_LastValidTime) < BlinkGrace)
            {
                worldRay = m_LastValidRay;
                return true;
            }

            return false;
        }
    }
}
