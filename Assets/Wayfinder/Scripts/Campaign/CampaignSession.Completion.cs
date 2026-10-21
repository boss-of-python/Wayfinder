using System;
using System.Collections.Generic;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;
using Wayfinder.World;

namespace Wayfinder.Campaign
{
    public enum DayPhase
    {
        Dawn = 0,
        Midday = 1,
        Dusk = 2,
        Overcast = 3
    }

    /// <summary>
    /// Full-campaign domain surface (§8.3, §9.2–9.3, §10.1, §12.1–12.2, §17.3):
    /// - Multi-vehicle fleet unlock, purchase, per-chassis state persistence, and safe cargo settlement
    /// - Quest item deliveries + permanent world infrastructure unlocks (Q07 bridge, Q11, Q15, Q19)
    /// - Visit volumes (regional garages + Q20 Frontier Observatory)
    /// - Multi-target contracts across all 6 patterns, GentleDelivery +100 clean-drive bonus, and repeatable jobs
    /// - Biome relic memorabilia cosmetics, calm time-of-day / tidal waiting, persistent SettingsFile, and Q20 finale state.
    /// </summary>
    public sealed partial class CampaignSession
    {
        public const string FinaleMissionCode = "Q20";
        public const int GentleDeliveryCarefulBonusSalvage = 100;

        readonly SortedSet<string> _ownedVehicles = new SortedSet<string>(StringComparer.Ordinal) { ScoutVehicleId };
        readonly Dictionary<string, VehicleDto> _fleet = new Dictionary<string, VehicleDto>(StringComparer.Ordinal);
        readonly SortedSet<string> _deliveredSites = new SortedSet<string>(StringComparer.Ordinal);
        readonly SortedSet<string> _visitedVolumes = new SortedSet<string>(StringComparer.Ordinal);
        readonly SortedSet<string> _equippedCosmetics = new SortedSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, HashSet<string>> _contractProgress = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        readonly HashSet<string> _damagedGentleContracts = new HashSet<string>(StringComparer.Ordinal);

        public SettingsFile Settings { get; private set; } = new SettingsFile();
        public bool CampaignCompleted { get; internal set; }
        public int RepeatableCompletionCount { get; internal set; }

        public IReadOnlyCollection<string> OwnedVehicles => _ownedVehicles;
        public IReadOnlyCollection<string> DeliveredSites => _deliveredSites;
        public IReadOnlyCollection<string> VisitedVolumes => _visitedVolumes;
        public IReadOnlyCollection<string> EquippedCosmetics => _equippedCosmetics;
        public bool HasSiteDelivered(string siteId) => _deliveredSites.Contains(siteId ?? string.Empty);
        public bool HasVisitedVolume(string volumeId) => _visitedVolumes.Contains(volumeId ?? string.Empty);
        public WetlandWaterState CurrentTideState => WorldHydrology.StateAtTime(Clock.DayTimeSeconds);

        // ================================================================== quest deliveries & visit volumes (§9.2)

