using System;
using NUnit.Framework;
using Wayfinder.Core;
using Wayfinder.Simulation;

namespace Wayfinder.Tests
{
    /// <summary>
    /// KI-025 / D-044 (Batch N): the researched stats the garage displays must reach the physics
    /// rig through one pure translation (<see cref="WheelRigMath"/>) that
    /// <c>VehicleController.ApplyEffectiveStats</c> applies to the WheelColliders. Suspension
    /// tiers 5/14 previously changed a panel number only. These tests execute the whole chain —
    /// research composition → rig settings → per-wheel friction — headlessly; the Unity-side
    /// application remains syntax-checked only and still needs M1 in-editor calibration.
    /// </summary>
    public class WheelRigMathTests
    {
        static EffectiveVehicleStats Stats(float travel = 0.28f, float clearance = 0.24f,
            float damping = 1f, float winchReach = 0f, float winchForce = 0f) =>
            new EffectiveVehicleStats
            {
                SuspensionTravelM = travel,
                GroundClearanceM = clearance,
                DampingEffectiveness = damping,
                WinchReachM = winchReach,
                WinchForceCapN = winchForce
            };

        [Test]
        public void FromStats_CarriesResearchedTravelAndClearance_ToTheRig_KI025()
        {
            var spec = VehicleSpec.ScoutS1();
            // Tier 5 (suspension1): travel 0.28 -> 0.34 m, clearance 0.24 -> 0.27 m (§11.1).
            var rig = WheelRigMath.FromStats(spec, Stats(0.34f, 0.27f));
            Assert.AreEqual(0.34f, rig.SuspensionDistanceM, 1e-6f);
            Assert.AreEqual(-0.03f, rig.WheelCenterYOffsetM, 1e-6f,
                "more clearance lifts the hull, so the wheel center moves down relative to the body");
            Assert.AreEqual(0.27f, rig.GroundClearanceM, 1e-6f, "the value HUD/safe-pose already read");
            Assert.AreEqual(spec.WheelRadiusM, rig.WheelRadiusM, 1e-6f, "no tier changes wheel radius");
            Assert.AreEqual(0.5f, rig.TargetPosition, 1e-6f, "rest at mid-travel");

            // Tier 14 (suspension2) replaces tier 5 in the same slot; un-fitting returns to base.
            Assert.AreEqual(0.40f, WheelRigMath.FromStats(spec, Stats(0.40f, 0.30f)).SuspensionDistanceM, 1e-6f);
            var baseRig = WheelRigMath.FromStats(spec, Stats());
            Assert.AreEqual(0.28f, baseRig.SuspensionDistanceM, 1e-6f);
            Assert.AreEqual(0f, baseRig.WheelCenterYOffsetM, 1e-6f, "no research -> authored centers untouched");
        }

        [Test]
        public void FromStats_ClampsTravelAndClearance_SoAMalformedStatCannotInvertAWheel()
        {
            var spec = VehicleSpec.ScoutS1();
            var low = WheelRigMath.FromStats(spec, Stats(0f, 0.01f));
            Assert.AreEqual(WheelRigMath.MinTravelM, low.SuspensionDistanceM, 1e-6f);
            Assert.AreEqual(0.05f, low.WheelCenterYOffsetM, 1e-6f,
                "clearance may fall at most 5 cm below authored (a raise of the wheel center)");
            var high = WheelRigMath.FromStats(spec, Stats(2f, 1.5f));
            Assert.AreEqual(WheelRigMath.MaxTravelM, high.SuspensionDistanceM, 1e-6f);
            Assert.AreEqual(-0.20f, high.WheelCenterYOffsetM, 1e-6f, "lift capped at 20 cm");
        }

        [Test]
        public void FromStats_SpringsPassThrough_DampersScaleWithSuspensionCondition()
        {
            var spec = VehicleSpec.ScoutS1();
            var healthy = WheelRigMath.FromStats(spec, Stats(damping: 1f));
            Assert.AreEqual(spec.SpringRateFrontNm, healthy.SpringRateFrontNm, 1e-3f);
            Assert.AreEqual(spec.SpringRateRearNm, healthy.SpringRateRearNm, 1e-3f);
            Assert.AreEqual(spec.DamperFrontNsm, healthy.DamperFrontNsm, 1e-3f);
            Assert.AreEqual(spec.DamperRearNsm, healthy.DamperRearNsm, 1e-3f);

            // Wrecked suspension resolves DampingEffectiveness 0.70 (§5.5) -> 70 % damper.
            var wrecked = WheelRigMath.FromStats(spec, Stats(damping: 0.70f));
            Assert.AreEqual(spec.DamperFrontNsm * 0.70f, wrecked.DamperFrontNsm, 1e-2f);
            Assert.AreEqual(spec.DamperRearNsm * 0.70f, wrecked.DamperRearNsm, 1e-2f);
            Assert.AreEqual(spec.SpringRateFrontNm, wrecked.SpringRateFrontNm, 1e-3f,
                "condition degrades damping, not the spring (§5.5)");

            // A malformed effectiveness above 1 can never over-dampen beyond authored rates.
            var over = WheelRigMath.FromStats(spec, Stats(damping: 1.9f));
            Assert.AreEqual(spec.DamperFrontNsm, over.DamperFrontNsm, 1e-3f);
        }

