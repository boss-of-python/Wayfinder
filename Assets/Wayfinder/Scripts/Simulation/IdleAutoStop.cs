using System;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Engine auto-stop while parked (§5.1: "idle at zero throttle still consumes the base amount
    /// unless auto-stop is enabled after 15 seconds parked").
    /// <para>
    /// <b>Why this exists (SYS-SIM-002).</b> The issue report asked for an <c>IdleBurnRate</c>
    /// multiplier that reduces consumption at zero throttle. That would contradict the authoritative
    /// drain formula and its QA example (<c>0.003 + 0.025·throttle + 0.015·load + 0.004·slip</c>,
    /// 0.02255 L/s at throttle 0.45). The blueprint's own answer to idle drain is an auto-stop after
    /// 15 s parked: the engine turns off, and an engine that is off consumes exactly zero
    /// (<c>FuelModel.LitersPerSecond(..., engineOn: false)</c>). <c>FuelModel.AutoStopIdleSeconds</c>
    /// existed as a constant but nothing ever read it, so a parked vehicle burned the 0.003 L/s base
    /// rate forever and the documented behaviour was unreachable.
    /// </para>
    /// <para>
    /// Pure and engine-free so it is covered by executed EditMode tests. The Unity controller owns
    /// the actual engine on/off flag; this model never touches a <c>WheelCollider</c>.
    /// </para>
    /// </summary>
    public sealed class IdleAutoStopModel
    {
        /// <summary>Throttle at or below this counts as released (same threshold as §5.1's idle check).</summary>
        public const float ThrottleThreshold = 0.01f;

        /// <summary>At or below this speed the vehicle counts as parked (§5.1 "parked").</summary>
        public const float ParkedSpeedMetersPerSecond = 0.2f;

        /// <summary>Seconds parked before the engine stops itself.</summary>
        public const float DelaySeconds = FuelModel.AutoStopIdleSeconds; // 15 s

        float _parkedSeconds;

        /// <summary>Seconds spent parked with the engine running since the last release/move.</summary>
        public float ParkedSeconds => _parkedSeconds;

        /// <summary>True while this model is holding the engine off (not a manual or fault stop).</summary>
        public bool AutoStopped { get; private set; }

        /// <summary>0..1 progress toward the auto-stop, for a HUD hint or audio.</summary>
        public float Progress => AutoStopped ? 1f : MathUtil.Clamp01(_parkedSeconds / DelaySeconds);

        /// <summary>
        /// Steps the model for one tick. Returns <c>true</c> exactly once, on the tick the engine
        /// should be stopped; <paramref name="requestRestart"/> is <c>true</c> when a pedal was
        /// pressed after an auto-stop and the caller should re-enable the engine.
        /// </summary>
        /// <param name="throttle">Driver throttle demand 0..1 (not brake: brake is how reverse is requested).</param>
        /// <param name="brake">Brake demand 0..1; pressing it also wakes an auto-stopped engine.</param>
        /// <param name="speedMetersPerSecond">Current vehicle speed.</param>
        /// <param name="engineOn">Current engine flag owned by the controller.</param>
        /// <param name="deltaSeconds">Simulation delta (never wall clock).</param>
        public bool Step(float throttle, float brake, float speedMetersPerSecond,
            bool engineOn, float deltaSeconds, out bool requestRestart)
        {
            requestRestart = false;

            if (AutoStopped)
            {
                // A pedal wakes the engine. A dead engine (0 % condition) stays dead: the caller
                // checks that before re-enabling.
                if (WakeInput(throttle) || WakeInput(brake))
                {
                    AutoStopped = false;
                    _parkedSeconds = 0f;
                    requestRestart = true;
                }
                return false;
            }

            // A held brake is driver intent, not "parked": with this gearbox the brake is also how
            // reverse is requested, so a 15 s brake hold must never be read as an unattended idle.
            if (!engineOn || WakeInput(throttle) || WakeInput(brake) ||
                speedMetersPerSecond > ParkedSpeedMetersPerSecond || float.IsNaN(speedMetersPerSecond))
            {
                _parkedSeconds = 0f;
                return false;
            }

            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds)) return false;

            _parkedSeconds += deltaSeconds;
            if (_parkedSeconds < DelaySeconds) return false;

            AutoStopped = true;
            _parkedSeconds = DelaySeconds;
            return true;
        }

        /// <summary>
        /// Releases the latch without restarting anything (respawn, refuel, recovery, forced engine
        /// stop). The next parked interval starts its own 15 s countdown.
        /// </summary>
        public void Clear()
        {
            AutoStopped = false;
            _parkedSeconds = 0f;
        }

        static bool WakeInput(float axis) => axis > ThrottleThreshold && !float.IsNaN(axis);
    }
}
