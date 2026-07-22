# Zoom Spheres (dwell-to-zoom) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace wink-triggered zoom with two head-locked, gaze-dwell-activated spheres (right = zoom in, left = zoom out) on Pico, while keeping the wink code intact behind a flag.

**Architecture:** All runtime logic lives in the existing self-contained `PicoInteractionController` (raw PXR eye-tracking reads, no dependency on the generic `GazeDwellUIClicker` system). Detection is a fixed angular radius check against each sphere's world position, reusing the same gain-adjusted gaze direction already computed for the arrow-pad. `GazeSceneSetup.cs` gets matching scene-authoring support for fresh scenes; the already-existing `SampleScene` gets the two spheres wired in by hand in the editor (see Task 5) so the automated one-click setup, which is a no-op on an already-configured rig, doesn't touch the already-tuned arrow/gaze values in that scene.

**Tech Stack:** Unity, C#, PICO Unity Integration SDK (`Unity.XR.PXR`), `Gaze.Pico` asmdef.

## Global Constraints

- Detection is purely angular (`Vector3.Angle`), no physics raycasting — spec non-goal.
- No changes to arrow-pad scrolling logic or its activation math.
- Pico-only; nothing in this plan touches `Gaze.Core` or `Gaze.VisionOS`.
- `zoomDwellSeconds` default is `1.5f`.
- `winkZoomEnabled` defaults to `true` — wink stays on until manually disabled later; do not flip it off as part of this plan.
- No automated test suite exists for this script (PICO SDK dependency, matches existing arrow-pad/wink code). Verification is manual: Unity Editor compiles with zero console errors, then on-device checks.

---

### Task 1: Gate wink zoom behind a flag

**Files:**
- Modify: `Assets/Scripts/Gaze/Pico/PicoInteractionController.cs:89-99` (Wink Zoom header/fields), `:330-359` (`UpdateWinkZoom`)

**Interfaces:**
- Produces: `bool winkZoomEnabled` field (default `true`), read at the top of `UpdateWinkZoom()`.

- [ ] **Step 1: Add the flag field**

In `PicoInteractionController.cs`, the `Wink Zoom` header currently reads (lines 89-96):

```csharp
        [Header("Wink Zoom")]
        [SerializeField] float zoomSpeed = 20f;
        [SerializeField] float minFOV = 40f;
        [SerializeField] float maxFOV = 90f;

        [Tooltip("Seconds one eye must stay closed while the other is open to count as a wink. " +
                 "Keeps normal blinks (~130 ms) from triggering zoom.")]
        [SerializeField] float winkThreshold = 0.3f;
```

Add `winkZoomEnabled` right after the header:

```csharp
        [Header("Wink Zoom")]
        [Tooltip("Master switch for the eye-wink zoom gesture. Turn off once the gaze-dwell " +
                 "zoom spheres are validated on-device; the wink code stays in place, just unused.")]
        [SerializeField] bool winkZoomEnabled = true;

        [SerializeField] float zoomSpeed = 20f;
        [SerializeField] float minFOV = 40f;
        [SerializeField] float maxFOV = 90f;

        [Tooltip("Seconds one eye must stay closed while the other is open to count as a wink. " +
                 "Keeps normal blinks (~130 ms) from triggering zoom.")]
        [SerializeField] float winkThreshold = 0.3f;
```

- [ ] **Step 2: Gate the method body**

`UpdateWinkZoom()` currently starts (line 330-333):

```csharp
        void UpdateWinkZoom()
        {
            Camera cam = proxyCamera != null ? proxyCamera : Camera.main;
            if (cam == null) return;
```

Add the flag check as the very first line inside the method:

```csharp
        void UpdateWinkZoom()
        {
            if (!winkZoomEnabled) return;

            Camera cam = proxyCamera != null ? proxyCamera : Camera.main;
            if (cam == null) return;
```

- [ ] **Step 3: Verify in the Unity Editor**