        /// <summary>
        /// Delivers a quest item to an authored delivery site (Q07 bridge brace manifest, Q11 cable spool,
        /// Q15 stabilizer plates, Q19 sensor core). Unlocks permanent infrastructure when configured
        /// (such as <c>site.canyon.bridge</c>), reports mission progress, and saves critically.
        /// </summary>
        public CommandResult DeliverQuestItem(string siteOrTargetId)
        {
            if (!Expedition.IsActive)
                return CommandResult.Fail(FailureReason.WrongState, "no active expedition");

            SliceSite site = null;
            if (!string.IsNullOrEmpty(siteOrTargetId))
            {
                if (!Content.Sites.TryGetValue(siteOrTargetId, out site) || site.Kind != "delivery")
                {
                    site = null;
                    foreach (var s in Content.Sites.Values)
                        if (s.Kind == "delivery" && s.ItemId == siteOrTargetId) { site = s; break; }
                }
            }
            if (site == null)
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown delivery site '{siteOrTargetId}'");
            if (_deliveredSites.Contains(site.Id))
                return CommandResult.Fail(FailureReason.AlreadyApplied, $"'{site.Id}' already delivered");
            if (!string.IsNullOrEmpty(site.MissionCode) && Missions.StateOf(site.MissionCode) != MissionState.Active)
                return CommandResult.Fail(FailureReason.MissionNotAccepted, $"{site.MissionCode} is not active");

            if (!ItemCatalog.TryGet(site.ItemId ?? string.Empty, out var itemDef))
                return CommandResult.Fail(FailureReason.ContentMissing, $"unknown item '{site.ItemId}'");
            bool hasItem = itemDef.ProtectedLedger
                ? Inventory.ProtectedLedgerContains(site.ItemId)
                : Inventory.QuantityOf(Cargo, site.ItemId) > 0;
            if (!hasItem)
                return CommandResult.Fail(FailureReason.ItemNotFound, $"{site.ItemId} required in cargo");

            return RunCritical(() =>
            {
                var remove = itemDef.ProtectedLedger
                    ? Inventory.RemoveProtected(site.ItemId)
                    : Inventory.Remove(Cargo, new ContentId(site.ItemId), 1);
                if (!remove.Success) return remove;

                _deliveredSites.Add(site.Id);
                if (!string.IsNullOrEmpty(site.UnlocksInfrastructure))
                    Garage.UnlockInfrastructure(site.UnlocksInfrastructure);

                ReportMission(MissionConditionType.DeliverQuestItem, site.Id);
                if (!string.Equals(site.Id, site.ItemId, StringComparison.Ordinal))
                    ReportMission(MissionConditionType.DeliverQuestItem, site.ItemId);

                Notice(CampaignNoticeKind.ObjectiveUpdated, $"Delivered {DisplayNames.Item(site.ItemId)}", 1, site.Id);
                return CommandResult.Ok();
            });
        }

        /// <summary>
        /// Enters a world visit volume (Frontier Observatory in Q20 or any regional garage apron).
        /// </summary>
        public CommandResult EnterVisitVolume(string volumeId)
        {
            if (!Expedition.IsActive)
                return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            if (string.IsNullOrEmpty(volumeId))
                return CommandResult.Fail(FailureReason.InvalidTarget, "empty volume id");

            if (GarageService.IsKnownGarage(volumeId))
            {
                if (!Garage.IsGarageUnlocked(volumeId))
                {
                    var unlock = RunCritical(() =>
                    {
                        var r = Transactions.Execute(new UnlockGarage(NextTransactionId(), new ContentId(volumeId)));
                        if (!r.Success) return r;
                        Notice(CampaignNoticeKind.GarageUnlocked, $"Garage unlocked: {volumeId}", 0, volumeId);
                        ReportMission(MissionConditionType.VisitVolume, volumeId);
                        return CommandResult.Ok();
                    });
                    return unlock;
                }
                ReportMission(MissionConditionType.VisitVolume, volumeId);
                return CommandResult.Ok();
            }

            if (!Content.Sites.TryGetValue(volumeId, out var site) || site.Kind != "visit")
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown visit volume '{volumeId}'");

            if (_visitedVolumes.Add(volumeId))
            {
                RevealAround(site.Position.X, site.Position.Z, vista: true);
                Notice(CampaignNoticeKind.ObjectiveUpdated, $"Reached {DisplayNames.Relic(volumeId)}", 1, volumeId);
                _autosave.MarkDirty();
            }
            ReportMission(MissionConditionType.VisitVolume, volumeId);
            return CommandResult.Ok();
        }

        // ================================================================== 4-vehicle fleet (§10.1)

        /// <summary>Purchases a new chassis once its regional relay milestone and Salvage cost are met (§10.1).</summary>
        public CommandResult PurchaseVehicle(string vehicleId)
        {
            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState, "vehicles are unlocked at a garage (§12.1)");
            if (string.IsNullOrEmpty(vehicleId) || !Content.Vehicles.TryGetValue(vehicleId, out var entry))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown vehicle '{vehicleId}'");
            if (_ownedVehicles.Contains(vehicleId))
                return CommandResult.Fail(FailureReason.AlreadyApplied, $"already own '{vehicleId}'");
            if (!string.IsNullOrEmpty(entry.RequiredMissionCode) && !Missions.IsCompletedCode(entry.RequiredMissionCode))
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet,
                    $"complete {entry.RequiredMissionCode} first");
            if (Economy.BankedSalvage < entry.UnlockSalvageCost)
                return CommandResult.Fail(FailureReason.InsufficientCurrency,
                    $"need {entry.UnlockSalvageCost} Salvage, have {Economy.BankedSalvage}");

