using System;

namespace Wayfinder.Core
{
    /// <summary>
    /// Commands validate and request changes (§16.3). They are data; TransactionService executes
    /// them idempotently against a transaction ID.
    /// </summary>
    public abstract class GameCommand
    {
        public readonly TransactionId TransactionId;
        protected GameCommand(TransactionId transactionId) => TransactionId = transactionId;
    }

    public sealed class AcceptMission : GameCommand
    {
        public readonly ContentId MissionId;
        public AcceptMission(TransactionId tx, ContentId missionId) : base(tx) => MissionId = missionId;
    }

    public sealed class CompleteMissionObjective : GameCommand
    {
        public readonly ContentId MissionId;
        public readonly int ObjectiveIndex;
        public CompleteMissionObjective(TransactionId tx, ContentId missionId, int objectiveIndex) : base(tx)
        {
            MissionId = missionId;
            ObjectiveIndex = objectiveIndex;
        }
    }

    public sealed class TurnInMission : GameCommand
    {
        public readonly ContentId MissionId;
        public readonly ContentId HubId;
        public TurnInMission(TransactionId tx, ContentId missionId, ContentId hubId) : base(tx)
        {
            MissionId = missionId;
            HubId = hubId;
        }
    }

    public sealed class CollectPickup : GameCommand
    {
        public readonly ContentId PickupId; // e.g. cache.forest.017
        public readonly InventoryId Destination;
        public CollectPickup(TransactionId tx, ContentId pickupId, InventoryId destination) : base(tx)
        {
            PickupId = pickupId;
            Destination = destination;
        }
    }

    public sealed class UseItem : GameCommand
    {
        public readonly InventoryId Source;
        public readonly ContentId ItemId;
        public readonly ContentId TargetId; // subsystem, vehicle, slot…
        public UseItem(TransactionId tx, InventoryId source, ContentId itemId, ContentId targetId) : base(tx)
        {
            Source = source;
            ItemId = itemId;
            TargetId = targetId;
        }
    }

    public sealed class ResearchUpgrade : GameCommand
    {
        public readonly ContentId UpgradeId;
        public readonly ContentId VehicleId;
        public ResearchUpgrade(TransactionId tx, ContentId upgradeId, ContentId vehicleId) : base(tx)
        {
            UpgradeId = upgradeId;
            VehicleId = vehicleId;
        }
    }

    public sealed class EquipModule : GameCommand
    {
        public readonly ContentId VehicleId;
        public readonly ContentId ModuleId; // empty string = unequip slot
        public readonly string SlotId;
        public EquipModule(TransactionId tx, ContentId vehicleId, ContentId moduleId, string slotId) : base(tx)
        {
            VehicleId = vehicleId;
            ModuleId = moduleId;
            SlotId = slotId;
        }
    }

    public sealed class EstablishCamp : GameCommand
    {
        public readonly ContentId CampId;
        public readonly InventoryId KitSource;
        public EstablishCamp(TransactionId tx, ContentId campId, InventoryId kitSource) : base(tx)
        {
            CampId = campId;
            KitSource = kitSource;
        }
    }

    public sealed class BankCargo : GameCommand
    {
        /// <summary>Extraction / camp deposit. Converts salvage_bundle units to banked Salvage, escrow → wallet.</summary>
        public readonly ContentId HubId;
        public readonly InventoryId CargoSource;
        /// <summary>
        /// Hub storage that receives crafting materials (garage storage on extraction, camp storage
        /// on deposit). Default (empty) leaves materials in cargo.
        /// </summary>
        public readonly InventoryId MaterialDestination;
        public BankCargo(TransactionId tx, ContentId hubId, InventoryId cargoSource,
            InventoryId materialDestination = default) : base(tx)
        {
            HubId = hubId;
            CargoSource = cargoSource;
            MaterialDestination = materialDestination;
        }
    }

    public sealed class TransferItems : GameCommand
    {
        public readonly InventoryId From;
        public readonly InventoryId To;
        public readonly ContentId ItemId;
        public readonly int Quantity;
        public TransferItems(TransactionId tx, InventoryId from, InventoryId to, ContentId itemId, int quantity) : base(tx)
        {
            From = from;
            To = to;
            ItemId = itemId;
            Quantity = quantity;
        }
    }

    public sealed class CraftItem : GameCommand
    {
        public readonly ContentId RecipeId;
        public readonly InventoryId MaterialSource;
        public readonly InventoryId OutputDestination;
        public CraftItem(TransactionId tx, ContentId recipeId, InventoryId materialSource, InventoryId outputDestination)
            : base(tx)
        {
            RecipeId = recipeId;
            MaterialSource = materialSource;
            OutputDestination = outputDestination;
        }
    }

    public sealed class RequestRecovery : GameCommand
    {
        public enum RecoveryKind { GentleReset, FullRescue }
        public readonly RecoveryKind Kind;
        public RequestRecovery(TransactionId tx, RecoveryKind kind) : base(tx) => Kind = kind;
    }

    public sealed class PurchaseCosmetic : GameCommand
    {
        public readonly ContentId CosmeticId;
        public PurchaseCosmetic(TransactionId tx, ContentId cosmeticId) : base(tx) => CosmeticId = cosmeticId;
    }

    /// <summary>Unlocks a known garage (§12.1). Garages are free — no Salvage is spent (§11.4).</summary>
    public sealed class UnlockGarage : GameCommand
    {
        public readonly ContentId GarageId;
        public UnlockGarage(TransactionId tx, ContentId garageId) : base(tx) => GarageId = garageId;
    }

    /// <summary>Accepts an optional contract from the board (§9.3).</summary>
    public sealed class AcceptContract : GameCommand
    {
        public readonly ContentId ContractId;
        public AcceptContract(TransactionId tx, ContentId contractId) : base(tx) => ContractId = contractId;
    }

    /// <summary>
    /// Completes an accepted contract: claims <paramref name="RewardKey"/> exactly once and escrows
    /// <paramref name="RewardSalvage"/> until the next banking (§9.3, §16.3).
    /// </summary>
    public sealed class CompleteContract : GameCommand
    {
        public readonly ContentId ContractId;
        public readonly int RewardSalvage;
        public readonly string RewardKey;
        public CompleteContract(TransactionId tx, ContentId contractId, int rewardSalvage, string rewardKey) : base(tx)
        {
            ContractId = contractId;
            RewardSalvage = rewardSalvage;
            RewardKey = rewardKey ?? string.Empty;
        }
    }

    public sealed class SellItems : GameCommand
    {
        public readonly InventoryId Source;
        public readonly ContentId ItemId;
        public readonly int Quantity;
        public SellItems(TransactionId tx, InventoryId source, ContentId itemId, int quantity) : base(tx)
        {
            Source = source;
            ItemId = itemId;
            Quantity = quantity;
        }
    }
}