Open the project in Unity, let it recompile, and check the Console: zero errors. Select the `Gaze Pico Interaction` object in `SampleScene` and confirm the Inspector now shows a `Wink Zoom Enabled` checkbox, checked by default.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Gaze/Pico/PicoInteractionController.cs
git commit -m "Gate wink-to-zoom behind a winkZoomEnabled flag"
```

---

### Task 2: Zoom-sphere fields and gaze-on-target detection helper

**Files:**
- Modify: `Assets/Scripts/Gaze/Pico/PicoInteractionController.cs` (fields block around line 89, `Awake()` around line 101-116)

**Interfaces:**
- Consumes: existing `headTransform` field, existing `gazeGain` field.
- Produces: fields `zoomSphereRight`, `zoomSphereLeft`, `zoomActivationAngleDeg`, `zoomDwellSeconds`, `zoomSphereHighlightColor`, `m_BaseColorZoomRight`, `m_BaseColorZoomLeft`; method `bool IsGazingAtTarget(Transform target, Vector3 worldGazeDir)`. These are consumed by Task 3.

- [ ] **Step 1: Add the new fields**

Insert a new header block directly above the `[Header("Wink Zoom")]` block added in Task 1:

```csharp
        [Header("Zoom Spheres (gaze-dwell, replaces wink over time)")]
        [Tooltip("Head-locked sphere that starts continuous zoom-IN once gazed at for zoomDwellSeconds.")]
        [SerializeField] Transform zoomSphereRight;

        [Tooltip("Head-locked sphere that starts continuous zoom-OUT once gazed at for zoomDwellSeconds.")]
        [SerializeField] Transform zoomSphereLeft;

        [Tooltip("Angular radius (degrees) around a zoom sphere's actual position within which gaze " +
                 "counts as \"looking at it\". A fixed radius rather than the arrow-pad's per-arrow " +
                 "threshold, because a diagonal circular target doesn't fit the left/right/up/down " +
                 "dominant-axis model.")]
        [SerializeField, Range(5f, 30f)] float zoomActivationAngleDeg = 12f;

        [Tooltip("Seconds of continuous gaze on a zoom sphere before it arms and starts changing FOV.")]
        [SerializeField] float zoomDwellSeconds = 1.5f;

        [Tooltip("Color tint applied to a zoom sphere while its dwell is armed (>= zoomDwellSeconds).")]
        [SerializeField] Color zoomSphereHighlightColor = new Color(0.62f, 0.2f, 0.86f, 1f);

        Color m_BaseColorZoomRight, m_BaseColorZoomLeft;
        float m_ZoomInDwellTimer;
        float m_ZoomOutDwellTimer;
```

- [ ] **Step 2: Capture base sphere colors in `Awake()`**

`Awake()` currently ends with (lines 110-116):

```csharp
            // Drive each arrow's screen position from the distances above rather than
            // whatever anchoredPosition is baked into the scene.
            if (arrowLeft != null)  ((RectTransform)arrowLeft.transform).anchoredPosition  = new Vector2(-arrowDistanceLeft, 0f);
            if (arrowRight != null) ((RectTransform)arrowRight.transform).anchoredPosition = new Vector2(arrowDistanceRight, 0f);
            if (arrowUp != null)    ((RectTransform)arrowUp.transform).anchoredPosition    = new Vector2(0f, arrowDistanceUp);
            if (arrowDown != null)  ((RectTransform)arrowDown.transform).anchoredPosition  = new Vector2(0f, -arrowDistanceDown);
        }
```

Add the sphere color capture right before the closing brace:

```csharp
            // Drive each arrow's screen position from the distances above rather than
            // whatever anchoredPosition is baked into the scene.
            if (arrowLeft != null)  ((RectTransform)arrowLeft.transform).anchoredPosition  = new Vector2(-arrowDistanceLeft, 0f);
            if (arrowRight != null) ((RectTransform)arrowRight.transform).anchoredPosition = new Vector2(arrowDistanceRight, 0f);
            if (arrowUp != null)    ((RectTransform)arrowUp.transform).anchoredPosition    = new Vector2(0f, arrowDistanceUp);
            if (arrowDown != null)  ((RectTransform)arrowDown.transform).anchoredPosition  = new Vector2(0f, -arrowDistanceDown);

            // Remember each zoom sphere's authored rest color, same reasoning as the
            // arrows' base colors above.
            if (zoomSphereRight != null)
            {
                var renderer = zoomSphereRight.GetComponent<Renderer>();
                if (renderer != null) m_BaseColorZoomRight = renderer.sharedMaterial.color;
            }
            if (zoomSphereLeft != null)
            {
                var renderer = zoomSphereLeft.GetComponent<Renderer>();
                if (renderer != null) m_BaseColorZoomLeft = renderer.sharedMaterial.color;
            }
        }
