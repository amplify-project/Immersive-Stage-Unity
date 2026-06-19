using System;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Gaze.Core;

/// <summary>
/// One-click scene setup for the gaze-dwell interaction:
///   Tools > Gaze > Set Up Gaze Interaction In Open Scene
/// Creates the Gaze Interaction rig (provider + clicker + reticle) and the
/// UI_GazeMenu world-space canvas (Play/Pause + Debug Log buttons), wiring all
/// references and persistent onClick events. Platform components (Pico /
/// visionOS) are added via reflection only when their assemblies are compiled,
/// so this script never hard-references platform SDKs.
/// </summary>
public static class GazeSceneSetup
{
    // UI_DebugMenu is Screen Space-Camera bound to the (disabled-in-VR) tablet
    // camera, so it is invisible in a headset. Converting it to world space makes
    // the Show/Hide Log button actually useful on device.
    const bool ConvertDebugCanvasToWorldSpace = true;

    [MenuItem("Tools/Gaze/Set Up Gaze Interaction In Open Scene")]
    public static void Setup()
    {
        PlatformManager platformManager = UnityEngine.Object.FindFirstObjectByType<PlatformManager>(FindObjectsInactive.Include);
        if (platformManager == null)
        {
            Debug.LogError("[GazeSceneSetup] No PlatformManager found in the open scene. Open SampleScene first.");
            return;
        }

        Camera proxyCamera = platformManager.proxyCamera;

        // Re-running only adds the platform components that were skipped because
        // their assembly wasn't compiled (run once per build target: Android adds
        // the Pico provider, visionOS adds the visionOS setup).
        GameObject existingRig = GameObject.Find("Gaze Interaction");
        if (existingRig != null)
        {
            Debug.Log("[GazeSceneSetup] 'Gaze Interaction' already exists - only adding missing platform components.");
            AddPlatformComponents(existingRig, platformManager, proxyCamera);
            EnsureMenuAnchor(proxyCamera);
            EditorSceneManager.MarkSceneDirty(existingRig.scene);
            return;
        }
        GameObject debugMenu = FindDebugMenu(platformManager);

        // ---------- Gaze Interaction rig ----------
        var rigGO = new GameObject("Gaze Interaction");
        Undo.RegisterCreatedObjectUndo(rigGO, "Gaze Setup");

        var headGaze = rigGO.AddComponent<HeadGazeProvider>();
        SetObjectField(headGaze, "gazeCamera", proxyCamera);

        GazeReticle reticle = CreateReticle(rigGO.transform);

        var clicker = rigGO.AddComponent<GazeDwellUIClicker>();
        SetObjectField(clicker, "fallbackProvider", headGaze);
        SetObjectField(clicker, "projectionCamera", proxyCamera);
        SetObjectField(clicker, "reticle", reticle);
        // Eyes-only (ELA/locked-in): reduce dwell time for faster interaction.
        var so = new SerializedObject(clicker);
        so.FindProperty("dwellSeconds").floatValue = 1.5f;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ---------- UI_GazeMenu ----------
        Vector3 anchorPos;
        Quaternion anchorRot;
        if (proxyCamera != null)
        {
            Transform camT = proxyCamera.transform;
            Vector3 flatForward = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
            if (flatForward == Vector3.zero) flatForward = Vector3.forward;
            anchorPos = camT.position + flatForward * 3.5f;
            anchorRot = Quaternion.LookRotation(flatForward, Vector3.up);
        }
        else
        {
            anchorPos = new Vector3(0f, 1.5f, 3.5f);
            anchorRot = Quaternion.identity;
        }

        GameObject menuGO = CreateMenuCanvas(anchorPos, anchorRot, out RectTransform menuRect);

        var videoToggle = menuGO.AddComponent<VideoPlaybackToggle>();
        SetObjectField(videoToggle, "videoPlayer", platformManager.videoPlayer);
        SetObjectField(videoToggle, "audioSourceRoot", platformManager.audioSourceObject);

        var logToggle = menuGO.AddComponent<GameObjectToggle>();
        SetObjectField(logToggle, "target", debugMenu);

        // The rig switch / zoom arm can move the runtime camera far from its
        // edit-time pose; the anchor re-places the menu in view at startup.
        var menuAnchor = menuGO.AddComponent<GazeMenuAnchor>();
        SetObjectField(menuAnchor, "projectionCamera", proxyCamera);

        // Eyes-only (ELA): large central Play/Pause button, Debug Log below.
        CreateMenuButton(menuRect, "Button_PlayPause", "PLAY / PAUSE", new Vector2(0f, 100f), videoToggle.Toggle, largeSize: true);
        CreateMenuButton(menuRect, "Button_DebugLog", "Debug Log", new Vector2(0f, -200f), logToggle.Toggle, largeSize: false);

        // ---------- Debug canvas conversion (headset visibility) ----------
        if (ConvertDebugCanvasToWorldSpace && debugMenu != null)
        {
            Canvas debugCanvas = debugMenu.GetComponent<Canvas>();
            if (debugCanvas != null && debugCanvas.renderMode != RenderMode.WorldSpace)
            {
                Undo.RecordObject(debugCanvas, "Gaze Setup");
                debugCanvas.renderMode = RenderMode.WorldSpace;
                var rt = (RectTransform)debugCanvas.transform;
                Undo.RecordObject(rt, "Gaze Setup");
                rt.sizeDelta = new Vector2(800f, 600f);
                rt.localScale = Vector3.one * 0.0025f;
                rt.position = anchorPos + anchorRot * new Vector3(-2.5f, 0.4f, 0f);
                rt.rotation = anchorRot * Quaternion.Euler(0f, -25f, 0f);
                Debug.Log("[GazeSceneSetup] UI_DebugMenu converted to World Space so it is visible in the headset.");
            }
        }

        // ---------- Platform components (reflection: only when their assembly compiles) ----------
        AddPlatformComponents(rigGO, platformManager, proxyCamera);

        EditorSceneManager.MarkSceneDirty(rigGO.scene);
        Debug.Log("[GazeSceneSetup] Done. Created 'Gaze Interaction' + 'UI_GazeMenu'. Save the scene to persist.");
    }

