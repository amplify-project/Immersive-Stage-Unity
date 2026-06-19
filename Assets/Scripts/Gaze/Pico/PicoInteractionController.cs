using UnityEngine;
using UnityEngine.UI;
using Unity.XR.PXR;

namespace Gaze.Pico
{
    /// <summary>
    /// Two PICO-specific interaction features:
    ///
    /// 1. Arrow-pad sphere rotation — a 4-way gaze D-pad. The combined eye gaze
    ///    picks ONE cardinal direction (the one it leans toward most) once it
    ///    passes edgeThreshold off head-forward; the matching arrow lights up and
    ///    the 360 sphere rotates that way at a CONSTANT speed while you keep
    ///    looking at it. Looking near center scrolls nothing.
    ///
    /// 2. Wink-to-zoom — right eye closed (left open) for winkThreshold seconds =
    ///    zoom in; left eye closed (right open) = zoom out.  A normal blink
    ///    (~130 ms, both eyes) is ignored because both-eyes-closed resets both
    ///    counters to zero.
    /// </summary>
    public class PicoInteractionController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The 360° video sphere. Its rotation is shifted to scroll the video.")]
        [SerializeField] Transform sphere;

        [Tooltip("Tracked head camera — the Main Camera inside the XR rig. Eye poses are head-relative and need this transform.")]
        [SerializeField] Transform headTransform;

        [Tooltip("Camera that renders to the HMD (Proxy Camera). Its FOV is changed for wink-zoom.")]
        [SerializeField] Camera proxyCamera;

        [Header("Arrow Pad (gaze-direction D-pad)")]
        [Tooltip("Dot-product distance from head-forward where an arrow ACTIVATES. " +
                 "Below this (looking near center) nothing scrolls. " +
                 "0.1 ≈ 6°  |  0.15 ≈ 9°  |  0.2 ≈ 12°  |  0.3 ≈ 17°. " +
                 "Keep low so a gentle eye glance triggers an arrow without head movement (locked-in users).")]
        [SerializeField, Range(0.05f, 0.8f)] float edgeThreshold = 0.12f;

        [Tooltip("Constant sphere rotation in degrees/second while an arrow is active. " +
                 "One cardinal arrow at a time — the direction the gaze leans toward most.")]
        [SerializeField] float scrollSpeed = 30f;

        [Header("Arrows (4 Image on a canvas anchored to the view — arrange in a cross)")]
        [SerializeField] Image arrowLeft;
        [SerializeField] Image arrowRight;
        [SerializeField] Image arrowUp;
        [SerializeField] Image arrowDown;

        [Tooltip("Opacity of an arrow at rest (always visible as a hint).")]
        [SerializeField, Range(0f, 1f)] float arrowMinAlpha = 0.3f;

        [Tooltip("Opacity of the arrow currently active (the direction you are looking).")]
        [SerializeField, Range(0f, 1f)] float arrowMaxAlpha = 0.8f;

        [Header("Wink Zoom")]
        [SerializeField] float zoomSpeed = 20f;
        [SerializeField] float minFOV = 40f;
        [SerializeField] float maxFOV = 90f;

        [Tooltip("Seconds one eye must stay closed while the other is open to count as a wink. " +
                 "Keeps normal blinks (~130 ms) from triggering zoom.")]
        [SerializeField] float winkThreshold = 0.3f;

        float m_RightClosedTime;
        float m_LeftClosedTime;

        void Update()
        {
            var getInfo = new EyeTrackingDataGetInfo
            {
                displayTime = 0,
                flags = EyeTrackingDataGetFlags.PXR_EYE_DEFAULT
                      | EyeTrackingDataGetFlags.PXR_EYE_POSITION
                      | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION
            };
            var data = new EyeTrackingData();
            if (PXR_MotionTracking.GetEyeTrackingData(ref getInfo, ref data) != 0)
            {
                SetAllArrowAlpha(0f);
                return;
            }

            if (headTransform != null)
            {
                var combined = data.eyeDatas[(int)PerEyeUsage.Combined];
                if (combined.isPoseValid != 0)
                    UpdateScrollAndArrows(combined);
                else
                    SetAllArrowAlpha(0f);
            }

            UpdateWinkZoom();
        }

