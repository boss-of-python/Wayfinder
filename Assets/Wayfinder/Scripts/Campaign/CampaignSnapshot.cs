using System;
using System.Collections.Generic;
using System.Globalization;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;

namespace Wayfinder.Campaign
{
    /// <summary>
    /// Maps live session state ⇄ <see cref="SavePayload"/> (§17.3). Capture is deterministic
    /// (sets are sorted, ledgers keep append order) so "save → load → capture" is byte-identical,
    /// which the round-trip tests assert. Apply is tolerant: unknown ids from an older/newer content
    /// set are skipped instead of crashing (migration rules handle intentional removals, §17.5).
    /// </summary>
    internal static class CampaignSnapshot
    {
        const string MissionPrefix = "mission.";

        public static SavePayload Capture(CampaignSession s)
        {
            var p = new SavePayload
            {
                wallet = new WalletDto
                {
                    salvage = s.Economy.BankedSalvage,
                    // DEF-017: the hub-attributed share is persisted separately; the scalar keeps
                    // holding the unattributed pool so an older build still reads a sane total.
                    escrowedSalvage = s.Economy.EscrowedSalvage - HubEscrowTotal(s.Economy),
                    escrowByHub = HubEscrowDtos(s.Economy)
                },
                activeVehicleId = s.ActiveVehicleId,
                researchedUpgradeIds = ResearchedInTierOrder(s),
                unlockedGarageIds = Sorted(s.Garage.UnlockedGarages),
                activatedCampIds = Sorted(s.Garage.ActivatedCamps),
                infrastructureFlags = Sorted(s.Garage.InfrastructureFlags),
                completedMissionIds = new List<string>(),
                activeMissions = new List<ActiveMissionDto>(),
                garageStorage = Stacks(s, CampaignSession.GarageStorage),
                campStorage = Stacks(s, CampaignSession.CampStorage),
                claimedRewardKeys = new List<string>(s.Discovery.RewardLedger.Order),
                appliedTransactionIds = new List<string>(s.Transactions.AppliedOrder),
                tutorialFlags = new List<string>(s.TutorialFlags),
                acceptedContracts = new List<ActiveContractDto>(),
                completedContractIds = new List<string>(s.Contracts.CompletedIds),
                protectedLedgerItemIds = new List<string>(s.Inventory.ProtectedLedgerItems),
                relicIds = Sorted(s.Discovery.RelicIds),
                ownedVehicleIds = new List<string> { s.ActiveVehicleId },
                nextTransactionSequence = s.NextSequence
            };

            p.vehicles = new List<VehicleDto>
            {
                new VehicleDto
                {
                    id = s.ActiveVehicleId,
                    fuelLiters = s.FuelLiters,
                    condition = new ConditionDto
                    {
                        engine = s.Damage.EngineCondition,
                        suspension = s.Damage.SuspensionCondition,
                        tires = s.Damage.TiresCondition,
                        body = s.Damage.BodyCondition
                    },
                    // Researched modules are installed automatically in this build (D-023).
                    equippedModuleIds = ResearchedInTierOrder(s),
                    unlockedTireSets = new List<string> { "AllTerrain" }
                }
            };

            foreach (var code in s.Missions.CompletedCodes)
                p.completedMissionIds.Add(MissionPrefix + code);
            foreach (var state in new[] { MissionState.Active, MissionState.ReadyToTurnIn })
                foreach (var code in s.Missions.CodesInState(state))
                    p.activeMissions.Add(new ActiveMissionDto
                    {
                        id = MissionPrefix + code,
                        state = state.ToString(),
                        objectiveCounters = new List<int>(s.Missions.ObjectiveCounters(code))
                    });
            p.activeMissions.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            foreach (var id in s.Contracts.AcceptedIds)
                p.acceptedContracts.Add(new ActiveContractDto { id = id, state = "Accepted" });

            p.world = new WorldStateDto
            {
                revealedCellsBase64 = s.Discovery.RevealedCells.ToBase64(),
                surveyedCellsBase64 = s.Discovery.SurveyedCells.ToBase64(),
                // DEF-008: persisted. It used to be dropped as "derived data", so every load
                // reverted the coverage denominator to all 9,216 cells (ocean included).
                countableMaskBase64 = s.Discovery.CountableMask.ToBase64(),
                discoveredLandmarkIds = Sorted(s.Discovery.DiscoveredLandmarkIds),
                collectedCacheIds = Sorted(s.Discovery.CollectedCacheIds),
                relicIds = Sorted(s.Discovery.RelicIds),
                mappingRewardsPaidThroughCount = s.Discovery.MappingRewardsPaidThroughCount,
                customMarkers = MarkerDtos(s.MapMarkers),
                nextCustomMarkerIndex = s.MapMarkers.NextIndex,
                routeTargetMarkerId = s.MapMarkers.RouteTargetId
            };

            var e = s.Expedition;
            p.expedition = new ExpeditionDto
            {
                id = e.Id ?? string.Empty,
                status = e.Status.ToString(),
                lastGarageId = e.LastGarageId ?? string.Empty,
                pose = PoseDto(e.Pose),
                lastSafePose = PoseDto(e.LastSafePose),
                cargo = Stacks(s, CampaignSession.Cargo),
                pendingRewardKeys = new List<string>(),
                elapsedSimulationSeconds = (float)e.ElapsedSimulationSeconds,
                stats = new ExpeditionStatsDto
                {
                    distanceMeters = e.Stats.DistanceMeters,
                    fuelUsedLiters = e.Stats.FuelUsedLiters,
                    cachesCollected = e.Stats.CachesCollected,
                    landmarksDiscovered = e.Stats.LandmarksDiscovered,
                    cellsRevealed = e.Stats.CellsRevealed
                }
            };

            p.simulation = new SimulationDto
            {
                dayTimeSeconds = s.Clock.DayTimeSeconds,
                weatherState = s.WeatherState,
                weatherSeed = s.WeatherSeed,
                gameMode = s.Mode.ToString()
            };
            return p;
        }

