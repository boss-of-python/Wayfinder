using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.World
{
    /// <summary>
    /// Read-only surface queries the scatter placer needs. The editor baker implements this from the
    /// baked heightfield, road corridors and POI reservations; tests implement it with analytic shapes.
    /// Pure C#: no UnityEngine types.
    /// </summary>
    public interface IScatterSurface
    {
        /// <summary>Ground height in meters (sea level = WorldConstants.SeaLevelY).</summary>
        float Height(float x, float z);

        /// <summary>Slope of the ground under (x, z) in degrees.</summary>
        float SlopeDegrees(float x, float z);

        /// <summary>
        /// Horizontal distance from (x, z) to the nearest road edge in meters; 0 or negative inside the
        /// road corridor. Return a large value where no road is nearby.
        /// </summary>
        float DistanceToRoadEdgeMeters(float x, float z);

        /// <summary>True inside reserved footprints: POI pads, camp pads, spawn pads, cache/landmark sockets.</summary>
        bool IsReserved(float x, float z);
    }

    /// <summary>
    /// Placement rules for one scatter layer (one species / prop family). Values follow §7.6.
    /// </summary>
    public sealed class ScatterRules
    {
        public string SpeciesId { get; }

        /// <summary>
        /// Stable numeric layer id used for seeding (§7.3). Changing it reshuffles this layer only.
        /// </summary>
        public int LayerId { get; }

        /// <summary>Poisson-style minimum spacing between accepted instances (§7.6: trees 3–8 m).</summary>
        public float MinSpacingMeters { get; }

        /// <summary>Probability that an otherwise eligible candidate is kept (density zone).</summary>
        public float Density { get; }

        public float MaxSlopeDegrees { get; }
        public float MinHeightMeters { get; }
        public float MaxHeightMeters { get; }

        /// <summary>Required clearance from the road edge (trees: road corridor + 1 m shoulder).</summary>
        public float RoadClearanceMeters { get; }

        public float MinScale { get; }
        public float MaxScale { get; }

        public ScatterRules(string speciesId, int layerId, float minSpacingMeters, float density,
            float maxSlopeDegrees, float minHeightMeters, float maxHeightMeters,
            float roadClearanceMeters, float minScale = 0.85f, float maxScale = 1.15f)
        {
            if (string.IsNullOrEmpty(speciesId)) throw new ArgumentException("speciesId required", nameof(speciesId));
            if (minSpacingMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(minSpacingMeters));
            if (density < 0f || density > 1f) throw new ArgumentOutOfRangeException(nameof(density));
            if (maxHeightMeters < minHeightMeters) throw new ArgumentException("max < min height");
            if (minScale <= 0f || maxScale < minScale) throw new ArgumentException("invalid scale range");
            if (WorldConstants.ChunkSizeMeters / minSpacingMeters < 1f)
                throw new ArgumentOutOfRangeException(nameof(minSpacingMeters), "spacing larger than a chunk");

            SpeciesId = speciesId;
            LayerId = layerId;
            MinSpacingMeters = minSpacingMeters;
            Density = density;
            MaxSlopeDegrees = maxSlopeDegrees;
            MinHeightMeters = minHeightMeters;
            MaxHeightMeters = maxHeightMeters;
            RoadClearanceMeters = roadClearanceMeters;
            MinScale = minScale;
            MaxScale = maxScale;
        }

        /// <summary>Tree preset: 3–8 m spacing band (caller picks the value), 1 m road shoulder (§7.6).</summary>
        public static ScatterRules Trees(string speciesId, int layerId, float spacingMeters, float density) =>
            new ScatterRules(speciesId, layerId, spacingMeters, density,
                maxSlopeDegrees: 32f, minHeightMeters: WorldConstants.SeaLevelY + 0.5f,
                maxHeightMeters: WorldConstants.TerrainMaxY, roadClearanceMeters: 1f);
    }

    /// <summary>One accepted scatter instance. Identified by its stable candidate index (§7.3).</summary>
    public readonly struct ScatterPoint
    {
        public ScatterPoint(int candidateIndex, int chunkX, int chunkZ, float x, float y, float z,
            float yawDegrees, float scale)
        {
            CandidateIndex = candidateIndex;
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            X = x;
            Y = y;
            Z = z;
            YawDegrees = yawDegrees;
            Scale = scale;
        }

        public int CandidateIndex { get; }
        public int ChunkX { get; }
        public int ChunkZ { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float YawDegrees { get; }
        public float Scale { get; }
    }

    /// <summary>
    /// Deterministic scatter placement (Blueprint §7.2 step 8, §7.3, §7.6).
    ///
    /// Design:
    ///  * The world is tiled by a candidate grid whose cells tile each 256 m chunk exactly.
    ///    Cell size is at least the layer's minimum spacing, so a jittered candidate is confined to its cell
    ///    and every candidate is owned by exactly one chunk.
    ///  * Each candidate's randomness comes from StableHash.CandidateSeed(chunkSeed(owner), globalIndex).
    ///    Its position, eligibility and priority therefore depend only on its global index, never on which
    ///    chunks happen to be requested. Neighbouring chunks agree on their shared border.
    ///  * Acceptance is a priority rule: a candidate is kept when it is eligible and no eligible candidate
    ///    within MinSpacing has higher (priority, index). Two accepted points can never be closer than
    ///    MinSpacing, and the result is independent of iteration order.
    /// </summary>
    public static class ScatterPlacer
    {
        /// <summary>Neighbour ring (in cells) searched for conflicts. Cell size ≥ spacing makes ±1 sufficient; ±2 is a float-safety margin.</summary>
        public const int ConflictRingCells = 2;

        /// <summary>Candidate cells along one chunk side for the given spacing: floor(256 / spacing), at least 1.</summary>
        public static int CellsPerChunk(float minSpacingMeters) =>
            Math.Max(1, (int)Math.Floor(WorldConstants.ChunkSizeMeters / minSpacingMeters));

        /// <summary>Exact cell size for the spacing: 256 / CellsPerChunk (always ≥ spacing).</summary>
        public static float CellSizeMeters(float minSpacingMeters) =>
            (float)WorldConstants.ChunkSizeMeters / CellsPerChunk(minSpacingMeters); // float: integer division would truncate

        /// <summary>Global candidate index for a global cell (row-major over the whole world).</summary>
        public static int CandidateIndex(int globalCellX, int globalCellZ, float minSpacingMeters)
        {
            int perSide = WorldConstants.ChunksPerSide * CellsPerChunk(minSpacingMeters);
            return globalCellZ * perSide + globalCellX;
        }

        /// <summary>Places all accepted instances whose candidate cells are owned by chunk (chunkX, chunkZ).</summary>
        public static List<ScatterPoint> PlaceChunk(int chunkX, int chunkZ, ScatterRules rules,
            IScatterSurface surface, int worldSeed = WorldConstants.CanonicalWorldSeed,
            int generatorVersion = WorldConstants.GeneratorVersion)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            var result = new List<ScatterPoint>();
            if (chunkX < 0 || chunkZ < 0 || chunkX >= WorldConstants.ChunksPerSide || chunkZ >= WorldConstants.ChunksPerSide)
                return result;

            int n = CellsPerChunk(rules.MinSpacingMeters);
            float cell = CellSizeMeters(rules.MinSpacingMeters);
            int halo = ConflictRingCells;
            int side = n + 2 * halo;

            // Evaluate eligibility once for the owned cells plus a halo of neighbouring cells (the halo can
            // belong to neighbouring chunks; their candidates are computed exactly as those chunks would).
            var cands = new Candidate[side * side];
            for (int ly = -halo; ly < n + halo; ly++)
            for (int lx = -halo; lx < n + halo; lx++)
            {
                int gx = chunkX * n + lx;
                int gz = chunkZ * n + ly;
                cands[(ly + halo) * side + (lx + halo)] =
                    EvaluateCandidate(gx, gz, n, cell, rules, surface, worldSeed, generatorVersion);
            }

            for (int ly = 0; ly < n; ly++)
            for (int lx = 0; lx < n; lx++)
            {
                var self = cands[(ly + halo) * side + (lx + halo)];
                if (!self.Eligible) continue;
                if (IsBlocked(self, cands, side, lx + halo, ly + halo, rules.MinSpacingMeters)) continue;
                result.Add(new ScatterPoint(self.Index, chunkX, chunkZ, self.X, self.Y, self.Z,
                    self.YawDegrees, self.Scale));
            }
            return result;
        }

        /// <summary>
        /// Evaluates one global candidate cell (no chunk-dependent state). Also used by tests to check
        /// cross-chunk spacing.
        /// </summary>
        public static bool TryEvaluateCandidate(int globalCellX, int globalCellZ, ScatterRules rules,
            IScatterSurface surface, out ScatterPoint point, int worldSeed = WorldConstants.CanonicalWorldSeed,
            int generatorVersion = WorldConstants.GeneratorVersion)
        {
            int n = CellsPerChunk(rules.MinSpacingMeters);
            var c = EvaluateCandidate(globalCellX, globalCellZ, n, CellSizeMeters(rules.MinSpacingMeters),
                rules, surface, worldSeed, generatorVersion);
            point = new ScatterPoint(c.Index, c.OwnerChunkX, c.OwnerChunkZ, c.X, c.Y, c.Z, c.YawDegrees, c.Scale);
            return c.Eligible;
        }

        struct Candidate
        {
            public int Index;
            public int OwnerChunkX;
            public int OwnerChunkZ;
            public float X, Y, Z;
            public float YawDegrees;
            public float Scale;
            public ulong Priority;
            public bool Eligible;
        }

        static Candidate EvaluateCandidate(int gx, int gz, int cellsPerChunk, float cell, ScatterRules rules,
            IScatterSurface surface, int worldSeed, int generatorVersion)
        {
            var c = new Candidate { Index = -1 };
            int perSide = WorldConstants.ChunksPerSide * cellsPerChunk;
            if (gx < 0 || gz < 0 || gx >= perSide || gz >= perSide) return c;

            // Owner chunk is the chunk containing the cell (cells never straddle chunks).
            int ownerX = gx / cellsPerChunk;
            int ownerZ = gz / cellsPerChunk;
            c.Index = gz * perSide + gx;
            c.OwnerChunkX = ownerX;
            c.OwnerChunkZ = ownerZ;

            ulong chunkSeed = StableHash.ChunkSeed(worldSeed, generatorVersion, ownerX, ownerZ, rules.LayerId);
            ulong seed = StableHash.CandidateSeed(chunkSeed, c.Index);
            var rng = new StableRandom(seed);

            // Fixed draw order keeps each stream stable when rules change (§7.3).
            bool densityKeep = rng.Chance(rules.Density);
            float jx = rng.NextFloat();
            float jz = rng.NextFloat();
            c.YawDegrees = rng.Range(0f, 360f);
            c.Scale = rng.Range(rules.MinScale, rules.MaxScale);

            c.X = gx * cell + jx * cell;
            c.Z = gz * cell + jz * cell;
            c.Priority = seed;

            if (!WorldConstants.InBounds(c.X, c.Z)) return c;
            if (!densityKeep) return c;

            c.Y = surface.Height(c.X, c.Z);
            if (c.Y < rules.MinHeightMeters || c.Y > rules.MaxHeightMeters) return c;
            if (surface.SlopeDegrees(c.X, c.Z) > rules.MaxSlopeDegrees) return c;
            if (surface.DistanceToRoadEdgeMeters(c.X, c.Z) < rules.RoadClearanceMeters) return c;
            if (surface.IsReserved(c.X, c.Z)) return c;

            c.Eligible = true;
            return c;
        }

        /// <summary>
        /// True when an eligible candidate with higher (priority, index) lies within the spacing radius.
        /// (lx, ly) are the candidate's coordinates inside the halo-padded array.
        /// </summary>
        static bool IsBlocked(in Candidate self, Candidate[] cands, int side, int lx, int ly, float spacing)
        {
            float spacingSq = spacing * spacing;
            for (int dz = -ConflictRingCells; dz <= ConflictRingCells; dz++)
            for (int dx = -ConflictRingCells; dx <= ConflictRingCells; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int sx = lx + dx;
                int sz = ly + dz;
                if (sx < 0 || sz < 0 || sx >= side || sz >= side) continue;
                var other = cands[sz * side + sx];
                if (!other.Eligible) continue;
                if (other.Priority < self.Priority ||
                    (other.Priority == self.Priority && other.Index < self.Index)) continue;
                float ddx = other.X - self.X;
                float ddz = other.Z - self.Z;
                if (ddx * ddx + ddz * ddz < spacingSq) return true;
            }
            return false;
        }
    }
}