        [Test]
        public void FromStats_WinchCapabilityFollowsTheFittedTier()
        {
            var spec = VehicleSpec.ScoutS1();
            var none = WheelRigMath.FromStats(spec, Stats());
            Assert.IsFalse(none.HasWinch);
            Assert.IsFalse(none.WinchCanReach(1f), "no winch fitted, nothing is reachable");

            var tier3 = WheelRigMath.FromStats(spec, Stats(winchReach: 12f, winchForce: 8000f));
            Assert.IsTrue(tier3.HasWinch);
            Assert.AreEqual(8000f, tier3.WinchForceCapN, 1e-3f);
            Assert.IsTrue(tier3.WinchCanReach(12f), "anchor exactly at reach is allowed");
            Assert.IsFalse(tier3.WinchCanReach(12.01f));
            Assert.IsFalse(tier3.WinchCanReach(-1f), "a negative distance is not an anchor");

            // Tier 15 replaces tier 3 in the winch slot (§10.2): reach grows, force grows.
            var tier15 = WheelRigMath.FromStats(spec, Stats(winchReach: 20f, winchForce: 12000f));
            Assert.IsTrue(tier15.WinchCanReach(19f));
            Assert.AreEqual(12000f, tier15.WinchForceCapN, 1e-3f);

            // Negative content values are floored, never a negative-force winch.
            var bad = WheelRigMath.FromStats(spec, Stats(winchReach: -5f, winchForce: -1f));
            Assert.IsFalse(bad.HasWinch);
            Assert.AreEqual(0f, bad.WinchForceCapN, 1e-6f);
        }

        [Test]
        public void FrictionStiffness_MatchesThePreBatchNInlineFormulas()
        {
            // Forward: grip * (1 - 0.5*sink) * traction (VehicleController.SetWheelDrive, pre-N).
            Assert.AreEqual(0.82f, WheelRigMath.ForwardFrictionStiffness(0.82f, 0f, 1f), 1e-6f);
            Assert.AreEqual(0.82f * 0.825f, WheelRigMath.ForwardFrictionStiffness(0.82f, 0.35f, 1f), 1e-5f,
                "max sink (0.35) cuts forward stiffness to 82.5 %");
            Assert.AreEqual(0.5f * 1.45f, WheelRigMath.ForwardFrictionStiffness(0.5f, 0f, 1.45f), 1e-6f,
                "recovery boards' traction multiplier still composes");
            // Sideways: grip * lateral * traction; mud tires on asphalt carry lateral 0.90 (§10.2).
            Assert.AreEqual(1.0f * 0.90f, WheelRigMath.SidewaysFrictionStiffness(1.0f, 0.90f, 1f), 1e-6f);
            Assert.AreEqual(0.5f * 0.9f * 1.45f, WheelRigMath.SidewaysFrictionStiffness(0.5f, 0.9f, 1.45f), 1e-6f);
            // Stiffness can never go negative, whatever a caller hands in.
            Assert.AreEqual(0f, WheelRigMath.ForwardFrictionStiffness(-0.5f, 0f, 1f), 1e-6f);
            Assert.AreEqual(0f, WheelRigMath.SidewaysFrictionStiffness(0.5f, -1f, 1f), 1e-6f);
            Assert.AreEqual(0.5f * 0.5f, WheelRigMath.ForwardFrictionStiffness(0.5f, 3f, 1f), 1e-6f,
                "an out-of-range sink is clamped to fully sunk, not beyond zero grip");
        }