        static List<EscrowEntryDto> HubEscrowDtos(EconomyService economy)
        {
            var entries = new List<EscrowEntryDto>();
            foreach (var pair in economy.EscrowByHub)
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value > 0)
                    entries.Add(new EscrowEntryDto { hubId = pair.Key, amount = pair.Value });
            entries.Sort((a, b) => string.CompareOrdinal(a.hubId, b.hubId));
            return entries;
        }

        /// <summary>Sum of the hub-attributed escrow (saved under wallet.escrowByHub).</summary>
        static int HubEscrowTotal(EconomyService economy)
        {
            int total = 0;
            foreach (var share in economy.EscrowByHub.Values) total += share;
            return total;
        }

        static List<(string hubId, int amount)> HubEscrow(WalletDto wallet)
        {
            var entries = new List<(string hubId, int amount)>();
            if (wallet?.escrowByHub == null) return entries;
            foreach (var e in wallet.escrowByHub)
                if (e != null && !string.IsNullOrEmpty(e.hubId) && e.amount > 0)
                    entries.Add((e.hubId, e.amount));
            return entries;
        }

        public static void Apply(CampaignSession s, SavePayload p)
        {
            p = p ?? new SavePayload();

            s.Economy.Restore(p.wallet?.salvage ?? 0, p.wallet?.escrowedSalvage ?? 0, HubEscrow(p.wallet));

            // Research first: it decides cargo slots/payload and tank size.
            var researched = new List<string>();
            foreach (var id in p.researchedUpgradeIds ?? new List<string>())
                foreach (var tier in s.Research.Tiers)
                    if (tier.Id.Value == id) researched.Add(id);
            s.Research.Restore(researched);

            s.Garage.Restore(p.unlockedGarageIds, p.activatedCampIds, p.infrastructureFlags);

            var completed = new List<string>();
            foreach (var id in p.completedMissionIds ?? new List<string>())
                completed.Add(StripMissionPrefix(id));
            var active = new List<(string code, MissionState state, int[] counters)>();
            foreach (var m in p.activeMissions ?? new List<ActiveMissionDto>())
            {
                if (m == null || !Enum.TryParse(m.state, out MissionState state)) continue;
                active.Add((StripMissionPrefix(m.id), state, (m.objectiveCounters ?? new List<int>()).ToArray()));
            }
            s.Missions.Restore(completed, active);

            var world = p.world ?? new WorldStateDto();
            var relics = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in world.relicIds ?? new List<string>()) relics.Add(r);
            foreach (var r in p.relicIds ?? new List<string>()) relics.Add(r);
            s.Discovery.Restore(world.revealedCellsBase64, world.surveyedCellsBase64, world.countableMaskBase64,
                world.discoveredLandmarkIds, world.collectedCacheIds, relics, world.mappingRewardsPaidThroughCount);
            s.Discovery.RewardLedger.Restore(p.claimedRewardKeys);
            s.MapMarkers.Restore(FromMarkerDtos(world.customMarkers), world.nextCustomMarkerIndex, world.routeTargetMarkerId);
            s.Transactions.Restore(p.appliedTransactionIds);

