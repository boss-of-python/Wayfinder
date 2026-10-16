// Characterization probe for the 2026-10-16 AI code review (Docs/AI_BUG_REPORT.md).
//
// Every test here asserts what the CURRENT code does, including the parts that contradict the
// blueprint or the repositories own documentation. The tests therefore PASS on the reviewed
// commit and fail as soon as the defect is fixed — that is the point: they are executable
// evidence, not a regression suite. Flip an assertion into the "expected" direction when you
// fix the bug, and move the test into Tests/EditMode.
//
// Compiled against the real Wayfinder assemblies by run_bug_probe.py (NOT part of the repo's
// Assets/ tree, so the normal headless run is unaffected).
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;
using Wayfinder.World;

namespace Wayfinder.Review
{
    public class ReviewProbeTests
    {
        static ResearchService AllTierResearch(bool reversed)
        {
            var research = new ResearchService(_ => true);   // every story gate "completed"
            var ids = new List<string>();
            foreach (var tier in research.Tiers) ids.Add(tier.Id.Value);
            if (reversed) ids.Reverse();
            research.Restore(ids);                           // all 20 tiers researched
            return research;
        }

        static VehicleStatsResolver ResolverWithAllUpgrades()
        {
            var resolver = new VehicleStatsResolver();
            AllTierResearch(reversed: false).ApplyTo(resolver);
            return resolver;
        }

        static EffectiveVehicleStats ResolveAll(VehicleStatsResolver resolver) =>
            resolver.Resolve(VehicleSpec.ScoutS1(), 24f, 0f, TireSet.AllTerrain, SurfaceType.PackedDirt, false,
                100, 100, 100, false, false, false);

        // ------------------------------------------------------------------ DEF-01
        [Test]
        public void DEF01_WadingDepthIsAlwaysZero()
        {
            var stats = ResolveAll(ResolverWithAllUpgrades());
            Assert.AreEqual(0f, stats.SafeWadingDepthM,
                "EffectiveVehicleStats.SafeWadingDepthM is never assigned by Resolve(); it stays 0 m even " +
                "with every research tier applied, while §5.3 documents 0.55 m baseline / 0.85 m upgraded.");
        }

        [Test]
        public void DEF01_VehicleStallsIn25cmOfWaterBecauseIntakeHeightIsZero()
        {
            // Exactly what VehicleController.FixedUpdate does: intakeHeight = stats.SafeWadingDepthM.
            var stats = ResolveAll(ResolverWithAllUpgrades());
            var water = new WaterCrossingModel();
            bool stalled = false;
            for (int i = 0; i < 30 && !stalled; i++)               // 3 s at 0.1 s steps
                stalled = water.Step(0.25f, stats.SafeWadingDepthM, 0.1f, out bool ev) && ev;
            Assert.IsTrue(stalled, "0.25 m of water (well under the 0.55 m baseline) stalls the engine");

            var documented = new WaterCrossingModel();
            bool stallsAtBaseline = false;
            for (int i = 0; i < 30; i++)
                if (documented.Step(0.25f, WaterCrossingModel.BaselineSafeWadingM, 0.1f, out bool ev) && ev)
                    stallsAtBaseline = true;
            Assert.IsFalse(stallsAtBaseline, "with the documented 0.55 m baseline the same water is safe.");
        }

        [Test]
        public void DEF01_LowRangeAndDiffLockFlagsAreNeverSet()
        {
            var stats = ResolveAll(ResolverWithAllUpgrades());
            Assert.IsFalse(stats.LowRangeAvailable, "tier 7 'lowRange' has no effect: nothing assigns this flag");
            Assert.IsFalse(stats.DiffLockAvailable, "tier 13 'diffLock' has no effect: nothing assigns this flag");
            // Tiers 7, 8, 12, 13, 18 are authored with an empty effect list.
            var definitions = new ResearchService(_ => true).Tiers;
            foreach (int tier in new[] { 7, 8, 12, 13, 18 })
            {
                var def = definitions[tier - 1];
                Assert.AreEqual(0, def.Effects.Count, $"research tier {tier} ({def.Id}) declares no stat effects");
            }
        }

