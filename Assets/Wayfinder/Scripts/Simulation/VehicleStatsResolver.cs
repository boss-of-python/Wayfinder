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
            SignalRadiusM, DirtGravelGrip, MaxEngineTorqueFactor
        }

        public enum Operation { Add, Multiply, Set }

        public readonly struct StatModifier
        {
            public readonly Stat Stat;
            public readonly Operation Operation;
            public readonly float Value;
            public readonly string SourceId;

            public StatModifier(Stat stat, Operation op, float value, string sourceId)
            {
                Stat = stat;
                Operation = op;
                Value = value;
                SourceId = sourceId ?? string.Empty;
            }
        }

        public void Clear() => _modifiers.Clear();

        public void AddModifier(Stat stat, Operation op, float value, string sourceId) =>
            _modifiers.Add(new StatModifier(stat, op, value, sourceId));

        /// <summary>Adds all modifiers from a researched upgrade definition (Content/upgrades.json effects).</summary>
        public void AddUpgradeEffects(IEnumerable<(Stat stat, Operation op, float value)> effects, string upgradeId)
        {
            if (effects == null) return;
            foreach (var e in effects)
                AddModifier(e.stat, e.op, e.value, upgradeId);
        }

        /// <summary>
        /// Composes modifiers per stat: Set replaces base, then Add accumulates, then Multiply
        /// accumulates; final = (base + Σadd) × Πmul (Set wins as new base).
        /// </summary>
        public float Compose(Stat stat, float baseValue)
        {
            float setBase = baseValue;
            float add = 0f;
            float mul = 1f;
            bool hasSet = false;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                var m = _modifiers[i];
                if (m.Stat != stat) continue;
                switch (m.Operation)
                {
                    case Operation.Set:
                        setBase = m.Value;
                        hasSet = true;
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
                MudTiresUnlocked = mudTiresResearched,
                SnowTiresUnlocked = snowTiresResearched,
                RockTiresUnlocked = rockTiresResearched
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
            float grip = TireSetRules.SurfaceMultiplier(tires, surface, wet, out float lateral);
            stats.EffectiveGrip = Math.Min(grip * stats.TireGripFactor, SurfaceTable.EffectiveGripCap);
            stats.LateralGripFactor = lateral;

            return stats;
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
