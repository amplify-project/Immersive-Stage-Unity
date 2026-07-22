using UnityEngine;
using UnityEngine.UI;
using Unity.XR.PXR;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEditor;
#endif

namespace Gaze.Pico
{
    /// <summary>
    /// Two PICO-specific interaction features:
    ///
    /// 1. Arrow-pad sphere rotation — a 4-way gaze D-pad. The combined eye gaze
    ///    picks ONE cardinal direction (the one it leans toward most) once it
    ///    reaches that arrow's own angle off head-forward; the matching arrow
    ///    lights up and the 360 sphere rotates that way at a CONSTANT speed
    ///    while you keep looking at it. Looking near center scrolls nothing.
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
        [Tooltip("Same gaze gain as PicoEyeGazeProvider's reticle. Without it the D-pad " +
                 "needs far more real eye travel than every other gaze target in the scene, " +
                 "which is why arrows only lit up when looking almost into the corners.")]
        [SerializeField, Range(1f, 3f)] float gazeGain = 1.6f;

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

        [Tooltip("Color tint applied to an arrow while active. An alpha-only change is too " +
                 "subtle to read at a glance over a moving 360 video, so the active arrow also " +
                 "switches to this color instead of just fading in.")]
        [SerializeField] Color arrowHighlightColor = new Color(0.62f, 0.2f, 0.86f, 1f);

        [Header("Arrow Distance From Center (canvas units, ~1.2 m away: 300 ≈ 14°, 180 ≈ 8.5°)")]
        [Tooltip("Set here instead of on the RectTransform so it survives without hand-editing " +
                 "the live scene - this overwrites anchoredPosition at startup. This is also THE " +
                 "gaze activation distance for that arrow: there used to be a separate threshold " +
                 "number that didn't track the arrow's actual screen position - now the gaze has " +
                 "to reach the arrow itself (computed from its real angle off head-forward), so " +
                 "moving an arrow always moves its trigger zone with it.")]
        [SerializeField, Range(50f, 500f)] float arrowDistanceLeft  = 180f;
        [SerializeField, Range(50f, 500f)] float arrowDistanceRight = 180f;
        [SerializeField, Range(50f, 500f)] float arrowDistanceUp    = 180f;
        [SerializeField, Range(50f, 500f)] float arrowDistanceDown  = 180f;

        Color m_BaseColorLeft, m_BaseColorRight, m_BaseColorUp, m_BaseColorDown;

#if UNITY_EDITOR
        enum EditorPreviewSource { Off, Keyboard, Mouse }

        [Header("Editor Preview (no headset needed)")]
        [Tooltip("Keyboard: arrow keys simulate looking that direction (full deflection while " +
                 "held). Mouse: cursor offset from the center of the Game view simulates gaze " +
                 "continuously, like the real eye data would. Only kicks in when there is no " +
                 "real eye data (never on device).")]
        [SerializeField] EditorPreviewSource editorPreviewSource = EditorPreviewSource.Mouse;
#endif

        [Header("Zoom Spheres (gaze-dwell, replaces wink over time)")]
        [Tooltip("Head-locked sphere that starts continuous zoom-IN once gazed at for zoomDwellSeconds.")]
        [SerializeField] Transform zoomSphereRight;

        [Tooltip("Head-locked sphere that starts continuous zoom-OUT once gazed at for zoomDwellSeconds.")]
        [SerializeField] Transform zoomSphereLeft;

        [Tooltip("Angular radius (degrees) around a zoom sphere's actual position within which gaze " +
                 "counts as \"looking at it\". A fixed radius rather than the arrow-pad's per-arrow " +
                 "threshold, because a diagonal circular target doesn't fit the left/right/up/down " +
                 "dominant-axis model.")]
        [SerializeField, Range(5f, 30f)] float zoomActivationAngleDeg = 12f;

        [Tooltip("Seconds of continuous gaze on a zoom sphere before it arms and starts changing FOV.")]
        [SerializeField] float zoomDwellSeconds = 1f;

        [Tooltip("Color tint applied to a zoom sphere while its dwell is armed (>= zoomDwellSeconds).")]
        [SerializeField] Color zoomSphereHighlightColor = new Color(0.62f, 0.2f, 0.86f, 1f);

        Color m_BaseColorZoomRight, m_BaseColorZoomLeft;
        float m_ZoomInDwellTimer;
        float m_ZoomOutDwellTimer;

