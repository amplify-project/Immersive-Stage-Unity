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

        // Fade / placement / size are tuned globally on the CloseupScreenSettings
        // component (on "360 Pisa Day Sources"), NOT per musician. These are runtime
        // fields, not serialized: they are filled from that component in Awake. The
        // initializers here are only the fallback if no settings component exists.
        float fadeInDuration = 0.6f;
        float fadeOutDuration = 0.3f;
        float openDistance = 3f;
        float heightOffset = 0f;
        float openScale = 0.005f;
        float startScaleFactor = 0.15f;

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
            ApplyGlobalSettings();

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

        // No LateUpdate billboard on purpose: the window is anchored in front of the
        // viewer at open time and must NOT follow the head (would be nauseating and
        // would keep covering whatever you look at next).

        public void Show()
        {
            if (IsShowing) return;
            IsShowing = true;

            if (screenRoot != null)
                screenRoot.SetActive(true);

            AnchorInFrontOfViewer();

            if (m_Fade != null) StopCoroutine(m_Fade);
            m_Fade = StartCoroutine(OpenRoutine());

            StartVideo();
        }

        public void Hide()
        {
            if (!IsShowing) return;
            IsShowing = false;

            if (videoPlayer != null)
                videoPlayer.Stop();

            if (m_Fade != null) StopCoroutine(m_Fade);
            m_Fade = StartCoroutine(CloseRoutine());
        }

        // If a CloseupScreenSettings lives on any parent (e.g. "360 Pisa Day Sources"),
        // adopt its values so all musicians are tuned from one place. Absent = keep local.
        void ApplyGlobalSettings()
        {
            var s = GetComponentInParent<CloseupScreenSettings>();
            if (s == null) return;

            fadeInDuration = s.fadeInDuration;
            fadeOutDuration = s.fadeOutDuration;
            openDistance = s.openDistance;
            heightOffset = s.heightOffset;
            openScale = s.openScale;
            startScaleFactor = s.startScaleFactor;
        }

        // Places the window a fixed distance ahead of the camera, upright, facing you.
        // Position and rotation are set once here and left alone while it's open.
        void AnchorInFrontOfViewer()
        {
            if (screenRoot == null) return;
            Camera cam = ResolveCamera();
            if (cam == null) return;

            Vector3 flatFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = cam.transform.forward;

            screenRoot.transform.position = cam.transform.position + flatFwd * openDistance + Vector3.up * heightOffset;
            screenRoot.transform.rotation = Quaternion.LookRotation(flatFwd, Vector3.up);
        }

        // Grows the window from a small scale to full while fading in.
        IEnumerator OpenRoutine()
        {
            Vector3 fromScale = Vector3.one * (openScale * startScaleFactor);
            Vector3 toScale = Vector3.one * openScale;
            if (screenRoot != null) screenRoot.transform.localScale = fromScale;
            if (canvasGroup != null) canvasGroup.alpha = 0f;

            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeInDuration));
                if (canvasGroup != null) canvasGroup.alpha = k;
                if (screenRoot != null) screenRoot.transform.localScale = Vector3.Lerp(fromScale, toScale, k);
                yield return null;
            }

            if (canvasGroup != null) canvasGroup.alpha = 1f;
            if (screenRoot != null) screenRoot.transform.localScale = toScale;
        }

        // Fades out (and slightly shrinks back) then deactivates.
        IEnumerator CloseRoutine()
        {
            float fromAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
            Vector3 fromScale = screenRoot != null ? screenRoot.transform.localScale : Vector3.one * openScale;
            Vector3 toScale = fromScale * 0.92f;

            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / fadeOutDuration);
                if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(fromAlpha, 0f, k);
                if (screenRoot != null) screenRoot.transform.localScale = Vector3.Lerp(fromScale, toScale, k);
                yield return null;
            }

            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (screenRoot != null) screenRoot.SetActive(false);
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

        Camera ResolveCamera()
        {
            if (m_Cam != null && m_Cam.isActiveAndEnabled) return m_Cam;
            m_Cam = Camera.main ?? (Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null);
            return m_Cam;
        }
    }
}
