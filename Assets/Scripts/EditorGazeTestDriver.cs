using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
using UnityEngine.InputSystem;
#endif

/// <summary>
/// EDITOR-ONLY test harness to try the gaze / closeup / audio-isolation flow in the
/// Game view without a headset. Drop it on any GameObject in the scene (e.g. the
/// "Gaze Interaction" rig) and press Play.
///
/// It does two things, both only in the editor:
///  1. Mouse-look: hold Right Mouse and drag to rotate the active gaze camera -
///     this is your "head". Sweep until the Musician reticle appears / a stem
///     isolates, then hold to open the window.
///  2. Silences PlatformManager's tablet-mode UpdateAudioFocus (which otherwise
///     writes AudioSource.volume every frame and fights MusicianAudioFocus).
///
/// The whole body compiles out of device builds, so it is safe to leave attached.
/// </summary>
public class EditorGazeTestDriver : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Mouse-look (stands in for head movement)")]
    [Tooltip("Camera the gaze ray comes from. Leave empty to auto-resolve the active camera (same one the gaze system uses).")]
    [SerializeField] Camera gazeCamera;

    [Tooltip("Degrees per pixel of mouse movement.")]
    [SerializeField] float sensitivity = 0.15f;

    [Tooltip("If true, only rotate while Right Mouse is held. If false, mouse always steers.")]
    [SerializeField] bool holdRightMouse = true;

    [Tooltip("Max look up/down angle, degrees.")]
    [SerializeField] float pitchClamp = 85f;

    [Header("Fixes")]
    [Tooltip("Force PlatformManager out of tablet mode so its UpdateAudioFocus stops overriding MusicianAudioFocus.")]
    [SerializeField] bool silencePlatformAudioFocus = true;

    float m_Yaw;
    float m_Pitch;
    bool m_Init;
    bool m_Silenced;

    void Update()
    {
        // Do this on the first Update, not Start: Unity runs every Start() before any
        // Update(), so PlatformManager has already set isTablet by now and we win.
        if (silencePlatformAudioFocus && !m_Silenced)
        {
            SilencePlatformTabletAudio();
            m_Silenced = true;
        }

        Camera cam = ResolveCamera();
        if (cam == null) return;

        // Seed yaw/pitch from the camera's current orientation so it doesn't jump.
        if (!m_Init)
        {
            Vector3 e = cam.transform.rotation.eulerAngles;
            m_Yaw = e.y;
            m_Pitch = NormalizePitch(e.x);
            m_Init = true;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        bool steering = !holdRightMouse || mouse.rightButton.isPressed;
        if (!steering) return;

        Vector2 delta = mouse.delta.ReadValue();
        m_Yaw += delta.x * sensitivity;
        m_Pitch = Mathf.Clamp(m_Pitch - delta.y * sensitivity, -pitchClamp, pitchClamp);
        cam.transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
    }

    Camera ResolveCamera()
    {
        if (gazeCamera != null && gazeCamera.isActiveAndEnabled) return gazeCamera;
        if (Camera.main != null && Camera.main.isActiveAndEnabled) return Camera.main;
        return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
    }

    static float NormalizePitch(float x)
    {
        // Euler x comes back 0..360; map to -180..180 so the clamp behaves.
        return x > 180f ? x - 360f : x;
    }

    // isTablet is private in PlatformManager; flip it via reflection (editor-only,
    // so keeping this out of the shared platform file). Guarded so a rename just
    // warns instead of throwing.
    void SilencePlatformTabletAudio()
    {
        PlatformManager pm = FindObjectOfType<PlatformManager>();
        if (pm == null)
        {
            Debug.LogWarning("[EditorGazeTestDriver] No PlatformManager found - cannot silence tablet audio focus.");
            return;
        }

        FieldInfo field = typeof(PlatformManager).GetField("isTablet", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogWarning("[EditorGazeTestDriver] PlatformManager.isTablet not found (renamed?) - its UpdateAudioFocus may fight MusicianAudioFocus.");
            return;
        }

        field.SetValue(pm, false);
        Debug.Log("[EditorGazeTestDriver] Forced PlatformManager.isTablet=false so tablet UpdateAudioFocus won't override the gaze audio isolation.");
    }
#endif
}