        [Header("Wink Zoom")]
        [Tooltip("Master switch for the eye-wink zoom gesture. Turn off once the gaze-dwell " +
                 "zoom spheres are validated on-device; the wink code stays in place, just unused.")]
        [SerializeField] bool winkZoomEnabled = true;

        [SerializeField] float zoomSpeed = 20f;
        [SerializeField] float minFOV = 40f;
        [SerializeField] float maxFOV = 90f;

        [Tooltip("Seconds one eye must stay closed while the other is open to count as a wink. " +
                 "Keeps normal blinks (~130 ms) from triggering zoom.")]
        [SerializeField] float winkThreshold = 0.3f;

        float m_RightClosedTime;
        float m_LeftClosedTime;

        // HUD elements (arrow canvas, zoom spheres) sit at a fixed distance from
        // proxyCamera, so narrowing its FOV during zoom magnifies them on screen
        // exactly like it magnifies the video. Counter-scaling by referenceFOV /
        // currentFOV keeps their apparent screen size constant through a zoom.
        float m_ReferenceFOV;
        Transform m_ArrowCanvas;
        Vector3 m_ArrowCanvasBaseScale;
        Vector3 m_ZoomSphereRightBaseScale;
        Vector3 m_ZoomSphereLeftBaseScale;

        void Awake()
        {
            // Remember each arrow's authored rest color so SetArrowActive can restore
            // it exactly (instead of assuming white) when the highlight turns off.
            if (arrowLeft != null)  m_BaseColorLeft  = arrowLeft.color;
            if (arrowRight != null) m_BaseColorRight = arrowRight.color;
            if (arrowUp != null)    m_BaseColorUp    = arrowUp.color;
            if (arrowDown != null)  m_BaseColorDown  = arrowDown.color;

            // Remember each zoom sphere's authored rest color, same reasoning as the
            // arrows' base colors above.
            if (zoomSphereRight != null)
            {
                var renderer = zoomSphereRight.GetComponent<Renderer>();
                if (renderer != null) m_BaseColorZoomRight = renderer.sharedMaterial.color;
            }
            if (zoomSphereLeft != null)
            {
                var renderer = zoomSphereLeft.GetComponent<Renderer>();
                if (renderer != null) m_BaseColorZoomLeft = renderer.sharedMaterial.color;
            }

            m_ReferenceFOV = proxyCamera != null ? proxyCamera.fieldOfView : 60f;

            m_ArrowCanvas = arrowLeft  != null ? arrowLeft.transform.parent
                          : arrowRight != null ? arrowRight.transform.parent
                          : arrowUp    != null ? arrowUp.transform.parent
                          : arrowDown  != null ? arrowDown.transform.parent
                          : null;
            if (m_ArrowCanvas != null) m_ArrowCanvasBaseScale = m_ArrowCanvas.localScale;

            if (zoomSphereRight != null) m_ZoomSphereRightBaseScale = zoomSphereRight.localScale;
            if (zoomSphereLeft  != null) m_ZoomSphereLeftBaseScale  = zoomSphereLeft.localScale;
        }

        // Keeps the arrow canvas and zoom spheres at a constant apparent screen
        // size regardless of proxyCamera's current FOV (see field comments above).
        void CompensateHudScaleForZoom()
        {
            if (proxyCamera == null || m_ReferenceFOV <= 0f) return;

            float scale = proxyCamera.fieldOfView / m_ReferenceFOV;
            if (m_ArrowCanvas != null) m_ArrowCanvas.localScale = m_ArrowCanvasBaseScale * scale;
            if (zoomSphereRight != null) zoomSphereRight.localScale = m_ZoomSphereRightBaseScale * scale;
            if (zoomSphereLeft  != null) zoomSphereLeft.localScale  = m_ZoomSphereLeftBaseScale  * scale;
        }

        // Drive each arrow's screen position from the distances above every frame
        // (not just once in Awake) so dragging the Distance sliders or the Scene-view
        // gizmo handles (PicoInteractionControllerEditor below) moves the arrows live
        // while in Play mode - needed to tune arrowDistance* against the editor gaze
        // preview without a headset.
        void PositionArrows()
        {
            if (arrowLeft != null)  ((RectTransform)arrowLeft.transform).anchoredPosition  = new Vector2(-arrowDistanceLeft, 0f);
            if (arrowRight != null) ((RectTransform)arrowRight.transform).anchoredPosition = new Vector2(arrowDistanceRight, 0f);
            if (arrowUp != null)    ((RectTransform)arrowUp.transform).anchoredPosition    = new Vector2(0f, arrowDistanceUp);
            if (arrowDown != null)  ((RectTransform)arrowDown.transform).anchoredPosition  = new Vector2(0f, -arrowDistanceDown);
        }

