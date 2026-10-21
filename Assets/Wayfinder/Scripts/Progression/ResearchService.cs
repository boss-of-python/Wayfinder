using System;
using System.Collections.Generic;
using System.Linq;
using Wayfinder.Core;
using Wayfinder.Simulation;

namespace Wayfinder.Progression
{
    /// <summary>
    /// §10.2 equipment slots. A permanent progression module occupies its named slot and never
    /// competes with consumable cargo: one tire set, one suspension package, one engine tune, one
    /// tank package, one intake package, one differential package, one cargo rack, one sensor, one
    /// winch, and two utility slots.
    /// </summary>
    public enum ModuleSlot
    {
        /// <summary>Occupies no slot: a researched unlock (a swappable tire set), not a module.</summary>
        None = 0,
        Tires, Suspension, Engine, Tank, Intake, Differential, CargoRack, Sensor, Winch, Utility
    }

    /// <summary>
    /// Twenty-tier Scout research tree (§11.2). Costs are INCREMENTAL, Σ = 32,200 Salvage.
    /// Tiers unlock sequentially; story gate missions must be completed first.
    /// Stat effects compose through VehicleStatsResolver (shared with garage display) — and since
    /// §10.2 gives each module a named slot, only the module *selected* in a slot composes.
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
            /// <summary>The §10.2 slot this module occupies; <see cref="ModuleSlot.None"/> for unlocks.</summary>
            public ModuleSlot Slot;
            public string RequiredMissionCode;   // e.g. "Q04"; empty = none
            public readonly List<(VehicleStatsResolver.Stat stat, VehicleStatsResolver.Operation op, float value)> Effects =
                new List<(VehicleStatsResolver.Stat, VehicleStatsResolver.Operation, float)>();
            public string ResultDescriptionKey;
        }

        public static readonly ResearchService Global = new ResearchService();

        readonly List<UpgradeDefinition> _tiers = new List<UpgradeDefinition>(20);
        readonly HashSet<string> _researched = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Selected modules, in tier order (§10.2). A slot holds one; Utility holds two.</summary>
        readonly List<string> _selected = new List<string>();
        readonly Func<string, bool> _isMissionCompleted;

        /// <summary>§10.2: "two utility slots". Every other named slot holds exactly one module.</summary>
        public const int UtilitySlots = 2;

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
            Add(1, "upgrade.scout.01.trailTires1", 200, "Q01", "upgrade.trailTires1.name", ModuleSlot.Tires,
                (VehicleStatsResolver.Stat.DirtGravelGrip, VehicleStatsResolver.Operation.Multiply, 1.10f));
            Add(2, "upgrade.scout.02.underbodyGuard", 300, "Q02", "upgrade.underbodyGuard.name", ModuleSlot.Utility,
                (VehicleStatsResolver.Stat.UnderbodyDamageFactor, VehicleStatsResolver.Operation.Multiply, 0.75f));
            Add(3, "upgrade.scout.03.fieldWinch1", 450, "Q02", "upgrade.fieldWinch1.name", ModuleSlot.Winch,
                (VehicleStatsResolver.Stat.WinchReachM, VehicleStatsResolver.Operation.Set, 12f),
                (VehicleStatsResolver.Stat.WinchForceCapN, VehicleStatsResolver.Operation.Set, 8000f));
            Add(4, "upgrade.scout.04.cargoOrganizer", 550, "Q03", "upgrade.cargoOrganizer.name", ModuleSlot.CargoRack,
                (VehicleStatsResolver.Stat.CargoSlots, VehicleStatsResolver.Operation.Set, 14f));
            Add(5, "upgrade.scout.05.suspension1", 700, "Q04", "upgrade.suspension1.name", ModuleSlot.Suspension,
                (VehicleStatsResolver.Stat.SuspensionTravelM, VehicleStatsResolver.Operation.Set, 0.34f),
                (VehicleStatsResolver.Stat.GroundClearanceM, VehicleStatsResolver.Operation.Set, 0.27f));
            Add(6, "upgrade.scout.06.tank1", 850, "Q04", "upgrade.tank1.name", ModuleSlot.Tank,
                (VehicleStatsResolver.Stat.FuelCapacityLiters, VehicleStatsResolver.Operation.Set, 30f));
            // Tiers 7, 8, 12, 13 and 18 cost 9,100 Salvage (28 % of the tree) and declared no
            // effects at all until 2026-10-19 (DEF-004): low range and diff lock had no Stat member
            // to write, the raised intake's 0.85 m constant was never composed, and the two tire
            // unlocks were left to callers that hardcoded `true,true,true` / `false,false,false`.
            Add(7, "upgrade.scout.07.lowRange", 1000, "Q05", "upgrade.lowRange.name", ModuleSlot.Utility,
                (VehicleStatsResolver.Stat.LowRange, VehicleStatsResolver.Operation.Set, 1f));
            Add(8, "upgrade.scout.08.mudTires", 1150, "Q06", "upgrade.mudTires.name", ModuleSlot.None,
                (VehicleStatsResolver.Stat.MudTires, VehicleStatsResolver.Operation.Set, 1f));
            Add(9, "upgrade.scout.09.radar1", 1300, "Q07", "upgrade.radar1.name", ModuleSlot.Sensor,
                (VehicleStatsResolver.Stat.SignalRadiusM, VehicleStatsResolver.Operation.Set, 140f));
            Add(10, "upgrade.scout.10.engineTune1", 1450, "Q08", "upgrade.engineTune1.name", ModuleSlot.Engine,
                (VehicleStatsResolver.Stat.PeakTorqueNm, VehicleStatsResolver.Operation.Multiply, 1.10f),
                (VehicleStatsResolver.Stat.PoweredFuelDrainFactor, VehicleStatsResolver.Operation.Multiply, 1.04f));
            Add(11, "upgrade.scout.11.reinforcedRack", 1600, "Q09", "upgrade.reinforcedRack.name", ModuleSlot.CargoRack,
                (VehicleStatsResolver.Stat.CargoSlots, VehicleStatsResolver.Operation.Set, 16f),
                (VehicleStatsResolver.Stat.PayloadKg, VehicleStatsResolver.Operation.Set, 180f));
            Add(12, "upgrade.scout.12.raisedIntake", 1750, "Q10", "upgrade.raisedIntake.name", ModuleSlot.Intake,
                (VehicleStatsResolver.Stat.SafeWadingDepthM, VehicleStatsResolver.Operation.Set,
                    WaterCrossingModel.UpgradedSafeWadingM));
            Add(13, "upgrade.scout.13.diffLock", 1900, "Q11", "upgrade.diffLock.name", ModuleSlot.Differential,
                (VehicleStatsResolver.Stat.DiffLock, VehicleStatsResolver.Operation.Set, 1f),
                // §11.2 tier 13 also unlocks the rock tire set. `Stat.RockTires` existed and nothing
                // ever set it, so the Rock set was unreachable (found while wiring §10.2 slots).
                (VehicleStatsResolver.Stat.RockTires, VehicleStatsResolver.Operation.Set, 1f));
            Add(14, "upgrade.scout.14.suspension2", 2100, "Q12", "upgrade.suspension2.name", ModuleSlot.Suspension,
                (VehicleStatsResolver.Stat.SuspensionTravelM, VehicleStatsResolver.Operation.Set, 0.40f),
                (VehicleStatsResolver.Stat.GroundClearanceM, VehicleStatsResolver.Operation.Set, 0.30f));
            Add(15, "upgrade.scout.15.fieldWinch2", 2300, "Q13", "upgrade.fieldWinch2.name", ModuleSlot.Winch,
                (VehicleStatsResolver.Stat.WinchReachM, VehicleStatsResolver.Operation.Set, 20f),
                (VehicleStatsResolver.Stat.WinchForceCapN, VehicleStatsResolver.Operation.Set, 12000f));
            Add(16, "upgrade.scout.16.tank2", 2500, "Q14", "upgrade.tank2.name", ModuleSlot.Tank,
                (VehicleStatsResolver.Stat.FuelCapacityLiters, VehicleStatsResolver.Operation.Set, 40f));
            Add(17, "upgrade.scout.17.expeditionRack", 2700, "Q15", "upgrade.expeditionRack.name", ModuleSlot.CargoRack,
                (VehicleStatsResolver.Stat.CargoSlots, VehicleStatsResolver.Operation.Set, 20f),
                (VehicleStatsResolver.Stat.PayloadKg, VehicleStatsResolver.Operation.Set, 240f));
            // §11.2 tier 18 "Cold-Weather Kit": unlock snow tires (the cold-start change is
            // presentation only, so it carries no stat).
            Add(18, "upgrade.scout.18.coldKit", 2900, "Q16", "upgrade.coldKit.name", ModuleSlot.None,
                (VehicleStatsResolver.Stat.SnowTires, VehicleStatsResolver.Operation.Set, 1f));
            Add(19, "upgrade.scout.19.radar2", 3100, "Q17", "upgrade.radar2.name", ModuleSlot.Sensor,
                (VehicleStatsResolver.Stat.SignalRadiusM, VehicleStatsResolver.Operation.Set, 220f));
            // §10.2 allows ONE engine tune, so selecting tier 20 replaces tier 10 in that slot.
            // §11.2 requires "explicit final stat composition": the capstone therefore carries the
            // torque curve forward rather than silently dropping Engine Tune I's ×1.10.
            Add(20, "upgrade.scout.20.expeditionCalibration", 3400, "Q19", "upgrade.expeditionCalibration.name", ModuleSlot.Engine,
                (VehicleStatsResolver.Stat.PeakTorqueNm, VehicleStatsResolver.Operation.Multiply, 1.10f),
                (VehicleStatsResolver.Stat.PoweredFuelDrainFactor, VehicleStatsResolver.Operation.Multiply, 0.90f));
        }

        void Add(int tier, string id, int cost, string missionGate, string nameKey, ModuleSlot slot,
            params (VehicleStatsResolver.Stat, VehicleStatsResolver.Operation, float)[] effects)
        {
            var def = new UpgradeDefinition
            {
                Id = new ContentId(id),
                Tier = tier,
                CostSalvage = cost,
                RequiredMissionCode = missionGate ?? string.Empty,
                NameKey = nameKey,
                Slot = slot
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
            // Researching a module fits it (§10.2): the tree is strictly progressive, so the newest
            // module in a slot is the default selection. The player may still fit an earlier one, or
            // none at all — dropping the Expedition Rack's loaded-COM penalty or the diff lock's
            // turning scrub is a real preparation choice. A full Utility slot simply leaves the new
            // module unfitted; it stays researched and can be fitted later.
            AutoFit(def);
            return CommandResult.Ok();
        }

        // ================================================================== §10.2 equipment slots

        /// <summary>The modules currently fitted, in tier order.</summary>
        public IReadOnlyList<string> FittedIds => _selected;

        public bool IsFitted(string upgradeId) => _selected.Contains(upgradeId);

        /// <summary>How many modules a slot holds: two for Utility, one for every named slot.</summary>
        public static int SlotCapacity(ModuleSlot slot) => slot == ModuleSlot.Utility ? UtilitySlots : 1;

        /// <summary>The modules fitted in one slot, in tier order (at most <see cref="SlotCapacity"/>).</summary>
        public List<string> ModulesInSlot(ModuleSlot slot)
        {
            var list = new List<string>();
            if (slot == ModuleSlot.None) return list;
            foreach (var def in _tiers)
                if (def.Slot == slot && _selected.Contains(def.Id.Value)) list.Add(def.Id.Value);
            return list;
        }

        /// <summary>The module fitted in a single-occupancy slot, or null.</summary>
        public string ModuleInSlot(ModuleSlot slot)
        {
            var list = ModulesInSlot(slot);
            return list.Count == 0 ? null : list[list.Count - 1];
        }

        public ModuleSlot SlotOf(string upgradeId) => TryFind(upgradeId, out var def) ? def.Slot : ModuleSlot.None;

        /// <summary>
        /// Fits a researched module into its §10.2 slot. A named slot holds one module, so fitting
        /// replaces its occupant; Utility holds two and refuses a third until one is removed.
        /// Slot-less tiers are unlocks (a swappable tire set) and occupy no slot.
        /// </summary>
        public CommandResult Fit(ContentId upgradeId)
        {
            if (!TryFind(upgradeId.Value, out var def))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown upgrade '{upgradeId}'");
            if (!_researched.Contains(def.Id.Value))
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet, "research the module first (§11.2)");
            if (def.Slot == ModuleSlot.None)
                return CommandResult.Fail(FailureReason.InvalidTarget,
                    "that tier unlocks a tire set; it occupies no slot (§10.2)");
            if (_selected.Contains(def.Id.Value))
                return CommandResult.Fail(FailureReason.AlreadyApplied, "that module is already fitted");
            if (def.Slot == ModuleSlot.Utility && ModulesInSlot(ModuleSlot.Utility).Count >= UtilitySlots)
                return CommandResult.Fail(FailureReason.OverCapacity,
                    "both utility slots are occupied; remove a module first (§10.2)");
            AutoFit(def);
            return CommandResult.Ok();
        }

        /// <summary>
        /// True when <see cref="Fit"/> would succeed right now, so a UI can enable a button without
        /// restating the §10.2 slot rules: researched, occupies a slot, not already fitted, and the
        /// slot still has room (a named slot always has room — fitting replaces its occupant).
        /// </summary>
        public bool CanFit(string upgradeId) =>
            TryFind(upgradeId, out var def) && _researched.Contains(def.Id.Value) && def.Slot != ModuleSlot.None
            && !_selected.Contains(def.Id.Value)
            && (def.Slot != ModuleSlot.Utility || ModulesInSlot(ModuleSlot.Utility).Count < UtilitySlots);

        /// <summary>Removes a fitted module. It stays researched and can be fitted again (§17.5).</summary>
        public CommandResult Unfit(ContentId upgradeId)
        {
            if (!TryFind(upgradeId.Value, out var def))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown upgrade '{upgradeId}'");
            if (!_selected.Remove(def.Id.Value))
                return CommandResult.Fail(FailureReason.ItemNotFound, "that module is not fitted");
            return CommandResult.Ok();
        }

        /// <summary>The tire set mounted on the vehicle (§10.2: one tire set, mutually exclusive).</summary>
        public TireSet SelectedTireSet { get; private set; } = TireSet.AllTerrain;

        /// <summary>
        /// A tire set is unlocked by RESEARCH (§11.2 tiers 8, 13, 18), not by which module is fitted,
        /// so removing the differential lock never un-unlocks the rock set.
        /// </summary>
        public bool IsTireSetUnlocked(TireSet set)
        {
            switch (set)
            {
                case TireSet.Mud: return ResearchedFlag(VehicleStatsResolver.Stat.MudTires);
                case TireSet.Rock: return ResearchedFlag(VehicleStatsResolver.Stat.RockTires);
                case TireSet.Snow: return ResearchedFlag(VehicleStatsResolver.Stat.SnowTires);
                default: return true;   // all-terrain is the baseline set every vehicle mounts
            }
        }

        public List<TireSet> UnlockedTireSets()
        {
            var list = new List<TireSet>();
            foreach (TireSet set in Enum.GetValues(typeof(TireSet)))
                if (IsTireSetUnlocked(set)) list.Add(set);
            return list;
        }

        /// <summary>Mounts a tire set (§10.2). Requires the research that unlocks it.</summary>
        public CommandResult SelectTireSet(TireSet set)
        {
            if (!Enum.IsDefined(typeof(TireSet), set))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown tire set '{set}'");
            if (!IsTireSetUnlocked(set))
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet,
                    $"the {set} tire set is not researched yet (§11.2)");
            SelectedTireSet = set;
            return CommandResult.Ok();
        }

        bool ResearchedFlag(VehicleStatsResolver.Stat stat)
        {
            foreach (var def in _tiers)
            {
                if (!_researched.Contains(def.Id.Value)) continue;
                foreach (var e in def.Effects)
                    if (e.stat == stat && e.op == VehicleStatsResolver.Operation.Set && e.value >= 0.5f) return true;
            }
            return false;
        }

        /// <summary>Replaces a single-occupancy slot's module, or takes a free Utility slot; keeps tier order.</summary>
        void AutoFit(UpgradeDefinition def)
        {
            if (def.Slot == ModuleSlot.None || _selected.Contains(def.Id.Value)) return;
            if (def.Slot == ModuleSlot.Utility)
            {
                if (ModulesInSlot(ModuleSlot.Utility).Count >= UtilitySlots) return;
            }
            else
            {
                _selected.RemoveAll(id => SlotOf(id) == def.Slot);
            }
            InsertInTierOrder(def.Id.Value);
        }

        void InsertInTierOrder(string upgradeId)
        {
            int tier = TryFind(upgradeId, out var def) ? def.Tier : int.MaxValue;
            for (int i = 0; i < _selected.Count; i++)
            {
                if (!TryFind(_selected[i], out var other) || other.Tier > tier)
                {
                    _selected.Insert(i, upgradeId);
                    return;
                }
            }
            _selected.Add(upgradeId);
        }

        bool TryFind(string upgradeId, out UpgradeDefinition def)
        {
            foreach (var t in _tiers)
                if (t.Id.Value == upgradeId) { def = t; return true; }
            def = null;
            return false;
        }

        /// <summary>QA case 9: cannot remove a capacity module while over capacity (checked by caller with inventory).</summary>
        public bool CanSwapModuleOut(InventoryService inventory, InventoryId cargoId) =>
            inventory == null || !inventory.IsOverCapacity(cargoId);

        /// <summary>
        /// Applies the *selected* loadout to the shared resolver (garage == driving).
        /// <para>
        /// Iterates the tier table in authored tier order — not the researched set — so composition is
        /// deterministic and a higher tier always overrides a lower one. Until 2026-10-19 this walked
        /// the <see cref="_researched"/> HashSet, whose enumeration order is an implementation detail,
        /// so <c>Operation.Set</c> stats (tank size, cargo slots, signal radius) could resolve to an
        /// earlier tier's value after a save/load (DEF-002). The tier is also passed as the modifier
        /// rank, so the ordering holds even if a caller applies effects out of order.
        /// </para>
        /// <para>
        /// §11.2's stat order is "chassis base → **selected** researched module → tire/surface
        /// lookup", and §10.2 gives each module a named slot holding one module. So a slotted tier
        /// composes only while it is the one fitted; a slot-less tier (a tire-set unlock) composes
        /// whenever it is researched.
        /// </para>
        /// </summary>
        public void ApplyTo(VehicleStatsResolver resolver)
        {
            for (int i = 0; i < _tiers.Count; i++)
            {
                var def = _tiers[i];
                if (!_researched.Contains(def.Id.Value)) continue;
                if (def.Slot != ModuleSlot.None && !_selected.Contains(def.Id.Value)) continue;
                resolver.AddUpgradeEffects(def.Effects, def.Id.Value, def.Tier);
            }
        }

        /// <summary>
        /// Restores research from a save written before §10.2 slots existed: every researched module
        /// was treated as fitted (D-023), so the loadout is reconciled by <see cref="Restore(IEnumerable{string}, IEnumerable{string}, TireSet)"/>.
        /// </summary>
        /// <summary>
        /// The content spelling of a slot. The strings come from <see cref="EquipmentSlots"/>, the
        /// §10.2 vocabulary Wayfinder.Simulation already publishes, so upgrades.json, the tier table
        /// and the resolver cannot drift apart on names. Utility is one slot KIND with two instances
        /// (<see cref="UtilitySlots"/>), spelled "utility" rather than "utility1"/"utility2": a module
        /// does not choose which of the two it occupies, and "none" is a research unlock, not a slot.
        /// </summary>
        public const string UtilitySlotName = "utility";
        public const string NoSlotName = "none";

        public static string SlotName(ModuleSlot slot) => slot switch
        {
            ModuleSlot.Tires => EquipmentSlots.Tires,
            ModuleSlot.Suspension => EquipmentSlots.Suspension,
            ModuleSlot.Engine => EquipmentSlots.EngineTune,
            ModuleSlot.Tank => EquipmentSlots.Tank,
            ModuleSlot.Intake => EquipmentSlots.Intake,
            ModuleSlot.Differential => EquipmentSlots.Differential,
            ModuleSlot.CargoRack => EquipmentSlots.CargoRack,
            ModuleSlot.Sensor => EquipmentSlots.Sensor,
            ModuleSlot.Winch => EquipmentSlots.Winch,
            ModuleSlot.Utility => UtilitySlotName,
            _ => NoSlotName
        };

        /// <summary>
        /// Parses the content spelling of a slot name, so upgrades.json and the canonical tier table
        /// share one authority for the §10.2 vocabulary instead of two lists that can drift.
        /// </summary>
        public static bool TryParseSlot(string text, out ModuleSlot slot)
        {
            slot = ModuleSlot.None;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (ModuleSlot value in Enum.GetValues(typeof(ModuleSlot)))
                if (string.Equals(SlotName(value), text, StringComparison.OrdinalIgnoreCase))
                {
                    slot = value; return true;
                }
            return false;
        }

        public void Restore(IEnumerable<string> researchedIds) => Restore(researchedIds, null, TireSet.AllTerrain);

        /// <summary>
        /// Restores research, the fitted loadout and the mounted tire set. A null
        /// <paramref name="fittedIds"/> means "a save from before slot equipping": reconciling keeps
        /// the highest tier per slot — exactly what Set-rank composition produced before — and drops
        /// the rest. Research is never lost (§17.5); a dropped module can be fitted again at any
        /// garage. A tire set that is not unlocked falls back to all-terrain.
        /// </summary>
        public void Restore(IEnumerable<string> researchedIds, IEnumerable<string> fittedIds, TireSet tireSet)
        {
            _researched.Clear();
            if (researchedIds != null)
                foreach (var id in researchedIds)
                    if (!string.IsNullOrEmpty(id)) _researched.Add(id);

            var wanted = fittedIds == null
                ? new HashSet<string>(_researched, StringComparer.Ordinal)
                : new HashSet<string>(fittedIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            _selected.Clear();
            foreach (var def in _tiers)   // ascending tier order, so the highest tier wins each slot
            {
                if (!_researched.Contains(def.Id.Value) || !wanted.Contains(def.Id.Value)) continue;
                if (def.Slot == ModuleSlot.None) continue;   // unlocks occupy no slot
                if (def.Slot == ModuleSlot.Utility)
                {
                    if (ModulesInSlot(ModuleSlot.Utility).Count < UtilitySlots) _selected.Add(def.Id.Value);
                    continue;
                }
                _selected.RemoveAll(id => SlotOf(id) == def.Slot);
                _selected.Add(def.Id.Value);
            }
            SelectedTireSet = IsTireSetUnlocked(tireSet) ? tireSet : TireSet.AllTerrain;
        }

        public IEnumerable<string> ResearchedIds => _researched;
    }
}
