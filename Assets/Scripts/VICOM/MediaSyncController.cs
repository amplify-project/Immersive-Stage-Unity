using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;
using TMPro;
using UnityEngine.InputSystem;

public class MediaSyncController : MonoBehaviour
{
    [Header("Debug Console (TMP)")]
    [SerializeField] private TextMeshProUGUI debugConsoleText;
    [SerializeField] private GameObject debugConsoleRoot; // Canvas/Panel a mostrar/ocultar
    [SerializeField] private float logEverySeconds = 0.5f;
    [SerializeField] private int maxLogLines = 25;

    [Header("XR Interaction Toolkit - Toggle Console")]
    [Tooltip("Asigna aquí una InputActionReference tipo Button (RightHand Primary Button = A).")]
    [SerializeField] private InputActionReference toggleConsoleAction;
    [SerializeField] private float toggleDebounceSeconds = 0.25f;

    [Header("Video (.mp4)")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private string persistentVideoRelativePath = "Media/video.mp4";

    [Header("Audio stems (.mp3)")]
    [SerializeField] private AudioSource[] audioSources;
    [SerializeField] private string[] persistentAudioRelativePaths;

    [Header("Sync")]
    [SerializeField] private double startDelaySeconds = 0.25;
    [SerializeField] private bool lockVideoToDsp = true;

    private CancellationTokenSource _cts;

    private double _startDsp;
    private bool _started;
    private double _nextLogDsp;

    private readonly StringBuilder _logBuilder = new StringBuilder();
    private bool _consoleVisible = true;
    private float _nextToggleTime;

    private async void Start()
    {
        _cts = new CancellationTokenSource();

        // Si no asignas root, usamos el GameObject del TMP
        if (debugConsoleRoot == null && debugConsoleText != null)
            debugConsoleRoot = debugConsoleText.gameObject;

        SetConsoleVisible(_consoleVisible);

        HookToggleAction();

        if (videoPlayer == null)
        {
            AddLog("ERROR: VideoPlayer no asignado.");
            return;
        }

        string videoAbs = GetPersistentAbsolutePath(persistentVideoRelativePath);
        string videoUrl = GetPersistentFileUrl(persistentVideoRelativePath);

        AddLog("persistentDataPath: " + Application.persistentDataPath);
        AddLog("Video abs: " + videoAbs);
        AddLog("Video exists: " + File.Exists(videoAbs));

        if (!File.Exists(videoAbs))
        {
            AddLog("ERROR: Video no encontrado en persistentDataPath.");
            return;
        }

        await LoadMp3AudioAsync(_cts.Token);

        ConfigureVideoPlayer(videoUrl);

        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.Prepare();
    }

    private void OnEnable()
    {
        // Por si el objeto se habilita/deshabilita
        HookToggleAction();
    }

    private void OnDisable()
    {
        UnhookToggleAction();
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
        }

        UnhookToggleAction();

        _cts?.Cancel();
        _cts?.Dispose();
    }

    // -----------------------
    // Video setup
    // -----------------------
    private void ConfigureVideoPlayer(string url)
    {
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = url;

        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.isLooping = true;
        videoPlayer.skipOnDrop = true;

        // Tu config original: seguir reloj externo
        videoPlayer.timeReference = VideoTimeReference.ExternalTime;
    }

    private void OnVideoPrepared(VideoPlayer vp)
    {
        _startDsp = AudioSettings.dspTime + startDelaySeconds;

        vp.externalReferenceTime = _startDsp;
        vp.Play();

        foreach (var a in audioSources)
        {
            if (a == null || a.clip == null) continue;

            a.loop = true;
            a.spatialize = true;
            a.spatialBlend = 1f;

            a.PlayScheduled(_startDsp);
        }

        _started = true;
        _nextLogDsp = AudioSettings.dspTime;

        AddLog($"Started at DSP={_startDsp:F6}");
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        AddLog("VIDEO ERROR: " + message);
    }

