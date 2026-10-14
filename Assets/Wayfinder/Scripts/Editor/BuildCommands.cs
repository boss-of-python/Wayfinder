using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Wayfinder.Editor
{
    /// <summary>
    /// Build entry points (§19.2, BUILD-01). Exact command lines are in Docs/BUILD_AND_RUN.md §4.
    /// Signing credentials NEVER live in this repository (§23.2).
    /// Artifact layout: Builds/Android/Wayfinder-dev.apk, Builds/iOS/Xcode project.
    /// </summary>
    public static class BuildCommands
    {
        static string[] ScenePaths
        {
            get
            {
                var scenes = new List<string>();
                foreach (var s in EditorBuildSettings.scenes)
                    if (s.enabled) scenes.Add(s.path);
                return scenes.ToArray();
            }
        }

        static void EnsureBuildDir(string platform)
        {
            Directory.CreateDirectory(Path.Combine("Builds", platform));
            Directory.CreateDirectory("Logs");
        }

        [MenuItem("Tools/Wayfinder/Build/Android Development")]
        public static void BuildAndroidDev() => BuildAndroid(development: true);

        [MenuItem("Tools/Wayfinder/Build/Android Release")]
        public static void BuildAndroidRelease() => BuildAndroid(development: false);

        static void BuildAndroid(bool development)
        {
            EnsureBuildDir("Android");
            string suffix = development ? "-dev" : "";
            var options = new BuildPlayerOptions
            {
                scenes = ScenePaths,
                locationPathName = $"Builds/Android/Wayfinder{suffix}.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            LogReport("Android", report);
        }

        [MenuItem("Tools/Wayfinder/Build/iOS Development")]
        public static void BuildIOSDev() => BuildIOS(development: true);

        [MenuItem("Tools/Wayfinder/Build/iOS Release")]
        public static void BuildIOSRelease() => BuildIOS(development: false);

        static void BuildIOS(bool development)
        {
            EnsureBuildDir("iOS");
            var options = new BuildPlayerOptions
            {
                scenes = ScenePaths,
                locationPathName = "Builds/iOS",
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = development ? BuildOptions.Development : BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            LogReport("iOS", report);
            Debug.Log("[Wayfinder] iOS Xcode project generated. Sign in Xcode with OWNER credentials " +
                      "(never commit them). Store submission is a separate owner-authorized process.");
        }

        static void LogReport(string platform, BuildReport report)
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {platform}: {report.summary.result}, " +
                          $"size={report.summary.totalSize}, errors={report.summary.totalErrors}, " +
                          $"warnings={report.summary.totalWarnings}\n";
            File.AppendAllText("Logs/build_report.txt", line);
            if (report.summary.result != BuildResult.Succeeded)
                Debug.LogError($"[Wayfinder] {platform} build FAILED: {report.summary.result}. See Logs/build_report.txt");
            else
                Debug.Log($"[Wayfinder] {platform} build succeeded → {report.summary.outputPath}");
        }
    }
}
