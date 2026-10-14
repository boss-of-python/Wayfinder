using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Immutable vehicle definition values (§10.1). Serialized source of truth is
    /// Content/vehicles.json; this type is the runtime mirror used by the stat resolver.
    /// </summary>
    public sealed class VehicleSpec
    {
        public ContentId Id;
        public string DisplayNameKey;
        /// <summary>Chassis dry mass without fuel/cargo (kg).</summary>
        public float DryMassKg;
        public float PayloadKg;
        public float FuelCapacityLiters;
        public const float FuelDensityKgPerLiter = 0.74f;
        public float PeakTorqueNm;
        public float GovernedSpeedKph;
        public int CargoSlots;
        public float WheelbaseM;
        public float TrackM;
        public float WheelRadiusM;
        public float GroundClearanceM;
        /// <summary>Center of mass height above level ground at nominal load (m).</summary>
        public float ComHeightM;
        /// <summary>Static front axle load fraction (0..1).</summary>
        public float FrontAxleLoadFraction;
        public float[] ForwardGears;
        public float ReverseGear;
        public float FinalDrive;
        public float DrivetrainEfficiency;
        public float BrakeTorqueFrontNmPerWheel;
        public float BrakeTorqueRearNmPerWheel;
        public float SuspensionTravelM;
        public float SpringRateFrontNm;
        public float SpringRateRearNm;
        public float DamperFrontNsm;
        public float DamperRearNsm;
        public float AntiRollCouplingNm;
        /// <summary>(rpm, N·m) torque curve samples; endpoints preserved (§4.3).</summary>
        public float[][] TorqueCurve;
        public float IdleRpm;
        public float LimiterRpm;
        public float CdA;

        /// <summary>Scout S1 baseline exactly per §4.2.</summary>
        public static VehicleSpec ScoutS1()
        {
            return new VehicleSpec
            {
                Id = new ContentId("vehicle.scout.s1"),
                DisplayNameKey = "vehicle.scout.s1.name",
                DryMassKg = 1350f,
                PayloadKg = 120f,
                FuelCapacityLiters = 24f,
                PeakTorqueNm = 240f,
                GovernedSpeedKph = 70f,
                CargoSlots = 12,
                WheelbaseM = 2.65f,
                TrackM = 1.58f,
                WheelRadiusM = 0.36f,
                GroundClearanceM = 0.24f,
                ComHeightM = 0.62f,
                FrontAxleLoadFraction = 0.55f,
                ForwardGears = new[] { 3.60f, 2.10f, 1.35f, 1.00f },
                ReverseGear = -3.30f,
                FinalDrive = 4.10f,
                DrivetrainEfficiency = 0.85f,
                BrakeTorqueFrontNmPerWheel = 1600f,
                BrakeTorqueRearNmPerWheel = 1300f,
                SuspensionTravelM = 0.28f,
                SpringRateFrontNm = 32000f,
                SpringRateRearNm = 29000f,
                DamperFrontNsm = 3400f,
                DamperRearNsm = 3100f,
                AntiRollCouplingNm = 4000f,
                TorqueCurve = new[]
                {
                    new[] { 800f, 110f }, new[] { 1500f, 190f }, new[] { 2500f, 240f },
                    new[] { 3500f, 225f }, new[] { 4500f, 185f }, new[] { 5200f, 140f }
                },
                IdleRpm = 800f,
                LimiterRpm = 5200f,
                CdA = 1.1f
            };
        }

        public static VehicleSpec MuleM2()
        {
            var v = ScoutS1();
            v.Id = new ContentId("vehicle.mule.m2");
            v.DisplayNameKey = "vehicle.mule.m2.name";
            v.DryMassKg = 1800f;
            v.PayloadKg = 260f;
            v.FuelCapacityLiters = 34f;
            v.PeakTorqueNm = 290f;
            v.GovernedSpeedKph = 55f;
            v.CargoSlots = 16;
            v.WheelbaseM = 3.10f;
            v.TrackM = 1.72f;
            v.ComHeightM = 0.72f;
            return v;
        }

        public static VehicleSpec LynxL3()
        {
            var v = ScoutS1();
            v.Id = new ContentId("vehicle.lynx.l3");
            v.DisplayNameKey = "vehicle.lynx.l3.name";
            v.DryMassKg = 1100f;
            v.PayloadKg = 90f;
            v.FuelCapacityLiters = 22f;
            v.PeakTorqueNm = 225f;
            v.GovernedSpeedKph = 75f;
            v.CargoSlots = 10;
            v.WheelbaseM = 2.30f;
            v.TrackM = 1.55f;
            v.ComHeightM = 0.58f;
            return v;
        }

        public static VehicleSpec AtlasA4()
        {
            var v = ScoutS1();
            v.Id = new ContentId("vehicle.atlas.a4");
            v.DisplayNameKey = "vehicle.atlas.a4.name";
            v.DryMassKg = 1650f;
            v.PayloadKg = 180f;
            v.FuelCapacityLiters = 38f;
            v.PeakTorqueNm = 320f;
            v.GovernedSpeedKph = 65f;
            v.CargoSlots = 14;
            v.WheelbaseM = 2.85f;
            v.TrackM = 1.68f;
            v.ComHeightM = 0.66f;
            return v;
        }

        public static VehicleSpec ById(string id)
        {
            switch (id)
            {
                case "vehicle.scout.s1": return ScoutS1();
                case "vehicle.mule.m2": return MuleM2();
                case "vehicle.lynx.l3": return LynxL3();
                case "vehicle.atlas.a4": return AtlasA4();
                default: throw new KeyNotFoundException($"Unknown vehicle id '{id}'.");
            }
        }
    }
}
