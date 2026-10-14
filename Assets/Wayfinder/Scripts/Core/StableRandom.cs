using System;

namespace Wayfinder.Core
{
    /// <summary>
    /// Deterministic PRNG (PCG-XSH-RR 64/32) for persistent generation (§7.3).
    /// Independent instances per chunk/layer; never use System.Random or UnityEngine.Random
    /// for authored placement. Adding one tree must not renumber all caches:
    /// always seed from StableHash.CandidateSeed(chunkSeed, stableCandidateIndex).
    /// </summary>
    public struct StableRandom
    {
        ulong _state;
        readonly ulong _increment;

        public StableRandom(ulong seed, ulong sequence = 1442695040888963407ul)
        {
            _state = 0;
            _increment = (sequence << 1) | 1ul;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        /// <summary>PCG-XSH-RR 32-bit output.</summary>
        public uint NextUInt()
        {
            ulong oldState = _state;
            _state = oldState * 6364136223846793005ul + _increment;
            uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rot = (int)(oldState >> 59);
            return (xorshifted >> rot) | (xorshifted << ((32 - rot) & 31));
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (float)NextDouble();

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint range = (uint)(maxExclusive - minInclusive);
            // Rejection-free Lemire-style multiply-shift (slight modulo bias is acceptable for dressing,
            // but we use 64-bit multiply for even the low-entropy cases).
            ulong product = (ulong)NextUInt() * range;
            return minInclusive + (int)(product >> 32);
        }

        /// <summary>True with probability p (0..1).</summary>
        public bool Chance(double p) => NextDouble() < p;

        /// <summary>Value-noise helper: deterministic hash-based gradient [0,1) from integer coords.</summary>
        public static float Hash01(ulong seed, int x, int y)
        {
            ulong h = StableHash.CandidateSeed(seed, x * 73856093 ^ y * 19349663);
            uint r = (uint)(h ^ (h >> 32));
            return (r & 0xFFFFFF) / 16777216f;
        }
    }
}
