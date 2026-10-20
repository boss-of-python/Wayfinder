using System;
using System.Collections.Generic;
using System.Globalization;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;

namespace Wayfinder.Campaign
{
    /// <summary>Field actions during an expedition (§2.3 Drive/Discover/Collect, §8, §12.2).</summary>
    public sealed partial class CampaignSession
    {
        // ================================================================== field actions

        /// <summary>Vehicle telemetry pushed by the presentation layer each frame (or at a fixed cadence).</summary>
        public void UpdateVehicle(Pose pose, float fuelLiters, int engine, int suspension, int tires, int body,
            float simulationDeltaSeconds)
        {
            float fuel = float.IsNaN(fuelLiters) ? FuelLiters : MathUtil.Clamp(fuelLiters, 0f, FuelCapacityLiters);
            bool changed = false;
            if (Expedition.IsActive)
            {
                if (Expedition.Pose.IsSet)
                {
                    float step = pose.Position.DistanceXZ(Expedition.Pose.Position);
                    if (step < MaxPlausibleStepMeters) Expedition.Stats.DistanceMeters += step;
                    changed |= step > 0.5f;
                }
                Expedition.Pose = pose;
                if (fuel < FuelLiters) Expedition.Stats.FuelUsedLiters += FuelLiters - fuel;
                if (simulationDeltaSeconds > 0f)
                {
                    Expedition.ElapsedSimulationSeconds += simulationDeltaSeconds;
                    Clock.Advance(simulationDeltaSeconds);
                }
            }
            changed |= Math.Abs(fuel - FuelLiters) > 0.01f;
            FuelLiters = fuel;
            changed |= SetCondition(ref Damage.EngineCondition, engine) | SetCondition(ref Damage.SuspensionCondition, suspension) |
                       SetCondition(ref Damage.TiresCondition, tires) | SetCondition(ref Damage.BodyCondition, body);
            if (changed) _autosave.MarkDirty();
        }

        static bool SetCondition(ref int field, int value)
        {
            int clamped = value < 0 ? 0 : (value > 100 ? 100 : value);
            if (clamped == field) return false;
            field = clamped;
            return true;
        }

        /// <summary>Records a validated safe reset pose (sampled by <see cref="SafePoseSampler"/>).</summary>
        public void RecordSafePose(Pose pose)
        {
            if (!Expedition.IsActive) return;
            Expedition.LastSafePose = pose;
            _autosave.MarkDirty();
        }

        public CommandResult CollectCache(string cacheId)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            if (!Content.TryGetContents(cacheId, out var contents))
                return CommandResult.Fail(FailureReason.ContentMissing, $"unknown cache '{cacheId}'");
            var result = Transactions.Execute(new CollectPickup(NextTransactionId(), new ContentId(cacheId), Cargo));
            if (!result.Success) return result;
            Expedition.Stats.CachesCollected++;
            if (contents.SalvageUnits > 0) ReportMission(MissionConditionType.CollectQuestItem, ItemCatalog.SalvageBundle);
            _autosave.MarkDirty();
            InvalidateMap();
            return result;
        }

