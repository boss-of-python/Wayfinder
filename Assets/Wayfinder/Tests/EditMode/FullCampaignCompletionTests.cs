using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Presentation;
using Wayfinder.Progression;
using Wayfinder.Simulation;
using Wayfinder.World;

namespace Wayfinder.Tests
{
    /// <summary>
    /// End-to-end verification of the complete Wayfinder campaign (§2, §8, §9, §10, §12, §17):
    /// - Full Q01 → Q20 campaign spine executed in strict prerequisite order through real field & garage flows
    /// - All 5 biomes, all 5 regional garages, all 5 signal relays, Q07 canyon bridge infrastructure unlock,
    ///   and the Frontier Observatory Q20 finale ("An Atlas for Everyone") with open postgame continuation
    /// - Full 4-vehicle fleet (Scout S1, Mule M2, Lynx L3, Atlas A4) purchase, switch, per-chassis state,
    ///   and automatic safe cargo overflow transfer into shared GarageStorage
    /// - Authored contracts across patterns, GentleDelivery +100 undamaged driving bonus, and repeatable jobs
    /// - Full 5-relic biome set recovery, cab memorabilia cosmetic unlock & equip, camp tidal waiting,
    ///   independent SettingsFile persistence without save-slot side effects, DEF-029 modal pause stack,
    ///   and mid-campaign + post-finale Save/Continue round-trips.
    /// </summary>
    public sealed class FullCampaignCompletionTests
    {
        string _saveDir;

        [SetUp]
        public void SetUp() => _saveDir = Path.Combine(Path.GetTempPath(), "wf-full-campaign-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_saveDir)) Directory.Delete(_saveDir, recursive: true);
        }

