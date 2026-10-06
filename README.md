# Immersive Stage Unity

**A Unity XR application for attending a concert from inside it** — a
360° video-on-demand player where eye gaze steers what you hear and see,
designed to run offline on standalone headsets.

AMPLIFY 360 Immersive Experience puts the viewer at the centre of a recorded
concert. A pre-recorded equirectangular video wraps around them while eye
tracking lets them follow individual musicians: dwelling on a performer brings
up a close-up window and isolates that musician's audio stem from the mix. The
experience is fully offline — all media is loaded from the device's local
storage, with no streaming server required.

It is one of the two open-source tools built by the [AMPLIFY](https://amplifyproject.eu)
project (Horizon Europe, Grant Agreement No 101177413) and is being trialled in
the project's pilots, among them the 360° capture of live jazz performances.

---

## What it does

- **360° sphere video.** A pre-recorded equirectangular video is rendered on
  an inside-out sphere. The viewer's head rotation drives yaw so they can look
  anywhere in the venue.
- **Eye-gaze close-ups.** Each musician has a world-fixed close-up window.
  Dwelling on it with eye gaze (Pico 4 Pro) or head gaze (Quest) brings it
  into focus and scales it up smoothly toward its authored size. Gaze is cast
  from a fixed proxy camera, so the window stays put regardless of where the
  viewer is looking.
- **Gaze-isolated audio.** Looking at a musician boosts their stem and
  attenuates the others, giving the listener a personal mix driven entirely by
  attention.
- **Video on demand, no server.** All media — sphere video, close-up feeds
  and audio stems — is loaded at runtime from the device's local storage.
  There is no streaming server, no network dependency, and no re-build needed
  to swap content.

## Supported platforms

| Platform | Gaze input | Notes |
|---|---|---|
| Meta Quest 2 / 3 / Pro | Head gaze | OpenXR |
| Pico 4 Pro | Eye gaze (XR Eye Tracking) | Full gaze interaction |

## Requirements

- **Unity 2022 LTS** or later with Android build support module.
- **Media files** pushed to the device before launch — see
  [Runtime file names](#runtime-file-names) below.
- A headset with sufficient GPU bandwidth to decode 4K equirectangular video
  in real time.

## Runtime file names

All media is loaded from `/sdcard/Android/data/<pkg>/files/`. Nothing is
bundled in the build; push the files to the device before running.

| File | Role |
|---|---|
| `AMPLIFY_TEST_2606_CAM360.mp4` | Main 360° sphere video (preferred) |
| `Pisa*.mp4` | Fallback sphere video (first match) |
| `AMPLIFY_TEST_2606_CLOSEUP_CAM_A.mp4` | Close-up feed A — Mauro, Pee Wee |
| `AMPLIFY_TEST_2606_CLOSEUP_CAM_B.mp4` | Close-up feed B — Gabrio |
| `Gabrio 03.wav` | Audio stem |
| `Mauro 03.wav` | Audio stem |
| `Pee Wee 03.wav` | Audio stem |

## Repository layout

```
Assets/
  Scripts/          C# MonoBehaviours — PlatformManager, MusicianCloseupScreen,
                    GazeSystem (Core / Pico), GazeMenuAnchor
  Scenes/           Scene1_360NextStage.unity — main scene
  files/            NextStageAudioAssets/ (LFS, gitignored — local only)
  Plugins/          Platform SDKs (Pico XR, Meta XR)
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

- Joseba Ruiz (Vicomtech)
`n- Tristan Merigny (Salsa Sound)

## Funding

Co-funded by the European Union under Grant Agreement No **101177413**
(**AMPLIFY** — *Phygital Solutions for the Cultural and Creative Industries*),
Horizon Europe call `HORIZON-CL2-2024-HERITAGE-01-03`, November 2024 – October
2027, coordinated by [Vicomtech](https://www.vicomtech.org).

> Views and opinions expressed are however those of the author(s) only and do
> not necessarily reflect those of the European Union or the European Research
> Executive Agency (REA). Neither the European Union nor the granting authority
> can be held responsible for them.
