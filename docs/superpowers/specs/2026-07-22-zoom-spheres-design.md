# Zoom Spheres (dwell-to-zoom) — Design

## Context

`PicoInteractionController` currently drives two eyes-only interactions on Pico 4 Pro:
arrow-pad sphere scrolling (gaze D-pad) and wink-to-zoom (right eye closed = zoom in,
left eye closed = zoom out). Wink detection is unreliable enough that we want to replace
it with a gaze-dwell trigger: two head-locked spheres near the bottom of the view — right
sphere zooms in, left sphere zooms out — activated by looking at one for 1.5 seconds,
then continuing to zoom while the gaze stays on it.

The wink code stays in place behind a flag so it can be re-enabled later; it is not being
deleted.

## Goals

- Two head-locked 3D spheres, closer to the camera than the existing arrow-pad canvas
  (1.2 m) and the `HeadLockedScreen` quad, positioned at the bottom-left (zoom out) and
  bottom-right (zoom in) diagonals so they don't overlap the arrow-pad's activation zones.
- Looking at a sphere for `zoomDwellSeconds` (1.5 s) arms it; zoom then continues every
  frame at the existing `zoomSpeed`, clamped to `minFOV`/`maxFOV`, for as long as the gaze
  stays on that sphere. Looking away resets the dwell timer to zero and stops the zoom.
- Reuses the exact FOV-changing code path already used by wink zoom — only the trigger
  changes.
- Wink zoom is preserved but gated behind a new `winkZoomEnabled` flag, defaulted on for
  now; it will be switched off once the spheres are validated on-device.
- One-click scene setup (`Tools > Gaze > Set Up Gaze Interaction`) creates and wires both
  spheres automatically, matching how it already creates the four arrows.

## Non-goals

- No physics raycasting. Detection stays purely angular (`Vector3.Angle`), matching the
  existing arrow-pad and avoiding a new raycast layer/collider-interaction surface.
- No changes to the arrow-pad scrolling logic or its activation math.
- No visionOS work — this is Pico-only, inside `Gaze.Pico`.

## Architecture

All changes live in `Assets/Scripts/Gaze/Pico/PicoInteractionController.cs`, following the
script's existing self-contained style (raw PXR eye-tracking reads, no dependency on the
generic `GazeDwellUIClicker`/`IGazeRayProvider` system used elsewhere for the world-space
menu). This keeps the three PICO interactions — arrows, wink, zoom spheres — in one place
with one source of head/eye data per frame, and avoids stretching the generic dwell-click
system (built for single-shot UI button clicks) to cover a "dwell then hold" gesture it
wasn't designed for.

### New serialized fields

```
[Header("Zoom Spheres (gaze-dwell)")]
[SerializeField] Transform zoomSphereRight;   // zoom IN
[SerializeField] Transform zoomSphereLeft;    // zoom OUT
[SerializeField, Range(5f, 30f)] float zoomActivationAngleDeg = 12f;
[SerializeField] float zoomDwellSeconds = 1.5f;
[SerializeField] Color zoomSphereHighlightColor = new Color(0.62f, 0.2f, 0.86f, 1f);

[Header("Wink Zoom")]
[SerializeField] bool winkZoomEnabled = true;   // existing fields (zoomSpeed, minFOV, maxFOV, winkThreshold) unchanged
```

`zoomSphereRight`/`Left` are assigned by `GazeSceneSetup` at edit time, same pattern as
`arrowLeft`/`Right`/`Up`/`Down`.

### Gaze-on-sphere detection

New helper, parallel to the existing `GetDirectionThreshold` used by the arrow-pad but
expressed as a fixed angular radius rather than a per-arrow computed edge threshold —
a diagonal circular target doesn't fit the arrow-pad's dominant-axis model:

```
bool IsGazingAtTarget(Transform target, Vector3 worldGazeDir)
{
    if (target == null || headTransform == null) return false;
    Vector3 toTarget = target.position - headTransform.position;
    return Vector3.Angle(worldGazeDir, toTarget) <= zoomActivationAngleDeg;
}
```

`worldGazeDir` is the existing gain-adjusted local gaze direction (already computed in
`UpdateScrollAndArrows` as `localDir`) transformed into world space via
`headTransform.rotation * localDir`. This is computed once per frame and reused for both
spheres.

### Dwell + continuous zoom

New per-eye-frame method, called from `Update()` alongside `UpdateScrollAndArrows` and
`UpdateWinkZoom`:

