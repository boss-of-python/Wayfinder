using System;

namespace Wayfinder.Campaign
{
    /// <summary>
    /// Autosave scheduling (§16.2): debounce 10–20 s during traversal plus immediate saves for
    /// critical events (handled by the session). A non-critical change starts the debounce
    /// window; the save runs once the window elapses and at least <see cref="MinIntervalSeconds"/>
    /// passed since the previous save. A failed save keeps the state dirty and retries after
    /// <see cref="RetryAfterFailureSeconds"/> — it never claims success (§17.4).
    /// Driven by unscaled real time so pausing the simulation does not postpone persistence.
    /// </summary>
    public sealed class AutosavePolicy
    {
        public const float DebounceSeconds = 15f;
        public const float MinIntervalSeconds = 10f;
        public const float RetryAfterFailureSeconds = 20f;

        float _sinceDirty;
        float _sinceLastSave = float.MaxValue;
        float _retryCooldown;

        public bool IsDirty { get; private set; }
        public bool LastAttemptFailed { get; private set; }

        public void MarkDirty()
        {
            if (IsDirty) return;
            IsDirty = true;
            _sinceDirty = 0f;
        }

        /// <summary>Advances timers; returns true when an autosave should run now.</summary>
        public bool Tick(float unscaledDeltaSeconds)
        {
            if (unscaledDeltaSeconds < 0f || float.IsNaN(unscaledDeltaSeconds)) unscaledDeltaSeconds = 0f;
            if (_sinceLastSave < float.MaxValue) _sinceLastSave += unscaledDeltaSeconds;
            _retryCooldown = Math.Max(0f, _retryCooldown - unscaledDeltaSeconds);
            if (!IsDirty) return false;
            _sinceDirty += unscaledDeltaSeconds;
            if (_retryCooldown > 0f) return false;
            return _sinceDirty >= DebounceSeconds && _sinceLastSave >= MinIntervalSeconds;
        }

        public void MarkSaved()
        {
            IsDirty = false;
            LastAttemptFailed = false;
            _sinceDirty = 0f;
            _sinceLastSave = 0f;
            _retryCooldown = 0f;
        }

        public void MarkFailed()
        {
            IsDirty = true;
            LastAttemptFailed = true;
            _retryCooldown = RetryAfterFailureSeconds;
        }
    }
}
