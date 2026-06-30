using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Gaze.Core
{
    /// <summary>
    /// Manages the per-musician closeup video screen.
    /// Attach to the musician GameObject alongside its AudioSource.
    ///
    /// Required hierarchy under this GO:
    ///   CloseupScreen (child)
    ///     CanvasGroup
    ///     Canvas (World Space)
    ///       Panel > RawImage   <- assign to videoDisplay
    ///       Panel > Text       <- optional label
    ///     VideoPlayer          <- optional, assign videoUrl when clips exist
    ///
    /// A SphereCollider is auto-added at Awake if no Collider is present;
    /// assign it to the Musician layer and set GazeWorldRaycaster.musicianLayerMask
    /// to that same layer.
    /// </summary>
    public class MusicianCloseupScreen : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Child GO that holds the Canvas. Deactivated while hidden.")]
        [SerializeField] GameObject screenRoot;

        [Tooltip("CanvasGroup on screenRoot used for fade.")]
        [SerializeField] CanvasGroup canvasGroup;

        [Tooltip("RawImage that receives the VideoPlayer render texture (or placeholder).")]
        [SerializeField] RawImage videoDisplay;

        [Header("Fade")]
        [SerializeField] float fadeInDuration = 0.6f;
        [SerializeField] float fadeOutDuration = 0.3f;

        [Header("Video")]
        [SerializeField] VideoPlayer videoPlayer;
        [Tooltip("Filename only (e.g. close_up_hands_from_saxophone.mp4). Loaded from persistentDataPath at runtime. Leave empty for placeholder color.")]
        [SerializeField] string videoFilename;
        [Tooltip("Optional pre-assigned RenderTexture. Created at runtime if empty.")]
        [SerializeField] RenderTexture videoRenderTexture;

        [Header("Gaze Collider")]
        [Tooltip("Radius of the auto-added SphereCollider in world units.")]
        [SerializeField] float colliderRadius = 4f;

        public bool IsShowing { get; private set; }

        Coroutine m_Fade;
        Camera m_Cam;

        void Awake()
        {
            if (GetComponent<Collider>() == null)
            {
                var col = gameObject.AddComponent<SphereCollider>();
                col.radius = colliderRadius;
                col.isTrigger = true;
            }

            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
                if (canvasGroup != null)
                    canvasGroup.alpha = 0f;
            }
        }

        void LateUpdate()
        {
            if (!IsShowing || screenRoot == null) return;

            Camera cam = ResolveCamera();
            if (cam == null) return;

            Vector3 toCam = cam.transform.position - screenRoot.transform.position;
            if (toCam.sqrMagnitude > 0.001f)
                screenRoot.transform.rotation = Quaternion.LookRotation(-toCam.normalized);
        }

        public void Show()
        {
            if (IsShowing) return;
            IsShowing = true;

            if (screenRoot != null)
                screenRoot.SetActive(true);

            if (m_Fade != null) StopCoroutine(m_Fade);
            m_Fade = StartCoroutine(FadeTo(0f, 1f, fadeInDuration));

            StartVideo();
        }

        public void Hide()
        {
            if (!IsShowing) return;
            IsShowing = false;

            if (videoPlayer != null)
                videoPlayer.Stop();

            float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
            if (m_Fade != null) StopCoroutine(m_Fade);
            m_Fade = StartCoroutine(FadeTo(startAlpha, 0f, fadeOutDuration, true));
        }

        void StartVideo()
        {
            if (string.IsNullOrEmpty(videoFilename))
            {
                Debug.Log($"[CloseupScreen:{name}] No videoFilename set.");
                return;
            }

            // Auto-create VideoPlayer on screenRoot if not wired in the scene
            if (videoPlayer == null)
            {
                GameObject vpTarget = screenRoot != null ? screenRoot : gameObject;
                videoPlayer = vpTarget.GetComponent<VideoPlayer>();
                if (videoPlayer == null)
                    videoPlayer = vpTarget.AddComponent<VideoPlayer>();
                videoPlayer.playOnAwake = false;
                videoPlayer.isLooping = true;
                videoPlayer.renderMode = VideoRenderMode.RenderTexture;
                Debug.Log($"[CloseupScreen:{name}] VideoPlayer auto-created on {vpTarget.name}.");
            }

            if (videoRenderTexture == null)
            {
                videoRenderTexture = new RenderTexture(1920, 1080, 0);
                videoPlayer.targetTexture = videoRenderTexture;
            }

            if (videoDisplay != null)
            {
                videoDisplay.texture = videoRenderTexture;
                videoDisplay.color = Color.white;
            }

            string path = System.IO.Path.Combine(Application.persistentDataPath, videoFilename);
            string url  = "file://" + path;
            Debug.Log($"[CloseupScreen:{name}] Loading {url}");

            videoPlayer.url = url;
            videoPlayer.isLooping = true;
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.errorReceived    -= OnVideoError;
            videoPlayer.errorReceived    += OnVideoError;
            videoPlayer.Prepare();
        }

        void OnVideoPrepared(VideoPlayer vp)
        {
            vp.prepareCompleted -= OnVideoPrepared;
            if (IsShowing)
                vp.Play();
        }

        void OnVideoError(VideoPlayer vp, string message)
        {
            Debug.LogError($"[CloseupScreen:{name}] VideoPlayer error: {message}");
        }

        IEnumerator FadeTo(float from, float to, float duration, bool deactivateOnEnd = false)
        {
            float elapsed = 0f;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = from;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
                    yield return null;
                }
                canvasGroup.alpha = to;
            }

            if (deactivateOnEnd && screenRoot != null)
                screenRoot.SetActive(false);
        }

        Camera ResolveCamera()
        {
            if (m_Cam != null && m_Cam.isActiveAndEnabled) return m_Cam;
            m_Cam = Camera.main ?? (Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null);
            return m_Cam;
        }
    }
}
