using System;
using System.Collections.Generic;
using Wayfinder.Core;
using Wayfinder.Simulation;

namespace Wayfinder.Progression
{
    /// <summary>
    /// Twenty-tier Scout research tree (§11.2). Costs are INCREMENTAL, Σ = 32,200 Salvage.
    /// Tiers unlock sequentially; story gate missions must be completed first.
    /// Stat effects compose through VehicleStatsResolver (shared with garage display).
    /// </summary>
    public sealed class ResearchService
    {
        public sealed class UpgradeDefinition
        {
            public ContentId Id;
            public int Tier;
            public string NameKey;
            public int CostSalvage;
            public ContentId Prerequisite;       // previous tier (sequential tree)
            public string RequiredMissionCode;   // e.g. "Q04"; empty = none
            public readonly List<(VehicleStatsResolver.Stat stat, VehicleStatsResolver.Operation op, float value)> Effects =
                new List<(VehicleStatsResolver.Stat, VehicleStatsResolver.Operation, float)>();
            public string ResultDescriptionKey;
        }

        public static readonly ResearchService Global = new ResearchService();

        readonly List<UpgradeDefinition> _tiers = new List<UpgradeDefinition>(20);
        readonly HashSet<string> _researched = new HashSet<string>(StringComparer.Ordinal);
        readonly Func<string, bool> _isMissionCompleted;