        // Fixed angular radius around the target's actual world position — unlike
        // GetDirectionThreshold (arrow-pad), this isn't picking a dominant axis, just
        // "is the gaze within N degrees of this point in space".
        bool IsGazingAtTarget(Transform target, Vector3 worldGazeDir)
        {
            if (target == null || headTransform == null) return false;
            Vector3 toTarget = target.position - headTransform.position;
            return Vector3.Angle(worldGazeDir, toTarget) <= zoomActivationAngleDeg;
        }

#if UNITY_EDITOR
        // Canvas units -> meters and the canvas's depth, read from whichever arrow is wired
        // rather than assumed, so the gizmo always matches GazeSceneSetup's actual canvas
        // even if its depth/scale ever changes.
        internal bool TryGetCanvasDepthAndScale(out float depthMeters, out float worldScale)
        {
            Transform canvas = arrowLeft  != null ? arrowLeft.transform.parent
                             : arrowRight != null ? arrowRight.transform.parent
                             : arrowUp    != null ? arrowUp.transform.parent
                             : arrowDown  != null ? arrowDown.transform.parent
                             : null;
            if (canvas == null)
            {
                depthMeters = 1.2f;
                worldScale = 0.001f;
                return false;
            }
            depthMeters = canvas.localPosition.z;
            worldScale = canvas.lossyScale.x;
            return true;
        }

        // Drawn from current field values, not from the arrows' actual transforms - so this
        // updates live as you drag the Arrow Distance sliders, with no need to enter Play
        // mode (Awake() is what moves the real icons, and only runs on Play).
        void OnDrawGizmosSelected()
        {
            if (headTransform == null) return;
            TryGetCanvasDepthAndScale(out float depthMeters, out float worldScale);

            Vector3 origin  = headTransform.position;
            Vector3 basePos = origin + headTransform.forward * depthMeters;
            Vector3 right   = headTransform.right;
            Vector3 up      = headTransform.up;

            Vector3 c1 = basePos + right * (arrowDistanceRight * worldScale) + up * (arrowDistanceUp   * worldScale); // top-right
            Vector3 c2 = basePos + right * (arrowDistanceRight * worldScale) - up * (arrowDistanceDown * worldScale); // bottom-right
            Vector3 c3 = basePos - right * (arrowDistanceLeft  * worldScale) - up * (arrowDistanceDown * worldScale); // bottom-left
            Vector3 c4 = basePos - right * (arrowDistanceLeft  * worldScale) + up * (arrowDistanceUp   * worldScale); // top-left

            Gizmos.color = arrowHighlightColor;
            Gizmos.DrawLine(c1, c2);
            Gizmos.DrawLine(c2, c3);
            Gizmos.DrawLine(c3, c4);
            Gizmos.DrawLine(c4, c1);

            Color dim = arrowHighlightColor;
            dim.a = 0.3f;
            Gizmos.color = dim;
            Gizmos.DrawLine(origin, c1);
            Gizmos.DrawLine(origin, c2);
            Gizmos.DrawLine(origin, c3);
            Gizmos.DrawLine(origin, c4);
        }
#endif

        void Update()
        {
            PositionArrows();
            CompensateHudScaleForZoom();

            var getInfo = new EyeTrackingDataGetInfo
            {
                displayTime = 0,
                flags = EyeTrackingDataGetFlags.PXR_EYE_DEFAULT
                      | EyeTrackingDataGetFlags.PXR_EYE_POSITION
                      | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION
            };
            var data = new EyeTrackingData();
            bool haveEyeData = PXR_MotionTracking.GetEyeTrackingData(ref getInfo, ref data) == 0
                && headTransform != null
                && data.eyeDatas[(int)PerEyeUsage.Combined].isPoseValid != 0;

            if (haveEyeData)
            {
                UpdateScrollAndArrows(data.eyeDatas[(int)PerEyeUsage.Combined]);
            }
#if UNITY_EDITOR
            else if (editorPreviewSource == EditorPreviewSource.Keyboard && Keyboard.current != null)
            {
                GetKeyboardGazeComponents(out float rightComp, out float upComp);
                ApplyGazeComponents(rightComp, upComp);
                ResetZoomSphereDwell();
            }
            else if (editorPreviewSource == EditorPreviewSource.Mouse && Mouse.current != null)
            {
                GetMouseGazeComponents(out float rightComp, out float upComp);
                ApplyGazeComponents(rightComp, upComp);
                ResetZoomSphereDwell();
            }
#endif
            else
            {
                SetAllArrowAlpha(0f);
                ResetZoomSphereDwell();
            }

            UpdateWinkZoom();
        }

#if UNITY_EDITOR
        // Full deflection on key-down is plenty to clear the activation threshold - this is a
        // layout/color preview, not a precision input simulator.
        static void GetKeyboardGazeComponents(out float rightComp, out float upComp)
        {
            var kb = Keyboard.current;
            rightComp = kb.rightArrowKey.isPressed ? 1f : (kb.leftArrowKey.isPressed ? -1f : 0f);
            upComp    = kb.upArrowKey.isPressed    ? 1f : (kb.downArrowKey.isPressed ? -1f : 0f);
        }

