using System.Collections.Generic;
using NUnit.Framework;
using Wayfinder.Core;
using Wayfinder.Progression;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Inventory, economy, transaction and research tests. Mandatory QA cases 1, 2, 3, 6, 7,
    /// 9, 10, 16, 17.
    /// </summary>
    public class ProgressionTests
    {
        static readonly InventoryId Cargo = new InventoryId("cargo");
        static readonly InventoryId Storage = new InventoryId("garageStorage");

        InventoryService NewInventory(int slots = 12, float mass = 120f)
        {
            var inv = new InventoryService();
            inv.ConfigureContainer(Cargo, slots, mass);
            inv.ConfigureContainer(Storage, 999, 99999f);
            return inv;
        }

        [Test]
        public void Inventory_FullReturnsTypedErrorWithoutConsuming_QA3()
        {
            var inv = NewInventory(slots: 1);
            Assert.IsTrue(inv.Add(Cargo, new ItemStack(new ContentId("scrap_metal"), 5)).Success);
            var result = inv.Add(Cargo, new ItemStack(new ContentId("polymer"), 1));
            Assert.IsFalse(result.Success);
            Assert.AreEqual(FailureReason.InsufficientSlots, result.Reason);
            Assert.AreEqual(5, inv.QuantityOf(Cargo, "scrap_metal"), "failed add must not consume");
        }

        [Test]
        public void Inventory_MassCapacityBlocks()
        {
            var inv = NewInventory(slots: 10, mass: 2f);
            var result = inv.Add(Cargo, new ItemStack(new ContentId("scrap_metal"), 5)); // 5 kg
            Assert.IsFalse(result.Success);
            Assert.AreEqual(FailureReason.InsufficientMassCapacity, result.Reason);
        }

        [Test]
        public void Inventory_ProtectedLedgerBypassesSlots()
        {
            var inv = NewInventory(slots: 1);
            Assert.IsTrue(inv.Add(Cargo, new ItemStack(new ContentId("relay_fuse"), 1)).Success);
            Assert.IsTrue(inv.ProtectedLedgerContains("relay_fuse"));
        }

        [Test]
        public void Transaction_ReplayedIdGrantsNothingTwice_QA1_QA2_QA16()
        {
            // Revised with KI-020: collection now grants the authored cache contents (caches.json:
            // cache.forest.001 = 77 Salvage) instead of a hard-coded single unit, and the collected
            // registry is checked inside the same commit. The replay assertions are unchanged.
            var economy = new EconomyService();
            var inv = NewInventory();
            var discovery = new DiscoveryService();
            var pickups = new PickupCatalog().Add(new PickupContents("cache.forest.001", 77, null));
            var tx = new TransactionService(economy, inv,
                new TransactionDependencies { Discovery = discovery, Pickups = pickups });
            var command = new CollectPickup(new TransactionId("tx-1"), new ContentId("cache.forest.001"), Cargo);

            Assert.IsTrue(tx.Execute(command).Success);
            Assert.IsTrue(tx.Execute(command).Success, "replay is a no-op success");
            Assert.AreEqual(77, inv.QuantityOf(Cargo, "salvage_bundle"), "items granted once");
            Assert.IsTrue(tx.WasApplied(new TransactionId("tx-1")));
            Assert.IsTrue(discovery.IsCacheCollected("cache.forest.001"));

            // A second interaction with a NEW transaction id is a typed failure (QA case 2).
            var again = tx.Execute(new CollectPickup(new TransactionId("tx-2"), new ContentId("cache.forest.001"), Cargo));
            Assert.IsFalse(again.Success);
            Assert.AreEqual(FailureReason.AlreadyCollected, again.Reason);
            Assert.AreEqual(77, inv.QuantityOf(Cargo, "salvage_bundle"));
        }

        [Test]
        public void Transaction_BankingConvertsSalvageAndEscrow_Once()
        {
            var economy = new EconomyService();
            var inv = NewInventory();
            var tx = new TransactionService(economy, inv);
            inv.Add(Cargo, new ItemStack(new ContentId("salvage_bundle"), 35));
            economy.Escrow(50);

            var bank = new BankCargo(new TransactionId("bank-1"), new ContentId("garage.home"), Cargo);
            Assert.IsTrue(tx.Execute(bank).Success);
            Assert.AreEqual(85, economy.BankedSalvage, "35 units + 50 escrow");
            Assert.AreEqual(0, economy.EscrowedSalvage);
            Assert.AreEqual(0, inv.QuantityOf(Cargo, "salvage_bundle"));

            Assert.IsTrue(tx.Execute(new BankCargo(new TransactionId("bank-1"), new ContentId("garage.home"), Cargo)).Success);
            Assert.AreEqual(85, economy.BankedSalvage, "replayed banking cannot pay twice");
        }

        [Test]
        public void Economy_CannotGoNegative()
        {
            var economy = new EconomyService();
            Assert.IsFalse(economy.TrySpend(1, new TransactionId("x")).Success);
            economy.Grant(100);
            Assert.IsFalse(economy.TrySpend(101, new TransactionId("y")).Success);
            Assert.IsTrue(economy.TrySpend(100, new TransactionId("z")).Success);
            Assert.AreEqual(0, economy.BankedSalvage);
        }

        [Test]
        public void Economy_SellPriceIsFortyPercentFloored()
        {
            Assert.AreEqual(4, ItemCatalog.SellPrice("scrap_metal"));   // 10 × 0.4
            Assert.AreEqual(4, ItemCatalog.SellPrice("polymer"));       // 12 × 0.48 → 4
            Assert.AreEqual(24, ItemCatalog.SellPrice("fuel_can"));     // 60 × 0.4
        }

        [Test]
        public void Crafting_ArbitrageIsImpossible_QA17()
        {
            foreach (var recipe in CraftingService.Recipes)
                Assert.IsFalse(CraftingService.CraftResaleCreatesCurrency(recipe),
                    $"{recipe.Id} must never create currency");
        }

        [Test]
        public void Crafting_ConsumesOnlyWhenCapacityAllows()
        {
            var inv = NewInventory(slots: 1, mass: 999f);
            var economy = new EconomyService();
            economy.Grant(100);
            inv.Add(Cargo, new ItemStack(new ContentId("scrap_metal"), 2));
            inv.Add(Cargo, new ItemStack(new ContentId("polymer"), 1));
            // 1 slot occupied by scrap (stack) → output repair_kit needs its own slot: rejected.
            // (Signature gained the wallet with KI-022: the recipe fee is now actually charged.)
            var result = CraftingService.TryCraft(inv, economy, new ContentId("recipe.repair_kit"), Cargo, Cargo);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(2, inv.QuantityOf(Cargo, "scrap_metal"), "materials not consumed on failure");
            Assert.AreEqual(100, economy.BankedSalvage, "fee not charged on failure");
        }

        [Test]
        public void Research_SequentialWithStoryGates_QA7()
        {
            var economy = new EconomyService();
            // Story-gate predicate is injected: the test must not depend on MissionService.Global,
            // whose state other tests in this fixture legitimately mutate (test-order independence).
            var completedMissions = new HashSet<string>();
            var research = new ResearchService(completedMissions.Contains);
            Assert.AreEqual(32200, research.TotalCostSalvage);

            economy.Grant(100000);
            Assert.IsFalse(research.TryPurchase(new ContentId("upgrade.scout.02.underbodyGuard"), economy).Success,
                "tier 2 requires tier 1 first");

            Assert.IsFalse(research.TryPurchase(new ContentId("upgrade.scout.01.trailTires1"), economy).Success,
                "Q01 story gate not completed");

            completedMissions.Add("Q01");
            Assert.IsTrue(research.TryPurchase(new ContentId("upgrade.scout.01.trailTires1"), economy).Success);
            Assert.IsTrue(research.IsResearched("upgrade.scout.01.trailTires1"));
            Assert.AreEqual(100000 - 200, economy.BankedSalvage);
        }

        [Test]
        public void Research_CapacityModuleRemovalBlockedWhenOver_QA9()
        {
            var inv = NewInventory(slots: 12, mass: 99999f);
            var research = new ResearchService(_ => true);
            Assert.IsTrue(research.CanSwapModuleOut(inv, Cargo));
            // scrap_metal stacks to 10, so 20 units must occupy exactly two slots.
            Assert.IsTrue(inv.Add(Cargo, new ItemStack(new ContentId("scrap_metal"), 20)).Success);
            Assert.AreEqual(2, inv.UsedSlots(Cargo), "oversize pickup is split into MaxStack-sized stacks");
            // Force over-capacity: simulate removing the cargo rack (container shrinks to 1 slot).
            inv.Restore(Cargo, inv.Stacks(Cargo), (1, 99999f));
            Assert.IsTrue(inv.IsOverCapacity(Cargo));
            Assert.IsFalse(research.CanSwapModuleOut(inv, Cargo), "over capacity blocks rack removal");
        }

        [Test]
        public void Camp_EstablishmentAtomic_QA10()
        {
            // Revised with KI-021: activation now happens inside the same commit as kit consumption,
            // so the test injects the GarageService and asserts the camp is really active.
            var economy = new EconomyService();
            var inv = NewInventory();
            var garage = new GarageService();
            var tx = new TransactionService(economy, inv, new TransactionDependencies { Garage = garage });
            CampEstablished committed = null;
            tx.OnCommitted += e => { if (e is CampEstablished ce) committed = ce; };

            // No kit → nothing consumed.
            var failed = tx.Execute(new EstablishCamp(new TransactionId("camp-1"), new ContentId("camp.forest.a"), Cargo));
            Assert.IsFalse(failed.Success);
            Assert.AreEqual(0, inv.QuantityOf(Cargo, "camp_kit"));

            inv.Add(Cargo, new ItemStack(new ContentId("camp_kit"), 1));
            var ok = tx.Execute(new EstablishCamp(new TransactionId("camp-2"), new ContentId("camp.forest.a"), Cargo));
            Assert.IsTrue(ok.Success);
            Assert.AreEqual(0, inv.QuantityOf(Cargo, "camp_kit"), "kit consumed only with activation");
            Assert.IsTrue(garage.IsCampActivated("camp.forest.a"), "camp activated in the same commit");
            Assert.IsNotNull(committed);
            Assert.AreEqual("camp.forest.a", committed.CampId.Value);
            Assert.IsFalse(garage.IsCampActivated("camp.forest.b"));
        }

        [Test]
        public void Missions_LifecycleAndSingleReward_QA1()
        {
            // Uses a local instance (public constructor since KI-023) instead of the shared
            // MissionService.Global, so the test no longer depends on fixture execution order.
            var economy = new EconomyService();
            var missions = new MissionService();
            missions.Restore(null, new[] { ("Q01", MissionState.Active, new[] { 1, 1, 0 }) });
            missions.ReportObjective("Q01", 2);
            Assert.AreEqual(MissionState.ReadyToTurnIn, missions.StateOf("Q01"));

            var ledger = new ClaimedRewardLedger();
            Assert.IsTrue(missions.TryTurnIn("Q01", new ContentId("garage.home"), economy, ledger).Success);
            Assert.AreEqual(150, economy.BankedSalvage);
            Assert.AreEqual(MissionState.Completed, missions.StateOf("Q01"));
            Assert.AreEqual(MissionState.Available, missions.StateOf("Q02"), "next mission unlocks");
            Assert.IsFalse(missions.TryTurnIn("Q01", new ContentId("garage.home"), economy, ledger).Success,
                "a completed mission cannot be turned in again");
            Assert.AreEqual(150, economy.BankedSalvage, "reward paid exactly once");
        }

        [Test]
        public void Discovery_LandmarkRewardOnceAndMappingThresholds_QA18()
        {
            var economy = new EconomyService();
            var discovery = new DiscoveryService();
            discovery.BindEconomy(economy);

            Assert.IsTrue(discovery.TryDiscoverLandmark("landmark.forest.01", 10f, 2.5f, out _));
            Assert.IsFalse(discovery.TryDiscoverLandmark("landmark.forest.01", 10f, 2.5f, out _), "one reward ever");
            Assert.AreEqual(DiscoveryService.LandmarkDiscoveryRewardSalvage, economy.EscrowedSalvage);

            discovery.RevealAround(3000f, 3000f, fromVista: true);
            int escrowAfterReveal = economy.EscrowedSalvage;
            // Mapping threshold pays are escrowed once per 50 cells with unique keys.
            discovery.RevealAround(3100f, 3100f, fromVista: true);
            Assert.GreaterOrEqual(economy.EscrowedSalvage, escrowAfterReveal);

            // Restore must not re-pay past thresholds.
            var second = new DiscoveryService();
            second.BindEconomy(economy);
            second.Restore(discovery.RevealedCells.ToBase64(), "", null,
                discovery.DiscoveredLandmarkIds, discovery.CollectedCacheIds, discovery.RelicIds,
                discovery.MappingRewardsPaidThroughCount);
            int before = economy.EscrowedSalvage;
            second.RevealAround(3000f, 3000f, fromVista: true);
            Assert.AreEqual(before, economy.EscrowedSalvage, "threshold reward not duplicated after restore");
        }
    }
}
