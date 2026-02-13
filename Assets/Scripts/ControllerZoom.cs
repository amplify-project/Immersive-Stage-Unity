using UnityEngine;
using System.Collections.Generic;
using Unity.XR.CoreUtils.Bindings;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using TMPro;

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

    [Header("Controller Actions")]
    public InputActionReference m_LeftActivateValue;
    public InputActionReference m_RightActivateValue;

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

    private InputAction leftActivateModeAction;
    private InputAction rightActivateModeAction;

    private Quaternion lastCameraRot;
    private bool firstFrame = true;

    void Start()
    {
        leftActivateModeAction = GetInputAction(m_LeftActivateValue);
        rightActivateModeAction = GetInputAction(m_RightActivateValue);

        if (leftActivateModeAction != null)
            leftActivateModeAction.Enable();

        if (rightActivateModeAction != null)
            rightActivateModeAction.Enable();

        float fovNormalized = Mathf.InverseLerp(maxFOV, minFOV, currZoom);
        float distance = Mathf.Lerp(0f, maxDistance, fovNormalized);
        proxyCamera.transform.localPosition = Vector3.forward * distance;
        
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
        if (leftActivateModeAction != null && leftActivateModeAction.ReadValue<float>() > 0.1f)
        {
            OnZoom(leftActivateModeAction.ReadValue<float>());
        }

        if (rightActivateModeAction != null && rightActivateModeAction.ReadValue<float>() > 0.1f)
        {
            OnZoom(-rightActivateModeAction.ReadValue<float>());
        }

        UpdateAudioFocus();
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

    void OnZoom(float triggerValue)
    {
        currZoom = Mathf.Clamp(
            currZoom + triggerValue * zoomSpeed * Time.deltaTime,
            minFOV,
            maxFOV
        );

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

    static InputAction GetInputAction(InputActionReference actionReference)
    {
#pragma warning disable IDE0031
        return actionReference != null ? actionReference.action : null;
#pragma warning restore IDE0031
    }
}