        // Cursor offset from the center of the Game view, converted into the SAME
        // gain-adjusted angular quantity UpdateScrollAndArrows derives from real eye
        // data - not a raw linear screen fraction. A linear fraction doesn't match
        // GetDirectionThreshold's angle-based math and ignores the Game view's aspect
        // ratio, which is why arrows used to need wildly different mouse travel on
        // each axis to light up (confirmed: left/right needed overshoot past the
        // arrow, up/down lit at half the distance to it).
        void GetMouseGazeComponents(out float rightComp, out float upComp)
        {
            Vector2 pos = Mouse.current.position.ReadValue();
            float nx = Mathf.Clamp((pos.x - Screen.width  * 0.5f) / (Screen.width  * 0.5f), -1f, 1f);
            float ny = Mathf.Clamp((pos.y - Screen.height * 0.5f) / (Screen.height * 0.5f), -1f, 1f);

            Camera cam = proxyCamera != null ? proxyCamera : Camera.main;
            float aspect = cam != null ? cam.aspect : (Screen.width / (float)Screen.height);
            float halfFovY = (cam != null ? cam.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad;
            float halfFovX = Mathf.Atan(Mathf.Tan(halfFovY) * aspect);

            // Synthetic local gaze direction (yaw/pitch off forward), same convention
            // as UpdateScrollAndArrows' localDir, before gain.
            float yaw   = nx * halfFovX;
            float pitch = ny * halfFovY;
            Vector3 localDir = new Vector3(Mathf.Sin(yaw), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));

            // Same gain as the real path. No sign negation here (unlike
            // UpdateScrollAndArrows) - that negation only corrects PICO's mirrored
            // SDK output; this direction is authored directly so "mouse right" already
            // means "look right".
            if (gazeGain != 1f && localDir.z > 0.0001f)
                localDir = new Vector3(localDir.x * gazeGain, localDir.y * gazeGain, localDir.z).normalized;

            rightComp = localDir.x;
            upComp    = localDir.y;
        }
#endif

        void UpdateScrollAndArrows(PerEyeData combined)
        {
            // Reconstruct head-local gaze direction — same OpenXR->Unity conversion
            // as PicoEyeGazeProvider: negate quaternion Z/W on the LOCAL pose.
            var localRot = new Quaternion(
                combined.pose.orientation.x,
                combined.pose.orientation.y,
               -combined.pose.orientation.z,
               -combined.pose.orientation.w);
            Vector3 rawLocalDir = localRot * Vector3.forward;
            Vector3 localDir = rawLocalDir;

            // Same gain as PicoEyeGazeProvider's reticle, applied in the same local
            // space, so the D-pad needs the same comfortable eye travel as everything
            // else the user looks at instead of needing an exaggerated glance.
            if (gazeGain != 1f && localDir.z > 0.0001f)
                localDir = new Vector3(localDir.x * gazeGain, localDir.y * gazeGain, localDir.z).normalized;

            // NOT mirrored: localDir.x/y already read positive-right/positive-up as-is.
            // A previous version negated both here on the assumption PICO's combined
            // eye gaze came out mirrored - re-confirmed on-device (2026-07-22) that
            // WITH that negation, looking right lit the left arrow and looking up lit
            // the down arrow, i.e. the negation was itself the bug, not a fix for one.
            float rightComp = localDir.x;
            float upComp    = localDir.y;

            // Zoom spheres take priority over the arrow-pad. They sit off-center
            // enough that the arrow-pad's dominant-axis pick frequently also crosses
            // that arrow's own (much smaller) threshold, so instead of chasing exact
            // angles/positions to avoid every possible overlap, checking the spheres
            // first and suppressing the arrow-pad entirely while gazing at one
            // guarantees they never fight over the same glance.
            Vector3 worldGazeDir = headTransform.rotation * rawLocalDir;
            bool onZoomSphere = UpdateZoomSpheres(worldGazeDir);

            if (onZoomSphere)
            {
                SetArrowActive(arrowLeft,  false, m_BaseColorLeft);
                SetArrowActive(arrowRight, false, m_BaseColorRight);
                SetArrowActive(arrowUp,    false, m_BaseColorUp);
                SetArrowActive(arrowDown,  false, m_BaseColorDown);
            }
            else
            {
                ApplyGazeComponents(rightComp, upComp);
            }
        }

