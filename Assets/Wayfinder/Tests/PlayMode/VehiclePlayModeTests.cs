using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Wayfinder.Core;
using Wayfinder.Simulation;

namespace Wayfinder.Tests
{
    /// <summary>
    /// PlayMode integration smoke tests (§20.1). These exercise real Unity physics; they are
    /// UNVERIFIED in this repository's authoring environment (KI-001) and run via
    /// Window → General → Test Runner or the batch command in Docs/BUILD_AND_RUN.md §3.
    /// </summary>
    public class VehiclePlayModeTests
    {
        [UnityTest]
        public IEnumerator Vehicle_GroundsOnFlatSurface()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = Vector3.zero;

            var vehicleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicleGo.name = "TestVehicle";
            vehicleGo.transform.position = new Vector3(0f, 2f, 0f);
            vehicleGo.transform.localScale = new Vector3(1.6f, 1.0f, 3.6f);
            var rb = vehicleGo.AddComponent<Rigidbody>();
            rb.mass = 1350f;
            var controller = vehicleGo.AddComponent<VehicleController>();
            controller.spec = VehicleSpec.ScoutS1();
            controller.SetFuel(24f);

            // Let physics settle.
            for (int i = 0; i < 180; i++) yield return new WaitForFixedUpdate();

            Assert.Less(vehicleGo.transform.position.y, 1.5f, "vehicle should settle onto the ground");
            Assert.IsTrue(controller.SpeedMetersPerSecond < 2f, "vehicle should come to rest");
            Assert.IsTrue(controller.FuelLiters > 0f);

            Object.Destroy(vehicleGo);
            Object.Destroy(ground);
        }

        [UnityTest]
        public IEnumerator Vehicle_FuelDrainsOnlyWithSimulationTime()
        {
            var vehicleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicleGo.transform.position = new Vector3(0f, 1f, 0f);
            var rb = vehicleGo.AddComponent<Rigidbody>();
            rb.useGravity = false;
            var controller = vehicleGo.AddComponent<VehicleController>();
            controller.spec = VehicleSpec.ScoutS1();
            controller.SetFuel(24f);

            float before = controller.FuelLiters;
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();

            // Idle consumption only; never negative, never above capacity (QA case 11).
            Assert.GreaterOrEqual(controller.FuelLiters, 0f);
            Assert.LessOrEqual(controller.FuelLiters, 24f);
            Assert.LessOrEqual(controller.FuelLiters, before + 1e-4f);
            Object.Destroy(vehicleGo);
        }

        [UnityTest]
        public IEnumerator Lifecycle_SuppressesInputWithoutLatchingThrottle()
        {
            var go = new GameObject("RouterHost");
            var router = go.AddComponent<Wayfinder.Platform.InputRouter>();
            yield return null;

            router.SetTouchPedal(1f, 0f);
            router.SuppressInputs(true);
            yield return null;
            Assert.AreEqual(0f, router.Current.Throttle, "suppressed input must zero throttle (QA case 15)");
            Object.Destroy(go);
        }
    }
}
