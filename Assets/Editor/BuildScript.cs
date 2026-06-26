using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

public static class BuildScript
{
    const string ProfilePath = "Assets/Settings/Build Profiles/PICO.asset";
    const string OutputPath  = "Builds/Android/Concert360.apk";

    [MenuItem("Build/Build PICO APK")]
    public static void BuildPico()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
        if (profile == null)
        {
            Debug.LogError($"[BuildScript] Build profile not found at {ProfilePath}");
            return;
        }

        BuildProfile.SetActiveBuildProfile(profile);

        var options = new BuildPlayerOptions
        {
            scenes           = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = OutputPath,
            target           = BuildTarget.Android,
            options          = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[BuildScript] Result: {report.summary.result} — {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings");
    }
}
