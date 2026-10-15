using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Progression
{
    /// <summary>
    /// Garage operations (§12.1): free full fuel, free full repair, save, research, vehicle change,
    /// loadout, storage, crafting, paint, route planning, safe-hub travel. Service occurs on entry
    /// confirmation and is visible in the expedition summary. No fees, ever (§11.4).
    /// </summary>
    public sealed class GarageService
    {
        public static readonly string[] GarageIds =
        {
            "garage.home", "garage.canyon", "garage.city", "garage.wetlands", "garage.highlands"
        };

        /// <summary>Two camp sockets per biome (§12.2, Content/camps.json).</summary>
        public static readonly string[] CampIds =
        {
            "camp.forest.a", "camp.forest.b", "camp.canyon.a", "camp.canyon.b", "camp.city.a",
            "camp.city.b", "camp.wetlands.a", "camp.wetlands.b", "camp.highlands.a", "camp.highlands.b"
        };

        public static bool IsKnownCamp(string campId) => Array.IndexOf(CampIds, campId) >= 0;
        public static bool IsKnownGarage(string garageId) => Array.IndexOf(GarageIds, garageId) >= 0;

        readonly HashSet<string> _unlockedGarages = new HashSet<string>(StringComparer.Ordinal) { "garage.home" };
        readonly HashSet<string> _activatedCamps = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _infrastructureFlags = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> UnlockedGarages => _unlockedGarages;
        public IReadOnlyCollection<string> ActivatedCamps => _activatedCamps;
        public IReadOnlyCollection<string> InfrastructureFlags => _infrastructureFlags;

        public void UnlockGarage(string garageId) => _unlockedGarages.Add(garageId);

        /// <summary>Entry service: free refuel + full repair (visible in the summary, §12.1).</summary>
        public (bool refueled, bool repaired) ServiceOnEntry(
            Simulation.VehicleSpec vehicle, Simulation.DamageModel damage,
            Action<float> setFuel)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            setFuel?.Invoke(vehicle.FuelCapacityLiters);
            damage?.RepairFully();
            return (true, true);
        }

        /// <summary>
        /// Fast travel only between activated hubs, only while AT an activated hub (§12.1).
        /// No field teleport out of danger — full rescue fills that role.
        /// </summary>
        public CommandResult TryHubTravel(string currentHubId, string destinationHubId)
        {
            bool atHub = _unlockedGarages.Contains(currentHubId) || _activatedCamps.Contains(currentHubId);
            if (!atHub)
                return CommandResult.Fail(FailureReason.WrongState, "must be at an activated hub");
            bool destinationOk = _unlockedGarages.Contains(destinationHubId) || _activatedCamps.Contains(destinationHubId);
            if (!destinationOk)
                return CommandResult.Fail(FailureReason.InvalidTarget, "destination hub not activated");
            return CommandResult.Ok();
        }

        /// <summary>Hub-only logistics (§10.4): free instant transfer among activated hubs while at a hub.</summary>
        public CommandResult TryLogisticsTransfer(string currentHubId)
        {
            if (_unlockedGarages.Contains(currentHubId) || _activatedCamps.Contains(currentHubId))
                return CommandResult.Ok();
            return CommandResult.Fail(FailureReason.WrongState, "hub logistics require being at a hub");
        }

        /// <summary>Camp establishment state — atomicity enforced by TransactionService (QA case 10).</summary>
        public bool IsCampActivated(string campId) => _activatedCamps.Contains(campId);

        /// <summary>Returns false when the camp was already active. Use the EstablishCamp transaction in gameplay.</summary>
        public bool ActivateCamp(string campId) => _activatedCamps.Add(campId);

        public bool IsGarageUnlocked(string garageId) => _unlockedGarages.Contains(garageId);

        /// <summary>Infrastructure unlocks (bridges/causeways) come from story materials — never currency (§6.3).</summary>
        public void UnlockInfrastructure(string flag) => _infrastructureFlags.Add(flag);
        public bool HasInfrastructure(string flag) => string.IsNullOrEmpty(flag) || _infrastructureFlags.Contains(flag);

        public void Restore(IEnumerable<string> garages, IEnumerable<string> camps, IEnumerable<string> infrastructure)
        {
            _unlockedGarages.Clear();
            _unlockedGarages.Add("garage.home");
            if (garages != null) foreach (var g in garages) _unlockedGarages.Add(g);
            _activatedCamps.Clear();
            if (camps != null) foreach (var c in camps) _activatedCamps.Add(c);
            _infrastructureFlags.Clear();
            if (infrastructure != null) foreach (var f in infrastructure) _infrastructureFlags.Add(f);
        }
    }

    /// <summary>
    /// Camp rules (§12.2): save, deposit (banks loose Salvage), consume carried/camp fuel, choose
    /// time of day, map access, safe-hub travel. Camp storage holds 500 kg. No research/vehicle changes.
    /// If supplies are empty the player can still save and travel to an activated garage.
    /// </summary>
    public sealed class CampService
    {
        public const float CampStorageCapacityKg = 500f;

        public bool CanEstablish(bool hasCampKit, bool socketOccupied) => hasCampKit && !socketOccupied;

        /// <summary>
        /// Deposit (§12.2/§12.4 note): banks salvage_bundle units and escrow, and moves crafting
        /// materials into camp storage as far as its 500 kg allows. The previous version moved only
        /// scrap metal and ignored the transfer result (KI-021). Gameplay routes deposits through a
        /// BankCargo transaction (idempotent); this helper shares the exact same settlement code.
        /// </summary>
        public CargoSettlement.Outcome Deposit(InventoryService inventory, InventoryId cargoSource, InventoryId campStorage,
            EconomyService economy) =>
            CargoSettlement.Settle(inventory, economy, cargoSource, campStorage);
    }

    /// <summary>
    /// Interaction contract (§16.4): every interactable provides ID, action label key, allowed
    /// range, LOS requirement, conditions, and result. Player picks one candidate by distance +
    /// facing score. Hold-to-interact 0.35 s pickups / 0.75 s substantial, tap alternative.
    /// Interaction is revalidated at commit — two input events in one frame cannot double-grant.
    /// </summary>
    public sealed class InteractionService
    {
        public const float PickupHoldSeconds = 0.35f;
        public const float SubstantialHoldSeconds = 0.75f;

        public readonly struct Interactable
        {
            public readonly ContentId Id;
            public readonly string ActionLabelKey;
            public readonly float RangeMeters;
            public readonly bool RequiresLineOfSight;
            public readonly Func<CommandResult> Preconditions;
            public readonly Func<CommandResult> Commit;

            public Interactable(ContentId id, string actionLabelKey, float rangeMeters,
                bool requiresLineOfSight, Func<CommandResult> preconditions, Func<CommandResult> commit)
            {
                Id = id;
                ActionLabelKey = actionLabelKey;
                RangeMeters = rangeMeters;
                RequiresLineOfSight = requiresLineOfSight;
                Preconditions = preconditions;
                Commit = commit;
            }
        }

        /// <summary>Scores candidates by distance and facing; returns the best within range.</summary>
        public static bool TrySelect(IEnumerable<(Interactable item, float distance, float facingScore)> candidates,
            out Interactable selected)
        {
            selected = default;
            float bestScore = float.NegativeInfinity;
            bool found = false;
            foreach (var (item, distance, facingScore) in candidates)
            {
                if (distance > item.RangeMeters) continue;
                float score = facingScore * 2f - distance / Math.Max(0.1f, item.RangeMeters);
                if (score > bestScore)
                {
                    bestScore = score;
                    selected = item;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Revalidate at commit time (§16.4). Typed capacity errors never consume the cache.</summary>
        public static CommandResult CommitInteraction(Interactable item)
        {
            var pre = item.Preconditions?.Invoke() ?? CommandResult.Ok();
            if (!pre.Success) return pre;
            return item.Commit?.Invoke() ?? CommandResult.Ok();
        }

        public static float RequiredHoldSeconds(bool substantial) =>
            substantial ? SubstantialHoldSeconds : PickupHoldSeconds;
    }
}
