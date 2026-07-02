using UnityEngine;
using System.Collections.Generic;

public class ControllerZoom : MonoBehaviour
{   
    [Header("Camera")]
    public Camera vrCamera;
    public Camera mainCamera;
    public Camera proxyCamera;
    public float zoomSpeed = 1.0f;
    public float minFOV = 30f;
    public float maxFOV = 120f;
    public float currZoom = 30f;

    [Header("Gaze-driven zoom")]
    [Tooltip("Wide 'resting' FOV used while no musician is focused. Higher = further out.")]
    public float defaultFOV = 90f;
    [Tooltip("FOV to ease into while a musician closeup is open. Lower = closer in.")]
    public float focusedFOV = 30f;
    [Tooltip("Zoom transition speed (exponential smoothing). Lower = slower, gentler zoom. ~1 ≈ 2s, ~3 ≈ 0.8s.")]
    [Range(0.3f, 5f)]
    public float zoomLerpSpeed = 1f;

    [Header("Arm Control")]
    public Transform armRoot;
    public float maxDistance = 25f;
    
    [Header("Error Logging")]
    public bool isLoggingActive;
    public GameObject logTextPrefab;
    public GameObject logContentRoot;

    [Header("Audio Sources")]
    public GameObject audioSourceObject;
    private List<AudioSource> audioSources = new List<AudioSource>();

    private Quaternion lastCameraRot;
    private bool firstFrame = true;

    // Target the zoom eases toward. Flipped by the gaze system, not the controllers.
    private float targetFOV;

    void OnEnable()
    {
        Gaze.Core.GazeWorldRaycaster.MusicianFocusChanged += OnMusicianFocusChanged;
    }

    void OnDisable()
    {
        Gaze.Core.GazeWorldRaycaster.MusicianFocusChanged -= OnMusicianFocusChanged;
    }

    // Gaze opened/closed a musician closeup: pick the zoom target, Update() eases to it.
    void OnMusicianFocusChanged(bool focused)
    {
        targetFOV = focused ? focusedFOV : defaultFOV;
    }

    void Start()
    {
        // Start wide; zoom is now driven entirely by musician gaze focus.
        currZoom = defaultFOV;
        targetFOV = defaultFOV;
        ApplyZoom();

        foreach (Transform child in audioSourceObject.transform)
        {
            AudioSource source = child.GetComponent<AudioSource>();

            if (source != null)
            {
                audioSources.Add(source);
            }
        }
    }

    void Update()
    {
        if (Mathf.Abs(currZoom - targetFOV) > 0.01f)
        {
            // Frame-rate independent exponential smoothing toward the target FOV.
            float t = 1f - Mathf.Exp(-zoomLerpSpeed * Time.deltaTime);
            currZoom = Mathf.Lerp(currZoom, targetFOV, t);
            ApplyZoom();
        }

        UpdateAudioFocus();  // RE-ENABLED (old behaviour): ducks every stem except the
        // one you face down to sliderValue (~0.1), so effectively ONE instrument is
        // audible and the focus cone is driven by zoom (currZoom), not gaze alone. This
        // is the version that was turned off for burying the others — kept as-is for a
        // live test. Pending redesign: high floor (~0.7) and decouple from zoom.
    }

    void LateUpdate()
    {
        Quaternion L = vrCamera.transform.rotation;

        armRoot.transform.rotation = L;
    }

    void LogAll()
    {
        Debug.Log("ArmRoot     " + armRoot.rotation.eulerAngles + "   local " + armRoot.localRotation.eulerAngles);
        Debug.Log("VRCamera    " + vrCamera.transform.rotation.eulerAngles + "   local " + vrCamera.transform.localRotation.eulerAngles);
        Debug.Log("Proxy Camera    " + proxyCamera.transform.rotation.eulerAngles + "   local " + proxyCamera.transform.localRotation.eulerAngles);
        Debug.Log("----------------------------------------------------------");
    }

    // Maps the current FOV to the proxy camera arm distance (min FOV = closest in).
    void ApplyZoom()
    {
        float fovNormalized = Mathf.InverseLerp(maxFOV, minFOV, currZoom);
        float distance = Mathf.Lerp(0f, maxDistance, fovNormalized);
        proxyCamera.transform.localPosition = Vector3.forward * distance;
    }

    void UpdateAudioFocus()
    {
        float normalized = Mathf.InverseLerp(minFOV, maxFOV, currZoom);
        float sliderValue = Mathf.Lerp(0.1f, 1.0f, normalized);

        Vector3 listenerPos = vrCamera.transform.position;
        Vector3 forward = vrCamera.transform.forward;

        foreach (AudioSource src in audioSources)
        {
            Vector3 toSource = (src.transform.position - listenerPos).normalized;
            float angle = Vector3.Angle(forward, toSource);

            float boost = Mathf.Clamp01((currZoom - angle) / currZoom);
            src.volume = Mathf.Clamp(1 * boost, sliderValue, 1.0f);
        }
    }
}
