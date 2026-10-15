using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Wayfinder.Platform;
using Wayfinder.Simulation;

namespace Wayfinder.Tests
{
    /// <summary>Requires real Unity physics and setup-generated resources. Not headless evidence.</summary>
    public class VehiclePlayModeTests
    {
        readonly List<GameObject> _objects = new List<GameObject>();

        GameObject Track(GameObject go) { _objects.Add(go); return go; }

        VehicleController Vehicle(bool gravity)
        {
            var go = Track(new GameObject("Test Scout"));
            go.transform.position = new Vector3(0f, 1f, 0f);
            var body = go.AddComponent<Rigidbody>();
            body.mass = 1350f;
            body.useGravity = gravity;
            var hull = go.AddComponent<BoxCollider>();
            hull.center = new Vector3(0f, .3f, 0f);
            hull.size = new Vector3(1.7f, .7f, 3.9f);
            var vehicle = go.AddComponent<VehicleController>();
            var wheels = new WheelCollider[4];
            for (int i = 0; i < 4; i++)
            {
                var wheel = new GameObject("Wheel " + i);
                wheel.transform.SetParent(go.transform, false);
                wheel.transform.localPosition = new Vector3(i % 2 == 0 ? -.79f : .79f, 0f, i < 2 ? 1.325f : -1.325f);
                wheels[i] = wheel.AddComponent<WheelCollider>();
                wheels[i].radius = .36f;
                wheels[i].suspensionDistance = .28f;
                wheels[i].suspensionSpring = new JointSpring { spring = 32000f, damper = 3400f, targetPosition = .5f };
            }
            vehicle.wheelFrontLeft = wheels[0]; vehicle.wheelFrontRight = wheels[1];
            vehicle.wheelRearLeft = wheels[2]; vehicle.wheelRearRight = wheels[3];
            return vehicle;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            foreach (var go in _objects) if (go != null) Object.Destroy(go);
            _objects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator WaterVolume_StallsOnceAndClearsDepthAfterTeleport()
        {
            var vehicle = Vehicle(false);
            var waterGo = Track(new GameObject("Deep Water Test"));
            waterGo.transform.position = new Vector3(0f, 1f, 0f);
            var trigger = waterGo.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(8f, 4f, 8f);
            var water = waterGo.AddComponent<WaterVolume>();
            water.surfaceHeightY = 3f;
            yield return new WaitForSeconds(2.5f);
            Assert.Greater(vehicle.WaterDepthMeters, .55f);
            Assert.AreEqual(90, vehicle.Damage.EngineCondition, "one incident must cost exactly ten condition");
            yield return new WaitForSeconds(2.5f);
            Assert.AreEqual(90, vehicle.Damage.EngineCondition, "remaining submerged must not repeat the penalty");
            vehicle.GetComponent<Rigidbody>().position = new Vector3(20f, 1f, 0f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0f, vehicle.WaterDepthMeters);
        }

        [UnityTest]
        public IEnumerator Lifecycle_PauseAndFocusCannotResumeEachOther()
        {
            var go = Track(new GameObject("Lifecycle"));
            var lifecycle = go.AddComponent<LifecycleHandler>();
            yield return null;
            int suspended = 0, resumed = 0;
            lifecycle.OnSuspendRequested += () => suspended++;
            lifecycle.OnResumeRequested += () => resumed++;
            go.SendMessage("OnApplicationFocus", false);
            go.SendMessage("OnApplicationPause", true);
            go.SendMessage("OnApplicationFocus", true);
            Assert.IsTrue(lifecycle.IsSuspended);
            Assert.AreEqual(1, suspended);
            Assert.AreEqual(0, resumed);
            go.SendMessage("OnApplicationPause", false);
            Assert.IsFalse(lifecycle.IsSuspended);
            Assert.AreEqual(1, resumed);
        }

        [UnityTest]
        public IEnumerator Vehicle_DrivesForwardAndEmptyTankCutsMotorTorque()
        {
            var ground = Track(GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            var vehicle = Vehicle(true);
            for (int i = 0; i < 120; i++) yield return new WaitForFixedUpdate();
            float start = vehicle.transform.position.z;
            vehicle.SetDriveCommand(0f, 1f, 0f, false, false);
            for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
            Assert.Greater(vehicle.transform.position.z, start + .5f, "grounded Scout must move under throttle");
            vehicle.SetFuel(0f);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0f, vehicle.wheelFrontLeft.motorTorque);
            Assert.AreEqual(0f, vehicle.wheelFrontRight.motorTorque);
            Assert.AreEqual(0f, vehicle.wheelRearLeft.motorTorque);
            Assert.AreEqual(0f, vehicle.wheelRearRight.motorTorque);
        }

        [UnityTest]
        public IEnumerator Vehicle_GroundsOnAllFourWheels()
        {
            var ground = Track(GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            var vehicle = Vehicle(true);
            for (int i = 0; i < 180; i++) yield return new WaitForFixedUpdate();
            Assert.IsTrue(vehicle.wheelFrontLeft.isGrounded);
            Assert.IsTrue(vehicle.wheelFrontRight.isGrounded);
            Assert.IsTrue(vehicle.wheelRearLeft.isGrounded);
            Assert.IsTrue(vehicle.wheelRearRight.isGrounded);
            Assert.Less(vehicle.SpeedMetersPerSecond, .5f);
        }

        [UnityTest]
        public IEnumerator Vehicle_IdleDrainsFuelAndStoppedEngineDoesNot()
        {
            var vehicle = Vehicle(false);
            float before = vehicle.FuelLiters;
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            Assert.Less(vehicle.FuelLiters, before, "an idling engine uses fuel");
            vehicle.SetEngineOn(false);
            before = vehicle.FuelLiters;
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(before, vehicle.FuelLiters, 1e-6f);
        }

        [UnityTest]
        public IEnumerator Input_TouchPedalSurvivesUpdatesAndCannotLatchAcrossPause()
        {
            var router = Track(new GameObject("Router")).AddComponent<InputRouter>();
            router.SetTouchPedal(1f, 0f);
            router.SetTouchSteer(-1f);
            yield return new WaitForSeconds(.4f);
            Assert.Greater(router.Current.Throttle, .9f);
            Assert.Less(router.Current.Steer, -.9f);
            router.SuppressInputs(true);
            router.SetTouchPedal(1f, 0f); // a late pointer event while paused must not latch
            yield return null;
            Assert.AreEqual(0f, router.Current.Throttle);
            router.SuppressInputs(false);
            yield return null;
            Assert.AreEqual(0f, router.Current.Throttle);
            Assert.AreEqual(0f, router.Current.Steer);
        }

        [UnityTest]
        public IEnumerator Input_TouchReleaseReturnsToZero()
        {
            var router = Track(new GameObject("Router")).AddComponent<InputRouter>();
            router.SetTouchPedal(1f, 0f);
            yield return new WaitForSeconds(.3f);
            router.SetTouchPedal(0f, 0f);
            yield return new WaitForSeconds(.3f);
            Assert.AreEqual(0f, router.Current.Throttle, 1e-6f);
        }
    }
}