        public CommandResult DiscoverLandmark(string landmarkId, float speedKph, float observedSeconds)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            if (string.IsNullOrEmpty(landmarkId) || !Content.Landmarks.TryGetValue(landmarkId, out var landmark))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown landmark '{landmarkId}'");
            if (Discovery.IsLandmarkDiscovered(landmarkId))
                return CommandResult.Fail(FailureReason.AlreadyApplied, "already discovered");
            if (!Discovery.TryDiscoverLandmark(landmarkId, speedKph, observedSeconds, out _, NextTransactionId()))
                return CommandResult.Fail(FailureReason.WrongState, "slow below 15 km/h and observe for 2 s (§8.2)");
            Expedition.Stats.LandmarksDiscovered++;
            Notice(CampaignNoticeKind.LandmarkDiscovered,
                $"Discovered {landmark.Name} (+{DiscoveryService.LandmarkDiscoveryRewardSalvage} Salvage pending)",
                DiscoveryService.LandmarkDiscoveryRewardSalvage, landmarkId);
            ReportMission(MissionConditionType.DiscoverLandmark, landmarkId);
            foreach (var sliceLandmark in Content.Slice.Landmarks)
                if (sliceLandmark.Id == landmarkId && sliceLandmark.Vista)
                    RevealAround(landmark.Position.X, landmark.Position.Z, vista: true);
            _autosave.MarkDirty();
            InvalidateMap();
            return CommandResult.Ok();
        }

        /// <summary>
        /// Map reveal (§8.1): 96 m while driving, 384 m from a vista, filtered by terrain
        /// line-of-sight from <see cref="MapTerrain"/> so cells behind major ridges stay fogged.
        /// Returns newly revealed countable cells.
        /// </summary>
        public int RevealAround(float x, float z, bool vista)
        {
            if (!Expedition.IsActive) return 0;
            int escrowBefore = Economy.EscrowedSalvage;
            int newly = Discovery.RevealVisibleAround(x, z, vista, HeightSampler);
            if (newly <= 0) return 0;
            Expedition.Stats.CellsRevealed += newly;
            Missions.SetConditionProgress(MissionConditionType.RevealMapPercent, "map", (int)Discovery.MapCoveragePercent);
            int mappingGain = Economy.EscrowedSalvage - escrowBefore;
            if (mappingGain > 0)
                Notice(CampaignNoticeKind.MappingReward, $"Mapping reward +{mappingGain} Salvage pending", mappingGain);
            _autosave.MarkDirty();
            InvalidateMap();
            return newly;
        }

        public CommandResult InspectObject(string targetId)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            if (Content.FindSite(Content.Slice.InspectTargets, targetId) == null)
                return CommandResult.Fail(FailureReason.InvalidTarget, $"'{targetId}' is not an inspect target");
            if (ReportMission(MissionConditionType.InspectObject, targetId) == 0)
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet, "no active objective for this target");
            _autosave.MarkDirty();
            return CommandResult.Ok();
        }

        /// <summary>True when an Active mission still needs <paramref name="type"/>/<paramref name="targetId"/>.</summary>
        public bool IsObjectivePending(MissionConditionType type, string targetId)
        {
            foreach (var code in Missions.CodesInState(MissionState.Active))
            {
                var def = Missions.Definitions[code];
                var counters = Missions.ObjectiveCounters(code);
                for (int i = 0; i < def.Objectives.Count && i < counters.Length; i++)
                    if (def.Objectives[i].Type == type && def.Objectives[i].TargetId.Value == targetId &&
                        counters[i] < def.Objectives[i].Count)
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Capacity preview for a cache (§10.4 "show capacity before pickup"): the authored contents
        /// are trial-placed and rolled back, so the HUD can say "cargo full" before the player commits.
        /// </summary>
        public CommandResult PreviewCacheCollect(string cacheId)
        {
            if (!Content.TryGetContents(cacheId, out var contents))
                return CommandResult.Fail(FailureReason.ContentMissing, cacheId);
            if (Discovery.IsCacheCollected(cacheId)) return CommandResult.Fail(FailureReason.AlreadyCollected, cacheId);
            var checkpoint = Inventory.Checkpoint();
            var result = Inventory.AddAll(Cargo, contents.Stacks);
            Inventory.Rollback(checkpoint);
            return result;
        }

        /// <summary>Index of the next checkpoint an active mission expects on <paramref name="routeId"/>, or -1.</summary>
        public int NextCheckpointIndex(string routeId)
        {
            foreach (var code in Missions.CodesInState(MissionState.Active))
            {
                var def = Missions.Definitions[code];
                var counters = Missions.ObjectiveCounters(code);
                for (int i = 0; i < def.Objectives.Count && i < counters.Length; i++)
                {
                    var o = def.Objectives[i];
                    if (o.Type == MissionConditionType.TraverseCheckpoints && o.TargetId.Value == routeId && counters[i] < o.Count)
                        return counters[i];
                }
            }
            return -1;
        }

        public CommandResult PassCheckpoint(string routeId, int checkpointIndex)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            int before = CountReady();
            int advanced = Missions.ReportCheckpoint(routeId, checkpointIndex);
            if (advanced == 0) return CommandResult.Fail(FailureReason.InvalidTarget, "not the next checkpoint");
            Notice(CampaignNoticeKind.ObjectiveUpdated, "Checkpoint reached", checkpointIndex + 1, routeId);
            if (CountReady() > before) AnnounceReadyMissions(null);
            _autosave.MarkDirty();
            return CommandResult.Ok();
        }

        /// <summary>One-time mission pickup (e.g. the Q04 relay fuse). Only while its mission is active.</summary>
        public CommandResult CollectQuestItem(string siteId)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            var site = Content.FindSite(Content.Slice.QuestItemSites, siteId);
            if (site == null) return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown quest site '{siteId}'");
            if (Missions.StateOf(site.MissionCode) != MissionState.Active)
                return CommandResult.Fail(FailureReason.MissionNotAccepted, $"{site.MissionCode} is not active");
            if (Discovery.IsCacheCollected(siteId)) return CommandResult.Fail(FailureReason.AlreadyCollected, siteId);
            return RunCritical(() =>
            {
                var add = Inventory.Add(Cargo, new ItemStack(new ContentId(site.ItemId), 1));
                if (!add.Success) return add;
                Discovery.TryMarkCacheCollected(siteId);
                Notice(CampaignNoticeKind.QuestItemCollected, $"Collected {site.ItemId.Replace('_', ' ')}", 1, site.ItemId);
                ReportMission(MissionConditionType.CollectQuestItem, site.ItemId);
                return CommandResult.Ok();
            });
        }

        /// <summary>Relay activation (Q04): consumes the protected fuse and sets the infrastructure flag.</summary>
        public CommandResult ActivateRelay(string relayId)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            var site = Content.FindSite(Content.Slice.Relays, relayId);
            if (site == null) return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown relay '{relayId}'");
            if (Garage.HasInfrastructure(relayId)) return CommandResult.Fail(FailureReason.AlreadyApplied, "relay already active");
            if (!Inventory.ProtectedLedgerContains(site.ItemId))
                return CommandResult.Fail(FailureReason.ItemNotFound, $"{site.ItemId} required");
            return RunCritical(() =>
            {
                var remove = Inventory.RemoveProtected(site.ItemId);
                if (!remove.Success) return remove;
                Garage.UnlockInfrastructure(relayId);
                Notice(CampaignNoticeKind.RelayActivated, "Relay restored — signal online", 0, relayId);
                ReportMission(MissionConditionType.ActivateRelay, relayId);
                return CommandResult.Ok();
            });
        }

        /// <summary>Camp establishment (§12.2, QA case 10): kit consumed only with activation, then saved.</summary>
        public CommandResult EstablishCamp(string campId)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            if (Content.FindSite(Content.Slice.CampSockets, campId) == null)
                return CommandResult.Fail(FailureReason.InvalidTarget, $"no camp socket '{campId}' in this area");
            return RunCritical(() =>
            {
                var result = Transactions.Execute(new EstablishCamp(NextTransactionId(), new ContentId(campId), Cargo));
                if (result.Success) ReportMission(MissionConditionType.EstablishCamp, campId);
                return result;
            });
        }

        /// <summary>Camp deposit (§12.2/§12.4): banks Salvage + escrow, stores materials (500 kg), saves.</summary>
        public CommandResult DepositAtCamp(string campId)
        {
            if (!Expedition.IsActive) return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            if (!Garage.IsCampActivated(campId)) return CommandResult.Fail(FailureReason.WrongState, "establish the camp first");
            return RunCritical(() =>
            {
                // DEF-017 (part 1): the notice is built from the wallet the transaction actually
                // produced, not from state read before it ran — a rolled-back deposit could
                // otherwise still toast "Deposited N Salvage".
                int bankedBefore = Economy.BankedSalvage;
                var result = Transactions.Execute(new BankCargo(NextTransactionId(), new ContentId(campId), Cargo, CampStorage));
                if (result.Success)
                {
                    int banked = Economy.BankedSalvage - bankedBefore;
                    Notice(CampaignNoticeKind.Banked, $"Deposited: {banked} Salvage banked", banked, campId);
                }
                return result;
            });
        }

        /// <summary>Manual save (camp/garage "save" function).</summary>
        public SaveResult SaveNow() => PersistNow();
    }
}
