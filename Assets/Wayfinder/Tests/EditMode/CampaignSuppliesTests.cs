using System.IO;
using System.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Field supplies (§5.5, §5.6, §10.3) and selling (§10.4) through <see cref="CampaignSession"/>:
    /// exact item consumption, use conditions, waste confirmation, idempotent replays, persistence,
    /// the 40 % sell-back with rounding down, no arbitrage, critical-save rollback and mission
    /// reservations.
    /// </summary>
    public class CampaignSuppliesTests
    {
        string _root;

        [SetUp]
        public void SetUp() => _root = NewTempRoot();

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        CampaignSession InField(out SaveService saves)
        {
            saves = new SaveService(_root);
            var s = NewGame(saves);
            Ok(s.Depart(), "depart");
            return s;
        }

        static void Telemetry(CampaignSession s, float fuel, int engine = 100, int suspension = 100, int tires = 100, int body = 100) =>
            s.UpdateVehicle(new Pose(1000f, 0.6f, 2250f, 0f), fuel, engine, suspension, tires, body, 0.1f);

        static int InCargo(CampaignSession s, string itemId) => s.Inventory.QuantityOf(CampaignSession.Cargo, itemId);

        [Test]
        public void FuelCan_AddsUpToSixLitres_NeedsOneLitreMissing_ConfirmsWaste_ConsumesExactlyOne_S10_3()
        {
            var s = InField(out _);
            float capacity = s.FuelCapacityLiters;
            Assert.AreEqual(2, InCargo(s, "fuel_can"), "starting loadout (§10.3)");

            Telemetry(s, capacity - 0.5f);
            Assert.AreEqual(FailureReason.ItemNotUsableHere, s.UseFuelCan(acceptWaste: true).Reason, "needs ≥ 1 L missing");
            Assert.AreEqual(2, InCargo(s, "fuel_can"));

            Telemetry(s, capacity - 10f);
            Ok(s.UseFuelCan(), "10 L missing: the whole 6 L goes in, nothing wasted, no confirmation");
            Assert.AreEqual(capacity - 4f, s.FuelLiters, 1e-4f);
            Assert.AreEqual(1, InCargo(s, "fuel_can"));

            var preview = s.PreviewFuelCan();
            Assert.IsTrue(preview.NeedsWasteConfirmation);
            Assert.AreEqual(4f, preview.LitersAdded, 1e-4f);
            Assert.AreEqual(2f, preview.LitersWasted, 1e-4f);
            var unconfirmed = s.UseFuelCan();
            Assert.AreEqual(FailureReason.Cancelled, unconfirmed.Reason);
            StringAssert.Contains("2.0 L of the can would be wasted", unconfirmed.Detail);
            Assert.AreEqual(1, InCargo(s, "fuel_can"), "an unconfirmed use changes nothing");
            Assert.AreEqual(capacity - 4f, s.FuelLiters, 1e-4f);

            Ok(s.UseFuelCan(acceptWaste: true), "confirmed: the partly used can is consumed as one item (§10.3)");
            Assert.AreEqual(capacity, s.FuelLiters, 1e-4f, "never above capacity");
            Assert.AreEqual(0, InCargo(s, "fuel_can"));
            Telemetry(s, capacity - 8f);
            Assert.AreEqual(FailureReason.ItemNotFound, s.UseFuelCan(true).Reason);
            Assert.IsTrue(s.HasUnsavedChanges, "supplies are field progress (debounced autosave)");
            Assert.IsTrue(s.DrainNotices().Any(n => n.Kind == CampaignNoticeKind.SupplyUsed && n.Text == "Fuel can: +6.0 L"));
        }

        [Test]
        public void FieldSupplies_AreNotUsedInTheGarage_RepairsThereAreFree()
        {
            var s = NewGame(new SaveService(_root));
            Assert.AreEqual(FailureReason.WrongState, s.UseFuelCan(true).Reason);
            Assert.AreEqual(FailureReason.WrongState, s.UseRepairKit(HitZone.Engine, 0f, false).Reason);
            Assert.AreEqual(FailureReason.WrongState, s.UseTirePatch(0f).Reason);
        }

        [Test]
        public void RepairKit_RestoresThirtyFiveToOneSubsystem_OnlyStoppedAndOutOfWater_NeverWasted_S5_5()
        {
            var s = InField(out _);
            Telemetry(s, 20f, engine: 40, suspension: 90, tires: 100, body: 70);
            Assert.AreEqual(FailureReason.VehicleMoving, s.UseRepairKit(HitZone.Engine, 12f, false).Reason);
            Assert.AreEqual(FailureReason.ItemNotUsableHere, s.UseRepairKit(HitZone.Engine, 0f, true).Reason, "not in water");
            Assert.AreEqual(FailureReason.ItemNotUsableHere, s.UseRepairKit(HitZone.Tires, 0f, false).Reason, "full: kit not wasted");
            Assert.AreEqual(FailureReason.InvalidTarget, s.UseRepairKit(HitZone.Underbody, 0f, false).Reason);
            Assert.AreEqual(2, InCargo(s, "repair_kit"), "refusals consume nothing");

            Ok(s.UseRepairKit(HitZone.Engine, 0.5f, false), "engine");
            Assert.AreEqual(75, s.Damage.EngineCondition);
            Ok(s.UseRepairKit(HitZone.Suspension, 0f, false), "suspension");
            Assert.AreEqual(100, s.Damage.SuspensionCondition, "capped at 100");
            Assert.AreEqual(0, InCargo(s, "repair_kit"));
            Assert.AreEqual(FailureReason.ItemNotFound, s.UseRepairKit(HitZone.Body, 0f, false).Reason);
            Assert.IsTrue(s.DrainNotices().Any(n => n.Text == "Repair kit: engine 40 → 75"));
        }

        [Test]
        public void RepairKit_OnTheEngine_EndsAWaterStallIncident_S5_3()
        {
            var s = InField(out _);
            Assert.IsTrue(s.Damage.ApplyWaterStall());
            Ok(s.UseRepairKit(HitZone.Engine, 0f, inWater: false), "repair after leaving the water");
            Assert.IsFalse(s.Damage.WaterIncidentActive);
            Assert.AreEqual(100, s.Damage.EngineCondition);
        }

        [Test]
        public void TirePatch_RemovesThePuncture_OnlyWhenPunctured_GripPenaltyRemains_S5_5()
        {
            var s = InField(out _);
            Telemetry(s, 20f, tires: 45);
            Assert.IsFalse(s.Damage.HasPuncture);
            Assert.AreEqual(FailureReason.ItemNotUsableHere, s.UseTirePatch(0f).Reason, "no puncture: patch not wasted");
            Telemetry(s, 20f, tires: 12);
            Assert.IsTrue(s.Damage.HasPuncture);
            Assert.AreEqual(FailureReason.VehicleMoving, s.UseTirePatch(5f).Reason);
            Ok(s.UseTirePatch(0f), "patch");
            Assert.AreEqual(DamageModel.PunctureThreshold, s.Damage.TiresCondition);
            Assert.IsFalse(s.Damage.HasPuncture, "25 km/h cap lifted");
            Assert.Less(s.Damage.TiresCondition, 50, "the below-50 grip penalty stays until a free garage repair");
            Assert.AreEqual(1, InCargo(s, "tire_patch"));
        }

        [Test]
        public void FieldSupplies_AreIdempotentTransactions_AndPersistByteIdentically()
        {
            var s = InField(out _);
            Telemetry(s, s.FuelCapacityLiters - 8f, engine: 50);
            Ok(s.UseFuelCan(), "fuel can");
            Ok(s.UseRepairKit(HitZone.Engine, 0f, false), "repair kit");
            string lastId = s.Transactions.AppliedOrder[s.Transactions.AppliedOrder.Count - 1];
            var replay = s.Transactions.Execute(new UseItem(new TransactionId(lastId), CampaignSession.Cargo,
                new ContentId("repair_kit"), new ContentId("subsystem.engine")));
            Assert.IsTrue(replay.Success, "a replayed id is an idempotent no-op");
            Assert.AreEqual(1, InCargo(s, "repair_kit"), "…that never consumes a second kit");
            var notSupply = s.Transactions.Execute(new UseItem(new TransactionId("test:material"), CampaignSession.Cargo,
                new ContentId("relay_fuse"), new ContentId("vehicle")));
            Assert.AreEqual(FailureReason.ItemNotUsableHere, notSupply.Reason, "only consumables can be used");

            Assert.IsTrue(s.SaveNow().Success);
            string canonical = Canonical(s);
            var reloaded = Reload(_root);
            Assert.AreEqual(canonical, Canonical(reloaded));
            Assert.AreEqual(1, InCargo(reloaded, "fuel_can"));
            Assert.AreEqual(85, reloaded.Damage.EngineCondition);
            Assert.AreEqual(s.FuelCapacityLiters - 2f, reloaded.FuelLiters, 1e-4f);
        }

        [Test]
        public void Selling_AtTheGarage_FortyPercentRoundedDown_NoArbitrage_S10_4()
        {
            var s = NewGame(new SaveService(_root));
            int wallet = s.Economy.BankedSalvage;
            Ok(s.SellItems("tire_patch", 1, fromCargo: true), "sell a tire patch");
            Assert.AreEqual(wallet + 8, s.Economy.BankedSalvage, "22 × 40 % = 8.8 → 8 (rounded down)");
            Ok(s.SellItems("fuel_can", 2, fromCargo: true), "sell both fuel cans");
            Assert.AreEqual(wallet + 8 + 48, s.Economy.BankedSalvage);
            Assert.AreEqual(FailureReason.ItemNotFound, s.SellItems("fuel_can", 1, true).Reason);
            Assert.AreEqual(FailureReason.ItemNotUsableHere, s.SellItems("salvage_bundle", 1, true).Reason, "banks 1:1 instead");
            Assert.AreEqual(FailureReason.ItemNotUsableHere, s.SellItems("relay_fuse", 1).Reason, "protected");
            Assert.AreEqual(FailureReason.InvalidTarget, s.SellItems("unobtainium", 1).Reason);
            Assert.AreEqual(FailureReason.InvalidTarget, s.SellItems("repair_kit", 0, true).Reason);
            Assert.IsTrue(s.DrainNotices().Any(n => n.Kind == CampaignNoticeKind.ItemSold && n.Amount == 48));

            foreach (var recipe in CraftingService.Recipes)
            {
                int inputValue = recipe.SalvageFee;
                foreach (var (itemId, quantity) in recipe.Inputs) inputValue += ItemCatalog.Get(itemId).PurchasePriceSalvage * quantity;
                int sellValue = ItemCatalog.SellPrice(recipe.OutputItemId) * recipe.OutputQuantity;
                Assert.Less(sellValue, inputValue, $"{recipe.Id.Value}: crafting then selling never creates Salvage (QA 17)");
            }

            Ok(s.Depart(), "depart");
            Assert.AreEqual(FailureReason.WrongState, s.SellItems("repair_kit", 1, true).Reason, "only at a garage");
        }

        [Test]
        public void Selling_IsACriticalSave_RolledBackOnWriteFailure_AndKeepsMissionCampKits_D020()
        {
            var saves = new SaveService(_root);
            var s = NewGame(saves);
            string before = Canonical(s);
            saves.StepHook = step => { if (step == SaveStep.TempVerified) throw new IOException("simulated write failure"); };
            var failed = s.SellItems("repair_kit", 1, fromCargo: true);
            Assert.AreEqual(FailureReason.SaveIoFailure, failed.Reason);
            Assert.AreEqual(before, Canonical(s), "rolled back: kit kept, no Salvage granted");
            saves.StepHook = null;

            PlayQ01(s);
            PlayQ02(s);
            Ok(s.AcceptMission("Q03"), "Q03 grants its camp kit");
            Assert.AreEqual(FailureReason.ItemNotUsableHere, s.SellItems("camp_kit", 1).Reason, "Q03 still needs it");
            var row = GarageModel.Build(s).Storage.Single(r => r.ItemId == "camp_kit");
            Assert.IsFalse(row.Sellable);
            StringAssert.Contains("active mission", row.SellBlockedReason);
            var kits = GarageModel.Build(s).Cargo.Single(r => r.ItemId == "repair_kit");
            Assert.IsTrue(kits.Sellable);
            Assert.AreEqual(18, kits.SellPriceEach, "45 × 40 %");
        }

        [Test]
        public void SuppliesModel_ExplainsWhyEachSupplyIsOrIsNotUsable()
        {
            var s = InField(out _);
            Telemetry(s, s.FuelCapacityLiters - 3f, engine: 60, tires: 20);
            var m = SuppliesModel.Build(s, speedKph: 20f, inWater: false);
            Assert.AreEqual("Fuel can ×2 — +3.0 L (3.0 L wasted — tap twice)", m.FuelCanLine);
            Assert.IsTrue(m.FuelCanNeedsConfirmation);
            StringAssert.EndsWith("stop the vehicle to repair", m.RepairLine);
            Assert.IsFalse(m.Subsystems.Any(r => r.CanRepair));
            StringAssert.EndsWith("stop the vehicle to patch", m.TirePatchLine);

            SuppliesModel.Build(s, 0f, false, m);
            Assert.IsTrue(m.Subsystems.Single(r => r.Zone == HitZone.Engine).CanRepair);
            Assert.IsFalse(m.Subsystems.Single(r => r.Zone == HitZone.Body).CanRepair, "already at 100");
            Assert.IsTrue(m.TirePatchUsable);
            SuppliesModel.Build(s, 0f, true, m);
            StringAssert.EndsWith("get out of the water first", m.RepairLine);

            var garage = SuppliesModel.Build(NewGame(new SaveService(Path.Combine(_root, "garage"))), 0f, false);
            StringAssert.EndsWith("the garage refuels for free", garage.FuelCanLine);
        }
    }
}
