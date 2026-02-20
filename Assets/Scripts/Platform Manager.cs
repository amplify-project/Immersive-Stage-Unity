using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Android;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using TMPro;
using UnityEngine.XR;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.Threading.Tasks;

#if UNITY_IOS
using UnityEngine.EventSystems;
#endif

public class PlatformManager : MonoBehaviour
{
    [Header("Video Player")]
    public VideoPlayer videoPlayer;

    [Header("Camera Rigs")]
    public GameObject xrOrigin;
    public Camera proxyCamera;
    public Camera standardCamera;

    [Header("iPad Controls")]
    public float rotationSpeed = 0.2f;
    public float zoomSpeed = 0.2f;
    public float minFOV = 30f;
    public float maxFOV = 90f;
    public bool gyroEnabled = false;

    [Header("Sync Settings")]
    public double syncThreshold = 0.1;
    public double maxDrift = 0.5;
    public double oculusAudioLatencyOffset = 0.08;

    [Header("Error Logging")]
    public bool isLoggingActive;
    public GameObject logTextPrefab;
    public GameObject logContentRoot;

    [Header("Audio Sources")]
    public GameObject audioSourceObject;
    private List<AudioSource> audioSources = new List<AudioSource>();
    private List<string> audioPaths = new List<string>();

    private Vector2 lastTouchPos;
    private string relativePath;
    private bool isTablet;
    private Quaternion baseWorldRotation;

    async void Start()
    {
        isTablet = true;

        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            Permission.RequestUserPermission(Permission.ExternalStorageRead);

        foreach (Transform child in audioSourceObject.transform)
        {
            AudioSource source = child.GetComponent<AudioSource>();

            if (source != null)
            {
                audioSources.Add(source);
                string filename = source.gameObject.name + ".wav";
                audioPaths.Add(filename);
            }
        }

        string model = SystemInfo.deviceModel.ToLower();
        if (model.Contains("quest"))
            isTablet = false;

        if (XRSettings.isDeviceActive && XRSettings.loadedDeviceName.ToLower().Contains("oculus"))
            isTablet = false;

        string videoPath;
        string mPath = Application.streamingAssetsPath;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!isTablet)
        {
            if (xrOrigin != null) xrOrigin.SetActive(true);
            if (proxyCamera != null) proxyCamera.gameObject.SetActive(true);
            if (standardCamera != null) standardCamera.gameObject.SetActive(false);
            videoPath = "Pisa2Concert360_4k.mp4";
            videoPath = "file://" + System.IO.Path.Combine(Application.persistentDataPath, videoPath);
        }
        else
        {
            if (xrOrigin != null) xrOrigin.SetActive(false);
            if (proxyCamera != null) proxyCamera.gameObject.SetActive(false);
            if (standardCamera != null) standardCamera.gameObject.SetActive(true);
            videoPath = "Pisa2Concert360.mp4";
            videoPath = "file://" + "/storage/emulated/0/Movies/" + videoPath;
        }
#elif UNITY_IOS
        if (xrOrigin != null) xrOrigin.SetActive(false);
        if (standardCamera != null) standardCamera.gameObject.SetActive(true);
        videoPath = System.IO.Path.Combine(Application.persistentDataPath, "Pisa360_8K.mp4");
#else
        if (xrOrigin != null) xrOrigin.SetActive(false);
        if (standardCamera != null) standardCamera.gameObject.SetActive(true);
        videoPath = "video.mp4";
        videoPath = "file://" + System.IO.Path.Combine(Application.persistentDataPath, videoPath);
#endif

        GameObject instance = null;

