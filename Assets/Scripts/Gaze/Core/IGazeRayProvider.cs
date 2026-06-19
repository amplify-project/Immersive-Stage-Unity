using UnityEngine;

namespace Gaze.Core
{
    /// <summary>
    /// A source of a world-space gaze ray. Platform assemblies (Pico eye tracking,
    /// visionOS head gaze) implement this and register themselves with
    /// <see cref="GazeDwellUIClicker.RegisterProvider"/>. Returning false means
    /// "no valid data right now" and lets the clicker fall back to head gaze.
    /// </summary>
    public interface IGazeRayProvider
    {
        bool TryGetGazeRay(out Ray worldRay);
    }
}
