# Gaze-Dwell Port: Pico 4 Pro/Enterprise + Apple Vision Pro

Look at a button for **3 seconds** → it clicks. Real **eye gaze** on Pico 4 Pro/Enterprise;
**head gaze** on Apple Vision Pro (visionOS never exposes continuous eye gaze to apps — privacy)
and as automatic fallback everywhere (editor, permission denied, tracking lost).

## What was changed in this repo (already done)

| Area | Change |
|---|---|
| `Assets/Scripts/Gaze/Core/` | **Shared engine** (`Gaze.Core` assembly, all platforms): `IGazeRayProvider`, `HeadGazeProvider`, `GazeDwellUIClicker` (dwell→click via ExecuteEvents, no changes to Buttons/EventSystem), `GazeReticle`, button relays `VideoPlaybackToggle` + `GameObjectToggle` |
| `Assets/Scripts/Gaze/Pico/` | **Pico-only** (`Gaze.Pico` assembly: Android+Editor, defineConstraint `PICO_XR`): `PicoEyeGazeProvider` — real eye ray via `PXR_MotionTracking`, self-registers at priority 100. Cannot compile into visionOS builds. |
| `Assets/Scripts/Gaze/VisionOS/` | **visionOS-only** (`Gaze.VisionOS` assembly: VisionOS+Editor): `VisionOSGazeSetup` — head-gaze stays active; holds the proxy-camera fallback toggle + future pinch shortcut. Cannot compile into Android builds. |
| `Assets/Scripts/Gaze/Editor/GazeSceneSetup.cs` | One-click scene setup menu (see step 3) |
| `Assets/Scripts/Platform Manager.cs` | Pico device detection (2 lines), `#elif UNITY_VISIONOS` startup branch, Android `Permission` calls wrapped in `#if UNITY_ANDROID` |
| `Packages/manifest.json` | + `com.unity.xr.picoxr` (PICO SDK 3.4.0 git), + `com.unity.xr.visionos` 2.4.3 |
| `ProjectSettings/ProjectSettings.asset` | Android defines + `PICO_XR`; minSdk 24→29 (PICO requirement); visionOS target OS 2.0; visionOS bundle id placeholder `com.salsasound.concert360` (**confirm/replace with your real Apple bundle id**) |
| `ProjectSettings/AudioManager.asset` | Cleared dangling ambisonic decoder `Meta XR Audio` (package not installed). Spatializer stays Steam Audio (works on Pico; auto-compiled-out on visionOS, Unity's panner takes over) |

## Remaining steps (in the Unity Editor)

### 1. Open the project
- Unity **6000.2.5f1**. Git must be installed for the PICO package (or replace the git URL in
  `Packages/manifest.json` with a downloaded tarball: `"com.unity.xr.picoxr": "file:../PICO-Unity-Integration-SDK-3.4.0.tgz"`).
- If `Gaze.Pico.asmdef` shows a missing-reference warning, check the PICO SDK runtime assembly
  name in the imported package (expected **`Unity.XR.PXR`**) and fix the reference if it differs.

### 2. XR Plug-in Management (Project Settings)
- **Android tab**: check **PICO** (creates the PXR Loader and fills the currently *empty* Android loader list).
  Delete the dangling `Assets/XR/Loaders/Oculus Loader.asset` (its package is uninstalled).
- **visionOS tab** (needs the *visionOS Build Support* module + **Unity Pro**): check **Apple visionOS**,
  set **App Mode = Virtual Reality (Fully Immersive)**.

### 3. Scene setup (run once per build target)
Open `SampleScene`, then **Tools → Gaze → Set Up Gaze Interaction In Open Scene**. Creates and wires:
- `Gaze Interaction` (HeadGazeProvider + GazeDwellUIClicker + reticle, projection camera = Proxy Camera)
- `UI_GazeMenu` world-space canvas: **Play/Pause** (video + audio stems together) and **Debug Log** buttons
- Converts `UI_DebugMenu` to world space so the log is visible in-headset
- Platform children when their assembly is compiled: `Gaze Pico` (only while on the **Android** build target),
  `Gaze Pico Interaction` (edge-scroll arrows on a `UI_GazeEdgeArrows` canvas under the Proxy Camera + wink zoom),
  `Gaze VisionOS` (Editor/visionOS). **Re-run the menu after switching build target** — it only adds what's missing.
- `GazeMenuAnchor` on `UI_GazeMenu`: re-places the menu 3.5 m in front of the runtime camera during the first
  frames (the ControllerZoom arm moves the Proxy Camera up to 12.5 m from its edit-time pose, which would
  otherwise leave the menu behind the camera and invisible on device).
- Save the scene. On the *other* build target a platform child shows "missing script" — that is expected
  and harmless; the component compiles back in on its own target.

### 4. Pico scene component
Select the **XR Origin** root → Add Component → **PXR_Manager** → enable **Eye Tracking**.
(Injects `com.picovr.permission.EYE_TRACKING` into the manifest; runtime permission is requested by `PicoEyeGazeProvider`.)

### 5. Verify in Editor (before any device)
Play `SampleScene` → rotate the view with right-mouse-drag → hold the center reticle on a menu button
→ radial fill completes in 3 s → click fires. The debug scroll/video sphere never accumulate dwell.

### 6. Pico 4 Pro/Enterprise build
- Build target **Android** → Build & Run.
- Push media: `adb push Pisa2Concert360_4k.mp4 /sdcard/Android/data/com.SalsaSound.Concert360_1/files/`
  (+ the stem `.wav` files to the same folder).
- Verify: ET permission prompt → reticle follows your **eyes**; deny permission → head-gaze fallback;
  dwell clicks; Steam Audio + controller trigger zoom still work.
- Verify (Pico interaction extras): look past ~24° from view center → that edge's arrow fades in and the
  360 sphere scrolls; keep one eye closed ≥ 0.3 s → zoom in (right eye closed) / out (left eye closed).
- If the PICO 3.4.0 eye-tracking API drifted from 3.x docs, the **only** file to adjust is
  `Assets/Scripts/Gaze/Pico/PicoEyeGazeProvider.cs` (compiler will point at it).

### 7. Apple Vision Pro build (Apple Silicon Mac, Xcode 16+, visionOS 2 SDK)
- Switch target to **visionOS** → re-run the Tools→Gaze menu (adds `Gaze VisionOS` if missing) → Build → open in Xcode → run on simulator first, then device.
- **First check**: the 360 sphere renders (the Proxy Camera trick must survive Metal compositing).
  If the view is black/wrong: select `Gaze Interaction/Gaze VisionOS` → enable
  **Use Tracked Camera Fallback** (zoom is lost on AVP — fine, there are no controllers).
- Media: copy the mp4 + wavs into the app container's `Documents` (persistentDataPath) via
  Xcode → Devices or the Files app. Consider re-encoding the 4K video to **HEVC**.
- Interaction = head-gaze dwell (Apple does not allow apps continuous eye-gaze access; the OS-level
  gaze highlight cannot trigger app logic). Optional pinch-to-click-instantly: see the note at the
  bottom of `VisionOSGazeSetup.cs`.

## Architecture recap

```
Gaze.Core (all platforms)   ←  registers at runtime  ←  Gaze.Pico   (Android-only assembly)
 head-gaze fallback built in                            Gaze.VisionOS (visionOS-only assembly)
```
Shared dwell engine, platform code physically separated at the assembly level:
Pico code can never enter a visionOS build and vice versa, by construction.
