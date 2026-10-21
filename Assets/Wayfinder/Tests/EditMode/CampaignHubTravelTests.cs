using System.IO;
using System.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Safe-hub travel (§10.4) through <see cref="CampaignSession"/>: only from an activated garage or
    /// camp, only to another activated hub that offers the service, never out of a dangerous field
    /// situation, free of charge, committed only with a verified save, and — once parked at a camp —
    /// the garage-only services (§12.1/§12.2) stay closed. Closes the hub-travel half of KI-028.
    /// </summary>
    public class CampaignHubTravelTests
    {
        const string CampA = "camp.forest.a";
        const string CampB = "camp.forest.b";

        /// <summary>camp.forest.a's socket in vertical_slice.json (1240, 0, 2520) r = 8 m.</summary>
        static readonly Pose AtCampA = new Pose(1240f, 0f, 2520f, 0f);
        static readonly Pose OutsideCampA = new Pose(1270f, 0f, 2520f, 0f);
        static readonly Pose OpenField = new Pose(1100f, 0.6f, 2400f, 0f);

        string _root;

        [SetUp]
        public void SetUp() => _root = NewTempRoot();

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        /// <summary>Q01–Q03 played: camp.forest.a is activated and the session is parked at home.</summary>
        CampaignSession WithCampA(out SaveService saves)
        {
            saves = new SaveService(_root);
            var s = NewGame(saves);
            PlayQ01(s);
            PlayQ02(s);
            PlayQ03(s);
            s.DrainNotices();
            return s;
        }

        static void DriveTo(CampaignSession s, Pose pose) =>
            s.UpdateVehicle(pose, 40f, 100, 100, 100, 100, 0.1f);

        // ================================================================== where you are

        [Test]
        public void CurrentHubId_IsTheGarageWhenParked_AndNullInTheOpenField()
        {
            var s = WithCampA(out _);
            Assert.AreEqual(Home, s.CurrentHubId, "parked at the home garage after extraction");
            Assert.IsTrue(s.InGarage && s.AtGarage);

            Ok(s.Depart(), "depart");
            DriveTo(s, OpenField);
            Assert.IsNull(s.CurrentHubId, "the open field is not a hub");
        }

        [Test]
        public void CurrentHubId_IsTheCamp_OnlyInsideItsSocketRadius_AndOnlyOnceActivated()
        {
            var s = WithCampA(out _);
            Ok(s.Depart(), "depart");

            DriveTo(s, AtCampA);
            Assert.AreEqual(CampA, s.CurrentHubId, "inside an activated camp's 8 m socket");

            DriveTo(s, OutsideCampA);
            Assert.IsNull(s.CurrentHubId, "30 m away is outside the socket");

            // An unactivated socket is not a hub, however close you park.
            var fresh = NewGame(new SaveService(NewTempRoot()));
            Ok(fresh.Depart(), "depart");
            DriveTo(fresh, AtCampA);
            Assert.IsNull(fresh.CurrentHubId, "the camp is not established yet");
        }

        // ================================================================== the move itself

        [Test]
        public void HubTravel_FromTheHomeGarage_ToAnActivatedCamp_MovesTheVehicleAndRebasesDeparture()
        {
            var s = WithCampA(out _);
            int wallet = s.Economy.BankedSalvage;

            Ok(s.TravelToHub(CampA, inWater: false, inDeepMud: false), "garage → camp");

            Assert.AreEqual(CampA, s.CurrentHubId, "now parked at the camp");
            Assert.AreEqual(CampA, s.Expedition.LastGarageId, "the hub you sit at is the next departure point (§2.3)");
            Assert.AreEqual(1240f, s.Expedition.Pose.Position.X, 0.001f, "moved to the camp socket");
            Assert.AreEqual(2520f, s.Expedition.Pose.Position.Z, 0.001f);
            Assert.AreEqual(s.Expedition.Pose.Position.X, s.Expedition.LastSafePose.Position.X, 0.001f,
                "the arrival pose is a safe pose");
            Assert.AreEqual(wallet, s.Economy.BankedSalvage, "hub travel has no currency fee (§10.4)");
            Assert.IsTrue(s.InGarage, "still parked — no expedition was started");
            Assert.IsFalse(s.AtGarage, "a camp is not a garage");

            Ok(s.Depart(), "depart");
            Assert.AreEqual(1240f, s.Expedition.Pose.Position.X, 0.001f, "the expedition starts from the camp");
        }

        [Test]
        public void HubTravel_MidExpedition_KeepsTheExpeditionAndItsExtractionTarget()
        {
            var s = WithCampA(out _);
            Ok(s.TravelToHub(CampA, inWater: false, inDeepMud: false), "park at the camp first");
            Ok(s.Depart(), "depart from the camp");
            Assert.AreEqual(CampA, s.Expedition.LastGarageId, "the camp is the extraction target");
            DriveTo(s, OpenField);
            DriveTo(s, AtCampA);
            Assert.AreEqual(CampA, s.CurrentHubId);

            Ok(s.TravelToHub(Home, inWater: false, inDeepMud: false), "camp → home garage mid-expedition");

            Assert.IsTrue(s.Expedition.IsActive, "fast travel does not end the expedition");
            Assert.AreEqual(CampA, s.Expedition.LastGarageId,
                "mid-expedition the extraction target must not move (§12.4)");
            Assert.AreEqual(1024f, s.Expedition.Pose.Position.X, 0.001f, "parked on the home garage spawn");

            var summary = Return(s);
            Assert.IsTrue(summary.Success, "the expedition still banks at the home garage");
        }

        [Test]
        public void HubTravel_DoesNotInflateExpeditionDistance_WithTheTeleport()
        {
            var s = WithCampA(out _);
            Ok(s.Depart(), "depart");
            DriveTo(s, OpenField);
            double before = s.Expedition.Stats.DistanceMeters;

            DriveTo(s, AtCampA);
            Ok(s.TravelToHub(Home, inWater: false, inDeepMud: false), "camp → garage");
            DriveTo(s, new Pose(1030f, 0.6f, 2290f, 0f));

            Assert.LessOrEqual(s.Expedition.Stats.DistanceMeters - before, 200d,
                "a hub transfer is not driven distance (§16.2 mapping must not be fed by teleports)");
        }

        [Test]
        public void HubTravel_EmitsItsNotice_OnlyWhenTheMoveIsCommitted()
        {
            var s = WithCampA(out _);
            Ok(s.TravelToHub(CampA, inWater: false, inDeepMud: false), "travel");
            var notices = s.DrainNotices();
            Assert.IsTrue(notices.Any(n => n.Kind == CampaignNoticeKind.HubTravelled && n.Id == CampA),
                "a committed transfer announces itself (§13.2)");

            var refused = s.TravelToHub(CampB, inWater: false, inDeepMud: false);
            Assert.IsFalse(refused.Success);
            Assert.IsFalse(s.DrainNotices().Any(n => n.Kind == CampaignNoticeKind.HubTravelled),
                "never emit a notice for rolled-back or refused work");
        }

        // ================================================================== refusals

        [Test]
        public void HubTravel_IsRefused_OutOfWaterOrDeepMud()
        {
            var s = WithCampA(out _);
            string hubBefore = s.CurrentHubId;

            foreach (var (water, mud, what) in new[]
                     {
                         (true, false, "in water"),
                         (false, true, "in deep mud"),
                         (true, true, "in both")
                     })
            {
                var refused = s.TravelToHub(CampA, water, mud);
                Assert.IsFalse(refused.Success, what);
                Assert.AreEqual(FailureReason.WrongState, refused.Reason, what);
                StringAssert.Contains("dangerous field situation", refused.Detail, what);
                Assert.AreEqual(hubBefore, s.CurrentHubId, "nothing moved: " + what);
            }
        }

        [Test]
        public void HubTravel_IsRefused_InTheOpenField_WhereFullRescueIsTheOnlyWayOut()
        {
            var s = WithCampA(out _);
            Ok(s.Depart(), "depart");
            DriveTo(s, OpenField);

            s.DrainNotices(); // clear Depart's "Expedition started"

            // Deliberately to a *different* activated hub, so the refusal can only come from "you are
            // not at a hub" — not from "already at that hub".
            var refused = s.TravelToHub(CampA, inWater: false, inDeepMud: false);
            Assert.AreEqual(FailureReason.WrongState, refused.Reason);
            StringAssert.Contains("activated garage or camp", refused.Detail);
            Assert.AreEqual(OpenField.Position.X, s.Expedition.Pose.Position.X, 0.001f, "the vehicle did not move");
            Assert.AreEqual(OpenField.Position.Z, s.Expedition.Pose.Position.Z, 0.001f);
            Assert.AreEqual(Home, s.Expedition.LastGarageId, "the extraction target is untouched");
            Assert.IsTrue(s.Expedition.IsActive, "the expedition is untouched");
            Assert.AreEqual(0, s.PendingNoticeCount, "a refusal is silent");

            // §10.4: full rescue is the way out of a dangerous field situation instead.
            var rescued = s.FullRescue();
            Assert.IsTrue(rescued.Success, "full rescue: " + rescued.Result);
            Assert.IsTrue(rescued.WasRescue);
            Assert.IsTrue(s.InGarage, "rescue parks the vehicle at a garage");
        }

        [Test]
        public void HubTravel_IsRefused_ToAHubThatIsNotActivated_OrUnknown_OrItself()
        {
            var s = WithCampA(out _);

            var notActivated = s.TravelToHub(CampB, false, false);
            Assert.AreEqual(FailureReason.InvalidTarget, notActivated.Reason, "camp.forest.b was never established");

            var unknown = s.TravelToHub("camp.nowhere", false, false);
            Assert.AreEqual(FailureReason.InvalidTarget, unknown.Reason, "an unknown hub is an invalid target");

            var empty = s.TravelToHub(null, false, false);
            Assert.AreEqual(FailureReason.InvalidTarget, empty.Reason);

            var same = s.TravelToHub(Home, false, false);
            Assert.AreEqual(FailureReason.WrongState, same.Reason, "already at the home garage");
            Assert.AreEqual(Home, s.CurrentHubId);
        }

        [Test]
        public void HubTravel_RollsBackCompletely_WhenTheVerifiedSaveFails()
        {
            var s = WithCampA(out var saves);
            var poseBefore = s.Expedition.Pose;

            saves.StepHook = step =>
            {
                if (step == SaveStep.TempVerified) throw new IOException("simulated write failure");
            };
            var failed = s.TravelToHub(CampA, inWater: false, inDeepMud: false);

            Assert.AreEqual(FailureReason.SaveIoFailure, failed.Reason, "success is never claimed without a verified save");
            Assert.AreEqual(Home, s.CurrentHubId, "the move was rolled back");
            Assert.AreEqual(Home, s.Expedition.LastGarageId);
            Assert.AreEqual(poseBefore.Position.X, s.Expedition.Pose.Position.X, 0.001f, "the vehicle did not move");
            Assert.IsFalse(s.DrainNotices().Any(n => n.Kind == CampaignNoticeKind.HubTravelled));

            saves.StepHook = null;
            Ok(s.TravelToHub(CampA, inWater: false, inDeepMud: false), "retry after the write recovers");
            Assert.AreEqual(CampA, s.CurrentHubId);
        }

        [Test]
        public void HubTravel_IsACriticalSave_AndSurvivesReload()
        {
            var s = WithCampA(out _);
            int revisionBefore = s.Revision;
            Ok(s.TravelToHub(CampA, inWater: false, inDeepMud: false), "travel");
            Assert.Greater(s.Revision, revisionBefore, "hub travel is a critical, immediately verified save");
            Assert.IsFalse(s.HasUnsavedChanges, "nothing is left pending");

            var reloaded = Reload(_root);
            Assert.AreEqual(CampA, reloaded.CurrentHubId, "the session reloads parked at the camp");
            Assert.AreEqual(CampA, reloaded.Expedition.LastGarageId);
            Assert.AreEqual(1240f, reloaded.Expedition.Pose.Position.X, 0.001f);
            Assert.IsFalse(reloaded.AtGarage, "a camp is still not a garage after a reload");
        }

        // ================================================================== §10.4 service split

        [Test]
        public void ACampOffersNoGarageServices_WhileParkedThere()
        {
            var s = WithCampA(out _);
            Ok(s.TravelToHub(CampA, inWater: false, inDeepMud: false), "travel to the camp");
            Assert.IsFalse(s.AtGarage);

            // §10.4: a camp offers save, deposit, fuel, repair supplies, time of day, map and hub
            // travel — "No permanent research or vehicle changes", and no mission board, loadout,
            // crafting or selling.
            Assert.AreEqual(FailureReason.WrongState, s.ResearchUpgrade("upgrade.scout.01.trailTires1").Reason, "research is garage-only");
            Assert.AreEqual(FailureReason.WrongState, s.Craft("recipe.repair_kit").Reason, "crafting is garage-only");
            Assert.AreEqual(FailureReason.WrongState, s.LoadCargo("fuel_can", 1).Reason, "loadout is garage-only");
            Assert.AreEqual(FailureReason.WrongState, s.UnloadCargo("fuel_can", 1).Reason, "loadout is garage-only");
            Assert.AreEqual(FailureReason.WrongState, s.AcceptMission("Q04").Reason, "the mission board is garage-only");
            Assert.AreEqual(FailureReason.WrongState, s.AcceptContract("C_F01").Reason,
                "the contract board is garage-only");
            Assert.AreEqual(FailureReason.WrongState, s.SellItems("scrap_metal", 1).Reason, "selling is garage-only");

            // The camp's own functions still work.
            Assert.AreEqual(SaveResultStatus.Saved, s.SaveNow().Status, "a camp still saves (§10.4)");
            Ok(s.TravelToHub(Home, inWater: false, inDeepMud: false), "and it still offers hub travel");
            Assert.IsTrue(s.AtGarage, "back at a garage");
            // The loadout gate is a *garage* gate, not an inventory gate: at the garage the call gets
            // past it and only the missing item stops it (the session starts with no fuel cans).
            Assert.AreEqual(FailureReason.ItemNotFound, s.LoadCargo("fuel_can", 1).Reason,
                "garage services reopen immediately");
        }

        [Test]
        public void GarageServicesAreUnaffected_WhenNoHubTravelHasHappened()
        {
            // AtGarage must equal InGarage for every save that predates hub travel, so this batch
            // cannot change existing behaviour (D-023 / KI-028).
            var s = WithCampA(out _);
            Assert.AreEqual(s.InGarage, s.AtGarage);
            Assert.IsTrue(s.AtGarage, "the home garage is a garage");
            Assert.AreEqual(FailureReason.ItemNotFound, s.LoadCargo("fuel_can", 1).Reason,
                "loadout at the home garage passes the gate");
            Ok(s.AcceptMission("Q04"), "mission board at the home garage");
        }

        // ================================================================== content gate

        [Test]
        public void Content_DeclaresHubTravel_ForEveryGarageAndCamp()
        {
            var content = Content;
            Assert.AreEqual(5, content.Garages.Count, "release-scope garages");
            Assert.AreEqual(10, content.Camps.Count, "release-scope camps");

            foreach (var garage in content.Garages.Values)
            {
                Assert.IsNotEmpty(garage.Services, $"{garage.Id} services parsed from garages.json");
                Assert.IsTrue(content.OffersHubTravel(garage.Id), $"{garage.Id} offers safe-hub travel (§10.4)");
            }
            foreach (var camp in content.Camps.Values)
            {
                Assert.IsNotEmpty(camp.Functions, $"{camp.Id} functions parsed from camps.json");
                Assert.IsTrue(content.OffersHubTravel(camp.Id), $"{camp.Id} offers safe-hub travel (§10.4)");
            }

            Assert.IsFalse(content.OffersHubTravel("garage.unknown"), "an unknown hub offers nothing");
            Assert.IsFalse(content.OffersHubTravel(null), "and neither does no hub");
            Assert.IsFalse(content.OffersHubTravel("landmark.forest.01"), "a landmark is not a hub");
        }

        [Test]
        public void HubTravel_IsRefused_WhenTheDestinationContentWithholdsTheService()
        {
            // The gate is data-driven, so prove it against a catalog that really withholds the
            // service: strip "hubTravel" from camp.forest.a's functions and rebuild the session on
            // that catalog. The shipped content stays complete (all 15 hubs offer it).
            var content = ContentWithoutHubTravel(CampA);
            Assert.IsFalse(content.OffersHubTravel(CampA), "the rebuilt catalog withholds it");
            Assert.IsTrue(content.OffersHubTravel(Home), "the home garage still offers it");

            var opened = CampaignSession.StartNew(new SaveService(_root), content, new SaveSlotId(0));
            Assert.AreEqual(CampaignOpenStatus.NewGameSaved, opened.Status, opened.Detail);
            Assert.IsTrue(opened.Playable);
            var session = opened.Session;
            PlayQ01(session);
            PlayQ02(session);
            PlayQ03(session);

            var refused = session.TravelToHub(CampA, inWater: false, inDeepMud: false);
            Assert.IsFalse(refused.Success, "an activated camp that does not offer the service is not a destination");
            Assert.AreEqual(FailureReason.InvalidTarget, refused.Reason);
            StringAssert.Contains("safe-hub travel", refused.Detail);
            Assert.AreEqual(Home, session.CurrentHubId, "the vehicle did not move");
        }

        /// <summary>
        /// Rebuilds the real catalog with <c>hubTravel</c> removed from one hub's own entry
        /// (garages.json <c>services</c> or camps.json <c>functions</c>).
        /// </summary>
        static CampaignContent ContentWithoutHubTravel(string hubId)
        {
            var files = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (var path in System.IO.Directory.GetFiles(ContentDir, "*.json"))
                files[System.IO.Path.GetFileName(path)] = System.IO.File.ReadAllText(path);

            string file = hubId.StartsWith("camp") ? "camps.json" : "garages.json";
            var array = Newtonsoft.Json.Linq.JArray.Parse(files[file]);
            bool stripped = false;
            foreach (var token in array)
            {
                var entry = (Newtonsoft.Json.Linq.JObject)token;
                if (entry.Value<string>("id") != hubId) continue;
                var list = (Newtonsoft.Json.Linq.JArray)entry[hubId.StartsWith("camp") ? "functions" : "services"];
                var service = list.FirstOrDefault(x => x.ToString() == CampaignContent.HubTravelService);
                if (service == null) continue;
                service.Remove();
                stripped = true;
            }
            Assert.IsTrue(stripped, $"hubTravel removed from {hubId} in {file}");
            files[file] = array.ToString(Newtonsoft.Json.Formatting.None);
            return CampaignContent.FromJson(files);
        }
    }
}
