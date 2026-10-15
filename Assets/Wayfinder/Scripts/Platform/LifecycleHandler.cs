using System;
using UnityEngine;
using Wayfinder.Core;

namespace Wayfinder.Platform
{
    /// <summary>
    /// Suspend until BOTH Android pause and focus signals allow resume. Consumers must
    /// explicitly wait for tap-to-resume; this adapter never unpauses gameplay itself.
    /// Campaign save snapshots remain a separate integration task.
    /// </summary>
    public sealed class LifecycleHandler : MonoBehaviour
    {
        readonly MobileLifecycleGate _gate = new MobileLifecycleGate();
        public event Action OnSuspendRequested;
        public event Action OnResumeRequested;
        public bool IsSuspended => _gate.IsSuspended;

        void OnApplicationPause(bool paused)
        {
            if (_gate.SetApplicationPaused(paused)) NotifyTransition();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (_gate.SetFocus(hasFocus)) NotifyTransition();
        }

        void NotifyTransition()
        {
            if (IsSuspended) OnSuspendRequested?.Invoke();
            else OnResumeRequested?.Invoke();
        }
    }
}
