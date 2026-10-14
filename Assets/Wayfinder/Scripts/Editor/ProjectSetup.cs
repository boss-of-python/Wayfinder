using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Wayfinder.Platform;

namespace Wayfinder.Editor
{
    /// <summary>
    /// One-time project setup (M0). Menu: Tools → Wayfinder → Setup Project.
    /// Applies player settings, quality tiers and (if available) URP configuration, then builds
    /// the required scenes. Generated assets must be committed afterwards (BUILD_AND_RUN.md §1).
    /// </summary>
    public static class ProjectSetup
    {
        const string ProductName = "Wayfinder: The Quiet Frontier";

        [MenuItem("Tools/Wayfinder/Setup Project")]
        public static void Setup()
        {
            Debug.Log("[Wayfinder] Setup Project started.");

            PlayerSettings.companyName = "Wayfinder";
            PlayerSettings.productName = ProductName;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Android (§19.2): ARM64, IL2CPP. Verify current target API at release time — do not
            // hardcode stale submission requirements from the blueprint.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // iOS (§19.2): IL2CPP is the only option; signing is owner-provided in Xcode.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);

            // No analytics, no crash-reporting opt-out dance needed: none are installed (PROD-01).
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.wayfinder.quietfrontier");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "com.wayfinder.quietfrontier");

            ApplyQualityTiers();
            ConfigureUrpIfPresent();
            SceneBuilder.BuildAll();

            AssetDatabase.SaveAssets();
            Debug.Log("[Wayfinder] Setup Project complete. NEXT: commit generated ProjectSettings, " +
                      "Scenes, and .meta files (see Docs/BUILD_AND_RUN.md §1).");
        }

        static void ApplyQualityTiers()
        {
            // §18.5 tiers. Names must match Content/quality_tiers.json.
            string[] names = { "BatteryLow", "Balanced", "High" };
            QualitySettings.names = names;
            QualityTierService.Apply(QualityTier.Balanced);
            Debug.Log("[Wayfinder] Quality tiers configured (30/60/60 FPS targets).");
        }

        static void ConfigureUrpIfPresent()
        {
            var pipelineAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
            {
                Debug.LogWarning("[Wayfinder] No SRP asset assigned. Create one via " +
                                 "Create → Rendering → URP Pipeline Asset (Forward, SRP Batcher on, " +
                                 "mobile shadow settings per §18.4), assign it in Graphics settings, " +
                                 "then re-run this setup. Tracked as KI-004.");
                return;
            }
            // Forward path, SRP Batcher and shadow distance are set on the URP asset/quality
            // settings; detailed values live in Docs/DECISIONS.md D-012.
            Debug.Log("[Wayfinder] URP asset present: " + pipelineAsset.name);
        }
    }
}