    // ----------------------------------------------------------------------

    static GazeReticle CreateReticle(Transform parent)
    {
        var reticleGO = new GameObject("Gaze Reticle", typeof(Canvas));
        reticleGO.transform.SetParent(parent, false);
        var canvas = reticleGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        // 64 px rect scaled to exactly 1 world unit; GazeReticle rescales the root
        // per-frame for constant angular size. No GraphicRaycaster: never blocks UI.
        var rect = (RectTransform)reticleGO.transform;
        rect.sizeDelta = new Vector2(64f, 64f);
        rect.localScale = Vector3.one / 64f;

        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        Image ring = CreateImage(reticleGO.transform, "Ring", knob, new Vector2(18f, 18f));
        ring.color = new Color(1f, 1f, 1f, 0.85f);

        Image progress = CreateImage(reticleGO.transform, "Progress", knob, new Vector2(44f, 44f));
        progress.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        progress.type = Image.Type.Filled;
        progress.fillMethod = Image.FillMethod.Radial360;
        progress.fillOrigin = (int)Image.Origin360.Top;
        progress.fillClockwise = true;
        progress.fillAmount = 0f;

        var reticle = reticleGO.AddComponent<GazeReticle>();
        SetObjectField(reticle, "progressImage", progress);
        SetObjectField(reticle, "ringImage", ring);
        return reticle;
    }