        [Test]
        public void BrakeScale_MatchesThePreBatchNEnvelope_AndStaysInUnitRange()
        {
            Assert.AreEqual(1f, WheelRigMath.BrakeScale(0.9f), 1e-6f, "full brake at the reference grip");
            Assert.AreEqual(0.1f, WheelRigMath.BrakeScale(0f), 1e-6f, "never quite zero: some brake always remains");
            Assert.AreEqual(0.12f / 0.9f + 0.1f, WheelRigMath.BrakeScale(0.12f), 1e-6f, "ice keeps ~23 % of brake torque");
            Assert.AreEqual(1f, WheelRigMath.BrakeScale(5f), 1e-6f, "clamped above");
            Assert.AreEqual(0.1f, WheelRigMath.BrakeScale(-2f), 1e-6f, "clamped below");
        }

        [Test]
        public void RequiresGeometryUpdate_TripsOnLoadoutChanges_NotOnPerFrameSignals()
        {
            var spec = VehicleSpec.ScoutS1();
            var a = WheelRigMath.FromStats(spec, Stats());
            Assert.IsFalse(WheelRigMath.RequiresGeometryUpdate(a, a), "identical settings never rewrite colliders");
            var jitter = WheelRigMath.FromStats(spec, Stats(0.28f + WheelRigMath.GeometryEpsilon * 0.5f, 0.24f));
            Assert.IsFalse(WheelRigMath.RequiresGeometryUpdate(a, jitter), "sub-epsilon noise is not a change");

            Assert.IsTrue(WheelRigMath.RequiresGeometryUpdate(a,
                WheelRigMath.FromStats(spec, Stats(0.34f, 0.27f))), "fitting suspension1 moves travel + centers");
            Assert.IsTrue(WheelRigMath.RequiresGeometryUpdate(a,
                WheelRigMath.FromStats(spec, Stats(0.34f, 0.24f))), "travel alone (same clearance) still trips it");
            Assert.IsTrue(WheelRigMath.RequiresGeometryUpdate(a,
                WheelRigMath.FromStats(spec, Stats(0.28f, 0.27f))), "clearance alone (same travel) still trips it");
            Assert.IsTrue(WheelRigMath.RequiresGeometryUpdate(a,
                WheelRigMath.FromStats(spec, Stats(damping: 0.7f))), "suspension damage rewrites dampers");
            Assert.IsTrue(WheelRigMath.RequiresGeometryUpdate(a,
                WheelRigMath.FromStats(spec, Stats(winchReach: 12f, winchForce: 8000f))), "fitting the winch registers");

            // GroundClearanceM alone is display data: within the clamped delta it moves the
            // centers (covered above); the raw field by itself must not force a rewrite.
            var displayOnly = new WheelRigSettings(a.SuspensionDistanceM, a.SpringRateFrontNm, a.SpringRateRearNm,
                a.DamperFrontNsm, a.DamperRearNsm, a.TargetPosition, a.WheelCenterYOffsetM, a.WheelRadiusM,
                a.GroundClearanceM + 0.5f, a.HasWinch, a.WinchReachM, a.WinchForceCapN);
            Assert.IsFalse(WheelRigMath.RequiresGeometryUpdate(a, displayOnly));
            Assert.AreNotEqual(a, displayOnly, "equality still sees the display field");
        }

        [Test]
        public void SettingsEquality_AndHash_CoverEveryField()
        {
            var spec = VehicleSpec.ScoutS1();
            var a = WheelRigMath.FromStats(spec, Stats(0.34f, 0.27f, 0.85f, 12f, 8000f));
            var b = WheelRigMath.FromStats(spec, Stats(0.34f, 0.27f, 0.85f, 12f, 8000f));
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a, WheelRigMath.FromStats(spec, Stats(0.34f, 0.27f, 0.85f, 20f, 8000f)));
            Assert.IsFalse(a.Equals(null));
            Assert.IsFalse(a.Equals("settings"));
        }

        [Test]
        public void FromStats_RejectsNullInputs()
        {
            Assert.Throws<ArgumentNullException>(() => WheelRigMath.FromStats(null, Stats()));
            Assert.Throws<ArgumentNullException>(() => WheelRigMath.FromStats(VehicleSpec.ScoutS1(), null));
        }

