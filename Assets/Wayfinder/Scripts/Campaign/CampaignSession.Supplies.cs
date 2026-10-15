using System;
using System.Globalization;
using Wayfinder.Core;
using Wayfinder.Progression;
using Wayfinder.Simulation;

namespace Wayfinder.Campaign
{
    /// <summary>What a fuel can would do right now (§10.3 "confirm if some of the can will be wasted").</summary>
    public readonly struct FuelCanPreview
    {
        public readonly bool Usable;
        public readonly float LitersAdded;
        public readonly float LitersWasted;
        public readonly string Reason;

        public FuelCanPreview(bool usable, float added, float wasted, string reason)
        {
            Usable = usable;
            LitersAdded = added;
            LitersWasted = wasted;
            Reason = reason ?? string.Empty;
        }

        public bool NeedsWasteConfirmation => Usable && LitersWasted > CampaignSession.FuelWasteEpsilonLiters;
    }

    /// <summary>
    /// Field supplies (§5.5, §5.6 "Field supplies", §10.3) and selling (§10.4). Supplies are used from
    /// cargo during an expedition, cost exactly one item per successful use (an idempotent
    /// <see cref="UseItem"/> transaction) and are field progress (debounced autosave, D-020). Selling
    /// happens at a garage, changes banked Salvage and is therefore a critical, verified save.
    /// </summary>
    public sealed partial class CampaignSession
    {
        public const string FuelCanId = "fuel_can";
        public const string RepairKitId = "repair_kit";
        public const string TirePatchId = "tire_patch";
        /// <summary>§10.3: "Require missing fuel ≥ 1 L".</summary>
        public const float FuelCanMinMissingLiters = 1f;
        public const float FuelWasteEpsilonLiters = 0.05f;
        /// <summary>§5.5 "while stopped".</summary>
        public const float SupplyMaxSpeedKph = 1f;

        // ------------------------------------------------------------------ fuel can

        public FuelCanPreview PreviewFuelCan()
        {
            if (!Expedition.IsActive) return new FuelCanPreview(false, 0f, 0f, "the garage refuels for free");
            if (Inventory.QuantityOf(Cargo, FuelCanId) <= 0) return new FuelCanPreview(false, 0f, 0f, "no fuel can in cargo");
            float missing = FuelCapacityLiters - FuelLiters;
            if (missing < FuelCanMinMissingLiters)
                return new FuelCanPreview(false, 0f, 0f, "the tank is nearly full (needs at least 1 L of space)");
            float after = FuelModel.ApplyFuelCan(FuelLiters, FuelCapacityLiters, out float wasted);
            return new FuelCanPreview(true, after - FuelLiters, wasted, string.Empty);
        }

        /// <summary>
        /// Adds <c>min(6, capacity − fuel)</c> litres and consumes one can. When part of the can would
        /// be wasted the caller must confirm (<paramref name="acceptWaste"/>), otherwise
        /// <see cref="FailureReason.Cancelled"/> is returned and nothing changes.
        /// </summary>
        public CommandResult UseFuelCan(bool acceptWaste = false)
        {
            var preview = PreviewFuelCan();
            if (!preview.Usable)
                return CommandResult.Fail(Expedition.IsActive
                    ? (Inventory.QuantityOf(Cargo, FuelCanId) <= 0 ? FailureReason.ItemNotFound : FailureReason.ItemNotUsableHere)
                    : FailureReason.WrongState, preview.Reason);
            if (preview.NeedsWasteConfirmation && !acceptWaste)
                return CommandResult.Fail(FailureReason.Cancelled, string.Format(CultureInfo.InvariantCulture,
                    "{0:0.0} L of the can would be wasted — confirm to use it anyway", preview.LitersWasted));
            var result = Transactions.Execute(new UseItem(NextTransactionId(), Cargo, new ContentId(FuelCanId),
                new ContentId("vehicle.fuel")));
            if (!result.Success) return result;
            FuelLiters = MathUtil.Clamp(FuelLiters + preview.LitersAdded, 0f, FuelCapacityLiters);
            Notice(CampaignNoticeKind.SupplyUsed, string.Format(CultureInfo.InvariantCulture, "Fuel can: +{0:0.0} L", preview.LitersAdded),
                0, FuelCanId);
            _autosave.MarkDirty();
            return result;
        }

        // ------------------------------------------------------------------ repair kit / tire patch

