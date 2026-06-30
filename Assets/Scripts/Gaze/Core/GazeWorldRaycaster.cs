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

        MusicianCloseupScreen m_Current;
        float m_DwellTimer;

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
            if (Physics.Raycast(ray, out RaycastHit info, rayDistance, musicianLayerMask, QueryTriggerInteraction.Collide))
                hit = info.collider.GetComponentInParent<MusicianCloseupScreen>();

            if (hit != m_Current)
            {
                if (m_Current != null)
                    m_Current.Hide();
                m_Current = hit;
                m_DwellTimer = 0f;
            }

            if (m_Current == null || m_Current.IsShowing)
                return;

            m_DwellTimer += Time.unscaledDeltaTime;
            if (m_DwellTimer >= dwellSeconds)
                m_Current.Show();
        }

        void LoseFocus()
        {
            if (m_Current != null)
            {
                m_Current.Hide();
                m_Current = null;
            }
            m_DwellTimer = 0f;
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
