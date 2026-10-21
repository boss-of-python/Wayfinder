using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Wayfinder.Core;
using Wayfinder.Simulation;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Appendix C winch state machine, DEF-026/DEF-027 tension &amp; overload regression tests,
    /// and DEF-028 4-vehicle <c>vehicles.json</c> parity audit.
    /// </summary>
    public class WinchAndFleetSimulationTests
    {
        static List<WinchAnchorCandidate> SceneAnchors => new List<WinchAnchorCandidate>
        {
            new WinchAnchorCandidate("anchor.front_near", 0f, 0.5f, 8f, isStaticAnchor: true, hasLineOfSight: true),
            new WinchAnchorCandidate("anchor.front_far", 0f, 0.5f, 16f, isStaticAnchor: true, hasLineOfSight: true),
            new WinchAnchorCandidate("anchor.side", 7f, 0.5f, 2f, isStaticAnchor: true, hasLineOfSight: true),
            new WinchAnchorCandidate("anchor.obscured", 0f, 0.5f, 5f, isStaticAnchor: true, hasLineOfSight: false),
            new WinchAnchorCandidate("anchor.dynamic_truck", 0f, 0.5f, 4f, isStaticAnchor: false, hasLineOfSight: true),
            new WinchAnchorCandidate("anchor.beyond_20m", 0f, 0.5f, 21f, isStaticAnchor: true, hasLineOfSight: true)
        };

        [Test]
        public void WinchMath_SlackCableNeverPushes_AndOverloadPausesAtTwoSeconds_DEF026_DEF027()
        {
            var math = new WinchMath();
            // Slack cable (current < target): flexible line goes slack instead of pushing backward (DEF-027).
            float slackForce = math.ComputePullForce(5f, 8f, 0f, WinchMath.DefaultForceCapN, 0.01667f, out bool paused);
            Assert.AreEqual(0f, slackForce, 1e-5f, "a slack winch cable must never push the vehicle backward");
            Assert.IsFalse(paused);

            // Sustained stretch above 8 kN cap at 60 Hz steps must accumulate to >= 2.0 s (DEF-026).
            bool sawPause = false;
            int framesToPause = 0;
            for (int frame = 1; frame <= 200; frame++)
            {
                float pull = math.ComputePullForce(12f, 4f, 0f, WinchMath.DefaultForceCapN, 1f / 60f, out paused);
                Assert.AreEqual(WinchMath.DefaultForceCapN, pull, 1e-3f, "pull force never exceeds module cap");
                if (paused && !sawPause)
                {
                    sawPause = true;
                    framesToPause = frame;
                }
            }
            Assert.IsTrue(sawPause, "60 Hz overload must trigger reeling pause instead of freezing at 1 frame");
            Assert.That(framesToPause, Is.InRange(119, 121), "2.0 s at 60 Hz = 120 frames");
        }

        [Test]
        public void WinchController_EnforcesSpeedGateAndAimBrakeHold_AppendixC1()
        {
            var winch = new WinchController();
            Assert.IsFalse(winch.TryEngageAim(0f, out var r0));
            Assert.AreEqual(WinchRejectReason.NotEquipped, r0);

            winch.ConfigureCapability(WinchMath.DefaultReachM, WinchMath.DefaultForceCapN);
            Assert.IsFalse(winch.TryEngageAim(1.0f, out var rFast), "1.0 m/s hits the ceiling");
            Assert.AreEqual(WinchRejectReason.MovingTooFast, rFast);
            Assert.IsFalse(winch.HoldBrakes);

            Assert.IsTrue(winch.TryEngageAim(0.95f, out var rOk));
            Assert.AreEqual(WinchRejectReason.None, rOk);
            Assert.AreEqual(WinchState.Aiming, winch.State);
            Assert.IsTrue(winch.HoldBrakes, "Appendix C: entering winch mode holds brakes while selecting an anchor");
        }

        [Test]
        public void WinchController_ScoresStaticVisibleAnchorsWithinReach_AndCyclesOrSelects_AppendixC2()
        {
            var winch = new WinchController();
            winch.ConfigureCapability(WinchMath.DefaultReachM, WinchMath.DefaultForceCapN); // 12 m
            Assert.IsTrue(winch.TryEngageAim(0f, out _));

            int count12 = winch.UpdateAimCandidates(SceneAnchors, 0f, 0.5f, 0f, 0f, 0f, 1f, 0f, 0f, 1f);
            Assert.AreEqual(2, count12, "only static, unobscured anchors within 12 m qualify");
            Assert.AreEqual("anchor.front_near", winch.SelectedAnchorId, "forward + closer anchor wins");

            Assert.AreEqual("anchor.side", winch.CycleAnchor());
            Assert.AreEqual("anchor.front_near", winch.CycleAnchor());
            Assert.IsFalse(winch.SelectAnchor("anchor.obscured"), "obscured anchor cannot be selected");
            Assert.IsFalse(winch.SelectAnchor("anchor.dynamic_truck"), "dynamic vehicle cannot be selected");

            // Upgrade to Field Winch 2 (20 m, 12 kN): the 16 m anchor enters range while 21 m stays out.
            winch.ConfigureCapability(WinchMath.UpgradedReachM, WinchMath.UpgradedForceCapN);
            int count20 = winch.UpdateAimCandidates(SceneAnchors, 0f, 0.5f, 0f, 0f, 0f, 1f, 0f, 0f, 1f);
            Assert.AreEqual(3, count20, "16 m anchor joins; 21 m anchor stays excluded");
            Assert.IsTrue(winch.SelectAnchor("anchor.front_far"));
        }

        [Test]
        public void WinchController_AttachesExplicitlyReelsOverloadsRecoversAndDetachesCleanly_AppendixC3To7()
        {
            var winch = new WinchController();
            winch.ConfigureCapability(WinchMath.DefaultReachM, WinchMath.DefaultForceCapN);
            Assert.IsTrue(winch.TryEngageAim(0f, out _));
            winch.UpdateAimCandidates(SceneAnchors, 0f, 0.5f, 0f, 0f, 0f, 1f, 0f, 0f, 1f);

            // Initial step while Aiming exerts zero force — never auto-attaches without player action.
            var idleStep = winch.Step(0f, 0.5f, 0f, reelHeld: true, deltaSeconds: 0.5f);
            Assert.AreEqual(WinchState.Aiming, idleStep.State);
            Assert.AreEqual(0f, idleStep.PullForceN, 1e-6f);

            // Explicit attach sets initial cable length to current separation (8 m) and releases brake hold.
            Assert.IsTrue(winch.TryAttachSelected(0f, 0.5f, 0f, out var reason));
            Assert.AreEqual(WinchRejectReason.None, reason);
            Assert.AreEqual(WinchState.Attached, winch.State);
            Assert.IsFalse(winch.HoldBrakes);
            Assert.AreEqual(8f, winch.TargetCableLengthM, 1e-4f);

            // Hold chassis fixed at z = 0 while reeling at 0.8 m/s: stretch reaches 2 m -> 8 kN cap,
            // then 2.0 s of cap saturation transitions to OverloadPaused and freezes TargetCableLengthM.
            WinchStepOutput step = default;
            for (int i = 0; i < 360; i++)
                step = winch.Step(0f, 0.5f, 0f, reelHeld: true, deltaSeconds: 1f / 60f);

            Assert.AreEqual(WinchState.OverloadPaused, step.State);
            Assert.AreEqual(WinchController.OverloadWarning, step.WarningText);
            Assert.AreEqual(WinchMath.DefaultForceCapN, step.PullForceN, 1e-3f);
            Assert.AreEqual(step.PullForceN, step.PullFz, 1e-3f, "force points directly from fairlead toward +Z anchor");
            Assert.AreEqual(0f, step.PullFx, 1e-4f);

            float frozenLength = winch.TargetCableLengthM;
            step = winch.Step(0f, 0.5f, 0f, reelHeld: true, deltaSeconds: 0.5f);
            Assert.AreEqual(frozenLength, winch.TargetCableLengthM, 1e-5f, "reeling stays paused while overloaded");

            // Player drives forward to z = 7.5 m so tension drops to 0: overload clears and reeling resumes.
            for (int i = 0; i < 150; i++)
                step = winch.Step(0f, 0.5f, 7.5f, reelHeld: false, deltaSeconds: 1f / 60f);
            Assert.AreEqual(WinchState.Attached, step.State);
            Assert.IsEmpty(step.WarningText);

            // Instant detach on cancel/module disable leaves zero residual pull.
            winch.Detach();
            Assert.AreEqual(WinchState.Idle, winch.State);
            Assert.AreEqual(0f, winch.CurrentPullForceN, 1e-6f);
            Assert.AreEqual((0f, 0f, 0f), winch.CurrentPullVector);
        }

        [Test]
        public void VehicleSpec_AllFourChassisMatchVehiclesJsonFieldForField_DEF028()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "Assets", "Wayfinder", "Content", "vehicles.json");
            if (!File.Exists(path))
            {
                var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Assets", "Wayfinder", "Content", "vehicles.json")))
                    dir = dir.Parent;
                path = Path.Combine(dir.FullName, "Assets", "Wayfinder", "Content", "vehicles.json");
            }
            var arr = JArray.Parse(File.ReadAllText(path));
            Assert.AreEqual(4, arr.Count);
            foreach (JObject v in arr)
            {
                string id = v.Value<string>("id");
                var spec = VehicleSpec.ById(id);
                Assert.AreEqual(v.Value<float>("dryMassKg"), spec.DryMassKg, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("payloadKg"), spec.PayloadKg, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("fuelCapacityLiters"), spec.FuelCapacityLiters, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("peakTorqueNm"), spec.PeakTorqueNm, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("governedSpeedKph"), spec.GovernedSpeedKph, 1e-3f, id);
                Assert.AreEqual(v.Value<int>("cargoSlots"), spec.CargoSlots, id);
                Assert.AreEqual(v.Value<float>("wheelbaseM"), spec.WheelbaseM, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("trackM"), spec.TrackM, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("wheelRadiusM"), spec.WheelRadiusM, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("groundClearanceM"), spec.GroundClearanceM, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("comHeightM"), spec.ComHeightM, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("frontAxleLoadFraction"), spec.FrontAxleLoadFraction, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("reverseGear"), spec.ReverseGear, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("finalDrive"), spec.FinalDrive, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("brakeTorqueFrontNmPerWheel"), spec.BrakeTorqueFrontNmPerWheel, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("brakeTorqueRearNmPerWheel"), spec.BrakeTorqueRearNmPerWheel, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("suspensionTravelM"), spec.SuspensionTravelM, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("springRateFrontNm"), spec.SpringRateFrontNm, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("springRateRearNm"), spec.SpringRateRearNm, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("damperFrontNsm"), spec.DamperFrontNsm, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("damperRearNsm"), spec.DamperRearNsm, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("antiRollCouplingNm"), spec.AntiRollCouplingNm, 1e-3f, id);
                Assert.AreEqual(v.Value<float>("cdA"), spec.CdA, 1e-3f, id);
                var jsonGears = v["forwardGears"].Select(g => g.Value<float>()).ToArray();
                Assert.AreEqual(jsonGears.Length, spec.ForwardGears.Length, id);
                for (int i = 0; i < jsonGears.Length; i++)
                    Assert.AreEqual(jsonGears[i], spec.ForwardGears[i], 1e-3f, $"{id} gear {i}");
                var jsonCurve = v["torqueCurve"].ToArray();
                for (int i = 0; i < jsonCurve.Length; i++)
                {
                    Assert.AreEqual(jsonCurve[i][0].Value<float>(), spec.TorqueCurve[i][0], 1e-3f, $"{id} rpm {i}");
                    Assert.AreEqual(jsonCurve[i][1].Value<float>(), spec.TorqueCurve[i][1], 1e-3f, $"{id} nm {i}");
                }
            }
        }
    }
}
