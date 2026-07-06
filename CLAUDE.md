# 360inmersive

Unity 360-video concert experience (Quest / Pico / Vision Pro / tablet).
Main scene: `Assets/Scenes/Scene1_360NextStage.unity`.
Branch `feature_noZoom_CloseUp_AisleSound`: no zoom, world-fixed closeup windows, gaze-isolated audio.

## 360 NextStage — runtime file names

All of these are loaded at runtime from the device's `persistentDataPath`
(`/sdcard/Android/data/<pkg>/files/` on Quest/Pico). They are NOT bundled in
the build; push them to the device before running.

### Sphere video (PlatformManager.ResolveVideoFile)
- `AMPLIFY_TEST_2606_CAM360.mp4` — preferred if present
- `Pisa*.mp4` — fallback (first match)
- `Pisa2Concert360_4k.mp4` — historical default

### Closeup videos (MusicianCloseupScreen.videoFilename)
- `AMPLIFY_TEST_2606_CLOSEUP_CAM_A.mp4` — Mauro 03, Pee Wee 03
- `AMPLIFY_TEST_2606_CLOSEUP_CAM_B.mp4` — Gabrio 03
- `close_up_hands_from_saxophone.mp4`, `close_up_hands_from_Drums_play.mp4`,
  `close_up_hands_from_piano_play.mp4` — legacy Pisa musicians (inactive object)

### Audio stems (PlatformManager: "<AudioSource child name>.wav")
One wav per child of the `360 NextStage` object, filename = object name + `.wav`:
- `Gabrio 03.wav`
- `Mauro 03.wav`
- `Pee Wee 03.wav`
- `W 03.wav`, `X 03.wav`, `Y 03.wav`, `Z 03.wav` — extra stems, wired when their
  source objects are added to the scene

Project copies of the stems live in `Assets/files/NextStageAudioAssets/` (LFS).

## Closeup screens — placement rules

- The render viewpoint on device is the **Proxy Camera** on the arm at world
  `(-3.15, -9.31, -3.7)` (NOT the head-tracked XR camera, which only feeds yaw).
- Each musician's `CloseupScreen` transform authored in the editor is the ground
  truth: position, rotation AND scale. `MusicianCloseupScreen` does no placement;
  it only fades and grows toward the authored scale (`startScaleFactor` on
  `CloseupScreenSettings`).
