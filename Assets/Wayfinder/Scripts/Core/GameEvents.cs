using System;

namespace Wayfinder.Core
{
    /// <summary>
    /// Events report committed outcomes only (§16.3). Every permanent reward event carries a
    /// stable source key and transaction ID so rewards are idempotent across replay/restore.
    /// </summary>
    public abstract class GameEvent
    {
        public readonly TransactionId TransactionId;
        protected GameEvent(TransactionId transactionId) => TransactionId = transactionId;
    }

    public sealed class PickupCollected : GameEvent
    {
        public readonly ContentId PickupId;
        public readonly int SalvageUnits;
        public PickupCollected(TransactionId tx, ContentId pickupId, int salvageUnits) : base(tx)
        {
            PickupId = pickupId;
            SalvageUnits = salvageUnits;
        }
    }

    public sealed class LandmarkDiscovered : GameEvent
    {
        public readonly ContentId LandmarkId;
        public readonly string RewardKey;
        public readonly int SalvageReward;
        public LandmarkDiscovered(TransactionId tx, ContentId landmarkId, string rewardKey, int salvageReward) : base(tx)
        {
            LandmarkId = landmarkId;
            RewardKey = rewardKey;
            SalvageReward = salvageReward;
        }
    }

    /// <summary>A recipe was crafted (§12.3). Emitted only after the commit succeeded.</summary>
    public sealed class ItemCrafted : GameEvent
    {
        public readonly ContentId RecipeId;
        public ItemCrafted(TransactionId tx, ContentId recipeId) : base(tx) => RecipeId = recipeId;
    }

    /// <summary>A stack moved between two containers.</summary>
    public sealed class ItemsTransferred : GameEvent
    {
        public readonly InventoryId From;
        public readonly InventoryId To;
        public readonly ContentId ItemId;
        public readonly int Quantity;
        public ItemsTransferred(TransactionId tx, InventoryId from, InventoryId to, ContentId itemId, int quantity) : base(tx)
        {
            From = from; To = to; ItemId = itemId; Quantity = quantity;
        }
    }

    /// <summary>Items sold at 40 % of purchase price (§10.4). <c>SalvageGained</c> is the wallet delta.</summary>
    public sealed class ItemsSold : GameEvent
    {
        public readonly ContentId ItemId;
        public readonly int Quantity;
        public readonly int SalvageGained;
        public ItemsSold(TransactionId tx, ContentId itemId, int quantity, int salvageGained) : base(tx)
        {
            ItemId = itemId; Quantity = quantity; SalvageGained = salvageGained;
        }
    }

    /// <summary>One field-supply item was consumed (§5.6).</summary>
    public sealed class ItemUsed : GameEvent
    {
        public readonly ContentId ItemId;
        public readonly InventoryId Source;
        public ItemUsed(TransactionId tx, ContentId itemId, InventoryId source) : base(tx)
        {
            ItemId = itemId; Source = source;
        }
    }

    /// <summary>Recovery was requested (REC-01). The session applies the rescue rules.</summary>
    public sealed class RecoveryRequested : GameEvent
    {
        public RecoveryRequested(TransactionId tx) : base(tx) { }
    }

    public sealed class MapCellsRevealed : GameEvent
    {
        public readonly int NewlyRevealed;
        public readonly int TotalRevealed;
        public MapCellsRevealed(TransactionId tx, int newlyRevealed, int totalRevealed) : base(tx)
        {
            NewlyRevealed = newlyRevealed;
            TotalRevealed = totalRevealed;
        }
    }

    public sealed class MissionCompleted : GameEvent
    {
        public readonly ContentId MissionId;
        public readonly string RewardKey;
        public MissionCompleted(TransactionId tx, ContentId missionId, string rewardKey) : base(tx)
        {
            MissionId = missionId;
            RewardKey = rewardKey;
        }
    }

    public sealed class UpgradeResearched : GameEvent
    {
        public readonly ContentId UpgradeId;
        public UpgradeResearched(TransactionId tx, ContentId upgradeId) : base(tx) => UpgradeId = upgradeId;
    }

    public sealed class ExpeditionBanked : GameEvent
    {
        public readonly string BankTransactionId;
        public readonly int SalvageBanked;
        public readonly int LooseSalvageLostToRescue;
        public ExpeditionBanked(TransactionId tx, string bankTransactionId, int salvageBanked, int lostToRescue)
            : base(tx)
        {
            BankTransactionId = bankTransactionId;
            SalvageBanked = salvageBanked;
            LooseSalvageLostToRescue = lostToRescue;
        }
    }

    public sealed class VehicleDisabled : GameEvent
    {
        public readonly ContentId VehicleId;
        public readonly string Cause;
        public VehicleDisabled(TransactionId tx, ContentId vehicleId, string cause) : base(tx)
        {
            VehicleId = vehicleId;
            Cause = cause;
        }
    }

    public sealed class CampEstablished : GameEvent
    {
        public readonly ContentId CampId;
        public CampEstablished(TransactionId tx, ContentId campId) : base(tx) => CampId = campId;
    }

    public sealed class ContractAccepted : GameEvent
    {
        public readonly ContentId ContractId;
        public ContractAccepted(TransactionId tx, ContentId contractId) : base(tx) => ContractId = contractId;
    }

    /// <summary>A contract was completed; <c>RewardSalvage</c> went to escrow, not to the wallet.</summary>
    public sealed class ContractCompleted : GameEvent
    {
        public readonly ContentId ContractId;
        public readonly int RewardSalvage;
        public ContractCompleted(TransactionId tx, ContentId contractId, int rewardSalvage) : base(tx)
        {
            ContractId = contractId;
            RewardSalvage = rewardSalvage;
        }
    }

    /// <summary>A garage was unlocked (§12.1). Free — no Salvage moves (DEF-003).</summary>
    public sealed class GarageUnlocked : GameEvent
    {
        public readonly ContentId GarageId;
        public GarageUnlocked(TransactionId tx, ContentId garageId) : base(tx) => GarageId = garageId;
    }

    public sealed class SaveFailed : GameEvent
    {
        public readonly string Detail;
        public SaveFailed(TransactionId tx, string detail) : base(tx) => Detail = detail;
    }

    public sealed class WarningRaised : GameEvent
    {
        public enum WarningKind
        {
            FuelThirtyPercent,
            FuelFifteenPercent,
            FuelReturnRangeInsufficient,
            RolloverRisk,
            DeepWaterAhead,
            IntakeLimitNear,
            EngineStalled,
            SubsystemDamaged,
            CargoNearFull,
            CargoFull,
            ObjectiveUpdated,
            CosmeticNotification
        }

        public readonly WarningKind Kind;
        public WarningRaised(TransactionId tx, WarningKind kind) : base(tx) => Kind = kind;
    }
}
