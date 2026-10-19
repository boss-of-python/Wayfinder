using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Effective runtime attributes after composition (§11.2 application order):
    /// chassis base → selected researched module → tire/surface lookup → cargo/fuel mass →
    /// condition modifiers → weather modifiers → assists.
    /// Additive and multiplicative modifiers are stored separately (Additive then Multiplicative).
    /// The garage displays EXACTLY these values — one authoritative resolver (handoff rule).
    /// </summary>
    public sealed class EffectiveVehicleStats
    {
        public float DryMassKg;
        public float FuelMassKg;
        public float CargoMassKg;
        public float TotalMassKg => DryMassKg + FuelMassKg + CargoMassKg;

        public float FuelCapacityLiters;
        public float PayloadKg;
        public int CargoSlots;

        public float PeakTorqueNm;
        public float EngineConditionFactor = 1f;
        public float PoweredFuelDrainFactor = 1f;

        public float SuspensionTravelM;
        public float GroundClearanceM;
        public float DampingEffectiveness = 1f;

        public float TireGripFactor = 1f;
        public bool PunctureActive;
        public float PunctureSpeedCapKph = 25f;

        public float SafeWadingDepthM;
        public float WinchReachM;
        public float WinchForceCapN;
        public float SignalRadiusM;
        public float UnderbodyDamageFactor = 1f;
        public bool LowRangeAvailable;
        public bool DiffLockAvailable;
        public bool SnowTiresUnlocked;
        public bool MudTiresUnlocked;
        public bool RockTiresUnlocked;

        public float EffectiveGrip;
        public float LateralGripFactor = 1f;
    }

    /// <summary>
    /// Pure stat composition. No rendering, no Unity types. QA case 8: tire research bonuses
    /// are represented as explicit module modifiers and never re-applied by TireSetRules.
    /// </summary>
    public sealed class VehicleStatsResolver
    {
        readonly List<StatModifier> _modifiers = new List<StatModifier>(32);

        public enum Stat
        {
            FuelCapacityLiters, PayloadKg, CargoSlots, PeakTorqueNm, PoweredFuelDrainFactor,
            SuspensionTravelM, GroundClearanceM, UnderbodyDamageFactor, WinchReachM, WinchForceCapN,
            SignalRadiusM, DirtGravelGrip, MaxEngineTorqueFactor,
            /// <summary>Intake height above the chassis datum (§5.3): 0.55 m baseline, 0.85 m raised.</summary>
            SafeWadingDepthM,
            /// <summary>Feature flags (0 or 1). Research sets them; the resolver exposes them as bools.</summary>
            LowRange, DiffLock, MudTires, SnowTires, RockTires
        }

        public enum Operation { Add, Multiply, Set }

        public readonly struct StatModifier
        {
            public readonly Stat Stat;
            public readonly Operation Operation;
            public readonly float Value;
            public readonly string SourceId;
            /// <summary>
            /// Precedence for <see cref="Operation.Set"/>: the highest rank wins, and list order
            /// breaks ties. Research passes the tier, so a later tier always overrides an earlier
            /// one no matter what order the modifiers were added in (DEF-002).
            /// </summary>
            public readonly int Rank;

            public StatModifier(Stat stat, Operation op, float value, string sourceId)
                : this(stat, op, value, sourceId, 0)
            {
            }

            public StatModifier(Stat stat, Operation op, float value, string sourceId, int rank)
            {
                Stat = stat;
                Operation = op;
                Value = value;
                SourceId = sourceId ?? string.Empty;
                Rank = rank;
            }
        }

        public void Clear() => _modifiers.Clear();

        public void AddModifier(Stat stat, Operation op, float value, string sourceId) =>
            _modifiers.Add(new StatModifier(stat, op, value, sourceId));

        /// <summary>Adds a modifier with an explicit precedence (higher rank overrides a lower one).</summary>
        public void AddModifier(Stat stat, Operation op, float value, string sourceId, int rank) =>
            _modifiers.Add(new StatModifier(stat, op, value, sourceId, rank));

        /// <summary>Adds all modifiers from a researched upgrade definition (Content/upgrades.json effects).</summary>
        public void AddUpgradeEffects(IEnumerable<(Stat stat, Operation op, float value)> effects, string upgradeId) =>
            AddUpgradeEffects(effects, upgradeId, 0);

        /// <summary>
        /// Adds all modifiers from a researched upgrade definition at one precedence (its tier), so
        /// composition is independent of the order the researched set happens to enumerate in.
        /// </summary>
        public void AddUpgradeEffects(IEnumerable<(Stat stat, Operation op, float value)> effects, string upgradeId, int rank)
        {
            if (effects == null) return;
            foreach (var e in effects)
                AddModifier(e.stat, e.op, e.value, upgradeId, rank);
        }

        /// <summary>
        /// Composes modifiers per stat: Set replaces base, then Add accumulates, then Multiply
        /// accumulates; final = (base + Σadd) × Πmul (Set wins as new base).
        /// <para>
        /// Two or more <see cref="Operation.Set"/> modifiers on one stat are resolved by
        /// <see cref="StatModifier.Rank"/> (highest wins; ties fall back to the order added), never by
        /// the order a caller's collection happened to enumerate in. Before 2026-10-19 the last Set in
        /// list order won and research was applied in <c>HashSet</c> order, so a fully-researched Scout
        /// could resolve tier-4 values instead of tier-17 (DEF-002).
        /// </para>
        /// </summary>
        public float Compose(Stat stat, float baseValue)
        {
            float setBase = baseValue;
            float add = 0f;
            float mul = 1f;
            bool hasSet = false;
            int setRank = int.MinValue;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                var m = _modifiers[i];
                if (m.Stat != stat) continue;
                switch (m.Operation)
                {
                    case Operation.Set:
                        if (!hasSet || m.Rank >= setRank)
                        {
                            setBase = m.Value;
                            setRank = m.Rank;
                            hasSet = true;
                        }
                        break;
                    case Operation.Add:
                        add += m.Value;
                        break;
                    case Operation.Multiply:
                        mul *= m.Value;
                        break;
                }
            }
            float value = (hasSet ? setBase : baseValue) + add;
            value *= mul;
            return value;
        }

        /// <summary>
        /// Full resolution used by both driving and garage display.
        /// </summary>
        public EffectiveVehicleStats Resolve(
            VehicleSpec spec,
            float fuelLiters,
            float cargoMassKg,
            TireSet tires,
            SurfaceType surface,
            bool wet,
            int engineCondition0to100,
            int suspensionCondition0to100,
            int tiresCondition0to100,
            bool mudTiresResearched,
            bool snowTiresResearched,
            bool rockTiresResearched)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            var stats = new EffectiveVehicleStats
            {
                DryMassKg = spec.DryMassKg,
                FuelMassKg = Math.Max(0f, fuelLiters) * VehicleSpec.FuelDensityKgPerLiter,
                CargoMassKg = Math.Max(0f, cargoMassKg),
                FuelCapacityLiters = Compose(Stat.FuelCapacityLiters, spec.FuelCapacityLiters),
                PayloadKg = Compose(Stat.PayloadKg, spec.PayloadKg),
                CargoSlots = (int)Compose(Stat.CargoSlots, spec.CargoSlots),
                PeakTorqueNm = Compose(Stat.PeakTorqueNm, spec.PeakTorqueNm),
                SuspensionTravelM = Compose(Stat.SuspensionTravelM, spec.SuspensionTravelM),
                GroundClearanceM = Compose(Stat.GroundClearanceM, spec.GroundClearanceM),
                UnderbodyDamageFactor = Compose(Stat.UnderbodyDamageFactor, 1f),
                WinchReachM = Compose(Stat.WinchReachM, 0f),
                WinchForceCapN = Compose(Stat.WinchForceCapN, 0f),
                SignalRadiusM = Compose(Stat.SignalRadiusM, 80f),
                PoweredFuelDrainFactor = Compose(Stat.PoweredFuelDrainFactor, 1f),
                // §5.3: the intake height is the safe wading depth. Before 2026-10-19 this field was
                // declared but never composed, so every puddle read as "above the intake" and stalled
                // the engine (DEF-001). The baseline is the authored constant, and research raises it.
                SafeWadingDepthM = Compose(Stat.SafeWadingDepthM, WaterCrossingModel.BaselineSafeWadingM),
                LowRangeAvailable = Compose(Stat.LowRange, 0f) >= 0.5f,
                DiffLockAvailable = Compose(Stat.DiffLock, 0f) >= 0.5f,
                // Tire-set availability comes from research (§11.1 tiers 8 and 18), not from the
                // caller. The parameters remain as an explicit override (content or a save that
                // already unlocked a set) and are OR-ed, never the sole source (DEF-004).
                MudTiresUnlocked = mudTiresResearched || Compose(Stat.MudTires, 0f) >= 0.5f,
                SnowTiresUnlocked = snowTiresResearched || Compose(Stat.SnowTires, 0f) >= 0.5f,
                RockTiresUnlocked = rockTiresResearched || Compose(Stat.RockTires, 0f) >= 0.5f
            };

            // Condition modifiers (§5.5): below 50 the stat degrades linearly to the floor at 0.
            float engineC = MathUtil.Clamp(engineCondition0to100, 0, 100);
            stats.EngineConditionFactor = engineC >= 50f
                ? 1f
                : MathUtil.Lerp(0.70f, 1.0f, engineC / 50f);

            float suspC = MathUtil.Clamp(suspensionCondition0to100, 0, 100);
            stats.DampingEffectiveness = suspC >= 50f
                ? 1f
                : MathUtil.Lerp(0.70f, 1.0f, suspC / 50f);

            float tireC = MathUtil.Clamp(tiresCondition0to100, 0, 100);
            stats.TireGripFactor = tireC >= 50f
                ? 1f
                : MathUtil.Lerp(0.80f, 1.0f, tireC / 50f);

            // Tire/surface lookup — tire multipliers compose exactly once here (QA case 8).
            stats.EffectiveGrip = GripFor(tires, surface, wet, stats.TireGripFactor, out float lateral);
            stats.LateralGripFactor = lateral;

            return stats;
        }

        /// <summary>
        /// Grip on one surface: tire-set table → researched Trail Tires I bonus → tire condition → cap.
        /// Trail Tires I is "dirt/gravel grip +10 % relative to baseline" (§11.1 tier 1) and applies
        /// only to the base all-terrain set on packed dirt or gravel; a specialized set already
        /// carries its progression bonus, so it is never granted twice (§11.1 note, QA case 8).
        /// Before 2026-10-15 the DirtGravelGrip modifier was recorded but never composed (KI-032).
        /// </summary>
        public float GripFor(TireSet tires, SurfaceType surface, bool wet, float tireConditionFactor, out float lateralMultiplier)
        {
            float grip = TireSetRules.SurfaceMultiplier(tires, surface, wet, out lateralMultiplier);
            if (tires == TireSet.AllTerrain && (surface == SurfaceType.PackedDirt || surface == SurfaceType.Gravel))
                grip *= Compose(Stat.DirtGravelGrip, 1f);
            return Math.Min(grip * tireConditionFactor, SurfaceTable.EffectiveGripCap);
        }
    }

    /// <summary>Shared equipment slot identifiers (§10.2).</summary>
    public static class EquipmentSlots
    {
        public const string Tires = "tires";
        public const string Suspension = "suspension";
        public const string EngineTune = "engineTune";
        public const string Tank = "tank";
        public const string Intake = "intake";
        public const string Differential = "differential";
        public const string CargoRack = "cargoRack";
        public const string Sensor = "sensor";
        public const string Winch = "winch";
        public const string Utility1 = "utility1";
        public const string Utility2 = "utility2";

        public static readonly string[] All =
        {
            Tires, Suspension, EngineTune, Tank, Intake, Differential,
            CargoRack, Sensor, Winch, Utility1, Utility2
        };
    }
}
