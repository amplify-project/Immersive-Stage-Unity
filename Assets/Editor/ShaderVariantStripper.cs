using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

// Strips shader variants that can never be reached in this 360 VR project.
// Runs automatically on every Player build via IPreprocessShaders.
public class ShaderVariantStripper : IPreprocessShaders
{
    // -----------------------------------------------------------------------
    // Shader name prefixes stripped entirely (all their variants removed).
    // Post Processing Stack is the biggest single contributor when the package
    // is present but no camera effects are active.
    // -----------------------------------------------------------------------
    static readonly string[] s_StripPrefixes =
    {
        // URP is installed (via XRI samples) but NOT the active render pipeline.
        // Built-in RP is active — all URP variants are dead weight.
        "Universal Render Pipeline/",
        "Hidden/Universal Render Pipeline/",

        // Post Processing Stack — package present but no active camera effects
        "Hidden/PostProcessing/",
        "Hidden/Post FX/",
        "Hidden/Fast Approximate Anti-aliasing",
        "Hidden/SMAA",
        "Hidden/TemporalAntialiasing",
        "Hidden/TextCore/",
    };

    // -----------------------------------------------------------------------
    // Keyword names: any variant that has at least one of these enabled is
    // removed. Using string comparison (via GetShaderKeywords().name) works
    // for BOTH global and local keywords in Unity 6, unlike the legacy
    // ShaderKeyword(string) constructor which only matched global keywords.
    // -----------------------------------------------------------------------
    static readonly HashSet<string> s_UnusedKeywords = new HashSet<string>
    {
        // Shadows — no shadow-casting lights in the scene
        "SHADOWS_SCREEN", "SHADOWS_DEPTH", "SHADOWS_CUBE",
        "SHADOWS_SOFT", "SHADOWS_SHADOWMASK",

        // Lightmaps — sphere uses Skybox/Panoramic, no baked GI
        "LIGHTMAP_ON", "DYNAMICLIGHTMAP_ON", "DIRLIGHTMAP_COMBINED",
        "LIGHTMAP_SHADOW_MIXING", "LIGHTPROBE_SH",

        // Fog — disabled in RenderSettings; inside a sphere fog is invisible
        "FOG_LINEAR", "FOG_EXP", "FOG_EXP2",

        // Unused light types
        "POINT", "SPOT", "DIRECTIONAL_COOKIE", "POINT_COOKIE", "VERTEXLIGHT_ON",
    };

    // Counters reset each build session (constructor called once per build).
    int m_In;
    int m_Out;
    int m_ShadersCalled;

    public int callbackOrder => 0;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        m_ShadersCalled++;
        int before = data.Count;
        m_In += before;

        // --- 1. Strip entire shader families by prefix ---
        foreach (string prefix in s_StripPrefixes)
        {
            if (shader.name.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                data.Clear();
                m_Out += 0;
                MaybeLogSummary();
                return;
            }
        }

        // --- 2. Strip variants with unused keywords (string-based, Unity 6 safe) ---
        for (int i = data.Count - 1; i >= 0; i--)
        {
            ShaderKeyword[] activeKws = data[i].shaderKeywordSet.GetShaderKeywords();
            foreach (ShaderKeyword kw in activeKws)
            {
                if (s_UnusedKeywords.Contains(kw.name))
                {
                    data.RemoveAt(i);
                    break;
                }
            }
        }

        m_Out += data.Count;
        MaybeLogSummary();
    }

    // Print a running total every 500 shaders so we can confirm the stripper
    // is active without flooding the console.
    void MaybeLogSummary()
    {
        if (m_ShadersCalled % 500 == 0 || m_ShadersCalled == 1)
            Debug.Log($"[ShaderStripper] active — {m_ShadersCalled} shaders processed, {m_In} variants in, {m_Out} out so far.");
    }
}
