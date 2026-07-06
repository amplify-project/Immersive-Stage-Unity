using UnityEngine;
using UnityEngine.Video;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif
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

    [Header("Error Logging")]
    public bool isLoggingActive;
    public GameObject logTextPrefab;
    public GameObject logContentRoot;

    [Header("Playback")]
    [Tooltip("Start the 360 video AND every stem at this time (seconds). 120 = minute 2.")]
    public float startAtSeconds = 120f;

    [Header("Audio Sources")]
    public GameObject audioSourceObject;

    [Tooltip("Set by Tools > Audio > Set Up Pico Spatial Audio. When true the stems feed the Pico spatializer DRY (spatialBlend 0); when false they use Unity 3D paneo (spatialBlend 1). A plain bool, not reflection, so it survives il2cpp.")]
    public bool usePicoSpatialAudio = false;

    private List<AudioSource> audioSources = new List<AudioSource>();
    private List<string> audioPaths = new List<string>();

    private Vector2 lastTouchPos;
    private string relativePath;
    private bool isTablet;
    private Quaternion baseWorldRotation;

    // dspTime captured the moment playback starts; the video's external clock is
    // startAtSeconds + (dspTime - this), so video and stems share one timeline.
    private double dspAtPlay;

    async void Start()
    {
        isTablet = true;

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            Permission.RequestUserPermission(Permission.ExternalStorageRead);
#endif

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
        if (model.Contains("quest") || model.Contains("pico"))
            isTablet = false;

        // Any initialized HMD runtime means headset path (PICO reports "PICO",
        // which the original "oculus" check missed).
        if (XRSettings.isDeviceActive)
            isTablet = false;

        TogglePicoSpatialAudio(usePicoSpatialAudio);

        StartCoroutine(DiagnoseAudioLevels());

        string videoPath;
        string mPath = Application.streamingAssetsPath;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!isTablet)
        {
            if (xrOrigin != null) xrOrigin.SetActive(true);
            if (proxyCamera != null) proxyCamera.gameObject.SetActive(true);
            if (standardCamera != null) standardCamera.gameObject.SetActive(false);
            string vname = ResolveVideoFile(Application.persistentDataPath, "Pisa2Concert360_4k.mp4");
            videoPath = "file://" + System.IO.Path.Combine(Application.persistentDataPath, vname);
        } else {
            if (xrOrigin != null) xrOrigin.SetActive(false);
            if (proxyCamera != null) proxyCamera.gameObject.SetActive(false);
            if (standardCamera != null) standardCamera.gameObject.SetActive(true);
            string moviesDir = "/storage/emulated/0/Movies/";
            videoPath = "file://" + moviesDir + ResolveVideoFile(moviesDir, "Pisa2Concert360.mp4");
        }
#elif UNITY_IOS
        if (xrOrigin != null) xrOrigin.SetActive(false);
        if (standardCamera != null) standardCamera.gameObject.SetActive(true);
        videoPath = System.IO.Path.Combine(Application.persistentDataPath, ResolveVideoFile(Application.persistentDataPath, "Pisa360_8K.mp4"));
#elif UNITY_VISIONOS
        // Apple Vision Pro - Fully Immersive VR: always the headset path.
        isTablet = false;
        if (xrOrigin != null) xrOrigin.SetActive(true);
        if (proxyCamera != null) proxyCamera.gameObject.SetActive(true);
        if (standardCamera != null) standardCamera.gameObject.SetActive(false);
        videoPath = "file://" + System.IO.Path.Combine(Application.persistentDataPath, ResolveVideoFile(Application.persistentDataPath, "Pisa2Concert360_4k.mp4"));
#else
        if (xrOrigin != null) xrOrigin.SetActive(false);
        if (standardCamera != null) standardCamera.gameObject.SetActive(true);
        videoPath = "video.mp4";
        videoPath = "file://" + System.IO.Path.Combine(Application.persistentDataPath, videoPath);
