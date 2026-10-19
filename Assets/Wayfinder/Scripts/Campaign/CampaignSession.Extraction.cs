using System;
using System.Collections.Generic;
using System.Globalization;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;

namespace Wayfinder.Campaign
{
    /// <summary>Return, banking and rescue (§2.3 Return, §5.6, §12.4).</summary>
    public sealed partial class CampaignSession
    {
        // ================================================================== return / rescue

        /// <summary>
        /// Extraction (§2.3 Return, §12.4 banking): ReturnToHub progress, idempotent BankCargo
        /// transaction (salvage + escrow → wallet, materials → garage storage), mission turn-ins at
        /// this garage, free refuel/repair (§12.1) — all one commit-or-rollback unit with a verified save.
        /// </summary>
        public ExpeditionSummary EndExpedition(string garageId) => Extract(garageId, rescue: false);

        /// <summary>
        /// Full rescue (§5.6, REC-01): always available during an expedition; returns to the nearest
        /// activated garage; loses 25% of loose salvage_bundle units (rounded down) — nothing else.
        /// Free while the tutorial is active and in Relaxed Exploration ("no reward punishment").
        /// </summary>
        public ExpeditionSummary FullRescue() => Extract(NearestUnlockedGarage(Expedition.Pose.Position), rescue: true);

        public bool RescueLossApplies => Mode != GameMode.RelaxedExploration && !TutorialActive;

        ExpeditionSummary Extract(string garageId, bool rescue)
        {
            var summary = new ExpeditionSummary { GarageId = garageId ?? string.Empty, WasRescue = rescue };
            if (!Expedition.IsActive)
            {
                summary.Result = CommandResult.Fail(FailureReason.WrongState, "no active expedition");
                return summary;
            }
            // DEF-003: reaching a known garage is what unlocks it. Nothing ever called
            // GarageService.UnlockGarage, so only garage.home was unlocked and Q05's
            // VisitVolume(garage.canyon) — reported only by a successful extraction here — could
            // never complete, which froze Q06…Q20 behind it. Unlocking is free (§11.4) and runs as a
            // transaction, so it is idempotent and rolls back with the extraction that follows.
            if (!string.IsNullOrEmpty(garageId) && GarageService.IsKnownGarage(garageId) && !Garage.IsGarageUnlocked(garageId))
            {
                var unlock = Transactions.Execute(new UnlockGarage(NextTransactionId(), new ContentId(garageId)));
                if (!unlock.Success)
                {
                    summary.Result = unlock;
                    return summary;
                }
                Notice(CampaignNoticeKind.GarageUnlocked, $"Garage unlocked: {garageId}", 0, garageId);
            }

            if (string.IsNullOrEmpty(garageId) || !Garage.IsGarageUnlocked(garageId))
            {
                summary.Result = CommandResult.Fail(FailureReason.InvalidTarget, $"garage '{garageId}' is not unlocked");
                return summary;
            }

            summary.ExpeditionId = Expedition.Id;
            summary.Result = RunCritical(() =>
            {
                if (rescue)
                {
                    int loose = Inventory.QuantityOf(Cargo, ItemCatalog.SalvageBundle);
                    int loss = RescueLossApplies ? RecoveryRules.RescueSalvageLoss(loose) : 0;
                    if (loss > 0)
                    {
                        var remove = Inventory.Remove(Cargo, new ContentId(ItemCatalog.SalvageBundle), loss);
                        if (!remove.Success) return remove;
                        Notice(CampaignNoticeKind.RescueLoss, $"Rescue: {loss} loose Salvage lost", loss);
                    }
                    summary.RescueSalvageLost = loss;
                }

                var readyBefore = Missions.CodesInState(MissionState.ReadyToTurnIn);
                Missions.ReportCondition(MissionConditionType.ReturnToHub, garageId);
                Missions.ReportCondition(MissionConditionType.VisitVolume, garageId);
                AnnounceReadyMissions(readyBefore);

                int units = Inventory.QuantityOf(Cargo, ItemCatalog.SalvageBundle);
                int escrow = Economy.EscrowedSalvage;
                int materialsBefore = MaterialUnits(Cargo);
                var bankId = NextTransactionId();
                var bank = Transactions.Execute(new BankCargo(bankId, new ContentId(garageId), Cargo, GarageStorage));
                if (!bank.Success) return bank;
                summary.BankTransactionId = bankId.Value;
                summary.SalvageUnitsBanked = units;
                summary.EscrowBanked = escrow;
                summary.MaterialUnitsStored = materialsBefore - MaterialUnits(Cargo);

                foreach (var code in Missions.CodesInState(MissionState.ReadyToTurnIn))
                {
                    var def = Missions.Definitions[code];
                    if (def.TurnInHub.Value != garageId) continue;
                    var turnIn = Missions.TryTurnIn(code, new ContentId(garageId), Economy, Discovery.RewardLedger);
                    if (turnIn.Success)
                    {
                        summary.MissionsCompleted.Add(code);
                        summary.MissionRewardSalvage += def.RewardSalvage;
                        Notice(CampaignNoticeKind.MissionCompleted, $"{code} complete (+{def.RewardSalvage} Salvage)", def.RewardSalvage, code);
                    }
                    else if (turnIn.Reason == FailureReason.AlreadyApplied)
                    {
                        summary.MissionsCompleted.Add(code); // converged on replay; never paid twice
                    }
                }

                FuelLiters = FuelCapacityLiters;
                Damage.RepairFully();
                summary.Refueled = summary.Repaired = true;

                summary.Stats = Expedition.Stats.Clone();
                Expedition.Status = ExpeditionStatus.Ended;
                Expedition.LastGarageId = garageId;
                Expedition.Pose = SpawnFor(garageId);
                Expedition.LastSafePose = Expedition.Pose;
                if (Missions.IsCompletedCode("Q01")) TutorialFlags.Remove(TutorialFlag);
                summary.WalletAfter = Economy.BankedSalvage;
                Notice(CampaignNoticeKind.Banked, $"Banked {units + escrow} Salvage", units + escrow, garageId);
                return CommandResult.Ok();
            });
            return summary;
        }
    }
}
