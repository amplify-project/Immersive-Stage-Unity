using UnityEngine;

namespace Gaze.Core
{
    /// <summary>
    /// Button relay: flips a GameObject's active state (UnityEvents can't toggle
    /// SetActive without a fixed bool argument). Used for Show/Hide debug log.
    /// </summary>
    public class GameObjectToggle : MonoBehaviour
    {
        [SerializeField] GameObject target;

        public void Toggle()
        {
            if (target != null)
                target.SetActive(!target.activeSelf);
        }
    }
}
