using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Wayfinder.Editor
{
    /// <summary>Repeatable local setup. Does not overwrite owner app IDs, versions or signing.</summary>
    public static class ProjectSetup
    {
        public const string ControlsOutputPath = "Assets/Wayfinder/Resources/WayfinderControls.json";
        /// <summary>Runtime copies of the campaign catalog; CampaignHost loads them as TextAssets.</summary>
        public const string CampaignContentResourcesDir = "Assets/Wayfinder/Resources/Content";
        public const string CanonicalContentDir = "Assets/Wayfinder/Content";
        public const string PipelinePath = "Assets/Wayfinder/Settings/WayfinderURP.asset";
        public const string RendererPath = "Assets/Wayfinder/Settings/WayfinderRenderer.asset";

        [MenuItem("Tools/Wayfinder/Setup Project")]
        public static void Setup()
        {
            if (!Application.isBatchMode && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            PlayerSettings.productName = "Wayfinder: The Quiet Frontier";
            if (string.IsNullOrWhiteSpace(PlayerSettings.companyName) || PlayerSettings.companyName == "DefaultCompany")
                PlayerSettings.companyName = "Wayfinder";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            SetDefaultIdentifier(NamedBuildTarget.Android);
            SetDefaultIdentifier(NamedBuildTarget.iOS);

            // Preserve the authored input file as the single source; build a loadable TextAsset copy.
            Directory.CreateDirectory(Path.GetDirectoryName(ControlsOutputPath));
            File.Copy("Assets/Wayfinder/Content/Input/WayfinderControls.inputactions", ControlsOutputPath, true);
            AssetDatabase.ImportAsset(ControlsOutputPath, ImportAssetOptions.ForceSynchronousImport);
            CopyCampaignContent();
            ConfigureQualityTiers();
            ConfigureUrp();
            ConfigureInputBackend();
            Time.fixedDeltaTime = 0.02f;
            SceneBuilder.BuildAll();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/unity-pipeline");
            File.WriteAllText("Logs/unity-pipeline/setup-complete.json",
                "{\"editorVersion\":\"" + Application.unityVersion + "\",\"completedUtc\":\"" + DateTime.UtcNow.ToString("O") + "\"}");
            Debug.Log("[Wayfinder] Setup complete. Run Unity tests, then an Android smoke build. Preserve generated settings/scenes/.meta files. Greybox vertical slice: Title → New → VerticalSlice.");
        }

        /// <summary>
        /// Copies the canonical campaign JSON (single source: Assets/Wayfinder/Content) into
        /// Resources/Content so the player build can load it. Generated — git-ignored.
        /// </summary>
        [MenuItem("Tools/Wayfinder/Refresh Campaign Content")]
        public static void CopyCampaignContent()
        {
            Directory.CreateDirectory(CampaignContentResourcesDir);
            foreach (var name in Wayfinder.Campaign.CampaignContent.RequiredFiles)
            {
                string destination = CampaignContentResourcesDir + "/" + name;
                File.Copy(CanonicalContentDir + "/" + name, destination, true);
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            }
            Debug.Log($"[Wayfinder] Campaign content copied to {CampaignContentResourcesDir}.");
        }

        static void SetDefaultIdentifier(NamedBuildTarget target)
        {
            string current = PlayerSettings.GetApplicationIdentifier(target);
            if (string.IsNullOrWhiteSpace(current) || current.StartsWith("com.DefaultCompany", StringComparison.OrdinalIgnoreCase))
                PlayerSettings.SetApplicationIdentifier(target, "com.wayfinder.quietfrontier");
        }

        static void ConfigureInputBackend()
        {
            // No public PlayerSettings setter exists for Active Input Handling in this editor stream.
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var handler = settings.FindProperty("activeInputHandler");
            if (handler == null)
                throw new BuildFailedException("Cannot find Active Input Handling. Select Input System Package (New) in Player Settings and rerun setup.");
            handler.intValue = 1; // Input System Package (New), not the legacy input manager.
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureQualityTiers()
        {
            // QualitySettings.names is read-only. Edit Unity's serialized quality entries instead.
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (asset.Length == 0) throw new BuildFailedException("Unity QualitySettings asset not available.");
            var settings = new SerializedObject(asset[0]);
            var levels = settings.FindProperty("m_QualitySettings");
            if (levels == null) throw new BuildFailedException("Unsupported QualitySettings layout.");
            string[] names = { "BatteryLow", "Balanced", "High" };
            levels.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var entry = levels.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("name").stringValue = names[i];
                entry.FindPropertyRelative("vSyncCount").intValue = 0;
                var excluded = entry.FindPropertyRelative("excludedTargetPlatforms");
                if (excluded != null) excluded.arraySize = 0;
                entry.FindPropertyRelative("antiAliasing").intValue = i == 0 ? 0 : i == 1 ? 2 : 4;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.SetQualityLevel(1, true);
            Application.targetFrameRate = 60;
        }

        static void ConfigureUrp()
        {
            Directory.CreateDirectory("Assets/Wayfinder/Settings");
            AssetDatabase.Refresh();
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, RendererPath);
                }
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Wayfinder Mobile URP";
                pipeline.supportsHDR = false;
                pipeline.msaaSampleCount = 2;
                pipeline.shadowDistance = 60f;
                pipeline.renderScale = 1f;
                pipeline.useSRPBatcher = true;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            // Keep a deliberately supplied owner pipeline; fill only absent assignments.
            if (GraphicsSettings.defaultRenderPipeline == null)
                GraphicsSettings.defaultRenderPipeline = pipeline;
            int previous = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline == null) QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(previous, true);
        }
    }
}
