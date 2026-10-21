using System;
using System.Collections.Generic;
using System.Globalization;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;

namespace Wayfinder.Campaign
{
    public enum ExpeditionStatus
    {
        None,
        Active,
        Ended
    }

    public sealed class ExpeditionStats
    {
        public float DistanceMeters;
        public float FuelUsedLiters;
        public int CachesCollected;
        public int LandmarksDiscovered;
        public int CellsRevealed;

        public ExpeditionStats Clone() => (ExpeditionStats)MemberwiseClone();
    }

    /// <summary>Expedition lifecycle state (§2.3). Saved as ExpeditionDto.</summary>
    public sealed class ExpeditionState
    {
        public string Id = string.Empty;
        public ExpeditionStatus Status = ExpeditionStatus.None;
        public string LastGarageId = "garage.home";
        public Pose Pose;
        public Pose LastSafePose;
        public double ElapsedSimulationSeconds;
        public ExpeditionStats Stats = new ExpeditionStats();

        public bool IsActive => Status == ExpeditionStatus.Active;
    }

    public enum CampaignNoticeKind
    {
        Info,
        CacheCollected,
        LandmarkDiscovered,
        MappingReward,
        ObjectiveUpdated,
        MissionAccepted,
        MissionReady,
        MissionCompleted,
        Banked,
        CampEstablished,
        RelayActivated,
        UpgradeResearched,
        ItemCrafted,
        QuestItemCollected,
        RescueLoss,
        SaveFailed,
        SupplyUsed,
        ItemSold,
        GarageUnlocked,
        ContractAccepted,
        ContractCompleted,
        HubTravelled
    }

    /// <summary>Committed-outcome notification for the HUD queue (§13.2). Never emitted for rolled-back work.</summary>
    public readonly struct CampaignNotice
    {
        public readonly CampaignNoticeKind Kind;
        public readonly string Text;
        public readonly int Amount;
        public readonly string Id;

        public CampaignNotice(CampaignNoticeKind kind, string text, int amount = 0, string id = null)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            Amount = amount;
            Id = id ?? string.Empty;
        }

        public override string ToString() => $"{Kind}: {Text}";
    }

    /// <summary>Expedition summary (§2.3): banked totals, missions settled, service performed, stats.</summary>
    public sealed class ExpeditionSummary
    {
        public CommandResult Result;
        public string ExpeditionId = string.Empty;
        public string GarageId = string.Empty;
        public string BankTransactionId = string.Empty;
        public bool WasRescue;
        public int RescueSalvageLost;
        public int SalvageUnitsBanked;
        public int EscrowBanked;
        public int MaterialUnitsStored;
        public readonly List<string> MissionsCompleted = new List<string>();
        public int MissionRewardSalvage;
        public bool Refueled;
        public bool Repaired;
        public int WalletAfter;
        public ExpeditionStats Stats = new ExpeditionStats();

        public bool Success => Result.Success;
        public int TotalSalvageBanked => SalvageUnitsBanked + EscrowBanked + MissionRewardSalvage;
    }

    public enum CampaignOpenStatus
    {
        NewGameSaved,
        NewGameSaveFailed,
        LoadedCurrent,
        LoadedBackup,
        NoSave,
        SchemaTooNew,
        Failed
    }

    public sealed class CampaignOpenResult
    {
        public readonly CampaignSession Session;
        public readonly CampaignOpenStatus Status;
        public readonly string Detail;

        public CampaignOpenResult(CampaignSession session, CampaignOpenStatus status, string detail)
        {
            Session = session;
            Status = status;
            Detail = detail ?? string.Empty;
        }

        /// <summary>True when play may begin (a new game is only playable once its first save landed, §17.5).</summary>
        public bool Playable => Session != null &&
                                (Status == CampaignOpenStatus.NewGameSaved || Status == CampaignOpenStatus.LoadedCurrent ||
                                 Status == CampaignOpenStatus.LoadedBackup);
    }

    /// <summary>Title-screen slot card (§13.1). Read without constructing a session.</summary>
    public sealed class SlotSummary
    {
        public SaveSlotId Slot;
        public LoadResultStatus Status;
        public bool HasSave;
        public int Revision;
        public string SavedAtUtc = string.Empty;
        public int BankedSalvage;
        public int CompletedMissions;
        public int LandmarksDiscovered;
        public bool ExpeditionActive;
        public string Detail = string.Empty;
    }

    public enum AutosaveTickResult
    {
        Idle,
        Saved,
        Failed
    }
}
