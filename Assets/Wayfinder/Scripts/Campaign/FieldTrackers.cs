using System;
using Wayfinder.Progression;
using Wayfinder.Simulation;

namespace Wayfinder.Campaign
{
    /// <summary>
    /// Garage arrival (§2.3): inside the arrival volume below 1 m/s for 2 s continuously begins
    /// extraction. The vehicle must have left the volume once per expedition, so the departure
    /// spawn (which is inside the volume) can never end an expedition immediately.
    /// </summary>
    public sealed class ArrivalTracker
    {
        public const float MaxSpeedMs = 1f;
        public const float HoldSeconds = 2f;

        float _held;
        bool _armed;

        public bool Armed => _armed;
        public float HeldSeconds => _held;

        public void Reset(bool startInsideVolume)
        {
            _held = 0f;
            _armed = !startInsideVolume;
        }

        /// <summary>Returns true on the frame the arrival condition is satisfied.</summary>
        public bool Update(bool insideVolume, float speedMs, float deltaSeconds)
        {
            if (!insideVolume)
            {
                _armed = true;
                _held = 0f;
                return false;
            }
            if (!_armed || Math.Abs(speedMs) >= MaxSpeedMs)
            {
                _held = 0f;
                return false;
            }
            float before = _held;
            _held += Math.Max(0f, deltaSeconds);
            return before < HoldSeconds && _held >= HoldSeconds;
        }
    }

    /// <summary>
    /// Landmark observation (§8.2): inside the observation volume, with line of sight, below
    /// 15 km/h. Accumulates continuous observation time; any broken condition resets it.
    /// </summary>
    public sealed class ObservationTracker
    {
        string _targetId = string.Empty;
        float _held;

        public string TargetId => _targetId;
        public float HeldSeconds => _held;

        public void Clear()
        {
            _targetId = string.Empty;
            _held = 0f;
        }

        /// <summary>Returns observed seconds for <paramref name="targetId"/> (0 when conditions fail).</summary>
        public float Update(string targetId, bool lineOfSight, float speedKph, float deltaSeconds)
        {
            if (string.IsNullOrEmpty(targetId) || !lineOfSight || speedKph >= DiscoveryService.LandmarkDiscoveryMaxSpeedKph)
            {
                Clear();
                return 0f;
            }
            if (!string.Equals(targetId, _targetId, StringComparison.Ordinal))
            {
                _targetId = targetId;
                _held = 0f;
            }
            _held += Math.Max(0f, deltaSeconds);
            return _held;
        }
    }

    /// <summary>
    /// Hold-to-interact (§16.4): 0.35 s pickups / 0.75 s substantial actions, with a tap
    /// alternative (accessibility, §3.5). Fires once per press; the input must be released
    /// before it can fire again, so one long press can never double-commit.
    /// </summary>
    public sealed class HoldInteractTracker
    {
        float _held;
        bool _firedThisPress;
        bool _wasPressed;

        public float Progress01 { get; private set; }

        public void Reset()
        {
            _held = 0f;
            _firedThisPress = false;
            _wasPressed = false;
            Progress01 = 0f;
        }

        public bool Update(bool pressed, bool substantial, bool tapMode, float deltaSeconds)
        {
            float required = InteractionService.RequiredHoldSeconds(substantial);
            if (!pressed)
            {
                _held = 0f;
                _firedThisPress = false;
                _wasPressed = false;
                Progress01 = 0f;
                return false;
            }
            bool pressStarted = !_wasPressed;
            _wasPressed = true;
            if (_firedThisPress) return false;
            if (tapMode)
            {
                if (!pressStarted) return false;
                _firedThisPress = true;
                Progress01 = 1f;
                return true;
            }
            _held += Math.Max(0f, deltaSeconds);
            Progress01 = Math.Min(1f, _held / required);
            if (_held < required) return false;
            _firedThisPress = true;
            return true;
        }
    }

    /// <summary>
    /// Samples a safe reset pose every 2 s when <see cref="RecoveryRules.IsValidSafePose"/>
    /// holds (§5.6). The session persists the latest sample as lastSafePose.
    /// </summary>
    public sealed class SafePoseSampler
    {
        float _sinceSample = RecoveryRules.SafePoseSampleIntervalSeconds;

        public bool Update(float deltaSeconds, bool grounded, float speedMs, float rollDegrees, bool inWater,
            bool inDeepMud, float clearanceM, bool insideUnresolvedMissionTrigger)
        {
            _sinceSample += Math.Max(0f, deltaSeconds);
            if (_sinceSample < RecoveryRules.SafePoseSampleIntervalSeconds) return false;
            if (!RecoveryRules.IsValidSafePose(grounded, speedMs, rollDegrees, inWater, inDeepMud, clearanceM,
                    insideUnresolvedMissionTrigger))
                return false;
            _sinceSample = 0f;
            return true;
        }
    }
}
