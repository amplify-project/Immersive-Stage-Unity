using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

public class MediaSyncController : MonoBehaviour
{
    [Header("Video (.mp4)")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Tooltip("Ruta RELATIVA dentro de persistentDataPath. Ej: Media/concert360.mp4")]
    [SerializeField] private string persistentVideoRelativePath = "Media/video.mp4";

    [Header("Audio stems (.mp3)")]
    [Tooltip("AudioSources posicionados en la escena (mismo orden que las rutas).")]
    [SerializeField] private AudioSource[] audioSources;

    [Tooltip("Rutas RELATIVAS dentro de persistentDataPath, una por AudioSource. Ej: Audio/stem1.mp3")]
    [SerializeField] private string[] persistentAudioRelativePaths;

    [Header("Sync")]
    [SerializeField] private double startDelaySeconds = 0.25;
    [SerializeField] private bool lockVideoToDsp = true;

    private CancellationTokenSource _cts;

    private async void Start()
    {

        Debug.Log("persistentDataPath: " + Application.persistentDataPath);

        _cts = new CancellationTokenSource();

        if (videoPlayer == null)
        {
            Debug.LogError("[MediaSync] VideoPlayer no asignado.");
            return;
        }

        // 1) Video desde persistentDataPath (.mp4)
        string videoUrl = GetPersistentFileUrl(persistentVideoRelativePath);

        if (!File.Exists(GetPersistentAbsolutePath(persistentVideoRelativePath)))
        {
            Debug.LogError("[MediaSync] Video.mp4 no encontrado en persistentDataPath");
            return;
        }

        // 2) Cargar audios .mp3 desde persistentDataPath
        await LoadMp3AudioAsync(_cts.Token);

        // 3) Configurar VideoPlayer
        ConfigureVideoPlayer(videoUrl);

        // 4) Preparar y arrancar sincronizado
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.Prepare();

        
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
        }

        _cts?.Cancel();
        _cts?.Dispose();
    }

    private void ConfigureVideoPlayer(string url)
    {
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = url;

        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.isLooping = true;
        videoPlayer.skipOnDrop = true;

        videoPlayer.timeReference = VideoTimeReference.ExternalTime;
    }

    private void OnVideoPrepared(VideoPlayer vp)
    {
        double startDsp = AudioSettings.dspTime + startDelaySeconds;

        vp.externalReferenceTime = startDsp;
        vp.Play();

        foreach (var a in audioSources)
        {
            if (a == null || a.clip == null) continue;

            a.loop = true;
            a.spatialize = true;
            a.spatialBlend = 1f;

            a.PlayScheduled(startDsp);
        }

        Debug.Log($"[MediaSync] Started at DSP={startDsp:F3}");
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogError($"[MediaSync] Video error: {message}");
    }

    private void Update()
    {
        if (!lockVideoToDsp) return;
        if (videoPlayer == null || !videoPlayer.isPlaying) return;

        videoPlayer.externalReferenceTime = AudioSettings.dspTime;
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
    // Load MP3 audio only
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
                Debug.LogError($"[MediaSync] MP3 no encontrado: {abs}");
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
                    Debug.LogError($"[MediaSync] Error cargando MP3: {req.error}");
                    continue;
                }

                source.clip = DownloadHandlerAudioClip.GetContent(req);
            }
        }
    }
}