        [Test]
        public void DEF01_TireUnlockFlagsOnlyReflectCallerArguments()
        {
            var stats = new VehicleStatsResolver().Resolve(VehicleSpec.ScoutS1(), 24f, 0f, TireSet.AllTerrain,
                SurfaceType.PackedDirt, false, 100, 100, 100, mudTiresResearched: false,
                snowTiresResearched: false, rockTiresResearched: false);
            Assert.IsFalse(stats.MudTiresUnlocked);
            Assert.IsFalse(stats.SnowTiresUnlocked);
            Assert.IsFalse(stats.RockTiresUnlocked);
            // VehicleController.ResolveStats() hardcodes these three arguments to `true`
            // (Assets/Wayfinder/Scripts/Simulation/VehicleController.cs), and the campaign path
            // hardcodes them to `false` (CampaignSession.ResolveStats) — neither reads research.
        }

        // ------------------------------------------------------------------ DEF-02
        [Test]
        public void DEF02_ComposeIsSetOrderDependent()
        {
            var a = new VehicleStatsResolver();
            a.AddModifier(VehicleStatsResolver.Stat.FuelCapacityLiters, VehicleStatsResolver.Operation.Set, 30f, "tier6");
            a.AddModifier(VehicleStatsResolver.Stat.FuelCapacityLiters, VehicleStatsResolver.Operation.Set, 40f, "tier16");
            Assert.AreEqual(40f, a.Compose(VehicleStatsResolver.Stat.FuelCapacityLiters, 24f));

            var b = new VehicleStatsResolver();
            b.AddModifier(VehicleStatsResolver.Stat.FuelCapacityLiters, VehicleStatsResolver.Operation.Set, 40f, "tier16");
            b.AddModifier(VehicleStatsResolver.Stat.FuelCapacityLiters, VehicleStatsResolver.Operation.Set, 30f, "tier6");
            Assert.AreEqual(30f, b.Compose(VehicleStatsResolver.Stat.FuelCapacityLiters, 24f),
                "the LAST Set modifier wins, so a researched tank can resolve smaller depending on order");
        }

        [Test]
        public void DEF02_ResearchApplyOrderIsHashSetOrderNotTierOrder()
        {
            // ResearchService.ApplyTo iterates a HashSet<string>, and Compose() lets the LAST Set
            // modifier win. CargoSlots is Set 14 (tier 4), 16 (tier 11), 20 (tier 17).
            var normal = new VehicleStatsResolver();
            AllTierResearch(reversed: false).ApplyTo(normal);
            Assert.AreEqual(20, ResolveAll(normal).CargoSlots, "tier order happens to work today");

            var reversed = new VehicleStatsResolver();
            AllTierResearch(reversed: true).ApplyTo(reversed);
            Assert.AreEqual(14, ResolveAll(reversed).CargoSlots,
                "reverse the iteration order and all 20 researched tiers resolve to the TIER 4 rack (14 slots, " +
                "and 30 L tank), i.e. buying later upgrades can silently make the vehicle worse");
        }

        // ------------------------------------------------------------------ DEF-03
        [Test]
        public void DEF03_SeamCheckComparesTheSameContinuousField()
        {
            var field = new HeightField();
            field.Bake();
            float endX = 5 * WorldConstants.ChunkSizeMeters + WorldConstants.ChunkSizeMeters;
            float z = 5 * WorldConstants.ChunkSizeMeters + 40f;
            float a = HeightField.Quantize(field.Sample(endX - 0.001f, z));
            float b = HeightField.Quantize(field.Sample(endX + 0.001f, z));
            Assert.AreEqual(0f, Math.Abs(a - b),
                "the two probes are 2 mm apart in ONE continuous height function, so they can only differ " +
                "by slope × 0.002 m; tile border samples are never compared");
            Assert.IsTrue(SeamCheck.BordersMatch(field, 5, 5, out float worst));
            Assert.AreEqual(0f, worst, "reported 'worst diff 0.0' — a check that cannot fail");
        }

