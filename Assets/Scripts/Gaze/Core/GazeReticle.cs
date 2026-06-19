using UnityEngine;
using UnityEngine.UI;

namespace Gaze.Core
{
    /// <summary>
    /// World-space gaze reticle: a small ring that follows the gaze point and a
    /// radial fill showing dwell progress. Keeps constant angular size and never
    /// blocks raycasts (images must have raycastTarget off).
    /// </summary>
    public class GazeReticle : MonoBehaviour
    {
        [Tooltip("Radial Image (type Filled / Radial360) showing dwell progress.")]
        [SerializeField] Image progressImage;

        [Tooltip("Ring/dot Image always visible while gaze is active.")]
        [SerializeField] Image ringImage;

        [Tooltip("Apparent size: world scale per meter of distance from the camera.")]
        [SerializeField] float angularScale = 0.02f;

        public void SetState(Vector3 worldPosition, Camera facingCamera, float progress, bool hovering)
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            transform.position = worldPosition;

            if (facingCamera != null)
            {
                transform.rotation = Quaternion.LookRotation(worldPosition - facingCamera.transform.position);
                float distance = Vector3.Distance(worldPosition, facingCamera.transform.position);
                transform.localScale = Vector3.one * (distance * angularScale);
            }

            if (progressImage != null)
            {
                progressImage.enabled = hovering;
                progressImage.fillAmount = progress;
            }

            if (ringImage != null)
                ringImage.enabled = true;
        }

        public void Hide()
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }
    }
}