            var acceptedContracts = new List<string>();
            foreach (var c in p.acceptedContracts ?? new List<ActiveContractDto>())
                if (c != null) acceptedContracts.Add(c.id);
            s.Contracts.Restore(acceptedContracts, p.completedContractIds);

            s.TutorialFlags.Clear();
            foreach (var f in p.tutorialFlags ?? new List<string>())
                if (!string.IsNullOrEmpty(f)) s.TutorialFlags.Add(f);

            var sim = p.simulation ?? new SimulationDto();
            s.Mode = Enum.TryParse(sim.gameMode, out GameMode mode) ? mode : GameMode.Expedition;
            s.WeatherState = string.IsNullOrEmpty(sim.weatherState) ? "Clear" : sim.weatherState;
            s.WeatherSeed = sim.weatherSeed;

            var e = p.expedition ?? new ExpeditionDto();
            s.Expedition.Id = e.id ?? string.Empty;
            s.Expedition.Status = Enum.TryParse(e.status, out ExpeditionStatus status) ? status : ExpeditionStatus.None;
            s.Expedition.LastGarageId = string.IsNullOrEmpty(e.lastGarageId) ? s.Content.Slice.GarageId : e.lastGarageId;
            s.Expedition.Pose = FromDto(e.pose);
            s.Expedition.LastSafePose = FromDto(e.lastSafePose);
            s.Expedition.ElapsedSimulationSeconds = e.elapsedSimulationSeconds;
            var st = e.stats ?? new ExpeditionStatsDto();
            s.Expedition.Stats = new ExpeditionStats
            {
                DistanceMeters = st.distanceMeters,
                FuelUsedLiters = st.fuelUsedLiters,
                CachesCollected = st.cachesCollected,
                LandmarksDiscovered = st.landmarksDiscovered,
                CellsRevealed = st.cellsRevealed
            };
            s.Clock.Restore(e.elapsedSimulationSeconds, sim.dayTimeSeconds);

            // Containers after research so capacities match the researched modules.
            s.ConfigureContainers();
            var cargoCapacity = s.Inventory.CapacityOf(CampaignSession.Cargo);
            s.Inventory.Restore(CampaignSession.Cargo, FromDtos(e.cargo), cargoCapacity);
            s.Inventory.Restore(CampaignSession.GarageStorage, FromDtos(p.garageStorage),
                s.Inventory.CapacityOf(CampaignSession.GarageStorage));
            s.Inventory.Restore(CampaignSession.CampStorage, FromDtos(p.campStorage),
                s.Inventory.CapacityOf(CampaignSession.CampStorage));
            var ledger = new List<string>();
            foreach (var id in p.protectedLedgerItemIds ?? new List<string>())
                if (ItemCatalog.TryGet(id ?? string.Empty, out var def) && def.ProtectedLedger) ledger.Add(id);
            s.Inventory.RestoreProtectedLedger(ledger);