        // ------------------------------------------------------------------ DEF-04
        [Test]
        public void DEF04_UnknownItemIdInPickupContentThrowsOutOfTransactionExecute()
        {
            var economy = new EconomyService();
            var inventory = new InventoryService();
            inventory.ConfigureContainer(new InventoryId("cargo"), 12, 120f);
            // PickupContents does NOT validate item ids; neither does IPickupCatalog.
            var pickups = new PickupCatalog().Add(new PickupContents("cache.typo", 5,
                new[] { ("item.that.does.not.exist", 2) }));
            var tx = new TransactionService(economy, inventory,
                new TransactionDependencies { Pickups = pickups, Discovery = new DiscoveryService() });

            Assert.Throws<KeyNotFoundException>(() => tx.Execute(new CollectPickup(new TransactionId("t1"),
                    new ContentId("cache.typo"), new InventoryId("cargo"))),
                "an unknown item id inside authored pickup content raises KeyNotFoundException from " +
                "InventoryService.Add instead of a typed ContentMissing");
            // The same applies to any item id that reaches a container (ItemStack.MassKg / UsedSlots /
            // CargoSettlement.CategoryOf all call ItemCatalog.Get, which throws on unknown ids).
            Assert.DoesNotThrow(() => ItemCatalog.SellPrice("salvage_bundle"));
            Assert.Throws<KeyNotFoundException>(() => ItemCatalog.CategoryOf("item.that.does.not.exist"));
        }

        [Test]
        public void DEF04_DoubleCollectGuardDependsOnDiscoveryBeingWired()
        {
            var economy = new EconomyService();
            var inventory = new InventoryService();
            inventory.ConfigureContainer(new InventoryId("cargo"), 12, 120f);
            var pickups = new PickupCatalog().Add(new PickupContents("cache.x", 10, null));
            var deps = new TransactionDependencies { Pickups = pickups, Discovery = null }; // Discovery forgotten
            var tx = new TransactionService(economy, inventory, deps);

            Assert.IsTrue(tx.Execute(new CollectPickup(new TransactionId("a"), new ContentId("cache.x"),
                new InventoryId("cargo"))).Success);
            Assert.IsTrue(tx.Execute(new CollectPickup(new TransactionId("b"), new ContentId("cache.x"),
                new InventoryId("cargo"))).Success);
            Assert.AreEqual(20, inventory.QuantityOf(new InventoryId("cargo"), "salvage_bundle"),
                "the same cache was collected twice: the only duplicate guard is `_deps.Discovery != null`");
        }

        // ------------------------------------------------------------------ DEF-05
        [Test]
        public void DEF05_OnlySomeCommandsEmitCommittedEvents()
        {
            var economy = new EconomyService();
            economy.Grant(1000);
            var inventory = new InventoryService();
            inventory.ConfigureContainer(new InventoryId("cargo"), 12, 120f);
            inventory.ConfigureContainer(new InventoryId("store"), 12, 1200f);
            inventory.Add(new InventoryId("store"), new ItemStack(new ContentId("scrap_metal"), 4));
            inventory.Add(new InventoryId("store"), new ItemStack(new ContentId("polymer"), 2));
            inventory.Add(new InventoryId("store"), new ItemStack(new ContentId("electronics"), 1));

            var tx = new TransactionService(economy, inventory, new TransactionDependencies());
            var events = new List<string>();
            tx.OnCommitted += e => events.Add(e.GetType().Name);

            var craft = tx.Execute(new CraftItem(new TransactionId("c1"), new ContentId("recipe.camp_kit"),
                new InventoryId("store"), new InventoryId("store")));
            Assert.IsTrue(craft.Success);
            Assert.AreEqual(0, events.Count,
                "CraftItem/TransferItems/SellItems/UseItem commit with `committed = null`: no GameEvent is raised");
        }

