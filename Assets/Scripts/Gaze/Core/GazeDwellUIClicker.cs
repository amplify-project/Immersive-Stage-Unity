using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gaze.Core
{
    /// <summary>
    /// Gaze-dwell engine: looking at any uGUI element that has an
    /// IPointerClickHandler (e.g. a Button) for <see cref="dwellSeconds"/>
    /// triggers a click. Existing Buttons and the EventSystem are untouched;
    /// events are driven directly through ExecuteEvents. Elements without a
    /// click handler (debug scroll, video sphere) are ignored automatically.
    ///
    /// Platform gaze sources plug in at runtime via <see cref="RegisterProvider"/>
    /// (highest priority valid ray wins); without one, head gaze is used.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class GazeDwellUIClicker : MonoBehaviour
    {
        public const int GazePointerId = 0x6A7E;

        [Tooltip("Head-gaze fallback used when no platform provider supplies a valid ray. Auto-added if empty.")]
        [SerializeField] HeadGazeProvider fallbackProvider;

        [Tooltip("Camera that renders the user's view (Proxy Camera in VR, standard Camera on tablet). Auto-resolved when empty or inactive.")]
        [SerializeField] Camera projectionCamera;

        [SerializeField] GazeReticle reticle;

        [Tooltip("Seconds of continuous gaze on a clickable element before the click fires.")]
        [SerializeField] float dwellSeconds = 3f;

        [Tooltip("Default reticle distance when the gaze hits nothing.")]
        [SerializeField] float rayDistance = 25f;

        static readonly List<(IGazeRayProvider provider, int priority)> s_Providers = new List<(IGazeRayProvider, int)>();

        readonly List<RaycastResult> m_RaycastResults = new List<RaycastResult>();
        readonly List<Canvas> m_WorldCanvases = new List<Canvas>();

        PointerEventData m_PointerData;
        GameObject m_HoverTarget;   // receives pointerEnter/Exit (keeps Button highlight states working)
        GameObject m_ClickTarget;   // object owning the IPointerClickHandler
        float m_DwellTimer;
        bool m_Armed = true;        // a completed click re-arms only after gaze leaves the target
        float m_NextCanvasScan;
        bool m_UseHeadGazeFallback = true; // platforms with real eye tracking (Pico) turn this off

        /// <summary>
        /// Eyes-only platforms (Pico) call this to suppress the head-gaze fallback:
        /// when eye data is invalid the reticle hides instead of snapping to
        /// head-forward. visionOS leaves it enabled (head gaze is its only source).
        /// </summary>
        public void SetHeadGazeFallbackEnabled(bool enabled)
        {
            m_UseHeadGazeFallback = enabled;
        }

        /// <summary>
        /// Called by platform assemblies (Gaze.Pico, Gaze.VisionOS) so Core never
        /// references them. Higher priority wins; head-gaze fallback is implicit.
        /// </summary>
        public static void RegisterProvider(IGazeRayProvider provider, int priority)
        {
            if (provider == null)
                return;
            s_Providers.RemoveAll(p => p.provider == provider);
            s_Providers.Add((provider, priority));
            s_Providers.Sort((a, b) => b.priority.CompareTo(a.priority));
        }

        public static void UnregisterProvider(IGazeRayProvider provider)
        {
            s_Providers.RemoveAll(p => p.provider == provider);
        }

        /// <summary>Clicks the currently hovered element immediately (e.g. visionOS pinch shortcut).</summary>
        public void TriggerImmediateClick()
        {
            if (m_ClickTarget != null && m_Armed)
                FireClick();
        }

        void Awake()
        {
            if (fallbackProvider == null)
            {
                fallbackProvider = GetComponent<HeadGazeProvider>();
                if (fallbackProvider == null)
                    fallbackProvider = gameObject.AddComponent<HeadGazeProvider>();
            }
        }

        void Update()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return;

            Camera cam = ResolveProjectionCamera();
            if (cam == null || !TryGetGazeRay(out Ray gazeRay))
            {
                SetHoverTarget(null, null);
                if (reticle != null)
                    reticle.Hide();
                return;
            }

            EnsureWorldCanvasCameras(cam);

            Vector3 screenPoint = cam.WorldToScreenPoint(gazeRay.origin + gazeRay.direction * Mathf.Max(1f, rayDistance * 0.5f));
            bool inFront = screenPoint.z > 0f;

            if (m_PointerData == null)
                m_PointerData = new PointerEventData(eventSystem) { pointerId = GazePointerId };
            m_PointerData.position = screenPoint;

            RaycastResult topResult = default;
            if (inFront)
            {
                m_RaycastResults.Clear();
                eventSystem.RaycastAll(m_PointerData, m_RaycastResults);
                if (m_RaycastResults.Count > 0)
                    topResult = m_RaycastResults[0];
            }

            GameObject hover = null;
            GameObject click = null;
            if (topResult.gameObject != null)
            {
                hover = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(topResult.gameObject);
                click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(topResult.gameObject);
            }
            m_PointerData.pointerCurrentRaycast = topResult;

            SetHoverTarget(hover, click);

            // Dwell accumulation on clickable targets only.
            float progress = 0f;
            if (m_ClickTarget != null && m_Armed)
            {
                m_DwellTimer += Time.unscaledDeltaTime;
                progress = Mathf.Clamp01(m_DwellTimer / Mathf.Max(0.01f, dwellSeconds));
                if (m_DwellTimer >= dwellSeconds)
                    FireClick();
            }

            UpdateReticle(gazeRay, topResult, cam, progress);
        }

        void SetHoverTarget(GameObject hover, GameObject click)
        {
            if (hover == m_HoverTarget)
            {
                // Same visual hover; click target may legitimately differ only when hover changed.
                return;
            }

            if (m_HoverTarget != null && m_PointerData != null)
                ExecuteEvents.Execute(m_HoverTarget, m_PointerData, ExecuteEvents.pointerExitHandler);

            m_HoverTarget = hover;
            m_ClickTarget = click;
            m_DwellTimer = 0f;
            m_Armed = true;

            if (m_HoverTarget != null && m_PointerData != null)
                ExecuteEvents.Execute(m_HoverTarget, m_PointerData, ExecuteEvents.pointerEnterHandler);
        }

        void FireClick()
        {
            // Down/up first so Button pressed-state transitions play, then the click.
            ExecuteEvents.Execute(m_ClickTarget, m_PointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(m_ClickTarget, m_PointerData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(m_ClickTarget, m_PointerData, ExecuteEvents.pointerClickHandler);

            m_DwellTimer = 0f;
            m_Armed = false; // stare-and-hold must not repeat the click; re-arms on gaze exit
        }

        bool TryGetGazeRay(out Ray ray)
        {
            for (int i = 0; i < s_Providers.Count; i++)
            {
                if (s_Providers[i].provider.TryGetGazeRay(out ray))
                {
                    Debug.Log($"[GazeDwellUIClicker] Using provider: {s_Providers[i].provider.GetType().Name} (priority {s_Providers[i].priority})");
                    return true;
                }
            }
            if (m_UseHeadGazeFallback && fallbackProvider != null)
            {
                bool got = fallbackProvider.TryGetGazeRay(out ray);
                if (got)
                    Debug.Log("[GazeDwellUIClicker] Using fallback HeadGazeProvider");
                return got;
            }
            ray = default;
            return false;
        }

        Camera ResolveProjectionCamera()
        {
            if (projectionCamera != null && projectionCamera.isActiveAndEnabled)
                return projectionCamera;
            if (Camera.main != null && Camera.main.isActiveAndEnabled)
                return Camera.main;
            return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
        }

        /// <summary>
        /// World-space canvases need an event camera for GraphicRaycaster. Keep them
        /// pointed at the camera that actually renders, surviving the rig switch
        /// PlatformManager performs at startup.
        /// </summary>
        void EnsureWorldCanvasCameras(Camera cam)
        {
            if (Time.unscaledTime >= m_NextCanvasScan)
            {
                m_NextCanvasScan = Time.unscaledTime + 2f;
                m_WorldCanvases.Clear();
                foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (canvas.renderMode == RenderMode.WorldSpace)
                        m_WorldCanvases.Add(canvas);
                }
            }

            for (int i = 0; i < m_WorldCanvases.Count; i++)
            {
                Canvas canvas = m_WorldCanvases[i];
                if (canvas != null && canvas.worldCamera != cam)
                    canvas.worldCamera = cam;
            }
        }

        void UpdateReticle(Ray gazeRay, RaycastResult topResult, Camera cam, float progress)
        {
            if (reticle == null)
                return;

            Vector3 position;
            if (topResult.gameObject != null)
            {
                // Intersect the gaze ray with the hit canvas plane for a stable surface position.
                Canvas hitCanvas = topResult.gameObject.GetComponentInParent<Canvas>();
                Transform plane = hitCanvas != null ? hitCanvas.transform : topResult.gameObject.transform;
                var canvasPlane = new Plane(-plane.forward, plane.position);
                position = canvasPlane.Raycast(gazeRay, out float enter)
                    ? gazeRay.GetPoint(Mathf.Max(0.1f, enter - 0.05f))
                    : gazeRay.GetPoint(rayDistance);
            }
            else
            {
                position = gazeRay.GetPoint(rayDistance);
            }

            reticle.SetState(position, cam, progress, m_ClickTarget != null);
        }
    }
}
