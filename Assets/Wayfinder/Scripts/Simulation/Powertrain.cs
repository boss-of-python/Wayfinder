using System;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Powertrain model (§4.3): torque curve, automatic gearbox, AWD torque split, brakes.
    /// Pure math — VehicleController applies results to WheelColliders.
    /// </summary>
    public sealed class Powertrain
    {
        public const float UpshiftRpmLightLoad = 3800f;
        public const float UpshiftRpmHeavyLoad = 4400f;
        public const float DownshiftRpm = 1600f;
        public const float ShiftCooldownSeconds = 0.6f;
        public const float TorqueTransitionSeconds = 0.2f;
        public const float LowRangeRatio = 1.8f;
        public const float LowRangeSpeedCapKph = 25f;
        public const float ReverseStallGuardSpeedMs = 0.3f;
        public const float ReverseStallGuardSeconds = 0.4f;

        public int GearIndex { get; private set; }
        public float EngineRpm { get; private set; }
        public bool InReverse { get; private set; }
        public bool LowRangeEngaged { get; private set; }
        public bool DiffLockEngaged { get; private set; }

        float _shiftCooldown;
        float _torqueBlend = 1f;
        float _stationaryTimer;
        float _lastCommandedTorque;

        public void Reset(VehicleSpec spec)
        {
            GearIndex = 0;
            EngineRpm = spec.IdleRpm;
            InReverse = false;
            _shiftCooldown = 0f;
            _torqueBlend = 1f;
            _stationaryTimer = 0f;
        }

        /// <summary>Linear interpolation of the authored torque curve (§4.3).</summary>
        public static float TorqueAtRpm(VehicleSpec spec, float rpm)
        {
            var curve = spec.TorqueCurve;
            if (curve == null || curve.Length == 0) return 0f;
            if (rpm <= curve[0][0]) return curve[0][1];
            if (rpm >= curve[curve.Length - 1][0]) return curve[curve.Length - 1][1];
            for (int i = 1; i < curve.Length; i++)
            {
                if (rpm <= curve[i][0])
                {
                    float t = (rpm - curve[i - 1][0]) / (curve[i][0] - curve[i - 1][0]);
                    return MathUtil.Lerp(curve[i - 1][1], curve[i][1], t);
                }
            }
            return curve[curve.Length - 1][1];
        }

        /// <summary>
        /// Steps the gearbox/engine for one physics tick.
        /// wheelSpeedMs: representative driven-wheel linear speed. throttle 0..1, brake 0..1.
        /// Returns total drive torque to distribute across grounded drive wheels (not per-wheel).
        /// </summary>
        public float Step(
            VehicleSpec spec,
            EffectiveVehicleStats stats,
            float wheelSpeedMs,
            float throttle,
            float brake,
            bool handbrake,
            bool crawlEngaged,
            float deltaSeconds,
            out bool reverseGearIndicated)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            if (deltaSeconds <= 0f) deltaSeconds = 1f / 60f;

            float speedKph = Math.Abs(wheelSpeedMs) * 3.6f;
            if (crawlEngaged)
                speedKph = Math.Min(speedKph, 8f); // crawl cap §3.2

            // Stationary direction guard: brake held below 0.3 m/s for 0.4 s selects reverse;
            // throttle requests forward only after the same stationary guard (§3.2).
            float absSpeedMs = Math.Abs(wheelSpeedMs);
            if (absSpeedMs < ReverseStallGuardSpeedMs)
                _stationaryTimer += deltaSeconds;
            else
                _stationaryTimer = 0f;

            bool stationaryGuard = _stationaryTimer >= ReverseStallGuardSeconds;
            if (stationaryGuard && brake > 0.05f && throttle <= 0.01f)
                InReverse = true;
            else if (stationaryGuard && throttle > 0.05f)
                InReverse = false;
            // Never abruptly reverse engine torque at speed: state only changes under the guard.
            if (absSpeedMs >= ReverseStallGuardSpeedMs)
                InReverse = InReverse && brake > 0.05f && throttle <= 0.01f;

            reverseGearIndicated = InReverse;

            float ratio;
            if (InReverse)
            {
                ratio = spec.ReverseGear;
                EngineRpm = RpmFromWheel(spec, absSpeedMs, Math.Abs(spec.ReverseGear));
            }
            else
            {
                ratio = spec.ForwardGears[GearIndex];
                if (LowRangeEngaged) ratio *= LowRangeRatio;
                EngineRpm = RpmFromWheel(spec, absSpeedMs, Math.Abs(ratio));

                // Automatic shifting with cooldown and load-aware upshift point.
                _shiftCooldown = Math.Max(0f, _shiftCooldown - deltaSeconds);
                if (_shiftCooldown <= 0f)
                {
                    float load = throttle; // torque-demand proxy for shift scheduling
                    float upshiftRpm = load > 0.7f ? UpshiftRpmHeavyLoad : UpshiftRpmLightLoad;
                    if (EngineRpm > upshiftRpm && GearIndex < spec.ForwardGears.Length - 1 && !crawlEngaged)
                    {
                        GearIndex++;
                        _shiftCooldown = ShiftCooldownSeconds;
                        _torqueBlend = 0f;
                    }
                    else if (EngineRpm < DownshiftRpm && GearIndex > 0)
                    {
                        GearIndex--;
                        _shiftCooldown = ShiftCooldownSeconds;
                        _torqueBlend = 0f;
                    }
                }
            }

            EngineRpm = MathUtil.Clamp(EngineRpm, spec.IdleRpm, spec.LimiterRpm);
            _torqueBlend = MathUtil.Clamp01(_torqueBlend + deltaSeconds / TorqueTransitionSeconds);

            if (InReverse || throttle <= 0.001f || brake > 0.9f)
            {
                _lastCommandedTorque = 0f;
                return 0f;
            }

            float engineTorque = TorqueAtRpm(spec, EngineRpm);
            float condition = stats.EngineConditionFactor;
            if (condition <= 0f)
                return 0f; // condition 0 disables powered driving (§5.5)

            // totalWheelTorque = engineTorque × throttle × gearRatio × finalDrive × efficiency × condition
            float total = engineTorque
                          * throttle
                          * Math.Abs(ratio)
                          * spec.FinalDrive
                          * spec.DrivetrainEfficiency
                          * condition
                          * _torqueBlend;

            if (LowRangeEngaged && speedKph > LowRangeSpeedCapKph)
                total = 0f; // speed cap enforced by torque cut

            _lastCommandedTorque = total;
            return total;
        }

        /// <summary>
        /// Distributes total torque across grounded drive wheels (AWD 50:50 nominal).
        /// A slipping wheel cannot receive unlimited extra force: per-wheel share is capped
        /// by the nominal share × 1.6 (traction assist envelope, §4.3).
        /// </summary>
        public static void SplitAwdTorque(
            float totalTorqueNm,
            Span<bool> grounded,
            Span<float> slipRatio,
            Span<float> wheelTorqueOut,
            bool diffLockEngaged)
        {
            int n = grounded.Length;
            if (wheelTorqueOut.Length != n) throw new ArgumentException("Output span length mismatch.");

            int groundedCount = 0;
            for (int i = 0; i < n; i++)
                if (grounded[i]) groundedCount++;

            if (groundedCount == 0)
            {
                wheelTorqueOut.Clear();
                return;
            }

            float nominalShare = totalTorqueNm / groundedCount;
            if (diffLockEngaged)
            {
                for (int i = 0; i < n; i++)
                    wheelTorqueOut[i] = grounded[i] ? nominalShare : 0f;
                return;
            }

            // Open-differential style redistribution: wheel with poor contact loses share;
            // excess goes to gripping wheels but each is capped at 1.6 × nominal.
            float slipWeightSum = 0f;
            Span<float> weights = stackalloc float[n];
            for (int i = 0; i < n; i++)
            {
                if (!grounded[i])
                {
                    weights[i] = 0f;
                    continue;
                }
                // Less slip → more weight. |slip| ≥ 0 keeps weights sane.
                float gripWeight = 1f / (1f + Math.Abs(slipRatio[i]) * 2f);
                weights[i] = gripWeight;
                slipWeightSum += gripWeight;
            }

            float cap = nominalShare * 1.6f;
            for (int i = 0; i < n; i++)
            {
                float share = slipWeightSum > 0f
                    ? totalTorqueNm * (weights[i] / slipWeightSum)
                    : 0f;
                wheelTorqueOut[i] = grounded[i] ? Math.Min(share, cap) : 0f;
            }
        }

        public void SetLowRange(bool engaged) => LowRangeEngaged = engaged;
        public void SetDiffLock(bool engaged) => DiffLockEngaged = engaged;

        public float LastCommandedTorque => _lastCommandedTorque;

        static float RpmFromWheel(VehicleSpec spec, float wheelSpeedMs, float gearRatioMagnitude)
        {
            // wheel angular speed (rad/s) → driveline rpm, blended to idle at very low speed.
            float wheelAngularSpeed = wheelSpeedMs / Math.Max(0.01f, spec.WheelRadiusM);
            float drivelineRpm = wheelAngularSpeed * gearRatioMagnitude * spec.FinalDrive * 60f / (2f * (float)Math.PI);
            float blend = MathUtil.Clamp01(wheelSpeedMs / (8f / 3.6f)); // clutch model below 8 km/h (§4.3)
            return MathUtil.Lerp(spec.IdleRpm, Math.Max(spec.IdleRpm, drivelineRpm), blend);
        }
    }
}
