using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.World
{
    public readonly struct ReservedSocket
    {
        public readonly float X;
        public readonly float Z;
        public readonly float RadiusMeters;

        public ReservedSocket(float x, float z, float radiusMeters)
        {
            X = x;
            Z = z;
            RadiusMeters = radiusMeters;
        }
    }

    public readonly struct WinchAnchorPlacement
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public WinchAnchorPlacement(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// Concrete <see cref="IScatterSurface"/> backed by <see cref="NoiseField"/>,
    /// <see cref="MainlandRouteGraph"/>, and reserved gameplay sockets (§7.2, §7.6, KI-028, KI-037).
    /// Also generates deterministic road-shoulder <see cref="WinchAnchorPlacement"/> coordinates.
    /// </summary>
    public sealed class WorldScatterSurface : IScatterSurface
    {
        readonly MainlandRouteGraph _routes;
        readonly NoiseField _noise;
        readonly Dictionary<int, List<ReservedSocket>> _reservedByChunk = new Dictionary<int, List<ReservedSocket>>();

        public WorldScatterSurface(
            MainlandRouteGraph routes,
            IEnumerable<ReservedSocket> reservedSockets = null,
            NoiseField noise = null)
        {
            _routes = routes ?? throw new ArgumentNullException(nameof(routes));
            _noise = noise ?? new NoiseField();
            if (reservedSockets != null)
                foreach (var s in reservedSockets)
                    AddReserved(s);
        }

        public void AddReserved(ReservedSocket socket)
        {
            float pad = Math.Max(4f, socket.RadiusMeters);
            int minX = Math.Max(0, WorldConstants.ChunkCoordFromWorld(socket.X - pad));
            int maxX = Math.Min(WorldConstants.ChunksPerSide - 1, WorldConstants.ChunkCoordFromWorld(socket.X + pad));
            int minZ = Math.Max(0, WorldConstants.ChunkCoordFromWorld(socket.Z - pad));
            int maxZ = Math.Min(WorldConstants.ChunksPerSide - 1, WorldConstants.ChunkCoordFromWorld(socket.Z + pad));
            for (int cz = minZ; cz <= maxZ; cz++)
            for (int cx = minX; cx <= maxX; cx++)
            {
                int key = cz * WorldConstants.ChunksPerSide + cx;
                if (!_reservedByChunk.TryGetValue(key, out var list))
                    _reservedByChunk[key] = list = new List<ReservedSocket>();
                list.Add(socket);
            }
        }

        public float Height(float x, float z) => _noise.BaseHeight(x, z);

        public float SlopeDegrees(float x, float z) => _noise.SlopeDegrees(x, z, 6f);

        public float DistanceToRoadEdgeMeters(float x, float z) => _routes.DistanceToRoadEdgeMeters(x, z, out _);

        public bool IsReserved(float x, float z)
        {
            if (Height(x, z) <= WorldConstants.SeaLevelY + 0.25f)
                return true;
            var (cx, cz) = WorldConstants.ChunkOf(x, z);
            if (_reservedByChunk.TryGetValue(cz * WorldConstants.ChunksPerSide + cx, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    float dx = x - list[i].X, dz = z - list[i].Z;
                    if (dx * dx + dz * dz <= list[i].RadiusMeters * list[i].RadiusMeters)
                        return true;
                }
            }
            return false;
        }

        public static ScatterRules[] DefaultBiomeRules(WorldBiome biome) => biome switch
        {
            WorldBiome.Forest => new[]
            {
                ScatterRules.Trees("scatter.forest.pine", 101, spacingMeters: 7f, density: 0.45f),
                new ScatterRules("scatter.forest.boulder", 102, 16f, 0.30f, 34f, 0.5f, WorldConstants.TerrainMaxY, 2.5f)
            },
            WorldBiome.Canyon => new[]
            {
                new ScatterRules("scatter.canyon.hoodoo", 201, 20f, 0.32f, 35f, 0.5f, WorldConstants.TerrainMaxY, 3.5f),
                new ScatterRules("scatter.canyon.scrub", 202, 10f, 0.38f, 26f, 0.5f, WorldConstants.TerrainMaxY, 2.0f)
            },
            WorldBiome.City => new[]
            {
                new ScatterRules("scatter.city.barrier", 301, 16f, 0.28f, 20f, 0.5f, WorldConstants.TerrainMaxY, 2.5f),
                ScatterRules.Trees("scatter.city.birch", 302, spacingMeters: 8f, density: 0.35f)
            },
            WorldBiome.Wetlands => new[]
            {
                ScatterRules.Trees("scatter.wetlands.cypress", 401, spacingMeters: 8f, density: 0.38f),
                new ScatterRules("scatter.wetlands.reeds", 402, 8f, 0.48f, 16f, 0.2f, 60f, 1.5f)
            },
            _ => new[]
            {
                ScatterRules.Trees("scatter.highlands.spruce", 501, spacingMeters: 8f, density: 0.34f),
                new ScatterRules("scatter.highlands.granite", 502, 16f, 0.34f, 36f, 0.5f, WorldConstants.TerrainMaxY, 2.5f)
            }
        };

        /// <summary>Places deterministic road-shoulder winch anchors (2..11 m off road edge on dry ground).</summary>
        public List<WinchAnchorPlacement> PlaceWinchAnchorsForChunk(
            int chunkX,
            int chunkZ,
            int worldSeed = WorldConstants.CanonicalWorldSeed,
            int generatorVersion = WorldConstants.GeneratorVersion)
        {
            var result = new List<WinchAnchorPlacement>();
            if (chunkX < 0 || chunkZ < 0 || chunkX >= WorldConstants.ChunksPerSide || chunkZ >= WorldConstants.ChunksPerSide)
                return result;

            float minX = chunkX * WorldConstants.ChunkSizeMeters;
            float minZ = chunkZ * WorldConstants.ChunkSizeMeters;
            ulong seed = StableHash.ChunkSeed(worldSeed, generatorVersion, chunkX, chunkZ, layerId: 777);
            var rng = new StableRandom(seed);

            for (int i = 0; i < 40 && result.Count < 6; i++)
            {
                float x = rng.Range(minX + 6f, minX + WorldConstants.ChunkSizeMeters - 6f);
                float z = rng.Range(minZ + 6f, minZ + WorldConstants.ChunkSizeMeters - 6f);
                float roadDist = DistanceToRoadEdgeMeters(x, z);
                if (roadDist < 2.0f || roadDist > 12.0f) continue;
                float h = Height(x, z);
                if (h <= WorldConstants.SeaLevelY + 0.5f) continue;
                if (IsReserved(x, z)) continue;

                bool tooClose = false;
                for (int k = 0; k < result.Count; k++)
                {
                    float dx = x - result[k].X, dz = z - result[k].Z;
                    if (dx * dx + dz * dz < 36f * 36f) { tooClose = true; break; }
                }
                if (!tooClose)
                    result.Add(new WinchAnchorPlacement(x, h, z));
            }
            return result;
        }
    }
}