        /// <summary>
        /// <paramref name="isMissionCompleted"/> answers "is story mission code X completed?"
        /// (story gates, §11.1). Defaults to <see cref="MissionService.Global"/> for the runtime;
        /// tests inject a local predicate so no hidden global state leaks between them.
        /// The tier table itself is immutable canonical data.
        /// </summary>
        public ResearchService(Func<string, bool> isMissionCompleted = null)
        {
            _isMissionCompleted = isMissionCompleted ?? (code => MissionService.Global.IsCompletedCode(code));
            // Canonical table (§11.2). Content/upgrades.json mirrors this data; ContentValidators
            // assert the two agree so drift is a build error.
            Add(1, "upgrade.scout.01.trailTires1", 200, "Q01", "upgrade.trailTires1.name",
                (VehicleStatsResolver.Stat.DirtGravelGrip, VehicleStatsResolver.Operation.Multiply, 1.10f));
            Add(2, "upgrade.scout.02.underbodyGuard", 300, "Q02", "upgrade.underbodyGuard.name",
                (VehicleStatsResolver.Stat.UnderbodyDamageFactor, VehicleStatsResolver.Operation.Multiply, 0.75f));
            Add(3, "upgrade.scout.03.fieldWinch1", 450, "Q02", "upgrade.fieldWinch1.name",
                (VehicleStatsResolver.Stat.WinchReachM, VehicleStatsResolver.Operation.Set, 12f),
                (VehicleStatsResolver.Stat.WinchForceCapN, VehicleStatsResolver.Operation.Set, 8000f));
            Add(4, "upgrade.scout.04.cargoOrganizer", 550, "Q03", "upgrade.cargoOrganizer.name",
                (VehicleStatsResolver.Stat.CargoSlots, VehicleStatsResolver.Operation.Set, 14f));
            Add(5, "upgrade.scout.05.suspension1", 700, "Q04", "upgrade.suspension1.name",
                (VehicleStatsResolver.Stat.SuspensionTravelM, VehicleStatsResolver.Operation.Set, 0.34f),
                (VehicleStatsResolver.Stat.GroundClearanceM, VehicleStatsResolver.Operation.Set, 0.27f));
            Add(6, "upgrade.scout.06.tank1", 850, "Q04", "upgrade.tank1.name",
                (VehicleStatsResolver.Stat.FuelCapacityLiters, VehicleStatsResolver.Operation.Set, 30f));
            // Tiers 7, 8, 12, 13 and 18 cost 9,100 Salvage (28 % of the tree) and declared no
            // effects at all until 2026-10-19 (DEF-004): low range and diff lock had no Stat member
            // to write, the raised intake's 0.85 m constant was never composed, and the two tire
            // unlocks were left to callers that hardcoded `true,true,true` / `false,false,false`.
            Add(7, "upgrade.scout.07.lowRange", 1000, "Q05", "upgrade.lowRange.name",
                (VehicleStatsResolver.Stat.LowRange, VehicleStatsResolver.Operation.Set, 1f));
            Add(8, "upgrade.scout.08.mudTires", 1150, "Q06", "upgrade.mudTires.name",
                (VehicleStatsResolver.Stat.MudTires, VehicleStatsResolver.Operation.Set, 1f));
            Add(9, "upgrade.scout.09.radar1", 1300, "Q07", "upgrade.radar1.name",
                (VehicleStatsResolver.Stat.SignalRadiusM, VehicleStatsResolver.Operation.Set, 140f));
            Add(10, "upgrade.scout.10.engineTune1", 1450, "Q08", "upgrade.engineTune1.name",
                (VehicleStatsResolver.Stat.PeakTorqueNm, VehicleStatsResolver.Operation.Multiply, 1.10f),
                (VehicleStatsResolver.Stat.PoweredFuelDrainFactor, VehicleStatsResolver.Operation.Multiply, 1.04f));
            Add(11, "upgrade.scout.11.reinforcedRack", 1600, "Q09", "upgrade.reinforcedRack.name",
                (VehicleStatsResolver.Stat.CargoSlots, VehicleStatsResolver.Operation.Set, 16f),
                (VehicleStatsResolver.Stat.PayloadKg, VehicleStatsResolver.Operation.Set, 180f));
            Add(12, "upgrade.scout.12.raisedIntake", 1750, "Q10", "upgrade.raisedIntake.name",
                (VehicleStatsResolver.Stat.SafeWadingDepthM, VehicleStatsResolver.Operation.Set,
                    WaterCrossingModel.UpgradedSafeWadingM));
            Add(13, "upgrade.scout.13.diffLock", 1900, "Q11", "upgrade.diffLock.name",
                (VehicleStatsResolver.Stat.DiffLock, VehicleStatsResolver.Operation.Set, 1f));
            Add(14, "upgrade.scout.14.suspension2", 2100, "Q12", "upgrade.suspension2.name",
                (VehicleStatsResolver.Stat.SuspensionTravelM, VehicleStatsResolver.Operation.Set, 0.40f),
                (VehicleStatsResolver.Stat.GroundClearanceM, VehicleStatsResolver.Operation.Set, 0.30f));
            Add(15, "upgrade.scout.15.fieldWinch2", 2300, "Q13", "upgrade.fieldWinch2.name",
                (VehicleStatsResolver.Stat.WinchReachM, VehicleStatsResolver.Operation.Set, 20f),
                (VehicleStatsResolver.Stat.WinchForceCapN, VehicleStatsResolver.Operation.Set, 12000f));
            Add(16, "upgrade.scout.16.tank2", 2500, "Q14", "upgrade.tank2.name",
                (VehicleStatsResolver.Stat.FuelCapacityLiters, VehicleStatsResolver.Operation.Set, 40f));
            Add(17, "upgrade.scout.17.expeditionRack", 2700, "Q15", "upgrade.expeditionRack.name",
                (VehicleStatsResolver.Stat.CargoSlots, VehicleStatsResolver.Operation.Set, 20f),
                (VehicleStatsResolver.Stat.PayloadKg, VehicleStatsResolver.Operation.Set, 240f));
            // §11.2 tier 18 "Cold-Weather Kit": unlock snow tires (the cold-start change is
            // presentation only, so it carries no stat).
            Add(18, "upgrade.scout.18.coldKit", 2900, "Q16", "upgrade.coldKit.name",
                (VehicleStatsResolver.Stat.SnowTires, VehicleStatsResolver.Operation.Set, 1f));
            Add(19, "upgrade.scout.19.radar2", 3100, "Q17", "upgrade.radar2.name",
                (VehicleStatsResolver.Stat.SignalRadiusM, VehicleStatsResolver.Operation.Set, 220f));
            Add(20, "upgrade.scout.20.expeditionCalibration", 3400, "Q19", "upgrade.expeditionCalibration.name",
                (VehicleStatsResolver.Stat.PoweredFuelDrainFactor, VehicleStatsResolver.Operation.Multiply, 0.90f));
        }

