using System;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Mud/sand virtual sink model (§5.2). Visual tracks are decals only — no persistent
    /// terrain deformation. Sink fraction increases resistance and wheel burial, capped to
    /// avoid physics explosions.
    /// </summary>
    public sealed class MudSinkModel
    {
        public const float ShallowDepthM = 0.12f;
        public const float BogDepthM = 0.30f;
        public const float MaxSinkFraction = 0.35f;
        public const float SinkRatePerSecondAtFullThrottle = 0.04f;
        public const float RecoverRatePerSecond = 0.08f;
        public const float RecoveryBoardTractionMultiplier = 1.45f;
        public const float RecoveryBoardDurationSeconds = 20f;
        public const float RecoveryBoardDistanceM = 12f;

        public float SinkFraction { get; private set; }
        public bool BoardsActive { get; private set; }
        float _boardTimeRemaining;
        float _boardTravelAccumulated;

        public enum MudDepth { None, Shallow, Deep, Bog }

        public static MudDepth ClassifyDepth(float mudDepthMeters)
        {
            if (mudDepthMeters <= 0f) return MudDepth.None;
            if (mudDepthMeters <= ShallowDepthM) return MudDepth.Shallow;
            if (mudDepthMeters <= BogDepthM) return MudDepth.Deep;
            return MudDepth.Bog;
        }

        /// <summary>
        /// Per-tick update. Holding full throttle below 2 km/h worsens sink; reversing, easing
        /// throttle or winching reduces it. Recovery boards grant localized traction while active.
        /// </summary>
        public void Step(float throttle, float speedMs, float mudDepthMeters, float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            if (BoardsActive)
            {
                _boardTimeRemaining -= deltaSeconds;
                _boardTravelAccumulated += Math.Abs(speedMs) * deltaSeconds;
                if (_boardTimeRemaining <= 0f || _boardTravelAccumulated >= RecoveryBoardDistanceM)
                    BoardsActive = false;
            }

            var depth = ClassifyDepth(mudDepthMeters);
            if (depth == MudDepth.None)
            {
                SinkFraction = Math.Max(0f, SinkFraction - RecoverRatePerSecond * 2f * deltaSeconds);
                return;
            }

            float depthFactor = depth == MudDepth.Shallow ? 0.35f : depth == MudDepth.Deep ? 1f : 1.4f;
            float slow = Math.Abs(speedMs) < (2f / 3.6f) ? 1f : 0.25f;
            float throttleFactor = MathUtil.Clamp01(throttle);

            SinkFraction += SinkRatePerSecondAtFullThrottle * depthFactor * slow * throttleFactor * deltaSeconds;
            if (throttleFactor < 0.1f || speedMs < 0f)
                SinkFraction -= RecoverRatePerSecond * deltaSeconds; // easing/reversing recovers

            SinkFraction = MathUtil.Clamp(SinkFraction, 0f, MaxSinkFraction);
        }

        /// <summary>Extra rolling resistance factor from virtual sink.</summary>
        public float ResistanceMultiplier => 1f + SinkFraction * 2.5f;

        /// <summary>Recovery boards: consumed only once the effect starts (§5.2).</summary>
        public bool TryActivateBoards()
        {
            if (BoardsActive) return false;
            BoardsActive = true;
            _boardTimeRemaining = RecoveryBoardDurationSeconds;
            _boardTravelAccumulated = 0f;
            return true;
        }

        /// <summary>Traction multiplier for surfaces while boards are active.</summary>
        public float TractionMultiplier =>
            BoardsActive ? RecoveryBoardTractionMultiplier : 1f;

        public void Reset() { SinkFraction = 0f; BoardsActive = false; }
    }

    /// <summary>
    /// Water crossing rules (§5.3). Compare water level with intake height and wheel hubs.
    /// </summary>
    public sealed class WaterCrossingModel
    {
        public const float BaselineSafeWadingM = 0.55f;
        public const float UpgradedSafeWadingM = 0.85f;
        public const float WarningBandM = 0.10f;
        public const float StallSecondsAboveIntake = 2f;
        public const float ImpassableMainRouteDepthM = 1.2f;

        float _secondsAboveIntake;
        bool _stallLatched;

        public bool WarningActive { get; private set; }
        public bool EngineStalled { get; private set; }

        /// <summary>
        /// depthUnderChassisM: water surface height minus chassis datum.
        /// intakeHeightM: intake height above the same datum (0.55 baseline / 0.85 upgraded).
        /// Returns true when a stall transition occurred this tick (drops condition once).
        /// </summary>
        public bool Step(float depthUnderChassisM, float intakeHeightM, float deltaSeconds, out bool stallEvent)
        {
            stallEvent = false;
            float headroom = intakeHeightM - depthUnderChassisM;
            WarningActive = headroom <= WarningBandM && headroom > 0f;

            if (depthUnderChassisM > intakeHeightM)
            {
                _secondsAboveIntake += deltaSeconds;
                if (_secondsAboveIntake >= StallSecondsAboveIntake && !_stallLatched)
                {
                    _stallLatched = true;
                    EngineStalled = true;
                    stallEvent = true; // condition −10 ONCE per incident
                    return true;
                }
            }
            else
            {
                _secondsAboveIntake = 0f;
                // Engine restarts after water recedes and a repair/recovery action occurs.
            }
            return false;
        }

        public void ClearStall()
        {
            EngineStalled = false;
            _stallLatched = false;
            _secondsAboveIntake = 0f;
        }

        public void Reset()
        {
            _secondsAboveIntake = 0f;
            _stallLatched = false;
            EngineStalled = false;
            WarningActive = false;
        }

        /// <summary>Water drag increases with submerged area (simplified side/axle area proxy).</summary>
        public static float DragForce(float submergedFraction, float speedMs)
        {
            submergedFraction = MathUtil.Clamp01(submergedFraction);
            const float coefficient = 350f; // tuning value, clamped for stability
            float force = coefficient * submergedFraction * speedMs * Math.Abs(speedMs);
            return MathUtil.Clamp(force, -8000f, 8000f);
        }

        /// <summary>
        /// Lateral current: tuned coefficient × submerged side area × squared relative water velocity.
        /// Clamped for stability (§5.3).
        /// </summary>
        public static float CurrentForce(float submergedSideAreaM2, float relativeWaterVelocityMs)
        {
            const float coefficient = 25f;
            float force = coefficient * submergedSideAreaM2 * relativeWaterVelocityMs * Math.Abs(relativeWaterVelocityMs);
            return MathUtil.Clamp(force, -4000f, 4000f);
        }

        /// <summary>Partial buoyancy, capped so the vehicle cannot become a useful boat.</summary>
        public static float BuoyancyForce(float submergedVolumeM3, float totalMassKg)
        {
            const float waterDensity = 1000f;
            const float gravity = 9.81f;
            float fullBuoyancy = submergedVolumeM3 * waterDensity * gravity;
            // Cap at 70% of vehicle weight.
            return Math.Min(fullBuoyancy, totalMassKg * gravity * 0.70f);
        }

        public static bool IsImpassableOnMainRoute(float depthMeters) =>
            depthMeters > ImpassableMainRouteDepthM;
    }

    /// <summary>
    /// Rollover warning (§5.4): 24° body roll sustained 0.5 s on low-speed terrain,
    /// with incline context to avoid false warnings during harmless suspension motion.
    /// </summary>
    public sealed class RolloverMonitor
    {
        public const float WarningAngleDegrees = 24f;
        public const float SustainedSeconds = 0.5f;
        public const float LowSpeedThresholdMs = 8f;
        public const float SafeRouteSideSlopeLimitDegrees = 22f;

        float _timer;
        public bool WarningActive { get; private set; }

        public void Step(float bodyRollDegrees, float vehicleSpeedMs, float terrainSlopeDegrees, float deltaSeconds)
        {
            bool context = vehicleSpeedMs < LowSpeedThresholdMs;
            float relativeRoll = bodyRollDegrees - Math.Abs(terrainSlopeDegrees) * 0.5f;
            if (context && relativeRoll > WarningAngleDegrees)
            {
                _timer += deltaSeconds;
                WarningActive = _timer >= SustainedSeconds;
            }
            else
            {
                _timer = 0f;
                WarningActive = false;
            }
        }

        public void Reset() { _timer = 0f; WarningActive = false; }
    }

    /// <summary>
    /// Steering limits and Ackermann blending (§3.3). Road-wheel limit:
    /// 34° @ 0 km/h, 26° @ 20, 16° @ 40, 10° @ 70. Arcade blend 0.6.
    /// </summary>
    public static class SteeringModel
    {
        public const float DeadZoneStick = 0.08f;
        public const float ButtonRampToFullSeconds = 0.30f;
        public const float ButtonReturnSeconds = 0.18f;
        public const float AckermannBlend = 0.6f;
        public const float MaxSteerDegreesAtZero = 34f;

        public static float SteerAngleLimitDegrees(float speedKph)
        {
            speedKph = Math.Max(0f, speedKph);
            if (speedKph <= 20f) return MathUtil.Lerp(34f, 26f, speedKph / 20f);
            if (speedKph <= 40f) return MathUtil.Lerp(26f, 16f, (speedKph - 20f) / 20f);
            if (speedKph <= 70f) return MathUtil.Lerp(16f, 10f, (speedKph - 40f) / 30f);
            return 10f;
        }

        /// <summary>Approximate Ackermann inner/outer wheel angles for a steering command.</summary>
        public static void AckermannAngles(float steerCommand, float speedKph,
            float wheelbaseM, float trackM,
            out float innerDegrees, out float outerDegrees)
        {
            steerCommand = MathUtil.Clamp(steerCommand, -1f, 1f);
            float max = SteerAngleLimitDegrees(speedKph);
            float baseAngle = steerCommand * max;

            // Wheel-radius-neutral approximation; arcade blend keeps both wheels closer to base.
            float turnRadius = wheelbaseM / Math.Max(0.1f, Math.Tan(Math.Abs(baseAngle) * Math.PI / 180f)) * Math.Sign(baseAngle == 0 ? 1 : baseAngle);
            float inner = (float)(Math.Atan(wheelbaseM / Math.Max(0.5f, Math.Abs(turnRadius) - trackM / 2f)) * 180.0 / Math.PI) * Math.Sign(turnRadius);
            float outer = (float)(Math.Atan(wheelbaseM / (Math.Abs(turnRadius) + trackM / 2f)) * 180.0 / Math.PI) * Math.Sign(turnRadius);

            innerDegrees = MathUtil.Lerp(baseAngle, inner, AckermannBlend);
            outerDegrees = MathUtil.Lerp(baseAngle, outer, AckermannBlend);
            if (steerCommand < 0)
            {
                // Swap: turning left, inner is the left wheel.
                float tmp = innerDegrees;
                innerDegrees = outerDegrees;
                outerDegrees = tmp;
            }
        }

        /// <summary>Button steering eased ramp (0→full in 0.30 s, return in 0.18 s, §3.2).</summary>
        public static float RampButtonSteer(float current, float target, float deltaSeconds)
        {
            float rate = Math.Abs(target) > Math.Abs(current)
                ? 1f / ButtonRampToFullSeconds
                : 1f / ButtonReturnSeconds;
            float step = rate * deltaSeconds;
            if (current < target) return Math.Min(current + step, target);
            return Math.Max(current - step, target);
        }
    }

    /// <summary>
    /// Anti-roll coupling (§4.5): force depends on left/right compression difference, capped,
    /// and cannot add force at an ungrounded wheel.
    /// </summary>
    public static class AntiRoll
    {
        public const float MaxForceN = 6000f;

        public static float Force(float leftCompressionM, float rightCompressionM,
            float couplingNm, bool leftGrounded, bool rightGrounded)
        {
            float diff = leftCompressionM - rightCompressionM;
            float force = diff * couplingNm;
            force = MathUtil.Clamp(force, -MaxForceN, MaxForceN);
            // Never push an ungrounded wheel upward (cannot launch the chassis).
            if (!leftGrounded && force > 0f) force = 0f;
            if (!rightGrounded && force < 0f) force = 0f;
            return force;
        }
    }
}
