using System;
using Wayfinder.Core;

namespace Wayfinder.World
{
    /// <summary>Coastal Wetlands dual water state (§5.3, §6.1, §6.2 D, KI-029).</summary>
    public enum WetlandWaterState
    {
        LowWater = 0,
        HighWater = 1
    }

    public enum WorldBiome
    {
        Forest = 0,
        Canyon = 1,
        City = 2,
        Wetlands = 3,
        Highlands = 4
    }

    /// <summary>
    /// Deterministic hydrology and surface classification across the 6,144 m mainland (§4.3, §6.1).
    /// Pure C# (depends only on <c>Wayfinder.Core</c>). Models open coastal water, regional creeks,
    /// the Coastal Wetlands dual water state (low tide exposes the direct tidal crossing at 0.34 m;
    /// high tide floods it to 0.78 m while keeping the raised firm causeway at 0.28 m), and maps any
    /// world coordinate onto the 11 canonical <see cref="SurfaceType"/> values.
    /// </summary>
    public static class WorldHydrology
    {
        public const float TidalCycleSeconds = 900f;
        static readonly NoiseField DefaultNoise = new NoiseField();

        public static WetlandWaterState StateAtTime(double simulationSeconds)
        {
            if (simulationSeconds <= 0.0) return WetlandWaterState.LowWater;
            double phase = (simulationSeconds % TidalCycleSeconds) / TidalCycleSeconds;
            return phase >= 0.5 ? WetlandWaterState.HighWater : WetlandWaterState.LowWater;
        }

        public static WorldBiome NearestBiome(float x, float z)
        {
            var anchors = new (WorldBiome id, float ax, float az)[]
            {
                (WorldBiome.Forest, WorldConstants.ForestRelayAnchor.x, WorldConstants.ForestRelayAnchor.z),
                (WorldBiome.Canyon, WorldConstants.CanyonRelayAnchor.x, WorldConstants.CanyonRelayAnchor.z),
                (WorldBiome.City, WorldConstants.CityRelayAnchor.x, WorldConstants.CityRelayAnchor.z),
                (WorldBiome.Wetlands, WorldConstants.WetlandRelayAnchor.x, WorldConstants.WetlandRelayAnchor.z),
                (WorldBiome.Highlands, WorldConstants.HighlandRelayAnchor.x, WorldConstants.HighlandRelayAnchor.z)
            };
            WorldBiome best = WorldBiome.Forest;
            float bestD = float.MaxValue;
            for (int i = 0; i < anchors.Length; i++)
            {
                float dx = anchors[i].ax - x, dz = anchors[i].az - z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = anchors[i].id; }
            }
            return best;
        }

        public static float WaterDepthAt(
            float x,
            float z,
            WetlandWaterState state = WetlandWaterState.LowWater,
            NoiseField noise = null)
        {
            var nf = noise ?? DefaultNoise;
            float terrain = nf.BaseHeight(x, z);
            if (terrain <= WorldConstants.SeaLevelY)
                return Math.Max(0.05f, WorldConstants.SeaLevelY - terrain);

            // Forest Creek Ford (slice greybox feature & route.forest.ford at 985, 2350)
            if (Math.Abs(x - 985f) <= 22f && Math.Abs(z - 2350f) <= 20f)
                return 0.28f;

            // Wetlands lower tidal channels flood in HighWater (0.78 m > 0.55 m stock intake, < 0.85 m Raised Intake)
            float dxTidal = (x - 3620f) / 240f;
            float dzTidal = (z - 1680f) / 220f;
            if (dxTidal * dxTidal + dzTidal * dzTidal <= 1f)
                return state == WetlandWaterState.HighWater ? 0.78f : 0.34f;

            float dxCross = (x - 3615f) / 140f;
            float dzCross = (z - 730f) / 90f;
            if (dxCross * dxCross + dzCross * dzCross <= 1f)
                return state == WetlandWaterState.HighWater ? 0.78f : 0.34f;

            // Raised firm causeway stays shallow (<= 0.28 m) in BOTH states (§6.2 D)
            float dxFirm = (x - 3216f) / 180f;
            float dzFirm = (z - 1183f) / 260f;
            if (dxFirm * dxFirm + dzFirm * dzFirm <= 1f)
                return state == WetlandWaterState.HighWater ? 0.28f : 0.08f;

            return 0f;
        }

        public static SurfaceType SurfaceAt(
            float x,
            float z,
            float roadDistanceMeters,
            WetlandWaterState state = WetlandWaterState.LowWater,
            NoiseField noise = null)
        {
            var nf = noise ?? DefaultNoise;
            float water = WaterDepthAt(x, z, state, nf);
            if (water >= 0.12f)
                return SurfaceType.ShallowMud;

            var biome = NearestBiome(x, z);
            float h = nf.BaseHeight(x, z);
            float patch = NoiseField.Fbm(x * 0.012f, z * 0.012f, 2, nf.LayerSeed(9));

            if (roadDistanceMeters <= 5.5f)
            {
                return biome switch
                {
                    WorldBiome.City => patch > 0.55f ? SurfaceType.IntactAsphalt : SurfaceType.BrokenAsphalt,
                    WorldBiome.Canyon => SurfaceType.Gravel,
                    WorldBiome.Highlands => h > 330f && patch > 0.62f ? SurfaceType.PackedSnow : SurfaceType.Gravel,
                    WorldBiome.Wetlands => patch > 0.72f ? SurfaceType.ShallowMud : SurfaceType.PackedDirt,
                    _ => SurfaceType.PackedDirt
                };
            }

            return biome switch
            {
                WorldBiome.City => patch > 0.5f ? SurfaceType.BrokenAsphalt : SurfaceType.Grass,
                WorldBiome.Canyon => patch > 0.52f ? SurfaceType.Sand : SurfaceType.Rock,
                WorldBiome.Wetlands => patch > 0.62f ? SurfaceType.DeepMud : SurfaceType.ShallowMud,
                WorldBiome.Highlands => h > 300f ? SurfaceType.PackedSnow : SurfaceType.Rock,
                _ => patch > 0.68f ? SurfaceType.ShallowMud : SurfaceType.Grass
            };
        }
    }
}
