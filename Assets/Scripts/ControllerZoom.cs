using UnityEngine;

/// <summary>
/// No-zoom variant for the feature_noZoom_CloseUp_AisleSound branch.
///
/// Zoom is intentionally removed here: neither the controllers nor gaze move the
/// view in/out. The proxy (render) camera is pinned to the arm origin so you see
/// the 360 video from head height with no zoom offset, and the arm just tracks
/// head yaw so you can look around. Audio isolation is handled separately by
/// <see cref="Gaze.Core.MusicianAudioFocus"/>.
///
/// The class name is kept so the existing scene component keeps its reference.
/// </summary>
public class ControllerZoom : MonoBehaviour
{
    [Header("Camera / Arm")]
    [Tooltip("The head-tracked XR camera whose rotation the arm follows.")]
    public Camera vrCamera;
    [Tooltip("The render camera on the arm. Pinned to the arm origin (no zoom).")]
    public Camera proxyCamera;
    [Tooltip("Arm root rotated to match head yaw so you can look around.")]
    public Transform armRoot;

    void Start()
    {
        // No zoom: keep the render camera at the arm origin (no forward offset).
        if (proxyCamera != null)
            proxyCamera.transform.localPosition = Vector3.zero;
    }

    void LateUpdate()
    {
        if (armRoot != null && vrCamera != null)
            armRoot.rotation = vrCamera.transform.rotation;
    }
}