        if (audioSources.Count == audioPaths.Count)
        {
            for (int i = 0; i < audioSources.Count; i++)
            {
                string filePath;
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!isTablet)
                {
                    filePath = "file://" + System.IO.Path.Combine(Application.persistentDataPath, audioPaths[i]);
                }
                else
                {
                    filePath = "file://" + "/storage/emulated/0/Music/" + audioPaths[i];
                }
#else
                filePath = "file://" + System.IO.Path.Combine(Application.persistentDataPath, audioPaths[i]);
#endif
                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(filePath, AudioType.WAV))
                {
                    var operation = www.SendWebRequest();

                    while (!operation.isDone)
                        await Task.Yield();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                        if (clip != null)
                        {
                            audioSources[i].clip = clip;
                            audioSources[i].loop = true;
                            audioSources[i].spatialize = true;
                            audioSources[i].spatialBlend = 1f;
                            audioSources[i].volume = 1f;
                            Debug.Log("Audio loaded: " + filePath);
                        }
                        else
                        {
                            Debug.LogError("Failed to decode audio file: " + filePath);
                        }
                    }
                    else
                    {
                        Debug.LogError("Failed to load audio file: " + filePath);
                    }
                }
            }
        }
        else
        {
            Debug.Log("Not the same size.");
        }

        if (isLoggingActive)
        {
            instance = Instantiate(logTextPrefab, logContentRoot.transform);
            instance.GetComponent<TextMeshProUGUI>().text = "dataPath : " + mPath;
            Debug.Log("Loading video: " + videoPath);
            instance = Instantiate(logTextPrefab, logContentRoot.transform);
            instance.GetComponent<TextMeshProUGUI>().text = "Loading video: " + videoPath;
        }

        using (UnityWebRequest request = UnityWebRequest.Get(videoPath))
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Video load failed: " + request.error);
                Debug.LogError("Video load failed: " + videoPath);
                return;
            }
        }

        videoPlayer.source = VideoSource.Url;
        videoPlayer.isLooping = true;
        videoPlayer.url = videoPath;
        videoPlayer.timeReference = VideoTimeReference.ExternalTime;
        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.skipOnDrop = true;

        videoPlayer.prepareCompleted += InitialiseVideo;
        videoPlayer.Prepare();
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= InitialiseVideo;

            if (videoPlayer.targetTexture != null)
                videoPlayer.targetTexture.Release();

            videoPlayer.Stop();
            videoPlayer.url = null;
        }
    }

    void InitialiseVideo(VideoPlayer source)
    {
        double startDspTime = AudioSettings.dspTime + 0.5;

        source.timeReference = VideoTimeReference.ExternalTime;
        source.externalReferenceTime = startDspTime - AudioSettings.dspTime;
        source.Play();

        foreach (AudioSource src in audioSources)
            src.PlayScheduled(startDspTime);
    }

    void Update()
    {
        if (videoPlayer.isPlaying)
        {
            // En Oculus retrasamos el vídeo el tiempo que tarda el audio en salir por hardware
            // En tablet/PC no aplicamos offset
            double offset = isTablet ? 0.0 : oculusAudioLatencyOffset;
            videoPlayer.externalReferenceTime = AudioSettings.dspTime - offset;

            CheckAudioVideoSync();
        }

        if (isTablet)
        {
            UpdateTouch();
            UpdateAudioFocus();
        }
    }

    void CheckAudioVideoSync()
    {
        if (audioSources.Count == 0 || audioSources[0].clip == null) return;

        AudioSource reference = audioSources[0];
        if (!reference.isPlaying) return;

        double audioTime = (double)reference.timeSamples / reference.clip.frequency;
        double videoTime = videoPlayer.time;
        double drift = videoTime - audioTime;

        if (System.Math.Abs(drift) > maxDrift)
        {
            // Desfase grande: reposicionamos el audio al tiempo del vídeo
            Debug.LogWarning($"Hard resync: drift = {drift:F3}s");

            foreach (AudioSource src in audioSources)
            {
                if (src.clip == null) continue;
                src.Stop();
                src.timeSamples = Mathf.Clamp(
                    (int)(videoTime * src.clip.frequency),
                    0,
                    src.clip.samples - 1
                );
                src.Play();
            }

            videoPlayer.externalReferenceTime = AudioSettings.dspTime;
        }
        else if (System.Math.Abs(drift) > syncThreshold)
        {
            // Desfase suave: ajustamos el pitch del audio levemente
            // drift > 0: vídeo va por delante → aceleramos audio
            // drift < 0: audio va por delante → frenamos audio
            Debug.Log($"Soft resync: drift = {drift:F3}s");

            float pitchCorrection = drift > 0 ? 1.02f : 0.98f;
            foreach (AudioSource src in audioSources)
                src.pitch = pitchCorrection;

            videoPlayer.playbackSpeed = 1.0f;
        }
        else
        {
            // Dentro del umbral: todo normal
            videoPlayer.playbackSpeed = 1.0f;
            foreach (AudioSource src in audioSources)
                src.pitch = 1.0f;
        }
    }

    void HandleTouchZoom()
    {
        if (Touchscreen.current == null) return;

        var screen = Touchscreen.current;
        var touches = screen.touches;

        if (touches.Count == 0) return;

        foreach (var t in touches)
        {
            if (!t.isInProgress) continue;

            Vector2 pos = t.position.ReadValue();

            if (pos.x > Screen.width / 2)
                Zoom(-1);
            else
                Zoom(1);
        }
    }

    void Zoom(float direction)
    {
        float newFOV = Mathf.Clamp(
            standardCamera.fieldOfView + direction * zoomSpeed * Time.deltaTime,
            minFOV,
            maxFOV
        );
        standardCamera.fieldOfView = newFOV;
    }

    void UpdateGyroRotation()
    {
        Quaternion deviceRotation = Input.gyro.attitude;
        deviceRotation = new Quaternion(deviceRotation.x, deviceRotation.y, -deviceRotation.z, -deviceRotation.w);

        Quaternion baseRotation = Quaternion.identity;
        switch (Screen.orientation)
        {
            case ScreenOrientation.LandscapeLeft:
                baseRotation = Quaternion.Euler(90f, 0f, 0f);
                break;
            case ScreenOrientation.LandscapeRight:
                baseRotation = Quaternion.Euler(90f, 0f, 180f);
                break;
            case ScreenOrientation.Portrait:
                baseRotation = Quaternion.Euler(90f, 0f, 90f);
                break;
            case ScreenOrientation.PortraitUpsideDown:
                baseRotation = Quaternion.Euler(90f, 0f, -90f);
                break;
        }

        standardCamera.transform.rotation = baseWorldRotation * (baseRotation * deviceRotation);
    }

    void UpdateAudioFocus()
    {
        float normalized = Mathf.InverseLerp(minFOV, maxFOV, standardCamera.fieldOfView);
        float sliderValue = Mathf.Lerp(0.1f, 1.0f, normalized);

        float coneAngle = standardCamera.fieldOfView;
        Vector3 listenerPos = standardCamera.transform.position;
        Vector3 forward = standardCamera.transform.forward;

        foreach (AudioSource src in audioSources)
        {
            Vector3 toSource = (src.transform.position - listenerPos).normalized;
            float angle = Vector3.Angle(forward, toSource);
            float boost = Mathf.Clamp01((coneAngle - angle) / coneAngle);
            src.volume = Mathf.Clamp(1 * boost, sliderValue, 1.0f);
        }
    }

    void UpdateMouse()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.rightButton.isPressed)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            float deltaX = delta.x * rotationSpeed;
            float deltaY = -delta.y * rotationSpeed;

            standardCamera.transform.Rotate(Vector3.up, deltaX, Space.World);
            standardCamera.transform.Rotate(Vector3.right, deltaY, Space.Self);
        }

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float newFOV = Mathf.Clamp(
                standardCamera.fieldOfView - scroll * zoomSpeed * Time.deltaTime,
                minFOV,
                maxFOV
            );
            standardCamera.fieldOfView = newFOV;
        }
    }

    void UpdateTouch()
    {
        var screen = Touchscreen.current;
        if (screen == null || screen.touches.Count == 0) return;

        if (screen.touches[0].isInProgress && !screen.touches[1].isInProgress)
        {
            float normalized = Mathf.InverseLerp(minFOV, maxFOV, standardCamera.fieldOfView);
            float sliderValue = Mathf.Lerp(0.3f, 1.0f, normalized);

            Vector2 delta = screen.touches[0].delta.ReadValue() * (rotationSpeed * sliderValue);
            standardCamera.transform.Rotate(Vector3.up, -delta.x, Space.World);
            standardCamera.transform.Rotate(Vector3.right, delta.y, Space.Self);
        }

        if (screen.touches[0].isInProgress && screen.touches[1].isInProgress)
        {
            var t0 = screen.touches[0];
            var t1 = screen.touches[1];

            Vector2 pos0 = t0.position.ReadValue();
            Vector2 pos1 = t1.position.ReadValue();

            Vector2 prev0 = pos0 - t0.delta.ReadValue();
            Vector2 prev1 = pos1 - t1.delta.ReadValue();

            float prevMag = (prev0 - prev1).magnitude;
            float currentMag = (pos0 - pos1).magnitude;
            float diff = currentMag - prevMag;

            float newFOV = Mathf.Clamp(
                standardCamera.fieldOfView - diff * zoomSpeed * Time.deltaTime,
                minFOV,
                maxFOV
            );
            standardCamera.fieldOfView = newFOV;
        }
    }
}