```

- [ ] **Step 3: Add the detection helper**

Add this new method right after `Awake()` closes (before the `#if UNITY_EDITOR` gizmo block that currently follows):

```csharp
        // Fixed angular radius around the target's actual world position — unlike
        // GetDirectionThreshold (arrow-pad), this isn't picking a dominant axis, just
        // "is the gaze within N degrees of this point in space".
        bool IsGazingAtTarget(Transform target, Vector3 worldGazeDir)
        {
            if (target == null || headTransform == null) return false;
            Vector3 toTarget = target.position - headTransform.position;
            return Vector3.Angle(worldGazeDir, toTarget) <= zoomActivationAngleDeg;
        }
```

- [ ] **Step 4: Verify in the Unity Editor**

Let Unity recompile. Console must show zero errors. `IsGazingAtTarget` is not called yet (Task 3 wires it in) so there is no runtime behavior change to check yet — this step is a compile-only checkpoint.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Gaze/Pico/PicoInteractionController.cs
git commit -m "Add zoom-sphere fields and gaze-on-target detection helper"
```

---

### Task 3: Dwell timers, continuous zoom, and Update() wiring

**Files:**
- Modify: `Assets/Scripts/Gaze/Pico/PicoInteractionController.cs` (`Update()` lines 174-210, `UpdateScrollAndArrows` lines 233-258)

**Interfaces:**
- Consumes: `IsGazingAtTarget` and fields from Task 2; existing `zoomSpeed`, `minFOV`, `maxFOV`, `proxyCamera` fields.
- Produces: `void UpdateZoomSpheres(Vector3 worldGazeDir)`, `void SetSphereActive(Transform sphere, Color baseColor, bool active)`.

- [ ] **Step 1: Add `UpdateZoomSpheres` and `SetSphereActive`**

Add these two new methods directly after `UpdateScrollAndArrows` closes and before `ApplyGazeComponents` begins (i.e. right after line 258 `}` that closes `UpdateScrollAndArrows`):

```csharp
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

            SetSphereActive(zoomSphereRight, m_BaseColorZoomRight, m_ZoomInDwellTimer  >= zoomDwellSeconds);
            SetSphereActive(zoomSphereLeft,  m_BaseColorZoomLeft,  m_ZoomOutDwellTimer >= zoomDwellSeconds);
        }

        void SetSphereActive(Transform sphere, Color baseColor, bool active)
        {
            if (sphere == null) return;
            Renderer renderer = sphere.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.material.color = active ? zoomSphereHighlightColor : baseColor;
        }

        void ResetZoomSphereDwell()
        {
            m_ZoomInDwellTimer = 0f;
            m_ZoomOutDwellTimer = 0f;
            SetSphereActive(zoomSphereRight, m_BaseColorZoomRight, false);
            SetSphereActive(zoomSphereLeft,  m_BaseColorZoomLeft,  false);
        }
```

- [ ] **Step 2: Compute world gaze direction and call `UpdateZoomSpheres` from `UpdateScrollAndArrows`**

`UpdateScrollAndArrows` currently ends with (lines 250-258):

```csharp
            // PICO's combined eye gaze x/y come out mirrored relative to head-forward
            // (confirmed on-device: looking right lit the left arrow, looking up lit
            // down) — negate both so positive means right/up like the rest of the code
            // below assumes.
            float rightComp = -localDir.x;
            float upComp    = -localDir.y;

            ApplyGazeComponents(rightComp, upComp);
        }
