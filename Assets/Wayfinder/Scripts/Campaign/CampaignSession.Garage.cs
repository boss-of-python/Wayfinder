using System;
using System.Collections.Generic;
using System.Globalization;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;

namespace Wayfinder.Campaign
{
    /// <summary>Garage-side actions (§2.3 Plan, §12.1): missions, research, loadout, crafting, departure.</summary>
    public sealed partial class CampaignSession
    {
        // ================================================================== garage actions

        public CommandResult AcceptMission(string code)
        {
            if (string.IsNullOrEmpty(code) || !Missions.Definitions.TryGetValue(code, out var def))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown mission '{code}'");
            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState, "missions are accepted at a garage");
            return RunCritical(() =>
            {
                var accept = Missions.Accept(code, WorldProgress);
                if (!accept.Success) return accept;
                foreach (var (itemId, quantity, destination) in def.OnAcceptGrant)
                {
                    if (!ItemCatalog.TryGet(itemId, out _))
                        return CommandResult.Fail(FailureReason.ContentMissing, $"grant item '{itemId}' unknown");
                    var target = destination == "garageStorage" ? GarageStorage : Cargo;
                    var add = Inventory.Add(target, new ItemStack(new ContentId(itemId), quantity));
                    if (!add.Success) return add;
                }
                Notice(CampaignNoticeKind.MissionAccepted, $"{code} accepted", 0, code);
                AnnounceReadyMissions(new List<string>());
                return CommandResult.Ok();
            });
        }