```
float m_ZoomInDwellTimer;
float m_ZoomOutDwellTimer;

void UpdateZoomSpheres(Vector3 worldGazeDir)
{
    Camera cam = proxyCamera != null ? proxyCamera : Camera.main;
    if (cam == null) return;

    bool onRight = IsGazingAtTarget(zoomSphereRight, worldGazeDir);
    bool onLeft  = IsGazingAtTarget(zoomSphereLeft, worldGazeDir);

    m_ZoomInDwellTimer  = onRight ? m_ZoomInDwellTimer  + Time.deltaTime : 0f;
    m_ZoomOutDwellTimer = onLeft  ? m_ZoomOutDwellTimer + Time.deltaTime : 0f;

    if (m_ZoomInDwellTimer >= zoomDwellSeconds)
        cam.fieldOfView = Mathf.Max(minFOV, cam.fieldOfView - zoomSpeed * Time.deltaTime);
    else if (m_ZoomOutDwellTimer >= zoomDwellSeconds)
        cam.fieldOfView = Mathf.Min(maxFOV, cam.fieldOfView + zoomSpeed * Time.deltaTime);

    SetSphereActive(zoomSphereRight, m_ZoomInDwellTimer  >= zoomDwellSeconds);
    SetSphereActive(zoomSphereLeft,  m_ZoomOutDwellTimer >= zoomDwellSeconds);
}
```

`SetSphereActive` mirrors `SetArrowActive`: swaps the sphere's `MeshRenderer.material.color`
between its authored rest color and `zoomSphereHighlightColor` (base colors captured in
`Awake()`, same pattern as `m_BaseColorLeft` etc. for arrows).

When eye data is invalid (`haveEyeData == false`), both dwell timers reset to zero, same
as the arrow alphas resetting to 0 in that branch.

### Wink gating

```
void UpdateWinkZoom()
{
    if (!winkZoomEnabled) return;
    ... existing body unchanged ...
}
```

### Editor gaze preview (keyboard/mouse)

The existing `#if UNITY_EDITOR` keyboard/mouse preview only feeds `rightComp`/`upComp`
into the arrow-pad path (`ApplyGazeComponents`). Zoom-sphere dwell is not previewable
through that path in this iteration — it requires real (or simulated) angular gaze data
via `worldGazeDir`, which the mouse/keyboard preview does not currently produce. Testing
the dwell zoom requires on-device eye tracking. Out of scope to extend the preview here;
flagged as a known gap, not silently ignored.

## Scene setup changes

`Assets/Scripts/Gaze/Editor/GazeSceneSetup.cs`, inside `AddPicoInteraction`: after wiring
the four arrows, create two spheres parented to the same `arrowParent` (proxyCamera or
tracked head transform), using `GameObject.CreatePrimitive(PrimitiveType.Sphere)`:

- `ZoomSphere_In` at local position roughly `(0.25, -0.35, 0.8)` (right-down-forward)
- `ZoomSphere_Out` at local position roughly `(-0.25, -0.35, 0.8)` (left-down-forward)
- Local scale small enough to read as a HUD element (e.g. `0.05` uniform, tuned on-device)
- Keep the auto-added `SphereCollider` (unused by detection, harmless, useful if a future
  physics-based interaction is added) but no `Rigidbody`.

Wire `zoomSphereRight`/`zoomSphereLeft` on the `PicoInteractionController` component via
the existing `SetObjectField` helper.

## Error handling

- Null `zoomSphereRight`/`Left`: `IsGazingAtTarget` returns `false`, dwell never arms —
  same silent-no-op pattern already used throughout this script for unwired references.
- Eye tracking unavailable: handled by the existing `haveEyeData` branch in `Update()`;
  zoom-sphere timers reset alongside the arrow alphas.
- `winkZoomEnabled` and the zoom-sphere dwell can both be active at once during the
  transition period; they write to the same `cam.fieldOfView` and don't conflict because
  only one gesture can be true at a time in practice (can't wink and gaze-dwell a distant
  sphere in a way that both cross their thresholds simultaneously in a meaningful way) —
  not interlocked in code, accepted as-is for the transition window.

## Testing

No automated test suite exists for this script (it depends on the PICO SDK's live eye
tracking API, matching how arrow-pad and wink zoom are already validated). Verification is
manual, on-device:

1. Build & deploy to Pico 4 Pro.
2. Look at the right sphere for 1.5 s → FOV narrows (zoom in) continuously while held.
3. Look at the left sphere for 1.5 s → FOV widens (zoom out) continuously while held.
4. Look away mid-dwell (before 1.5 s) → confirm no zoom happens and timer resets (verify
   via a brief look, look-away, look-again cycle staying under 1.5 s each time).
5. Confirm looking at the arrow-pad directions does not also cross a sphere's activation
   angle (tune `zoomActivationAngleDeg` and sphere placement if it does).
6. Confirm wink-to-zoom still works (flag left on) without interfering with sphere dwell.
7. Set `winkZoomEnabled = false`, rebuild, confirm winking no longer changes FOV and
   sphere-dwell zoom still works.