```

Change it to also compute and forward the world-space gaze direction:

```csharp
            // PICO's combined eye gaze x/y come out mirrored relative to head-forward
            // (confirmed on-device: looking right lit the left arrow, looking up lit
            // down) — negate both so positive means right/up like the rest of the code
            // below assumes.
            float rightComp = -localDir.x;
            float upComp    = -localDir.y;

            ApplyGazeComponents(rightComp, upComp);

            // Zoom-sphere detection needs the actual world-space gaze ray (not just the
            // rightComp/upComp screen-space projection used by the arrow-pad), since the
            // spheres sit off both cardinal axes.
            Vector3 worldGazeDir = headTransform.rotation * localDir;
            UpdateZoomSpheres(worldGazeDir);
        }
```

- [ ] **Step 3: Reset dwell state when there is no usable gaze data**

`Update()` currently has this block (lines 204-207):

```csharp
            else
            {
                SetAllArrowAlpha(0f);
            }
```

Change it to also reset the zoom-sphere dwell:

```csharp
            else
            {
                SetAllArrowAlpha(0f);
                ResetZoomSphereDwell();
            }
```

Also reset dwell state in the two editor-preview branches, since those don't produce a `worldGazeDir` (dwell zoom is on-device-only per the design spec). `Update()` currently has (lines 192-203):

```csharp
#if UNITY_EDITOR
            else if (editorPreviewSource == EditorPreviewSource.Keyboard && Keyboard.current != null)
            {
                GetKeyboardGazeComponents(out float rightComp, out float upComp);
                ApplyGazeComponents(rightComp, upComp);
            }
            else if (editorPreviewSource == EditorPreviewSource.Mouse && Mouse.current != null)
            {
                GetMouseGazeComponents(out float rightComp, out float upComp);
                ApplyGazeComponents(rightComp, upComp);
            }
#endif
```

Change to:

```csharp
#if UNITY_EDITOR
            else if (editorPreviewSource == EditorPreviewSource.Keyboard && Keyboard.current != null)
            {
                GetKeyboardGazeComponents(out float rightComp, out float upComp);
                ApplyGazeComponents(rightComp, upComp);
                ResetZoomSphereDwell();
            }
            else if (editorPreviewSource == EditorPreviewSource.Mouse && Mouse.current != null)
            {
                GetMouseGazeComponents(out float rightComp, out float upComp);
                ApplyGazeComponents(rightComp, upComp);
                ResetZoomSphereDwell();
            }
#endif
```

- [ ] **Step 4: Verify in the Unity Editor**

Let Unity recompile. Console must show zero errors. This is a compile-only checkpoint — `zoomSphereRight`/`zoomSphereLeft` are still unassigned in the scene (Task 5 wires them), so `IsGazingAtTarget` returns `false` for both and no runtime behavior changes yet.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Gaze/Pico/PicoInteractionController.cs
git commit -m "Add gaze-dwell zoom sphere logic (dwell timers, continuous FOV change)"
```

---

### Task 4: Scene-setup support for fresh scenes

**Files:**
- Modify: `Assets/Scripts/Gaze/Editor/GazeSceneSetup.cs:296-354` (`AddPicoInteraction`, `CreateEdgeArrow`)

**Interfaces:**
- Consumes: `PicoInteractionController` fields `zoomSphereRight`/`zoomSphereLeft` from Task 2; existing `SetObjectField` helper (line 391).
- Produces: `static Transform CreateZoomSphere(Transform parent, string name, Vector3 localPosition, float scale, Color baseColor)`.

- [ ] **Step 1: Add the sphere-creation helper**

Add this new method directly after `CreateEdgeArrow` closes (after line 354):

```csharp
    static Transform CreateZoomSphere(Transform parent, string name, Vector3 localPosition, float scale, Color baseColor)
    {
        GameObject sphereGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereGO.name = name;
        Undo.RegisterCreatedObjectUndo(sphereGO, "Gaze Setup");
        sphereGO.transform.SetParent(parent, false);
        sphereGO.transform.localPosition = localPosition;
        sphereGO.transform.localRotation = Quaternion.identity;
        sphereGO.transform.localScale = Vector3.one * scale;

        var renderer = sphereGO.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Standard")) { color = baseColor };
        renderer.sharedMaterial = mat;

        return sphereGO.transform;
    }
```