        void UpdateScrollAndArrows(PerEyeData combined)
        {
            // Reconstruct world-space gaze direction — same OpenXR->Unity conversion
            // as PicoEyeGazeProvider: negate quaternion Z/W on the LOCAL pose.
            var localRot = new Quaternion(
                combined.pose.orientation.x,
                combined.pose.orientation.y,
               -combined.pose.orientation.z,
               -combined.pose.orientation.w);
            Vector3 gazeDir = headTransform.rotation * (localRot * Vector3.forward);

            // Project gaze onto the head's local right and up axes.
            // Value ranges roughly -1..1; positive = right/up, negative = left/down.
            float rightComp = Vector3.Dot(gazeDir, headTransform.right);
            float upComp    = Vector3.Dot(gazeDir, headTransform.up);

            // Pick the single dominant cardinal direction, like pressing ONE arrow on
            // a D-pad. It activates only once the gaze passes edgeThreshold off-center;
            // looking near the middle leaves all arrows idle.
            float absR = Mathf.Abs(rightComp);
            float absU = Mathf.Abs(upComp);

            bool right = false, left = false, up = false, down = false;
            if (Mathf.Max(absR, absU) >= edgeThreshold)
            {
                if (absR >= absU) { right = rightComp > 0f; left = rightComp < 0f; }
                else              { up    = upComp    > 0f; down = upComp    < 0f; }
            }

            if (sphere != null)
            {
                // Constant speed while an arrow is held by gaze (no proportional ramp).
                float step = scrollSpeed * Time.deltaTime;

                // Horizontal scroll (unlimited).
                if (right) sphere.Rotate(Vector3.up,  step, Space.World);
                if (left)  sphere.Rotate(Vector3.up, -step, Space.World);

                // Vertical scroll with ±90° constraint (up = look up = tilt sphere down).
                if (up || down)
                {
                    float currentVertical = sphere.eulerAngles.x;
                    if (currentVertical > 180f) currentVertical -= 360f;
                    float dir = up ? -step : step;
                    float newVertical = Mathf.Clamp(currentVertical + dir, -90f, 90f);
                    float clampedDelta = newVertical - currentVertical;
                    sphere.Rotate(headTransform.right, clampedDelta, Space.World);
                }
            }

            SetArrowActive(arrowLeft,  left);
            SetArrowActive(arrowRight, right);
            SetArrowActive(arrowUp,    up);
            SetArrowActive(arrowDown,  down);
        }

        void UpdateWinkZoom()
        {
            Camera cam = proxyCamera != null ? proxyCamera : Camera.main;
            if (cam == null) return;

            long timestamp = 0;
            bool isLeftBlink = false;
            bool isRightBlink = false;
            int result = PXR_MotionTracking.GetEyeBlink(ref timestamp, ref isLeftBlink, ref isRightBlink);

            if (result != 0)
            {
                // Eye tracking not available or API failed; wink zoom disabled.
                m_RightClosedTime = 0f;
                m_LeftClosedTime = 0f;
                return;
            }

            // Both eyes closed (normal blink ~130ms) → reset both timers.
            // One eye closed alone → accumulate that timer.
            m_RightClosedTime = (isRightBlink && !isLeftBlink) ? m_RightClosedTime + Time.deltaTime : 0f;
            m_LeftClosedTime  = (isLeftBlink && !isRightBlink) ? m_LeftClosedTime  + Time.deltaTime : 0f;

            // Right eye closed ≥ winkThreshold → zoom in (narrow FOV).
            if (m_RightClosedTime >= winkThreshold)
                cam.fieldOfView = Mathf.Max(minFOV, cam.fieldOfView - zoomSpeed * Time.deltaTime);
            // Left eye closed ≥ winkThreshold → zoom out (widen FOV).
            else if (m_LeftClosedTime >= winkThreshold)
                cam.fieldOfView = Mathf.Min(maxFOV, cam.fieldOfView + zoomSpeed * Time.deltaTime);
        }

        void SetArrowActive(Image img, bool active)
        {
            if (img == null) return;

            // At rest: dim hint (minAlpha). Active (gaze on this direction): full (maxAlpha).
            Color c = img.color;
            c.a = active ? arrowMaxAlpha : arrowMinAlpha;
            img.color = c;
        }

        void SetAllArrowAlpha(float a)
        {
            if (arrowLeft != null)  arrowLeft.color = new Color(arrowLeft.color.r, arrowLeft.color.g, arrowLeft.color.b, a);
            if (arrowRight != null) arrowRight.color = new Color(arrowRight.color.r, arrowRight.color.g, arrowRight.color.b, a);
            if (arrowUp != null)    arrowUp.color = new Color(arrowUp.color.r, arrowUp.color.g, arrowUp.color.b, a);
            if (arrowDown != null)  arrowDown.color = new Color(arrowDown.color.r, arrowDown.color.g, arrowDown.color.b, a);
        }
    }
}
