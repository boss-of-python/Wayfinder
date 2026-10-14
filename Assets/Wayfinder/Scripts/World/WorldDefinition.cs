using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.World
{
    /// <summary>
    /// Fixed world constants (§6.1, §7.1, §8.1). All values are hard spec values, not tuning.
    /// </summary>
    public static class WorldConstants
    {
        public const int WorldSizeMeters = 6144;
        public const int ChunkSizeMeters = 256;
        public const int ChunksPerSide = WorldSizeMeters / ChunkSizeMeters; // 24
        public const int TerrainTileResolution = 129;                       // 129×129 samples, 2 m spacing
        public const float TerrainSampleSpacingMeters = 2f;
        public const int MapCellsPerSide = 96;
        public const float MapCellSizeMeters = 64f;                         // 96 × 64 = 6144
        public const int CanonicalWorldSeed = 481516;
        public const int GeneratorVersion = 1;
        public const float SeaLevelY = 0f;
        public const float TerrainMinY = -20f;
        public const float TerrainMaxY = 520f;

        // Driving reveal 96 m; vista survey reveal 384 m (§8.1).
        public const float DriveRevealRadiusMeters = 96f;
        public const float VistaRevealRadiusMeters = 384f;

        // Streaming (§18.3): active ring 3×3 chunks; colliders 180–250 m; far proxies ~1.5 km.
        public const int ActiveRingChunks = 3;
        public const float DetailColliderRadiusMeters = 250f;
        public const float FarProxyRadiusMeters = 1500f;
        public const float PrefetchLeadMeters = 200f;

        // Region relay layout anchors (§6.1) — may move during route validation; IDs must not change.
        public static readonly (float x, float z) HomeGarageAnchor = (1024f, 2304f);
        public static readonly (float x, float z) ForestRelayAnchor = (1408f, 2816f);
        public static readonly (float x, float z) CanyonRelayAnchor = (4864f, 4608f);
        public static readonly (float x, float z) CityRelayAnchor = (4608f, 2304f);
        public static readonly (float x, float z) WetlandRelayAnchor = (3328f, 768f);
        public static readonly (float x, float z) HighlandRelayAnchor = (1792f, 5120f);

        public static bool InBounds(float x, float z) =>
            x >= 0 && x < WorldSizeMeters && z >= 0 && z < WorldSizeMeters;

        public static int ChunkCoordFromWorld(float meters) =>
            (int)Math.Floor(meters / ChunkSizeMeters);

        public static (int chunkX, int chunkZ) ChunkOf(float x, float z) =>
            (ChunkCoordFromWorld(x), ChunkCoordFromWorld(z));

        public static (int cellX, int cellZ) MapCellOf(float x, float z) =>
            ((int)(x / MapCellSizeMeters), (int)(z / MapCellSizeMeters));
    }

    /// <summary>
    /// Deterministic macro heightfield (§7.2 steps 2–3): low-frequency ridged + fractal noise
    /// in world coordinates, blended by biome shaping, plus authored river/road carve hooks.
    /// Pure C# — the editor baker meshes this; runtime reads baked data.
    /// </summary>
    public sealed class NoiseField
    {
        readonly int _worldSeed;
        readonly int _generatorVersion;

        public NoiseField(int worldSeed = WorldConstants.CanonicalWorldSeed,
            int generatorVersion = WorldConstants.GeneratorVersion)
        {
            _worldSeed = worldSeed;
            _generatorVersion = generatorVersion;
        }

        public ulong LayerSeed(int layerId) =>
            StableHash.ChunkSeed(_worldSeed, _generatorVersion, 0, 0, layerId);

        /// <summary>Base macro height in meters for a world position (before road/POI carving).</summary>
        public float BaseHeight(float x, float z)
        {
            float continental = Fbm(x * 0.0006f, z * 0.0006f, 4, LayerSeed(1));
            float ridged = Ridged(x * 0.0018f, z * 0.0018f, 3, LayerSeed(2));
            float detail = Fbm(x * 0.012f, z * 0.012f, 2, LayerSeed(3));

            // Continental 0..1 → −20..520 m band (§6.1).
            float macro = MathUtil.Lerp(WorldConstants.TerrainMinY, WorldConstants.TerrainMaxY,
                MathUtil.Clamp01(continental * 0.65f + ridged * 0.35f));
            return macro + detail * 6f;
        }

        /// <summary>Deterministic value-noise layer. Same inputs always produce the same height.</summary>
        public static float ValueNoise(float x, float z, ulong seed)
        {
            int x0 = (int)Math.Floor(x);
            int z0 = (int)Math.Floor(z);
            float fx = x - x0;
            float fz = z - z0;
            float sx = fx * fx * (3f - 2f * fx);
            float sz = fz * fz * (3f - 2f * fz);

            float n00 = StableRandom.Hash01(seed, x0, z0);
            float n10 = StableRandom.Hash01(seed, x0 + 1, z0);
            float n01 = StableRandom.Hash01(seed, x0, z0 + 1);
            float n11 = StableRandom.Hash01(seed, x0 + 1, z0 + 1);

            float ix0 = n00 + (n10 - n00) * sx;
            float ix1 = n01 + (n11 - n01) * sx;
            return ix0 + (ix1 - ix0) * sz;
        }

        public static float Fbm(float x, float z, int octaves, ulong seed)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(x * freq, z * freq, StableHash.CandidateSeed(seed, i)) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum / Math.Max(1e-5f, norm);
        }

        /// <summary>Ridged multifractal: sharp crests for highland silhouettes (§7.2).</summary>
        public static float Ridged(float x, float z, int octaves, ulong seed)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = ValueNoise(x * freq, z * freq, StableHash.CandidateSeed(seed, i)) * 2f - 1f;
                float ridge = 1f - Math.Abs(n);
                sum += ridge * ridge * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum / Math.Max(1e-5f, norm);
        }
    }

    /// <summary>
    /// Baked heightfield accessor with shared seam samples (§7.4). Neighboring tiles read border
    /// samples from this common world-coordinate function so seams quantize to equality.
    /// </summary>
    public sealed class HeightField
    {
        readonly NoiseField _noise;
        readonly float[] _samples; // (TileResolution × ChunksPerSide + 1)² grid over the world
        readonly int _gridSide;

        public HeightField(NoiseField noise = null)
        {
            _noise = noise ?? new NoiseField();
            // World grid: 2 m spacing over 6144 m → 3072 samples + 1 border = 3073 per side.
            _gridSide = WorldConstants.WorldSizeMeters / (int)WorldConstants.TerrainSampleSpacingMeters + 1;
            _samples = new float[_gridSide * _gridSide];
        }

        public int GridSide => _gridSide;

        /// <summary>Bakes the macro heightfield (editor pipeline; deterministic).</summary>
        public void Bake()
        {
            for (int z = 0; z < _gridSide; z++)
            for (int x = 0; x < _gridSide; x++)
            {
                float wx = x * WorldConstants.TerrainSampleSpacingMeters;
                float wz = z * WorldConstants.TerrainSampleSpacingMeters;
                _samples[z * _gridSide + x] = Quantize(_noise.BaseHeight(wx, wz));
            }
        }

        public float Sample(float x, float z)
        {
            float gx = MathUtil.Clamp(x / WorldConstants.TerrainSampleSpacingMeters, 0f, _gridSide - 1.001f);
            float gz = MathUtil.Clamp(z / WorldConstants.TerrainSampleSpacingMeters, 0f, _gridSide - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            float h00 = _samples[z0 * _gridSide + x0];
            float h10 = _samples[z0 * _gridSide + x0 + 1];
            float h01 = _samples[(z0 + 1) * _gridSide + x0];
            float h11 = _samples[(z0 + 1) * _gridSide + x0 + 1];
            return Quantize((h00 * (1 - fx) + h10 * fx) * (1 - fz) + (h01 * (1 - fx) + h11 * fx) * fz);
        }

        /// <summary>Quantization used for seam equality: 1 mm grid (§7.4).</summary>
        public static float Quantize(float heightMeters) =>
            (float)(Math.Round(heightMeters * 1000.0) / 1000.0);
    }

    /// <summary>
    /// Seam validation (§7.4): neighboring tiles must agree along shared borders within 1 mm
    /// after quantization. QA evidence for GEN-02.
    /// </summary>
    public static class SeamCheck
    {
        public const float MaxAllowedDifferenceMeters = 0.001f;

        public static bool BordersMatch(HeightField field, int chunkX, int chunkZ, out float maxDifference)
        {
            maxDifference = 0f;
            if (chunkX < 0 || chunkZ < 0 ||
                chunkX >= WorldConstants.ChunksPerSide - 0 || chunkZ >= WorldConstants.ChunksPerSide - 0)
                return true;

            float originX = chunkX * WorldConstants.ChunkSizeMeters;
            float originZ = chunkZ * WorldConstants.ChunkSizeMeters;
            float endX = originX + WorldConstants.ChunkSizeMeters;
            float endZ = originZ + WorldConstants.ChunkSizeMeters;

            // Shared east border with the neighbor chunk.
            for (int i = 0; i < WorldConstants.TerrainTileResolution; i++)
            {
                float z = originZ + i * WorldConstants.TerrainSampleSpacingMeters;
                float a = HeightField.Quantize(field.Sample(endX - 0.001f, z));
                float b = HeightField.Quantize(field.Sample(endX + 0.001f, z));
                maxDifference = Math.Max(maxDifference, Math.Abs(a - b));
            }
            // Shared north border.
            for (int i = 0; i < WorldConstants.TerrainTileResolution; i++)
            {
                float x = originX + i * WorldConstants.TerrainSampleSpacingMeters;
                float a = HeightField.Quantize(field.Sample(x, endZ - 0.001f));
                float b = HeightField.Quantize(field.Sample(x, endZ + 0.001f));
                maxDifference = Math.Max(maxDifference, Math.Abs(a - b));
            }
            return maxDifference <= MaxAllowedDifferenceMeters;
        }
    }

    /// <summary>
    /// Chunk streaming state machine (§18.3):
    /// Unloaded → Requested → DataReady → Activating → Active → Deactivating → Unloaded,
    /// with Failed and cancellation. Pure state logic; WorldStreamer drives it.
    /// </summary>
    public enum ChunkState
    {
        Unloaded,
        Requested,
        DataReady,
        Activating,
        Active,
        Deactivating,
        Failed
    }

    public sealed class ChunkStateMachine
    {
        public ChunkState State { get; private set; } = ChunkState.Unloaded;
        public int StateVersion { get; private set; }
        public string FailureDetail { get; private set; } = string.Empty;

        public bool TryTransition(ChunkState target, out string error)
        {
            error = string.Empty;
            if (!IsLegal(State, target))
            {
                error = $"illegal chunk transition {State} → {target}";
                return false;
            }
            State = target;
            StateVersion++;
            if (target == ChunkState.Failed) FailureDetail = error;
            return true;
        }

        public static bool IsLegal(ChunkState from, ChunkState to)
        {
            switch (from)
            {
                case ChunkState.Unloaded:
                    return to == ChunkState.Requested || to == ChunkState.Failed;
                case ChunkState.Requested:
                    return to == ChunkState.DataReady || to == ChunkState.Unloaded ||
                           to == ChunkState.Failed; // cancellation path
                case ChunkState.DataReady:
                    return to == ChunkState.Activating || to == ChunkState.Unloaded || to == ChunkState.Failed;
                case ChunkState.Activating:
                    return to == ChunkState.Active || to == ChunkState.Failed;
                case ChunkState.Active:
                    return to == ChunkState.Deactivating || to == ChunkState.Failed;
                case ChunkState.Deactivating:
                    return to == ChunkState.Unloaded || to == ChunkState.Failed;
                case ChunkState.Failed:
                    return to == ChunkState.Unloaded; // retry from scratch
                default:
                    return false;
            }
        }

        /// <summary>Load/unload hysteresis (§18.3): unload farther than load threshold.</summary>
        public static bool ShouldLoad(float distanceMeters, float loadRadius) => distanceMeters <= loadRadius;
        public static bool ShouldUnload(float distanceMeters, float loadRadius) => distanceMeters > loadRadius * 1.25f;
    }
}