        // ------------------------------------------------------------------ DEF-06
        [Test]
        public void DEF06_NonHomeGaragesAreNeverUnlockedAndFullCampaignCannotProgress()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wf-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                var content = CampaignContent.FromDirectory("Assets/Wayfinder/Content");
                var saves = new SaveService(dir);
                var open = CampaignSession.StartNew(saves, content, new SaveSlotId(0));
                Assert.IsTrue(open.Playable);
                var s = open.Session;
                s.Depart();

                // Q05 asks for VisitVolume(garage.canyon); the only source of that condition is
                // extraction at that garage, which requires the garage to be unlocked.
                foreach (var id in GarageService.GarageIds)
                    if (id != "garage.home")
                        Assert.IsFalse(s.Garage.IsGarageUnlocked(id), $"{id} can never be unlocked: UnlockGarage is never called");

                var summary = s.EndExpedition("garage.canyon");
                Assert.IsFalse(summary.Result.Success, "extraction at a locked garage is refused");
                Assert.AreEqual(FailureReason.InvalidTarget, summary.Result.Reason);

                // The Q05+ objective targets do not exist in any world/content catalog either.
                Assert.IsFalse(s.Content.Landmarks.ContainsKey("junction.city.01"));
                Assert.IsNull(content.Slice.FindSite(content.Slice.InspectTargets, "marker.canyon.01"));
            }
            finally
            {
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }

        // ------------------------------------------------------------------ DEF-07
        [Test]
        public void DEF07_BitGridFromBase64DoesNotValidatePayload()
        {
            var grid = BitGrid.FromBase64(96, 96, "AAAAAAAAAAA="); // 8 bytes — one word of a 144-word grid
            Assert.AreEqual(0, grid.CountSet(), "short payloads are silently accepted (no length validation)");
            Assert.Throws<FormatException>(() => BitGrid.FromBase64(96, 96, "not base64 at all!"),
                "malformed payloads throw FormatException — a type SaveService.TryRead does not catch");
        }

        // ------------------------------------------------------------------ DEF-08
        [Test]
        public void DEF08_TransactionIdOfGrantedRewardsIsDefaultInDiscoveryService()
        {
            var economy = new EconomyService();
            var discovery = new DiscoveryService();
            discovery.BindEconomy(economy);
            Assert.IsTrue(discovery.TryDiscoverLandmark("landmark.forest.01", 0f, 5f, out var ev));
            var landmark = (LandmarkDiscovered)ev;
            Assert.IsNull(landmark.TransactionId.Value,
                "LandmarkDiscovered carries default(TransactionId) — no replay-safe id on a permanent reward");
            Assert.AreEqual(120, economy.EscrowedSalvage);
        }

        // ------------------------------------------------------------------ DEF-09
        [Test]
        public void DEF09_ChunkFailedTransitionLosesItsFailureDetail()
        {
            var sm = new ChunkStateMachine();
            Assert.IsTrue(sm.TryTransition(ChunkState.Requested, out _));
            Assert.IsTrue(sm.TryTransition(ChunkState.Failed, out _));
            Assert.AreEqual(string.Empty, sm.FailureDetail,
                "FailureDetail is assigned from `error`, which is only ever set on the rejected path");
        }

        // ------------------------------------------------------------------ DEF-10
        [Test]
        public void DEF10_CargoSettlementBanksEscrowFromAnySourceOnEveryBank()
        {
            var economy = new EconomyService();
            var inventory = new InventoryService();
            inventory.ConfigureContainer(new InventoryId("cargo"), 12, 500f);
            inventory.ConfigureContainer(new InventoryId("camp"), 10000, 500f);
            economy.Escrow(75); // e.g. a mapping reward earned far from this hub
            var outcome = CargoSettlement.Settle(inventory, economy, new InventoryId("cargo"), new InventoryId("camp"));
            Assert.AreEqual(75, outcome.EscrowBanked, "a bank at any hub drains the whole escrow balance");
            Assert.AreEqual(75, economy.BankedSalvage);
        }
    }
}
