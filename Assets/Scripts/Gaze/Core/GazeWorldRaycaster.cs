using UnityEngine;

namespace Gaze.Core
{
    /// <summary>
    /// Physics.Raycast companion to GazeDwellUIClicker for 3D world objects.
    /// Fires a ray each frame using the same gaze provider chain; when the ray
    /// hits a MusicianCloseupScreen collider for dwellSeconds, calls Show().
    /// Calls Hide() immediately when gaze leaves or data is lost.
    /// Add this to the same GameObject as GazeDwellUIClicker.
    /// </summary>
    [DefaultExecutionOrder(101)]
    public class GazeWorldRaycaster : MonoBehaviour
    {
        [Tooltip("Seconds of continuous gaze required to open the closeup screen.")]
        [SerializeField] float dwellSeconds = 1f;

        [Tooltip("Max raycast distance in world units.")]
        [SerializeField] float rayDistance = 80f;

        [Tooltip("Only colliders on these layers are tested. Assign a dedicated 'Musician' layer here.")]
        [SerializeField] LayerMask musicianLayerMask = ~0;

        [Tooltip("Head-gaze fallback for platforms without eye tracking (Quest, Editor). Auto-added if empty.")]
        [SerializeField] HeadGazeProvider headGazeFallback;

        [Tooltip("Optional reticle shown at the hit point while gazing at a musician.")]
        [SerializeField] MusicianReticle musicianReticle;

        /// <summary>
        /// Fires true when a musician closeup screen opens (dwell completed) and
        /// false when focus is lost. Consumed by ControllerZoom (default assembly)
        /// to drive the zoom, so Gaze.Core stays free of any zoom dependency.
        /// </summary>
        public static event System.Action<bool> MusicianFocusChanged;

        MusicianCloseupScreen m_Current;
        float m_DwellTimer;
        Camera m_Camera;
        bool m_ZoomActive;

        void Awake()
        {
            if (headGazeFallback == null)
            {
                headGazeFallback = GetComponent<HeadGazeProvider>();
                if (headGazeFallback == null)
                    headGazeFallback = gameObject.AddComponent<HeadGazeProvider>();
            }
        }

        void Update()
        {
            if (!TryGetGazeRay(out Ray ray))
            {
                LoseFocus();
                return;
            }

            MusicianCloseupScreen hit = null;
            Vector3 hitPoint = Vector3.zero;
            if (Physics.Raycast(ray, out RaycastHit info, rayDistance, musicianLayerMask, QueryTriggerInteraction.Collide))
            {
                hit = info.collider.GetComponentInParent<MusicianCloseupScreen>();
                hitPoint = info.point;
            }

            if (musicianReticle != null)
            {
                if (hit != null) musicianReticle.SetState(hitPoint, ResolveCamera());
                else             musicianReticle.Hide();
            }

            if (hit != null)
                Debug.Log($"[GazeWorldRaycaster] hit={hit.name} point={hitPoint} reticle={(musicianReticle != null ? "assigned" : "NULL")}");
            Debug.DrawRay(ray.origin, ray.direction * rayDistance, hit != null ? Color.green : Color.red);

            if (hit != m_Current)
            {
                if (m_Current != null)
                {
                    m_Current.Hide();
                    SetZoomFocus(false);
                }
                m_Current = hit;
                m_DwellTimer = 0f;
            }

            if (m_Current == null || m_Current.IsShowing)
                return;

            m_DwellTimer += Time.unscaledDeltaTime;
            if (m_DwellTimer >= dwellSeconds)
            {
                m_Current.Show();
                SetZoomFocus(true);
            }
        }

        void LoseFocus()
        {
            if (m_Current != null)
            {
                m_Current.Hide();
                m_Current = null;
            }
            m_DwellTimer = 0f;
            musicianReticle?.Hide();
            SetZoomFocus(false);
        }

        // Only broadcasts on state change, so switching musicians (Hide old ->
        // dwell -> Show new) briefly drops focus, letting the zoom ease back out
        // and in rather than jumping between two focused positions.
        void SetZoomFocus(bool active)
        {
            if (m_ZoomActive == active) return;
            m_ZoomActive = active;
            MusicianFocusChanged?.Invoke(active);
        }

        Camera ResolveCamera()
        {
            if (m_Camera != null && m_Camera.isActiveAndEnabled) return m_Camera;
            m_Camera = Camera.main ?? (Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null);
            return m_Camera;
        }

        bool TryGetGazeRay(out Ray ray)
        {
            if (GazeDwellUIClicker.TryGetRegisteredProviderRay(out ray))
                return true;
            if (headGazeFallback != null)
                return headGazeFallback.TryGetGazeRay(out ray);
            ray = default;
            return false;
        }
    }
}
