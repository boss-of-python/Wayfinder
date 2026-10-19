using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;
using Wayfinder.World;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Regression tests for DEF-001 … DEF-025 of <c>Docs/AI_BUG_REPORT.md</c>.
    /// <para>
    /// Each test pins one defect: it fails against the code as reviewed and passes against the fix.
    /// They are the inverse of the characterization probes in
    /// <c>Docs/evidence/ai-review-2026-10-16/BugProbe.cs</c>, which asserted the *broken* behaviour
    /// so the findings were reproducible; those probes are expected to fail now.
    /// </para>
    /// Only engine-free assemblies are exercised (KI-034): the Unity-facing fixes (DEF-005, DEF-006,
    /// DEF-013, DEF-014, DEF-015) are verified by <c>Tools/Build/unity_type_check.py</c> and by the
    /// PlayMode suite on a real editor.
    /// </summary>
    public class BugReportRegressionTests
    {
        static readonly InventoryId Cargo = new InventoryId("cargo");
        static readonly InventoryId Storage = new InventoryId("garageStorage");

        // ============================================================ DEF-001 / DEF-004 / DEF-002

        /// <summary>Resolves through the campaign session: research → resolver → stats (§11.2).</summary>
        static EffectiveVehicleStats Resolve(CampaignSession s)
        {
            var rig = new VehicleStatsResolver();
            s.ConfigureResolver(rig);
            return rig.Resolve(s.Vehicle, s.FuelLiters, 0f, TireSet.AllTerrain, SurfaceType.PackedDirt, false,
                100, 100, 100, false, false, false);
        }

        static List<string> AllTierIds(ResearchService research)
        {
            var ids = new List<string>();
            foreach (var tier in research.Tiers) ids.Add(tier.Id.Value);
            return ids;
        }

        [Test]
        public void Research_EveryTierHasAnEffect_DEF004()
        {
            var research = new ResearchService(_ => true);
            foreach (var tier in research.Tiers)
                Assert.Greater(tier.Effects.Count, 0,
                    $"{tier.Id.Value} costs {tier.CostSalvage} Salvage and must change something " +
                    "(or declare \"none, by design\")");
        }

        [Test]
        public void Research_AllTiers_UnlockLowRangeDiffLockTiresAndWading_DEF001_DEF004()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var session = CampaignTestKit.NewGame(new SaveService(root));
                var stock = Resolve(session);

                session.Research.Restore(AllTierIds(session.Research));
                var all = Resolve(session);

                Assert.IsTrue(all.LowRangeAvailable, "tier 07 low range");
                Assert.IsTrue(all.DiffLockAvailable, "tier 13 diff lock");
                Assert.IsTrue(all.MudTiresUnlocked, "tier 08 mud tires");
                Assert.IsTrue(all.SnowTiresUnlocked, "tier 18 cold kit unlocks snow tires");
                Assert.Greater(all.SafeWadingDepthM, 0.5f, "tier 12 raised intake: the intake height is real");
                Assert.Greater(stock.SafeWadingDepthM, 0f, "even unresearched, the stock intake height is not zero");
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        [Test]
        public void Research_CompositionDoesNotDependOnInsertionOrder_DEF002()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var session = CampaignTestKit.NewGame(new SaveService(root));
                var ids = AllTierIds(session.Research);

                session.Research.Restore(ids);
                var forward = Resolve(session);

                var reversed = new List<string>(ids);
                reversed.Reverse();
                session.Research.Restore(reversed);
                var backward = Resolve(session);

                Assert.AreEqual(forward.CargoSlots, backward.CargoSlots, "cargo slots (Operation.Set)");
                Assert.AreEqual(forward.FuelCapacityLiters, backward.FuelCapacityLiters, "tank size (Operation.Set)");
                Assert.AreEqual(forward.SignalRadiusM, backward.SignalRadiusM, "signal radius (Operation.Set)");
                Assert.AreEqual(forward.PeakTorqueNm, backward.PeakTorqueNm, "engine tune (Operation.Mul)");

                // The highest tier wins, whichever order it was applied in.
                int bestSlots = 0;
                foreach (var tier in session.Research.Tiers)
                foreach (var effect in tier.Effects)
                    if (effect.stat == VehicleStatsResolver.Stat.CargoSlots &&
                        effect.op == VehicleStatsResolver.Operation.Set)
                        bestSlots = Math.Max(bestSlots, (int)effect.value);
                Assert.AreEqual(bestSlots, forward.CargoSlots, "the last authored Set wins");
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        // ==================================================================== DEF-003 campaign reach

        [Test]
        public void EveryMissionObjectiveTargetResolvesToKnownContent_DEF003()
        {
            var content = CampaignTestKit.Content;
            var missing = new List<string>();
            var service = new MissionService();
            foreach (var code in service.Definitions.Keys)
            {
                foreach (var objective in service.Definitions[code].Objectives)
                {
                    string target = objective.TargetId.Value;
                    if (target == "map") continue; // RevealMapPercent carries a percentage, not a place
                    if (content.Landmarks.ContainsKey(target) || content.Caches.ContainsKey(target) ||
                        content.Garages.ContainsKey(target) || content.Camps.ContainsKey(target) ||
                        content.Sites.ContainsKey(target) || content.Routes.ContainsKey(target) ||
                        ItemCatalog.TryGet(target, out _) ||
                        content.Slice.FindSite(content.Slice.InspectTargets, target) != null ||
                        content.Slice.FindSite(content.Slice.Relays, target) != null ||
                        content.Slice.FindSite(content.Slice.QuestItemSites, target) != null ||
                        content.Slice.FindSite(content.Slice.CampSockets, target) != null ||
                        HasRoute(content, target))
                        continue;
                    missing.Add($"{code}:{objective.Type}:{target}");
                }
            }
            Assert.IsEmpty(missing, "mission objective targets with no catalog entry:\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void ExtractingAtAKnownGarageUnlocksIt_DEF003()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var session = CampaignTestKit.NewGame(new SaveService(root));
                Assert.IsFalse(session.Garage.IsGarageUnlocked("garage.canyon"), "locked before arrival");

                CampaignTestKit.Ok(session.Depart(), "depart");
                var summary = session.EndExpedition("garage.canyon");
                Assert.IsTrue(summary.Success, "extraction at a known garage: " + summary.Result);
                Assert.IsTrue(session.Garage.IsGarageUnlocked("garage.canyon"),
                    "reaching a garage is what unlocks it (Q05's VisitVolume is reported by extraction)");
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        [Test]
        public void ExtractionStillRejectsAnUnknownGarage_DEF003()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var session = CampaignTestKit.NewGame(new SaveService(root));
                CampaignTestKit.Ok(session.Depart(), "depart");
                var summary = session.EndExpedition("garage.atlantis");
                Assert.IsFalse(summary.Success, "an id that is not a garage must not be invented");
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        static bool HasRoute(CampaignContent content, string id)
        {
            foreach (var route in content.Slice.CheckpointRoutes) if (route.Id == id) return true;
            return content.Routes.ContainsKey(id);
        }

        // ==================================================================== DEF-007 contracts

        [Test]
        public void Contract_AcceptComplete_EscrowsExactlyOnce_DEF007()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var session = CampaignTestKit.NewGame(new SaveService(root));
                var board = session.AvailableContracts();
                Assert.AreEqual(ContractService.MaxAvailableJobs, board.Count, "the board offers three jobs");
                string id = board[0].Id;

                CampaignTestKit.Ok(session.AcceptContract(id), "accept " + id);
                Assert.IsTrue(session.Contracts.IsAccepted(id));

                int expected = session.Content.Contracts[id].RewardSalvage;
                CampaignTestKit.Ok(session.CompleteContract(id), "complete " + id);
                Assert.AreEqual(expected, session.Economy.EscrowedSalvage,
                    "the reward is escrowed, not granted to the wallet (§9.3)");
                Assert.AreEqual(0, session.Economy.BankedSalvage);
                Assert.IsTrue(session.Contracts.IsCompleted(id));

                // A second completion is refused: the reward key is already claimed.
                var again = session.CompleteContract(id);
                Assert.IsFalse(again.Success, "one contract pays once");
                Assert.AreEqual(expected, session.Economy.EscrowedSalvage, "no second payment");
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        [Test]
        public void Contract_CompletionEmitsACommittedEvent_DEF007_DEF018()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var session = CampaignTestKit.NewGame(new SaveService(root));
                var events = new List<GameEvent>();
                session.Transactions.OnCommitted += e => events.Add(e);

                string id = session.AvailableContracts()[0].Id;
                CampaignTestKit.Ok(session.AcceptContract(id), "accept");
                CampaignTestKit.Ok(session.CompleteContract(id), "complete");

                Assert.AreEqual(2, events.Count, "accept and complete each raise exactly one event");
                Assert.IsInstanceOf<ContractAccepted>(events[0]);
                Assert.IsInstanceOf<ContractCompleted>(events[1]);
                Assert.AreNotEqual(default(TransactionId).Value, events[0].TransactionId.Value,
                    "a permanent reward event carries its transaction id (DEF-019)");
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        // ==================================================================== DEF-008 countable mask

        [Test]
        public void AnAuthoredCountableMaskSurvivesSaveLoad_DEF008()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var session = CampaignTestKit.NewGame(new SaveService(root));

                // The mainland macro heightfield spans 131–415 m, so no map cell is below sea level:
                // the bake's land-only rule leaves every cell countable, which is correct for this
                // world. What DEF-008 broke is that the mask was never persisted, so any refinement
                // (WorldBaker's ocean/cliff mask) was silently discarded by the next load.
                var authored = session.Discovery.CountableMask.Clone();
                for (int i = 0; i < 10; i++) authored.Set(i, i, false);
                authored.Set(0, 5, false);
                session.Discovery.SetCountableMask(authored);

                Assert.IsTrue(session.SaveNow().Success);
                var reloaded = CampaignTestKit.Reload(root);

                Assert.IsTrue(reloaded.Discovery.CountableMask.Equals(authored),
                    "the mask is persisted: coverage means the same thing before and after a save");
                Assert.AreEqual(authored.CountSet(), reloaded.Discovery.CountableMask.CountSet());
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        [Test]
        public void CountableMaskIsBakedDeterministically_DEF008()
        {
            var a = new DiscoveryService();
            var b = new DiscoveryService();
            a.BakeCountableMask();
            b.BakeCountableMask();
            Assert.IsTrue(a.CountableMask.Equals(b.CountableMask), "same seed → same mask");
            Assert.AreEqual(0, a.CountableMask.CountSet() == 0 ? 1 : 0, "the mainland is countable");

            Assert.Throws<System.ArgumentException>(() => a.SetCountableMask(new BitGrid(8, 8)),
                "a mask of the wrong size is rejected instead of corrupting coverage");
        }

        // ==================================================================== DEF-009 / DEF-010

        [Test]
        public void PickupWithAnUnknownItemIdIsReportedNotThrown_DEF009()
        {
            var contents = new PickupContents("cache.test.unknown", 5,
                new[] { ("item_that_does_not_exist", 2) });

            Assert.AreEqual(1, contents.UnknownItemIds.Count, "the unknown id is reported");
            foreach (var stack in contents.Stacks)
                Assert.AreNotEqual("item_that_does_not_exist", stack.ItemId.Value, "…and it is never stacked");
            Assert.AreEqual(1, contents.Stacks.Count, "the valid Salvage bundle is still granted");

            var economy = new EconomyService();
            var inventory = new InventoryService();
            inventory.ConfigureContainer(Cargo, 12, 140f);
            var transactions = new TransactionService(economy, inventory, new TransactionDependencies
            {
                Discovery = new DiscoveryService(),
                Pickups = new StubPickups(contents)
            });

            var result = transactions.Execute(new CollectPickup(new TransactionId("tx.1"),
                new ContentId("cache.test.unknown"), Cargo));
            Assert.IsFalse(result.Success, "a content error is never silently collected");
            Assert.AreEqual(FailureReason.ContentMissing, result.Reason);
            Assert.AreEqual(0, inventory.QuantityOf(Cargo, ItemCatalog.SalvageBundle), "nothing was granted");
        }

        sealed class StubPickups : IPickupCatalog
        {
            readonly PickupContents _contents;
            public StubPickups(PickupContents contents) => _contents = contents;
            public bool TryGetContents(string pickupId, out PickupContents contents)
            {
                contents = pickupId == _contents.PickupId ? _contents : null;
                return contents != null;
            }
        }

        [Test]
        public void CollectionWithoutADiscoveryServiceFailsInsteadOfDoubling_DEF010()
        {
            var economy = new EconomyService();
            var inventory = new InventoryService();
            inventory.ConfigureContainer(Cargo, 12, 140f);
            var transactions = new TransactionService(economy, inventory, new TransactionDependencies
            {
                // Discovery deliberately left null — the double-collect guard used to be optional.
                Pickups = new StubPickups(new PickupContents("cache.x", 10, null))
            });

            var result = transactions.Execute(new CollectPickup(new TransactionId("tx.2"),
                new ContentId("cache.x"), Cargo));
            Assert.IsFalse(result.Success, "the invariant is required, not optional");
            Assert.AreEqual(FailureReason.ContentMissing, result.Reason);
        }

        // ==================================================================== DEF-011 seam check

        [Test]
        public void SeamCheckDetectsADisagreementBetweenTwoTiles_DEF011()
        {
            int res = WorldConstants.TerrainTileResolution;
            var left = new SeamCheck.TerrainTile(res);
            var right = new SeamCheck.TerrainTile(res);
            for (int z = 0; z < res; z++) { left[res - 1, z] = 10f; right[0, z] = 10f; }

            Assert.IsTrue(SeamCheck.BordersMatch(left, right, SeamCheck.Edge.East, out float ok),
                "matching borders pass");
            Assert.AreEqual(0f, ok, 1e-6f);

            right[0, 5] = 10.05f; // 5 cm — fifty times the 1 mm contract
            Assert.IsFalse(SeamCheck.BordersMatch(left, right, SeamCheck.Edge.East, out float diff),
                "the old check sampled one continuous field 2 mm apart and could not fail; this one does");
            Assert.Greater(diff, SeamCheck.MaxAllowedDifferenceMeters);
        }

        [Test]
        public void SeamCheckRejectsAnOutOfRangeChunk_DEF011()
        {
            var field = BakedField();
            Assert.IsFalse(SeamCheck.BordersMatch(field, -1, 0, out _), "an invalid chunk is not a pass");
            Assert.IsFalse(SeamCheck.BordersMatch(field, WorldConstants.ChunksPerSide, 0, out _));
            Assert.IsTrue(SeamCheck.BordersMatch(field, 0, 0, out float diff), "real neighbours agree");
            Assert.LessOrEqual(diff, SeamCheck.MaxAllowedDifferenceMeters);
        }

        static HeightField BakedField()
        {
            var field = new HeightField();
            Assert.IsFalse(field.IsBaked, "DEF-023: nothing is allocated before Bake()");
            field.Bake();
            Assert.IsTrue(field.IsBaked);
            return field;
        }

        // ==================================================================== DEF-012 chunk failures

        [Test]
        public void FailedChunkTransitionKeepsItsCause_DEF012()
        {
            var machine = new ChunkStateMachine();
            Assert.IsTrue(machine.TryTransition(ChunkState.Requested, out _));
            Assert.IsTrue(machine.TryTransition(ChunkState.DataReady, out _));
            Assert.IsTrue(machine.TryTransition(ChunkState.Failed, "terrain bake threw: IO error", out _));

            Assert.AreEqual(ChunkState.Failed, machine.State);
            Assert.AreEqual("terrain bake threw: IO error", machine.FailureDetail,
                "the detail must survive — the planner reports it verbatim");
        }

        [Test]
        public void ChunkDetailIsClearedWhenItLeavesFailed_DEF012()
        {
            var machine = new ChunkStateMachine();
            machine.TryTransition(ChunkState.Failed, "boom", out _);
            Assert.IsTrue(machine.TryTransition(ChunkState.Unloaded, out _));
            Assert.AreEqual(string.Empty, machine.FailureDetail, "a retry starts clean");
        }

        // ==================================================================== DEF-017 escrow

        [Test]
        public void BankingAtOneHubDoesNotDrainAnotherHubsEscrow_DEF017()
        {
            var economy = new EconomyService();
            economy.Escrow(75, "garage.canyon");   // earned in the canyon, bankable there
            economy.Escrow(40);                    // unattributed: bankable anywhere

            var bankedAtHome = economy.BankEscrow("garage.home");
            Assert.AreEqual(40, bankedAtHome, "the unattributed pool banks anywhere");
            Assert.AreEqual(75, economy.EscrowFor("garage.canyon"),
                "a hub-attributed reward is not swept by a bank somewhere else (§8.4/§12.2)");
            Assert.AreEqual(75, economy.EscrowedSalvage, "it is still pending");

            var bankedInCanyon = economy.BankEscrow("garage.canyon");
            Assert.AreEqual(75, bankedInCanyon);
            Assert.AreEqual(0, economy.EscrowedSalvage);
            Assert.AreEqual(115, economy.BankedSalvage);
        }

        [Test]
        public void HubEscrowSurvivesSaveLoad_DEF017()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var session = CampaignTestKit.NewGame(new SaveService(root));
                session.Economy.Escrow(120, "garage.city");
                session.Economy.Escrow(30);
                Assert.IsTrue(session.SaveNow().Success);

                var reloaded = CampaignTestKit.Reload(root);
                Assert.AreEqual(150, reloaded.Economy.EscrowedSalvage, "total pending is preserved");
                Assert.AreEqual(120, reloaded.Economy.EscrowFor("garage.city"), "…and so is the hub split");
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        // ==================================================================== DEF-018 events

        [Test]
        public void EveryCommandKindRaisesACommittedEvent_DEF018()
        {
            var economy = new EconomyService();
            economy.Grant(5000);
            var inventory = new InventoryService();
            inventory.ConfigureContainer(Cargo, 12, 140f);
            inventory.ConfigureContainer(Storage, 999, 99999f);
            var research = new ResearchService(_ => true);
            var garage = new GarageService();
            var discovery = new DiscoveryService();
            var transactions = new TransactionService(economy, inventory, new TransactionDependencies
            {
                Research = research, Discovery = discovery, Garage = garage
            });

            var seen = new List<GameEvent>();
            transactions.OnCommitted += e => seen.Add(e);

            inventory.Add(Storage, new ItemStack(new ContentId("scrap_metal"), 3)); // 2 craft + 1 to sell
            inventory.Add(Storage, new ItemStack(new ContentId("polymer"), 1));
            inventory.Add(Cargo, new ItemStack(new ContentId("repair_kit"), 1));

            var step1 = transactions.Execute(new TransferItems(new TransactionId("t1"), Cargo, Storage,
                new ContentId("repair_kit"), 1));
            Assert.IsTrue(step1.Success, "transfer: " + step1);
            var step2 = transactions.Execute(new CraftItem(new TransactionId("t2"), new ContentId("recipe.repair_kit"),
                Storage, Cargo));
            Assert.IsTrue(step2.Success, "craft: " + step2);
            var step3 = transactions.Execute(new SellItems(new TransactionId("t3"), Storage, new ContentId("scrap_metal"), 1));
            Assert.IsTrue(step3.Success, "sell: " + step3);
            var step4 = transactions.Execute(new UseItem(new TransactionId("t4"), Cargo, new ContentId("repair_kit"),
                new ContentId("engine")));
            Assert.IsTrue(step4.Success, "use: " + step4);
            var step5 = transactions.Execute(new RequestRecovery(new TransactionId("t5"),
                RequestRecovery.RecoveryKind.GentleReset));
            Assert.IsTrue(step5.Success, "recovery: " + step5);

            Assert.AreEqual(5, seen.Count, "one event per committed command");
            Assert.IsInstanceOf<ItemsTransferred>(seen[0]);
            Assert.IsInstanceOf<ItemCrafted>(seen[1]);
            Assert.IsInstanceOf<ItemsSold>(seen[2]);
            Assert.IsInstanceOf<ItemUsed>(seen[3]);
            Assert.IsInstanceOf<RecoveryRequested>(seen[4]);
        }

        // ==================================================================== DEF-020 bit grid

        [Test]
        public void BitGridReportsAShortOrMalformedPayload_DEF020()
        {
            var grid = new BitGrid(64, 64);
            grid.Set(3, 4, true);
            string good = grid.ToBase64();

            Assert.IsTrue(BitGrid.TryFromBase64(64, 64, good, out var restored, out string error), error);
            Assert.IsTrue(restored.Get(3, 4));

            Assert.IsFalse(BitGrid.TryFromBase64(64, 64, good.Substring(0, 8), out var shortGrid, out string shortError),
                "a truncated payload is no longer silently accepted");
            Assert.IsNotNull(shortError);
            Assert.IsNotNull(shortGrid, "the caller still gets a grid so a legacy save can load");

            Assert.IsFalse(BitGrid.TryFromBase64(64, 64, "not base 64!!", out _, out string badError), "malformed");
            Assert.IsNotNull(badError);

            // The legacy entry point never throws.
            Assert.DoesNotThrow(() => BitGrid.FromBase64(64, 64, "not base 64!!"));
        }

        // ==================================================================== DEF-021 inventory

        [Test]
        public void RemoveCanTargetOneMissionInstance_DEF021()
        {
            var inventory = new InventoryService();
            inventory.ConfigureContainer(Cargo, 12, 140f);

            var missionA = new ItemStack(new ContentId("quest.archive_case"), 1, "mission.Q10.instance1");
            var missionB = new ItemStack(new ContentId("quest.archive_case"), 1, "mission.Q10.instance2");
            Assert.IsTrue(inventory.Add(Cargo, missionA).Success);
            Assert.IsTrue(inventory.Add(Cargo, missionB).Success);

            Assert.AreEqual(2, inventory.QuantityOf(Cargo, "quest.archive_case"));
            Assert.AreEqual(1, inventory.QuantityOf(Cargo, "quest.archive_case", "mission.Q10.instance2"));

            var result = inventory.Remove(Cargo, new ContentId("quest.archive_case"), 1, "mission.Q10.instance2");
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, inventory.QuantityOf(Cargo, "quest.archive_case", "mission.Q10.instance2"),
                "the instance that was removed is gone");
            Assert.AreEqual(1, inventory.QuantityOf(Cargo, "quest.archive_case", "mission.Q10.instance1"),
                "the other mission's copy is untouched");
        }

        // ==================================================================== DEF-022 persistence

        [Test]
        public void ASaveStepFailureThatIsNotAnIoErrorStillReturnsFailedWrite_DEF022()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var saves = new SaveService(root);
                var session = CampaignTestKit.NewGame(saves);
                var payload = session.CaptureSnapshot();

                // A serialization/argument failure used to escape Save() entirely: the catch filters
                // covered IOException and UnauthorizedAccessException only.
                saves.StepHook = step =>
                {
                    if (step == SaveStep.Begin) throw new ArgumentException("dto cannot be serialized");
                };
                var result = saves.Save(new SaveSlotId(0), payload, 1);
                Assert.AreEqual(SaveResultStatus.FailedWrite, result.Status, "a failed save is a typed result");
                saves.StepHook = null;

                // A simulated process death is not a save failure — it must still reach the caller,
                // which is why the filter is a type list and not `catch (Exception)`.
                saves.StepHook = step =>
                {
                    if (step == SaveStep.Begin) throw new SimulatedCrashException(step);
                };
                Assert.Throws<SimulatedCrashException>(() => saves.Save(new SaveSlotId(0), payload, 2));
                saves.StepHook = null;
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        [Test]
        public void ASaveWrittenForAnotherSlotIsNotLoaded_DEF022()
        {
            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var saves = new SaveService(root);
                var session = CampaignTestKit.NewGame(saves, slot: 1);
                Assert.IsTrue(session.SaveNow().Success);

                var other = new SaveService(root).LoadBestValid(new SaveSlotId(2));
                Assert.AreEqual(LoadResultStatus.NoSave, other.status,
                    "slot 2 has no save of its own; slot 1's file must not be adopted");
            }
            finally { CampaignTestKit.DeleteRoot(root); }
        }

        // ==================================================================== DEF-024 weather

        [Test]
        public void WeatherTicksAtOneHertzInsteadOfDrifting_DEF024()
        {
            var weather = new WeatherSystem(1234);
            // 40 steps of 0.3 s = 12 s of simulated time → 12 ticks at 1 Hz. Resetting the
            // accumulator to zero discards the 0.2 s overshoot on every tick, which turns the period
            // into a whole number of steps (4) and yields 10 ticks in the same 12 s — a 17 % drift.
            for (int i = 0; i < 40; i++) weather.Step(0.3f);
            Assert.AreEqual(12, weather.TickCount, "the remainder is carried, not dropped");
        }

    }
}
