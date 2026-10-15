using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Simulation;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    /// <summary>
    /// KI-025 / D-030: the physics rig's own stat resolver is configured by the campaign with exactly
    /// the researched modules (garage display == driving, §11.2), never twice (QA case 8).
    /// </summary>
    public class CampaignVehicleStatsTests
    {
        string _root;

        [SetUp]
        public void SetUp() => _root = NewTempRoot();

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        static readonly string[] FirstTenTiers =
        {
            "upgrade.scout.01.trailTires1", "upgrade.scout.02.underbodyGuard", "upgrade.scout.03.fieldWinch1",
            "upgrade.scout.04.cargoOrganizer", "upgrade.scout.05.suspension1", "upgrade.scout.06.tank1",
            "upgrade.scout.07.lowRange", "upgrade.scout.08.mudTires", "upgrade.scout.09.radar1", "upgrade.scout.10.engineTune1"
        };

        /// <summary>Resolves with the same arguments for every resolver under comparison.</summary>
        static EffectiveVehicleStats Resolve(CampaignSession s, VehicleStatsResolver resolver) =>
            resolver.Resolve(s.Vehicle, s.FuelLiters, 0f, TireSet.AllTerrain, SurfaceType.PackedDirt, false,
                100, 100, 100, false, false, false);

        [Test]
        public void ConfigureResolver_BringsResearchedModulesToTheRigResolver_ExactlyOnce_QA8()
        {
            var s = NewGame(new SaveService(_root));
            var unconfigured = Resolve(s, new VehicleStatsResolver());
            s.Research.Restore(FirstTenTiers); // arrangement: research state as if purchased (gating is tested elsewhere)

            var rig = new VehicleStatsResolver();
            s.ConfigureResolver(rig);
            var once = Resolve(s, rig);
            Assert.AreEqual(30f, once.FuelCapacityLiters, 1e-4f, "tier 6 tank");
            Assert.AreEqual(unconfigured.PeakTorqueNm * 1.10f, once.PeakTorqueNm, 1e-3f, "tier 10 engine tune ×1.10");
            Assert.AreEqual(0.34f, once.SuspensionTravelM, 1e-5f, "tier 5 suspension");
            Assert.Greater(once.EffectiveGrip, unconfigured.EffectiveGrip, "tier 1 trail tires on packed dirt");
            Assert.AreEqual(0.75f, once.UnderbodyDamageFactor, 1e-5f, "tier 2 underbody guard");

            s.ConfigureResolver(rig);
            s.ConfigureResolver(rig);
            var thrice = Resolve(s, rig);
            Assert.AreEqual(once.PeakTorqueNm, thrice.PeakTorqueNm, 1e-4f, "re-configuring never double-applies (QA case 8)");
            Assert.AreEqual(once.EffectiveGrip, thrice.EffectiveGrip, 1e-5f);
            Assert.AreEqual(once.PoweredFuelDrainFactor, thrice.PoweredFuelDrainFactor, 1e-6f);

            var garage = s.ResolveStats();
            Assert.AreEqual(garage.FuelCapacityLiters, once.FuelCapacityLiters, 1e-4f, "garage display == driving (§11.2)");
            Assert.AreEqual(garage.PeakTorqueNm, once.PeakTorqueNm, 1e-3f);
            Assert.AreEqual(garage.CargoSlots, once.CargoSlots);
            Assert.AreEqual(garage.PayloadKg, once.PayloadKg, 1e-3f);

            s.Research.Restore(new string[0]);
            s.ConfigureResolver(rig);
            var cleared = Resolve(s, rig);
            Assert.AreEqual(unconfigured.PeakTorqueNm, cleared.PeakTorqueNm, 1e-4f, "configuration follows the save (e.g. after loading another slot)");
            Assert.AreEqual(unconfigured.FuelCapacityLiters, cleared.FuelCapacityLiters, 1e-4f);
        }

        [Test]
        public void TrailTiresI_AddsTenPercentOnDirtAndGravel_AllTerrainSetOnly_KI032()
        {
            var plain = new VehicleStatsResolver();
            var trail = new VehicleStatsResolver();
            trail.AddModifier(VehicleStatsResolver.Stat.DirtGravelGrip, VehicleStatsResolver.Operation.Multiply, 1.10f,
                "upgrade.scout.01.trailTires1");
            float Grip(VehicleStatsResolver r, TireSet set, SurfaceType surface) => r.GripFor(set, surface, false, 1f, out _);

            Assert.AreEqual(0.70f * 1.10f, Grip(trail, TireSet.AllTerrain, SurfaceType.Gravel), 1e-5f, "§11.1 tier 1");
            Assert.AreEqual(Grip(plain, TireSet.AllTerrain, SurfaceType.PackedDirt) * 1.10f,
                Grip(trail, TireSet.AllTerrain, SurfaceType.PackedDirt), 1e-5f);
            Assert.AreEqual(Grip(plain, TireSet.AllTerrain, SurfaceType.IntactAsphalt),
                Grip(trail, TireSet.AllTerrain, SurfaceType.IntactAsphalt), 1e-6f, "dirt and gravel only");
            Assert.AreEqual(Grip(plain, TireSet.Mud, SurfaceType.PackedDirt), Grip(trail, TireSet.Mud, SurfaceType.PackedDirt), 1e-6f,
                "a specialized set already carries its progression bonus (§11.1 note)");
            Assert.AreEqual(Grip(plain, TireSet.AllTerrain, SurfaceType.Gravel) * 0.80f,
                plain.GripFor(TireSet.AllTerrain, SurfaceType.Gravel, false, 0.80f, out _), 1e-6f, "condition factor still applies");
            var stats = trail.Resolve(VehicleSpec.ScoutS1(), 20f, 0f, TireSet.AllTerrain, SurfaceType.Gravel, false,
                100, 100, 100, false, false, false);
            Assert.AreEqual(0.77f, stats.EffectiveGrip, 1e-5f, "the resolved stat the garage shows and the rig uses");
        }

        [Test]
        public void EngineTune_ScalesTheTorqueThePowertrainDelivers_KI032()
        {
            var spec = VehicleSpec.ScoutS1();
            float DriveTorque(float peakTorque)
            {
                var powertrain = new Powertrain();
                powertrain.Reset(spec);
                var stats = new EffectiveVehicleStats { PeakTorqueNm = peakTorque };
                float torque = 0f;
                for (int i = 0; i < 20; i++) torque = powertrain.Step(spec, stats, 2f, 1f, 0f, false, false, .1f, out _);
                return torque;
            }
            float baseline = DriveTorque(spec.PeakTorqueNm);
            Assert.Greater(baseline, 0f);
            Assert.AreEqual(baseline * 1.10f, DriveTorque(spec.PeakTorqueNm * 1.10f), baseline * 1e-4f, "Engine Tune I ×1.10 reaches the wheels");
            Assert.AreEqual(baseline, DriveTorque(0f), baseline * 1e-4f, "unset stats leave the base curve unchanged");
        }

        [Test]
        public void ResearchingTheTank_RaisesTheCapacityTheRigClampsTo()
        {
            var s = NewGame(new SaveService(_root));
            float baseCapacity = s.FuelCapacityLiters;
            s.Research.Restore(new[] { "upgrade.scout.06.tank1" });
            var rig = new VehicleStatsResolver();
            s.ConfigureResolver(rig);
            Assert.Greater(Resolve(s, rig).FuelCapacityLiters, baseCapacity,
                "VehicleController.SetFuel clamps to this resolved value instead of the base spec");
        }
    }
}