        void Add(int tier, string id, int cost, string missionGate, string nameKey,
            params (VehicleStatsResolver.Stat, VehicleStatsResolver.Operation, float)[] effects)
        {
            var def = new UpgradeDefinition
            {
                Id = new ContentId(id),
                Tier = tier,
                CostSalvage = cost,
                RequiredMissionCode = missionGate ?? string.Empty,
                NameKey = nameKey
            };
            if (tier > 1)
                def.Prerequisite = _tiers[tier - 2].Id;
            if (effects != null)
                def.Effects.AddRange(effects);
            _tiers.Add(def);
        }

        public IReadOnlyList<UpgradeDefinition> Tiers => _tiers;
        public int TotalCostSalvage
        {
            get
            {
                int sum = 0;
                foreach (var t in _tiers) sum += t.CostSalvage;
                return sum;
            }
        }

        public bool IsResearched(string upgradeId) => _researched.Contains(upgradeId);

        public UpgradeDefinition ById(ContentId id)
        {
            foreach (var t in _tiers)
                if (t.Id.Value == id.Value) return t;
            throw new KeyNotFoundException($"Unknown upgrade '{id}'.");
        }

        /// <summary>
        /// Sequential purchase (§11.1): previous tier researched, story gate completed, funds available.
        /// Research cost is Salvage only — random material drops can never block the critical path.
        /// </summary>
        public CommandResult TryPurchase(ContentId upgradeId, EconomyService economy)
        {
            var def = ById(upgradeId);
            if (_researched.Contains(def.Id.Value))
                return CommandResult.Fail(FailureReason.AlreadyApplied);

            if (def.Tier > 1 && !_researched.Contains(def.Prerequisite.Value))
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet, "previous tier required");

            if (!string.IsNullOrEmpty(def.RequiredMissionCode) &&
                !_isMissionCompleted(def.RequiredMissionCode))
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet, $"requires {def.RequiredMissionCode}");

            var spend = economy.TrySpend(def.CostSalvage, default);
            if (!spend.Success) return spend;

            _researched.Add(def.Id.Value);
            return CommandResult.Ok();
        }

        /// <summary>QA case 9: cannot remove a capacity module while over capacity (checked by caller with inventory).</summary>
        public bool CanSwapModuleOut(InventoryService inventory, InventoryId cargoId) =>
            inventory == null || !inventory.IsOverCapacity(cargoId);

        /// <summary>
        /// Applies all researched effects to the shared resolver (garage == driving).
        /// <para>
        /// Iterates the tier table in authored tier order — not the researched set — so composition is
        /// deterministic and a higher tier always overrides a lower one. Until 2026-10-19 this walked
        /// the <see cref="_researched"/> HashSet, whose enumeration order is an implementation detail,
        /// so <c>Operation.Set</c> stats (tank size, cargo slots, signal radius) could resolve to an
        /// earlier tier's value after a save/load (DEF-002). The tier is also passed as the modifier
        /// rank, so the ordering holds even if a caller applies effects out of order.
        /// </para>
        /// </summary>
        public void ApplyTo(VehicleStatsResolver resolver)
        {
            for (int i = 0; i < _tiers.Count; i++)
            {
                var def = _tiers[i];
                if (!_researched.Contains(def.Id.Value)) continue;
                resolver.AddUpgradeEffects(def.Effects, def.Id.Value, def.Tier);
            }
        }

        public void Restore(IEnumerable<string> researchedIds)
        {
            _researched.Clear();
            if (researchedIds != null)
                foreach (var id in researchedIds)
                    _researched.Add(id);
        }

        public IEnumerable<string> ResearchedIds => _researched;
    }
}
