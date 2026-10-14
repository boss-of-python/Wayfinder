using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>Gameplay surface labels (§4.4). Shared by visuals and physics — one source.</summary>
    public enum SurfaceType
    {
        IntactAsphalt = 0,
        BrokenAsphalt = 1,
        PackedDirt = 2,
        Gravel = 3,
        Grass = 4,
        Rock = 5,
        ShallowMud = 6,
        DeepMud = 7,
        Sand = 8,
        PackedSnow = 9,
        Ice = 10
    }

    /// <summary>
    /// Tuning coefficients per §4.4. These are game tuning values, not measured tire-material
    /// constants. Effective μ after tire/weather/condition composition is capped (initially ≤ 1.25).
    /// </summary>
    public readonly struct SurfaceCoefficients
    {
        public readonly SurfaceType Surface;
        /// <summary>Intended effective dry grip μ target.</summary>
        public readonly float DryGrip;
        /// <summary>Multiplier applied when wet.</summary>
        public readonly float WetMultiplier;
        /// <summary>Rolling resistance coefficient Crr.</summary>
        public readonly float RollingResistance;
        /// <summary>0..1 severity used by fuel slip/load model and audio.</summary>
        public readonly float DragSeverity;

        public SurfaceCoefficients(SurfaceType surface, float dryGrip, float wetMultiplier, float rollingResistance)
        {
            Surface = surface;
            DryGrip = dryGrip;
            WetMultiplier = wetMultiplier;
            RollingResistance = rollingResistance;
            DragSeverity = MathUtil.Clamp01(rollingResistance / 0.20f);
        }

        public float EffectiveGrip(bool wet) => wet ? DryGrip * WetMultiplier : DryGrip;
    }

    /// <summary>Authoritative surface lookup table (§4.4). Cached by callers; no per-frame allocation.</summary>
    public static class SurfaceTable
    {
        public const float EffectiveGripCap = 1.25f;

        static readonly SurfaceCoefficients[] Table =
        {
            new SurfaceCoefficients(SurfaceType.IntactAsphalt, 1.00f, 0.75f, 0.015f),
            new SurfaceCoefficients(SurfaceType.BrokenAsphalt,  0.90f, 0.75f, 0.025f),
            new SurfaceCoefficients(SurfaceType.PackedDirt,     0.82f, 0.72f, 0.035f),
            new SurfaceCoefficients(SurfaceType.Gravel,         0.70f, 0.85f, 0.045f),
            new SurfaceCoefficients(SurfaceType.Grass,          0.65f, 0.65f, 0.050f),
            new SurfaceCoefficients(SurfaceType.Rock,           0.90f, 0.65f, 0.030f),
            new SurfaceCoefficients(SurfaceType.ShallowMud,     0.48f, 0.80f, 0.110f),
            new SurfaceCoefficients(SurfaceType.DeepMud,        0.30f, 0.80f, 0.200f),
            new SurfaceCoefficients(SurfaceType.Sand,           0.50f, 1.00f, 0.100f),
            new SurfaceCoefficients(SurfaceType.PackedSnow,     0.38f, 0.85f, 0.070f),
            new SurfaceCoefficients(SurfaceType.Ice,            0.12f, 1.00f, 0.010f)
        };

        public static SurfaceCoefficients Get(SurfaceType surface) => Table[(int)surface];
        public static int Count => Table.Length;

        public static bool IsMud(SurfaceType s) => s == SurfaceType.ShallowMud || s == SurfaceType.DeepMud;
        public static bool IsSnowLike(SurfaceType s) => s == SurfaceType.PackedSnow || s == SurfaceType.Ice;

        /// <summary>
        /// Cross-fade between two surfaces over 0.15 s (§4.4). t in [0,1].
        /// Water safety-critical state changes bypass this and apply immediately.
        /// </summary>
        public static SurfaceCoefficients Blend(SurfaceCoefficients from, SurfaceCoefficients to, float t)
        {
            t = MathUtil.Clamp01(t);
            return new SurfaceCoefficients(
                t < 0.5f ? from.Surface : to.Surface,
                MathUtil.Lerp(from.DryGrip, to.DryGrip, t),
                MathUtil.Lerp(from.WetMultiplier, to.WetMultiplier, t),
                MathUtil.Lerp(from.RollingResistance, to.RollingResistance, t));
        }
    }

    /// <summary>
    /// Tire set definitions (§10.2). Mutually exclusive. Multipliers compose exactly once with
    /// surface grip — never stacked with the Trail Tires research bonus twice (QA case 8).
    /// </summary>
    public enum TireSet
    {
        AllTerrain,
        Mud,
        Rock,
        Snow
    }

    public static class TireSetRules
    {
        public const float MudMudGrip = 1.35f;         // +35% relative mud grip
        public const float MudGrassGrip = 1.10f;       // +10% grass
        public const float MudAsphaltLateral = 0.90f;  // −10% asphalt lateral response
        public const float RockDryRockGrip = 1.20f;    // +20% dry rock grip
        public const float RockDeepMudGrip = 0.90f;    // −10% deep-mud grip
        public const float SnowSnowGrip = 1.35f;       // +35% snow
        public const float SnowIceGrip = 1.20f;        // +20% ice
        public const float SnowAsphaltGrip = 0.92f;    // −8% dry asphalt

        /// <summary>
        /// Applies the tire multiplier for a surface. Research 'Trail Tires I' is NOT applied here;
        /// it is composed by VehicleStatsResolver as a separate researched-module modifier so
        /// bonuses can never be applied twice.
        /// </summary>
        public static float SurfaceMultiplier(TireSet tires, SurfaceType surface, bool wet, out float lateralMultiplier)
        {
            lateralMultiplier = 1f;
            float grip = SurfaceTable.Get(surface).EffectiveGrip(wet);

            switch (tires)
            {
                case TireSet.Mud:
                    if (surface == SurfaceType.ShallowMud) grip *= MudMudGrip;
                    if (surface == SurfaceType.DeepMud) grip *= MudMudGrip;
                    if (surface == SurfaceType.Grass) grip *= MudGrassGrip;
                    if (surface == SurfaceType.IntactAsphalt || surface == SurfaceType.BrokenAsphalt)
                        lateralMultiplier = MudAsphaltLateral;
                    break;
                case TireSet.Rock:
                    if (surface == SurfaceType.Rock && !wet) grip *= RockDryRockGrip;
                    if (surface == SurfaceType.DeepMud) grip *= RockDeepMudGrip;
                    break;
                case TireSet.Snow:
                    if (surface == SurfaceType.PackedSnow) grip *= SnowSnowGrip;
                    if (surface == SurfaceType.Ice) grip *= SnowIceGrip;
                    if (surface == SurfaceType.IntactAsphalt && !wet) grip *= SnowAsphaltGrip;
                    break;
            }

            // Explicit cap so upgrades cannot stack indefinitely (§10.2).
            return Math.Min(grip, SurfaceTable.EffectiveGripCap);
        }
    }
}