        [Test]
        public void EndToEnd_ResearchComposition_ReachesTheRigNumbers()
        {
            // The exact chain the driver runs: research modifiers -> Resolve -> FromStats.
            var spec = VehicleSpec.ScoutS1();
            var resolver = new VehicleStatsResolver();
            resolver.AddModifier(VehicleStatsResolver.Stat.SuspensionTravelM,
                VehicleStatsResolver.Operation.Set, 0.34f, "upgrade.scout.05.suspension1", 5);
            resolver.AddModifier(VehicleStatsResolver.Stat.GroundClearanceM,
                VehicleStatsResolver.Operation.Set, 0.27f, "upgrade.scout.05.suspension1", 5);
            resolver.AddModifier(VehicleStatsResolver.Stat.WinchReachM,
                VehicleStatsResolver.Operation.Set, 12f, "upgrade.scout.03.fieldWinch1", 3);
            resolver.AddModifier(VehicleStatsResolver.Stat.WinchForceCapN,
                VehicleStatsResolver.Operation.Set, 8000f, "upgrade.scout.03.fieldWinch1", 3);

            var worn = resolver.Resolve(spec, 20f, 0f, TireSet.AllTerrain, SurfaceType.PackedDirt, false,
                100, 0, 100, false, false, false);
            var rig = WheelRigMath.FromStats(spec, worn);
            Assert.AreEqual(0.34f, rig.SuspensionDistanceM, 1e-6f, "tier 5 travel reaches the collider distance");
            Assert.AreEqual(spec.DamperFrontNsm * 0.70f, rig.DamperFrontNsm, 1e-2f,
                "suspension at 0 condition damps at the §5.5 floor");
            Assert.IsTrue(rig.WinchCanReach(10f), "tier 3 winch is usable by the rig");

            var pristine = new VehicleStatsResolver().Resolve(spec, 20f, 0f, TireSet.AllTerrain,
                SurfaceType.PackedDirt, false, 100, 100, 100, false, false, false);
            Assert.IsTrue(WheelRigMath.RequiresGeometryUpdate(
                WheelRigMath.FromStats(spec, pristine), rig),
                "researching + wrecking is exactly the change ApplyEffectiveStats must not miss");
        }

        [Test]
        public void PerWheelGrip_ResearchReachesOnlyTheWheelsOnTheRightSurface_KI025()
        {
            // Four wheels, four surfaces: the per-wheel path DEF-005 opened, composed with the
            // rig formulas — Trail Tires I may help only the dirt/gravel wheels (QA case 8).
            var plain = new VehicleStatsResolver();
            var trail = new VehicleStatsResolver();
            trail.AddModifier(VehicleStatsResolver.Stat.DirtGravelGrip,
                VehicleStatsResolver.Operation.Multiply, 1.10f, "upgrade.scout.01.trailTires1");
            var surfaces = new[] { SurfaceType.PackedDirt, SurfaceType.Ice, SurfaceType.DeepMud, SurfaceType.Gravel };

            float Friction(VehicleStatsResolver r, SurfaceType s) =>
                WheelRigMath.ForwardFrictionStiffness(r.GripFor(TireSet.AllTerrain, s, false, 1f, out _), 0f, 1f);

            Assert.AreEqual(Friction(plain, surfaces[0]) * 1.10f, Friction(trail, surfaces[0]), 1e-5f, "dirt wheel gains");
            Assert.AreEqual(Friction(plain, surfaces[1]), Friction(trail, surfaces[1]), 1e-6f, "ice wheel unchanged");
            Assert.AreEqual(Friction(plain, surfaces[2]), Friction(trail, surfaces[2]), 1e-6f, "mud wheel unchanged");
            Assert.AreEqual(Friction(plain, surfaces[3]) * 1.10f, Friction(trail, surfaces[3]), 1e-5f, "gravel wheel gains");
            Assert.Less(Friction(trail, surfaces[1]), Friction(trail, surfaces[0]),
                "one vehicle, four distinct wheel frictions — no whole-vehicle baseline remains");
        }

        [Test]
        public void EveryVehicle_RigSettingsStayInsideTheSafetyClamps()
        {
            foreach (var id in new[] { "vehicle.scout.s1", "vehicle.mule.m2", "vehicle.lynx.l3", "vehicle.atlas.a4" })
            {
                var spec = VehicleSpec.ById(id);
                var stats = new VehicleStatsResolver().Resolve(spec, spec.FuelCapacityLiters, spec.PayloadKg,
                    TireSet.AllTerrain, SurfaceType.PackedDirt, false, 100, 100, 100, false, false, false);
                var rig = WheelRigMath.FromStats(spec, stats);
                Assert.AreEqual(spec.SuspensionTravelM, rig.SuspensionDistanceM, 1e-6f, id);
                Assert.AreEqual(0f, rig.WheelCenterYOffsetM, 1e-6f, id + ": unresearched = authored stance");
                Assert.GreaterOrEqual(rig.SuspensionDistanceM, WheelRigMath.MinTravelM, id);
                Assert.LessOrEqual(rig.SuspensionDistanceM, WheelRigMath.MaxTravelM, id);
            }
        }
    }
}