        public CommandResult ResearchUpgrade(string upgradeId)
        {
            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState, "research is only available at a garage (§12.2)");
            if (!IsKnownUpgrade(upgradeId))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown upgrade '{upgradeId}'");
            return RunCritical(() =>
            {
                var result = Transactions.Execute(new ResearchUpgrade(NextTransactionId(), new ContentId(upgradeId),
                    new ContentId(ActiveVehicleId)));
                if (!result.Success) return result;
                ConfigureContainers();
                FuelLiters = FuelCapacityLiters; // garages refuel for free (§12.1)
                return result;
            });
        }

        // ================================================================== contract board (§9.3)

        /// <summary>
        /// Contracts the board offers: at most <see cref="ContractService.MaxAvailableJobs"/>, never
        /// one already accepted or completed. Deterministic order (content order, then id) so two
        /// players at the same point see the same board (DEF-007).
        /// </summary>
        public List<ContractDefinition> AvailableContracts(string biome = null)
        {
            var offers = new List<ContractDefinition>();
            foreach (var def in Content.Contracts.Values)
            {
                if (Contracts.IsAccepted(def.Id) || Contracts.IsCompleted(def.Id)) continue;
                if (!string.IsNullOrEmpty(biome) && !string.Equals(def.Biome, biome, StringComparison.Ordinal)) continue;
                offers.Add(def);
            }
            offers.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (offers.Count > ContractService.MaxAvailableJobs)
                offers.RemoveRange(ContractService.MaxAvailableJobs, offers.Count - ContractService.MaxAvailableJobs);
            return offers;
        }

        /// <summary>Accepts a contract at a garage board. Free; the reward is escrowed on completion.</summary>
        public CommandResult AcceptContract(string contractId)
        {
            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState, "contracts are accepted at a garage board (§9.3)");
            if (string.IsNullOrEmpty(contractId) || !Content.Contracts.ContainsKey(contractId))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown contract '{contractId}'");
            return RunCritical(() =>
            {
                var result = Transactions.Execute(new AcceptContract(NextTransactionId(), new ContentId(contractId)));
                if (!result.Success) return result;
                Notice(CampaignNoticeKind.ContractAccepted, $"Contract accepted: {contractId}", 0, contractId);
                _autosave.MarkDirty();
                return result;
            });
        }

        /// <summary>
        /// Completes an accepted contract. The reward goes to <b>escrow</b>, banked with the next
        /// extraction — never straight to the wallet (§9.3, DEF-007).
        /// </summary>
        public CommandResult CompleteContract(string contractId, float validatedRouteLengthMeters = 0f)
        {
            if (!Expedition.IsActive && !InGarage)
                return CommandResult.Fail(FailureReason.WrongState, "no expedition and no garage session");
            if (string.IsNullOrEmpty(contractId) || !Content.Contracts.TryGetValue(contractId, out var def))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown contract '{contractId}'");
            int reward = string.Equals(def.Pattern, "Repeatable", StringComparison.Ordinal)
                ? ContractService.RepeatableReward(validatedRouteLengthMeters)
                : def.RewardSalvage;
            if (reward <= 0)
                return CommandResult.Fail(FailureReason.ContentMissing, $"contract '{contractId}' has no reward");
            return RunCritical(() =>
            {
                var result = Transactions.Execute(new CompleteContract(NextTransactionId(), new ContentId(contractId),
                    reward, def.RewardKey));
                if (!result.Success) return result;
                Notice(CampaignNoticeKind.ContractCompleted, $"Contract complete: +{reward} Salvage pending", reward, contractId);
                _autosave.MarkDirty();
                return result;
            });
        }

        /// <summary>Garage storage → cargo (loadout planning, §2.3 Plan).</summary>
        public CommandResult LoadCargo(string itemId, int quantity) => MoveItems(GarageStorage, Cargo, itemId, quantity);

        /// <summary>Cargo → garage storage.</summary>
        public CommandResult UnloadCargo(string itemId, int quantity) => MoveItems(Cargo, GarageStorage, itemId, quantity);

        CommandResult MoveItems(InventoryId from, InventoryId to, string itemId, int quantity)
        {
            if (!AtGarage) return CommandResult.Fail(FailureReason.WrongState, "cargo can only be reorganised at a garage");
            if (string.IsNullOrEmpty(itemId) || !ItemCatalog.TryGet(itemId, out var def))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown item '{itemId}'");
            if (def.ProtectedLedger) return CommandResult.Fail(FailureReason.ItemNotUsableHere, "protected objects never move");
            var result = Transactions.Execute(new TransferItems(NextTransactionId(), from, to, new ContentId(itemId), quantity));
            if (result.Success) _autosave.MarkDirty();
            return result;
        }

        /// <summary>Crafts at the garage from garage storage into garage storage, paying the recipe fee.</summary>
        public CommandResult Craft(string recipeId)
        {
            if (!AtGarage) return CommandResult.Fail(FailureReason.WrongState, "crafting happens at a garage");
            bool known = false;
            foreach (var r in CraftingService.Recipes)
                if (r.Id.Value == recipeId) known = true;
            if (!known) return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown recipe '{recipeId}'");
            return RunCritical(() =>
            {
                var result = Transactions.Execute(new CraftItem(NextTransactionId(), new ContentId(recipeId), GarageStorage, GarageStorage));
                if (result.Success) Notice(CampaignNoticeKind.ItemCrafted, $"Crafted {recipeId.Replace("recipe.", "")}", 0, recipeId);
                return result;
            });
        }

        /// <summary>Depart (§2.3): snapshot the garage + loadout and start a new expedition id. Saved immediately.</summary>
        public CommandResult Depart()
        {
            if (Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "an expedition is already active");
            if (Inventory.IsOverCapacity(Cargo))
                return CommandResult.Fail(FailureReason.OverCapacity, "cargo exceeds capacity; unload before departing");
            return RunCritical(() =>
            {
                Expedition.Id = "exp-" + NextSequence.ToString(CultureInfo.InvariantCulture);
                NextSequence++;
                Expedition.Status = ExpeditionStatus.Active;
                Expedition.Pose = SpawnFor(Expedition.LastGarageId);
                Expedition.LastSafePose = Expedition.Pose;
                Expedition.ElapsedSimulationSeconds = 0d;
                Expedition.Stats = new ExpeditionStats();
                Notice(CampaignNoticeKind.Info, "Expedition started");
                return CommandResult.Ok();
            });
        }

        // ================================================================== equipment slots (§10.2)

        /// <summary>
        /// Fits a researched module into its §10.2 slot — "vehicle change" is a garage service
        /// (§10.4). A named slot holds one module, so fitting replaces its occupant; Utility holds
        /// two. Critical: the new loadout is acknowledged only with a verified save, and a swap that
        /// would leave the loaded cargo over capacity is refused and rolled back (QA case 9).
        /// </summary>
        public CommandResult EquipModule(string upgradeId) => ChangeLoadout(upgradeId, fit: true);

        /// <summary>
        /// Removes a fitted module. It stays researched and can be fitted again at any garage, so
        /// unequipping never destroys progression (§17.5). Same garage and capacity rules as fitting.
        /// </summary>
        public CommandResult UnequipModule(string upgradeId) => ChangeLoadout(upgradeId, fit: false);

        CommandResult ChangeLoadout(string upgradeId, bool fit)
        {
            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState,
                    "vehicle changes are only available at a garage (§10.4)");
            if (!IsKnownUpgrade(upgradeId))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown upgrade '{upgradeId}'");
            return RunCritical(() =>
            {
                var result = fit ? Research.Fit(new ContentId(upgradeId)) : Research.Unfit(new ContentId(upgradeId));
                if (!result.Success) return result;
                ConfigureContainers();
                if (Inventory.IsOverCapacity(Cargo))
                    return CommandResult.Fail(FailureReason.OverCapacity,
                        "the loaded cargo no longer fits that rack; unload first or keep the larger one (QA case 9)");
                // A smaller tank must never leave the vehicle over full (§5.4). Garages refuel for
                // free (§12.1), so clamping down costs the player nothing.
                FuelLiters = Math.Min(FuelLiters, FuelCapacityLiters);
                Notice(fit ? CampaignNoticeKind.ModuleFitted : CampaignNoticeKind.ModuleRemoved,
                    (fit ? "Fitted " : "Removed ") + upgradeId, 0, upgradeId);
                return CommandResult.Ok();
            });
        }

        /// <summary>
        /// Mounts a tire set (§10.2: one tire set, mutually exclusive — "meaningful terrain
        /// preparation"). Requires the research that unlocks it (§11.2 tiers 8, 13, 18) and a garage.
        /// </summary>
        public CommandResult SelectTireSet(TireSet tireSet)
        {
            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState, "tire sets are mounted at a garage (§10.4)");
            return RunCritical(() =>
            {
                var result = Research.SelectTireSet(tireSet);
                if (!result.Success) return result;
                Notice(CampaignNoticeKind.TireSetMounted, $"Mounted {tireSet} tires", 0, tireSet.ToString());
                return CommandResult.Ok();
            });
        }

        // ================================================================== community logistics (§10.4)

        /// <summary>
        /// The store a hub keeps property in (§10.4). Every garage shares the ONE unlimited garage
        /// store; each camp has its own store sized by its content (D-039). Null when the id is not an
        /// activated hub, so a caller can never address a store the player has not earned.
        /// </summary>
        public InventoryId? HubStore(string hubId)
        {
            if (string.IsNullOrEmpty(hubId)) return null;
            if (Garage.IsGarageUnlocked(hubId)) return GarageStorage;
            if (Garage.IsCampActivated(hubId)) return CampStorageFor(hubId);
            return null;
        }

        /// <summary>
        /// The activated hubs property may be exchanged with, in a deterministic order (garages then
        /// camps, each sorted) so a UI list and a canonical capture agree.
        /// </summary>
        public List<string> LogisticsHubs()
        {
            var hubs = new List<string>(Garage.UnlockedGarages);
            hubs.Sort(StringComparer.Ordinal);
            var camps = new List<string>(Garage.ActivatedCamps);
            camps.Sort(StringComparer.Ordinal);
            hubs.AddRange(camps);
            return hubs;
        }

        /// <summary>
        /// Sends items from the store of the hub the player is at to another activated hub's store —
        /// §10.4 "version 1 allows free instant transfer among activated hubs while the player is at a
        /// hub; no field access". Free, instant, and committed only with a verified save.
        /// </summary>
        public CommandResult TransferToHub(string destinationHubId, string itemId, int quantity) =>
            TransferBetweenHubs(CurrentHubId, destinationHubId, itemId, quantity);

        /// <summary>
        /// Pulls items from another activated hub's store into the store of the hub the player is at.
        /// This is the only way property stored elsewhere becomes reachable: §10.4 is explicit that it
        /// is "not magically accessible for field repairs".
        /// </summary>
        public CommandResult TransferFromHub(string sourceHubId, string itemId, int quantity) =>
            TransferBetweenHubs(sourceHubId, CurrentHubId, itemId, quantity);

        CommandResult TransferBetweenHubs(string fromHubId, string toHubId, string itemId, int quantity)
        {
            if (string.IsNullOrEmpty(itemId) || !ItemCatalog.TryGet(itemId, out var def))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown item '{itemId}'");
            if (def.ProtectedLedger)
                return CommandResult.Fail(FailureReason.ItemNotUsableHere, "protected objects never move (§10.4)");
            if (quantity <= 0)
                return CommandResult.Fail(FailureReason.InvalidTarget, "nothing to transfer");

            // No field access: the gate is the progression service's own §10.4 rule.
            string here = CurrentHubId;
            var atHub = Garage.TryLogisticsTransfer(here);
            if (!atHub.Success) return atHub;

            string other = string.Equals(fromHubId, here, StringComparison.Ordinal) ? toHubId : fromHubId;
            if (string.IsNullOrEmpty(other))
                return CommandResult.Fail(FailureReason.InvalidTarget, "no hub selected");
            if (string.Equals(other, here, StringComparison.Ordinal))
                return CommandResult.Fail(FailureReason.WrongState, "already at that hub");
            // One activation guard, immediately before the dereference it protects: `other` is always
            // one of fromHubId/toHubId, and HubStore(here) cannot be null once the at-hub gate passed.
            var from = HubStore(fromHubId);
            var to = HubStore(toHubId);
            if (from == null || to == null)
                return CommandResult.Fail(FailureReason.InvalidTarget, "that hub is not activated (§10.4)");
            if (from.Value.Equals(to.Value))
                return CommandResult.Fail(FailureReason.WrongState,
                    "every garage shares one unlimited store (§10.4), so there is nothing to move between two garages");

            return RunCritical(() =>
            {
                // Idempotent and defensive: an activated camp's store is already sized, but a transfer
                // must never depend on which command configured it last.
                if (Garage.IsCampActivated(fromHubId)) ConfigureCampStore(fromHubId);
                if (Garage.IsCampActivated(toHubId)) ConfigureCampStore(toHubId);
                var result = Transactions.Execute(new TransferItems(NextTransactionId(), from.Value, to.Value,
                    new ContentId(itemId), quantity));
                if (result.Success)
                    Notice(CampaignNoticeKind.LogisticsTransferred,
                        $"Community logistics: {quantity} × {itemId} to {toHubId}", quantity, toHubId);
                return result;
            });
        }

        // ================================================================== hub travel (§10.4)

        /// <summary>
        /// The hub the vehicle is at, or null (§10.4). In garage mode that is the hub the session is
        /// parked at; in the field it is an activated camp whose socket contains the current pose.
        /// Hub travel is offered only from a hub, so this is the gate the UI needs.
        /// </summary>
        public string CurrentHubId => Expedition.IsActive ? CampAtPose(Expedition.Pose.Position) : Expedition.LastGarageId;

        /// <summary>The nearest activated camp whose socket radius contains the position, else null.</summary>
        string CampAtPose(WorldPoint position)
        {
            string best = null;
            float bestDistance = float.MaxValue;
            foreach (var socket in Content.Slice.CampSockets)
            {
                if (!Garage.IsCampActivated(socket.Id)) continue;
                float distance = position.DistanceXZ(socket.Position);
                if (distance <= socket.RadiusMeters && distance < bestDistance)
                {
                    best = socket.Id;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// Safe-hub travel (§10.4): a free, instant transfer to another activated hub, available only
        /// while at an activated garage or camp and never out of a dangerous field situation — full
        /// rescue fills that role. No currency fee and no loading-screen punishment. Loaded cargo
        /// travels with the vehicle ("bank/deposit first" is the player's choice, not a gate).
        /// Critical operation: the move is acknowledged only with a verified save (§12.4).
        /// </summary>
        /// <param name="destinationHubId">An activated garage or camp that offers hub travel.</param>
        /// <param name="inWater">Field state from the caller, as for the supply actions.</param>
        /// <param name="inDeepMud">Field state from the caller, as for the supply actions.</param>
        public CommandResult TravelToHub(string destinationHubId, bool inWater, bool inDeepMud)
        {
            if (string.IsNullOrEmpty(destinationHubId))
                return CommandResult.Fail(FailureReason.InvalidTarget, "no destination hub selected");
            string origin = CurrentHubId;
            if (string.IsNullOrEmpty(origin))
                return CommandResult.Fail(FailureReason.WrongState,
                    "hub travel is only available at an activated garage or camp");
            if (inWater || inDeepMud)
                return CommandResult.Fail(FailureReason.WrongState,
                    "no fast travel out of a dangerous field situation; use full rescue");
            if (string.Equals(origin, destinationHubId, StringComparison.Ordinal))
                return CommandResult.Fail(FailureReason.WrongState, "already at that hub");
            var gate = Garage.TryHubTravel(origin, destinationHubId);
            if (!gate.Success) return gate;
            if (!Content.OffersHubTravel(origin) || !Content.OffersHubTravel(destinationHubId))
                return CommandResult.Fail(FailureReason.InvalidTarget, "that hub does not offer safe-hub travel (§10.4)");

            bool onExpedition = Expedition.IsActive;
            Pose destination = SpawnFor(destinationHubId);
            return RunCritical(() =>
            {
                Expedition.Pose = destination;
                Expedition.LastSafePose = destination;
                // Parked at a hub, the hub is also the next departure point (§2.3 Plan). Mid-expedition
                // the home garage stays the extraction target, so LastGarageId must not move (§12.4).
                if (!onExpedition) Expedition.LastGarageId = destinationHubId;
                Notice(CampaignNoticeKind.HubTravelled, $"Arrived at {destinationHubId}", 0, destinationHubId);
                return CommandResult.Ok();
            });
        }
    }
}
