using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    // SurfaceType (§4.4 surface labels) lives in Wayfinder.Core.SurfaceType so that World and
    // Simulation share one definition without an assembly dependency between them.

    /// <summary>
    /// Tuning coefficients per §4.4. These are game tuning values, not measured tire-material
    /// constants. Effective μ after tire/weather/condition composition is capped (initially ≤ 1.25).
    /// <para>
    /// Every field is clamped in the constructor (SYS-SIM-001). A coefficient that is NaN, negative
    /// or above the documented envelope can never reach a <c>WheelCollider</c>: a single NaN
    /// stiffness makes PhysX discard the friction force for that wheel, which reads to the player as
    /// a sudden loss of grip at the exact frame a surface boundary is crossed.
    /// </para>
    /// </summary>
    public readonly struct SurfaceCoefficients
    {
        public const float MaxRollingResistance = 0.20f;

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
            DryGrip = ClampFinite(dryGrip, 0f, SurfaceTable.EffectiveGripCap);
            WetMultiplier = ClampFinite(wetMultiplier, 0f, 1f);
            RollingResistance = ClampFinite(rollingResistance, 0f, MaxRollingResistance);
            DragSeverity = MathUtil.Clamp01(RollingResistance / MaxRollingResistance);
        }

        public float EffectiveGrip(bool wet) =>
            MathUtil.Clamp(wet ? DryGrip * WetMultiplier : DryGrip, 0f, SurfaceTable.EffectiveGripCap);

        /// <summary>
        /// NaN-safe clamp. <c>MathUtil.Clamp</c> propagates NaN (both comparisons are false), and a
        /// NaN reaching <c>WheelCollider.forwardFriction.stiffness</c> silently disables that wheel's
        /// friction instead of failing loudly.
        /// </summary>
        internal static float ClampFinite(float value, float min, float max) =>
            float.IsNaN(value) ? min : (value < min ? min : (value > max ? max : value));
    }

    /// <summary>Authoritative surface lookup table (§4.4). Cached by callers; no per-frame allocation.</summary>
    public static class SurfaceTable
    {
        public const float EffectiveGripCap = 1.25f;

        /// <summary>
        /// Upper bound for a <c>WheelCollider</c> friction stiffness written by this project.
        /// Unity's default stiffness is 1; values well above 2 make the solver impulsive and are a
        /// common source of jitter at speed, so the composed grip is clamped before it is applied
        /// (SYS-SIM-001).
        /// </summary>
        public const float MaxFrictionStiffness = 2.0f;

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

        /// <summary>
        /// True when <paramref name="surface"/> is a declared enum member <b>and</b> the table entry at
        /// that index still describes that member. The second half matters because the table is
        /// indexed by enum value: if a future edit reorders one without the other, a valid label
        /// would silently resolve to another surface's coefficients.
        /// </summary>
        public static bool IsDefined(SurfaceType surface)
        {
            int index = (int)surface;
            return index >= 0 && index < Table.Length && Table[index].Surface == surface;
        }

        /// <summary>
        /// Maps an undefined value (a corrupt baked chunk map, or an inspector dropdown that was
        /// edited to a removed member) onto <see cref="SurfaceType.PackedDirt"/> — the neutral
        /// authored surface — instead of throwing inside <c>FixedUpdate</c> or reading past the end
        /// of the table (SYS-SIM-001).
        /// </summary>
        public static SurfaceType Sanitize(SurfaceType surface) =>
            IsDefined(surface) ? surface : SurfaceType.PackedDirt;

        /// <summary>
        /// Safe lookup. Out-of-range values fall back to packed dirt; callers that need to know the
        /// label was invalid use <see cref="TryGet"/>.
        /// </summary>
        public static SurfaceCoefficients Get(SurfaceType surface) => Table[(int)Sanitize(surface)];

        /// <summary>Lookup that reports whether the label was valid, without allocating.</summary>
        public static bool TryGet(SurfaceType surface, out SurfaceCoefficients coefficients)
        {
            if (!IsDefined(surface))
            {
                coefficients = Table[(int)SurfaceType.PackedDirt];
                return false;
            }
            coefficients = Table[(int)surface];
            return true;
        }

        public static int Count => Table.Length;

        /// <summary>
        /// Clamps a composed friction stiffness into the range PhysX can integrate without spikes.
        /// NaN collapses to 0 (no friction) rather than to the maximum, so a corrupt coefficient can
        /// never turn into unbounded grip.
        /// </summary>
        public static float ClampFrictionStiffness(float stiffness) =>
            SurfaceCoefficients.ClampFinite(stiffness, 0f, MaxFrictionStiffness);

        public static bool IsMud(SurfaceType s) => s == SurfaceType.ShallowMud || s == SurfaceType.DeepMud;
        public static bool IsSnowLike(SurfaceType s) => s == SurfaceType.PackedSnow || s == SurfaceType.Ice;

        /// <summary>
        /// Cross-fade between two surfaces over 0.15 s (§4.4). t in [0,1]; NaN collapses to 0 so a
        /// bad timer keeps the previous coefficients instead of producing NaN grip.
        /// Water safety-critical state changes bypass this and apply immediately.
        /// </summary>
        public static SurfaceCoefficients Blend(SurfaceCoefficients from, SurfaceCoefficients to, float t)
        {
            t = SurfaceCoefficients.ClampFinite(t, 0f, 1f);
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