        /// <summary>
        /// Field repair kit: +35 to one chosen subsystem, only while stopped and out of water (§5.5).
        /// Refuses a subsystem already at 100 so a kit is never wasted. Repairing the engine ends a
        /// water-stall incident (the vehicle is out of the water by rule).
        /// </summary>
        public CommandResult UseRepairKit(HitZone subsystem, float speedKph, bool inWater)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "garage repairs are free");
            if (subsystem == HitZone.Underbody || !Enum.IsDefined(typeof(HitZone), subsystem))
                return CommandResult.Fail(FailureReason.InvalidTarget, "choose engine, suspension, tires or body");
            if (speedKph > SupplyMaxSpeedKph) return CommandResult.Fail(FailureReason.VehicleMoving, "stop the vehicle to repair");
            if (inWater) return CommandResult.Fail(FailureReason.ItemNotUsableHere, "get out of the water before repairing");
            if (Condition(subsystem) >= 100)
                return CommandResult.Fail(FailureReason.ItemNotUsableHere, $"{SubsystemName(subsystem)} is already at full condition");
            if (Inventory.QuantityOf(Cargo, RepairKitId) <= 0) return CommandResult.Fail(FailureReason.ItemNotFound, "no repair kit in cargo");
            var result = Transactions.Execute(new UseItem(NextTransactionId(), Cargo, new ContentId(RepairKitId),
                new ContentId("subsystem." + SubsystemName(subsystem))));
            if (!result.Success) return result;
            int before = Condition(subsystem);
            Damage.UseRepairKit(subsystem);
            if (subsystem == HitZone.Engine) Damage.EndWaterIncident();
            Notice(CampaignNoticeKind.SupplyUsed, $"Repair kit: {SubsystemName(subsystem)} {before} → {Condition(subsystem)}", 0, RepairKitId);
            _autosave.MarkDirty();
            return result;
        }

        /// <summary>Tire patch removes the puncture (25 km/h cap); only while stopped (§5.5, §10.3).</summary>
        public CommandResult UseTirePatch(float speedKph)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "garage repairs are free");
            if (speedKph > SupplyMaxSpeedKph) return CommandResult.Fail(FailureReason.VehicleMoving, "stop the vehicle to patch a tire");
            if (!Damage.HasPuncture) return CommandResult.Fail(FailureReason.ItemNotUsableHere, "no puncture to patch");
            if (Inventory.QuantityOf(Cargo, TirePatchId) <= 0) return CommandResult.Fail(FailureReason.ItemNotFound, "no tire patch in cargo");
            var result = Transactions.Execute(new UseItem(NextTransactionId(), Cargo, new ContentId(TirePatchId),
                new ContentId("subsystem.tires")));
            if (!result.Success) return result;
            Damage.UseTirePatch();
            Notice(CampaignNoticeKind.SupplyUsed, "Tire patched — the 25 km/h limit is lifted", 0, TirePatchId);
            _autosave.MarkDirty();
            return result;
        }

        public int Condition(HitZone subsystem)
        {
            switch (subsystem)
            {
                case HitZone.Engine: return Damage.EngineCondition;
                case HitZone.Suspension: return Damage.SuspensionCondition;
                case HitZone.Tires: return Damage.TiresCondition;
                case HitZone.Body: return Damage.BodyCondition;
                default: return 100;
            }
        }

        public static string SubsystemName(HitZone subsystem) => subsystem.ToString().ToLowerInvariant();

        // ------------------------------------------------------------------ selling (§10.4)

        /// <summary>
        /// Whether <paramref name="quantity"/> of an item may be sold from garage storage (or cargo)
        /// right now, and for how much. Rules: only at a garage; never Salvage bundles (they bank 1:1),
        /// protected ledger objects or mission cargo; never the camp kits a pending
        /// <c>EstablishCamp</c> objective still needs.
        /// </summary>
        public CommandResult CheckSell(string itemId, int quantity, bool fromCargo, out int totalPrice)
        {
            totalPrice = 0;
            if (!InGarage) return CommandResult.Fail(FailureReason.WrongState, "sell at a garage");
            if (quantity <= 0) return CommandResult.Fail(FailureReason.InvalidTarget, "quantity must be positive");
            if (string.IsNullOrEmpty(itemId) || !ItemCatalog.TryGet(itemId, out var def))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown item '{itemId}'");
            var category = ItemCatalog.CategoryOf(itemId);
            if (category == ItemCategory.Currency)
                return CommandResult.Fail(FailureReason.ItemNotUsableHere, "Salvage bundles bank automatically at 1:1");
            if (category == ItemCategory.ProtectedLedger || category == ItemCategory.MissionObject || !def.Sellable)
                return CommandResult.Fail(FailureReason.ItemNotUsableHere, "mission and protected items cannot be sold");
            int unit = ItemCatalog.SellPrice(itemId);
            if (unit <= 0) return CommandResult.Fail(FailureReason.ItemNotUsableHere, "worth nothing to sell");
            var source = fromCargo ? Cargo : GarageStorage;
            int have = Inventory.QuantityOf(source, itemId);
            if (have < quantity)
                return CommandResult.Fail(FailureReason.ItemNotFound, $"only {have} in {(fromCargo ? "cargo" : "garage storage")}");
            int reserved = ReservedForMissions(itemId);
            int total = Inventory.QuantityOf(Cargo, itemId) + Inventory.QuantityOf(GarageStorage, itemId);
            if (total - quantity < reserved)
                return CommandResult.Fail(FailureReason.ItemNotUsableHere, $"keep {reserved} for an active mission");
            totalPrice = unit * quantity;
            return CommandResult.Ok();
        }

        /// <summary>Sells at 40 % of purchase price, rounded down per unit (§10.4). Critical: saved before success.</summary>
        public CommandResult SellItems(string itemId, int quantity, bool fromCargo = false)
        {
            var check = CheckSell(itemId, quantity, fromCargo, out int price);
            if (!check.Success) return check;
            return RunCritical(() =>
            {
                var result = Transactions.Execute(new SellItems(NextTransactionId(), fromCargo ? Cargo : GarageStorage,
                    new ContentId(itemId), quantity));
                if (result.Success)
                    Notice(CampaignNoticeKind.ItemSold, $"Sold {quantity} × {DisplayNames.Item(itemId)} for {price} Salvage", price, itemId);
                return result;
            });
        }

        /// <summary>Camp kits still needed by pending EstablishCamp objectives of active missions.</summary>
        int ReservedForMissions(string itemId)
        {
            if (itemId != ItemCatalog.CampKit) return 0;
            int reserved = 0;
            foreach (var code in Missions.CodesInState(MissionState.Active))
            {
                var def = Missions.Definitions[code];
                var counters = Missions.ObjectiveCounters(code);
                for (int i = 0; i < def.Objectives.Count && i < counters.Length; i++)
                    if (def.Objectives[i].Type == MissionConditionType.EstablishCamp && counters[i] < def.Objectives[i].Count)
                        reserved += def.Objectives[i].Count - counters[i];
            }
            return reserved;
        }
    }
}
