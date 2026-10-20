using System;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Per-wheel surface cross-fade (§4.4: "cross-fade traction changes over 0.15 s at material
    /// boundaries so a seam does not read as a jolt").
    /// <para>
    /// <c>SurfaceTable.Blend</c> has always existed, but nothing called it: <c>VehicleController</c>
    /// wrote the raw coefficient of whichever surface a wheel was standing on each physics tick. At
    /// 20 m/s a 3 m mud patch is crossed in 0.15 s, so the whole transition happened between two
    /// <c>FixedUpdate</c>s and PhysX saw a step change in friction stiffness — the reported
    /// "physics jitter on surface transitions" (SYS-SIM-001).
    /// </para>
    /// <para>
    /// This is a struct with no allocation, stepped once per wheel per physics tick. Water
    /// safety-critical changes (stall depth, deep-water grip loss) pass <c>immediate: true</c> and
    /// bypass the fade, as §4.4 requires.
    /// </para>
    /// </summary>
    public struct SurfaceTransition
    {
        /// <summary>Cross-fade duration in seconds (§4.4).</summary>
        public const float CrossfadeSeconds = 0.15f;

        /// <summary>Surface the fade started from.</summary>
        public SurfaceType From { get; private set; }

        /// <summary>Surface the wheel is on now.</summary>
        public SurfaceType To { get; private set; }

        /// <summary>0..1 fade progress; 1 means the coefficients are exactly the target's.</summary>
        public float Progress { get; private set; }

        /// <summary>
        /// Coefficients the fade starts from. Held as coefficients, not as an enum, so a second
        /// boundary crossed mid-fade continues from the value the wheel actually had instead of
        /// snapping back to the previous surface.
        /// </summary>
        SurfaceCoefficients _fromCoefficients;

        /// <summary>True once the wheel has reported one surface.</summary>
        public bool IsInitialised { get; private set; }

        /// <summary>True while a fade is still running.</summary>
        public bool IsBlending => IsInitialised && Progress < 1f && From != To;

        /// <summary>
        /// Feeds the surface observed this tick. Returns true when a new surface boundary was
        /// crossed (the fade was restarted), which the caller uses to record a transition.
        /// </summary>
        public bool Step(SurfaceType observed, float deltaSeconds, bool immediate = false)
        {
            observed = SurfaceTable.Sanitize(observed);

            if (!IsInitialised)
            {
                From = To = observed;
                _fromCoefficients = SurfaceTable.Get(observed);
                Progress = 1f;
                IsInitialised = true;
                return false;
            }

            if (observed != To)
            {
                // Restart the fade from wherever the blend currently is, so a second boundary
                // crossed mid-fade never snaps: the new "from" is the coefficient the wheel was
                // already using, not the surface it started on.
                _fromCoefficients = Coefficients;
                From = Progress >= 1f ? To : From;
                To = observed;
                Progress = 0f;
                if (immediate)
                {
                    Progress = 1f;
                    _fromCoefficients = SurfaceTable.Get(To);
                }
                return true;
            }

            if (immediate)
            {
                From = To;
                _fromCoefficients = SurfaceTable.Get(To);
                Progress = 1f;
                return false;
            }

            if (Progress < 1f)
            {
                float step = deltaSeconds > 0f && !float.IsNaN(deltaSeconds)
                    ? deltaSeconds / CrossfadeSeconds
                    : 1f; // a non-positive tick must not freeze the blend
                Progress = Math.Min(1f, Progress + step);
                if (Progress >= 1f)
                {
                    From = To;
                    _fromCoefficients = SurfaceTable.Get(To);
                }
            }
            return false;
        }

        /// <summary>Jumps straight to a surface with no fade (§4.4 water safety path).</summary>
        public void SnapTo(SurfaceType surface)
        {
            surface = SurfaceTable.Sanitize(surface);
            From = To = surface;
            _fromCoefficients = SurfaceTable.Get(surface);
            Progress = 1f;
            IsInitialised = true;
        }

        /// <summary>Forgets everything; the next <see cref="Step"/> re-initialises without a fade.</summary>
        public void Reset()
        {
            From = To = SurfaceType.PackedDirt;
            _fromCoefficients = SurfaceTable.Get(SurfaceType.PackedDirt);
            Progress = 1f;
            IsInitialised = false;
        }

        /// <summary>The blended coefficients to apply to this wheel's friction this tick.</summary>
        public SurfaceCoefficients Coefficients =>
            Progress >= 1f
                ? SurfaceTable.Get(To)
                : SurfaceTable.Blend(_fromCoefficients, SurfaceTable.Get(To), Progress);

        /// <summary>Blended effective grip (dry or wet) for this tick.</summary>
        public float EffectiveGrip(bool wet) => Coefficients.EffectiveGrip(wet);
    }
}