    static Image CreateImage(Transform parent, string name, Sprite sprite, Vector2 size)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        ((RectTransform)go.transform).sizeDelta = size;
        return image;
    }

    static GameObject CreateMenuCanvas(Vector3 position, Quaternion rotation, out RectTransform menuRect)
    {
        var menuGO = new GameObject("UI_GazeMenu", typeof(Canvas), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(menuGO, "Gaze Setup");
        var canvas = menuGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        menuRect = (RectTransform)menuGO.transform;
        // Eyes-only (ELA): larger canvas to fit big buttons. ~2.4 m wide at 3.5 m distance.
        menuRect.sizeDelta = new Vector2(1000f, 600f);
        menuRect.localScale = Vector3.one * 0.0025f;
        menuRect.SetPositionAndRotation(position, rotation);

        Sprite bg = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        Image panel = CreateImage(menuGO.transform, "Panel", bg, menuRect.sizeDelta);
        panel.type = Image.Type.Sliced;
        panel.color = new Color(0f, 0f, 0f, 0.55f);

        return menuGO;
    }

    static void CreateMenuButton(RectTransform parent, string name, string label, Vector2 anchoredPos, UnityAction onClick, bool largeSize = false)
    {
        var resources = new DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd")
        };
        GameObject buttonGO = DefaultControls.CreateButton(resources);
        buttonGO.name = name;
        buttonGO.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(buttonGO, "Gaze Setup");

        var rect = (RectTransform)buttonGO.transform;
        // Eyes-only control: large buttons are much easier to target with eye gaze.
        rect.sizeDelta = largeSize ? new Vector2(600f, 250f) : new Vector2(300f, 130f);
        rect.anchoredPosition = anchoredPos;

        var text = buttonGO.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.text = label;
            text.fontSize = largeSize ? 72 : 46;
            text.color = new Color(0.1f, 0.1f, 0.1f);
        }

        var button = buttonGO.GetComponent<Button>();
        UnityEventTools.AddVoidPersistentListener(button.onClick, onClick);
    }

    static GameObject FindDebugMenu(PlatformManager platformManager)
    {
        if (platformManager.logContentRoot != null)
        {
            Canvas canvas = platformManager.logContentRoot.GetComponentInParent<Canvas>(true);
            if (canvas != null)
                return canvas.gameObject;
        }
        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.gameObject.name == "UI_DebugMenu")
                return canvas.gameObject;
        }
        Debug.LogWarning("[GazeSceneSetup] UI_DebugMenu not found - Debug Log button left unwired.");
        return null;
    }

    static void AddPlatformComponents(GameObject rigGO, PlatformManager platformManager, Camera proxyCamera)
    {
        // Pico (compiled only on Android target with PICO_XR define + PICO SDK)
        Type picoType = Type.GetType("Gaze.Pico.PicoEyeGazeProvider, Gaze.Pico");
        if (picoType != null)
        {
            Transform existingPico = rigGO.transform.Find("Gaze Pico");
            Component pico;
            if (existingPico == null)
            {
                var picoGO = new GameObject("Gaze Pico");
                picoGO.transform.SetParent(rigGO.transform, false);
                Undo.RegisterCreatedObjectUndo(picoGO, "Gaze Setup");
                pico = picoGO.AddComponent(picoType);
                Camera trackedCam = FindTrackedHeadCamera(platformManager, proxyCamera);
                SetObjectField(pico, "headTransform", trackedCam != null ? trackedCam.transform : null);
                Debug.Log("[GazeSceneSetup] Added PicoEyeGazeProvider (Gaze.Pico assembly present).");
            }
            else
            {
                pico = existingPico.GetComponent(picoType);
            }

            // Field added after early scenes were set up; (re)wire it on every run.
            if (pico != null)
                SetObjectField(pico, "projectionCamera", proxyCamera);

            AddPicoInteraction(rigGO, platformManager, proxyCamera);
        }
        else
        {
            Debug.Log("[GazeSceneSetup] Gaze.Pico not compiled (needs Android target + PICO_XR define + PICO SDK) - skipped. Re-run setup later or add the component manually.");
        }

        // visionOS (compiled in the Editor and on visionOS builds)
        Type visionType = Type.GetType("Gaze.VisionOS.VisionOSGazeSetup, Gaze.VisionOS");
        if (visionType != null && rigGO.transform.Find("Gaze VisionOS") == null)
        {
            var visionGO = new GameObject("Gaze VisionOS");
            visionGO.transform.SetParent(rigGO.transform, false);
            Undo.RegisterCreatedObjectUndo(visionGO, "Gaze Setup");
            Component vision = visionGO.AddComponent(visionType);
            SetObjectField(vision, "proxyCamera", proxyCamera);
            SetObjectField(vision, "trackedCamera", FindTrackedHeadCamera(platformManager, proxyCamera));
            Debug.Log("[GazeSceneSetup] Added VisionOSGazeSetup (Gaze.VisionOS assembly present).");
        }
        else
        {
            Debug.Log("[GazeSceneSetup] Gaze.VisionOS not compiled (needs visionOS build target) - skipped. Re-run setup later or add the component manually.");
        }
    }

    /// <summary>
    /// Edge-scroll arrows + wink zoom (PicoInteractionController) with a
    /// view-locked arrow canvas parented to the rendering camera.
    /// </summary>
    static void AddPicoInteraction(GameObject rigGO, PlatformManager platformManager, Camera proxyCamera)
    {
        Type ctrlType = Type.GetType("Gaze.Pico.PicoInteractionController, Gaze.Pico");
        if (ctrlType == null || rigGO.transform.Find("Gaze Pico Interaction") != null)
            return;

        var ctrlGO = new GameObject("Gaze Pico Interaction");
        ctrlGO.transform.SetParent(rigGO.transform, false);
        Undo.RegisterCreatedObjectUndo(ctrlGO, "Gaze Setup");
        Component ctrl = ctrlGO.AddComponent(ctrlType);

        Camera trackedCam = FindTrackedHeadCamera(platformManager, proxyCamera);
        SetObjectField(ctrl, "headTransform", trackedCam != null ? trackedCam.transform : null);
        SetObjectField(ctrl, "proxyCamera", proxyCamera);
        SetObjectField(ctrl, "sphere", FindVideoSphere());

        Transform arrowParent = proxyCamera != null
            ? proxyCamera.transform
            : (trackedCam != null ? trackedCam.transform : null);
        if (arrowParent != null)
        {
            var canvasGO = new GameObject("UI_GazeEdgeArrows", typeof(Canvas));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Gaze Setup");
            canvasGO.transform.SetParent(arrowParent, false);
            canvasGO.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            // 1 m square held 1.2 m in front of the camera; arrows sit ~20 deg
            // off-center. No GraphicRaycaster, images non-raycastable: the hints
            // can never block the gaze UI.
            var rect = (RectTransform)canvasGO.transform;
            rect.sizeDelta = new Vector2(1000f, 1000f);
            rect.localScale = Vector3.one * 0.001f;
            rect.localPosition = new Vector3(0f, 0f, 1.2f);
            rect.localRotation = Quaternion.identity;

            Sprite arrowSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd");
            SetObjectField(ctrl, "arrowLeft",  CreateEdgeArrow(canvasGO.transform, "Arrow_Left",  arrowSprite, new Vector2(-440f, 0f), -90f));
            SetObjectField(ctrl, "arrowRight", CreateEdgeArrow(canvasGO.transform, "Arrow_Right", arrowSprite, new Vector2(440f, 0f), 90f));
            SetObjectField(ctrl, "arrowUp",    CreateEdgeArrow(canvasGO.transform, "Arrow_Up",    arrowSprite, new Vector2(0f, 440f), 180f));
            SetObjectField(ctrl, "arrowDown",  CreateEdgeArrow(canvasGO.transform, "Arrow_Down",  arrowSprite, new Vector2(0f, -440f), 0f));
        }
        else
        {
            Debug.LogWarning("[GazeSceneSetup] No camera found to host the edge arrows - arrows left unwired.");
        }

        Debug.Log("[GazeSceneSetup] Added PicoInteractionController (edge-scroll arrows + wink zoom).");
    }

    static Image CreateEdgeArrow(Transform parent, string name, Sprite sprite, Vector2 anchoredPos, float zRotation)
    {
        // DropdownArrow points down; rotate per direction. Alpha starts at minAlpha (always visible),
        // PicoInteractionController drives opacity + position from edge-gaze strength.
        Image img = CreateImage(parent, name, sprite, new Vector2(120f, 120f));
        var rt = (RectTransform)img.transform;
        rt.anchoredPosition = anchoredPos;
        rt.localRotation = Quaternion.Euler(0f, 0f, zRotation);
        img.color = new Color(1f, 1f, 1f, 0.3f);  // minAlpha: always visible even at center
        return img;
    }

    static Transform FindVideoSphere()
    {
        GameObject sphere = GameObject.Find("Sphere");
        if (sphere == null)
            Debug.LogWarning("[GazeSceneSetup] 360 video 'Sphere' not found - edge-gaze scroll left unwired.");
        return sphere != null ? sphere.transform : null;
    }

    static void EnsureMenuAnchor(Camera proxyCamera)
    {
        GameObject menuGO = GameObject.Find("UI_GazeMenu");
        if (menuGO == null)
            return;
        var anchor = menuGO.GetComponent<GazeMenuAnchor>();
        if (anchor == null)
        {
            anchor = Undo.AddComponent<GazeMenuAnchor>(menuGO);
            Debug.Log("[GazeSceneSetup] Added GazeMenuAnchor to UI_GazeMenu - it now re-anchors in front of the runtime camera at startup.");
        }
        SetObjectField(anchor, "projectionCamera", proxyCamera);
    }

    static Camera FindTrackedHeadCamera(PlatformManager platformManager, Camera proxyCamera)
    {
        if (platformManager.xrOrigin == null)
            return null;
        foreach (Camera cam in platformManager.xrOrigin.GetComponentsInChildren<Camera>(true))
        {
            if (cam != proxyCamera)
                return cam;
        }
        return null;
    }

    /// <summary>Sets a private [SerializeField] through SerializedObject so the value persists in the scene.</summary>
    static void SetObjectField(Component component, string fieldName, UnityEngine.Object value)
    {
        var so = new SerializedObject(component);
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogWarning($"[GazeSceneSetup] Field '{fieldName}' not found on {component.GetType().Name}.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