    private void Update()
    {
        if (!lockVideoToDsp) return;
        if (videoPlayer == null || !videoPlayer.isPlaying) return;

        videoPlayer.externalReferenceTime = AudioSettings.dspTime;

        if (!_started) return;

        double dspNow = AudioSettings.dspTime;
        if (dspNow < _nextLogDsp) return;
        _nextLogDsp = dspNow + logEverySeconds;

        double masterTime = dspNow - _startDsp;
        if (masterTime < 0) masterTime = 0;

        double videoTime = videoPlayer.time;

        double audioTime = 0;
        if (audioSources != null && audioSources.Length > 0 &&
            audioSources[0] != null && audioSources[0].clip != null)
        {
            audioTime = (double)audioSources[0].timeSamples / audioSources[0].clip.frequency;
        }

        double drift = videoTime - audioTime;

        AddLog($"DSP:{masterTime:F3}s | V:{videoTime:F3}s | A:{audioTime:F3}s | Drift:{drift * 1000:F1}ms");
    }

    // -----------------------
    // XRI InputAction toggle
    // -----------------------
    private void HookToggleAction()
    {
        if (toggleConsoleAction == null || toggleConsoleAction.action == null) return;

        // Asegurar enabled
        if (!toggleConsoleAction.action.enabled)
            toggleConsoleAction.action.Enable();

        // Evitar doble suscripción
        toggleConsoleAction.action.performed -= OnToggleConsolePerformed;
        toggleConsoleAction.action.performed += OnToggleConsolePerformed;
    }

    private void UnhookToggleAction()
    {
        if (toggleConsoleAction == null || toggleConsoleAction.action == null) return;
        toggleConsoleAction.action.performed -= OnToggleConsolePerformed;
    }

    private void OnToggleConsolePerformed(InputAction.CallbackContext ctx)
    {
        // Debounce
        if (Time.unscaledTime < _nextToggleTime) return;

        // La acción suele ser Button; interpretamos como "pressed"
        float v = ctx.ReadValue<float>();
        if (v < 0.5f) return;

        _consoleVisible = !_consoleVisible;
        SetConsoleVisible(_consoleVisible);

        _nextToggleTime = Time.unscaledTime + toggleDebounceSeconds;
        AddLog($"Console {( _consoleVisible ? "ON" : "OFF" )}");
    }

    private void SetConsoleVisible(bool visible)
    {
        if (debugConsoleRoot != null)
            debugConsoleRoot.SetActive(visible);
    }

    // -----------------------
    // TMP console logging
    // -----------------------
    private void AddLog(string message)
    {
        if (debugConsoleText == null) return;

        string ts = Time.unscaledTime.ToString("F2");
        _logBuilder.AppendLine($"[{ts}] {message}");

        // Limitar líneas
        string[] lines = _logBuilder.ToString().Split('\n');
        if (lines.Length > maxLogLines)
        {
            _logBuilder.Clear();
            for (int i = lines.Length - maxLogLines; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    _logBuilder.AppendLine(lines[i]);
            }
        }

        debugConsoleText.text = _logBuilder.ToString();
    }

    // -----------------------
    // Persistent helpers
    // -----------------------
    private static string GetPersistentAbsolutePath(string relativePath)
        => Path.Combine(Application.persistentDataPath, relativePath);

    private static string GetPersistentFileUrl(string relativePath)
    {
        string abs = GetPersistentAbsolutePath(relativePath);
        return new Uri(abs).AbsoluteUri; // file:///
    }

    // -----------------------
    // Load MP3 audio
    // -----------------------
    private async Task LoadMp3AudioAsync(CancellationToken ct)
    {
        int n = Mathf.Min(audioSources.Length, persistentAudioRelativePaths.Length);

        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();

            var source = audioSources[i];
            var rel = persistentAudioRelativePaths[i];

            if (source == null || string.IsNullOrWhiteSpace(rel)) continue;

            string abs = GetPersistentAbsolutePath(rel);
            if (!File.Exists(abs))
            {
                AddLog("MP3 no encontrado: " + abs);
                continue;
            }

            string url = new Uri(abs).AbsoluteUri;

            using (var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    AddLog("Error cargando MP3: " + req.error);
                    continue;
                }

                source.clip = DownloadHandlerAudioClip.GetContent(req);
                AddLog("Audio cargado: " + rel);
            }
        }
    }
}