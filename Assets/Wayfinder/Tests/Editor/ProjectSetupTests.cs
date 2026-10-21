using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Wayfinder.Editor;
using Wayfinder.Platform;
using Wayfinder.Presentation;
using Wayfinder.Simulation;

namespace Wayfinder.Tests
{
    /// <summary>Run after Setup Project in Unity EditMode. Not included in headless C# tests.</summary>
    public class ProjectSetupTests
    {
        [Test]
        public void GeneratedInputMatchesAuthoritativeSource()
        {
            var resource = Resources.Load<TextAsset>("WayfinderControls");
            Assert.IsNotNull(resource, "Run Setup Project first");
            Assert.AreEqual(File.ReadAllText("Assets/Wayfinder/Content/Input/WayfinderControls.inputactions"), resource.text);
        }

        [Test]
        public void QualityAndPipelineAreConfigured()
        {
            CollectionAssert.AreEqual(new[] { "BatteryLow", "Balanced", "High" }, QualitySettings.names);
            Assert.IsNotNull(GraphicsSettings.defaultRenderPipeline);
            Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(ProjectSetup.PipelinePath));
        }

        [Test]
        public void AndroidReleaseAndDevelopmentHaveDifferentArtifactFormats()
        {
            Assert.IsTrue(BuildCommands.AndroidDevPath.EndsWith(".apk"));
            Assert.IsTrue(BuildCommands.AndroidReleasePath.EndsWith(".aab"));
        }

        [Test]
        public void GeneratedProvingGroundHasPersistentRigAndInputReferences()
        {
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene(SceneBuilder.ScenesDir + "/PhysicsTestGround.unity");
                var driver = Object.FindFirstObjectByType<ProvingGroundDriver>();
                Assert.IsNotNull(driver);
                Assert.IsNotNull(driver.vehicle);
                Assert.IsNotNull(driver.input);
                Assert.IsNotNull(driver.chaseCamera);
                Assert.AreEqual(driver.vehicle.transform, driver.chaseCamera.target);
                var vehicle = driver.vehicle;
                Assert.AreEqual(Vector3.one, vehicle.transform.localScale, "WheelCollider root must not be non-uniformly scaled");
                Assert.IsNotNull(vehicle.wheelFrontLeft);
                Assert.IsNotNull(vehicle.wheelFrontRight);
                Assert.IsNotNull(vehicle.wheelRearLeft);
                Assert.IsNotNull(vehicle.wheelRearRight);
                foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    var script = MonoScript.FromMonoBehaviour(behaviour);
                    Assert.IsNotNull(script, behaviour.name + " must have a serializable script reference");
                    Assert.AreEqual(behaviour.GetType(), script.GetClass());
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }

        [Test]
        public void CampaignContentIsCopiedToResourcesVerbatim()
        {
            foreach (var name in Wayfinder.Campaign.CampaignContent.RequiredFiles)
            {
                var asset = Resources.Load<TextAsset>(ProjectSetup.CampaignContentResourcesDir.Substring(
                    "Assets/Wayfinder/Resources/".Length) + "/" + Path.GetFileNameWithoutExtension(name));
                Assert.IsNotNull(asset, name + " missing from Resources — run Setup Project");
                Assert.AreEqual(File.ReadAllText(ProjectSetup.CanonicalContentDir + "/" + name), asset.text, name + " is stale");
            }
        }

        [Test]
        public void Required3DStreamingAssetsArePresentAndVerified()
        {
            Assert.DoesNotThrow(() => ProjectSetup.VerifyStreamingAssets());
        }

        [Test]
        public void GeneratedVerticalSliceHasDriverAtTheGarageSpawn()
        {
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene(SceneBuilder.ScenesDir + "/VerticalSlice.unity");
                var driver = Object.FindFirstObjectByType<VerticalSliceDriver>();
                Assert.IsNotNull(driver);
                Assert.IsNotNull(driver.vehicle);
                Assert.IsNotNull(driver.input);
                Assert.IsNotNull(driver.chaseCamera);
                Assert.AreEqual(driver.vehicle.transform, driver.chaseCamera.target);
                var slice = Wayfinder.Campaign.CampaignContent.FromDirectory(ProjectSetup.CanonicalContentDir).Slice;
                var p = driver.vehicle.transform.position;
                Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(slice.GarageSpawn.Position.X, slice.GarageSpawn.Position.Z)), 1f,
                    "scene is built in world coordinates from vertical_slice.json");
                Assert.AreEqual(slice.CacheIds.Count, CountNamed("Cache "), "one marker per slice cache");
                Assert.AreEqual(slice.Landmarks.Count, CountNamed("Landmark "), "one marker per slice landmark");
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }

        static int CountNamed(string prefix)
        {
            int n = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith(prefix)) n++;
            return n;
        }
    }
}