        // Returns true when gaze is on either zoom sphere, so the caller can
        // suppress the arrow-pad for this frame instead of letting both react to
        // the same glance.
        bool UpdateZoomSpheres(Vector3 worldGazeDir)
        {
            Camera cam = proxyCamera != null ? proxyCamera : Camera.main;
            if (cam == null) return false;

            bool onRight = IsGazingAtTarget(zoomSphereRight, worldGazeDir);
            bool onLeft  = IsGazingAtTarget(zoomSphereLeft, worldGazeDir);

            m_ZoomInDwellTimer  = onRight ? m_ZoomInDwellTimer  + Time.deltaTime : 0f;
            m_ZoomOutDwellTimer = onLeft  ? m_ZoomOutDwellTimer + Time.deltaTime : 0f;

            if (m_ZoomInDwellTimer >= zoomDwellSeconds)
                cam.fieldOfView = Mathf.Max(minFOV, cam.fieldOfView - zoomSpeed * Time.deltaTime);
            else if (m_ZoomOutDwellTimer >= zoomDwellSeconds)
                cam.fieldOfView = Mathf.Min(maxFOV, cam.fieldOfView + zoomSpeed * Time.deltaTime);

            SetSphereActive(zoomSphereRight, m_BaseColorZoomRight, m_ZoomInDwellTimer  >= zoomDwellSeconds);
            SetSphereActive(zoomSphereLeft,  m_BaseColorZoomLeft,  m_ZoomOutDwellTimer >= zoomDwellSeconds);

            return onRight || onLeft;
        }

        void SetSphereActive(Transform sphere, Color baseColor, bool active)
        {
            if (sphere == null) return;
            Renderer renderer = sphere.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.material.color = active ? zoomSphereHighlightColor : baseColor;
        }

        void ResetZoomSphereDwell()
        {
            m_ZoomInDwellTimer = 0f;
            m_ZoomOutDwellTimer = 0f;
            SetSphereActive(zoomSphereRight, m_BaseColorZoomRight, false);
            SetSphereActive(zoomSphereLeft,  m_BaseColorZoomLeft,  false);
        }

        void ApplyGazeComponents(float rightComp, float upComp)
        {
            // Pick the single dominant cardinal direction, like pressing ONE arrow on a
            // D-pad. Each side activates only once the gaze reaches THAT arrow's own
            // angle off head-forward - looking near the middle leaves all arrows idle.
            float absR = Mathf.Abs(rightComp);
            float absU = Mathf.Abs(upComp);

            bool right = false, left = false, up = false, down = false;
            if (absR >= absU)
            {
                if (rightComp > 0f)      right = rightComp  >= GetDirectionThreshold(arrowRight);
                else if (rightComp < 0f) left  = -rightComp >= GetDirectionThreshold(arrowLeft);
            }
            else
            {
                if (upComp > 0f)      up   = upComp  >= GetDirectionThreshold(arrowUp);
                else if (upComp < 0f) down = -upComp >= GetDirectionThreshold(arrowDown);
            }

            if (sphere != null)
            {
                // Constant speed while an arrow is held by gaze (no proportional ramp).
                float step = scrollSpeed * Time.deltaTime;

                // Horizontal scroll (unlimited). Reverted to this sign after the
                // 2026-07-22 fix above: the "inverted movement" reports that led to
                // flipping this were made while rightComp/upComp identification was
                // itself backwards (see above) - the two flips cancelled out and
                // looked like no change. Re-test this sign fresh now that
                // identification is fixed, one variable at a time.
                if (right) sphere.Rotate(Vector3.up, -step, Space.World);
                if (left)  sphere.Rotate(Vector3.up,  step, Space.World);

                // Vertical scroll with ±90° constraint (up = look up = content should pan up).
                if (up || down)
                {
                    float currentVertical = sphere.eulerAngles.x;
                    if (currentVertical > 180f) currentVertical -= 360f;
                    float dir = up ? step : -step;
                    float newVertical = Mathf.Clamp(currentVertical + dir, -90f, 90f);
                    float clampedDelta = newVertical - currentVertical;
                    sphere.Rotate(Vector3.right, clampedDelta, Space.World);
                }
            }

            SetArrowActive(arrowLeft,  left,  m_BaseColorLeft);
            SetArrowActive(arrowRight, right, m_BaseColorRight);
            SetArrowActive(arrowUp,    up,    m_BaseColorUp);
            SetArrowActive(arrowDown,  down,  m_BaseColorDown);
        }

