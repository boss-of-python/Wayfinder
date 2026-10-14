using System;

namespace Wayfinder.Core
{
    /// <summary>Application-level states (§2.5). Transitions must be explicit commands.</summary>
    public enum AppState
    {
        Boot,
        Title,
        Loading,
        Garage,
        Driving,
        MapPaused,
        Camp,
        Recovery,
        ExpeditionSummary,
        Credits,
        Suspended
    }

    /// <summary>Vehicle states (§2.5).</summary>
    public enum VehicleState
    {
        Parked,
        Driving,
        Airborne,
        Winching,
        Stalled,
        Disabled,
        Recovering
    }

    /// <summary>Mission lifecycle (§2.5). Never regresses on vehicle recovery.</summary>
    public enum MissionState
    {
        Locked,
        Available,
        Accepted,
        Active,
        ReadyToTurnIn,
        Completed
    }

    public enum GameMode
    {
        /// <summary>Fuel, condition, weight and recovery rules active (default).</summary>
        Expedition,
        /// <summary>Fuel and mechanical damage can be disabled independently. No reward punishment.</summary>
        RelaxedExploration
    }

    public enum WeatherState
    {
        Clear,
        Overcast,
        Rain,
        Storm,
        Fog,
        Snow,
        Wind
    }

    public enum DayNightPhase
    {
        Dawn,
        Day,
        Dusk,
        Night
    }

    /// <summary>
    /// Pause token acquired by UI overlays (§2.5). Closing one overlay cannot unpause
    /// another overlay's token: tokens are strictly owned and released by their acquirer.
    /// </summary>
    public sealed class PauseTokenRegistry
    {
        int _tokens;

        public bool IsPaused => _tokens > 0;
        public int TokenCount => _tokens;

        public IDisposable Acquire()
        {
            _tokens++;
            return new Token(this);
        }

        void Release()
        {
            if (_tokens <= 0)
                throw new InvalidOperationException("Pause token released without acquisition.");
            _tokens--;
        }

        sealed class Token : IDisposable
        {
            PauseTokenRegistry _owner;
            public Token(PauseTokenRegistry owner) => _owner = owner;
            public void Dispose()
            {
                if (_owner != null)
                {
                    _owner.Release();
                    _owner = null;
                }
            }
        }
    }

    /// <summary>
    /// Simulation clock that never advances while paused (§16.2). Fuel/wear must integrate
    /// with simulation delta — never wall-clock delta across suspend/resume.
    /// </summary>
    public sealed class SimulationTime
    {
        public double ElapsedSimulationSeconds { get; private set; }
        public float DayTimeSeconds { get; private set; }
        public bool Paused { get; set; }

        public const float DayLengthSeconds = 1200f; // tuning value: full day cycle

        public void Advance(float deltaSeconds)
        {
            if (Paused || deltaSeconds <= 0f) return;
            ElapsedSimulationSeconds += deltaSeconds;
            DayTimeSeconds = (DayTimeSeconds + deltaSeconds) % DayLengthSeconds;
        }

        public DayNightPhase Phase
        {
            get
            {
                float t = DayTimeSeconds / DayLengthSeconds;
                if (t < 0.22f) return DayNightPhase.Dawn;
                if (t < 0.70f) return DayNightPhase.Day;
                if (t < 0.82f) return DayNightPhase.Dusk;
                return DayNightPhase.Night;
            }
        }
    }
}
