using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Immutable vehicle definition values (§10.1). Serialized source of truth is
    /// Content/vehicles.json; every field of all four chassis matches the JSON table verbatim.
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

        /// <summary>Scout S1 baseline exactly per §4.2 and Content/vehicles.json.</summary>
        public static VehicleSpec ScoutS1() => new VehicleSpec
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

        /// <summary>Mule M2 wide hauler per §10.1 and Content/vehicles.json (DEF-028).</summary>
        public static VehicleSpec MuleM2() => new VehicleSpec
        {
            Id = new ContentId("vehicle.mule.m2"),
            DisplayNameKey = "vehicle.mule.m2.name",
            DryMassKg = 1800f,
            PayloadKg = 260f,
            FuelCapacityLiters = 34f,
            PeakTorqueNm = 290f,
            GovernedSpeedKph = 55f,
            CargoSlots = 16,
            WheelbaseM = 3.10f,
            TrackM = 1.72f,
            WheelRadiusM = 0.38f,
            GroundClearanceM = 0.26f,
            ComHeightM = 0.72f,
            FrontAxleLoadFraction = 0.52f,
            ForwardGears = new[] { 3.80f, 2.20f, 1.40f, 1.00f },
            ReverseGear = -3.30f,
            FinalDrive = 4.30f,
            DrivetrainEfficiency = 0.85f,
            BrakeTorqueFrontNmPerWheel = 1800f,
            BrakeTorqueRearNmPerWheel = 1500f,
            SuspensionTravelM = 0.30f,
            SpringRateFrontNm = 38000f,
            SpringRateRearNm = 35000f,
            DamperFrontNsm = 3900f,
            DamperRearNsm = 3600f,
            AntiRollCouplingNm = 4500f,
            TorqueCurve = new[]
            {
                new[] { 800f, 130f }, new[] { 1500f, 220f }, new[] { 2500f, 290f },
                new[] { 3500f, 270f }, new[] { 4500f, 215f }, new[] { 5200f, 165f }
            },
            IdleRpm = 800f,
            LimiterRpm = 5200f,
            CdA = 1.3f
        };

        /// <summary>Lynx L3 short-wheelbase trail rig per §10.1 and Content/vehicles.json (DEF-028).</summary>
        public static VehicleSpec LynxL3() => new VehicleSpec
        {
            Id = new ContentId("vehicle.lynx.l3"),
            DisplayNameKey = "vehicle.lynx.l3.name",
            DryMassKg = 1100f,
            PayloadKg = 90f,
            FuelCapacityLiters = 22f,
            PeakTorqueNm = 225f,
            GovernedSpeedKph = 75f,
            CargoSlots = 10,
            WheelbaseM = 2.30f,
            TrackM = 1.55f,
            WheelRadiusM = 0.35f,
            GroundClearanceM = 0.25f,
            ComHeightM = 0.58f,
            FrontAxleLoadFraction = 0.56f,
            ForwardGears = new[] { 3.50f, 2.05f, 1.30f, 0.95f },
            ReverseGear = -3.20f,
            FinalDrive = 4.05f,
            DrivetrainEfficiency = 0.85f,
            BrakeTorqueFrontNmPerWheel = 1500f,
            BrakeTorqueRearNmPerWheel = 1250f,
            SuspensionTravelM = 0.32f,
            SpringRateFrontNm = 28000f,
            SpringRateRearNm = 26000f,
            DamperFrontNsm = 3100f,
            DamperRearNsm = 2800f,
            AntiRollCouplingNm = 3600f,
            TorqueCurve = new[]
            {
                new[] { 800f, 105f }, new[] { 1500f, 180f }, new[] { 2500f, 225f },
                new[] { 3500f, 210f }, new[] { 4500f, 175f }, new[] { 5200f, 135f }
            },
            IdleRpm = 800f,
            LimiterRpm = 5200f,
            CdA = 1.0f
        };

        /// <summary>Atlas A4 expedition utility per §10.1 and Content/vehicles.json (DEF-028).</summary>
        public static VehicleSpec AtlasA4() => new VehicleSpec
        {
            Id = new ContentId("vehicle.atlas.a4"),
            DisplayNameKey = "vehicle.atlas.a4.name",
            DryMassKg = 1650f,
            PayloadKg = 180f,
            FuelCapacityLiters = 38f,
            PeakTorqueNm = 320f,
            GovernedSpeedKph = 65f,
            CargoSlots = 14,
            WheelbaseM = 2.85f,
            TrackM = 1.68f,
            WheelRadiusM = 0.38f,
            GroundClearanceM = 0.28f,
            ComHeightM = 0.66f,
            FrontAxleLoadFraction = 0.54f,
            ForwardGears = new[] { 3.70f, 2.15f, 1.38f, 1.00f },
            ReverseGear = -3.30f,
            FinalDrive = 4.15f,
            DrivetrainEfficiency = 0.85f,
            BrakeTorqueFrontNmPerWheel = 1750f,
            BrakeTorqueRearNmPerWheel = 1450f,
            SuspensionTravelM = 0.31f,
            SpringRateFrontNm = 35000f,
            SpringRateRearNm = 32000f,
            DamperFrontNsm = 3700f,
            DamperRearNsm = 3400f,
            AntiRollCouplingNm = 4300f,
            TorqueCurve = new[]
            {
                new[] { 800f, 140f }, new[] { 1500f, 245f }, new[] { 2500f, 320f },
                new[] { 3500f, 300f }, new[] { 4500f, 235f }, new[] { 5200f, 180f }
            },
            IdleRpm = 800f,
            LimiterRpm = 5200f,
            CdA = 1.2f
        };

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
