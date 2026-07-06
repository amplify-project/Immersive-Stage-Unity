using UnityEngine;

namespace Gaze.Core
{
    /// <summary>
    /// One place to tune the closeup windows for ALL musicians at once. Put this on
    /// the parent that holds the per-musician stems (e.g. "360 Pisa Day Sources").
    ///
    /// Every <see cref="MusicianCloseupScreen"/> under this object copies these
    /// values into itself at Awake, so you set the growth/size/placement once here
    /// instead of on each of the 6 musicians. If this component is absent, each
    /// screen just keeps its own inspector values.
    /// </summary>
    public class CloseupScreenSettings : MonoBehaviour
    {
        [Header("Fade / growth (applied to every musician under this object)")]
        [Tooltip("How long the window takes to fade in AND grow to full size. Higher = slower, gentler open.")]
        public float fadeInDuration = 0.6f;
        [Tooltip("How long the window takes to fade out when gaze leaves.")]
        public float fadeOutDuration = 0.3f;

        // NOTE: placement and final size are NOT tuned here anymore. Each screen's
        // position/rotation/scale ground truth is its screenRoot transform as
        // authored in the editor; the script only fades and grows toward it.
        [Header("Growth")]
        [Tooltip("Starting scale as a fraction of the editor-authored scale (window grows from this to full).")]
        [Range(0.05f, 1f)] public float startScaleFactor = 0.15f;
    }
}
