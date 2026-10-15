using System.Collections.Generic;
using NUnit.Framework;
using Wayfinder.Core;
using Wayfinder.Progression;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Regression tests for the domain defects found while composing the campaign session
    /// (KI-018 … KI-024). Each test pins one defect or the API that fixes it; reverting the fixes
    /// makes them fail (Docs/evidence/campaign-session-2026-10-15/mutation-check.log).
    /// </summary>
    public class ProgressionRegressionTests
    {
        static readonly InventoryId Cargo = new InventoryId("cargo");
        static readonly InventoryId Storage = new InventoryId("garageStorage");
        static readonly InventoryId CampStorage = new InventoryId("campStorage");

        static InventoryService NewInventory(int cargoSlots = 12, float cargoMass = 140f)
        {
            var inv = new InventoryService();
            inv.ConfigureContainer(Cargo, cargoSlots, cargoMass);
            inv.ConfigureContainer(Storage, 999, 99999f);
            inv.ConfigureContainer(CampStorage, 999, CampService.CampStorageCapacityKg);
            return inv;
        }

        static ItemStack Stack(string id, int quantity) => new ItemStack(new ContentId(id), quantity);

        // ---------------------------------------------------------------- KI-019 inventory

        [Test]
        public void Inventory_FailedRemoveDoesNotDestroyItems_KI019()
        {
            var inv = NewInventory();
            inv.Add(Cargo, Stack("scrap_metal", 3));
            var result = inv.Remove(Cargo, new ContentId("scrap_metal"), 5);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(3, inv.QuantityOf(Cargo, "scrap_metal"), "a failed remove must not consume anything");
        }

        [Test]
        public void Inventory_FailedTransferRestoresBothContainers_KI019()
        {
            var inv = NewInventory();
            inv.ConfigureContainer(CampStorage, 1, 500f);
            inv.Add(CampStorage, Stack("polymer", 10)); // camp store's only slot is full
            inv.Add(Cargo, Stack("scrap_metal", 4));

            var result = inv.Transfer(Cargo, CampStorage, Stack("scrap_metal", 4));
            Assert.IsFalse(result.Success);
            Assert.AreEqual(FailureReason.InsufficientSlots, result.Reason);
            Assert.AreEqual(4, inv.QuantityOf(Cargo, "scrap_metal"));
            Assert.AreEqual(0, inv.QuantityOf(CampStorage, "scrap_metal"));

            // Transferring more than exists fails without touching the source (old code removed 4 then failed).
            Assert.IsFalse(inv.Transfer(Cargo, Storage, Stack("scrap_metal", 9)).Success);
            Assert.AreEqual(4, inv.QuantityOf(Cargo, "scrap_metal"));
        }

        [Test]
        public void Inventory_AddAllIsAllOrNothing()
        {
            var inv = NewInventory(cargoSlots: 2);
            var stacks = new List<ItemStack> { Stack("salvage_bundle", 77), Stack("polymer", 2) }; // needs 3 slots
            var result = inv.AddAll(Cargo, stacks);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, inv.UsedSlots(Cargo), "partial placement must be rolled back");
            Assert.AreEqual(0, inv.QuantityOf(Cargo, "salvage_bundle"));
        }

        // ---------------------------------------------------------------- KI-020 collection

        [Test]
        public void Collect_GrantsAuthoredCacheContentsAtomically_KI020_QA3()
        {
            var economy = new EconomyService();
            var inv = NewInventory(cargoSlots: 3);
            var discovery = new DiscoveryService();
            var pickups = new PickupCatalog()
                .Add(new PickupContents("cache.forest.037", 234, new[] { ("scrap_metal", 1), ("electronics", 1) }));
            var tx = new TransactionService(economy, inv, new TransactionDependencies { Discovery = discovery, Pickups = pickups });

            // 234 units = 5 salvage stacks + 2 material stacks: does not fit 3 slots → typed failure, nothing consumed.
            var full = tx.Execute(new CollectPickup(new TransactionId("t1"), new ContentId("cache.forest.037"), Cargo));
            Assert.IsFalse(full.Success);
            Assert.AreEqual(FailureReason.InsufficientSlots, full.Reason);
            Assert.AreEqual(0, inv.UsedSlots(Cargo));
            Assert.IsFalse(discovery.IsCacheCollected("cache.forest.037"), "cache stays collectable after a capacity failure");
            Assert.IsFalse(tx.WasApplied(new TransactionId("t1")), "failed command is not recorded as applied");

            inv.ConfigureContainer(Cargo, 12, 140f);
            Assert.IsTrue(tx.Execute(new CollectPickup(new TransactionId("t2"), new ContentId("cache.forest.037"), Cargo)).Success);
            Assert.AreEqual(234, inv.QuantityOf(Cargo, "salvage_bundle"));
            Assert.AreEqual(1, inv.QuantityOf(Cargo, "scrap_metal"));
            Assert.AreEqual(1, inv.QuantityOf(Cargo, "electronics"));
            Assert.IsTrue(discovery.IsCacheCollected("cache.forest.037"));
        }

        [Test]
        public void Collect_UnknownPickupOrMissingCatalogIsTypedContentError()
        {
            var tx = new TransactionService(new EconomyService(), NewInventory());
            var noCatalog = tx.Execute(new CollectPickup(new TransactionId("a"), new ContentId("cache.forest.001"), Cargo));
            Assert.AreEqual(FailureReason.ContentMissing, noCatalog.Reason);

            var tx2 = new TransactionService(new EconomyService(), NewInventory(),
                new TransactionDependencies { Pickups = new PickupCatalog() });
            var unknown = tx2.Execute(new CollectPickup(new TransactionId("b"), new ContentId("cache.nowhere"), Cargo));
            Assert.AreEqual(FailureReason.ContentMissing, unknown.Reason);
        }

        // ---------------------------------------------------------------- KI-021 camps / banking

        [Test]
        public void Camp_SecondEstablishDoesNotConsumeAnotherKit_KI021()
        {
            var inv = NewInventory();
            var garage = new GarageService();
            var tx = new TransactionService(new EconomyService(), inv, new TransactionDependencies { Garage = garage });
            inv.Add(Cargo, Stack("camp_kit", 1));
            Assert.IsTrue(tx.Execute(new EstablishCamp(new TransactionId("c1"), new ContentId("camp.forest.a"), Cargo)).Success);

            inv.Add(Cargo, Stack("camp_kit", 1));
            var again = tx.Execute(new EstablishCamp(new TransactionId("c2"), new ContentId("camp.forest.a"), Cargo));
            Assert.AreEqual(FailureReason.AlreadyApplied, again.Reason);
            Assert.AreEqual(1, inv.QuantityOf(Cargo, "camp_kit"), "kit kept when the socket is already active");

            var unknown = tx.Execute(new EstablishCamp(new TransactionId("c3"), new ContentId("camp.moon.a"), Cargo));
            Assert.AreEqual(FailureReason.InvalidTarget, unknown.Reason);
            Assert.AreEqual(1, inv.QuantityOf(Cargo, "camp_kit"));
        }

        [Test]
        public void Bank_StoresMaterialsAtHubAndKeepsLoadout()
        {
            var economy = new EconomyService();
            var inv = NewInventory();
            var tx = new TransactionService(economy, inv);
            inv.Add(Cargo, Stack("salvage_bundle", 120));
            inv.Add(Cargo, Stack("polymer", 3));
            inv.Add(Cargo, Stack("fuel_can", 2));
            economy.Escrow(240);

            Assert.IsTrue(tx.Execute(new BankCargo(new TransactionId("b1"), new ContentId("garage.home"), Cargo, Storage)).Success);
            Assert.AreEqual(360, economy.BankedSalvage);
            Assert.AreEqual(0, inv.QuantityOf(Cargo, "salvage_bundle"));
            Assert.AreEqual(0, inv.QuantityOf(Cargo, "polymer"), "materials move to hub storage");
            Assert.AreEqual(3, inv.QuantityOf(Storage, "polymer"));
            Assert.AreEqual(2, inv.QuantityOf(Cargo, "fuel_can"), "consumables stay loaded");
        }

        [Test]
        public void CampDeposit_RespectsStorageCapacityAndLosesNothing_KI021()
        {
            var economy = new EconomyService();
            var inv = NewInventory(cargoSlots: 12, cargoMass: 999f);
            inv.ConfigureContainer(CampStorage, 999, 5f); // nearly full store: 5 kg
            inv.Add(Cargo, Stack("salvage_bundle", 30));
            inv.Add(Cargo, Stack("scrap_metal", 8));    // 8 kg
            inv.Add(Cargo, Stack("polymer", 2));        // 1 kg

            var outcome = new CampService().Deposit(inv, Cargo, CampStorage, economy);
            Assert.IsTrue(outcome.Result.Success);
            Assert.AreEqual(30, economy.BankedSalvage, "salvage always banks");
            int scrapTotal = inv.QuantityOf(Cargo, "scrap_metal") + inv.QuantityOf(CampStorage, "scrap_metal");
            int polymerTotal = inv.QuantityOf(Cargo, "polymer") + inv.QuantityOf(CampStorage, "polymer");
            Assert.AreEqual(8, scrapTotal, "no scrap lost");
            Assert.AreEqual(2, polymerTotal, "no polymer lost (old code only ever moved scrap)");
            Assert.LessOrEqual(inv.TotalMassKg(CampStorage), 5f + 1e-3f);
            Assert.AreEqual(5, inv.QuantityOf(CampStorage, "scrap_metal"), "largest quantity that fits is stored");
            Assert.AreEqual(outcome.MaterialUnitsLeftInCargo, inv.QuantityOf(Cargo, "scrap_metal") + inv.QuantityOf(Cargo, "polymer"));
        }

        // ---------------------------------------------------------------- KI-022 crafting fee

        [Test]
        public void Crafting_ChargesRecipeSalvageFee_KI022()
        {
            var economy = new EconomyService();
            var inv = NewInventory();
            inv.Add(Storage, Stack("scrap_metal", 4));
            inv.Add(Storage, Stack("polymer", 2));
            inv.Add(Storage, Stack("electronics", 1));
            var tx = new TransactionService(economy, inv);

            economy.Grant(79);
            var poor = tx.Execute(new CraftItem(new TransactionId("k1"), new ContentId("recipe.camp_kit"), Storage, Storage));
            Assert.AreEqual(FailureReason.InsufficientCurrency, poor.Reason);
            Assert.AreEqual(4, inv.QuantityOf(Storage, "scrap_metal"), "no materials consumed when the fee is unaffordable");

            economy.Grant(1);
            Assert.IsTrue(tx.Execute(new CraftItem(new TransactionId("k2"), new ContentId("recipe.camp_kit"), Storage, Storage)).Success);
            Assert.AreEqual(0, economy.BankedSalvage, "camp kit costs 80 Salvage + materials (§10.4)");
            Assert.AreEqual(1, inv.QuantityOf(Storage, "camp_kit"));
            Assert.AreEqual(0, inv.QuantityOf(Storage, "scrap_metal"));
        }

        // ---------------------------------------------------------------- KI-018 missions

        [Test]
        public void Missions_RestoreNeverRegressesCompletedMissions_KI018()
        {
            var missions = new MissionService();
            missions.Restore(new[] { "Q01" }, new[] { ("Q02", MissionState.Active, new[] { 1, 0 }) });
            Assert.AreEqual(MissionState.Completed, missions.StateOf("Q01"), "old code restored Q01 as Available");
            Assert.IsTrue(missions.IsCompletedCode("Q01"));
            Assert.IsFalse(missions.Accept("Q01").Success, "a completed mission cannot be accepted again");
            Assert.AreEqual(MissionState.Active, missions.StateOf("Q02"));
            Assert.AreEqual(MissionState.Locked, missions.StateOf("Q03"));
        }

        [Test]
        public void Missions_RestoreIgnoresUnknownCodesAndNormalisesCounters()
        {
            var missions = new MissionService();
            missions.Restore(new[] { "Q99" }, new[]
            {
                ("Q77", MissionState.Active, new[] { 1 }),
                ("Q01", MissionState.ReadyToTurnIn, new[] { 1, 0 }) // stale, too short and not complete
            });
            Assert.AreEqual(MissionState.Active, missions.StateOf("Q01"), "ReadyToTurnIn is re-derived from counters");
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, missions.ObjectiveCounters("Q01"));
            Assert.IsTrue(missions.ReportObjective("Q01", 2).Success, "normalised counters cannot overflow");
        }

        [Test]
        public void Missions_ReplayedTurnInStillUnlocksSuccessor_KI018()
        {
            var missions = new MissionService();
            var economy = new EconomyService();
            var ledger = new ClaimedRewardLedger();
            ledger.TryClaim("reward.story.Q01"); // reward already claimed before an interrupted save
            missions.Restore(null, new[] { ("Q01", MissionState.ReadyToTurnIn, new[] { 1, 1, 1 }) });
            var result = missions.TryTurnIn("Q01", new ContentId("garage.home"), economy, ledger);
            Assert.AreEqual(FailureReason.AlreadyApplied, result.Reason);
            Assert.AreEqual(0, economy.BankedSalvage, "never paid twice");
            Assert.AreEqual(MissionState.Completed, missions.StateOf("Q01"));
            Assert.AreEqual(MissionState.Available, missions.StateOf("Q02"), "successor unlocks on the replay path too");
        }

        [Test]
        public void Missions_ConditionRoutingAndAcceptReconciliation()
        {
            var missions = new MissionService();
            missions.Restore(new[] { "Q01", "Q02" }, null);
            // Q03 accepted after its camp already exists: objective pre-satisfied instead of dead-locked.
            Assert.IsTrue(missions.Accept("Q03", o => o.Type == MissionConditionType.EstablishCamp ? 1 : 0).Success);
            Assert.AreEqual(MissionState.ReadyToTurnIn, missions.StateOf("Q03"));

            var fresh = new MissionService();
            Assert.IsTrue(fresh.Accept("Q01").Success);
            Assert.AreEqual(0, fresh.ReportCondition(MissionConditionType.DiscoverLandmark, "landmark.forest.02"));
            Assert.AreEqual(1, fresh.ReportCondition(MissionConditionType.DiscoverLandmark, "landmark.forest.01"));
            Assert.AreEqual(0, fresh.ReportCondition(MissionConditionType.DiscoverLandmark, "landmark.forest.01"), "capped");
            fresh.ReportCondition(MissionConditionType.CollectQuestItem, "salvage_bundle");
            Assert.AreEqual(MissionState.Active, fresh.StateOf("Q01"));
            fresh.ReportCondition(MissionConditionType.ReturnToHub, "garage.home");
            Assert.AreEqual(MissionState.ReadyToTurnIn, fresh.StateOf("Q01"));
        }

        [Test]
        public void Missions_CheckpointsCountOnlyInOrder()
        {
            var missions = new MissionService();
            missions.Restore(new[] { "Q01" }, null);
            Assert.IsTrue(missions.Accept("Q02").Success);
            Assert.AreEqual(0, missions.ReportCheckpoint("route.forest.ford", 1), "skipping ahead does not count");
            Assert.AreEqual(1, missions.ReportCheckpoint("route.forest.ford", 0));
            Assert.AreEqual(0, missions.ReportCheckpoint("route.forest.ford", 0), "re-driving a gate does not count twice");
            Assert.AreEqual(1, missions.ReportCheckpoint("route.forest.ford", 1));
            CollectionAssert.AreEqual(new[] { 0, 2 }, missions.ObjectiveCounters("Q02"));
        }

        // ---------------------------------------------------------------- KI-023 hidden globals

        [Test]
        public void Research_TransactionUsesInjectedServiceNotGlobal_KI023()
        {
            var economy = new EconomyService();
            economy.Grant(1000);
            var completed = new HashSet<string> { "Q01" };
            var research = new ResearchService(completed.Contains);
            var tx = new TransactionService(economy, new InventoryService(), new TransactionDependencies { Research = research });

            Assert.IsTrue(tx.Execute(new ResearchUpgrade(new TransactionId("r1"),
                new ContentId("upgrade.scout.01.trailTires1"), new ContentId("vehicle.scout.s1"))).Success);
            Assert.IsTrue(research.IsResearched("upgrade.scout.01.trailTires1"));
            Assert.IsFalse(ResearchService.Global.IsResearched("upgrade.scout.01.trailTires1"), "global untouched");
            Assert.AreEqual(800, economy.BankedSalvage);

            var unconfigured = new TransactionService(economy, new InventoryService());
            var result = unconfigured.Execute(new ResearchUpgrade(new TransactionId("r2"),
                new ContentId("upgrade.scout.02.underbodyGuard"), new ContentId("vehicle.scout.s1")));
            Assert.AreEqual(FailureReason.InternalError, result.Reason, "no silent fallback to a static instance");
        }

        // ---------------------------------------------------------------- KI-024 day cycle

        [Test]
        public void SimulationTime_DayCycleIs48Minutes_KI024()
        {
            Assert.AreEqual(48f * 60f, SimulationTime.DayLengthSeconds, 1e-3f);
            var clock = new SimulationTime();
            clock.Restore(100d, SimulationTime.DayLengthSeconds + 30f);
            Assert.AreEqual(30f, clock.DayTimeSeconds, 1e-3f, "time of day wraps into one cycle");
            clock.Paused = true;
            clock.Advance(10f);
            Assert.AreEqual(100d, clock.ElapsedSimulationSeconds, 1e-9, "paused clock never advances (§16.2)");
            clock.Restore(double.NaN, float.NaN);
            Assert.AreEqual(0d, clock.ElapsedSimulationSeconds);
            Assert.AreEqual(0f, clock.DayTimeSeconds);
        }
    }
}
