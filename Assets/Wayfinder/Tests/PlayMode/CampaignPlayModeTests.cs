using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Wayfinder.Campaign;
using Wayfinder.Platform;
using Wayfinder.Presentation;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Unity glue for the campaign (CampaignHost + VerticalSlice scene). Requires the Unity player
    /// loop, Setup-generated Resources/Content and scenes registered by SceneBuilder.
    /// Not headless evidence — never executed in the implementation environment.
    /// </summary>
    public class CampaignPlayModeTests
    {
        string _saveRoot;

        [SetUp]
        public void SetUp() =>
            _saveRoot = Path.Combine(Application.temporaryCachePath, "wayfinder-playmode-" + System.Guid.NewGuid().ToString("N"));

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            var host = CampaignHost.Instance;
            host.CloseSession();
            Object.Destroy(host.gameObject);
            yield return null;
            if (Directory.Exists(_saveRoot)) Directory.Delete(_saveRoot, true);
        }

        [UnityTest]
        public IEnumerator CampaignHost_LoadsResourcesContentAndSavesNewGame()
        {
            var host = CampaignHost.CreateWithSaveRoot(_saveRoot);
            yield return null;
            Assert.IsTrue(host.Ready, "Resources/Content missing — run Tools → Wayfinder → Setup Project: " + host.ContentError);
            var result = host.StartNew(0);
            Assert.AreEqual(CampaignOpenStatus.NewGameSaved, result.Status, result.Detail);
            Assert.IsNotNull(host.Session);
            Assert.IsTrue(host.Peek(0).HasSave);
        }

        [UnityTest]
        public IEnumerator VerticalSlice_LoadsAtTheGarageWithTheSession()
        {
            var host = CampaignHost.CreateWithSaveRoot(_saveRoot);
            yield return null;
            Assume.That(host.Ready, "Setup Project has not generated Resources/Content");
            Assert.IsTrue(host.StartNew(1).Playable);
            SceneManager.LoadScene(TitleFlowHost.SliceScene);
            yield return null;
            yield return null;
            var driver = Object.FindFirstObjectByType<VerticalSliceDriver>();
            Assert.IsNotNull(driver, "VerticalSlice scene must contain the slice driver (SceneBuilder)");
            Assert.IsTrue(driver.enabled, "driver disables itself when references or the session are missing");
            var spawn = host.Content.Slice.GarageSpawn.Position;
            var p = driver.vehicle.transform.position;
            Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(spawn.X, spawn.Z)), 3f, "Scout starts at the garage spawn");
            Assert.IsTrue(host.Session.InGarage);
        }
    }
}
