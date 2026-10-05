# AMPLIFY 360 Immersive Experience

**A Unity XR application for immersive 360-video concert experiences** — a
head-tracked sphere video player with gaze-driven musician close-ups and
spatially isolated audio stems, designed to run on standalone headsets and
tablets.

AMPLIFY 360 Immersive Experience places the audience inside a recorded
concert: a 360° video sphere surrounds the viewer while gaze interaction
surfaces individual musician close-ups and isolates their audio stem, making
the experience simultaneously collective and personal. It is built for
community and cultural settings where physical presence is not possible.

It is one of the two open-source tools built by the [AMPLIFY](https://amplifyproject.eu)
project (Horizon Europe, Grant Agreement No 101177413) and is being trialled in
the project's pilots, among them the 360° capture of live jazz performances.

---

## What it does

- **360° sphere video.** A monoscopic equirectangular video is streamed onto
  an inside-out sphere. The viewer's head rotation drives yaw, placing them at
  the centre of the performance.
- **Gaze-driven close-ups.** Each musician has a world-fixed close-up window.
  Dwelling on a window with eye gaze (Pico 4 Pro) or head gaze brings it into
  focus and scales it up smoothly.
- **Per-musician audio isolation.** Looking at a musician boosts their stem
  and attenuates the others, so the listener can follow one voice through the
  mix without losing the ensemble.
- **Multi-platform.** Runs on Meta Quest, Pico 4 Pro, Apple Vision Pro and
  Android tablets from a single codebase, with platform-specific eye-tracking
  and audio backends separated by assembly definitions.
- **Runtime media loading.** Video and audio stems are loaded from
  `persistentDataPath` at runtime — no re-build needed to swap content.

## Supported platforms

| Platform | Eye tracking | Notes |
|---|---|---|
| Meta Quest 2 / 3 / Pro | Head gaze | OpenXR |
| Pico 4 Pro | Eye gaze (XR Eye Tracking) | Full gaze interaction |
| Apple Vision Pro | Head gaze (no eye API on visionOS) | Unity PolySpatial |
| Android tablet | Touch | No gaze |

## Requirements

- **Unity 2022 LTS** or later with Android and visionOS build support modules.
- **Media files** pushed to the device's `persistentDataPath` before launch
  (see [Runtime file names](#runtime-file-names) below).
- A headset or tablet with sufficient GPU bandwidth to decode 4K equirectangular
  video in real time.

## Runtime file names

All media is loaded from `/sdcard/Android/data/<pkg>/files/` (Quest / Pico) or
the equivalent `persistentDataPath` on other platforms. Nothing is bundled in
the build.

| File | Role |
|---|---|
| `AMPLIFY_TEST_2606_CAM360.mp4` | Main 360° sphere video |
| `AMPLIFY_TEST_2606_CLOSEUP_CAM_A.mp4` | Close-up feed A (Mauro, Pee Wee) |
| `AMPLIFY_TEST_2606_CLOSEUP_CAM_B.mp4` | Close-up feed B (Gabrio) |
| `Gabrio 03.wav`, `Mauro 03.wav`, `Pee Wee 03.wav` | Audio stems |

## Repository layout

```
Assets/
  Scripts/          C# MonoBehaviours — PlatformManager, MusicianCloseupScreen,
                    GazeSystem (Core / Pico / VisionOS), GazeMenuAnchor
  Scenes/           Scene1_360NextStage.unity — main scene
  files/            NextStageAudioAssets/ (LFS, not pushed — local only)
  Plugins/          Platform SDKs (Pico XR, Meta XR, PolySpatial)
Packages/           Unity package manifest
ProjectSettings/    Build and XR settings
```

## Contributing

Issues and pull requests are welcome. Please open an issue describing the
problem or the change before a substantial pull request, and do not include
credentials or recordings in reports.

## Third-party licences

This project uses several third-party Unity packages and SDKs under their own
licences. For a complete overview see `LICENSING_SUMMARY.txt`.

## License

GPLv3. See [`LICENSE`](LICENSE).

*(For a complete overview of third-party licenses, see `LICENSING_SUMMARY.txt`)*.

## Contributions

- Joseba Ruiz (Vicomtech): Core
- Iñigo Tamayo (Vicomtech): Core

## Funding

Co-funded by the European Union under Grant Agreement No **101177413**
(**AMPLIFY** — *Phygital Solutions for the Cultural and Creative Industries*),
Horizon Europe call `HORIZON-CL2-2024-HERITAGE-01-03`, November 2024 – October
2027, coordinated by [Vicomtech](https://www.vicomtech.org).

> Views and opinions expressed are however those of the author(s) only and do
> not necessarily reflect those of the European Union or the European Research
> Executive Agency (REA). Neither the European Union nor the granting authority
> can be held responsible for them.
