using System.Collections.Generic;
using UnityEngine;

namespace Gaze.Core
{
    /// <summary>
    /// Isolates the gazed musician's stem: while you look at a musician, its
    /// AudioSource stays at full volume and the rest duck to <see cref="duckedVolume"/>.
    /// Driven by <see cref="GazeWorldRaycaster.MusicianGazeChanged"/> so it reacts
    /// the instant your gaze lands on a musician, before the closeup window opens.
    ///
    /// This only touches per-source volume; it does not change spatialBlend or the
    /// spatializer, so it composes with whatever PlatformManager set up for the
    /// build target (flat 2D on Quest, Pico HRTF, etc). The "spatial" feel comes
    /// from the scene's existing audio config; here we only aisle the mix.
    ///
    /// Attach to the same rig as GazeWorldRaycaster and assign <see cref="audioSourceRoot"/>
    /// to the same object PlatformManager uses (its audioSourceObject).
    /// </summary>
    public class MusicianAudioFocus : MonoBehaviour
    {
        [Tooltip("Parent GameObject whose children hold the per-musician AudioSources (PlatformManager.audioSourceObject).")]
        [SerializeField] GameObject audioSourceRoot;

        [Tooltip("Volume of the musician you are looking at, and of every stem when no one is focused.")]
        [SerializeField, Range(0f, 1f)] float focusedVolume = 1f;

        [Tooltip("Volume the other stems duck to while a musician is focused. Not zero, so the mix stays present.")]
        [SerializeField, Range(0f, 1f)] float duckedVolume = 0.15f;

        [Tooltip("Higher = faster volume transition (exponential smoothing).")]
        [SerializeField] float lerpSpeed = 4f;

        readonly List<AudioSource> m_Sources = new List<AudioSource>();
        AudioSource m_Focused;

        void OnEnable()
        {
            GazeWorldRaycaster.MusicianGazeChanged += OnMusicianGazeChanged;
        }

        void OnDisable()
        {
            GazeWorldRaycaster.MusicianGazeChanged -= OnMusicianGazeChanged;
        }

        void Start()
        {
            if (audioSourceRoot == null)
            {
                Debug.LogWarning("[MusicianAudioFocus] audioSourceRoot not assigned - no stems to control.");
                return;
            }

            foreach (Transform child in audioSourceRoot.transform)
            {
                var src = child.GetComponent<AudioSource>();
                if (src != null)
                    m_Sources.Add(src);
            }
        }

        // The gazed musician changed (or null when gaze leaves). The screen lives on
        // the same GameObject as the musician's AudioSource, so pull it from there.
        void OnMusicianGazeChanged(MusicianCloseupScreen screen)
        {
            m_Focused = screen != null ? screen.GetComponent<AudioSource>() : null;
        }

        void Update()
        {
            if (m_Sources.Count == 0) return;

            float t = 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime);
            bool anyFocus = m_Focused != null;

            for (int i = 0; i < m_Sources.Count; i++)
            {
                AudioSource src = m_Sources[i];
                if (src == null) continue;

                // No focus -> everyone back to full. Focus -> only the gazed stem full.
                float target = !anyFocus || src == m_Focused ? focusedVolume : duckedVolume;
                src.volume = Mathf.Lerp(src.volume, target, t);
            }
        }
    }
}