- [ ] **Step 2: Create and wire the two spheres in `AddPicoInteraction`**

`AddPicoInteraction` currently ends its `if (arrowParent != null)` block with (lines 330-335):

```csharp
            Sprite arrowSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd");
            SetObjectField(ctrl, "arrowLeft",  CreateEdgeArrow(canvasGO.transform, "Arrow_Left",  arrowSprite, new Vector2(-300f, 0f), -90f));
            SetObjectField(ctrl, "arrowRight", CreateEdgeArrow(canvasGO.transform, "Arrow_Right", arrowSprite, new Vector2(300f, 0f), 90f));
            SetObjectField(ctrl, "arrowUp",    CreateEdgeArrow(canvasGO.transform, "Arrow_Up",    arrowSprite, new Vector2(0f, 300f), 180f));
            SetObjectField(ctrl, "arrowDown",  CreateEdgeArrow(canvasGO.transform, "Arrow_Down",  arrowSprite, new Vector2(0f, -300f), 0f));
        }
```

Change it to also create the two zoom spheres, parented to the same `arrowParent` (so they're head-locked the same way as the arrow canvas) but closer (0.8 m vs the arrow canvas's 1.2 m) and in the bottom diagonals:

```csharp
            Sprite arrowSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd");
            SetObjectField(ctrl, "arrowLeft",  CreateEdgeArrow(canvasGO.transform, "Arrow_Left",  arrowSprite, new Vector2(-300f, 0f), -90f));
            SetObjectField(ctrl, "arrowRight", CreateEdgeArrow(canvasGO.transform, "Arrow_Right", arrowSprite, new Vector2(300f, 0f), 90f));
            SetObjectField(ctrl, "arrowUp",    CreateEdgeArrow(canvasGO.transform, "Arrow_Up",    arrowSprite, new Vector2(0f, 300f), 180f));
            SetObjectField(ctrl, "arrowDown",  CreateEdgeArrow(canvasGO.transform, "Arrow_Down",  arrowSprite, new Vector2(0f, -300f), 0f));

            // Bottom diagonals, closer than the 1.2 m arrow canvas, so they don't
            // overlap the arrow-pad's activation zones.
            Transform zoomIn  = CreateZoomSphere(arrowParent, "ZoomSphere_In",  new Vector3(0.25f, -0.35f, 0.8f), 0.05f, Color.white);
            Transform zoomOut = CreateZoomSphere(arrowParent, "ZoomSphere_Out", new Vector3(-0.25f, -0.35f, 0.8f), 0.05f, Color.white);
            SetObjectField(ctrl, "zoomSphereRight", zoomIn);
            SetObjectField(ctrl, "zoomSphereLeft",  zoomOut);
        }
```

- [ ] **Step 3: Verify in the Unity Editor**

Let Unity recompile. Console must show zero errors. This method only runs for a scene that does NOT already have a `Gaze Pico Interaction` object (see the early-return guard at the top of `AddPicoInteraction`), so it will not affect the already-configured `SampleScene` — confirmed by inspecting `SampleScene.unity` and finding `Gaze Pico Interaction` already present. This step is a compile-only checkpoint; functional verification of this method happens on a scene that doesn't have the rig yet, which is out of scope for this plan (`SampleScene` is the only scene in the project).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Gaze/Editor/GazeSceneSetup.cs
git commit -m "Auto-create zoom spheres in GazeSceneSetup for fresh scenes"
```

---

### Task 5: Wire the spheres into SampleScene and verify on-device

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity` (editor-driven changes, not hand-edited YAML)

`SampleScene` already has a configured `Gaze Pico Interaction` object with hand-tuned arrow distances and gaze gain (see `arrowLeft` etc. wired at `Assets/Scenes/SampleScene.unity:7876`). `GazeSceneSetup`'s one-click command is a no-op here (Task 4's Step 3), so the two spheres must be added by hand in the Unity Editor to avoid disturbing the already-tuned rig.