            VehicleDto vehicle = null;
            foreach (var v in p.vehicles ?? new List<VehicleDto>())
                if (v != null && v.id == s.ActiveVehicleId) vehicle = v;
            s.FuelLiters = vehicle == null ? s.FuelCapacityLiters : MathUtil.Clamp(vehicle.fuelLiters, 0f, s.FuelCapacityLiters);
            var condition = vehicle?.condition ?? new ConditionDto();
            s.Damage.RepairFully();
            s.Damage.EngineCondition = Clamp100(condition.engine);
            s.Damage.SuspensionCondition = Clamp100(condition.suspension);
            s.Damage.TiresCondition = Clamp100(condition.tires);
            s.Damage.BodyCondition = Clamp100(condition.body);

            // Never reissue a transaction id that is already applied (would silently no-op new work).
            s.NextSequence = Math.Max(Math.Max(0, p.nextTransactionSequence), MaxAppliedSequence(p.appliedTransactionIds) + 1);
        }

        static int Clamp100(int v) => v < 0 ? 0 : (v > 100 ? 100 : v);

        /// <summary>Markers keep placement order (the order the player sees in lists), not sorted order.</summary>
        static List<MapMarkerDto> MarkerDtos(MapMarkerService markers)
        {
            var list = new List<MapMarkerDto>(markers.Count);
            foreach (var m in markers.Markers)
                list.Add(new MapMarkerDto { id = m.Id, x = m.X, z = m.Z, label = m.Label });
            return list;
        }

        static List<CustomMapMarker> FromMarkerDtos(List<MapMarkerDto> dtos)
        {
            var list = new List<CustomMapMarker>();
            if (dtos == null) return list;
            foreach (var d in dtos)
                if (d != null) list.Add(new CustomMapMarker(d.id, d.x, d.z, d.label));
            return list;
        }

        static string StripMissionPrefix(string id) =>
            id != null && id.StartsWith(MissionPrefix, StringComparison.Ordinal) ? id.Substring(MissionPrefix.Length) : id ?? string.Empty;

        static int MaxAppliedSequence(List<string> appliedIds)
        {
            int max = -1;
            if (appliedIds == null) return max;
            foreach (var id in appliedIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                int colon = id.LastIndexOf(':');
                if (colon < 0 || colon == id.Length - 1) continue;
                if (int.TryParse(id.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int seq))
                    max = Math.Max(max, seq);
            }
            return max;
        }

        static List<string> ResearchedInTierOrder(CampaignSession s)
        {
            var list = new List<string>();
            foreach (var tier in s.Research.Tiers)
                if (s.Research.IsResearched(tier.Id.Value)) list.Add(tier.Id.Value);
            return list;
        }

        static List<string> Sorted(IEnumerable<string> values)
        {
            var list = new List<string>(values ?? Array.Empty<string>());
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static List<ItemStackDto> Stacks(CampaignSession s, InventoryId container)
        {
            var list = new List<ItemStackDto>();
            foreach (var stack in s.Inventory.Stacks(container))
                list.Add(new ItemStackDto
                {
                    itemId = stack.ItemId.Value,
                    quantity = stack.Quantity,
                    missionInstanceId = stack.MissionInstanceId ?? string.Empty
                });
            return list;
        }

        static List<ItemStack> FromDtos(List<ItemStackDto> dtos)
        {
            var list = new List<ItemStack>();
            if (dtos == null) return list;
            foreach (var d in dtos)
            {
                if (d == null || d.quantity <= 0 || !ItemCatalog.TryGet(d.itemId ?? string.Empty, out var def)) continue;
                if (def.ProtectedLedger) continue; // ledger objects live in protectedLedgerItemIds
                list.Add(new ItemStack(new ContentId(d.itemId), d.quantity, d.missionInstanceId));
            }
            return list;
        }

        static PoseDto PoseDto(Pose pose) => new PoseDto
        {
            x = pose.Position.X,
            y = pose.Position.Y,
            z = pose.Position.Z,
            yawDegrees = pose.YawDegrees
        };

        static Pose FromDto(PoseDto dto) => dto == null ? default : new Pose(dto.x, dto.y, dto.z, dto.yawDegrees);
    }
}
