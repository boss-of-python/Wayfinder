using System;

namespace Wayfinder.Core
{
    /// <summary>
    /// Typed failure reasons for normal gameplay outcomes. Exceptions are reserved for
    /// unexpected faults — never for "inventory full" cases (Appendix D).
    /// </summary>
    public enum FailureReason
    {
        None = 0,
        InventoryFull,
        InsufficientMassCapacity,
        InsufficientSlots,
        InsufficientCurrency,
        InsufficientMaterials,
        ItemNotFound,
        ItemNotUsableHere,
        AlreadyCollected,
        AlreadyApplied,
        PrerequisiteNotMet,
        MissionLocked,
        MissionNotAccepted,
        OverCapacity,
        TooFarAway,
        LineOfSightBlocked,
        WrongState,
        VehicleMoving,
        NoValidAnchor,
        InvalidTarget,
        SaveCorrupt,
        SaveIoFailure,
        SaveSchemaTooNew,
        ContentMissing,
        Cancelled,
        InternalError
    }

    /// <summary>Result of a command/validation. Success payloads live in dedicated result types.</summary>
    public readonly struct CommandResult
    {
        public readonly bool Success;
        public readonly FailureReason Reason;
        public readonly string Detail;

        CommandResult(bool success, FailureReason reason, string detail)
        {
            Success = success;
            Reason = reason;
            Detail = detail ?? string.Empty;
        }

        public static CommandResult Ok() => new CommandResult(true, FailureReason.None, string.Empty);
        public static CommandResult Fail(FailureReason reason, string detail = null) =>
            new CommandResult(false, reason, detail);

        public override string ToString() => Success ? "Ok" : $"Fail({Reason}: {Detail})";
    }

    /// <summary>Capacity check for a prospective stack addition (Appendix D IInventoryService).</summary>
    public readonly struct CapacityResult
    {
        public readonly bool Fits;
        public readonly FailureReason Reason;
        public readonly int RequiredSlots;
        public readonly int AvailableSlots;
        public readonly float RequiredMassKg;
        public readonly float AvailableMassKg;

        public CapacityResult(bool fits, FailureReason reason,
            int requiredSlots, int availableSlots, float requiredMassKg, float availableMassKg)
        {
            Fits = fits;
            Reason = reason;
            RequiredSlots = requiredSlots;
            AvailableSlots = availableSlots;
            RequiredMassKg = requiredMassKg;
            AvailableMassKg = availableMassKg;
        }

        public static CapacityResult Fit(int requiredSlots, int availableSlots, float requiredMassKg, float availableMassKg) =>
            new CapacityResult(true, FailureReason.None, requiredSlots, availableSlots, requiredMassKg, availableMassKg);

        public static CapacityResult Rejected(FailureReason reason, int requiredSlots, int availableSlots,
            float requiredMassKg, float availableMassKg) =>
            new CapacityResult(false, reason, requiredSlots, availableSlots, requiredMassKg, availableMassKg);
    }

    public enum SaveResultStatus
    {
        Saved,
        FailedWrite,
        FailedVerify,
        Cancelled
    }

    public readonly struct SaveResult
    {
        public readonly SaveResultStatus Status;
        public readonly int Revision;
        public readonly string Detail;

        public SaveResult(SaveResultStatus status, int revision, string detail = null)
        {
            Status = status;
            Revision = revision;
            Detail = detail ?? string.Empty;
        }

        public bool Success => Status == SaveResultStatus.Saved;
    }

    public enum LoadResultStatus
    {
        LoadedCurrent,
        LoadedBackup,
        NoSave,
        CorruptReadOnlyRecovery,
        SchemaTooNewReadOnly,
        Failed
    }

    /// <summary>Utility clamp helpers used by several domain models; kept allocation-free.</summary>
    public static class MathUtil
    {
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static float Clamp(float v, float min, float max) =>
            v < min ? min : (v > max ? max : v);

        public static double Clamp(double v, double min, double max) =>
            v < min ? min : (v > max ? max : v);

        /// <summary>Linear interpolation with clamped t.</summary>
        public static float Lerp(float a, float b, float t)
        {
            t = Clamp01(t);
            return a + (b - a) * t;
        }

        /// <summary>Frame-rate independent exponential smoothing factor.</summary>
        public static float SmoothFactor(float timeConstantSeconds, float deltaTime)
        {
            if (timeConstantSeconds <= 0f) return 1f;
            return 1f - (float)Math.Exp(-deltaTime / timeConstantSeconds);
        }
    }
}
