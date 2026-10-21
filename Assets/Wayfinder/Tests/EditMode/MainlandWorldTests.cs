using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.World;

namespace Wayfinder.Tests
{
    /// <summary>
    /// M3 continental world pipeline verification (KI-027, KI-029, KI-030, KI-037, KI-048):
    /// heightfield geomorphology, ocean &amp; boundary-cliff countable mask, dual-water hydrology,
    /// five-biome MainlandRouteGraph + infrastructure shortcuts, WorldScatterSurface + winch
    /// anchors, and full-mainland ExplorationMap rasterization.
    /// </summary>
    public class MainlandWorldTests
    {
        static string ContentDir
        {
            get
            {
                var d = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "Wayfinder", "Content")))
                    d = d.Parent;
                return Path.Combine(d.FullName, "Assets", "Wayfinder", "Content");
            }
        }

        [Test]
        public void ContinentalHeightfield_HasOpenOceanOriginBiomeReliefAndDryVerticalSlice_KI027()
        {
            var nf = new NoiseField();
            Assert.Less(nf.BaseHeight(0f, 0f), -10f, "world origin (0,0) sits in open coastal ocean");

            float hWetlands = nf.BaseHeight(WorldConstants.WetlandRelayAnchor.x, WorldConstants.WetlandRelayAnchor.z);
            float hCity = nf.BaseHeight(WorldConstants.CityRelayAnchor.x, WorldConstants.CityRelayAnchor.z);
            float hForest = nf.BaseHeight(WorldConstants.ForestRelayAnchor.x, WorldConstants.ForestRelayAnchor.z);
            float hCanyon = nf.BaseHeight(WorldConstants.CanyonRelayAnchor.x, WorldConstants.CanyonRelayAnchor.z);
            float hHighlands = nf.BaseHeight(WorldConstants.HighlandRelayAnchor.x, WorldConstants.HighlandRelayAnchor.z);

            Assert.Greater(hWetlands, 5f, "Wetlands anchor above sea level");
            Assert.Less(hWetlands, hCity, "Wetlands sits below City terrace");
            Assert.Less(hCity, hForest, "City terrace sits below Forest uplands");
            Assert.Less(hForest, hCanyon, "Forest sits below Canyon plateau");
            Assert.Less(hCanyon, hHighlands, "Canyon sits below Alpine Highlands massif");
            Assert.Less(hHighlands, WorldConstants.TerrainMaxY);

            // Entire 1,024 m vertical slice box stays dry and gently sloped.
            for (float x = 512f; x <= 1536f; x += 64f)
            for (float z = 1792f; z <= 2816f; z += 64f)
            {
                Assert.Greater(nf.BaseHeight(x, z), 20f);
                Assert.Less(nf.SlopeDegrees(x, z), 18f);
            }
        }

        [Test]
        public void MainlandRouteGraph_ValidatesBothWetlandWaterStatesAndInfrastructureShortcuts_KI029_KI030()
        {
            var routes = MainlandRouteGraph.FromFile(Path.Combine(ContentDir, "routes.json"));
            var report = routes.ValidateAll();
            Assert.IsTrue(report.Ok, string.Join("; ", report.Errors));

            // 1. Q07 Canyon Bridge infrastructure flag strictly shortens the Canyon crossing.
            float detourDist = routes.ShortestDistanceMeters(
                "camp.canyon.a", "garage.canyon", CapabilityBand.Technical,
                WetlandWaterState.LowWater, unlockedInfrastructure: new HashSet<string>());
            float bridgeDist = routes.ShortestDistanceMeters(
                "camp.canyon.a", "garage.canyon", CapabilityBand.Technical,
                WetlandWaterState.LowWater, unlockedInfrastructure: new HashSet<string> { "site.canyon.bridge" });
            Assert.Less(bridgeDist, detourDist * 0.80f,
                $"Q07 bridge shortcut ({bridgeDist:F0} m) strictly beats detour ({detourDist:F0} m)");

            // 2. Coastal Wetlands HighWater floods the direct low ford (0.78 m > 0.55 m stock intake)
            //    while keeping the raised firm causeway open; Raised Intake (0.85 m) unlocks the direct ford!
            float highStock = routes.ShortestDistanceMeters(
                "camp.city.a", "garage.wetlands", CapabilityBand.Technical,
                WetlandWaterState.HighWater, wadingCeilingMeters: 0.55f);
            float highSnorkel = routes.ShortestDistanceMeters(
                "camp.city.a", "garage.wetlands", CapabilityBand.Technical,
                WetlandWaterState.HighWater, wadingCeilingMeters: 0.85f);
            Assert.IsFalse(float.IsInfinity(highStock), "raised firm causeway keeps Wetlands reachable at HighWater");
            Assert.Less(highSnorkel, highStock * 0.75f,
                $"Raised Intake unlocks direct tidal crossing ({highSnorkel:F0} m vs {highStock:F0} m)");
        }

        [Test]
        public void WorldScatterSurfaceAndWinchAnchors_RespectRoadClearanceAndReservations_KI037()
        {
            var routes = MainlandRouteGraph.FromFile(Path.Combine(ContentDir, "routes.json"));
            var reserved = new[] { new ReservedSocket(1408f, 2816f, 30f) };
            var surface = new WorldScatterSurface(routes, reserved);

            var rules = ScatterRules.Trees("scatter.forest.pine", 101, spacingMeters: 8f, density: 0.6f);
            var placed = ScatterPlacer.PlaceChunk(5, 11, rules, surface);
            Assert.Greater(placed.Count, 10, "trees populate active forest chunks");
            foreach (var p in placed)
            {
                Assert.GreaterOrEqual(surface.DistanceToRoadEdgeMeters(p.X, p.Z), rules.RoadClearanceMeters - 1e-3f);
                Assert.IsFalse(surface.IsReserved(p.X, p.Z));
            }

            var anchorsA = surface.PlaceWinchAnchorsForChunk(5, 10);
            var anchorsB = surface.PlaceWinchAnchorsForChunk(5, 10);
            Assert.AreEqual(anchorsA.Count, anchorsB.Count);
            for (int i = 0; i < anchorsA.Count; i++)
            {
                Assert.AreEqual(anchorsA[i].X, anchorsB[i].X, 1e-4f);
                Assert.AreEqual(anchorsA[i].Z, anchorsB[i].Z, 1e-4f);
            }
        }

        [Test]
        public void FullMainlandExplorationMap_RendersContinentalCoastlineRoadsAndRelief_KI029()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var content = CampaignContent.FromDirectory(ContentDir);
                var routes = MainlandRouteGraph.FromFile(Path.Combine(ContentDir, "routes.json"));
                var terrain = new HeightFieldTerrainSource(content, routes: routes);
                Assert.AreEqual(MapSurface.Water, terrain.Sample(64f, 64f).Surface, "coastal ocean draws as water");
                Assert.AreEqual(MapSurface.Pad, terrain.Sample(4864f, 4608f).Surface, "Canyon garage draws as pad");

                var session = CampaignSession.StartNew(new SaveService(root), content, new Wayfinder.Core.SaveSlotId(0)).Session;
                session.MapTerrain = terrain;
                session.Depart();
                session.RevealAround(4864f, 4608f, vista: true);
                var fullMap = new ExplorationMap(session, terrain, fullMainland: true);
                Assert.AreEqual(96, fullMap.Raster.CellsX);
                Assert.Greater(fullMap.Raster.Pixels.Length, 0);
            }
            finally
            {
                CampaignTestKit.DeleteRoot(root);
            }
        }
    }
}
