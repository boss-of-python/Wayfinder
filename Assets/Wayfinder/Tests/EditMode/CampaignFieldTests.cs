using System.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Field loop for the vertical slice (§2.3, §8.2, §16.4) driven through the engine-free
    /// <see cref="SliceFieldController"/>, plus HUD/garage view models and the slice manifest
    /// (§21.3) checks that mirror Tools/Validation/validate_content.py.
    /// </summary>
    public class CampaignFieldTests
    {
        string _root;

        [SetUp]
        public void SetUp() => _root = NewTempRoot();

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        static FieldInput At(float x, float z, float speedKph, float dt, bool interact = false, bool tap = false) => new FieldInput
        {
            Pose = new Pose(x, 0.6f, z, 0f),
            SpeedKph = speedKph,
            DeltaSeconds = dt,
            FuelLiters = 24f,
            EngineCondition = 100,
            SuspensionCondition = 100,
            TiresCondition = 100,
            BodyCondition = 100,
            Grounded = true,
            ClearanceMeters = 0.3f,
            InteractPressed = interact,
            TapToInteract = tap
        };

        static FieldFrame Hold(SliceFieldController field, float x, float z, float seconds, float speedKph, bool interact = false,
            float dt = 0.1f)
        {
            // Integer frame count: float accumulation must not add or drop a frame near thresholds.
            FieldFrame frame = null;
            int frames = (int)System.Math.Round(seconds / dt);
            for (int i = 0; i < frames; i++) frame = field.Update(At(x, z, speedKph, dt, interact));
            return frame;
        }

        CampaignSession Departed(out SliceFieldController field)
        {
            var s = NewGame(new SaveService(_root));
            Ok(s.Depart(), "depart");
            field = new SliceFieldController(s);
            return s;
        }

        [Test]
        public void Field_LandmarkDiscoveredOnlyWhenSlowForTwoSeconds_S8_2()
        {
            var s = Departed(out var field);
            var rise = Content.Landmarks["landmark.forest.01"].Position;
            Hold(field, rise.X, rise.Z - 15f, 3f, speedKph: 30f);
            Assert.IsFalse(s.Discovery.IsLandmarkDiscovered("landmark.forest.01"), "driving past at 30 km/h never discovers");

            var frame = Hold(field, rise.X, rise.Z - 15f, 1.5f, speedKph: 10f);
            Assert.AreEqual("landmark.forest.01", frame.ObservingLandmarkId);
            Assert.IsFalse(s.Discovery.IsLandmarkDiscovered("landmark.forest.01"), "needs 2 s of continuous observation");
            frame = Hold(field, rise.X, rise.Z - 15f, 0.6f, speedKph: 10f);
            Assert.IsTrue(s.Discovery.IsLandmarkDiscovered("landmark.forest.01"));
            Assert.AreEqual(MissionState.Active, s.Missions.StateOf("Q01"));
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, s.Missions.ObjectiveCounters("Q01"));

            field.LineOfSight = (_, __) => false;
            var hollow = Content.Landmarks["landmark.forest.02"].Position;
            Hold(field, hollow.X + 10f, hollow.Z, 4f, speedKph: 5f);
            Assert.IsFalse(s.Discovery.IsLandmarkDiscovered("landmark.forest.02"), "line of sight is required");
        }

        [Test]
        public void Field_HoldToCollectCommitsExactlyOnce_S16_4()
        {
            var s = Departed(out var field);
            var cache = Content.Caches["cache.forest.001"].Position;
            var frame = field.Update(At(cache.X, cache.Z + 2f, 0f, 0.1f));
            Assert.AreEqual(FieldPromptKind.CollectCache, frame.Prompt.Kind);
            Assert.IsTrue(frame.Prompt.Available);

            frame = Hold(field, cache.X, cache.Z + 2f, 0.3f, 0f, interact: true);
            Assert.IsFalse(frame.InteractionCommitted, "0.35 s hold for pickups");
            Assert.Greater(frame.HoldProgress01, 0.5f);
            frame = field.Update(At(cache.X, cache.Z + 2f, 0f, 0.1f, interact: true));
            Assert.IsTrue(frame.InteractionCommitted);
            Assert.IsTrue(frame.InteractionResult.Success);
            int commits = 0;
            for (int i = 0; i < 20; i++)
                if (field.Update(At(cache.X, cache.Z + 2f, 0f, 0.1f, interact: true)).InteractionCommitted) commits++;
            Assert.AreEqual(0, commits, "one long press can never double-commit");
            Assert.AreEqual(77, s.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle));
            Assert.AreEqual(FieldPromptKind.None, field.Update(At(cache.X, cache.Z + 2f, 0f, 0.1f)).Prompt.Kind,
                "collected caches offer no prompt");
        }

        [Test]
        public void Field_TapModeCommitsOnPressStart_Accessibility()
        {
            var s = Departed(out var field);
            var cache = Content.Caches["cache.forest.002"].Position;
            field.Update(At(cache.X, cache.Z, 0f, 0.016f));
            var frame = field.Update(At(cache.X, cache.Z, 0f, 0.016f, interact: true, tap: true));
            Assert.IsTrue(frame.InteractionCommitted, "tap alternative to hold (§3.5)");
            Assert.AreEqual(91, s.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle));
        }

        [Test]
        public void Field_PromptBlockedWhenTooFastOrCargoFull()
        {
            var s = Departed(out var field);
            var cache = Content.Caches["cache.forest.007"].Position;
            var frame = Hold(field, cache.X, cache.Z, 1f, speedKph: 25f, interact: true);
            Assert.AreEqual("Slow down to interact", frame.Prompt.BlockedReason);
            Assert.IsFalse(s.Discovery.IsCacheCollected("cache.forest.007"));

            s.Inventory.Add(CampaignSession.Cargo, new ItemStack(new ContentId("scrap_metal"), 80)); // 8 slots → full
            frame = Hold(field, cache.X, cache.Z, 1f, speedKph: 0f, interact: true);
            StringAssert.StartsWith("Cargo full", frame.Prompt.BlockedReason);
            Assert.IsFalse(frame.InteractionCommitted);
            Assert.IsFalse(s.Discovery.IsCacheCollected("cache.forest.007"), "capacity failure never consumes the cache");
        }

        [Test]
        public void Field_CheckpointsCountInOrderAndInspectCompletesQ02()
        {
            var s = NewGame(new SaveService(_root));
            PlayQ01(s);
            Ok(s.AcceptMission("Q02"), "accept");
            Ok(s.Depart(), "depart");
            var field = new SliceFieldController(s);
            var route = Content.Slice.CheckpointRoutes.Single(r => r.Id == Ford);

            Hold(field, route.Checkpoints[1].X, route.Checkpoints[1].Z, 0.5f, 10f);
            CollectionAssert.AreEqual(new[] { 0, 0 }, s.Missions.ObjectiveCounters("Q02"), "far bank first does not count");
            Hold(field, route.Checkpoints[0].X, route.Checkpoints[0].Z, 0.5f, 10f);
            Hold(field, route.Checkpoints[1].X, route.Checkpoints[1].Z, 0.5f, 10f);
            CollectionAssert.AreEqual(new[] { 0, 2 }, s.Missions.ObjectiveCounters("Q02"));

            var gauge = Content.Slice.InspectTargets.Single();
            var frame = field.Update(At(gauge.Position.X, gauge.Position.Z + 3f, 0f, 0.1f));
            Assert.AreEqual(FieldPromptKind.Inspect, frame.Prompt.Kind);
            Hold(field, gauge.Position.X, gauge.Position.Z + 3f, 0.5f, 0f, interact: true);
            Assert.AreEqual(MissionState.ReadyToTurnIn, s.Missions.StateOf("Q02"));
            Assert.AreNotEqual(FieldPromptKind.Inspect, field.Update(At(gauge.Position.X, gauge.Position.Z + 3f, 0f, 0.1f)).Prompt.Kind,
                "no inspect prompt once the objective is done");
        }

        [Test]
        public void Field_ArrivalRequiresLeavingThenStoppingTwoSeconds_S2_3()
        {
            var s = Departed(out var field);
            var spawn = Content.Slice.GarageSpawn.Position;
            Hold(field, spawn.X, spawn.Z, 3f, 0f);
            Assert.IsTrue(s.Expedition.IsActive, "sitting at the departure spawn never ends the expedition");

            var cache = Content.Caches["cache.forest.001"].Position;
            Hold(field, cache.X, cache.Z, 0.5f, 0f, interact: true);       // collect on the way
            Hold(field, spawn.X, spawn.Z, 1.5f, 10f);                      // rolling through: too fast
            Assert.IsTrue(s.Expedition.IsActive);
            var frame = Hold(field, spawn.X, spawn.Z, 1.9f, 2f);           // < 1 m/s but under 2 s
            Assert.IsFalse(frame.ArrivalTriggered);
            Assert.IsTrue(s.Expedition.IsActive);
            frame = field.Update(At(spawn.X, spawn.Z, 2f, 0.15f));         // crosses 2 s on this frame
            Assert.IsTrue(frame.ArrivalTriggered);
            Assert.IsNotNull(frame.Extraction);
            Assert.IsTrue(frame.Extraction.Success, frame.Extraction.Result.ToString());
            Assert.AreEqual(77, s.Economy.BankedSalvage);
            Assert.IsFalse(s.Expedition.IsActive);
            Assert.IsFalse(field.Update(At(spawn.X, spawn.Z, 0f, 0.1f)).ArrivalTriggered, "inactive expedition: no field logic");
        }

        [Test]
        public void Field_TelemetryRevealsMapAndTracksDistanceAndFuel()
        {
            var s = Departed(out var field);
            float z = Content.Slice.GarageSpawn.Position.Z;
            int revealed = 0;
            for (int i = 0; i < 60; i++)
            {
                var input = At(1024f, z - i * 5f, 18f, 1f / 3f);
                input.FuelLiters = 24f - i * 0.01f;
                revealed += field.Update(input).CellsRevealed;
            }
            Assert.Greater(revealed, 0, "driving reveals cells within 96 m (§8.1)");
            Assert.AreEqual(295f, s.Expedition.Stats.DistanceMeters, 1f);
            Assert.AreEqual(0.59f, s.Expedition.Stats.FuelUsedLiters, 0.01f);
            Assert.IsTrue(s.Expedition.LastSafePose.IsSet);
            Assert.Less(s.Expedition.LastSafePose.Position.Z, z, "safe pose sampled while driving (§5.6)");
        }

        [Test]
        public void ViewModels_DescribeProgressFromContent()
        {
            var s = NewGame(new SaveService(_root));
            var hud = HudModel.Build(s);
            StringAssert.Contains("Q01 First Tracks", hud.ObjectiveLine);
            StringAssert.Contains("Collect Salvage from a cache", hud.ObjectiveLine);
            StringAssert.Contains("Cargo 4/12 slots", hud.CargoLine);
            Assert.AreEqual("Fuel 24.0 / 24 L", hud.FuelLine);

            var garage = GarageModel.Build(s);
            var tier1 = garage.Research[0];
            Assert.AreEqual("Trail Tires 1", tier1.Name);
            Assert.AreEqual(GarageModel.ResearchState.NeedsMission, tier1.State);
            StringAssert.Contains("grip", tier1.Result.ToLowerInvariant(), "exact change text from upgrades.json (§13.3)");
            Assert.AreEqual(GarageModel.ResearchState.NeedsPreviousTier, garage.Research[1].State);
            Assert.AreEqual(4, garage.Missions.Count, "slice lists Q01–Q04");
            Assert.AreEqual("The Creek Gauge", garage.Missions[1].Name);

            var summary = PlayQ01(s);
            string text = SummaryText.Describe(summary, s);
            StringAssert.Contains("Mission complete: Q01 First Tracks", text);
            StringAssert.Contains("refuelled and repaired", text);
            Assert.AreEqual(GarageModel.ResearchState.Available, GarageModel.Build(s).Research[0].State);
            StringAssert.Contains("New mission available", HudModel.Build(s).ObjectiveLine);
        }

        [Test]
        public void SliceManifest_MatchesBlueprintScope_S21_3()
        {
            var slice = Content.Slice;
            Assert.AreEqual(1024f, slice.AreaSizeMeters);
            Assert.AreEqual(6, slice.Landmarks.Count, "six landmarks");
            Assert.AreEqual(12, slice.CacheIds.Count, "twelve caches");
            Assert.AreEqual(2, slice.CampSockets.Count, "two camp sockets");
            CollectionAssert.AreEqual(new[] { "Q01", "Q02", "Q03", "Q04" }, slice.MissionCodes);
            Assert.AreEqual(6, slice.ResearchTierCount);
            Assert.AreEqual("garage.home", slice.GarageId);
            Assert.IsTrue(slice.Contains(slice.GarageSpawn.Position));

            foreach (var l in slice.Landmarks) Assert.IsTrue(slice.Contains(Content.Landmarks[l.Id].Position), l.Id);
            foreach (var id in slice.CacheIds)
            {
                var p = Content.Caches[id].Position;
                Assert.IsTrue(slice.Contains(p), id);
                Assert.GreaterOrEqual(p.DistanceXZ(slice.ArrivalCenter), 60f, id + " must not sit inside the garage yard");
            }
            foreach (var site in slice.CampSockets.Concat(slice.InspectTargets).Concat(slice.QuestItemSites).Concat(slice.Relays))
                Assert.IsTrue(slice.Contains(site.Position), site.Id);
            foreach (var route in slice.CheckpointRoutes)
                foreach (var cp in route.Checkpoints) Assert.IsTrue(slice.Contains(cp), route.Id);

            // Every slice mission objective has a reachable site in the manifest.
            var missions = new MissionService();
            foreach (var code in slice.MissionCodes)
                foreach (var o in missions.Definitions[code].Objectives)
                {
                    string t = o.TargetId.Value;
                    bool reachable = o.Type switch
                    {
                        MissionConditionType.DiscoverLandmark => slice.Landmarks.Any(l => l.Id == t),
                        MissionConditionType.InspectObject => slice.InspectTargets.Any(i => i.Id == t),
                        MissionConditionType.TraverseCheckpoints => slice.CheckpointRoutes.Any(r => r.Id == t && r.Checkpoints.Count >= o.Count),
                        MissionConditionType.EstablishCamp => slice.CampSockets.Any(c => c.Id == t),
                        MissionConditionType.ActivateRelay => slice.Relays.Any(r => r.Id == t),
                        MissionConditionType.CollectQuestItem => t == ItemCatalog.SalvageBundle || slice.QuestItemSites.Any(q => q.ItemId == t),
                        MissionConditionType.ReturnToHub => t == slice.GarageId,
                        _ => false
                    };
                    Assert.IsTrue(reachable, $"{code}: {o.Type} {t} has no site in the slice");
                }
        }

        [Test]
        public void Content_CacheContentsComeFromCachesJson_KI020()
        {
            Assert.IsTrue(Content.TryGetContents("cache.forest.037", out var contents));
            Assert.AreEqual(234, contents.SalvageUnits);
            Assert.AreEqual(3, contents.Stacks.Count, "salvage + scrap + electronics");
            Assert.AreEqual(200, Content.Caches.Count);
            Assert.AreEqual(100, Content.Landmarks.Count);
            Assert.AreEqual("Ranger Rise", Content.LandmarkName("landmark.forest.01"));
        }
    }
}
