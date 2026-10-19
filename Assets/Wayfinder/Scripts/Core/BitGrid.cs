using System;
using System.Collections.Generic;

namespace Wayfinder.Core
{
    /// <summary>
    /// Fixed-size bitset with base64 round-trip, used for the 96 × 96 map grid (§8.1),
    /// revealed/surveyed cells and countable mask. Indexed row-major: index = z * Width + x.
    /// </summary>
    public sealed class BitGrid
    {
        public readonly int Width;
        public readonly int Height;
        readonly ulong[] _words;

        public BitGrid(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            _words = new ulong[(width * height + 63) / 64];
        }

        public int CellCount => Width * Height;

        public bool Get(int x, int z)
        {
            int i = Index(x, z);
            return (_words[i >> 6] & (1ul << (i & 63))) != 0;
        }

        public void Set(int x, int z, bool value)
        {
            int i = Index(x, z);
            ulong mask = 1ul << (i & 63);
            if (value) _words[i >> 6] |= mask;
            else _words[i >> 6] &= ~mask;
        }

        public int CountSet()
        {
            int count = 0;
            foreach (ulong w in _words)
            {
                ulong v = w;
                while (v != 0)
                {
                    v &= v - 1;
                    count++;
                }
            }
            return count;
        }

        /// <summary>Counts cells that are set here AND in the mask (e.g. countable cells, §8.1).</summary>
        public int CountSetInMask(BitGrid mask)
        {
            if (mask.Width != Width || mask.Height != Height)
                throw new ArgumentException("Mask dimensions must match.");
            int count = 0;
            for (int w = 0; w < _words.Length; w++)
            {
                ulong v = _words[w] & mask._words[w];
                while (v != 0)
                {
                    v &= v - 1;
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Sets all cells whose center is within radiusMeters of (cx, cz), where one cell is
        /// cellSizeMeters wide. Optional predicate filters occlusion (§8.1 ridge filtering).
        /// Returns the number of cells newly set.
        /// </summary>
        public int RevealCircle(float cx, float cz, float radiusMeters, float cellSizeMeters,
            Func<int, int, bool> cellAllowed = null)
        {
            int newly = 0;
            int minX = Math.Max(0, (int)((cx - radiusMeters) / cellSizeMeters));
            int maxX = Math.Min(Width - 1, (int)((cx + radiusMeters) / cellSizeMeters));
            int minZ = Math.Max(0, (int)((cz - radiusMeters) / cellSizeMeters));
            int maxZ = Math.Min(Height - 1, (int)((cz + radiusMeters) / cellSizeMeters));
            float r2 = radiusMeters * radiusMeters;
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float px = (x + 0.5f) * cellSizeMeters;
                    float pz = (z + 0.5f) * cellSizeMeters;
                    float dx = px - cx, dz = pz - cz;
                    if (dx * dx + dz * dz > r2) continue;
                    if (cellAllowed != null && !cellAllowed(x, z)) continue;
                    if (!Get(x, z))
                    {
                        Set(x, z, true);
                        newly++;
                    }
                }
            }
            return newly;
        }

        public string ToBase64()
        {
            byte[] bytes = new byte[_words.Length * 8];
            Buffer.BlockCopy(_words, 0, bytes, 0, bytes.Length);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Parses a saved grid. Never throws: a corrupt or short payload is reported through
        /// <paramref name="error"/> and whatever prefix was readable is kept, so a save made by an
        /// older build still loads instead of throwing out of the load path.
        /// <para>
        /// Until 2026-10-19 <c>FromBase64</c> silently truncated a short payload (leaving the tail of
        /// the grid as zeros — a partially revealed map became "not revealed") and let
        /// <c>Convert.FromBase64String</c>'s <c>FormatException</c> escape into
        /// <c>SaveService</c>, which caught only IO and access exceptions (DEF-020).
        /// </para>
        /// </summary>
        public static bool TryFromBase64(int width, int height, string base64, out BitGrid grid, out string error)
        {
            grid = new BitGrid(width, height);
            error = null;
            if (string.IsNullOrEmpty(base64)) return true;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                error = "malformed base64 grid payload: " + ex.Message;
                return false;
            }

            int expected = grid._words.Length * 8;
            if (bytes.Length != expected)
            {
                error = $"grid payload is {bytes.Length} byte(s), expected {expected} for {width}x{height}";
                int copy = Math.Min(bytes.Length, expected);
                Buffer.BlockCopy(bytes, 0, grid._words, 0, copy);
                return false;
            }
            Buffer.BlockCopy(bytes, 0, grid._words, 0, expected);
            return true;
        }

        /// <summary>Parses a saved grid, ignoring a malformed/short payload (see <see cref="TryFromBase64"/>).</summary>
        public static BitGrid FromBase64(int width, int height, string base64)
        {
            TryFromBase64(width, height, base64, out var grid, out _);
            return grid;
        }

        /// <summary>Value equality: two grids are equal when they are the same size and every bit matches.</summary>
        public bool Equals(BitGrid other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (Width != other.Width || Height != other.Height) return false;
            for (int i = 0; i < _words.Length; i++)
                if (_words[i] != other._words[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as BitGrid);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Width;
                hash = hash * 31 + Height;
                for (int i = 0; i < _words.Length; i++) hash = hash * 31 + (int)_words[i];
                return hash;
            }
        }

        public BitGrid Clone()
        {
            var g = new BitGrid(Width, Height);
            Array.Copy(_words, g._words, _words.Length);
            return g;
        }

        /// <summary>Overwrites every bit with <paramref name="source"/> (same dimensions). Allocation-free.</summary>
        public void CopyFrom(BitGrid source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Width != Width || source.Height != Height)
                throw new ArgumentException("Grid dimensions must match.");
            Array.Copy(source._words, _words, _words.Length);
        }

