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
            if (!InGarage)
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
            if (!InGarage)
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

        /// <summary>Garage storage → cargo (loadout planning, §2.3 Plan).</summary>
        public CommandResult LoadCargo(string itemId, int quantity) => MoveItems(GarageStorage, Cargo, itemId, quantity);

        /// <summary>Cargo → garage storage.</summary>
        public CommandResult UnloadCargo(string itemId, int quantity) => MoveItems(Cargo, GarageStorage, itemId, quantity);

        CommandResult MoveItems(InventoryId from, InventoryId to, string itemId, int quantity)
        {
            if (!InGarage) return CommandResult.Fail(FailureReason.WrongState, "cargo can only be reorganised at a garage");
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
            if (!InGarage) return CommandResult.Fail(FailureReason.WrongState, "crafting happens at a garage");
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
    }
}
