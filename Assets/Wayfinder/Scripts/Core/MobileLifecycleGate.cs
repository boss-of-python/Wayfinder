namespace Wayfinder.Core
{
    /// <summary>
    /// Android reports focus and application pause independently. A positive callback for
    /// one signal must not undo suspension while the other signal still blocks gameplay.
    /// Returning true means the effective state changed, not that the app is active.
    /// </summary>
    public sealed class MobileLifecycleGate
    {
        public bool HasFocus { get; private set; } = true;
        public bool ApplicationPaused { get; private set; }
        public bool IsSuspended => ApplicationPaused || !HasFocus;

        public bool SetFocus(bool hasFocus)
        {
            bool before = IsSuspended;
            HasFocus = hasFocus;
            return before != IsSuspended;
        }

        public bool SetApplicationPaused(bool paused)
        {
            bool before = IsSuspended;
            ApplicationPaused = paused;
            return before != IsSuspended;
        }
    }
}