        /// <summary>
        /// Appends the row-major index (z * Width + x) of every cell whose bit differs from
        /// <paramref name="other"/> — set here and clear there, or the reverse — and returns how
        /// many were appended. Compares 64 cells per step, so an unchanged 96 × 96 map costs 144
        /// word comparisons; the map view uses this to redraw only cells that changed (§8.1).
        /// </summary>
        public int CollectDifferences(BitGrid other, List<int> cellIndices)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (cellIndices == null) throw new ArgumentNullException(nameof(cellIndices));
            if (other.Width != Width || other.Height != Height)
                throw new ArgumentException("Grid dimensions must match.");
            int added = 0;
            int cells = CellCount;
            for (int w = 0; w < _words.Length; w++)
            {
                ulong diff = _words[w] ^ other._words[w];
                while (diff != 0)
                {
                    int bit = TrailingZeroCount(diff);
                    int index = (w << 6) + bit;
                    if (index < cells)
                    {
                        cellIndices.Add(index);
                        added++;
                    }
                    diff &= diff - 1;
                }
            }
            return added;
        }

        static int TrailingZeroCount(ulong v)
        {
            // .NET Standard 2.1 has no BitOperations; a de Bruijn-free loop is fine at ≤ 64 steps.
            int n = 0;
            while ((v & 1ul) == 0)
            {
                v >>= 1;
                n++;
            }
            return n;
        }

        int Index(int x, int z)
        {
            if (x < 0 || x >= Width || z < 0 || z >= Height)
                throw new ArgumentOutOfRangeException($"cell ({x},{z}) outside {Width}x{Height}");
            return z * Width + x;
        }
    }

    /// <summary>Ordered set of stable reward keys with durable idempotency semantics (§16.3).</summary>
    public sealed class ClaimedRewardLedger
    {
        readonly HashSet<string> _claimed = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> _order = new List<string>();

        public IReadOnlyList<string> Order => _order;
        public int Count => _claimed.Count;

        public bool IsClaimed(string rewardKey) => _claimed.Contains(rewardKey ?? string.Empty);

        /// <summary>Claims a reward key exactly once. Returns false when already claimed.</summary>
        public bool TryClaim(string rewardKey)
        {
            if (string.IsNullOrEmpty(rewardKey))
                throw new ArgumentException("Reward key must be non-empty.", nameof(rewardKey));
            if (!_claimed.Add(rewardKey)) return false;
            _order.Add(rewardKey);
            return true;
        }

        public void Restore(IEnumerable<string> keys)
        {
            _claimed.Clear();
            _order.Clear();
            if (keys == null) return;
            foreach (string k in keys)
            {
                if (!string.IsNullOrEmpty(k) && _claimed.Add(k))
                    _order.Add(k);
            }
        }
    }
}
