using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.World;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    /// <summary>
    /// The exploration map against a real <see cref="CampaignSession"/> (§8.1, §8.4, §7.6, §13.2,
    /// §13.4, §16.2): it starts blank, fills in live while driving and from vistas, redraws
    /// incrementally (byte-identical to a full redraw), never spoils unopened caches, persists
    /// markers, and keeps every revealed cell through rescue and reload.
    /// Set WAYFINDER_MAP_SNAPSHOT_DIR to dump RGBA snapshots of the scripted exploration
    /// (converted to PNG by Tools/Validation/rgba_to_png.py).
    /// </summary>
    public class ExplorationMapTests
    {
        string _root;

        [SetUp]
        public void SetUp() => _root = NewTempRoot();

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        // ------------------------------------------------------------------ helpers

        /// <summary>Drives the field controller along straight segments at a fixed speed (engine-free).</summary>
        sealed class Route
        {
            readonly SliceFieldController _field;
            public float X, Z, Yaw;
            public float Fuel = 24f;
            public Action AfterFrame;
            public readonly List<string> Discovered = new List<string>();
            public ExpeditionSummary Extraction;

            public Route(SliceFieldController field, Pose start)
            {
                _field = field;
                X = start.Position.X;
                Z = start.Position.Z;
                Yaw = start.YawDegrees;
            }

            public void DriveTo(float tx, float tz, float metersPerSecond = 10f, float dt = 0.25f)
            {
                float sx = X, sz = Z, dx = tx - sx, dz = tz - sz;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                if (distance < 0.01f) return;
                Yaw = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                int steps = Math.Max(1, (int)Math.Ceiling(distance / (metersPerSecond * dt)));
                for (int i = 1; i <= steps; i++)
                {
                    X = sx + dx * i / steps;
                    Z = sz + dz * i / steps;
                    Step(metersPerSecond * 3.6f, dt);
                }
            }

            public void Hold(float seconds, float dt = 0.25f)
            {
                int frames = (int)Math.Round(seconds / dt);
                for (int i = 0; i < frames; i++) Step(0f, dt);
            }

            void Step(float speedKph, float dt)
            {
                var frame = _field.Update(new FieldInput
                {
                    Pose = new Pose(X, 0.6f, Z, Yaw),
                    SpeedKph = speedKph,
                    DeltaSeconds = dt,
                    FuelLiters = Fuel,
                    EngineCondition = 100,
                    SuspensionCondition = 100,
                    TiresCondition = 100,
                    BodyCondition = 100,
                    Grounded = true,
                    ClearanceMeters = 0.3f
                });
                if (!string.IsNullOrEmpty(frame.DiscoveredLandmarkId)) Discovered.Add(frame.DiscoveredLandmarkId);
                if (frame.ArrivalTriggered) Extraction = frame.Extraction;
                AfterFrame?.Invoke();
            }
        }

        // The scripted slice exploration (garage → Ranger Rise vista → ford → camp → relay → home).
        static void FirstDrive(Route r)
        {
            r.DriveTo(1024f, 2160f);
            r.DriveTo(880f, 2160f);
        }

        static void RangerRiseVista(Route r)
        {
            r.DriveTo(872f, 2158f, 2f); // below 15 km/h within 30 m of the vista (§8.2)
            r.Hold(3f);
        }

        static void FordCampRelay(Route r)
        {
            r.DriveTo(985f, 2300f);
            r.DriveTo(985f, 2333f);
            r.DriveTo(985f, 2367f);
            r.DriveTo(1240f, 2520f);
            r.DriveTo(1400f, 2740f);
        }

        static void FirewatchVista(Route r)
        {
            r.DriveTo(1060f, 2440f);
            r.DriveTo(1050f, 2428f, 2f); // slow stop beside Firewatch Nine: surveys the 8 m hill 206 m away
            r.Hold(3f);
        }

        static void ReturnHome(Route r)
        {
            r.DriveTo(1024f, 2380f);
            r.DriveTo(1024f, 2292f);
            r.Hold(3f); // stop in the arrival volume for 2 s → extraction
        }

        CampaignSession Departed(out Route route, out ExplorationMap map)
        {
            var s = NewGame(new SaveService(_root));
            map = new ExplorationMap(s);
            Ok(s.Depart(), "depart");
            route = new Route(new SliceFieldController(s), s.ResumePose);
            return s;
        }

        /// <summary>Blank-sheet pixel: the parchment colour scaled by grain/grid only (hue preserved).</summary>
        static bool IsFog(MapColor c)
        {
            float r = c.R / (float)MapPalette.Fog.R, g = c.G / (float)MapPalette.Fog.G, b = c.B / (float)MapPalette.Fog.B;
            return r > 0.84f && r < 1.06f && Math.Abs(r - g) < 0.025f && Math.Abs(r - b) < 0.025f;
        }

        /// <summary>Contour-line pixel: the contour ink scaled by hillshade/grain.</summary>
        static bool IsContour(MapColor c)
        {
            float r = c.R / (float)MapPalette.Contour.R, g = c.G / (float)MapPalette.Contour.G, b = c.B / (float)MapPalette.Contour.B;
            return r > 0.5f && r < 1.4f && Math.Abs(r - g) < 0.06f && Math.Abs(r - b) < 0.08f;
        }

        static int CountPixels(MapRaster raster, Func<MapColor, bool> predicate)
        {
            int n = 0;
            for (int y = 0; y < raster.Height; y++)
            for (int x = 0; x < raster.Width; x++)
                if (predicate(raster.GetPixel(x, y))) n++;
            return n;
        }

        static bool SamePixels(MapRaster a, MapRaster b) => a.Pixels.SequenceEqual(b.Pixels);

        // ------------------------------------------------------------------ tests

        [Test]
        public void NewGame_MapStartsBlank_OnlyYouTheHomeGarageAndTheQ01SearchArea_S8_1()
        {
            var s = NewGame(new SaveService(_root));
            var map = new ExplorationMap(s);
            Assert.AreEqual(0, s.Discovery.RevealedCells.CountSet(), "no cell is known on a new save");
            Assert.AreEqual(512, map.Raster.Width, "16 slice cells × 32 px = 2 m per pixel");
            Assert.AreEqual(0, CountPixels(map.Raster, c => !IsFog(c)), "every pixel is blank survey paper");
            CollectionAssert.AreEqual(
                new[] { MapMarkerKind.Garage, MapMarkerKind.ObjectiveArea, MapMarkerKind.Player },
                map.Markers.Select(m => m.Kind).ToArray(),
                "only the home garage, the current mission's search circle and you");
            Assert.IsFalse(map.Refresh(), "nothing to draw until something is revealed");

            Ok(s.Depart(), "depart");
            Assert.IsFalse(map.Refresh(), "departing reveals nothing by itself");
            var model = MapScreenModel.Build(map);
            Assert.AreEqual("Forest test area · 0% mapped", model.HeaderLine);
            StringAssert.StartsWith("Mainland 0.0% mapped (finale needs 60%)", model.CoverageLine);
        }

        [Test]
        public void Driving_FillsTheMapInLive_RedrawingOnlyChangedCells_S8_1_S16_2()
        {
            var s = Departed(out var route, out var map);
            int version = map.Raster.Version, refreshes = 0, maxDrawn = 0;
            float percent = 0f;
            route.AfterFrame = () =>
            {
                if (!map.Refresh()) return;
                refreshes++;
                Assert.Greater(map.Raster.Version, version);
                version = map.Raster.Version;
                maxDrawn = Math.Max(maxDrawn, map.CellsDrawnLastRefresh);
                float now = map.SheetCoveragePercent;
                Assert.GreaterOrEqual(now, percent, "coverage never shrinks while exploring");
                percent = now;
            };
            FirstDrive(route);

            Assert.GreaterOrEqual(refreshes, 4, "the sheet updated repeatedly during one drive");
            Assert.LessOrEqual(maxDrawn, 30, "a drive reveal redraws a handful of cells, never the whole sheet");
            Assert.Greater(percent, 5f);
            Assert.IsFalse(IsFog(map.Raster.GetPixelAtWorld(1024f, 2200f)), "the track we drove is mapped");
            Assert.IsFalse(IsFog(map.Raster.GetPixelAtWorld(900f, 2160f)));
            Assert.IsTrue(IsFog(map.Raster.GetPixelAtWorld(1450f, 2750f)), "the far corner is still blank");
            Assert.IsTrue(IsFog(map.Raster.GetPixelAtWorld(1300f, 2000f)));
            Assert.IsTrue(s.HasUnsavedChanges, "revealed cells are field progress for the autosave");
        }

        [Test]
        public void IncrementalAndBudgetedRefresh_MatchAFullRedraw_ByteForByte_EvenWhenCellsRollBack()
        {
            var s = Departed(out var route, out var map);
            int budgetLimited = 0;
            route.AfterFrame = () =>
            {
                map.Refresh(maxCells: 3);
                if (map.PendingCells > 0) budgetLimited++;
            };
            FirstDrive(route);
            string earlyRevealed = s.Discovery.RevealedCells.ToBase64();
            string earlySurveyed = s.Discovery.SurveyedCells.ToBase64();
            RangerRiseVista(route);
            FordCampRelay(route);
            while (map.PendingCells > 0) map.Refresh(3);

            Assert.Greater(budgetLimited, 0, "the vista reveal was spread over several frames");
            Assert.IsTrue(SamePixels(new ExplorationMap(s).Raster, map.Raster), "incremental == full redraw");

            // Rollback/load replacing the bitsets with an earlier state: cells return to fog.
            s.Discovery.Restore(earlyRevealed, earlySurveyed, null, s.Discovery.DiscoveredLandmarkIds.ToList(),
                s.Discovery.CollectedCacheIds.ToList(), s.Discovery.RelicIds.ToList(),
                s.Discovery.MappingRewardsPaidThroughCount);
            Assert.IsTrue(map.Refresh());
            Assert.IsTrue(SamePixels(new ExplorationMap(s).Raster, map.Raster), "un-revealed cells redraw as fog");
            Assert.IsTrue(IsFog(map.Raster.GetPixelAtWorld(1400f, 2740f)));
        }

        [Test]
        public void VistaSurvey_RevealsWideAreaAtOnce_AndAddsContoursOnlyWhereThereIsRelief_S8_1()
        {
            var s = Departed(out var route, out var map);
            route.AfterFrame = () => map.Refresh();
            FirstDrive(route);
            int beforeVista = s.Discovery.RevealedCountableCells;
            RangerRiseVista(route);
            CollectionAssert.Contains(route.Discovered, "landmark.forest.01");
            Assert.Greater(s.Discovery.RevealedCountableCells - beforeVista, 25, "a 384 m survey, not a 96 m drive reveal");
            Assert.Greater(s.Discovery.SurveyedCells.CountSet(), 25);
            Assert.AreEqual(0, CountPixels(map.Raster, IsContour), "flat surveyed ground has no contour lines");

            s.RevealAround(1150f, 2600f, false); // drive past the 8 m hill: revealed, not surveyed
            map.Refresh();
            Assert.AreEqual(0, CountPixels(map.Raster, IsContour), "contours need survey detail");
            Ok(s.DiscoverLandmark("landmark.forest.06", 10f, 2.5f), "Firewatch Nine (vista)");
            map.Refresh();
            int contours = CountPixels(map.Raster, IsContour);
            Assert.Greater(contours, 20, "the surveyed hill shows contour lines");
            // Every contour pixel lies on the hill (1120–1180 × 2555–2645) or within a pixel of it.
            for (int y = 0; y < map.Raster.Height; y++)
            for (int x = 0; x < map.Raster.Width; x++)
            {
                if (!IsContour(map.Raster.GetPixel(x, y))) continue;
                float wx = map.Raster.MinWorldX + (x + 0.5f) * map.Raster.MetersPerPixel;
                float wz = map.Raster.MinWorldZ + (y + 0.5f) * map.Raster.MetersPerPixel;
                Assert.IsTrue(wx >= 1116f && wx <= 1184f && wz >= 2551f && wz <= 2649f, $"contour at ({wx},{wz})");
            }
        }

        [Test]
        public void SurveyOfGroundAlreadyDrivenAround_StillRedrawsContours_S8_1()
        {
            // Mutation-check finding: when a vista also revealed cells next to the hill, the neighbour
            // redraw masked a missing "surveyed changed" redraw. Here every cell around the hill is
            // already revealed, so only the survey flag changes for the hill cells.
            var s = NewGame(new SaveService(_root));
            Ok(s.Depart(), "depart");
            for (float x = 1000f; x <= 1300f; x += 40f)
            for (float z = 2420f; z <= 2780f; z += 40f)
                s.RevealAround(x, z, false);
            for (int cz = 38; cz <= 42; cz++)
            for (int cx = 16; cx <= 19; cx++)
                Assert.IsTrue(s.Discovery.RevealedCells.Get(cx, cz), $"setup: cell ({cx},{cz}) around the hill is revealed");
            var map = new ExplorationMap(s);
            Assert.AreEqual(0, CountPixels(map.Raster, IsContour));

            Ok(s.DiscoverLandmark("landmark.forest.06", 10f, 2.5f), "Firewatch Nine (vista) surveys the hill");
            // The hill's west half (cell 17,40) faces the vista; parts of its far side sit behind the crest.
            Assert.IsTrue(s.Discovery.SurveyedCells.Get(17, 40), "setup: the vista surveys the hill's west half");
            Assert.IsTrue(map.Refresh());
            Assert.Greater(CountPixels(map.Raster, IsContour), 20, "contours appear on ground that was already mapped");
            Assert.IsTrue(SamePixels(new ExplorationMap(s).Raster, map.Raster));
        }

        [Test]
        public void SpoilerRules_UnopenedCachesNeverShown_LandmarksUnnamedUntilDiscovered_S7_6()
        {
            var s = NewGame(new SaveService(_root));
            Ok(s.Depart(), "depart");
            var slice = Content.Slice;
            foreach (var id in slice.CacheIds)
                s.RevealAround(Content.Caches[id].Position.X, Content.Caches[id].Position.Z, false);
            foreach (var l in slice.Landmarks)
                s.RevealAround(Content.Landmarks[l.Id].Position.X, Content.Landmarks[l.Id].Position.Z, false);
            var map = new ExplorationMap(s);

            Assert.IsFalse(map.Markers.Any(m => m.Kind == MapMarkerKind.CacheOpened),
                "§7.6: no unopened cache is drawn, even inside revealed cells");
            Assert.IsFalse(map.Markers.Any(m => slice.CacheIds.Contains(m.Id)));
            var suspected = map.Markers.Where(m => m.Kind == MapMarkerKind.LandmarkSuspected).ToList();
            Assert.AreEqual(slice.Landmarks.Count, suspected.Count, "seen silhouettes appear as unconfirmed");
            Assert.IsTrue(suspected.All(m => m.Label == "Unconfirmed landmark"));
            Assert.IsFalse(map.Markers.Any(m => m.Label.Contains("Hollow Crown")), "names stay hidden until discovered");

            Ok(s.CollectCache("cache.forest.001"), "open one cache");
            Ok(s.DiscoverLandmark("landmark.forest.02", 8f, 2.5f), "discover Hollow Crown");
            map.RebuildMarkers();
            var opened = map.Markers.Where(m => m.Kind == MapMarkerKind.CacheOpened).ToList();
            Assert.AreEqual(1, opened.Count);
            Assert.AreEqual("cache.forest.001", opened[0].Id);
            Assert.AreEqual("Hollow Crown", map.Markers.Single(m => m.Id == "landmark.forest.02").Label);
            Assert.AreEqual(MapMarkerKind.LandmarkDiscovered, map.Markers.Single(m => m.Id == "landmark.forest.02").Kind);

            map.RebuildMarkers(MapLayers.SafeHubs);
            Assert.IsTrue(map.Markers.All(m => m.Layer == MapLayers.SafeHubs || m.Kind == MapMarkerKind.Player),
                "filters hide whole layers; the player arrow always stays");
        }

        [Test]
        public void ObjectiveMarkers_SearchAreaContainsTarget_SitesPinnedWhilePending_TrackedMatchesHud_S13_4()
        {
            foreach (var l in Content.Landmarks.Values)
            {
                string id = l.Id;
                var (cx, cz) = MapMarkerRules.SearchAreaCenter(id, l.Position.X, l.Position.Z);
                float off = (float)Math.Sqrt((cx - l.Position.X) * (cx - l.Position.X) + (cz - l.Position.Z) * (cz - l.Position.Z));
                Assert.GreaterOrEqual(off, 0.25f * 0.45f * MapMarkerRules.SearchAreaRadiusMeters - 0.01f, id + " never centred");
                Assert.LessOrEqual(off, 0.45f * MapMarkerRules.SearchAreaRadiusMeters + 0.01f, id + " always inside");
                Assert.AreEqual((cx, cz), MapMarkerRules.SearchAreaCenter(id, l.Position.X, l.Position.Z), "deterministic");
            }

            var s = NewGame(new SaveService(_root));
            var map = new ExplorationMap(s);
            var area = map.Markers.Single(m => m.Kind == MapMarkerKind.ObjectiveArea);
            Assert.AreEqual("landmark.forest.01", area.Id);
            Assert.AreEqual("Q01", area.MissionCode);
            Assert.IsFalse(area.Tracked, "the HUD tracks the salvage bundle first, which has no map pin");

            PlayQ01(s);
            Ok(s.AcceptMission("Q02"), "accept Q02");
            Ok(s.Depart(), "depart");
            map.RebuildMarkers();
            var inspect = map.Markers.Single(m => m.Id == "landmark.forest.04" && m.Kind == MapMarkerKind.Objective);
            Assert.IsTrue(inspect.Tracked);
            StringAssert.Contains(inspect.Label, ObjectiveText.TrackedLine(s), "map and HUD track the same objective");
            var ford = map.Markers.Single(m => m.MissionCode == "Q02" && m.Id.EndsWith("#0", StringComparison.Ordinal));
            Assert.IsFalse(ford.Tracked);
            StringAssert.EndsWith("(1/2)", ford.Label);

            Ok(s.InspectObject("landmark.forest.04"), "inspect");
            Ok(s.PassCheckpoint(CampaignTestKit.Ford, 0), "checkpoint 1");
            map.RebuildMarkers();
            var next = map.Markers.Single(m => m.MissionCode == "Q02");
            StringAssert.EndsWith("#1", next.Id);
            Assert.IsTrue(next.Tracked);
            var route = Content.Slice.CheckpointRoutes.Single(r => r.Id == CampaignTestKit.Ford);
            Assert.AreEqual(route.Checkpoints[1].X, next.X);
            Assert.AreEqual(route.Checkpoints[1].Z, next.Z);

            Ok(s.PassCheckpoint(CampaignTestKit.Ford, 1), "checkpoint 2");
            map.RebuildMarkers();
            Assert.IsFalse(map.Markers.Any(m => m.MissionCode == "Q02"), "finished objectives disappear");
            Assert.IsTrue(map.Markers.Single(m => m.Id == "garage.home").Tracked, "ready to turn in → the garage is tracked");
        }

        [Test]
        public void Markers_PersistThroughSaveAndLoad_AndRescueKeepsEveryRevealedCell_S8_1_D021()
        {
            var s = NewGame(new SaveService(_root));
            Ok(s.PlaceMapMarker(1100f, 2400f, "Ford scout", out var first), "markers can be placed from the garage");
            Assert.AreEqual(0, s.RevealAround(1024f, 2200f, false), "no expedition → no reveal");
            Ok(s.Depart(), "depart");
            var route = new Route(new SliceFieldController(s), s.ResumePose);
            FirstDrive(route);
            Ok(s.PlaceMapMarker(1300f, 2600f, null, out var second), "place while the map pauses the drive");
            Ok(s.SetRouteTarget(second), "route");
            Assert.IsFalse(s.RemoveMapMarker("marker.99").Success);
            Assert.IsTrue(s.HasUnsavedChanges, "marker edits are field progress (debounced autosave)");
            Assert.IsTrue(s.SaveNow().Success);

            string canonical = Canonical(s);
            StringAssert.Contains("\"customMarkers\"", canonical);
            var reloaded = Reload(_root);
            Assert.AreEqual(canonical, Canonical(reloaded), "byte-identical round trip including markers");
            Assert.AreEqual(new[] { first, second }, reloaded.MapMarkers.Markers.Select(m => m.Id).ToArray());
            Assert.AreEqual("Ford scout", reloaded.MapMarkers.Markers[0].Label);
            Assert.AreEqual(second, reloaded.MapMarkers.RouteTargetId);
            Assert.AreEqual(3, reloaded.MapMarkers.NextIndex);
            Assert.IsTrue(SamePixels(new ExplorationMap(s).Raster, new ExplorationMap(reloaded).Raster),
                "the reloaded sheet is pixel-identical");

            int cells = s.Discovery.RevealedCountableCells;
            var rescue = s.FullRescue();
            Assert.IsTrue(rescue.Success, "rescue: " + rescue.Result);
            Assert.AreEqual(cells, s.Discovery.RevealedCountableCells, "§8.1: known map cells never disappear on rescue");
            Assert.AreEqual(2, s.MapMarkers.Count, "markers survive rescue");
            Assert.IsFalse(IsFog(new ExplorationMap(s).Raster.GetPixelAtWorld(1024f, 2200f)));
        }

        [Test]
        public void ScreenModel_ReturnEstimate_Route_Selection_S13_4()
        {
            var s = NewGame(new SaveService(_root));
            var map = new ExplorationMap(s);
            StringAssert.StartsWith("At Home Garage", MapScreenModel.Build(map).ReturnLine);

            Ok(s.Depart(), "depart");
            var route = new Route(new SliceFieldController(s), s.ResumePose);
            route.DriveTo(1024f, 2304f);
            route.DriveTo(1324f, 2704f); // 500 m NE of the garage (1024, 2304)
            var model = MapScreenModel.Build(map);
            Assert.AreEqual(2.706f, MapScreenModel.NominalLitersPerKm, 0.001f, "0.02255 L/s at 30 km/h");
            StringAssert.StartsWith("Return: Home Garage 500 m SW · ≈1.8 L of 24.0 L", model.ReturnLine,
                "0.5 km × 1.3 detour × 2.706 L/km (no fuel measured yet)");
            Assert.IsFalse(model.ReturnFuelWarning);

            route.Fuel = 1f;
            route.Hold(0.25f);
            model = MapScreenModel.Build(map);
            Assert.IsTrue(model.ReturnFuelWarning);
            StringAssert.Contains("not enough fuel", model.ReturnLine);

            StringAssert.StartsWith("No route", model.RouteLine);
            Ok(s.PlaceMapMarker(1324f, 2504f, null, out var marker), "place");
            Ok(s.SetRouteTarget(marker), "route");
            map.RebuildMarkers();
            int index = map.Markers.FindIndex(m => m.Id == marker);
            model = MapScreenModel.Build(map, index);
            Assert.IsTrue(model.HasRoute);
            StringAssert.StartsWith("Route to Waypoint 1: 200 m S", model.RouteLine);
            StringAssert.Contains("estimate", model.RouteLine);
            Assert.AreEqual("Waypoint 1", model.SelectionTitle);
            StringAssert.Contains("route target · 200 m S", model.SelectionBody);
            Assert.AreEqual("Markers 1/10", model.MarkerCountLine);

            model = MapScreenModel.Build(map, map.Markers.FindIndex(m => m.Kind == MapMarkerKind.Player));
            Assert.AreEqual("You are here", model.SelectionTitle);
            StringAssert.StartsWith("Heading NE", model.SelectionBody);
            model = MapScreenModel.Build(map, map.Markers.FindIndex(m => m.Id == "garage.home"));
            StringAssert.StartsWith("Safe hub", model.SelectionBody);
            StringAssert.EndsWith("500 m SW", model.SelectionBody);
        }

        sealed class EyeCountingTerrain : IMapTerrainSource
        {
            readonly IMapTerrainSource _inner;
            public readonly HashSet<(float, float)> FramePositions = new HashSet<(float, float)>();
            public readonly HashSet<(float, float)> RevealEyes = new HashSet<(float, float)>();

            public EyeCountingTerrain(IMapTerrainSource inner) => _inner = inner;

            public float HeightAt(float x, float z)
            {
                if (FramePositions.Contains((x, z))) RevealEyes.Add((x, z)); // each reveal samples its eye height
                return _inner.HeightAt(x, z);
            }

            public MapTerrainSample Sample(float x, float z) => _inner.Sample(x, z);
        }

        [Test]
        public void RevealCadence_IsTwoHertzWhileMoving_AndIdleWhenParked_S16_2()
        {
            Assert.AreEqual(0.5f, SliceFieldController.RevealIntervalSeconds);
            foreach (bool moving in new[] { false, true })
            {
                var s = NewGame(new SaveService(Path.Combine(_root, moving ? "moving" : "parked")));
                var terrain = new EyeCountingTerrain(new SliceMapTerrain(Content.Slice));
                s.MapTerrain = terrain;
                Ok(s.Depart(), "depart");
                var field = new SliceFieldController(s);
                const float dt = 0.125f; // exact in binary: 4 frames = 0.5 s
                for (int i = 0; i < 16; i++) // 2 s
                {
                    float x = 1000.5f, z = 2100.25f + (moving ? 5f * i : 0f); // 40 m/s; never a cell centre
                    terrain.FramePositions.Add((x, z));
                    field.Update(new FieldInput
                    {
                        Pose = new Pose(x, 0.6f, z, 0f),
                        SpeedKph = moving ? 144f : 0f,
                        DeltaSeconds = dt,
                        FuelLiters = 24f,
                        EngineCondition = 100,
                        SuspensionCondition = 100,
                        TiresCondition = 100,
                        BodyCondition = 100,
                        Grounded = true
                    });
                }
                Assert.AreEqual(moving ? 4 : 1, terrain.RevealEyes.Count,
                    moving ? "reveals at 0, 0.5, 1.0 and 1.5 s (2 Hz)" : "parked: one reveal, then nothing new to sample");
            }
        }

        [Test]
        public void SessionReveal_UsesTheTerrainSource_RidgesHideCellsForDriveAndVista_S8_1()
        {
            var s = NewGame(new SaveService(_root));
            s.MapTerrain = new FuncTerrain(MapDomainTests.Ridge);
            Ok(s.Depart(), "depart");
            s.RevealAround(1060f, 2300f, false);
            var (behindX, behindZ) = WorldConstants.MapCellOf(1120f, 2300f); // centre (1120, 2272), 66 m away beyond the ridge
            var (frontX, frontZ) = WorldConstants.MapCellOf(992f, 2300f);
            Assert.IsFalse(s.Discovery.RevealedCells.Get(behindX, behindZ), "drive: behind the ridge stays fogged");
            Assert.IsTrue(s.Discovery.RevealedCells.Get(frontX, frontZ));
            s.RevealAround(1060f, 2300f, true);
            Assert.IsFalse(s.Discovery.RevealedCells.Get(behindX, behindZ), "vista: still hidden by a 40 m ridge");
            Assert.IsFalse(s.Discovery.RevealedCells.Get(WorldConstants.MapCellOf(1300f, 2300f).cellX, behindZ));
            Assert.IsTrue(s.Discovery.RevealedCells.Get(WorldConstants.MapCellOf(800f, 2300f).cellX, behindZ), "open side: 260 m");
        }

        sealed class FuncTerrain : IMapTerrainSource
        {
            readonly Func<float, float, float> _height;
            public FuncTerrain(Func<float, float, float> height) => _height = height;
            public float HeightAt(float x, float z) => _height(x, z);
            public MapTerrainSample Sample(float x, float z) => new MapTerrainSample(_height(x, z), MapSurface.Ground);
        }

        [Test]
        public void ScriptedExploration_FillsTheSheetStageByStage_AndSurvivesReload_Evidence()
        {
            var s = NewGame(new SaveService(_root));
            var map = new ExplorationMap(s);
            var fogShare = new List<float>();
            void Stage(string name)
            {
                map.Refresh();
                map.RebuildMarkers();
                int total = map.Raster.Width * map.Raster.Height;
                fogShare.Add(CountPixels(map.Raster, IsFog) / (float)total);
                MapSnapshot.Write(name, map);
            }

            Stage("00-new-game");
            Ok(s.Depart(), "depart");
            var route = new Route(new SliceFieldController(s), s.ResumePose) { AfterFrame = () => map.Refresh() };
            FirstDrive(route);
            Stage("01-first-drive");
            RangerRiseVista(route);
            Assert.IsTrue(s.Discovery.IsLandmarkDiscovered("landmark.forest.01"));
            Stage("02-ranger-rise-vista");
            FordCampRelay(route);
            Stage("03-ford-camp-relay");
            Assert.AreEqual(0, CountPixels(map.Raster, IsContour), "no survey near relief yet");
            FirewatchVista(route);
            Assert.IsTrue(s.Discovery.IsLandmarkDiscovered("landmark.forest.06"));
            Stage("04-firewatch-vista");
            Assert.Greater(CountPixels(map.Raster, IsContour), 20, "the surveyed hill now shows contour lines");
            ReturnHome(route);
            Assert.IsNotNull(route.Extraction, "stopped in the garage volume → extraction");
            Assert.IsTrue(route.Extraction.Success);
            Stage("05-home");

            Assert.AreEqual(1f, fogShare[0], "stage 0 is completely blank");
            for (int i = 1; i < 5; i++) Assert.Less(fogShare[i], fogShare[i - 1], $"stage {i} reveals more of the sheet");
            Assert.LessOrEqual(fogShare[5], fogShare[4]);
            var reloaded = Reload(_root);
            Assert.IsTrue(SamePixels(map.Raster, new ExplorationMap(reloaded).Raster), "the sheet survives save/load byte-for-byte");
            TestContext.WriteLine("fog share per stage: " + string.Join(", ",
                fogShare.Select(f => f.ToString("0.000", CultureInfo.InvariantCulture))));
        }
    }

    /// <summary>
    /// Evidence dump for the map (test-only): when WAYFINDER_MAP_SNAPSHOT_DIR is set, writes the
    /// raw terrain raster and a copy with simple marker glyphs (a stand-in for the Unity UI icons,
    /// NOT the shipped presentation) as RGBA files + JSON headers for rgba_to_png.py.
    /// </summary>
    static class MapSnapshot
    {
        public static void Write(string name, ExplorationMap map)
        {
            string dir = Environment.GetEnvironmentVariable("WAYFINDER_MAP_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var raster = map.Raster;
            File.WriteAllBytes(Path.Combine(dir, name + ".terrain.rgba"), raster.Pixels);
            var composite = (byte[])raster.Pixels.Clone();
            foreach (var m in map.Markers) Stamp(composite, raster, m);
            File.WriteAllBytes(Path.Combine(dir, name + ".rgba"), composite);
            var model = MapScreenModel.Build(map);
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"width\":{0},\"height\":{1},\"stage\":\"{2}\",\"sheetPercent\":{3:0.0},\"mainlandPercent\":{4:0.00}," +
                "\"revealedCells\":{5},\"surveyedCells\":{6},\"markers\":{7},\"header\":\"{8}\",\"rowOrder\":\"bottom-up\"}}",
                raster.Width, raster.Height, name, model.SheetPercent, model.MainlandPercent,
                map.Session.Discovery.RevealedCountableCells, map.Session.Discovery.SurveyedCells.CountSet(), map.Markers.Count,
                model.HeaderLine);
            File.WriteAllText(Path.Combine(dir, name + ".json"), json);
        }

        static void Stamp(byte[] px, MapRaster r, MapMarker m)
        {
            float cx = (m.X - r.MinWorldX) / r.MetersPerPixel, cy = (m.Z - r.MinWorldZ) / r.MetersPerPixel;
            switch (m.Kind)
            {
                case MapMarkerKind.ObjectiveArea:
                    Ring(px, r, cx, cy, m.RadiusMeters / r.MetersPerPixel, 2f, new MapColor(200, 60, 40));
                    break;
                case MapMarkerKind.Player:
                    Disc(px, r, cx, cy, 6f, new MapColor(20, 20, 20));
                    double yaw = m.YawDegrees * Math.PI / 180.0;
                    for (int i = 0; i < 14; i++)
                        Disc(px, r, cx + (float)Math.Sin(yaw) * i, cy + (float)Math.Cos(yaw) * i, 2f, new MapColor(250, 250, 250));
                    break;
                case MapMarkerKind.Garage:
                case MapMarkerKind.GarageLocked:
                    Square(px, r, cx, cy, 7f, new MapColor(30, 60, 140));
                    break;
                case MapMarkerKind.CampActive:
                    Square(px, r, cx, cy, 6f, new MapColor(220, 120, 30));
                    break;
                case MapMarkerKind.CampSite:
                    Ring(px, r, cx, cy, 6f, 2f, new MapColor(220, 120, 30));
                    break;
                case MapMarkerKind.LandmarkDiscovered:
                    Disc(px, r, cx, cy, 6f, new MapColor(230, 190, 40));
                    break;
                case MapMarkerKind.LandmarkSuspected:
                    Ring(px, r, cx, cy, 6f, 2f, new MapColor(230, 190, 40));
                    break;
                case MapMarkerKind.CacheOpened:
                    Disc(px, r, cx, cy, 3f, new MapColor(40, 160, 170));
                    break;
                case MapMarkerKind.Relay:
                case MapMarkerKind.RelayOffline:
                    Square(px, r, cx, cy, 4f, new MapColor(120, 120, 130));
                    break;
                case MapMarkerKind.Objective:
                    Disc(px, r, cx, cy, 5f, new MapColor(200, 60, 40));
                    break;
                case MapMarkerKind.Waypoint:
                    Disc(px, r, cx, cy, 5f, new MapColor(180, 60, 170));
                    break;
            }
        }

        static void Put(byte[] px, MapRaster r, int x, int y, MapColor c)
        {
            if (x < 0 || y < 0 || x >= r.Width || y >= r.Height) return;
            int i = (y * r.Width + x) * 4;
            px[i] = c.R;
            px[i + 1] = c.G;
            px[i + 2] = c.B;
            px[i + 3] = 255;
        }

        static void Disc(byte[] px, MapRaster r, float cx, float cy, float radius, MapColor c)
        {
            for (int y = (int)(cy - radius); y <= (int)(cy + radius); y++)
            for (int x = (int)(cx - radius); x <= (int)(cx + radius); x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius) Put(px, r, x, y, c);
        }

        static void Ring(byte[] px, MapRaster r, float cx, float cy, float radius, float width, MapColor c)
        {
            for (int y = (int)(cy - radius - width); y <= (int)(cy + radius + width); y++)
            for (int x = (int)(cx - radius - width); x <= (int)(cx + radius + width); x++)
            {
                float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (Math.Abs(d - radius) <= width * 0.5f) Put(px, r, x, y, c);
            }
        }

        static void Square(byte[] px, MapRaster r, float cx, float cy, float half, MapColor c)
        {
            for (int y = (int)(cy - half); y <= (int)(cy + half); y++)
            for (int x = (int)(cx - half); x <= (int)(cx + half); x++)
                Put(px, r, x, y, c);
        }
    }
}
