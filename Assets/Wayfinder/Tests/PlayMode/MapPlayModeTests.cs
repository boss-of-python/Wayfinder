using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Wayfinder.Campaign;
using Wayfinder.Platform;
using Wayfinder.Presentation;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Unity side of the exploration map: the raster reaches the GPU texture byte-for-byte (row
    /// order included) and the minimap / map screen build and run without errors. Requires the
    /// Unity player loop and Setup-generated Resources/Content.
    /// Not headless evidence — never executed in the implementation environment (KI-031).
    /// </summary>
    public class MapPlayModeTests
    {
        string _saveRoot;

        [SetUp]
        public void SetUp() =>
            _saveRoot = Path.Combine(Application.temporaryCachePath, "wayfinder-map-playmode-" + System.Guid.NewGuid().ToString("N"));

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
        public IEnumerator MapTexture_MatchesTheRaster_AndUpdatesAfterAReveal()
        {
            var host = CampaignHost.CreateWithSaveRoot(_saveRoot);
            yield return null;
            Assume.That(host.Ready, "Setup Project has not generated Resources/Content");
            Assert.IsTrue(host.StartNew(0).Playable);
            var session = host.Session;
            var presenter = new MapPresenter(session);
            try
            {
                var texture = presenter.Binding.Texture;
                var raster = presenter.Map.Raster;
                Assert.AreEqual(raster.Width, texture.width);
                Assert.AreEqual(raster.Height, texture.height);
                Assert.AreEqual(TextureFormat.RGBA32, texture.format);
                Assert.AreEqual(1, texture.mipmapCount, "no mip chain");
                int px = (int)((1024f - raster.MinWorldX) / raster.MetersPerPixel);
                int py = (int)((2200f - raster.MinWorldZ) / raster.MetersPerPixel);
                var before = texture.GetPixels32()[py * raster.Width + px];

                Assert.IsTrue(session.Depart().Success);
                Assert.Greater(session.RevealAround(1024f, 2200f, false), 0);
                for (int i = 0; i < 4; i++) presenter.Tick(0.3f); // drain the budget and upload
                yield return null;

                var pixels = texture.GetPixels32(); // bottom row first — same order as MapRaster.Pixels
                var expected = raster.GetPixel(px, py);
                var actual = pixels[py * raster.Width + px];
                Assert.AreEqual(expected.R, actual.r);
                Assert.AreEqual(expected.G, actual.g);
                Assert.AreEqual(expected.B, actual.b);
                Assert.IsFalse(before.r == actual.r && before.g == actual.g && before.b == actual.b, "revealed pixel changed on the GPU copy");
            }
            finally
            {
                presenter.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator MinimapAndMapScreen_BuildAndRunFramesWithoutErrors()
        {
            var host = CampaignHost.CreateWithSaveRoot(_saveRoot);
            yield return null;
            Assume.That(host.Ready, "Setup Project has not generated Resources/Content");
            Assert.IsTrue(host.StartNew(0).Playable);
            var presenter = new MapPresenter(host.Session);
            var root = PrototypeUi.CreateRoot("Map Test UI");
            try
            {
                var minimap = MinimapView.Create(root, presenter, new Vector2(.80f, .44f), new Vector2(.98f, .77f));
                bool closed = false;
                var screen = MapOverlayView.Create(root, presenter, host.Session, () => closed = true, _ => { });
                for (int i = 0; i < 5; i++)
                {
                    presenter.Tick(Time.unscaledDeltaTime);
                    yield return null;
                }
                Assert.IsNotNull(minimap);
                screen.Close();
                yield return null;
                Assert.IsTrue(closed);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                presenter.Dispose();
                Object.Destroy(root.root.gameObject);
            }
        }
    }
}
