using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Progression;
using Wayfinder.World;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Exploration-map building blocks (§8.1, §13.4): bitset diffing, terrain line-of-sight,
    /// occlusion-aware reveal, custom markers, slice terrain profile, viewport math and gestures.
    /// </summary>
    public class MapDomainTests
    {
        /// <summary>A 40 m wall along x ∈ [1100, 1110] for every z: a "major ridge".</summary>
        public static float Ridge(float x, float z) => x >= 1100f && x <= 1110f ? 40f : 0f;

        [Test]
        public void BitGrid_CollectDifferencesReportsSetAndClearedCellsOnly_CopyFromSyncs()
        {
            var a = new BitGrid(10, 10); // 100 cells: the second word has 28 padding bits that must never be reported
            var b = new BitGrid(10, 10);
            a.Set(0, 0, true);
            a.Set(9, 9, true);
            a.Set(3, 6, true);
            b.Set(3, 6, true);
            b.Set(5, 1, true);
            var diff = new List<int>();
            Assert.AreEqual(3, a.CollectDifferences(b, diff));
            CollectionAssert.AreEquivalent(new[] { 0, 99, 1 * 10 + 5 }, diff);

            b.CopyFrom(a);
            diff.Clear();
            Assert.AreEqual(0, a.CollectDifferences(b, diff));
            Assert.IsTrue(b.Get(9, 9) && !b.Get(5, 1));
            Assert.Throws<ArgumentException>(() => a.CopyFrom(new BitGrid(9, 10)));
        }

        [Test]
        public void LineOfSight_RidgeHidesCellsBehindIt_SmallBumpsAndUnknownTerrainDoNot_S8_1()
        {
            Assert.IsTrue(TerrainLineOfSight.IsVisible((x, z) => 0f, 1000f, 1000f, 1090f, 1000f), "flat ground");
            Assert.IsFalse(TerrainLineOfSight.IsVisible(Ridge, 1060f, 1000f, 1150f, 1000f), "behind the 40 m ridge");
            Assert.IsTrue(TerrainLineOfSight.IsVisible(Ridge, 1060f, 1000f, 1060f, 1090f), "along the ridge, not through it");
            Assert.IsTrue(TerrainLineOfSight.IsVisible(Ridge, 1105f, 1000f, 1190f, 1000f, 2f), "standing on the crest sees down");
            Func<float, float, float> bump = (x, z) => x >= 1040f && x <= 1050f ? 3.5f : 0f;
            Assert.IsTrue(TerrainLineOfSight.IsVisible(bump, 1000f, 1000f, 1090f, 1000f),
                "rises below the 4 m major-ridge tolerance never block");
            Assert.IsTrue(TerrainLineOfSight.IsVisible((x, z) => float.NaN, 1000f, 1000f, 1090f, 1000f), "unknown heights never occlude");
            Assert.IsTrue(TerrainLineOfSight.IsVisible(null, 1060f, 1000f, 1150f, 1000f), "no sampler = no occlusion");
            Assert.IsNull(TerrainLineOfSight.CellFilter(null, 0f, 0f, 64f));
        }

        [Test]
        public void Discovery_RevealVisibleAround_KeepsCellsBehindRidgesFogged_SurveyFollowsReveal_S8_1()
        {
            var open = new DiscoveryService();
            int openCount = open.RevealVisibleAround(1060f, 2300f, false, (x, z) => 0f);
            var blocked = new DiscoveryService();
            int blockedCount = blocked.RevealVisibleAround(1060f, 2300f, false, Ridge);
            Assert.Greater(openCount, blockedCount, "the ridge hides part of the 96 m circle");
            var (behindX, behindZ) = WorldConstants.MapCellOf(1120f, 2300f); // cell centre (1120, 2272): 66 m away, beyond x = 1110
            Assert.IsTrue(open.RevealedCells.Get(behindX, behindZ), "flat control: the same cell is visible without the ridge");
            Assert.IsFalse(blocked.RevealedCells.Get(behindX, behindZ), "behind a major ridge stays fogged");
            var (hereX, hereZ) = WorldConstants.MapCellOf(1060f, 2300f);
            Assert.IsTrue(blocked.RevealedCells.Get(hereX, hereZ), "the cell under the vehicle always reveals");

            var vista = new DiscoveryService();
            vista.RevealVisibleAround(1060f, 2300f, true, Ridge);
            int surveyedNotRevealed = 0;
            for (int z = 0; z < WorldConstants.MapCellsPerSide; z++)
            for (int x = 0; x < WorldConstants.MapCellsPerSide; x++)
                if (vista.SurveyedCells.Get(x, z) && !vista.RevealedCells.Get(x, z)) surveyedNotRevealed++;
            Assert.AreEqual(0, surveyedNotRevealed, "survey detail respects the same occlusion (§8.1)");
            Assert.IsFalse(vista.SurveyedCells.Get(WorldConstants.MapCellOf(1300f, 2300f).cellX, hereZ),
                "the vista cannot survey through the ridge either");
            Assert.IsTrue(vista.SurveyedCells.Get(WorldConstants.MapCellOf(800f, 2300f).cellX, hereZ));
        }

        [Test]
        public void MapMarkers_TenMarkerLimit_StableNeverReusedIds_RouteTarget_S13_4()
        {
            var markers = new MapMarkerService();
            for (int i = 0; i < MapMarkerService.MaxMarkers; i++)
                Assert.IsTrue(markers.TryAdd(1000f + i, 2000f, i == 0 ? "  Ford \u0007 crossing  " : null, out _).Success);
            Assert.AreEqual("Ford  crossing", markers.Markers[0].Label, "trimmed, control characters dropped");
            Assert.AreEqual("Waypoint 2", markers.Markers[1].Label, "default label");
            var full = markers.TryAdd(1500f, 2000f, "extra", out _);
            Assert.AreEqual(FailureReason.OverCapacity, full.Reason, "up to ten custom markers (§13.4)");
            Assert.AreEqual(FailureReason.InvalidTarget, new MapMarkerService().TryAdd(-5f, 10f, "sea", out _).Reason);
            Assert.AreEqual(FailureReason.InvalidTarget, new MapMarkerService().TryAdd(float.NaN, 10f, "nan", out _).Reason);

            Assert.IsTrue(markers.SetRouteTarget("marker.3").Success);
            Assert.IsTrue(markers.Remove("marker.3").Success);
            Assert.AreEqual(string.Empty, markers.RouteTargetId, "removing the route target clears the route");
            Assert.IsTrue(markers.TryAdd(1200f, 2100f, new string('x', 40), out var again).Success);
            Assert.AreEqual("marker.11", again.Id, "ids are never reused");
            Assert.AreEqual(MapMarkerService.MaxLabelLength, again.Label.Length);
            Assert.AreEqual(FailureReason.ItemNotFound, markers.Remove("marker.3").Reason);
            Assert.AreEqual(FailureReason.ItemNotFound, markers.SetRouteTarget("marker.99").Reason);
            Assert.IsTrue(markers.SetRouteTarget(string.Empty).Success);
        }

        [Test]
        public void MapMarkers_RestoreIsTolerant_DropsBadEntries_LiftsNextIndex()
        {
            var restored = new MapMarkerService();
            var input = new List<CustomMapMarker>
            {
                new CustomMapMarker("marker.4", 1000f, 2000f, "camp"),
                new CustomMapMarker("marker.4", 1001f, 2000f, "duplicate"),
                new CustomMapMarker("bogus", 1002f, 2000f, "malformed id"),
                new CustomMapMarker("marker.0", 1003f, 2000f, "zero"),
                new CustomMapMarker("marker.9", 99999f, 2000f, "outside the mainland"),
                new CustomMapMarker("marker.7", 1004f, 2000f, "")
            };
            for (int i = 20; i < 32; i++) input.Add(new CustomMapMarker("marker." + i, 1100f + i, 2100f, "bulk"));
            restored.Restore(input, 2, "marker.7");
            Assert.AreEqual(MapMarkerService.MaxMarkers, restored.Count, "capped at ten");
            Assert.AreEqual("marker.4", restored.Markers[0].Id);
            Assert.AreEqual("marker.7", restored.Markers[1].Id);
            Assert.AreEqual("Waypoint 7", restored.Markers[1].Label);
            Assert.AreEqual("marker.7", restored.RouteTargetId);
            Assert.AreEqual(28, restored.NextIndex, "next index above every surviving id (marker.27 is the last kept)");

            var noTarget = new MapMarkerService();
            noTarget.Restore(input.Take(1), 1, "marker.404");
            Assert.AreEqual(string.Empty, noTarget.RouteTargetId, "unknown route target dropped");
            Assert.AreEqual(5, noTarget.NextIndex);
        }

        [Test]
        public void SliceTerrain_MatchesTheGreyboxSceneLayout_KI027()
        {
            var slice = CampaignTestKit.Content.Slice;
            var terrain = new SliceMapTerrain(slice);
            Assert.AreEqual(8f, terrain.HeightAt(1150f, 2600f), 1e-4f, "hill top (8 m)");
            Assert.AreEqual(4f, terrain.HeightAt(1150f, 2555f + 31.5f * 0.5f), 1e-3f, "half-way up the 31.5 m ramp");
            Assert.AreEqual(0f, terrain.HeightAt(1185f, 2600f), "vertical side: outside the 60 m width");
            Assert.IsTrue(float.IsNaN(terrain.HeightAt(400f, 2300f)), "no ground outside the slice area");
            Assert.AreEqual(MapSurface.Unbuilt, terrain.Sample(400f, 2300f).Surface);

            var ford = terrain.Sample(1000f, 2350f);
            Assert.AreEqual(MapSurface.Water, ford.Surface);
            Assert.AreEqual(0.35f, ford.WaterDepthMeters, 1e-4f);
            Assert.AreEqual(MapSurface.Mud, terrain.Sample(900f, 2200f).Surface);
            Assert.AreEqual(MapSurface.Pad, terrain.Sample(1024f, 2290f).Surface, "garage arrival pad");
            Assert.AreEqual(MapSurface.Ground, terrain.Sample(1300f, 2000f).Surface);
            Assert.AreEqual(0.35f, SliceMapTerrain.HillRampFraction, "must match SceneBuilder.BuildVerticalSlice");
        }

        [Test]
        public void Viewport_RoundTrip_ZoomKeepsFocusPoint_PanFollowsFinger_ClampsAndUv()
        {
            var raster = new MapRaster(8, 28, 16, 16, 32, 64f); // the slice sheet: 1024 m at 2 m/px
            var v = MapViewport.ForRaster(raster, 512f, 512f);
            Assert.AreEqual(2f, v.MetersPerPixel, 1e-5f, "opens showing the whole sheet");
            var uv = v.UvFor(raster);
            Assert.AreEqual(0f, uv.X, 1e-5f);
            Assert.AreEqual(0f, uv.Y, 1e-5f);
            Assert.AreEqual(1f, uv.Width, 1e-5f);
            Assert.AreEqual(1f, uv.Height, 1e-5f);

            var (sx, sy) = v.WorldToScreen(1100f, 2400f);
            var (wx, wz) = v.ScreenToWorld(sx, sy);
            Assert.AreEqual(1100f, wx, 1e-3f);
            Assert.AreEqual(2400f, wz, 1e-3f);

            var (fx, fz) = v.ScreenToWorld(100f, 400f);
            v.ZoomAt(4f, 100f, 400f);
            Assert.AreEqual(0.5f, v.MetersPerPixel, 1e-5f);
            var (gx, gz) = v.ScreenToWorld(100f, 400f);
            Assert.AreEqual(fx, gx, 1e-2f, "the point under the fingers stays put (§13.4 pinch zoom)");
            Assert.AreEqual(fz, gz, 1e-2f);
            v.ZoomAt(100f, 0f, 0f);
            Assert.AreEqual(v.MinMetersPerPixel, v.MetersPerPixel, 1e-6f, "zoom-in clamps");
            v.ZoomAt(0.0001f, 0f, 0f);
            Assert.AreEqual(v.MaxMetersPerPixel, v.MetersPerPixel, 1e-6f, "zoom-out clamps");

            v.CenterOn(1000f, 2300f);
            v.Pan(50f, 0f);
            Assert.AreEqual(1000f - 50f * v.MetersPerPixel, v.CenterX, 1e-3f, "dragging right moves the map right");
            v.Pan(1e6f, -1e6f);
            Assert.AreEqual(raster.MinWorldX, v.CenterX, 1e-3f, "the centre never leaves the sheet");
            Assert.AreEqual(raster.MinWorldZ + raster.HeightMeters, v.CenterZ, 1e-3f);
        }

        [Test]
        public void Viewport_HitTest_PrefersPointMarkersThenPlayerThenSearchAreas()
        {
            var v = new MapViewport(400f, 400f, 1f, 0f, 0f, 6144f, 6144f);
            v.CenterOn(1000f, 1000f);
            var markers = new List<MapMarker>
            {
                new MapMarker(MapMarkerKind.ObjectiveArea, "area", "search", 1000f, 1000f, 110f),
                new MapMarker(MapMarkerKind.Garage, "garage.home", "Home", 1030f, 1000f),
                new MapMarker(MapMarkerKind.Player, "player", "You", 1002f, 1000f)
            };
            var (gx, gy) = v.WorldToScreen(1028f, 1001f);
            Assert.AreEqual(1, v.HitTest(markers, gx, gy, 20f), "nearest point marker");
            var (px, py) = v.WorldToScreen(1003f, 1000f);
            Assert.AreEqual(1, v.HitTest(markers, px, py, 40f), "point markers beat the player arrow");
            Assert.AreEqual(2, v.HitTest(markers, px, py, 10f), "the player arrow beats a search area it sits in");
            var (ax, ay) = v.WorldToScreen(1000f, 1080f);
            Assert.AreEqual(0, v.HitTest(markers, ax, ay, 10f), "inside a search area");
            var (nx, ny) = v.WorldToScreen(1500f, 1500f);
            Assert.AreEqual(-1, v.HitTest(markers, nx, ny, 10f));
        }

        [Test]
        public void Gestures_TapLongPressPanAndPinch_S13_4()
        {
            var g = new MapGestureRecognizer();
            g.PointerDown(1, 100f, 100f, 0f);
            Assert.AreEqual(MapGestureKind.None, g.PointerMove(1, 104f, 103f, 0.05f).Kind, "inside the slop: still a tap");
            Assert.AreEqual(MapGestureKind.Tap, g.PointerUp(1, 104f, 103f, 0.2f).Kind);

            g.PointerDown(1, 200f, 200f, 1f);
            Assert.AreEqual(MapGestureKind.None, g.Tick(1.5f).Kind);
            var longPress = g.Tick(1.65f);
            Assert.AreEqual(MapGestureKind.LongPress, longPress.Kind);
            Assert.AreEqual(200f, longPress.X);
            Assert.AreEqual(MapGestureKind.None, g.Tick(2.5f).Kind, "fires once");
            Assert.AreEqual(MapGestureKind.None, g.PointerUp(1, 200f, 200f, 2.6f).Kind, "a long-press is not also a tap");

            g.PointerDown(1, 50f, 50f, 3f);
            var pan = g.PointerMove(1, 70f, 50f, 3.1f);
            Assert.AreEqual(MapGestureKind.Pan, pan.Kind);
            Assert.AreEqual(20f, pan.DeltaX, 1e-4f, "first pan step includes the slop distance");
            Assert.AreEqual(5f, g.PointerMove(1, 75f, 50f, 3.15f).DeltaX, 1e-4f);
            Assert.AreEqual(MapGestureKind.None, g.Tick(4f).Kind, "dragging cancels the long-press");
            Assert.AreEqual(MapGestureKind.None, g.PointerUp(1, 75f, 50f, 3.2f).Kind);

            g.PointerDown(1, 100f, 100f, 5f);
            g.PointerDown(2, 200f, 100f, 5f);
            var pinch = g.PointerMove(2, 300f, 100f, 5.1f);
            Assert.AreEqual(MapGestureKind.Pinch, pinch.Kind);
            Assert.AreEqual(2f, pinch.Scale, 1e-4f, "fingers twice as far apart");
            Assert.AreEqual(200f, pinch.X, 1e-4f, "focus is the midpoint");
            Assert.AreEqual(MapGestureKind.None, g.PointerUp(2, 300f, 100f, 5.2f).Kind);
            Assert.AreEqual(MapGestureKind.None, g.PointerMove(1, 100f, 100f, 5.3f).Kind, "leftover finger after a pinch does nothing");
            Assert.AreEqual(MapGestureKind.None, g.Tick(7f).Kind, "and never long-presses");
            Assert.AreEqual(MapGestureKind.None, g.PointerUp(1, 100f, 100f, 7.1f).Kind);
            Assert.AreEqual(0, g.ActivePointers);
        }
    }
}