            return RunCritical(() =>
            {
                if (entry.UnlockSalvageCost > 0)
                {
                    var spend = Economy.TrySpend(entry.UnlockSalvageCost, NextTransactionId());
                    if (!spend.Success) return spend;
                }
                _ownedVehicles.Add(vehicleId);
                _fleet[vehicleId] = new VehicleDto
                {
                    id = vehicleId,
                    fuelLiters = entry.Spec.FuelCapacityLiters,
                    condition = new ConditionDto(),
                    equippedModuleIds = new List<string>(),
                    unlockedTireSets = UnlockedTireNames(),
                    equippedTireSet = TireSet.AllTerrain.ToString()
                };
                Notice(CampaignNoticeKind.Info, $"Unlocked vehicle: {entry.Spec.DisplayNameKey}", entry.UnlockSalvageCost, vehicleId);
                return CommandResult.Ok();
            });
        }

        /// <summary>
        /// Switches the active chassis at a garage (§10.1, §10.4). Any excess cargo that would exceed the
        /// incoming chassis's slot or payload capacity is automatically transferred into unlimited shared
        /// garage storage so zero items are lost and capacity invariants remain intact.
        /// </summary>
        public CommandResult SelectVehicle(string vehicleId)
        {
            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState, "switch vehicles at a garage (§10.4)");
            if (string.IsNullOrEmpty(vehicleId) || !Content.Vehicles.ContainsKey(vehicleId))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown vehicle '{vehicleId}'");
            if (!_ownedVehicles.Contains(vehicleId))
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet, $"vehicle '{vehicleId}' is not owned");
            if (string.Equals(ActiveVehicleId, vehicleId, StringComparison.Ordinal))
                return CommandResult.Ok();

            return RunCritical(() =>
            {
                SyncActiveVehicleToFleet();

                var nextSpec = Content.VehicleSpecById(vehicleId);
                var nextSaved = _fleet.TryGetValue(vehicleId, out var dto) ? dto : null;
                var nextFitted = new List<string>();
                if (nextSaved?.equippedModuleIds != null)
                {
                    foreach (var m in nextSaved.equippedModuleIds)
                        if (Research.IsResearched(m)) nextFitted.Add(m);
                }
                var nextTire = nextSaved != null && Enum.TryParse(nextSaved.equippedTireSet, out TireSet parsedTire)
                    ? parsedTire : TireSet.AllTerrain;

                // Restore per-chassis loadout on Research (preserving global researched tiers!).
                var researched = new List<string>();
                foreach (var tier in Research.Tiers)
                    if (Research.IsResearched(tier.Id.Value)) researched.Add(tier.Id.Value);
                Research.Restore(researched, nextFitted, nextTire);

                ActiveVehicleId = vehicleId;
                Vehicle = nextSpec;
                var resolved = ResolveStats();

                // Transfer excess cargo stacks into shared GarageStorage before tightening container capacity.
                while (Inventory.UsedSlots(Cargo) > resolved.CargoSlots ||
                       Inventory.TotalMassKg(Cargo) > resolved.PayloadKg)
                {
                    var stacks = Inventory.Stacks(Cargo);
                    ItemStack? pick = null;
                    for (int i = stacks.Count - 1; i >= 0; i--)
                    {
                        if (!ItemCatalog.Get(stacks[i].ItemId).ProtectedLedger) { pick = stacks[i]; break; }
                    }
                    if (!pick.HasValue) break;
                    var moved = Transactions.Execute(new TransferItems(
                        NextTransactionId(), Cargo, GarageStorage, pick.Value.ItemId, pick.Value.Quantity));
                    if (!moved.Success) return moved;
                }

                ConfigureContainers();
                Damage.RepairFully();
                FuelLiters = FuelCapacityLiters;
                SyncActiveVehicleToFleet();
                Notice(CampaignNoticeKind.Info, $"Active vehicle: {Vehicle.DisplayNameKey}", 0, vehicleId);
                return CommandResult.Ok();
            });
        }

        // ================================================================== field contract execution (§9.3)

        /// <summary>Marks a heavy collision during the active expedition, voiding the GentleDelivery careful bonus.</summary>
        public void NotifyHeavyImpact()
        {
            if (!Expedition.IsActive) return;
            foreach (var id in Contracts.AcceptedIds)
                if (Content.Contracts.TryGetValue(id, out var def) &&
                    string.Equals(def.Pattern, "GentleDelivery", StringComparison.Ordinal))
                    _damagedGentleContracts.Add(id);
        }

        /// <summary> Resolves concrete target socket IDs for an authored or repeatable contract. </summary>
        public IReadOnlyList<string> ContractTargetsFor(string contractId)
        {
            if (!Content.Contracts.TryGetValue(contractId ?? string.Empty, out var def))
                return Array.Empty<string>();
            if (def.Targets.Count > 0) return def.Targets;

            string biome = Content.Garages.TryGetValue(Expedition.LastGarageId, out var g) ? g.Biome : "forest";
            var sockets = new List<string>();
            foreach (var s in Content.Sites.Values)
                if (s.Id.StartsWith("contract_socket.", StringComparison.Ordinal) && s.Biome == biome)
                    sockets.Add(s.Id);
            sockets.Sort(StringComparer.Ordinal);
            if (sockets.Count == 0) return Array.Empty<string>();

            int count = Math.Max(1, def.TargetCount);
            int seedOffset = (int)(StableHash.Hash32($"{contractId}:{Expedition.LastGarageId}:{RepeatableCompletionCount}") % (uint)sockets.Count);
            var resolved = new List<string>(count);
            for (int i = 0; i < count; i++)
                resolved.Add(sockets[(seedOffset + i) % sockets.Count]);
            return resolved;
        }

        public bool IsContractTargetDone(string contractId, string targetId) =>
            _contractProgress.TryGetValue(contractId ?? string.Empty, out var done) && done.Contains(targetId ?? string.Empty);

        /// <summary>
        /// Completes one target socket/landmark of an accepted contract in the field; when all targets of
        /// the contract are satisfied, settles the contract reward into escrow (+100 bonus for undamaged
        /// GentleDelivery, or route-scaled 150–350 Salvage for repeatable jobs).
        /// </summary>
        public CommandResult InteractContractTarget(string contractId, string targetId)
        {
            if (!Expedition.IsActive)
                return CommandResult.Fail(FailureReason.WrongState, "no active expedition");
            if (!Contracts.IsAccepted(contractId ?? string.Empty) || !Content.Contracts.TryGetValue(contractId, out var def))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"contract '{contractId}' is not active");

            var targets = ContractTargetsFor(contractId);
            bool validTarget = false;
            for (int i = 0; i < targets.Count; i++)
                if (string.Equals(targets[i], targetId, StringComparison.Ordinal)) { validTarget = true; break; }
            if (!validTarget)
                return CommandResult.Fail(FailureReason.InvalidTarget, $"'{targetId}' is not a target of '{contractId}'");

            if (!_contractProgress.TryGetValue(contractId, out var done))
                _contractProgress[contractId] = done = new HashSet<string>(StringComparer.Ordinal);
            if (!done.Add(targetId))
                return CommandResult.Fail(FailureReason.AlreadyApplied, $"'{targetId}' already completed");

            if (done.Count < targets.Count)
            {
                Notice(CampaignNoticeKind.ObjectiveUpdated, $"{contractId}: {done.Count}/{targets.Count}", done.Count, contractId);
                _autosave.MarkDirty();
                return CommandResult.Ok();
            }

            float routeMeters = EstimateContractRouteMeters(targets);
            bool carefulBonus = string.Equals(def.Pattern, "GentleDelivery", StringComparison.Ordinal) &&
                                !_damagedGentleContracts.Contains(contractId);

            return RunCritical(() =>
            {
                if (!def.OneTime)
                {
                    int repReward = ContractService.RepeatableReward(routeMeters);
                    string repKey = $"reward.repeatable.{def.Id}.{++RepeatableCompletionCount}";
                    if (!Discovery.RewardLedger.TryClaim(repKey))
                        return CommandResult.Fail(FailureReason.AlreadyApplied, repKey);
                    Economy.Escrow(repReward);
                    var remainingAccepted = new List<string>();
                    foreach (var cid in Contracts.AcceptedIds)
                        if (!string.Equals(cid, def.Id, StringComparison.Ordinal)) remainingAccepted.Add(cid);
                    Contracts.Restore(remainingAccepted, Contracts.CompletedIds);
                    _contractProgress.Remove(contractId);
                    Notice(CampaignNoticeKind.ContractCompleted, $"Job complete: +{repReward} Salvage pending", repReward, contractId);
                    return CommandResult.Ok();
                }

                var complete = Transactions.Execute(new CompleteContract(
                    NextTransactionId(), new ContentId(contractId), def.RewardSalvage, def.RewardKey));
                if (!complete.Success) return complete;

                int total = def.RewardSalvage;
                if (carefulBonus)
                {
                    Economy.Escrow(GentleDeliveryCarefulBonusSalvage);
                    total += GentleDeliveryCarefulBonusSalvage;
                }
                _contractProgress.Remove(contractId);
                _damagedGentleContracts.Remove(contractId);
                Notice(CampaignNoticeKind.ContractCompleted, $"Contract complete: +{total} Salvage pending", total, contractId);
                return CommandResult.Ok();
            });
        }

        float EstimateContractRouteMeters(IReadOnlyList<string> targets)
        {
            var prev = SpawnFor(Expedition.LastGarageId).Position;
            float sum = 0f;
            for (int i = 0; i < targets.Count; i++)
            {
                if (Content.Sites.TryGetValue(targets[i], out var s))
                {
                    sum += prev.DistanceXZ(s.Position);
                    prev = s.Position;
                }
            }
            return sum;
        }

        // ================================================================== cosmetics, time/tide & settings (§8.3, §12.2, §17.3)

        public List<string> UnlockedCosmetics()
        {
            var list = new List<string>();
            foreach (var biome in CompletedRelicSets())
                list.Add("cosmetic.relic." + biome);
            if (CampaignCompleted)
                list.Add("cosmetic.atlas.medallion");
            return list;
        }

        public CommandResult EquipCosmetic(string cosmeticId)
        {
            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState, "cosmetics are equipped at a garage (§12.1)");
            if (!UnlockedCosmetics().Contains(cosmeticId ?? string.Empty))
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet, $"cosmetic '{cosmeticId}' is locked");
            return RunCritical(() =>
            {
                _equippedCosmetics.Add(cosmeticId);
                return CommandResult.Ok();
            });
        }

        public CommandResult UnequipCosmetic(string cosmeticId)
        {
            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState, "cosmetics are equipped at a garage (§12.1)");
            return RunCritical(() =>
            {
                _equippedCosmetics.Remove(cosmeticId ?? string.Empty);
                return CommandResult.Ok();
            });
        }

        /// <summary>Calm rest at any unlocked hub or established camp to choose time of day (§12.2).</summary>
        public CommandResult WaitAtHubOrCamp(DayPhase phase)
        {
            if (!CanRestNow())
                return CommandResult.Fail(FailureReason.WrongState, "rest requires an unlocked hub or established camp");
            float targetDaySeconds = ((int)phase + 1) * 225f;
            Clock.Restore(Expedition.ElapsedSimulationSeconds, targetDaySeconds);
            _autosave.MarkDirty();
            Notice(CampaignNoticeKind.Info, $"Rested until {phase}", 0, phase.ToString());
            return CommandResult.Ok();
        }

        /// <summary>Rests until Coastal Wetlands reaches LowWater or HighWater (§6.2 D, §12.2).</summary>
        public CommandResult WaitForTide(WetlandWaterState tideState)
        {
            if (!CanRestNow())
                return CommandResult.Fail(FailureReason.WrongState, "rest requires an unlocked hub or established camp");
            float targetDaySeconds = tideState == WetlandWaterState.HighWater
                ? WorldHydrology.TidalCycleSeconds * 0.60f
                : WorldHydrology.TidalCycleSeconds * 0.10f;
            Clock.Restore(Expedition.ElapsedSimulationSeconds, targetDaySeconds);
            _autosave.MarkDirty();
            Notice(CampaignNoticeKind.Info, $"Tide state: {tideState}", 0, tideState.ToString());
            return CommandResult.Ok();
        }

        bool CanRestNow()
        {
            if (InGarage) return true;
            foreach (var c in Content.Camps.Values)
                if (Garage.IsCampActivated(c.Id) && Expedition.Pose.Position.DistanceXZ(c.Position) <= 12f)
                    return true;
            return false;
        }

        /// <summary>Persists independent SettingsFile (settings.json) without touching career save slots (§17.3).</summary>
        public bool UpdateSettings(Action<SettingsFile> mutate)
        {
            if (mutate == null) throw new ArgumentNullException(nameof(mutate));
            mutate(Settings);
            Settings = SaveService.NormalizeSettings(Settings);
            return _saves.SaveSettings(Settings);
        }

        internal void CheckFinaleCompletion()
        {
            if (!CampaignCompleted && Missions.IsCompletedCode(FinaleMissionCode))
            {
                CampaignCompleted = true;
                Notice(CampaignNoticeKind.Info,
                    "An Atlas for Everyone complete — Frontier Observatory broadcast live. Explore freely.", 0, FinaleMissionCode);
            }
        }

        // ================================================================== snapshot helpers

        List<string> UnlockedTireNames()
        {
            var list = new List<string>();
            foreach (var set in Research.UnlockedTireSets()) list.Add(set.ToString());
            return list;
        }

        internal void SyncActiveVehicleToFleet()
        {
            if (string.IsNullOrEmpty(ActiveVehicleId)) ActiveVehicleId = ScoutVehicleId;
            _ownedVehicles.Add(ActiveVehicleId);
            _fleet[ActiveVehicleId] = new VehicleDto
            {
                id = ActiveVehicleId,
                fuelLiters = FuelLiters,
                condition = new ConditionDto
                {
                    engine = Damage.EngineCondition,
                    suspension = Damage.SuspensionCondition,
                    tires = Damage.TiresCondition,
                    body = Damage.BodyCondition
                },
                equippedModuleIds = new List<string>(Research.FittedIds),
                unlockedTireSets = UnlockedTireNames(),
                equippedTireSet = Research.SelectedTireSet.ToString()
            };
        }

        internal List<VehicleDto> CaptureFleetDtos()
        {
            SyncActiveVehicleToFleet();
            var list = new List<VehicleDto>(_ownedVehicles.Count);
            foreach (var id in _ownedVehicles)
            {
                if (_fleet.TryGetValue(id, out var v))
                {
                    list.Add(new VehicleDto
                    {
                        id = v.id,
                        fuelLiters = v.fuelLiters,
                        condition = new ConditionDto
                        {
                            engine = v.condition.engine,
                            suspension = v.condition.suspension,
                            tires = v.condition.tires,
                            body = v.condition.body
                        },
                        equippedModuleIds = new List<string>(v.equippedModuleIds ?? new List<string>()),
                        unlockedTireSets = new List<string>(v.unlockedTireSets ?? new List<string>()),
                        equippedTireSet = v.equippedTireSet ?? TireSet.AllTerrain.ToString()
                    });
                }
            }
            return list;
        }

        internal void RestoreCompletionState(SavePayload p)
        {
            _ownedVehicles.Clear();
            _ownedVehicles.Add(ScoutVehicleId);
            foreach (var id in p.ownedVehicleIds ?? new List<string>())
                if (!string.IsNullOrEmpty(id) && Content.Vehicles.ContainsKey(id)) _ownedVehicles.Add(id);

            _fleet.Clear();
            foreach (var v in p.vehicles ?? new List<VehicleDto>())
            {
                if (v == null || string.IsNullOrEmpty(v.id) || !Content.Vehicles.ContainsKey(v.id)) continue;
                _ownedVehicles.Add(v.id);
                _fleet[v.id] = new VehicleDto
                {
                    id = v.id,
                    fuelLiters = v.fuelLiters,
                    condition = v.condition ?? new ConditionDto(),
                    equippedModuleIds = new List<string>(v.equippedModuleIds ?? new List<string>()),
                    unlockedTireSets = new List<string>(v.unlockedTireSets ?? new List<string>()),
                    equippedTireSet = v.equippedTireSet ?? TireSet.AllTerrain.ToString()
                };
            }

            _deliveredSites.Clear();
            foreach (var d in p.deliveredSiteIds ?? new List<string>())
                if (!string.IsNullOrEmpty(d)) _deliveredSites.Add(d);

            _visitedVolumes.Clear();
            foreach (var vol in p.visitedVolumeIds ?? new List<string>())
                if (!string.IsNullOrEmpty(vol)) _visitedVolumes.Add(vol);

            _equippedCosmetics.Clear();
            foreach (var c in p.equippedCosmeticIds ?? new List<string>())
                if (!string.IsNullOrEmpty(c)) _equippedCosmetics.Add(c);

            RepeatableCompletionCount = p.repeatableCompletionCount;
            CampaignCompleted = p.campaignCompleted;
        }
    }
}
