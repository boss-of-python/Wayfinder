using System;

namespace Wayfinder.Core
{
    /// <summary>
    /// Stable content identifier (e.g. "cache.forest.017", "mission.Q03", "upgrade.scout.06.tank1").
    /// Persistence must never rely on scene instance IDs, mutable positions, or hash codes.
    /// </summary>
    public readonly struct ContentId : IEquatable<ContentId>
    {
        public readonly string Value;

        public ContentId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("ContentId must be a non-empty stable string.", nameof(value));
            Value = value;
        }

        public bool Equals(ContentId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ContentId other && Equals(other);

        // Ordinal comparison only; never culture-sensitive hashing for persistence.
        public override int GetHashCode() => Value == null ? 0 : Value.GetHashCode(StringComparison.Ordinal);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ContentId left, ContentId right) => left.Equals(right);
        public static bool operator !=(ContentId left, ContentId right) => !left.Equals(right);

        public static implicit operator string(ContentId id) => id.Value;
    }

    /// <summary>Identifies one atomic gameplay mutation; used for idempotency.</summary>
    public readonly struct TransactionId : IEquatable<TransactionId>
    {
        public readonly string Value;

        public TransactionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("TransactionId must be non-empty.", nameof(value));
            Value = value;
        }

        /// <summary>Deterministic expedition-scoped sequence id (append-only per §2.3).</summary>
        public static TransactionId ExpeditionSequence(string expeditionId, long sequence)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            return new TransactionId($"{expeditionId}:{sequence}");
        }

        public bool Equals(TransactionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TransactionId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : Value.GetHashCode(StringComparison.Ordinal);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(TransactionId left, TransactionId right) => left.Equals(right);
        public static bool operator !=(TransactionId left, TransactionId right) => !left.Equals(right);
    }

    /// <summary>Save slot identity. Release scope has exactly three slots (§1.4).</summary>
    public readonly struct SaveSlotId : IEquatable<SaveSlotId>
    {
        public const int SlotCount = 3;
        public readonly int Index;

        public SaveSlotId(int index)
        {
            if (index < 0 || index >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(index), "Slot index must be 0..2.");
            Index = index;
        }

        public string FileName => $"slot-{Index}";
        public bool Equals(SaveSlotId other) => Index == other.Index;
        public override bool Equals(object obj) => obj is SaveSlotId other && Equals(other);
        public override int GetHashCode() => Index;
        public override string ToString() => FileName;
    }

    /// <summary>Inventory container identity (vehicle cargo, garage storage, camp storage, quest ledger).</summary>
    public readonly struct InventoryId : IEquatable<InventoryId>
    {
        public readonly string Value;
        public InventoryId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("InventoryId must be non-empty.", nameof(value));
            Value = value;
        }

        public bool Equals(InventoryId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is InventoryId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : Value.GetHashCode(StringComparison.Ordinal);
        public override string ToString() => Value ?? string.Empty;
    }
}
