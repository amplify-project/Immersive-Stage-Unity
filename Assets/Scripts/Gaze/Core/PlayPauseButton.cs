using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Gaze.Core
{
    /// <summary>
    /// Swaps a Button's icon sprite between play and pause based on VideoPlayer state.
    /// Wire the Button's onClick to VideoPlaybackToggle.Toggle().
    /// </summary>
    public class PlayPauseButton : MonoBehaviour
    {
        [SerializeField] VideoPlayer videoPlayer;
        [SerializeField] Image icon;
        [SerializeField] Sprite playSprite;
        [SerializeField] Sprite pauseSprite;

        void Update()
        {
            if (icon == null || videoPlayer == null) return;
            icon.sprite = videoPlayer.isPlaying ? pauseSprite : playSprite;
        }
    }
}
