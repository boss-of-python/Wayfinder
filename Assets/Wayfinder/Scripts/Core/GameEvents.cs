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
