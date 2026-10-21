using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Wayfinder.Editor
{
    /// <summary>Local build entry points. No publication or stored signing secrets.</summary>
    public static class BuildCommands
    {
        public const string AndroidDevPath = "Builds/Android/Wayfinder-dev.apk";
        public const string AndroidReleasePath = "Builds/Android/Wayfinder.aab";

        static string[] GetScenes()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || scenes.Any(s => !File.Exists(s)))
                throw new BuildFailedException("Missing build scenes. Run Tools → Wayfinder → Setup Project first.");
            if (Path.GetFileNameWithoutExtension(scenes[0]) != "Bootstrap")
                throw new BuildFailedException("Bootstrap must be the first enabled build scene.");
            if (!File.Exists(ProjectSetup.ControlsOutputPath))
                throw new BuildFailedException("Generated input resource missing. Run Setup Project.");
            ProjectSetup.VerifyStreamingAssets();
            return scenes;
        }

        // The four entry points below are what CI invokes with -executeMethod. EditorFailureLog
        // records the cause in Logs/wayfinder-editor-failure.log before rethrowing, because the
        // editor log itself only exists on the build container's stdout (D-035).
        [MenuItem("Tools/Wayfinder/Build/Android Development")]
        public static void BuildAndroidDev() =>
            EditorFailureLog.Run("Wayfinder.Editor.BuildCommands.BuildAndroidDev", () => BuildAndroid(true));

        [MenuItem("Tools/Wayfinder/Build/Android Release AAB")]
        public static void BuildAndroidRelease() =>
            EditorFailureLog.Run("Wayfinder.Editor.BuildCommands.BuildAndroidRelease", () => BuildAndroid(false));

        static void BuildAndroid(bool development)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Install Android Build Support, SDK/NDK and OpenJDK in Unity Hub.");
            var scenes = GetScenes();
            Directory.CreateDirectory("Builds/Android");
            Directory.CreateDirectory("Logs");
            bool previousBundle = EditorUserBuildSettings.buildAppBundle;
            bool previousCustomKey = PlayerSettings.Android.useCustomKeystore;
            string previousStore = PlayerSettings.Android.keystoreName;
            string previousAlias = PlayerSettings.Android.keyaliasName;
            string previousStorePass = PlayerSettings.Android.keystorePass;
            string previousAliasPass = PlayerSettings.Android.keyaliasPass;
            try
            {
                EditorUserBuildSettings.buildAppBundle = !development;
                PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                if (development)
                    PlayerSettings.Android.useCustomKeystore = false;
                else
                    ConfigureReleaseSigning();

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = development ? AndroidDevPath : AndroidReleasePath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = development ? BuildOptions.Development : BuildOptions.None
                };
                LogReport("Android", BuildPipeline.BuildPlayer(options));
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = previousBundle;
                PlayerSettings.Android.useCustomKeystore = previousCustomKey;
                PlayerSettings.Android.keystoreName = previousStore;
                PlayerSettings.Android.keyaliasName = previousAlias;
                PlayerSettings.Android.keystorePass = previousStorePass;
                PlayerSettings.Android.keyaliasPass = previousAliasPass;
            }
        }

        static void ConfigureReleaseSigning()
        {
            string[] keys = { "WAYFINDER_KEYSTORE_PATH", "WAYFINDER_KEYSTORE_PASS", "WAYFINDER_KEY_ALIAS", "WAYFINDER_KEY_ALIAS_PASS" };
            var values = keys.Select(Environment.GetEnvironmentVariable).ToArray();
            int provided = values.Count(v => !string.IsNullOrEmpty(v));
            if (provided > 0 && provided != keys.Length)
                throw new BuildFailedException("Provide all four WAYFINDER signing environment variables, or none and use Publishing Settings.");
            if (provided == keys.Length)
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Path.GetFullPath(values[0]);
                PlayerSettings.Android.keystorePass = values[1];
                PlayerSettings.Android.keyaliasName = values[2];
                PlayerSettings.Android.keyaliasPass = values[3];
            }
            if (!PlayerSettings.Android.useCustomKeystore ||
                !File.Exists(PlayerSettings.Android.keystoreName) ||
                string.IsNullOrWhiteSpace(PlayerSettings.Android.keyaliasName) ||
                string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) ||
                string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
                throw new BuildFailedException("Release requires a private upload keystore and passwords. Configure Publishing Settings or signing environment variables. Never commit them.");
            string id = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith("com.DefaultCompany", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Set your permanent Android application identifier before a release build.");
            Debug.LogWarning("[Wayfinder] Verify target API, native 16 KB compatibility, permanent app ID and version code against current Play requirements. A successful AAB build is not release approval.");
        }

        [MenuItem("Tools/Wayfinder/Build/iOS Development")]
        public static void BuildIOSDev() =>
            EditorFailureLog.Run("Wayfinder.Editor.BuildCommands.BuildIOSDev", () => BuildIOS(true));

        [MenuItem("Tools/Wayfinder/Build/iOS Release")]
        public static void BuildIOSRelease() =>
            EditorFailureLog.Run("Wayfinder.Editor.BuildCommands.BuildIOSRelease", () => BuildIOS(false));

        static void BuildIOS(bool development)
        {
            Directory.CreateDirectory("Builds/iOS");
            Directory.CreateDirectory("Logs");
            LogReport("iOS", BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = GetScenes(), locationPathName = "Builds/iOS", target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = development ? BuildOptions.Development : BuildOptions.None
            }));
        }

        static void LogReport(string platform, BuildReport report)
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {platform}: {report.summary.result}, " +
                          $"size={report.summary.totalSize}, errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}\n";
            File.AppendAllText("Logs/build_report.txt", line);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{platform} build failed: {report.summary.result}. See Logs/build_report.txt.");
            Debug.Log($"[Wayfinder] {platform} build succeeded → {report.summary.outputPath}");
            // Satisfy game-ci/cli's UnityBuildValidation.validateBuild(stdout), which expects
            // StdOutReporter's Build results table and/or "Build succeeded!" on container stdout.
            string eol = Environment.NewLine;
            Console.WriteLine(
                $"{eol}###########################{eol}" +
                $"#      Build results      #{eol}" +
                $"###########################{eol}{eol}" +
                $"Duration: {report.summary.totalTime}{eol}" +
                $"Warnings: {report.summary.totalWarnings}{eol}" +
                $"Errors: {report.summary.totalErrors}{eol}" +
                $"Size: {report.summary.totalSize} bytes{eol}");
            Console.WriteLine("Build succeeded!");
        }
    }
}
