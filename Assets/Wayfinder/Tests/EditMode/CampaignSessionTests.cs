using System;
using System.IO;
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
    /// Campaign composition (FLOW-01, SAVE-01, REC-01, CAMP-01): new game → depart → collect →
    /// discover → return → bank → research → save → reload, plus interruption at every
    /// persistence step (QA case 4), rescue rules (QA case 6), camp atomicity (QA case 10),
    /// autosave policy (§16.2) and slot isolation. Runs headless (no UnityEngine).
    /// </summary>
    public class CampaignSessionTests
    {
        string _root;

        [SetUp]
        public void SetUp() => _root = NewTempRoot();

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        [Test]
        public void NewGame_IsSavedBeforeMovementWithStartingLoadout()
        {
            var saves = new SaveService(_root);
            var s = NewGame(saves);
            Assert.AreEqual(1, s.Revision, "new game written as revision 1 before any movement (§17.5)");
            Assert.IsTrue(File.Exists(Path.Combine(_root, "slot-0", SaveService.CurrentFileName)));
            Assert.AreEqual(2, s.Inventory.QuantityOf(CampaignSession.Cargo, "fuel_can"));
            Assert.AreEqual(2, s.Inventory.QuantityOf(CampaignSession.Cargo, "repair_kit"));
            Assert.AreEqual(2, s.Inventory.QuantityOf(CampaignSession.Cargo, "tire_patch"));
            Assert.AreEqual(1, s.Inventory.QuantityOf(CampaignSession.Cargo, "recovery_boards"));
            Assert.AreEqual(0, s.Economy.BankedSalvage);
            Assert.AreEqual(24f, s.FuelLiters, 1e-4f);
            Assert.AreEqual(MissionState.Active, s.Missions.StateOf("Q01"), "onboarding mission starts accepted (D-022)");
            Assert.IsTrue(s.InGarage);

            var card = CampaignSession.PeekSlot(saves, new SaveSlotId(0));
            Assert.IsTrue(card.HasSave);
            Assert.AreEqual(1, card.Revision);
            Assert.IsFalse(card.ExpeditionActive);
            Assert.IsFalse(CampaignSession.PeekSlot(saves, new SaveSlotId(1)).HasSave);
        }

        [Test]
        public void Loop_DepartCollectDiscoverReturnBankResearch_SurvivesReload_FLOW01()
        {
            var s = NewGame(new SaveService(_root));
            Ok(s.Depart(), "depart");
            Assert.IsTrue(s.Expedition.IsActive);
            Assert.AreEqual("exp-0", s.Expedition.Id);

            Ok(s.CollectCache("cache.forest.001"), "collect");
            Assert.AreEqual(77, s.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle), "authored contents");
            Assert.AreEqual(0, s.Economy.BankedSalvage, "loose cargo is not banked yet (§10.3)");

            Ok(s.DiscoverLandmark("landmark.forest.01", 8f, 2.5f), "discover");
            int escrow = s.Economy.EscrowedSalvage;
            int mappingPaid = s.Discovery.MappingRewardsPaidThroughCount;
            Assert.AreEqual(DiscoveryService.LandmarkDiscoveryRewardSalvage + mappingPaid, escrow,
                "landmark reward + vista mapping thresholds are escrowed, not banked (§8.4)");
            Assert.Greater(mappingPaid, 0, "Ranger Rise is a vista: 384 m survey crosses mapping thresholds");
            Assert.AreEqual(MissionState.Active, s.Missions.StateOf("Q01"), "ReturnToHub still pending");

            var summary = Return(s);
            Assert.AreEqual(77, summary.SalvageUnitsBanked);
            Assert.AreEqual(escrow, summary.EscrowBanked);
            CollectionAssert.AreEqual(new[] { "Q01" }, summary.MissionsCompleted);
            Assert.AreEqual(150, summary.MissionRewardSalvage);
            Assert.AreEqual(77 + escrow + 150, s.Economy.BankedSalvage);
            Assert.AreEqual(0, s.Economy.EscrowedSalvage);
            Assert.AreEqual(0, s.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle));
            Assert.AreEqual(2, s.Inventory.QuantityOf(CampaignSession.Cargo, "fuel_can"), "loadout stays loaded");
            Assert.IsTrue(summary.Refueled && summary.Repaired);
            Assert.IsFalse(s.TutorialActive, "tutorial ends when Q01 completes");
            Assert.AreEqual(MissionState.Available, s.Missions.StateOf("Q02"));

            int before = s.Economy.BankedSalvage;
            Ok(s.ResearchUpgrade("upgrade.scout.01.trailTires1"), "research Trail Tires I");
            Assert.AreEqual(before - 200, s.Economy.BankedSalvage);
            string expected = Canonical(s);

            var reloaded = Reload(_root);
            Assert.AreEqual(expected, Canonical(reloaded), "reload restores exactly the saved campaign");
            Assert.AreEqual(MissionState.Completed, reloaded.Missions.StateOf("Q01"), "no mission regression after load");
            Assert.AreEqual(MissionState.Available, reloaded.Missions.StateOf("Q02"));
            Assert.IsTrue(reloaded.Research.IsResearched("upgrade.scout.01.trailTires1"));
            Assert.IsTrue(reloaded.Discovery.IsLandmarkDiscovered("landmark.forest.01"));
            Assert.IsTrue(reloaded.Discovery.IsCacheCollected("cache.forest.001"));
            Assert.AreEqual(FailureReason.AlreadyCollected, Reload(_root).PreviewCacheCollect("cache.forest.001").Reason);
        }

        [TestCase(SaveStep.Begin, false)]
        [TestCase(SaveStep.TempWritten, false)]
        [TestCase(SaveStep.TempVerified, false)]
        [TestCase(SaveStep.Backup2Rotated, false)]
        [TestCase(SaveStep.Backup1Rotated, false)]
        [TestCase(SaveStep.CurrentReplaced, true)]
        public void Banking_InterruptedAtEveryPersistenceStep_LeavesOldOrNewCompleteState_QA4(SaveStep crashAfter, bool expectNew)
        {
            var saves = new SaveService(_root);
            var s = NewGame(saves);
            Ok(s.Depart(), "depart");
            Ok(s.CollectCache("cache.forest.001"), "collect");
            Ok(s.DiscoverLandmark("landmark.forest.01", 8f, 2.5f), "discover");
            Assert.IsTrue(s.SaveNow().Success);
            int escrow = s.Economy.EscrowedSalvage;
            // A second save rotates backups so every rotation step below has real files to move.
            Assert.IsTrue(s.SaveNow().Success);

            saves.StepHook = step => { if (step == crashAfter) throw new SimulatedCrashException(step); };
            Assert.Throws<SimulatedCrashException>(() => s.EndExpedition(Home), "process dies mid-save");

            var after = Reload(_root);
            int wallet = after.Economy.BankedSalvage;
            int loose = after.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle);
            bool isOld = wallet == 0 && loose == 77 && after.Economy.EscrowedSalvage == escrow &&
                         after.Expedition.IsActive && after.Missions.StateOf("Q01") == MissionState.Active;
            bool isNew = wallet == 77 + escrow + 150 && loose == 0 && after.Economy.EscrowedSalvage == 0 &&
                         !after.Expedition.IsActive && after.Missions.StateOf("Q01") == MissionState.Completed;
            Assert.IsTrue(isOld ^ isNew, $"mixed state after crash at {crashAfter}: wallet={wallet} loose={loose}");
            Assert.AreEqual(expectNew, isNew, $"crash after {crashAfter}");

            if (isOld)
            {
                // Retrying after the reboot banks exactly once.
                Return(after);
                Assert.AreEqual(77 + escrow + 150, after.Economy.BankedSalvage);
                Assert.AreEqual(77 + escrow + 150, Reload(_root).Economy.BankedSalvage);
            }
        }

        [Test]
        public void Banking_SaveFailure_RollsBackAndNeverClaimsSuccess()
        {
            var saves = new SaveService(_root);
            var s = NewGame(saves);
            Ok(s.Depart(), "depart");
            Ok(s.CollectCache("cache.forest.001"), "collect");
            s.DrainNotices();

            saves.StepHook = step => { if (step == SaveStep.TempWritten) throw new IOException("disk full (simulated)"); };
            var failed = s.EndExpedition(Home);
            Assert.IsFalse(failed.Success);
            Assert.AreEqual(FailureReason.SaveIoFailure, failed.Result.Reason);
            Assert.AreEqual(77, s.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle), "cargo kept in memory");
            Assert.AreEqual(0, s.Economy.BankedSalvage, "nothing banked without a verified save");
            Assert.IsTrue(s.Expedition.IsActive);
            Assert.AreEqual(MissionState.Active, s.Missions.StateOf("Q01"));
            var notices = s.DrainNotices();
            Assert.IsTrue(notices.Any(n => n.Kind == CampaignNoticeKind.SaveFailed));
            Assert.IsFalse(notices.Any(n => n.Kind == CampaignNoticeKind.Banked), "rolled-back work is never announced");
            Assert.IsTrue(s.LastSaveFailed);

            saves.StepHook = null;
            var ok = s.EndExpedition(Home);
            Assert.IsTrue(ok.Success);
            Assert.AreEqual(77, s.Economy.BankedSalvage, "paid exactly once after the retry");
            Assert.AreEqual(77, Reload(_root).Economy.BankedSalvage);
        }

        [Test]
        public void TransactionSequence_SurvivesReload_NoSilentIdCollision()
        {
            var s = NewGame(new SaveService(_root));
            Ok(s.Depart(), "depart");
            Ok(s.CollectCache("cache.forest.001"), "collect 001");
            Assert.IsTrue(s.SaveNow().Success);

            // Two pickups after the reload: with a restarted counter the first would get the free id
            // "exp-0:0" but the second would reuse the applied "exp-0:1" and silently grant nothing.
            var reloaded = Reload(_root);
            Ok(reloaded.CollectCache("cache.forest.002"), "collect 002 after reload");
            Ok(reloaded.CollectCache("cache.forest.007"), "collect 007 after reload");
            Assert.AreEqual(77 + 91 + 64, reloaded.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle),
                "a reused transaction id would have turned a pickup into a silent no-op");
            Assert.IsTrue(reloaded.SaveNow().Success);

            // Older payloads without the counter: the next id is derived from the applied ids.
            var payload = reloaded.CaptureSnapshot();
            Assert.Greater(payload.nextTransactionSequence, 3);
            payload.nextTransactionSequence = 0;
            Assert.IsTrue(new SaveService(_root).Save(new SaveSlotId(0), payload, reloaded.Revision + 1).Success);
            var legacy = Reload(_root);
            Ok(legacy.CollectCache("cache.forest.009"), "collect 009 with derived sequence");
            Ok(legacy.CollectCache("cache.forest.010"), "collect 010 with derived sequence");
            Assert.AreEqual(77 + 91 + 64 + 67 + 68, legacy.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle));
        }

        [Test]
        public void FullRescue_LosesQuarterOfLooseSalvageOnly_QA6()
        {
            var s = NewGame(new SaveService(_root));
            PlayQ01(s);
            int wallet = s.Economy.BankedSalvage;
            Ok(s.AcceptMission("Q02"), "accept Q02");
            Ok(s.Depart(), "depart");
            Ok(s.CollectCache("cache.forest.002"), "collect 91");
            Ok(s.CollectCache("cache.forest.007"), "collect 64");
            Ok(s.CollectCache("cache.forest.021"), "collect 142 + 2 polymer");
            Ok(s.DiscoverLandmark("landmark.forest.02", 5f, 3f), "discover");
            Ok(s.InspectObject("landmark.forest.04"), "Q02 progress");
            int escrow = s.Economy.EscrowedSalvage;
            s.UpdateVehicle(new Pose(900f, 1f, 2200f, 90f), 3f, 40, 55, 60, 70, 1f);

            var summary = s.FullRescue();
            Assert.IsTrue(summary.Success, summary.Result.ToString());
            Assert.IsTrue(summary.WasRescue);
            int loose = 91 + 64 + 142;
            Assert.AreEqual(loose / 4, summary.RescueSalvageLost, "25% of loose salvage_bundle units, rounded down");
            Assert.AreEqual(wallet + loose - loose / 4 + escrow, s.Economy.BankedSalvage, "escrowed rewards remain payable");
            Assert.AreEqual(2, s.Inventory.QuantityOf(CampaignSession.GarageStorage, "polymer"), "materials never lost");
            Assert.AreEqual(2, s.Inventory.QuantityOf(CampaignSession.Cargo, "fuel_can"), "consumables preserved");
            Assert.IsTrue(s.Discovery.IsLandmarkDiscovered("landmark.forest.02"), "discoveries preserved");
            Assert.AreEqual(MissionState.Active, s.Missions.StateOf("Q02"), "accepted objectives preserved");
            CollectionAssert.AreEqual(new[] { 1, 0 }, s.Missions.ObjectiveCounters("Q02"), "objective progress preserved");
            Assert.AreEqual(s.FuelCapacityLiters, s.FuelLiters, 1e-4f, "refuelled at the garage");
            Assert.AreEqual(100, s.Damage.EngineCondition, "repaired at the garage");
            Assert.IsFalse(s.Expedition.IsActive);
        }

        [Test]
        public void FullRescue_IsFreeDuringTutorialAndInRelaxedMode()
        {
            var tutorial = NewGame(new SaveService(_root));
            Ok(tutorial.Depart(), "depart");
            Ok(tutorial.CollectCache("cache.forest.001"), "collect");
            var free = tutorial.FullRescue();
            Assert.IsTrue(free.Success);
            Assert.AreEqual(0, free.RescueSalvageLost, "a stuck new player recovers for free (§5.6 tutorial)");
            Assert.AreEqual(77, tutorial.Economy.BankedSalvage);

            var relaxed = NewGame(new SaveService(_root), slot: 1, mode: GameMode.RelaxedExploration);
            PlayQ01(relaxed);
            Ok(relaxed.Depart(), "depart");
            Ok(relaxed.CollectCache("cache.forest.002"), "collect");
            Assert.AreEqual(0, relaxed.FullRescue().RescueSalvageLost, "Relaxed Exploration: no reward punishment");
        }

        [Test]
        public void ZeroResourcePlayer_CanAlwaysReachSafetyAndDepartAgain_QA6()
        {
            var s = NewGame(new SaveService(_root));
            PlayQ01(s);
            foreach (var (item, qty) in CampaignSession.StartingLoadout) Ok(s.UnloadCargo(item, qty), "unload " + item);
            Assert.AreEqual(0, s.Inventory.UsedSlots(CampaignSession.Cargo), "no supplies carried");
            Ok(s.Depart(), "depart with no supplies");
            s.UpdateVehicle(new Pose(700f, 1f, 2000f, 0f), 0f, 0, 0, 0, 0, 5f); // empty tank, wrecked
            var rescue = s.FullRescue();
            Assert.IsTrue(rescue.Success, "rescue needs no currency or supplies (§5.6)");
            Assert.AreEqual(s.FuelCapacityLiters, s.FuelLiters, 1e-4f);
            Assert.AreEqual(100, s.Damage.EngineCondition);
            Ok(s.Depart(), "playable again");
        }

        [Test]
        public void CampEstablishment_IsAtomicWithItsSaveAndPersisted_QA10()
        {
            var saves = new SaveService(_root);
            var s = NewGame(saves);
            PlayQ01(s);
            PlayQ02(s);
            Ok(s.AcceptMission("Q03"), "accept Q03");
            Assert.AreEqual(1, s.Inventory.QuantityOf(CampaignSession.GarageStorage, "camp_kit"), "Q03 grants a kit to storage");
            Ok(s.LoadCargo("camp_kit", 1), "load kit");
            Ok(s.Depart(), "depart");

            saves.StepHook = step => { if (step == SaveStep.TempVerified) throw new IOException("simulated write failure"); };
            var failed = s.EstablishCamp("camp.forest.a");
            Assert.AreEqual(FailureReason.SaveIoFailure, failed.Reason);
            Assert.AreEqual(1, s.Inventory.QuantityOf(CampaignSession.Cargo, "camp_kit"), "kit kept when the commit fails");
            Assert.IsFalse(s.Garage.IsCampActivated("camp.forest.a"));
            Assert.AreEqual(MissionState.Active, s.Missions.StateOf("Q03"));

            saves.StepHook = null;
            Ok(s.EstablishCamp("camp.forest.a"), "establish");
            Assert.AreEqual(0, s.Inventory.QuantityOf(CampaignSession.Cargo, "camp_kit"));
            Assert.IsTrue(s.Garage.IsCampActivated("camp.forest.a"));
            Assert.AreEqual(MissionState.ReadyToTurnIn, s.Missions.StateOf("Q03"));

            var reloaded = Reload(_root);
            Assert.IsTrue(reloaded.Garage.IsCampActivated("camp.forest.a"), "establishment was saved immediately");
            Assert.AreEqual(0, reloaded.Inventory.QuantityOf(CampaignSession.Cargo, "camp_kit"));
            Assert.AreEqual(FailureReason.AlreadyApplied, reloaded.EstablishCamp("camp.forest.a").Reason);
        }

        [Test]
        public void CampDeposit_BanksAndStoresMaterialsMidExpedition()
        {
            var s = NewGame(new SaveService(_root));
            PlayQ01(s);
            PlayQ02(s);
            Ok(s.AcceptMission("Q03"), "accept");
            Ok(s.LoadCargo("camp_kit", 1), "load");
            Ok(s.Depart(), "depart");
            Ok(s.EstablishCamp("camp.forest.a"), "establish");
            Ok(s.CollectCache("cache.forest.037"), "remote cache 234 + scrap + electronics");
            int wallet = s.Economy.BankedSalvage;
            Ok(s.DepositAtCamp("camp.forest.a"), "deposit");
            Assert.AreEqual(wallet + 234, s.Economy.BankedSalvage, "camp deposit banks loose Salvage (§12.4)");
            Assert.AreEqual(1, s.Inventory.QuantityOf(CampaignSession.CampStorage, "scrap_metal"));
            Assert.AreEqual(1, s.Inventory.QuantityOf(CampaignSession.CampStorage, "electronics"));
            Assert.IsTrue(s.Expedition.IsActive, "the expedition continues after a camp deposit");
            Assert.AreEqual(s.Economy.BankedSalvage, Reload(_root).Economy.BankedSalvage, "deposit saved immediately");
        }

        [Test]
        public void Autosave_DebouncesFieldProgress_AndSavesOnSuspend()
        {
            var s = NewGame(new SaveService(_root));
            Ok(s.Depart(), "depart");
            Assert.AreEqual(2, s.Revision, "departure is a critical save");
            Ok(s.CollectCache("cache.forest.001"), "collect");
            Assert.IsTrue(s.HasUnsavedChanges);
            Assert.AreEqual(AutosaveTickResult.Idle, s.Tick(5f));
            Assert.AreEqual(2, s.Revision, "pickups are debounced, not saved per event");
            Assert.AreEqual(AutosaveTickResult.Idle, s.Tick(9f));
            Assert.AreEqual(AutosaveTickResult.Saved, s.Tick(1.5f), "saved once the 15 s window elapsed");
            Assert.AreEqual(3, s.Revision);
            Assert.IsNull(s.OnSuspend(), "nothing unsaved → no redundant write on suspend");

            s.UpdateVehicle(new Pose(1030f, 1f, 2250f, 180f), 23f, 100, 100, 100, 100, 2f);
            Assert.IsTrue(s.HasUnsavedChanges);
            var suspended = s.OnSuspend();
            Assert.IsTrue(suspended.HasValue && suspended.Value.Success, "suspend saves immediately (§16.2)");
            Assert.AreEqual(1030f, Reload(_root).Expedition.Pose.Position.X, 1e-3f);
        }

        [Test]
        public void Autosave_FailureKeepsStateDirtyAndRetries()
        {
            var saves = new SaveService(_root);
            var s = NewGame(saves);
            Ok(s.Depart(), "depart");
            Ok(s.CollectCache("cache.forest.001"), "collect");
            saves.StepHook = step => { if (step == SaveStep.Begin) throw new IOException("storage unavailable"); };
            Assert.AreEqual(AutosaveTickResult.Failed, s.Tick(16f));
            Assert.IsTrue(s.HasUnsavedChanges && s.LastSaveFailed);
            Assert.AreEqual(77, s.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle), "progress kept in memory");
            saves.StepHook = null;
            Assert.AreEqual(AutosaveTickResult.Idle, s.Tick(10f), "retry waits 20 s");
            Assert.AreEqual(AutosaveTickResult.Saved, s.Tick(10.5f));
            Assert.IsFalse(s.LastSaveFailed);
        }

        [Test]
        public void Continue_MidExpedition_ResumesAtLastSafePose()
        {
            var s = NewGame(new SaveService(_root));
            Ok(s.Depart(), "depart");
            s.UpdateVehicle(new Pose(980f, 2f, 2200f, 45f), 20f, 90, 100, 100, 100, 3f);
            s.RecordSafePose(new Pose(990f, 1f, 2210f, 40f));
            s.UpdateVehicle(new Pose(985f, 4f, 2195f, 50f), 19.5f, 90, 100, 100, 100, 1f); // e.g. mid-air when suspended
            Assert.IsTrue(s.OnSuspend().Value.Success);

            var resumed = Reload(_root);
            Assert.IsTrue(resumed.Expedition.IsActive, "expedition continues after a crash or kill (§17.4)");
            Assert.AreEqual(990f, resumed.ResumePose.Position.X, 1e-3f, "resume at the last validated safe pose");
            Assert.AreEqual(2210f, resumed.ResumePose.Position.Z, 1e-3f);
            Assert.AreEqual(19.5f, resumed.FuelLiters, 1e-3f);
            Assert.AreEqual(90, resumed.Damage.EngineCondition);
        }

        [Test]
        public void Continue_FromCorruptCurrent_LoadsNewestValidBackup()
        {
            var s = NewGame(new SaveService(_root));
            Ok(s.Depart(), "depart");                          // revision 2
            Ok(s.CollectCache("cache.forest.001"), "collect");
            Assert.IsTrue(s.SaveNow().Success);                 // revision 3
            File.WriteAllText(Path.Combine(_root, "slot-0", SaveService.CurrentFileName), "{ \"truncated\": ");

            var result = CampaignSession.Continue(new SaveService(_root), Content, new SaveSlotId(0));
            Assert.AreEqual(CampaignOpenStatus.LoadedBackup, result.Status);
            Assert.AreEqual(2, result.Session.Revision);
            Assert.IsTrue(result.Session.Expedition.IsActive);
            Assert.AreEqual(0, result.Session.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle),
                "the backup predates the pickup — a consistent older state, never a mixed one");
        }

        [Test]
        public void NewGame_InOccupiedSlot_ReplacesTheOldRevisionHistory()
        {
            var saves = new SaveService(_root);
            var old = NewGame(saves);
            PlayQ01(old);
            Assert.Greater(old.Revision, 2);

            var fresh = NewGame(saves);
            Assert.AreEqual(1, fresh.Revision);
            var loaded = Reload(_root);
            Assert.AreEqual(0, loaded.Economy.BankedSalvage, "old backups with higher revisions must not outrank the new game");
            Assert.AreEqual(MissionState.Active, loaded.Missions.StateOf("Q01"));
        }

        [Test]
        public void SaveSlots_AreFullyIsolated_KI023()
        {
            var saves = new SaveService(_root);
            var a = NewGame(saves, slot: 0);
            var b = NewGame(saves, slot: 2);
            PlayQ01(a);
            Assert.AreEqual(MissionState.Completed, a.Missions.StateOf("Q01"));
            Assert.AreEqual(MissionState.Active, b.Missions.StateOf("Q01"), "no shared static mission state");
            Assert.AreEqual(0, b.Economy.BankedSalvage);
            Assert.IsFalse(b.Discovery.IsCacheCollected("cache.forest.001"));
            Assert.IsFalse(b.Research.IsResearched("upgrade.scout.01.trailTires1"));
            Assert.AreEqual(MissionState.Active, Reload(_root, 2).Missions.StateOf("Q01"));
            Assert.AreEqual(MissionState.Completed, Reload(_root, 0).Missions.StateOf("Q01"));
        }

        [Test]
        public void Pickup_WithFullCargo_IsTypedFailureAndCacheRemains_QA3()
        {
            var s = NewGame(new SaveService(_root));
            Ok(s.Depart(), "depart");
            Ok(s.CollectCache("cache.forest.037"), "remote 234 + 2 materials");   // 4 supply + 5 salvage + 2 = 11 slots
            var preview = s.PreviewCacheCollect("cache.forest.039");
            Assert.AreEqual(FailureReason.InsufficientSlots, preview.Reason, "HUD can warn before the player commits");
            var result = s.CollectCache("cache.forest.039");
            Assert.AreEqual(FailureReason.InsufficientSlots, result.Reason);
            Assert.IsFalse(s.Discovery.IsCacheCollected("cache.forest.039"), "cache not consumed");
            Assert.AreEqual(234, s.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle));
            Return(s);
            Ok(s.Depart(), "depart again");
            Ok(s.CollectCache("cache.forest.039"), "collectable after banking");
        }

        [Test]
        public void Missions_CannotBeAcceptedOrResearchBoughtInTheField()
        {
            var s = NewGame(new SaveService(_root));
            PlayQ01(s);
            Ok(s.Depart(), "depart");
            Assert.AreEqual(FailureReason.WrongState, s.AcceptMission("Q02").Reason);
            Assert.AreEqual(FailureReason.WrongState, s.ResearchUpgrade("upgrade.scout.01.trailTires1").Reason,
                "no research at camps or in the field (§12.2)");
            Assert.AreEqual(FailureReason.WrongState, s.LoadCargo("fuel_can", 1).Reason);
            Assert.AreEqual(FailureReason.ContentMissing, s.CollectCache("cache.forest.999").Reason,
                "unknown caches are typed content errors, never exceptions");
        }

        [Test]
        public void VerticalSlice_PlaythroughQ01toQ04_AffordsTiers1to6AndSecondCamp_LedgerBalances()
        {
            var s = NewGame(new SaveService(_root));
            var slice = Content.Slice;
            PlayQ01(s);
            Ok(s.ResearchUpgrade("upgrade.scout.01.trailTires1"), "tier 1");
            PlayQ02(s);
            PlayQ03(s);
            PlayQ04(s);

            // Collect every slice cache, banking whenever the Scout is full (cargo limits, §10.4).
            Ok(s.Depart(), "depart");
            foreach (var cacheId in slice.CacheIds)
            {
                if (s.Discovery.IsCacheCollected(cacheId)) continue;
                var cache = Content.Caches[cacheId];
                s.RevealAround(cache.Position.X, cache.Position.Z, vista: false); // drove there
                var r = s.CollectCache(cacheId);
                if (!r.Success)
                {
                    Assert.IsTrue(r.Reason == FailureReason.InsufficientSlots || r.Reason == FailureReason.InsufficientMassCapacity, r.ToString());
                    Return(s);
                    Ok(s.Depart(), "depart");
                    Ok(s.CollectCache(cacheId), "retry " + cacheId);
                }
            }
            foreach (var l in slice.Landmarks)
                if (!s.Discovery.IsLandmarkDiscovered(l.Id)) Ok(s.DiscoverLandmark(l.Id, 6f, 2.2f), "discover " + l.Id);
            Return(s);

            foreach (var tier in s.Research.Tiers.Take(slice.ResearchTierCount))
                if (!s.Research.IsResearched(tier.Id.Value)) Ok(s.ResearchUpgrade(tier.Id.Value), "research tier " + tier.Tier);

            // Second camp from collected materials: 4 scrap + 2 polymer + 1 electronics + 80 Salvage (§10.4).
            Ok(s.Craft("recipe.camp_kit"), "craft camp kit");
            Ok(s.LoadCargo("camp_kit", 1), "load kit");
            Ok(s.Depart(), "depart");
            Ok(s.EstablishCamp("camp.forest.b"), "second camp");
            Return(s);

            foreach (var code in slice.MissionCodes) Assert.IsTrue(s.Missions.IsCompletedCode(code), code);
            Assert.IsTrue(s.Garage.IsCampActivated("camp.forest.a") && s.Garage.IsCampActivated("camp.forest.b"));
            Assert.IsTrue(s.Garage.HasInfrastructure("relay.forest"));
            Assert.AreEqual(14, s.ResolveStats().CargoSlots, "Cargo Organizer applied");
            Assert.AreEqual(30f, s.FuelCapacityLiters, 1e-4f, "Tank I applied and refuelled");

            int cacheIncome = slice.CacheIds.Sum(id => Content.Caches[id].SalvageUnits);
            int landmarkIncome = slice.Landmarks.Count * DiscoveryService.LandmarkDiscoveryRewardSalvage;
            int missionIncome = slice.MissionCodes.Sum(code => s.Missions.Definitions[code].RewardSalvage);
            int mappingIncome = s.Discovery.MappingRewardsPaidThroughCount; // 50 Salvage per 50 cells
            int researchSpend = s.Research.Tiers.Take(slice.ResearchTierCount).Sum(t => t.CostSalvage);
            Assert.AreEqual(3050, researchSpend);
            Assert.AreEqual(cacheIncome + landmarkIncome + missionIncome + mappingIncome - researchSpend - 80,
                s.Economy.BankedSalvage, "every Salvage accounted for: no leak, no double payment");
            Assert.GreaterOrEqual(cacheIncome + landmarkIncome + missionIncome - researchSpend - 80, 0,
                "slice is affordable even with zero mapping income (no economy dead-end)");

            string expected = Canonical(s);
            Assert.AreEqual(expected, Canonical(Reload(_root)), "the finished slice reloads identically");
        }
    }
}
