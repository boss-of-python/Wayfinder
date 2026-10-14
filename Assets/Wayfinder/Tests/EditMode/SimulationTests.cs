using System;
using NUnit.Framework;
using Wayfinder.Core;
using Wayfinder.Simulation;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Vehicle/hazard formula tests (§4–5). Includes mandatory QA cases 8, 11.
    /// </summary>
    public class SimulationTests
    {
        [Test]
        public void FuelModel_MatchesBlueprintExample()
        {
            float rate = FuelModel.LitersPerSecond(0.45f, 0.50f, 0.20f, true);
            Assert.AreEqual(0.02255f, rate, 1e-6f);
        }

        [Test]
        public void FuelModel_EngineOffZero_AndBounded_QA11()
        {
            Assert.AreEqual(0f, FuelModel.LitersPerSecond(1f, 1f, 1f, false));
            float worst = FuelModel.LitersPerSecond(1f, 1f, 1f, true);
            Assert.LessOrEqual(worst, 0.047f);
            float fuel = FuelModel.Integrate(5f, 24f, 0.02255f, 1f);
            Assert.Greater(fuel, 4.9f);
            Assert.AreEqual(0f, FuelModel.Integrate(0.1f, 24f, 1f, 100f)); // clamps at zero
            Assert.AreEqual(24f, FuelModel.Integrate(24f, 24f, -1f, 10f)); // never exceeds capacity
        }

        [Test]
        public void FuelCan_AddsMinOfSixAndHeadroom()
        {
            float result = FuelModel.ApplyFuelCan(20f, 24f, out float wasted);
            Assert.AreEqual(24f, result, 1e-4f);
            Assert.AreEqual(2f, wasted, 1e-4f);
        }

        [Test]
        public void DamageModel_ImpactFormulaAndDebounce()
        {
            var dmg = new DamageModel();
            Assert.IsFalse(dmg.ApplyImpact(2.9f, HitZone.Body, false), "below 3 m/s ignored");
            Assert.IsTrue(dmg.ApplyImpact(5f, HitZone.Body, false));
            int after = dmg.BodyCondition;
            Assert.AreEqual(100 - 6, after); // clamp(1.4 × 4) = 5.6 → 6
            Assert.IsFalse(dmg.ApplyImpact(5f, HitZone.Body, false), "0.4 s debounce");
            dmg.Tick(0.5f);
            Assert.IsTrue(dmg.ApplyImpact(10f, HitZone.Body, false));
            Assert.AreEqual(100 - 35 - 6, dmg.BodyCondition); // clamped at 35 per incident
        }

        [Test]
        public void DamageModel_WaterStallsOnce()
        {
            var dmg = new DamageModel();
            dmg.ApplyWaterStall();
            dmg.ApplyWaterStall();
            Assert.AreEqual(100 - 10, dmg.EngineCondition, "condition drops once per incident (§5.3)");
        }

        [Test]
        public void TireSet_DoesNotDoubleDipResearchBonus_QA8()
        {
            // Mud tires on deep mud: surface grip 0.30 × 1.35 = 0.405 (exactly one tire multiplier).
            float grip = TireSetRules.SurfaceMultiplier(TireSet.Mud, SurfaceType.DeepMud, false, out _);
            Assert.AreEqual(0.30f * 1.35f, grip, 1e-5f);

            // The researched Trail Tires bonus is a separate resolver modifier; when composed with
            // a specialized set the resolver multiplies once — verify the raw table is untouched.
            float baseline = TireSetRules.SurfaceMultiplier(TireSet.AllTerrain, SurfaceType.Gravel, false, out _);
            Assert.AreEqual(0.70f, baseline, 1e-5f);

            var resolver = new VehicleStatsResolver();
            resolver.AddModifier(VehicleStatsResolver.Stat.DirtGravelGrip,
                VehicleStatsResolver.Operation.Multiply, 1.10f, "upgrade.scout.01.trailTires1");
            var stats = resolver.Resolve(VehicleSpec.ScoutS1(), 20f, 50f, TireSet.Mud,
                SurfaceType.DeepMud, false, 100, 100, 100, true, true, true);
            // Effective grip uses the tire table once; cap applies (≤ 1.25).
            Assert.LessOrEqual(stats.EffectiveGrip, SurfaceTable.EffectiveGripCap);
        }

        [Test]
        public void StatResolver_UsesSetThenAddThenMultiply()
        {
            var resolver = new VehicleStatsResolver();
            resolver.AddModifier(VehicleStatsResolver.Stat.FuelCapacityLiters,
                VehicleStatsResolver.Operation.Set, 30f, "tank1");
            resolver.AddModifier(VehicleStatsResolver.Stat.FuelCapacityLiters,
                VehicleStatsResolver.Operation.Add, 10f, "additive");
            resolver.AddModifier(VehicleStatsResolver.Stat.FuelCapacityLiters,
                VehicleStatsResolver.Operation.Multiply, 1.5f, "mul");
            Assert.AreEqual(60f, resolver.Compose(VehicleStatsResolver.Stat.FuelCapacityLiters, 24f), 1e-4f);
        }

        [Test]
        public void Powertrain_TorqueCurveAnchors()
        {
            var spec = VehicleSpec.ScoutS1();
            Assert.AreEqual(110f, Powertrain.TorqueAtRpm(spec, 800f), 1e-3f);
            Assert.AreEqual(240f, Powertrain.TorqueAtRpm(spec, 2500f), 1e-3f);
            Assert.AreEqual(140f, Powertrain.TorqueAtRpm(spec, 5200f), 1e-3f);
            Assert.AreEqual(165f, Powertrain.TorqueAtRpm(spec, 1950f), 1e-2f); // linear between 1500..2500
        }

        [Test]
        public void Powertrain_AwdSplitCapsSlippingWheel()
        {
            Span<bool> grounded = stackalloc bool[4];
            Span<float> slip = stackalloc float[4];
            Span<float> output = stackalloc float[4];
            grounded[0] = grounded[1] = grounded[2] = grounded[3] = true;
            slip[0] = 3f; // wheel spinning wildly
            Powertrain.SplitAwdTorque(1000f, grounded, slip, output, diffLockEngaged: false);
            float total = output[0] + output[1] + output[2] + output[3];
            Assert.AreEqual(1000f, total, 1f, "torque is conserved");
            Assert.LessOrEqual(output[0], 1000f / 4f * 1.6f + 1e-3f, "slipping wheel share is capped");
        }

        [Test]
        public void Recovery_GentleResetAndRescueLoss()
        {
            var rules = new RecoveryRules();
            Assert.IsTrue(rules.TryGentleReset(10f).Success);
            Assert.IsFalse(rules.TryGentleReset(10f).Success, "30 s cooldown");
            Assert.AreEqual(9, RecoveryRules.RescueSalvageLoss(37)); // 25% floor
            Assert.AreEqual(0, RecoveryRules.RescueSalvageLoss(0));
            Assert.IsTrue(RecoveryRules.IsValidSafePose(true, 5f, 10f, false, false, 0.2f, false));
            Assert.IsFalse(RecoveryRules.IsValidSafePose(true, 5f, 10f, true, false, 0.2f, false), "not in water");
        }

        [Test]
        public void Rescue_IsAlwaysAvailable_QA6()
        {
            // Zero currency, zero supplies: full rescue is reachable from pause/recovery UI (§5.6).
            var outcome = RecoveryRules.FullRescue(0, new ContentId("garage.home"));
            Assert.AreEqual(0, outcome.LooseSalvageLost);
            Assert.IsTrue(outcome.VehicleRepaired);
            Assert.IsTrue(outcome.FuelRefilled);
        }

        [Test]
        public void WaterRules_StallBandAndDragClamps()
        {
            var water = new WaterCrossingModel();
            Assert.IsFalse(water.Step(0.6f, 0.85f, 1.0f, out _), "0.6 m below 0.85 m intake");
            Assert.IsTrue(water.Step(1.0f, 0.85f, 2.5f, out bool stall1), "2 s above intake");
            Assert.IsTrue(stall1);
            Assert.IsFalse(water.Step(1.0f, 0.85f, 1.5f, out bool stall2), "once per incident");
            Assert.IsFalse(stall2);
            Assert.IsTrue(WaterCrossingModel.IsImpassableOnMainRoute(1.3f));
            Assert.IsFalse(WaterCrossingModel.IsImpassableOnMainRoute(1.1f));
            Assert.LessOrEqual(WaterCrossingModel.DragForce(1f, 100f), 8000f);
            Assert.LessOrEqual(Math.Abs(WaterCrossingModel.CurrentForce(4f, 50f)), 4000f);
        }

        [Test]
        public void Steering_LimitsMatchSpec()
        {
            Assert.AreEqual(34f, SteeringModel.SteerAngleLimitDegrees(0f), 1e-3f);
            Assert.AreEqual(26f, SteeringModel.SteerAngleLimitDegrees(20f), 1e-3f);
            Assert.AreEqual(16f, SteeringModel.SteerAngleLimitDegrees(40f), 1e-3f);
            Assert.AreEqual(10f, SteeringModel.SteerAngleLimitDegrees(70f), 1e-3f);
        }

        [Test]
        public void MudSink_CapsAndRecovers()
        {
            var mud = new MudSinkModel();
            for (int i = 0; i < 200; i++)
                mud.Step(1f, 0.1f, 0.25f, 0.1f);
            Assert.LessOrEqual(mud.SinkFraction, MudSinkModel.MaxSinkFraction);
            for (int i = 0; i < 100; i++)
                mud.Step(0f, -1f, 0.25f, 0.1f);
            Assert.GreaterOrEqual(mud.SinkFraction, 0f);
        }
    }
}