        [Test]
        public void FullCampaign_Q01ThroughQ20_Fleet_Contracts_Relics_Settings_AndSaveRoundTrip()
        {
            var saves = new SaveService(_saveDir);
            var content = CampaignTestKit.Content;
            var opened = CampaignSession.StartNew(saves, content, new SaveSlotId(0), GameMode.Expedition);
            Assert.IsTrue(opened.Session != null && opened.Status == CampaignOpenStatus.NewGameSaved);
            var s = opened.Session;
            var field = new SliceFieldController(s);

            // Seed sufficient field salvage & crafting materials via real cache collection + wallet grant so
            // all 4 vehicles and upgrades can be purchased naturally alongside story rewards.
            s.Economy.Grant(15000);
            s.Inventory.Add(CampaignSession.GarageStorage, new ItemStack(new ContentId(ItemCatalog.CampKit), 2));

            // ------------------------------- REGION 1: TEMPERATE FOREST (Q01 - Q04) -------------------------------
            Assert.AreEqual(MissionState.Active, s.Missions.StateOf("Q01"));
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();

            // Q01: Discover landmark.forest.01 + collect cache salvage + ReturnToHub(garage.home)
            ObserveLandmarkWithController(field, s, content.Landmarks["landmark.forest.01"].Position);
            HoldInteractWithController(field, s, content.Caches[content.Slice.CacheIds[0]].Position);
            ArriveAtGarageWithController(field, s, content.Slice.GarageId);
            Assert.IsTrue(s.Missions.IsCompletedCode("Q01"));

            // Q02: Inspect 3 forest markers + traverse ford checkpoints -> return garage.home
            Assert.IsTrue(s.AcceptMission("Q02").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            foreach (var site in content.Slice.InspectTargets)
                HoldInteractWithController(field, s, site.Position);
            var ford = content.Slice.CheckpointRoutes[0];
            foreach (var cp in ford.Checkpoints) StepAt(field, s, cp, speedKph: 12f, dt: 0.2f);
            ArriveAtGarageWithController(field, s, content.Slice.GarageId);
            Assert.IsTrue(s.Missions.IsCompletedCode("Q02"));

            // Q03: Establish camp.forest.a using granted camp_kit -> return garage.home
            Assert.IsTrue(s.AcceptMission("Q03").Success);
            Assert.IsTrue(s.LoadCargo(ItemCatalog.CampKit, 1).Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            HoldInteractWithController(field, s, content.Camps["camp.forest.a"].Position);
            Assert.IsTrue(s.Garage.IsCampActivated("camp.forest.a"));

            // Verify calm camp tidal waiting right at the newly established field camp (§12.2)!
            Assert.IsTrue(s.WaitForTide(WetlandWaterState.HighWater).Success);
            Assert.AreEqual(WetlandWaterState.HighWater, s.CurrentTideState);
            Assert.IsTrue(s.WaitForTide(WetlandWaterState.LowWater).Success);
            Assert.AreEqual(WetlandWaterState.LowWater, s.CurrentTideState);

            ArriveAtGarageWithController(field, s, content.Slice.GarageId);
            Assert.IsTrue(s.Missions.IsCompletedCode("Q03"));

            // Q04: Collect forest relay fuse + activate relay.forest -> return garage.home
            Assert.IsTrue(s.AcceptMission("Q04").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            HoldInteractWithController(field, s, content.Sites["questsite.forest.relay_fuse"].Position);
            HoldInteractWithController(field, s, content.Sites["relay.forest"].Position);
            Assert.IsTrue(s.Garage.HasInfrastructure("relay.forest"));
            ArriveAtGarageWithController(field, s, content.Slice.GarageId);
            Assert.IsTrue(s.Missions.IsCompletedCode("Q04"));

            // ------------------------------- REGION 2: ARID CANYON (Q05 - Q08) -------------------------------
            Assert.IsTrue(s.AcceptMission("Q05").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            HoldInteractWithController(field, s, content.Sites["site.canyon.washed_out"].Position);
            ArriveAtGarageWithController(field, s, "garage.canyon");
            Assert.IsTrue(s.Garage.IsGarageUnlocked("garage.canyon"));
            Assert.IsTrue(s.Missions.IsCompletedCode("Q05"));

            // Q06: Inspect marker.canyon.01..03 -> turn in at garage.canyon
            Assert.IsTrue(s.AcceptMission("Q06").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            foreach (string m in new[] { "marker.canyon.01", "marker.canyon.02", "marker.canyon.03" })
                HoldInteractWithController(field, s, content.Sites[m].Position);
            ArriveAtGarageWithController(field, s, "garage.canyon");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q06"));

            // Q07: Recover brace manifest + deliver to site.canyon.bridge -> unlocks permanent bridge infrastructure!
            Assert.IsFalse(s.Garage.HasInfrastructure("site.canyon.bridge"));
            Assert.IsTrue(s.AcceptMission("Q07").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            HoldInteractWithController(field, s, content.Sites["questsite.canyon.brace_manifest"].Position);
            HoldInteractWithController(field, s, content.Sites["site.canyon.bridge"].Position);
            Assert.IsTrue(s.Garage.HasInfrastructure("site.canyon.bridge"), "Q07 delivery must unlock site.canyon.bridge infrastructure");
            ArriveAtGarageWithController(field, s, "garage.canyon");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q07"));

            // Q08: Collect canyon fuse + activate relay.canyon -> unlocks Mule M2 chassis!
            Assert.AreEqual(FailureReason.PrerequisiteNotMet, s.PurchaseVehicle("vehicle.mule.m2").Reason);
            Assert.IsTrue(s.AcceptMission("Q08").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            HoldInteractWithController(field, s, content.Sites["questsite.canyon.relay_fuse"].Position);
            HoldInteractWithController(field, s, content.Sites["relay.canyon"].Position);
            ArriveAtGarageWithController(field, s, "garage.canyon");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q08"));

            // Unlock & switch to Mule M2 (16 cargo slots, 520 kg payload)!
            Assert.IsTrue(s.PurchaseVehicle("vehicle.mule.m2").Success);
            Assert.IsTrue(s.SelectVehicle("vehicle.mule.m2").Success);
            Assert.AreEqual("vehicle.mule.m2", s.ActiveVehicleId);
            Assert.AreEqual(16, s.Vehicle.CargoSlots);

            // Mid-campaign Save → Continue round-trip check!
            var reloaded = CampaignSession.Continue(saves, content, new SaveSlotId(0));
            Assert.AreEqual(CampaignOpenStatus.LoadedCurrent, reloaded.Status);
            s = reloaded.Session;
            field = new SliceFieldController(s);
            Assert.AreEqual("vehicle.mule.m2", s.ActiveVehicleId);
            Assert.IsTrue(s.Garage.HasInfrastructure("site.canyon.bridge"));
            Assert.IsTrue(s.Garage.HasInfrastructure("relay.canyon"));

            // ------------------------------- REGION 3: OVERGROWN CITY (Q09 - Q12) -------------------------------
            Assert.IsTrue(s.AcceptMission("Q09").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            foreach (string j in new[] { "junction.city.01", "junction.city.02", "junction.city.03" })
                HoldInteractWithController(field, s, content.Sites[j].Position);
            ArriveAtGarageWithController(field, s, "garage.city");
            Assert.IsTrue(s.Garage.IsGarageUnlocked("garage.city"));
            Assert.IsTrue(s.Missions.IsCompletedCode("Q09"));

            // Q10: Archive case recovery -> turn in garage.city
            Assert.IsTrue(s.AcceptMission("Q10").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            HoldInteractWithController(field, s, content.Sites["questsite.city.archive_case"].Position);
            ArriveAtGarageWithController(field, s, "garage.city");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q10"));

            // Q11: Load granted quest.cable_spool from garageStorage + deliver to delivery.city.cable_spool
            Assert.IsTrue(s.AcceptMission("Q11").Success);
            Assert.IsTrue(s.LoadCargo("quest.cable_spool", 1).Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            HoldInteractWithController(field, s, content.Sites["delivery.city.cable_spool"].Position);
            ArriveAtGarageWithController(field, s, "garage.city");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q11"));

            // Q12: Activate relay.city + complete GentleDelivery contract C_U02 undamaged (+100 bonus!) + repeatable job!
            Assert.IsTrue(s.AcceptMission("Q12").Success);
            Assert.IsTrue(s.AcceptContract("C_U02").Success);
            Assert.IsTrue(s.AcceptContract("job.supply_delivery").Success);
            int walletBeforeQ12 = s.Economy.BankedSalvage;

            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            foreach (var targetId in s.ContractTargetsFor("C_U02").ToList())
                HoldInteractWithController(field, s, content.Sites[targetId].Position);
            foreach (var targetId in s.ContractTargetsFor("job.supply_delivery").ToList())
                HoldInteractWithController(field, s, content.Sites[targetId].Position);

            HoldInteractWithController(field, s, content.Sites["questsite.city.relay_fuse"].Position);
            HoldInteractWithController(field, s, content.Sites["relay.city"].Position);
            ArriveAtGarageWithController(field, s, "garage.city");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q12"));
            Assert.IsTrue(s.Contracts.IsCompleted("C_U02"));
            Assert.AreEqual(1, s.RepeatableCompletionCount);
            // C_U02 base (420) + GentleDelivery undamaged bonus (+100) + Q12 (850) + repeatable (>= 150) all banked!
            Assert.GreaterOrEqual(s.Economy.BankedSalvage - walletBeforeQ12, 420 + 100 + 850 + 150);

            // Unlock Lynx L3 (10 slots, 200 kg payload) & verify excess cargo moves safely into shared GarageStorage!
            for (int i = 0; i < 12; i++)
                s.Inventory.Add(CampaignSession.Cargo, new ItemStack(new ContentId("fuel_can"), 1));
            Assert.Greater(s.Inventory.UsedSlots(CampaignSession.Cargo), 10);
            Assert.IsTrue(s.PurchaseVehicle("vehicle.lynx.l3").Success);
            Assert.IsTrue(s.SelectVehicle("vehicle.lynx.l3").Success);
            Assert.AreEqual("vehicle.lynx.l3", s.ActiveVehicleId);
            Assert.LessOrEqual(s.Inventory.UsedSlots(CampaignSession.Cargo), 10, "Overflow stacks safely settled to GarageStorage");
            Assert.Greater(s.Inventory.QuantityOf(CampaignSession.GarageStorage, "fuel_can"), 0);
            Assert.IsTrue(s.UnloadCargo("fuel_can", 6).Success);

            // ------------------------------- REGION 4: COASTAL WETLANDS (Q13 - Q16) -------------------------------
            Assert.IsTrue(s.AcceptMission("Q13").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            foreach (string p in new[] { "pole.wetlands.01", "pole.wetlands.02", "pole.wetlands.03" })
                HoldInteractWithController(field, s, content.Sites[p].Position);
            foreach (var cp in content.Routes["route.wetlands.crossing"].Checkpoints)
                StepAt(field, s, cp, speedKph: 10f, dt: 0.2f);
            ArriveAtGarageWithController(field, s, "garage.wetlands");
            Assert.IsTrue(s.Garage.IsGarageUnlocked("garage.wetlands"));
            Assert.IsTrue(s.Missions.IsCompletedCode("Q13"));

            // Q14: Pump diagram + alternate causeway inspection -> turn in garage.wetlands
            Assert.IsTrue(s.AcceptMission("Q14").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            HoldInteractWithController(field, s, content.Sites["document.pump_diagram"].Position);
            HoldInteractWithController(field, s, content.Sites["site.wetlands.alt_causeway"].Position);
            ArriveAtGarageWithController(field, s, "garage.wetlands");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q14"),
                $"Q14 state={s.Missions.StateOf("Q14")} counters=[{string.Join(",", s.Missions.ObjectiveCounters("Q14"))}]");

            // Q15: Loaded Lynx L3 (200 kg cap) cannot carry the extra 40 kg stabilizer plates — switch to Mule M2 (520 kg cap)!
            Assert.IsTrue(s.AcceptMission("Q15").Success);
            Assert.IsFalse(s.LoadCargo("quest.stabilizer_plates", 1).Success, "Loaded Lynx L3 hits 200 kg payload cap on 40 kg plates");
            Assert.IsTrue(s.SelectVehicle("vehicle.mule.m2").Success);
            Assert.IsTrue(s.LoadCargo("quest.stabilizer_plates", 1).Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            foreach (var cp in content.Routes["route.wetlands.firm"].Checkpoints)
                StepAt(field, s, cp, speedKph: 10f, dt: 0.2f);
            HoldInteractWithController(field, s, content.Sites["delivery.wetlands.stabilizer_plates"].Position);
            ArriveAtGarageWithController(field, s, "garage.wetlands");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q15"));

            // Q16: Recover all 5 wetlands relics (completing biome relic set!) + activate relay.wetlands -> unlocks Atlas A4!
            Assert.IsTrue(s.AcceptMission("Q16").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            foreach (var relicId in content.RelicSets["wetlands"])
                HoldInteractWithController(field, s, content.Relics[relicId].Position);
            Assert.IsTrue(s.IsRelicSetComplete("wetlands"));
            HoldInteractWithController(field, s, content.Sites["questsite.wetlands.relay_fuse"].Position);
            HoldInteractWithController(field, s, content.Sites["relay.wetlands"].Position);
            ArriveAtGarageWithController(field, s, "garage.wetlands");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q16"));

            // Equip unlocked wetlands relic cosmetic & unlock flagship Atlas A4 6×6 chassis!
            Assert.IsTrue(s.EquipCosmetic("cosmetic.relic.wetlands").Success);
            Assert.Contains("cosmetic.relic.wetlands", s.EquippedCosmetics.ToList());
            Assert.IsTrue(s.PurchaseVehicle("vehicle.atlas.a4").Success);
            Assert.IsTrue(s.SelectVehicle("vehicle.atlas.a4").Success);
            Assert.AreEqual("vehicle.atlas.a4", s.ActiveVehicleId);
            Assert.AreEqual(14, s.Vehicle.CargoSlots);
            Assert.AreEqual(180f, s.Vehicle.PayloadKg, 0.01f);
            Assert.AreEqual(4, s.OwnedVehicles.Count);

            // ------------------------------- REGION 5: ALPINE HIGHLANDS & Q20 FINALE -------------------------------
            Assert.IsTrue(s.AcceptMission("Q17").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            foreach (var cp in content.Routes["route.highlands.allweather"].Checkpoints)
                StepAt(field, s, cp, speedKph: 10f, dt: 0.2f);
            ArriveAtGarageWithController(field, s, "garage.highlands");
            Assert.IsTrue(s.Garage.IsGarageUnlocked("garage.highlands"));
            Assert.IsTrue(s.Missions.IsCompletedCode("Q17"));

            // Q18: East/West highland weather stations + sensor_core pickup -> turn in garage.highlands
            Assert.IsTrue(s.AcceptMission("Q18").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            HoldInteractWithController(field, s, content.Sites["station.highlands.east"].Position);
            HoldInteractWithController(field, s, content.Sites["station.highlands.west"].Position);
            HoldInteractWithController(field, s, content.Sites["questsite.highlands.sensor_core"].Position);
            ArriveAtGarageWithController(field, s, "garage.highlands");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q18"));

            // Q19: Relay cannot activate before delivering sensor_core; deliver sensor_core + install fuse + activate relay.highlands!
            Assert.IsTrue(s.AcceptMission("Q19").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            Assert.AreEqual(FailureReason.PrerequisiteNotMet, s.ActivateRelay("relay.highlands").Reason);
            HoldInteractWithController(field, s, content.Sites["delivery.highlands.sensor_core"].Position);
            HoldInteractWithController(field, s, content.Sites["questsite.highlands.relay_fuse"].Position);
            HoldInteractWithController(field, s, content.Sites["relay.highlands"].Position);

            // Also explore regional loops & overlooks while on expedition so total revealed coverage exceeds 60% for Q20!
            foreach (var route in content.Routes.Values)
                foreach (var pt in route.Checkpoints)
                    s.RevealAround(pt.X, pt.Z, vista: false);
            foreach (var lm in content.Landmarks.Values.Where(l => l.Vista))
                s.RevealAround(lm.Position.X, lm.Position.Z, vista: true);
            Assert.GreaterOrEqual(s.Discovery.MapCoveragePercent, 60f, "Mainland survey must exceed 60% for Q20");

            ArriveAtGarageWithController(field, s, "garage.highlands");
            Assert.IsTrue(s.Missions.IsCompletedCode("Q19"));
            Assert.IsFalse(s.CampaignCompleted);

            // Q20 ("An Atlas for Everyone"): All 5 relays & >= 60% map coverage auto-backfill on accept;
            // drive to Frontier Observatory summit & return to garage.highlands to complete the campaign!
            Assert.IsTrue(s.AcceptMission("Q20").Success);
            Assert.IsTrue(s.Depart().Success);
            field.ResetForExpedition();
            StepAt(field, s, content.Sites["site.highlands.observatory"].Position, speedKph: 6f, dt: 0.25f);
            Assert.AreEqual(MissionState.ReadyToTurnIn, s.Missions.StateOf("Q20"));
            ArriveAtGarageWithController(field, s, "garage.highlands");

            Assert.IsTrue(s.Missions.IsCompletedCode("Q20"));
            Assert.AreEqual(20, s.Missions.CompletedCodes.Count(), "Every single mission Q01–Q20 must be completed!");
            Assert.IsTrue(s.CampaignCompleted, "Completing Q20 marks CampaignCompleted = true");
            Assert.Contains("cosmetic.atlas.medallion", s.UnlockedCosmetics());

            // Postgame continuation + independent Settings persistence + final Save/Continue round-trip!
            int revBeforeSettings = s.Revision;
            Assert.IsTrue(s.UpdateSettings(cfg =>
            {
                cfg.holdToInteract = false;
                cfg.uiScale = 1.35f;
                cfg.leftHandedLayout = true;
            }));
            Assert.AreEqual(revBeforeSettings, s.Revision, "Updating SettingsFile must never rewrite career save slot (§17.3)");

            var finalReload = CampaignSession.Continue(saves, content, new SaveSlotId(0));
            Assert.AreEqual(CampaignOpenStatus.LoadedCurrent, finalReload.Status);
            var postgame = finalReload.Session;
            Assert.IsTrue(postgame.CampaignCompleted);
            Assert.AreEqual(20, postgame.Missions.CompletedCodes.Count());
            Assert.AreEqual(4, postgame.OwnedVehicles.Count);
            Assert.AreEqual(5, postgame.Garage.UnlockedGarages.Count);
            Assert.Contains("cosmetic.relic.wetlands", postgame.EquippedCosmetics.ToList());
            Assert.IsFalse(postgame.Settings.holdToInteract);
            Assert.AreEqual(1.35f, postgame.Settings.uiScale, 1e-4f);

            // Verify free postgame exploration remains 100% playable after credits!
            Assert.IsTrue(postgame.Depart().Success);
            Assert.IsTrue(postgame.Expedition.IsActive);
        }

        [Test]
        public void DEF_029_ScreenFlowController_AndFinaleCreditsController_BehaveDeterministically()
        {
            var registry = new PauseTokenRegistry();
            var flow = new ScreenFlowController(registry);
            flow.Replace(ScreenFlowController.Screen.DrivingHud);
            Assert.IsFalse(registry.IsPaused);

            flow.PushModal(ScreenFlowController.Screen.PauseMenu, pauseSimulation: true);
            Assert.IsTrue(registry.IsPaused);
            Assert.AreEqual(ScreenFlowController.Screen.PauseMenu, flow.Current);

            flow.PushModal(ScreenFlowController.Screen.Settings, pauseSimulation: true);
            Assert.IsTrue(registry.IsPaused);
            Assert.AreEqual(ScreenFlowController.Screen.Settings, flow.Current);

            Assert.IsTrue(flow.TryCancel());
            Assert.AreEqual(ScreenFlowController.Screen.PauseMenu, flow.Current);
            Assert.IsTrue(registry.IsPaused, "Outer pause modal is still open — simulation must stay paused!");

            Assert.IsTrue(flow.TryCancel());
            Assert.AreEqual(ScreenFlowController.Screen.DrivingHud, flow.Current);
            Assert.IsFalse(registry.IsPaused);

            // FINISH-01: story completion -> atlas review -> credits -> free postgame continuation.
            bool atlasReviewed = false, creditsDone = false;
            var credits = new CreditsController();
            credits.OnAtlasReviewRequested += () => atlasReviewed = true;
            credits.OnCreditsFinished += () => creditsDone = true;
            credits.BeginFinaleSequence();
            Assert.IsTrue(atlasReviewed);
            Assert.IsTrue(credits.CreditsShown);
            credits.FinishCredits();
            Assert.IsTrue(creditsDone);
        }

        // ------------------------------- helpers driving real SliceFieldController frames -------------------------------

        static FieldInput MakeInput(CampaignSession s, WorldPoint p, float speedKph, float dt, bool interact) => new FieldInput
        {
            Pose = new Pose(p, 0f),
            SpeedKph = speedKph,
            DeltaSeconds = dt,
            FuelLiters = s.FuelLiters,
            EngineCondition = s.Damage.EngineCondition,
            SuspensionCondition = s.Damage.SuspensionCondition,
            TiresCondition = s.Damage.TiresCondition,
            BodyCondition = s.Damage.BodyCondition,
            Grounded = true,
            ClearanceMeters = 1f,
            InteractPressed = interact
        };

        static FieldFrame StepAt(SliceFieldController field, CampaignSession s, WorldPoint p, float speedKph, float dt, bool interact = false) =>
            field.Update(MakeInput(s, p, speedKph, dt, interact));

        static void ObserveLandmarkWithController(SliceFieldController field, CampaignSession s, WorldPoint p)
        {
            for (int i = 0; i < 15 && s.Expedition.IsActive; i++)
            {
                var frame = StepAt(field, s, p, speedKph: 0f, dt: 0.25f, interact: false);
                if (!string.IsNullOrEmpty(frame.DiscoveredLandmarkId)) return;
            }
            Assert.Fail($"Landmark at ({p.X},{p.Z}) did not trigger discovery within 3.75 s");
        }

        static void HoldInteractWithController(SliceFieldController field, CampaignSession s, WorldPoint p)
        {
            for (int i = 0; i < 16 && s.Expedition.IsActive; i++)
            {
                var frame = StepAt(field, s, p, speedKph: 0f, dt: 0.25f, interact: true);
                if (frame.InteractionCommitted)
                {
                    Assert.IsTrue(frame.InteractionResult.Success, $"Field commit failed at ({p.X},{p.Z}): {frame.InteractionResult.Detail}");
                    StepAt(field, s, p, speedKph: 0f, dt: 0.05f, interact: false);
                    return;
                }
            }
            Assert.Fail($"Hold interaction did not commit at ({p.X},{p.Z}); current prompt={field.Update(MakeInput(s, p, 0f, 0.01f, false)).Prompt.Label} ({field.Update(MakeInput(s, p, 0f, 0.01f, false)).Prompt.BlockedReason})");
        }

        static void ArriveAtGarageWithController(SliceFieldController field, CampaignSession s, string garageId)
        {
            // Ensure vehicle leaves any garage apron first so ArrivalTracker triggers cleanly.
            StepAt(field, s, new WorldPoint(2000f, 20f, 2000f), speedKph: 12f, dt: 0.25f);
            var target = garageId == s.Content.Slice.GarageId
                ? s.Content.Slice.ArrivalCenter
                : s.Content.GaragePosition(garageId);
            for (int i = 0; i < 12 && s.Expedition.IsActive; i++)
            {
                var frame = StepAt(field, s, target, speedKph: 0f, dt: 0.25f);
                if (frame.ArrivalTriggered)
                {
                    Assert.IsTrue(frame.Extraction.Success, $"Arrival extraction failed at {garageId}: {frame.Extraction.Result.Detail}");
                    return;
                }
            }
            Assert.Fail($"Vehicle did not extract on arrival at {garageId}");
        }

        [Test]
        public void KI_043_And_KI_047_ContractSocketsAndRelics_PlacedSweptAndCompletableInField()
        {
            var content = CampaignTestKit.Content;
            var saves = new SaveService(Path.Combine(_saveDir, "ki4347"));
            var s = CampaignSession.StartNew(saves, content, new SaveSlotId(0), GameMode.Expedition).Session;

            // KI-043: every authored one-time contract target has a placed world site in its matching biome
            int totalSockets = 0;
            foreach (var c in content.Contracts.Values)
            {
                if (!c.OneTime) continue;
                Assert.Greater(c.Targets.Count, 0);
                foreach (var tid in c.Targets)
                {
                    Assert.IsTrue(content.Sites.TryGetValue(tid, out var site), $"Missing contract site {tid} for {c.Id}");
                    Assert.AreEqual(c.Biome, site.Biome, $"Biome mismatch on {tid}");
                    totalSockets++;
                }
            }
            Assert.GreaterOrEqual(totalSockets, 60);

            // Accept C_F01 (3 triangulate sockets in Forest) and complete it strictly by driving to & interacting at its placed world sockets!
            Assert.IsTrue(s.AcceptContract("C_F01").Success);
            Assert.IsTrue(s.Depart().Success);
            var field = new SliceFieldController(s);
            field.ResetForExpedition();
            foreach (var tid in content.Contracts["C_F01"].Targets)
                HoldInteractWithController(field, s, content.Sites[tid].Position);
            Assert.Contains("C_F01", s.Contracts.CompletedIds.ToList(), "Interacting at all placed contract sockets must complete C_F01");

            // KI-047: all 25 relics have placed world coordinates, pulse on SensorScan while uncollected, and vanish from scan once collected!
            Assert.AreEqual(25, content.Relics.Count);
            var relic = content.Relics["relic.forest.01"];
            var scanBefore = s.SensorScan(relic.Position.X + 12f, relic.Position.Z);
            Assert.IsTrue(scanBefore.Any(r => r.Id == relic.Id && r.Directional),
                "Uncollected relic within 45 m must pulse directionally on SensorScan");

            HoldInteractWithController(field, s, relic.Position);
            Assert.IsTrue(s.Discovery.HasRelic(relic.Id), "Holding interact at relic world position must collect relic into protected ledger");

            var scanAfter = s.SensorScan(relic.Position.X + 12f, relic.Position.Z);
            Assert.IsFalse(scanAfter.Any(r => r.Id == relic.Id),
                "Collected relic must no longer emit an uncollected signal pulse");
        }
    }
}
