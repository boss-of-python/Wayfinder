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
        public const float DecorativeCliffSlopeDegrees = 38f;
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

        // Region relay layout anchors (§6.1).
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
    /// Deterministic continental island heightfield (§6.1, §7.2 steps 2–3, KI-027):
    /// surrounds the 6,144 m mainland with open coastal ocean (world origin (0,0) &lt; 0 m) and steep
    /// decorative headland cliffs (&gt;= 38°), sculpts distinct elevation bands across all five biomes
    /// (Wetlands tidal plain, City river terrace, Forest uplands, incised Red-Rock Canyon plateau, and
    /// Alpine Highlands crowned by Frontier Observatory), and mirrors Tools/Validation/formula_checks.py.
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

        /// <summary>Continental macro height in metres for a world position (§6.1, −20..520 m band).</summary>
        public float BaseHeight(float x, float z)
        {
            ulong s1 = LayerSeed(1);
            ulong s2 = LayerSeed(2);
            ulong s3 = LayerSeed(3);
            ulong s4 = LayerSeed(4);
            ulong s5 = LayerSeed(5);
            ulong s6 = LayerSeed(6);

            float broad = (Fbm(x * 0.00055f, z * 0.00055f, 4, s1) - 0.5f) * 110f;
            float mid = (Ridged(x * 0.0018f, z * 0.0018f, 3, s2) - 0.5f) * 26f;
            float fine = (Fbm(x * 0.0088f, z * 0.0088f, 2, s3) - 0.5f) * 5f;
            float interior = 28f + broad + mid + fine;

            // Five regional geomorphology features
            float highlands = 365f * Gauss2(x, z, 1680f, 5360f, 1120f);
            float canyon = 165f * Gauss2(x, z, 4864f, 4608f, 1220f);
            float gorgeU = (x - 4420f) - 0.45f * (z - 4608f);
            float dzGorge = z - 4560f;
            float canyonCut = -54f
                * (float)Math.Exp(-(gorgeU * gorgeU) / (2.0 * 145.0 * 145.0))
                * (float)Math.Exp(-(dzGorge * dzGorge) / (2.0 * 920.0 * 920.0));
            float forest = 48f * Gauss2(x, z, 1100f, 2400f, 1100f);
            float city = 26f * Gauss2(x, z, 4608f, 2304f, 950f);
            interior += highlands + canyon + canyonCut + forest + city;

            float wetW = 0.72f * Gauss2(x, z, 3328f, 768f, 960f);
            float marshRipple = (Fbm(x * 0.0045f, z * 0.0045f, 2, s4) - 0.5f) * 6f;
            interior = MathUtil.Lerp(interior, 11f + marshRipple, wetW);

            // Organic coastline + steep decorative headland cliffs around outer capes
            const float R = 900f;
            float qx = Math.Max(0f, Math.Max(R - x, x - (WorldConstants.WorldSizeMeters - R)));
            float qz = Math.Max(0f, Math.Max(R - z, z - (WorldConstants.WorldSizeMeters - R)));
            float signedMargin = R - (float)Math.Sqrt(qx * qx + qz * qz);
            float coastWarp = (Fbm(x * 0.00085f, z * 0.00085f, 3, s5) - 0.5f) * 240f;
            float m = signedMargin + coastWarp;

            float headland = SmoothStep(0.52f, 0.70f, Fbm(x * 0.00062f + 11.3f, z * 0.00062f - 4.7f, 3, s6));
            float cliffBench = headland * 62f * SmoothStep(205f, 255f, m) * (1f - 0.35f * SmoothStep(340f, 520f, m));

            float landBlend = SmoothStep(150f, 390f, m);
            float seabed = -16.5f + (Fbm(x * 0.0018f, z * 0.0018f, 2, s4) - 0.5f) * 5f;
            float h = MathUtil.Lerp(seabed, Math.Max(4f, interior) + cliffBench, landBlend);
            return MathUtil.Clamp(h, -19.5f, 515f);
        }

        public float SlopeDegrees(float x, float z, float sampleStep = 8f)
        {
            float x0 = Math.Max(0f, x - sampleStep);
            float x1 = Math.Min(WorldConstants.WorldSizeMeters, x + sampleStep);
            float z0 = Math.Max(0f, z - sampleStep);
            float z1 = Math.Min(WorldConstants.WorldSizeMeters, z + sampleStep);
            float dhdx = (BaseHeight(x1, z) - BaseHeight(x0, z)) / Math.Max(1e-3f, x1 - x0);
            float dhdz = (BaseHeight(x, z1) - BaseHeight(x, z0)) / Math.Max(1e-3f, z1 - z0);
            return (float)(Math.Atan(Math.Sqrt(dhdx * dhdx + dhdz * dhdz)) * (180.0 / Math.PI));
        }

        /// <summary>
        /// Bakes the reachable continental countable mask (§8.1, §8.4, KI-048): 4-connected cells
        /// reachable from Home Garage that sit above sea level and below decorative cliff slope (38°).
        /// </summary>
        public BitGrid BuildCountableMask()
        {
            int n = WorldConstants.MapCellsPerSide;
            float half = WorldConstants.MapCellSizeMeters * 0.5f;
            var passable = new bool[n, n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float wx = x * WorldConstants.MapCellSizeMeters + half;
                float wz = z * WorldConstants.MapCellSizeMeters + half;
                passable[x, z] = BaseHeight(wx, wz) > WorldConstants.SeaLevelY &&
                                 SlopeDegrees(wx, wz, 8f) < WorldConstants.DecorativeCliffSlopeDegrees;
            }

            var mask = new BitGrid(n, n);
            var (sx, sz) = WorldConstants.MapCellOf(WorldConstants.ForestRelayAnchor.x, WorldConstants.ForestRelayAnchor.z);
            if (!passable[sx, sz])
                return mask;

            var q = new Queue<(int x, int z)>();
            mask.Set(sx, sz, true);
            q.Enqueue((sx, sz));
            while (q.Count > 0)
            {
                var (cx, cz) = q.Dequeue();
                TryVisit(cx - 1, cz);
                TryVisit(cx + 1, cz);
                TryVisit(cx, cz - 1);
                TryVisit(cx, cz + 1);
            }
            return mask;

            void TryVisit(int nx, int nz)
            {
                if (nx < 0 || nx >= n || nz < 0 || nz >= n) return;
                if (!passable[nx, nz] || mask.Get(nx, nz)) return;
                mask.Set(nx, nz, true);
                q.Enqueue((nx, nz));
            }
        }

        static float SmoothStep(float e0, float e1, float v)
        {
            float t = MathUtil.Clamp01((v - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static float Gauss2(float x, float z, float cx, float cz, float sigma)
        {
            float dx = x - cx, dz = z - cz;
            return (float)Math.Exp(-(dx * dx + dz * dz) / (2.0 * sigma * sigma));
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
        float[] _samples;
        readonly int _gridSide;

        public HeightField(NoiseField noise = null)
        {
            _noise = noise ?? new NoiseField();
            _gridSide = WorldConstants.WorldSizeMeters / (int)WorldConstants.TerrainSampleSpacingMeters + 1;
            _samples = null;
        }

        public int GridSide => _gridSide;
        public bool IsBaked => _samples != null;

        public void Bake()
        {
            if (_samples == null) _samples = new float[_gridSide * _gridSide];
            for (int z = 0; z < _gridSide; z++)
            for (int x = 0; x < _gridSide; x++)
            {
                float wx = x * WorldConstants.TerrainSampleSpacingMeters;
                float wz = z * WorldConstants.TerrainSampleSpacingMeters;
                _samples[z * _gridSide + x] = Quantize(_noise.BaseHeight(wx, wz));
            }
        }

        public SeamCheck.TerrainTile BakeTile(int chunkX, int chunkZ)
        {
            int res = WorldConstants.TerrainTileResolution;
            float spacing = WorldConstants.TerrainSampleSpacingMeters;
            float originX = chunkX * WorldConstants.ChunkSizeMeters;
            float originZ = chunkZ * WorldConstants.ChunkSizeMeters;
            var tile = new SeamCheck.TerrainTile(res);
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                tile[x, z] = Quantize(Sample(originX + x * spacing, originZ + z * spacing));
            return tile;
        }

        public float Sample(float x, float z)
        {
            if (_samples == null)
                throw new InvalidOperationException("HeightField.Bake() must be called before sampling.");
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

        public static float Quantize(float heightMeters) =>
            (float)(Math.Round(heightMeters * 1000.0) / 1000.0);
    }

    /// <summary>Seam validation (§7.4): neighboring tiles must agree along shared borders within 1 mm.</summary>
    public static class SeamCheck
    {
        public const float MaxAllowedDifferenceMeters = 0.001f;

        public sealed class TerrainTile
        {
            public readonly int Resolution;
            public readonly float[] Samples;
            public TerrainTile(int resolution) { Resolution = resolution; Samples = new float[resolution * resolution]; }
            public float this[int x, int z] { get => Samples[z * Resolution + x]; set => Samples[z * Resolution + x] = value; }
        }

        public static bool BordersMatch(HeightField field, int chunkX, int chunkZ, out float maxDifference)
        {
            maxDifference = 0f;
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (chunkX < 0 || chunkZ < 0 ||
                chunkX >= WorldConstants.ChunksPerSide || chunkZ >= WorldConstants.ChunksPerSide)
                return false;

            bool hasEast = chunkX + 1 < WorldConstants.ChunksPerSide;
            bool hasNorth = chunkZ + 1 < WorldConstants.ChunksPerSide;
            if (!hasEast && !hasNorth) return true;

            var tile = field.BakeTile(chunkX, chunkZ);
            bool ok = true;
            float difference;

            if (hasEast)
            {
                ok &= BordersMatch(tile, field.BakeTile(chunkX + 1, chunkZ), Edge.East, out difference);
                maxDifference = Math.Max(maxDifference, difference);
            }
            if (hasNorth)
            {
                ok &= BordersMatch(tile, field.BakeTile(chunkX, chunkZ + 1), Edge.North, out difference);
                maxDifference = Math.Max(maxDifference, difference);
            }
            return ok;
        }

        public enum Edge { East, North }

        public static bool BordersMatch(TerrainTile tile, TerrainTile neighbour, Edge edge, out float maxDifference)
        {
            maxDifference = 0f;
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            if (neighbour == null) throw new ArgumentNullException(nameof(neighbour));
            if (tile.Resolution != neighbour.Resolution)
                throw new ArgumentException("tiles of different resolutions cannot share a border");

            int res = tile.Resolution;
            if (edge == Edge.East)
            {
                for (int z = 0; z < res; z++)
                    maxDifference = Math.Max(maxDifference, Math.Abs(tile[res - 1, z] - neighbour[0, z]));
            }
            else
            {
                for (int x = 0; x < res; x++)
                    maxDifference = Math.Max(maxDifference, Math.Abs(tile[x, res - 1] - neighbour[x, 0]));
            }
            return maxDifference <= MaxAllowedDifferenceMeters;
        }
    }

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

        public bool TryTransition(ChunkState target, out string error) => TryTransition(target, null, out error);

        public bool TryTransition(ChunkState target, string detail, out string error)
        {
            error = string.Empty;
            if (!IsLegal(State, target))
            {
                error = $"illegal chunk transition {State} → {target}";
                return false;
            }
            State = target;
            StateVersion++;
            FailureDetail = target == ChunkState.Failed
                ? (string.IsNullOrEmpty(detail) ? "failed (no detail supplied)" : detail)
                : string.Empty;
            return true;
        }

        public static bool IsLegal(ChunkState from, ChunkState to)
        {
            switch (from)
            {
                case ChunkState.Unloaded:
                    return to == ChunkState.Requested || to == ChunkState.Failed;
                case ChunkState.Requested:
                    return to == ChunkState.DataReady || to == ChunkState.Unloaded || to == ChunkState.Failed;
                case ChunkState.DataReady:
                    return to == ChunkState.Activating || to == ChunkState.Unloaded || to == ChunkState.Failed;
                case ChunkState.Activating:
                    return to == ChunkState.Active || to == ChunkState.Failed;
                case ChunkState.Active:
                    return to == ChunkState.Deactivating || to == ChunkState.Failed;
                case ChunkState.Deactivating:
                    return to == ChunkState.Unloaded || to == ChunkState.DataReady || to == ChunkState.Failed;
                case ChunkState.Failed:
                    return to == ChunkState.Unloaded;
                default:
                    return false;
            }
        }

        public static bool ShouldLoad(float distanceMeters, float loadRadius) => distanceMeters <= loadRadius;
        public static bool ShouldUnload(float distanceMeters, float loadRadius) => distanceMeters > loadRadius * 1.25f;
    }
}
