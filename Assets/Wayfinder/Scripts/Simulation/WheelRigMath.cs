using System;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Pure (engine-free) translation from <see cref="EffectiveVehicleStats"/> to the numbers a
    /// four-wheel WheelCollider rig consumes: suspension travel, spring/damper rates, wheel-center
    /// lift for researched ground clearance, and the winch capability (KI-025, D-044).
    /// <para>
    /// Before Batch N the resolver already produced these stats — and the garage displayed them —
    /// but nothing pushed suspension travel or clearance into the WheelColliders: tier 5/14
    /// suspension research changed a number on a panel, not the vehicle. <c>VehicleController</c>
    /// now asks <see cref="FromStats"/> for one immutable settings value and applies it through
    /// its <c>ApplyEffectiveStats</c> seam; every formula lives here so the garage, the rig and
    /// the headless tests read exactly one definition.
    /// </para>
    /// <para>
    /// Calibration honesty (§4.4 note): WheelCollider friction/spring values are Unity's tuning
    /// space, not physical coefficients. Setting suspension distance to the authored travel and
    /// stiffness to the composed grip are authored starting points that still need M1 skidpad /
    /// ledge calibration in-editor; this type only guarantees the researched values reach the rig.
    /// </para>
    /// </summary>
    public readonly struct WheelRigSettings : IEquatable<WheelRigSettings>
    {
        /// <summary>WheelCollider.suspensionDistance (m): the resolved suspension travel.</summary>
        public readonly float SuspensionDistanceM;
        /// <summary>Front spring rate (N/m), authored spec value (§4.2).</summary>
        public readonly float SpringRateFrontNm;
        /// <summary>Rear spring rate (N/m), authored spec value (§4.2).</summary>
        public readonly float SpringRateRearNm;
        /// <summary>Front damper (N·s/m): spec value scaled by suspension condition (§5.5).</summary>
        public readonly float DamperFrontNsm;
        /// <summary>Rear damper (N·s/m): spec value scaled by suspension condition (§5.5).</summary>
        public readonly float DamperRearNsm;
        /// <summary>Rest position within the travel, 0..1 (JointSpring.targetPosition).</summary>
        public readonly float TargetPosition;
        /// <summary>
        /// Vertical offset to ADD to each wheel center (m). Researched clearance lifts the hull
        /// relative to the contact patch, so the wheel center moves <i>down</i> by the clearance
        /// gain: a positive research gain yields a negative offset.
        /// </summary>
        public readonly float WheelCenterYOffsetM;
        /// <summary>Wheel radius (m). No research tier changes it; passed through from the spec.</summary>
        public readonly float WheelRadiusM;
        /// <summary>Resolved ground clearance (m), kept for HUD/safe-pose use.</summary>
        public readonly float GroundClearanceM;
        /// <summary>True once a winch tier is fitted (reach &gt; 0).</summary>
        public readonly bool HasWinch;
        /// <summary>Anchor reach (m): 12 m at tier 3, 20 m at tier 15.</summary>
        public readonly float WinchReachM;
        /// <summary>Winch pull cap (N): 8 kN at tier 3, 12 kN at tier 15.</summary>
        public readonly float WinchForceCapN;

        public WheelRigSettings(float suspensionDistanceM, float springRateFrontNm, float springRateRearNm,
            float damperFrontNsm, float damperRearNsm, float targetPosition, float wheelCenterYOffsetM,
            float wheelRadiusM, float groundClearanceM, bool hasWinch, float winchReachM, float winchForceCapN)
        {
            SuspensionDistanceM = suspensionDistanceM;
            SpringRateFrontNm = springRateFrontNm;
            SpringRateRearNm = springRateRearNm;
            DamperFrontNsm = damperFrontNsm;
            DamperRearNsm = damperRearNsm;
            TargetPosition = targetPosition;
            WheelCenterYOffsetM = wheelCenterYOffsetM;
            WheelRadiusM = wheelRadiusM;
            GroundClearanceM = groundClearanceM;
            HasWinch = hasWinch;
            WinchReachM = winchReachM;
            WinchForceCapN = winchForceCapN;
        }

        /// <summary>True when a fitted winch can anchor at the given distance.</summary>
        public bool WinchCanReach(float distanceM) =>
            HasWinch && distanceM >= 0f && distanceM <= WinchReachM;

        public bool Equals(WheelRigSettings other) =>
            SuspensionDistanceM.Equals(other.SuspensionDistanceM) &&
            SpringRateFrontNm.Equals(other.SpringRateFrontNm) &&
            SpringRateRearNm.Equals(other.SpringRateRearNm) &&
            DamperFrontNsm.Equals(other.DamperFrontNsm) &&
            DamperRearNsm.Equals(other.DamperRearNsm) &&
            TargetPosition.Equals(other.TargetPosition) &&
            WheelCenterYOffsetM.Equals(other.WheelCenterYOffsetM) &&
            WheelRadiusM.Equals(other.WheelRadiusM) &&
            GroundClearanceM.Equals(other.GroundClearanceM) &&
            HasWinch == other.HasWinch &&
            WinchReachM.Equals(other.WinchReachM) &&
            WinchForceCapN.Equals(other.WinchForceCapN);

        public override bool Equals(object obj) => obj is WheelRigSettings other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SuspensionDistanceM.GetHashCode();
                hash = hash * 397 ^ SpringRateFrontNm.GetHashCode();
                hash = hash * 397 ^ SpringRateRearNm.GetHashCode();
                hash = hash * 397 ^ DamperFrontNsm.GetHashCode();
                hash = hash * 397 ^ DamperRearNsm.GetHashCode();
                hash = hash * 397 ^ TargetPosition.GetHashCode();
                hash = hash * 397 ^ WheelCenterYOffsetM.GetHashCode();
                hash = hash * 397 ^ WheelRadiusM.GetHashCode();
                hash = hash * 397 ^ GroundClearanceM.GetHashCode();
                hash = hash * 397 ^ HasWinch.GetHashCode();
                hash = hash * 397 ^ WinchReachM.GetHashCode();
                hash = hash * 397 ^ WinchForceCapN.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>Stateless rig formulas. Extracted so they can execute headlessly (KI-025, D-044).</summary>
    public static class WheelRigMath
    {
        /// <summary>Travel is clamped so a malformed stat can never invert a WheelCollider.</summary>
        public const float MinTravelM = 0.05f;
        public const float MaxTravelM = 0.60f;
        /// <summary>The hull may be lowered 5 cm or lifted 20 cm by research, never more.</summary>
        public const float MinClearanceDeltaM = -0.05f;
        public const float MaxClearanceDeltaM = 0.20f;
        /// <summary>Rest at mid-travel; sag itself comes from spring rate vs. load (M1 calibration).</summary>
        public const float RestTargetPosition = 0.5f;
        /// <summary>Geometry compares within this tolerance (m / N·m ratios) count as unchanged.</summary>
        public const float GeometryEpsilon = 0.0001f;

        /// <summary>
        /// Composes the rig settings for one resolved stat set. Suspension tiers (§11.1 tiers 5
        /// and 14) author travel and clearance only, so spring rates pass through from the spec
        /// while dampers scale with suspension condition (<see cref="EffectiveVehicleStats.DampingEffectiveness"/>,
        /// §5.5): a half-worn suspension damps at its authored rate, a wrecked one at 70 %.
        /// </summary>
        public static WheelRigSettings FromStats(VehicleSpec spec, EffectiveVehicleStats stats)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (stats == null) throw new ArgumentNullException(nameof(stats));

            float travel = MathUtil.Clamp(stats.SuspensionTravelM, MinTravelM, MaxTravelM);
            float clearanceDelta = MathUtil.Clamp(
                stats.GroundClearanceM - spec.GroundClearanceM, MinClearanceDeltaM, MaxClearanceDeltaM);
            float damping = MathUtil.Clamp(stats.DampingEffectiveness, 0f, 1f);
            float winchReach = Math.Max(0f, stats.WinchReachM);
            float winchForce = Math.Max(0f, stats.WinchForceCapN);

            return new WheelRigSettings(
                travel,
                spec.SpringRateFrontNm,
                spec.SpringRateRearNm,
                spec.DamperFrontNsm * damping,
                spec.DamperRearNsm * damping,
                RestTargetPosition,
                -clearanceDelta,
                spec.WheelRadiusM,
                stats.GroundClearanceM,
                winchReach > 0f,
                winchReach,
                winchForce);
        }

        /// <summary>
        /// Forward friction stiffness for one wheel. This is the exact formula that lived inline in
        /// <c>VehicleController.SetWheelDrive</c> before Batch N — composed grip, reduced as mud
        /// sink drags the wheel down, scaled by the mud model's traction multiplier (recovery
        /// boards). Extracted unchanged so the rig and the tests share one source.
        /// </summary>
        public static float ForwardFrictionStiffness(float grip, float sinkFraction, float tractionMultiplier) =>
            Math.Max(0f, grip * (1f - 0.5f * MathUtil.Clamp01(sinkFraction)) * tractionMultiplier);

        /// <summary>
        /// Sideways friction stiffness for one wheel: forward grip times the tire set's lateral
        /// multiplier (e.g. mud tires lose 10 % lateral response on asphalt, §10.2), then traction.
        /// </summary>
        public static float SidewaysFrictionStiffness(float grip, float lateralMultiplier, float tractionMultiplier) =>
            Math.Max(0f, grip * Math.Max(0f, lateralMultiplier) * tractionMultiplier);

        /// <summary>
        /// ABS-ish brake envelope for one wheel: full brake torque on high-grip surfaces, scaled
        /// down as grip falls so a light surface does not lock instantly. Identical to the
        /// pre-Batch-N inline formula in <c>VehicleController.SetWheelDrive</c>.
        /// </summary>
        public static float BrakeScale(float surfaceGrip) =>
            MathUtil.Clamp01(Math.Max(0f, surfaceGrip) / 0.9f + 0.1f);

        /// <summary>
        /// True when the geometry-bearing fields differ beyond <see cref="GeometryEpsilon"/> and
        /// the WheelColliders must be rewritten. Per-frame signals (grip, sink, mass) are not
        /// geometry and never trip this, so <c>ApplyEffectiveStats</c> touches the colliders only
        /// when a loadout/condition change actually moves something (§18.2).
        /// </summary>
        public static bool RequiresGeometryUpdate(WheelRigSettings previous, WheelRigSettings next) =>
            Math.Abs(previous.SuspensionDistanceM - next.SuspensionDistanceM) > GeometryEpsilon ||
            Math.Abs(previous.SpringRateFrontNm - next.SpringRateFrontNm) > GeometryEpsilon ||
            Math.Abs(previous.SpringRateRearNm - next.SpringRateRearNm) > GeometryEpsilon ||
            Math.Abs(previous.DamperFrontNsm - next.DamperFrontNsm) > GeometryEpsilon ||
            Math.Abs(previous.DamperRearNsm - next.DamperRearNsm) > GeometryEpsilon ||
            Math.Abs(previous.TargetPosition - next.TargetPosition) > GeometryEpsilon ||
            Math.Abs(previous.WheelCenterYOffsetM - next.WheelCenterYOffsetM) > GeometryEpsilon ||
            Math.Abs(previous.WheelRadiusM - next.WheelRadiusM) > GeometryEpsilon ||
            previous.HasWinch != next.HasWinch ||
            Math.Abs(previous.WinchReachM - next.WinchReachM) > GeometryEpsilon ||
            Math.Abs(previous.WinchForceCapN - next.WinchForceCapN) > GeometryEpsilon;
    }
}
