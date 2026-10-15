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

        public static BitGrid FromBase64(int width, int height, string base64)
        {
            var grid = new BitGrid(width, height);
            if (string.IsNullOrEmpty(base64)) return grid;
            byte[] bytes = Convert.FromBase64String(base64);
            int copy = Math.Min(bytes.Length, grid._words.Length * 8);
            Buffer.BlockCopy(bytes, 0, grid._words, 0, copy);
            return grid;
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