        // The component value (post-gain) that the gaze SDK would report if the user were
        // looking exactly at this arrow - i.e. the arrow's real on-screen angle off
        // head-forward, run through the same gain math as the live gaze. This is what ties
        // the activation zone to wherever the arrow actually is, instead of a separate
        // number that could silently drift out of sync with it.
        float GetDirectionThreshold(Image arrowImg)
        {
            if (arrowImg == null || headTransform == null) return float.MaxValue;
            Vector3 toArrow = arrowImg.transform.position - headTransform.position;
            float angleRad = Vector3.Angle(headTransform.forward, toArrow) * Mathf.Deg2Rad;
            return AngleToThreshold(angleRad, gazeGain);
        }

        static float AngleToThreshold(float angleRad, float gain)
        {
            float s = Mathf.Sin(angleRad);
            float c = Mathf.Cos(angleRad);
            float norm = Mathf.Sqrt(gain * gain * s * s + c * c);
            return norm < 0.0001f ? 1f : Mathf.Clamp01(gain * s / norm);
        }

        void UpdateWinkZoom()
        {
            if (!winkZoomEnabled) return;

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

        void SetArrowActive(Image img, bool active, Color baseColor)
        {
            if (img == null) return;

            // At rest: dim hint in its authored color. Active (gaze on this direction):
            // switch to the highlight color, not just a higher alpha, so it visibly lights up.
            Color c = active ? arrowHighlightColor : baseColor;
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

#if UNITY_EDITOR
    // Lets you drag each side of the gizmo box in the Scene view to set the matching Arrow
    // Distance value directly, instead of dialing in numbers on the Inspector sliders blind.
    [CustomEditor(typeof(PicoInteractionController))]
    public class PicoInteractionControllerEditor : Editor
    {
        void OnSceneGUI()
        {
            var ctrl = (PicoInteractionController)target;
            var headProp = serializedObject.FindProperty("headTransform");
            Transform head = headProp.objectReferenceValue as Transform;
            if (head == null) return;

            var leftProp  = serializedObject.FindProperty("arrowDistanceLeft");
            var rightProp = serializedObject.FindProperty("arrowDistanceRight");
            var upProp    = serializedObject.FindProperty("arrowDistanceUp");
            var downProp  = serializedObject.FindProperty("arrowDistanceDown");
            var colorProp = serializedObject.FindProperty("arrowHighlightColor");

            ctrl.TryGetCanvasDepthAndScale(out float depthMeters, out float worldScale);
            Vector3 basePos = head.position + head.forward * depthMeters;

            Handles.color = colorProp.colorValue;

            DragAxisHandle(basePos, head.right, -1f, leftProp,  worldScale);
            DragAxisHandle(basePos, head.right,  1f, rightProp, worldScale);
            DragAxisHandle(basePos, head.up,     1f, upProp,    worldScale);
            DragAxisHandle(basePos, head.up,    -1f, downProp,  worldScale);

            serializedObject.ApplyModifiedProperties();
        }

        static void DragAxisHandle(Vector3 basePos, Vector3 axis, float sign, SerializedProperty distProp, float worldScale)
        {
            Vector3 dir = axis * sign;
            Vector3 pos = basePos + dir * (distProp.floatValue * worldScale);
            float handleSize = HandleUtility.GetHandleSize(pos) * 0.08f;

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.Slider(pos, dir, handleSize, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float newDistMeters = Vector3.Dot(newPos - basePos, dir);
                distProp.floatValue = Mathf.Clamp(newDistMeters / worldScale, 50f, 500f);
            }
        }
    }
#endif
}
