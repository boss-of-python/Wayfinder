using System;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Fuel drain design model (§5.1). Not a claim of real-world L/km:
    /// litersPerSecond = 0.003 + 0.025 × throttle + 0.015 × load + 0.004 × slipSeverity
    /// load and slipSeverity clamped 0..1. Engine off consumes zero.
    /// </summary>
    public static class FuelModel
    {
        public const float BaseDrainPerSecond = 0.003f;
        public const float ThrottleCoefficient = 0.025f;
        public const float LoadCoefficient = 0.015f;
        public const float SlipCoefficient = 0.004f;
        public const float WarningFractionThirty = 0.30f;
        public const float WarningFractionFifteen = 0.15f;
        public const float AutoStopIdleSeconds = 15f;

        public static float LitersPerSecond(float throttle, float load, float slipSeverity, bool engineOn)
        {
            if (!engineOn) return 0f;
            throttle = MathUtil.Clamp01(throttle);
            load = MathUtil.Clamp01(load);
            slipSeverity = MathUtil.Clamp01(slipSeverity);
            return BaseDrainPerSecond
                   + ThrottleCoefficient * throttle
                   + LoadCoefficient * load
                   + SlipCoefficient * slipSeverity;
        }

        /// <summary>QA example from §5.1: throttle 0.45, load 0.50, slip 0.20 → 0.02255 L/s.</summary>
        public const float BlueprintExampleRate = 0.02255f;

        /// <summary>
        /// Integrates consumption with simulation delta (NEVER wall-clock delta).
        /// Returns remaining liters clamped to [0, capacity]; fuel never leaves valid bounds (QA case 11).
        /// </summary>
        public static float Integrate(float currentLiters, float capacityLiters, float litersPerSecond, float simulationDeltaSeconds)
        {
            if (capacityLiters < 0f) throw new ArgumentOutOfRangeException(nameof(capacityLiters));
            float next = currentLiters - Math.Max(0f, litersPerSecond) * Math.Max(0f, simulationDeltaSeconds);
            return MathUtil.Clamp(next, 0f, capacityLiters);
        }

        /// <summary>
        /// Load estimate from demanded engine torque (normalized 0..1), with slope/rolling
        /// resistance contribution. Never read wheel noise as instantaneous full load (§5.1).
        /// </summary>
        public static float EstimateLoad(float normalizedTorqueDemand, float slopeRadians, float rollingResistanceCrr)
        {
            normalizedTorqueDemand = MathUtil.Clamp01(normalizedTorqueDemand);
            // sin(slope) contribution normalized against 0.5 g; Crr scaled from 0.2 worst surface.
            float slopeLoad = Math.Abs((float)Math.Sin(slopeRadians)) * 0.5f;
            float rollingLoad = MathUtil.Clamp01(rollingResistanceCrr / 0.20f) * 0.25f;
            return MathUtil.Clamp01(normalizedTorqueDemand * 0.75f + slopeLoad + rollingLoad);
        }

        /// <summary>
        /// Slip severity for fuel/audio from wheel slip ratio (§4.4):
        /// (wheelAngularSpeed × radius − longitudinalSpeed) / max(|longitudinalSpeed|, 1 m/s).
        /// </summary>
        public static float SlipRatio(float wheelAngularSpeedRadS, float wheelRadiusM, float longitudinalSpeedMs)
        {
            float denom = Math.Max(Math.Abs(longitudinalSpeedMs), 1f);
            return (wheelAngularSpeedRadS * wheelRadiusM - longitudinalSpeedMs) / denom;
        }

        public static float SlipSeverity(Span<float> wheelSlipRatios)
        {
            if (wheelSlipRatios.Length == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < wheelSlipRatios.Length; i++)
                sum += Math.Abs(wheelSlipRatios[i]);
            return MathUtil.Clamp01((sum / wheelSlipRatios.Length) / 1.5f);
        }

        /// <summary>Consumes one fuel can: min(6, capacity − current) liters (§10.3).</summary>
        public static float ApplyFuelCan(float currentLiters, float capacityLiters, out float wastedLiters)
        {
            float add = Math.Min(6f, capacityLiters - currentLiters);
            if (add < 0f) add = 0f;
            wastedLiters = 6f - add;
            return MathUtil.Clamp(currentLiters + add, 0f, capacityLiters);
        }
    }

    /// <summary>
    /// Damage model (§5.5). Four independent condition values 0..100:
    /// engine, suspension, tires, body.
    /// </summary>
    public sealed class DamageModel
    {
        public const float IgnoreImpactSpeedMs = 3f;
        public const float MaxDamagePerIncident = 35f;
        public const float DebounceSeconds = 0.4f;
        public const float WaterStallConditionDrop = 10f;
        /// <summary>
        /// Tires below this condition count as punctured: speed capped at 25 km/h until patched (§5.5).
        /// v1 models the puncture as a condition band, so no extra persisted state is needed (D-029).
        /// </summary>
        public const int PunctureThreshold = 30;

        public int EngineCondition = 100;
        public int SuspensionCondition = 100;
        public int TiresCondition = 100;
        public int BodyCondition = 100;

        float _debounceTimer;

        public void Tick(float deltaSeconds)
        {
            _debounceTimer = Math.Max(0f, _debounceTimer - deltaSeconds);
        }

        /// <summary>
        /// D = clamp(1.4 × (impactSpeed − 3)², 0, 35) per incident, using relative normal speed
        /// BEFORE resolution (call once — do not also count the impulse). Contacts below 3 m/s
        /// are ignored. Related contacts debounce over 0.4 s.
        /// </summary>
        public bool ApplyImpact(float impactSpeedMs, HitZone zone, bool underbodyGuardInstalled)
        {
            if (impactSpeedMs < IgnoreImpactSpeedMs) return false;
            if (_debounceTimer > 0f) return false;
            _debounceTimer = DebounceSeconds;

            float damage = MathUtil.Clamp(1.4f * (impactSpeedMs - IgnoreImpactSpeedMs) * (impactSpeedMs - IgnoreImpactSpeedMs),
                0f, MaxDamagePerIncident);

            if (zone == HitZone.Underbody && underbodyGuardInstalled)
                damage *= 0.75f; // tier 2 Underbody Guard

            int d = (int)Math.Round(damage);
            switch (zone)
            {
                case HitZone.Engine:
                    EngineCondition = Clamp0to100(EngineCondition - d);
                    break;
                case HitZone.Suspension:
                    SuspensionCondition = Clamp0to100(SuspensionCondition - d);
                    break;
                case HitZone.Tires:
                    // Never damage all tires for one tiny rock: single-incident tire damage is halved.
                    TiresCondition = Clamp0to100(TiresCondition - d / 2);
                    break;
                case HitZone.Body:
                default:
                    BodyCondition = Clamp0to100(BodyCondition - d);
                    break;
            }
            return true;
        }

        /// <summary>One aggregated landing event for large falls (§5.5).</summary>
        public void ApplyAggregatedLanding(float impactSpeedMs, bool underbodyGuardInstalled) =>
            ApplyImpact(impactSpeedMs, HitZone.Underbody, underbodyGuardInstalled);

        /// <summary>Field repair kit restores 35 condition to one chosen subsystem (§5.5).</summary>
        public CommandResult UseRepairKit(HitZone subsystem)
        {
            switch (subsystem)
            {
                case HitZone.Engine:
                    EngineCondition = Clamp0to100(EngineCondition + 35);
                    break;
                case HitZone.Suspension:
                    SuspensionCondition = Clamp0to100(SuspensionCondition + 35);
                    break;
                case HitZone.Tires:
                    TiresCondition = Clamp0to100(TiresCondition + 35);
                    break;
                case HitZone.Body:
                    BodyCondition = Clamp0to100(BodyCondition + 35);
                    break;
                default:
                    return CommandResult.Fail(FailureReason.InvalidTarget);
            }
            return CommandResult.Ok();
        }

        public bool HasPuncture => TiresCondition < PunctureThreshold;

        /// <summary>
        /// Tire patch "removes one puncture" (§10.3): lifts tires to the puncture threshold, which
        /// removes the 25 km/h cap while the below-50 grip penalty stays until a garage repair.
        /// </summary>
        public CommandResult UseTirePatch()
        {
            if (!HasPuncture) return CommandResult.Fail(FailureReason.ItemNotUsableHere, "no puncture to patch");
            TiresCondition = PunctureThreshold;
            return CommandResult.Ok();
        }

        bool _waterIncidentActive;

        /// <summary>True from the stall until the incident is ended by repair/recovery.</summary>
        public bool WaterIncidentActive => _waterIncidentActive;

        /// <summary>
        /// Water stall: engine condition drops 10 ONCE per incident (§5.3). Idempotent while the
        /// incident is active, so neither deep submersion nor a duplicate caller can drain
        /// condition repeatedly. Returns true only when the drop was applied.
        /// </summary>
        public bool ApplyWaterStall()
        {
            if (_waterIncidentActive) return false;
            _waterIncidentActive = true;
            EngineCondition = Clamp0to100(EngineCondition - (int)WaterStallConditionDrop);
            return true;
        }

        /// <summary>Ends the water incident (vehicle recovered/repaired out of the water).</summary>
        public void EndWaterIncident()
        {
            _waterIncidentActive = false;
        }

        public void RepairFully()
        {
            EngineCondition = SuspensionCondition = TiresCondition = BodyCondition = 100;
            _waterIncidentActive = false;
        }

        public bool EngineDisabled => EngineCondition <= 0;

        static int Clamp0to100(int v) => v < 0 ? 0 : (v > 100 ? 100 : v);
    }

    public enum HitZone
    {
        Engine,
        Suspension,
        Tires,
        Body,
        Underbody
    }
}