- [ ] **Step 1: Locate the arrow canvas's parent in the Hierarchy**

In the Unity Editor, open `SampleScene`, find `Gaze Pico Interaction` in the Hierarchy, and select it. In its Inspector, note what `Arrow Left` etc. are parented under (a canvas named `UI_GazeEdgeArrows`, itself parented to the Proxy Camera). The two new spheres must go under that same parent transform as `UI_GazeEdgeArrows` (i.e. the Proxy Camera), so they are head-locked the same way.

- [ ] **Step 2: Create the two spheres**

For each of the two spheres:
1. Right-click the Proxy Camera in the Hierarchy → `3D Object > Sphere`.
2. Rename to `ZoomSphere_In` (right one) and `ZoomSphere_Out` (left one).
3. Set `Transform > Local Position`:
   - `ZoomSphere_In`: `(0.25, -0.35, 0.8)`
   - `ZoomSphere_Out`: `(-0.25, -0.35, 0.8)`
4. Set `Transform > Local Scale` to `(0.05, 0.05, 0.05)` on both.
5. Leave the default `Sphere Collider` in place (unused by detection, harmless).

- [ ] **Step 3: Wire the fields**

Select `Gaze Pico Interaction` in the Hierarchy. In its Inspector, under `Zoom Spheres (gaze-dwell, replaces wink over time)`:
- Drag `ZoomSphere_In` into `Zoom Sphere Right`.
- Drag `ZoomSphere_Out` into `Zoom Sphere Left`.

Save the scene (`Ctrl+S`).

- [ ] **Step 4: Build and deploy to Pico 4 Pro**

Follow the project's existing device build process (Build Settings → Android → Build And Run). Per this repo's convention: confirm the build compiles with zero errors before assuming anything changed on-device — do not skip straight to on-device testing if the build step itself failed.

- [ ] **Step 5: On-device verification**

With the headset on and eye tracking active:
1. Look at the right sphere (`ZoomSphere_In`) and hold for 1.5 s → FOV should narrow (zoom in) continuously while the gaze stays on it, and the sphere should visibly tint to the highlight color once armed.
2. Look at the left sphere (`ZoomSphere_Out`) and hold for 1.5 s → FOV should widen (zoom out) continuously while held, same visual tint.
3. Look away from a sphere before 1.5 s elapses (repeat a few short glances) → confirm no zoom happens and the sphere never tints.
4. While looking at the arrow-pad's four directions, confirm the zoom spheres do NOT also arm (no unwanted simultaneous zoom). If they do, reduce `zoomActivationAngleDeg` or move the sphere local positions further from the arrow directions, on the `Gaze Pico Interaction` Inspector — no code change needed, both are `[SerializeField]` values.
5. Confirm wink-to-zoom still works (flag left on from Task 1) and doesn't fight with sphere-dwell zoom.

- [ ] **Step 6: Commit the scene change**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "Wire zoom spheres into SampleScene's Gaze Pico Interaction rig"
```

---

## Self-Review Notes

- **Spec coverage:** angular detection (Task 2), dwell + continuous zoom reusing existing FOV code (Task 3), wink gated not deleted (Task 1), scene setup automation (Task 4), bottom-diagonal placement closer than the arrow canvas (Task 4 Step 2, Task 5 Step 2), on-device-only limitation for editor preview documented and handled (Task 3 Step 3), manual on-device test plan (Task 5) — all covered.
- **Placeholder scan:** no TBD/TODO; every step has literal code or literal editor instructions.
- **Type consistency:** `IsGazingAtTarget(Transform, Vector3)` (Task 2) is called from `UpdateZoomSpheres(Vector3)` (Task 3) with matching signature; `SetSphereActive(Transform, Color, bool)` used consistently in both `UpdateZoomSpheres` and `ResetZoomSphereDwell`; `CreateZoomSphere(Transform, string, Vector3, float, Color)` (Task 4) signature matches its two call sites.
