using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace Gaze.Core
{
    /// <summary>
    /// Button relay: pauses/resumes the 360 video together with the synced audio
    /// stems (PlatformManager only aligns them at start; pausing the video alone
    /// would desync the stems).
    /// </summary>
    public class VideoPlaybackToggle : MonoBehaviour
    {
        [SerializeField] VideoPlayer videoPlayer;

        [Tooltip("Parent of the stem AudioSources (PlatformManager's audioSourceObject).")]
        [SerializeField] GameObject audioSourceRoot;

        readonly List<AudioSource> m_PausedSources = new List<AudioSource>();
        bool m_Paused;

        public void Toggle()
        {
            if (videoPlayer == null)
                return;

            if (!m_Paused)
            {
                videoPlayer.Pause();
                m_PausedSources.Clear();
                foreach (AudioSource source in GetStemSources())
                {
                    if (source.isPlaying)
                    {
                        source.Pause();
                        m_PausedSources.Add(source);
                    }
                }
                m_Paused = true;
            }
            else
            {
                videoPlayer.Play();
                foreach (AudioSource source in m_PausedSources)
                {
                    if (source != null)
                        source.UnPause();
                }
                m_PausedSources.Clear();
                m_Paused = false;
            }
        }

        IEnumerable<AudioSource> GetStemSources()
        {
            if (audioSourceRoot == null)
                yield break;
            foreach (AudioSource source in audioSourceRoot.GetComponentsInChildren<AudioSource>(true))
                yield return source;
        }
    }
}
