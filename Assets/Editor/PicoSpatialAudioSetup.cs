// One-click setup for PICO Spatial Audio (PXR_Audio_Spatializer_*), wiring the
// existing scene to Pico's binaural/HRTF engine:
//   Tools > Audio > Set Up Pico Spatial Audio In Open Scene
//
// Adds, via reflection (so this never hard-references the Pico package and never
// breaks compilation if it is absent):
//   1. PXR_Audio_Spatializer_Context  on a "Pico Spatial Audio" GameObject (engine)
//   2. PXR_Audio_Spatializer_AudioListener on the active AudioListener's GameObject
//   3. PXR_Audio_Spatializer_AudioSource on every AudioSource child of the wired
//      PlatformManager.audioSourceObject (the "Day"/"Night" stem container)
//
// Pairs with Platform Manager.cs, which sets spatialBlend = 0 (dry) on sources
// that carry the PXR source component (PXR does the spatialization), and 1 (Unity
// paneo) otherwise.
//
// To revert to plain Unity paneo: delete the "Pico Spatial Audio" object and the
// PXR_Audio_* components (or just disable the Context).

using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PicoSpatialAudioSetup
{
    const string ContextTypeName  = "PXR_Audio_Spatializer_Context, Pico.Spatializer";
    const string ListenerTypeName = "PXR_Audio_Spatializer_AudioListener, Pico.Spatializer";
    const string SourceTypeName   = "PXR_Audio_Spatializer_AudioSource, Pico.Spatializer";

    [MenuItem("Tools/Audio/Set Up Pico Spatial Audio In Open Scene")]
    public static void Setup()
    {
        Type ctxType      = Type.GetType(ContextTypeName);
        Type listenerType = Type.GetType(ListenerTypeName);
        Type sourceType   = Type.GetType(SourceTypeName);

        if (ctxType == null || listenerType == null || sourceType == null)
        {
            Debug.LogError("[PicoSpatialAudioSetup] Pico Spatial Audio SDK (assembly 'Pico.Spatializer') not found. " +
                           "Make sure the PICO Integration SDK with SpatialAudio is imported.");
            return;
        }

        var platformManager = UnityEngine.Object.FindFirstObjectByType<PlatformManager>(FindObjectsInactive.Include);
        if (platformManager == null)
        {
            Debug.LogError("[PicoSpatialAudioSetup] No PlatformManager in the open scene. Open SampleScene first.");
            return;
        }

        GameObject sourceRoot = platformManager.audioSourceObject;
        if (sourceRoot == null)
        {
            Debug.LogError("[PicoSpatialAudioSetup] PlatformManager.audioSourceObject is not assigned.");
            return;
        }

        // 1. Context (engine) — one per scene.
        if (UnityEngine.Object.FindFirstObjectByType(ctxType, FindObjectsInactive.Include) == null)
        {
            var ctxGO = new GameObject("Pico Spatial Audio");
            Undo.RegisterCreatedObjectUndo(ctxGO, "Pico Spatial Audio Setup");
            Undo.AddComponent(ctxGO, ctxType);
            Debug.Log("[PicoSpatialAudioSetup] Added PXR_Audio_Spatializer_Context (engine).");
        }
        else
        {
            Debug.Log("[PicoSpatialAudioSetup] Context already present — kept.");
        }

        // 2. PXR listener on the Proxy Camera. That is the camera that renders in the
        // headset and carries the AudioListener Unity actually uses, so its
        // OnAudioFilterRead runs and the binaural mix is heard. Targeting xrOrigin
        // before landed on an inactive camera -> the listener never ran -> silence.
        // First clear any stray PXR listener placed elsewhere.
        foreach (UnityEngine.Object stray in UnityEngine.Object.FindObjectsByType(listenerType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            Undo.DestroyObjectImmediate(stray);

        GameObject listenerGO = platformManager.proxyCamera != null ? platformManager.proxyCamera.gameObject : null;
        if (listenerGO != null)
        {
            if (listenerGO.GetComponent<AudioListener>() == null)
                Debug.LogWarning($"[PicoSpatialAudioSetup] '{listenerGO.name}' has no AudioListener; the PXR listener will add one (watch for multiple-listener warnings).");
            Undo.AddComponent(listenerGO, listenerType);
            Debug.Log($"[PicoSpatialAudioSetup] Added PXR listener to Proxy Camera '{listenerGO.name}'.");
        }
        else
        {
            Debug.LogWarning("[PicoSpatialAudioSetup] PlatformManager.proxyCamera not assigned — PXR listener NOT added.");
        }

        // 3. PXR source on every AudioSource under the wired container.
        int added = 0, total = 0;
        foreach (AudioSource src in sourceRoot.GetComponentsInChildren<AudioSource>(true))
        {
            total++;
            if (src.GetComponent(sourceType) == null)
            {
                Undo.AddComponent(src.gameObject, sourceType);
                added++;
            }
        }
        Debug.Log($"[PicoSpatialAudioSetup] PXR source component on {total} AudioSource(s) ({added} newly added) under '{sourceRoot.name}'.");

        // 4. Flag PlatformManager so it feeds the stems dry (spatialBlend 0) at
        // runtime. A serialized bool, read directly — no il2cpp reflection, which
        // returns null in the built player and silently broke the previous attempt.
        SetBool(platformManager, "usePicoSpatialAudio", true);

        EditorSceneManager.MarkSceneDirty(platformManager.gameObject.scene);
        Debug.Log("[PicoSpatialAudioSetup] Done. SAVE the scene. Then Build And Run; PlatformManager feeds these sources dry so PXR spatializes them.");
    }

    [MenuItem("Tools/Audio/Remove Pico Spatial Audio From Open Scene")]
    public static void Remove()
    {
        Type ctxType      = Type.GetType(ContextTypeName);
        Type listenerType = Type.GetType(ListenerTypeName);
        Type sourceType   = Type.GetType(SourceTypeName);

        int comps = 0, objs = 0;

        // Remove per-source and listener PXR components.
        foreach (Type t in new[] { sourceType, listenerType })
        {
            if (t == null) continue;
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.DestroyObjectImmediate(o);
                comps++;
            }
        }

        // Remove the Context: destroy its whole GameObject if it is the dedicated
        // "Pico Spatial Audio" holder, else just the component.
        if (ctxType != null)
        {
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(ctxType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var comp = o as Component;
                if (comp != null && comp.gameObject.name == "Pico Spatial Audio" &&
                    comp.gameObject.GetComponents<Component>().Length <= 2) // Transform + Context
                {
                    Undo.DestroyObjectImmediate(comp.gameObject);
                    objs++;
                }
                else
                {
                    Undo.DestroyObjectImmediate(o);
                    comps++;
                }
            }
        }

        var pm = UnityEngine.Object.FindFirstObjectByType<PlatformManager>(FindObjectsInactive.Include);
        if (pm != null)
            SetBool(pm, "usePicoSpatialAudio", false);

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[PicoSpatialAudioSetup] Removed Pico Spatial Audio: {comps} component(s), {objs} object(s). " +
                  "SAVE the scene, then Build And Run — sources return to Unity paneo and should sound again.");
    }

    static void SetBool(UnityEngine.Object target, string field, bool value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop != null)
        {
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning($"[PicoSpatialAudioSetup] Field '{field}' not found on {target.GetType().Name}.");
        }
    }

}