#endif

        EnsureAudioListener();

        GameObject instance = null;

        LogScreen($"[Audio] isTablet={isTablet}  sources={audioSources.Count}  spatial={usePicoSpatialAudio}  base={Application.persistentDataPath}");

        if (audioSources.Count == audioPaths.Count)
        {
            for (int i = 0; i < audioSources.Count; i++)
            {
                string fsPath;
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!isTablet)
                    fsPath = System.IO.Path.Combine(Application.persistentDataPath, audioPaths[i]);
                else
                    fsPath = System.IO.Path.Combine("/storage/emulated/0/Music/", audioPaths[i]);
#else
                fsPath = System.IO.Path.Combine(Application.persistentDataPath, audioPaths[i]);
#endif
                // Audio filenames contain spaces (e.g. "VOX ERIC.wav"). A raw
                // "file://" + path leaves the space unescaped and UnityWebRequest
                // fails to open it (this is why the space-free video loads but audio
                // didn't). Uri.AbsoluteUri yields a correctly percent-encoded URL.
                string filePath = new System.Uri(fsPath).AbsoluteUri;
                LogScreen("[Audio] Loading: " + audioPaths[i]);
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
                            audioSources[i].playOnAwake = false;
                            audioSources[i].Stop();
                            audioSources[i].clip = clip;
                            audioSources[i].loop = true;
                            audioSources[i].outputAudioMixerGroup = null;
                            if (usePicoSpatialAudio)
                            {
                                // unity_native backend: the native Pico spatializer
                                // plugin spatializes each source via Unity's standard
                                // pipeline, which needs spatialize=true AND blend>0
                                // (blend=0 bypasses the spatializer). Keep the sources in
                                // the full-volume plateau (minDistance huge) so Unity's
                                // own 3D rolloff doesn't duck them at the ~36 m ring
                                // radius; Pico attenuation is None, so nothing else ducks.
                                audioSources[i].spatialize = true;
                                audioSources[i].spatialBlend = 1f;
                                audioSources[i].rolloffMode = AudioRolloffMode.Linear;
                                audioSources[i].minDistance = 1000f;
                                audioSources[i].maxDistance = 100000f;
                            }
                            else
                            {
                                // Flat 2D mix: no spatializer, no distance attenuation,
                                // no panning -> all 6 stems sum at full volume, equally
                                // audible. spatialBlend=0 (was 1) so the ~36 m source
                                // distance can't duck anything. (Pico HRTF is a separate
                                // path; this is the "just let me hear the 6" fallback.)
                                audioSources[i].spatialize = false;
                                audioSources[i].spatialBlend = 0f;
                            }
                            audioSources[i].volume = 1f;
                            LogScreen("[Audio] OK (" + Mathf.RoundToInt(clip.length) + "s): " + audioPaths[i]);
                        }
                        else
                        {
                            LogScreen("[Audio] DECODE FAIL: " + audioPaths[i]);
                        }
                    }
                    else
                    {
                        LogScreen("[Audio] LOAD FAIL (" + www.result + " / " + www.error + "): " + audioPaths[i]);
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

        // We used to pre-fetch the whole file with UnityWebRequest.Get(videoPath) just to
        // verify it existed. That buffers the ENTIRE file into RAM before playback: fine
        // for the 264 MB Pisa clip, fatal for the 16 GB AMPLIFY clip -> it OOM-kills the
        // app on a 6 GB Quest (that was the "crash on loading the new video"). VideoPlayer
        // streams the URL itself and surfaces problems via errorReceived, so the pre-fetch
        // was both useless and dangerous. Removed; just subscribe to error reporting.
        videoPlayer.errorReceived += (vp, msg) => LogScreen("[Video] ERROR: " + msg);

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

    /// <summary>
    /// Picks the sphere video from <paramref name="dir"/>: the AMPLIFY test clip if
    /// present, otherwise the first file whose name starts with "Pisa", otherwise the
    /// historical <paramref name="fallbackDefault"/>. Data-driven so dropping a new
    /// AMPLIFY_TEST_2606_CAM360.mp4 into the folder switches the video with no rebuild.
    /// Only .mp4 is considered.
    /// </summary>
    string ResolveVideoFile(string dir, string fallbackDefault)
    {
        try
        {
            string[] mp4s = System.IO.Directory.GetFiles(dir, "*.mp4");

            foreach (string f in mp4s)
            {
                if (System.IO.Path.GetFileNameWithoutExtension(f)
                        .Equals("AMPLIFY_TEST_2606_CAM360", System.StringComparison.OrdinalIgnoreCase))
                {
                    LogScreen("[Video] using AMPLIFY: " + System.IO.Path.GetFileName(f));
                    return System.IO.Path.GetFileName(f);
                }
            }

            foreach (string f in mp4s)
            {
                if (System.IO.Path.GetFileName(f)
                        .StartsWith("Pisa", System.StringComparison.OrdinalIgnoreCase))
                {
                    LogScreen("[Video] AMPLIFY missing, using Pisa: " + System.IO.Path.GetFileName(f));
                    return System.IO.Path.GetFileName(f);
                }
            }
        }
        catch (System.Exception e)
        {
            LogScreen("[Video] dir scan failed (" + dir + "): " + e.Message);
        }

        LogScreen("[Video] no match, using default: " + fallbackDefault);
        return fallbackDefault;
    }

    /// <summary>
    /// Logs to logcat AND to the in-headset Debug Log panel (logContentRoot), so
    /// audio status is visible on device without adb. Open it with the "Debug Log"
    /// gaze button.
    /// </summary>
    void LogScreen(string msg)
    {
        Debug.Log(msg);
        if (logTextPrefab != null && logContentRoot != null)
        {
            GameObject go = Instantiate(logTextPrefab, logContentRoot.transform);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (t != null)
                t.text = msg;
        }
    }

    /// <summary>
    /// The scene's only AudioListener lives on the XR Origin, which this class
    /// DEACTIVATES on the tablet/editor path - leaving no active listener and
    /// therefore total silence in the editor. Make sure whichever rig survived
    /// the switch has one.
    /// </summary>
    void EnsureAudioListener()
    {
        AudioListener active = FindObjectOfType<AudioListener>();
        if (active != null && active.isActiveAndEnabled)
            return;

        Camera cam = isTablet ? standardCamera : proxyCamera;
        if (cam == null)
            return;

        if (cam.GetComponent<AudioListener>() == null)
            cam.gameObject.AddComponent<AudioListener>();
        LogScreen("[Audio] No active AudioListener found; added one to " + cam.gameObject.name);
    }

    void TogglePicoSpatialAudio(bool enable)
    {
        foreach (var ps in FindObjectsOfType<PXR_Audio_Spatializer_AudioSource>())
            ps.enabled = enable;
        foreach (var pl in FindObjectsOfType<PXR_Audio_Spatializer_AudioListener>())
            pl.enabled = enable;
        foreach (var pc in FindObjectsOfType<PXR_Audio_Spatializer_Context>())
            pc.enabled = enable;
    }

    // DIAGNOSTIC (temporary): every second, dump per-stem play state + the RMS of the
    // clip at the current playhead (read from the CLIP, not GetOutputData, because the
    // PXR source zeroes the source output after submitting to the spatializer, which
    // would make GetOutputData read 0 for everything). Also dumps the final listener
    // mix RMS so we can tell whether the binaural render is producing any signal.
    System.Collections.IEnumerator DiagnoseAudioLevels()
    {
        var wait = new WaitForSeconds(1f);
        var mix = new float[256];
        while (true)
        {
            AudioListener.GetOutputData(mix, 0);
            float mixRms = RmsOf(mix);

            AudioListener listener = FindObjectOfType<AudioListener>();
            Vector3 lpos = listener != null ? listener.transform.position : Vector3.zero;
            Vector3 lfwd = listener != null ? listener.transform.forward : Vector3.forward;
            string lname = listener != null ? listener.gameObject.name : "NONE";

            var sb = new System.Text.StringBuilder();
            sb.Append($"[AudioLvl] listener='{lname}' mix={mixRms:F4} | ");
            for (int i = 0; i < audioSources.Count; i++)
            {
                AudioSource src = audioSources[i];
                float clipRms = -1f;
                bool playing = src != null && src.isPlaying;
                int t = src != null ? src.timeSamples : -1;
                if (src != null && src.clip != null && playing)
                {
                    int ch = src.clip.channels;
                    int n = 256;
                    if (t >= 0 && t + n < src.clip.samples)
                    {
                        var buf = new float[n * ch];
                        src.clip.GetData(buf, t);
                        clipRms = RmsOf(buf);
                    }
                }
                string nm = src != null ? src.gameObject.name : "null";
                float dist = src != null ? Vector3.Distance(lpos, src.transform.position) : -1f;
                float ang  = src != null ? Vector3.Angle(lfwd, src.transform.position - lpos) : -1f;
                sb.Append($"{nm}[d={dist:F1} ang={ang:F0} clipRms={clipRms:F3}] ");
            }
            LogScreen(sb.ToString());
            yield return wait;
        }
    }

    static float RmsOf(float[] a)
    {
        float s = 0f;
        for (int i = 0; i < a.Length; i++) s += a[i] * a[i];
        return a.Length > 0 ? Mathf.Sqrt(s / a.Length) : 0f;
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
        dspAtPlay = AudioSettings.dspTime;

        source.timeReference = UnityEngine.Video.VideoTimeReference.ExternalTime;
        source.externalReferenceTime = startAtSeconds;
        source.time = startAtSeconds;
        source.Play();

        LogScreen("[Audio] Video PREPARED. audioTrackCount=" + source.audioTrackCount
                  + " startAt=" + startAtSeconds + "s");

        foreach (AudioSource src in audioSources)
        {
            // Seek each stem to the same start point as the video. Guard against
            // clips shorter than the offset (time out of range throws).
            if (src.clip != null && src.clip.length > startAtSeconds)
                src.time = startAtSeconds;
            src.Play();
        }
    }

    void Update()
    {
        if (videoPlayer.isPlaying)
        {
            // Shared timeline: startAtSeconds plus elapsed dsp time since Play.
            // (The old absolute dspTime made the video chase "seconds since app
            // launch", desyncing it from stems that start at 0 or startAtSeconds.)
            videoPlayer.externalReferenceTime = startAtSeconds + (AudioSettings.dspTime - dspAtPlay);
        }

        if (isTablet)
        {
            UpdateTouch();
            UpdateAudioFocus();
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
            {
                Zoom(-1);
            }
            else
            {
                Zoom(1);
            }
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
            float newFOV = Mathf.Clamp(standardCamera.fieldOfView - scroll * zoomSpeed * Time.deltaTime, minFOV, maxFOV);
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
