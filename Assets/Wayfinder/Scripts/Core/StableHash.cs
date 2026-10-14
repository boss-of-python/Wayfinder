using System;

namespace Wayfinder.Core
{
    /// <summary>
    /// Documented stable integer hash for persistent generation (Blueprint §7.3).
    /// FNV-1a (32- and 64-bit) over UTF-8 bytes. NEVER use runtime GetHashCode()
    /// for generation or persistence — its algorithm is not stable across versions/platforms.
    /// </summary>
    public static class StableHash
    {
        public const uint FnvOffset32 = 2166136261u;
        public const uint FnvPrime32 = 16777619u;
        public const ulong FnvOffset64 = 14695981039346656037ul;
        public const ulong FnvPrime64 = 1099511628211ul;

        public static uint Hash32(string text)
        {
            if (text == null) return 0;
            unchecked
            {
                uint hash = FnvOffset32;
                foreach (char c in text)
                {
                    // UTF-8 encoding without allocating.
                    if (c < 0x80)
                    {
                        hash = (hash ^ c) * FnvPrime32;
                    }
                    else if (c < 0x800)
                    {
                        hash = (hash ^ (byte)(0xC0 | (c >> 6))) * FnvPrime32;
                        hash = (hash ^ (byte)(0x80 | (c & 0x3F))) * FnvPrime32;
                    }
                    else
                    {
                        hash = (hash ^ (byte)(0xE0 | (c >> 12))) * FnvPrime32;
                        hash = (hash ^ (byte)(0x80 | ((c >> 6) & 0x3F))) * FnvPrime32;
                        hash = (hash ^ (byte)(0x80 | (c & 0x3F))) * FnvPrime32;
                    }
                }
                return hash;
            }
        }

        public static ulong Hash64(string text)
        {
            if (text == null) return 0;
            unchecked
            {
                ulong hash = FnvOffset64;
                foreach (char c in text)
                {
                    if (c < 0x80)
                    {
                        hash = (hash ^ c) * FnvPrime64;
                    }
                    else if (c < 0x800)
                    {
                        hash = (hash ^ (byte)(0xC0 | (c >> 6))) * FnvPrime64;
                        hash = (hash ^ (byte)(0x80 | (c & 0x3F))) * FnvPrime64;
                    }
                    else
                    {
                        hash = (hash ^ (byte)(0xE0 | (c >> 12))) * FnvPrime64;
                        hash = (hash ^ (byte)(0x80 | ((c >> 6) & 0x3F))) * FnvPrime64;
                        hash = (hash ^ (byte)(0x80 | (c & 0x3F))) * FnvPrime64;
                    }
                }
                return hash;
            }
        }

        public static uint Hash32(ReadOnlySpan<byte> bytes)
        {
            unchecked
            {
                uint hash = FnvOffset32;
                for (int i = 0; i < bytes.Length; i++)
                    hash = (hash ^ bytes[i]) * FnvPrime32;
                return hash;
            }
        }

        /// <summary>
        /// chunkSeed = StableHash(worldSeed, generatorVersion, chunkX, chunkZ, layerId) (§7.3).
        /// Combination is order-stable and collision-resistant enough for seed derivation.
        /// </summary>
        public static ulong ChunkSeed(int worldSeed, int generatorVersion, int chunkX, int chunkZ, int layerId)
        {
            unchecked
            {
                ulong h = FnvOffset64;
                h = Mix(h, (ulong)(uint)worldSeed);
                h = Mix(h, (ulong)(uint)generatorVersion);
                h = Mix(h, (ulong)(uint)chunkX);
                h = Mix(h, (ulong)(uint)chunkZ);
                h = Mix(h, (ulong)(uint)layerId);
                return h;
            }
        }

        /// <summary>Stable object seed from chunk seed + stable candidate index (§7.3).</summary>
        public static ulong CandidateSeed(ulong chunkSeed, int candidateIndex)
        {
            unchecked
            {
                ulong h = chunkSeed ^ FnvOffset64;
                h = Mix(h, (ulong)(uint)candidateIndex);
                return h;
            }
        }

        static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                hash = (hash ^ value) * FnvPrime64;
                // Extra avalanche so low-integer fields do not produce trivially related seeds.
                hash ^= hash >> 33;
                hash *= FnvPrime64;
                return hash;
            }
        }
    }
}
