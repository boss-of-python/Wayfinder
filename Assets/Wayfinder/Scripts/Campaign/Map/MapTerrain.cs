using System;
using System.Collections.Generic;
using Wayfinder.Core;
using Wayfinder.World;

namespace Wayfinder.Campaign
{
    /// <summary>Surface classes the map can draw. Gameplay surfaces and map colours share one source (§7.2 step 7).</summary>
    public enum MapSurface : byte
    {
        Ground,
        Water,
        Mud,
        Pad,
        Unbuilt
    }

    public readonly struct MapTerrainSample
    {
        public readonly float HeightMeters;
        public readonly MapSurface Surface;
        public readonly float WaterDepthMeters;

        public MapTerrainSample(float heightMeters, MapSurface surface, float waterDepthMeters = 0f)
        {
            HeightMeters = heightMeters;
            Surface = surface;
            WaterDepthMeters = waterDepthMeters;
        }
    }

    public interface IMapTerrainSource
    {
        float HeightAt(float x, float z);
        MapTerrainSample Sample(float x, float z);
    }

    /// <summary>
    /// Map terrain for the vertical slice greybox (§21.3). Inside the 1,024 m slice box it matches
    /// <c>SceneBuilder.BuildVerticalSlice</c> byte-for-byte; outside it defers to <see cref="MapSurface.Unbuilt"/>
    /// unless wrapped in <see cref="MainlandMapTerrain"/> or replaced by <see cref="HeightFieldTerrainSource"/>.
    /// </summary>
    public sealed class SliceMapTerrain : IMapTerrainSource
    {
        public const float HillRampFraction = 0.35f;

        readonly VerticalSliceManifest _slice;
        readonly List<GreyboxFeature> _water = new List<GreyboxFeature>();
        readonly List<GreyboxFeature> _mud = new List<GreyboxFeature>();
        readonly List<GreyboxFeature> _hills = new List<GreyboxFeature>();

        public SliceMapTerrain(VerticalSliceManifest slice)
        {
            _slice = slice ?? throw new ArgumentNullException(nameof(slice));
            foreach (var f in slice.GreyboxFeatures)
            {
                switch (f.Kind)
                {
                    case "water": _water.Add(f); break;
                    case "mud": _mud.Add(f); break;
                    case "hill": _hills.Add(f); break;
                }
            }
        }

        public float HeightAt(float x, float z)
        {
            if (!_slice.Contains(new WorldPoint(x, 0f, z))) return float.NaN;
            float h = 0f;
            foreach (var hill in _hills)
            {
                float hh = HillHeight(hill, x, z);
                if (hh > h) h = hh;
            }
            return h;
        }

        public MapTerrainSample Sample(float x, float z)
        {
            float h = HeightAt(x, z);
            if (float.IsNaN(h)) return new MapTerrainSample(float.NaN, MapSurface.Unbuilt);
            foreach (var w in _water)
                if (Inside(w, x, z)) return new MapTerrainSample(h, MapSurface.Water, w.Size.Y);
            foreach (var m in _mud)
                if (Inside(m, x, z)) return new MapTerrainSample(h, MapSurface.Mud);
            float dx = x - _slice.ArrivalCenter.X, dz = z - _slice.ArrivalCenter.Z;
            if (dx * dx + dz * dz <= _slice.ArrivalRadiusMeters * _slice.ArrivalRadiusMeters)
                return new MapTerrainSample(h, MapSurface.Pad);
            return new MapTerrainSample(h, MapSurface.Ground);
        }

        static bool Inside(GreyboxFeature f, float x, float z) =>
            Math.Abs(x - f.Center.X) <= f.Size.X * 0.5f && Math.Abs(z - f.Center.Z) <= f.Size.Z * 0.5f;

        static float HillHeight(GreyboxFeature hill, float x, float z)
        {
            if (!Inside(hill, x, z)) return 0f;
            float rise = hill.Size.Y;
            float run = hill.Size.Z * HillRampFraction;
            if (run <= 0f) return rise;
            float fromEdge = hill.Size.Z * 0.5f - Math.Abs(z - hill.Center.Z);
            return rise * Math.Max(0f, Math.Min(1f, fromEdge / run));
        }
    }

    /// <summary>
    /// Continental map terrain source (§8.1, KI-029) backed directly by <see cref="NoiseField"/> /
    /// <see cref="HeightField"/>, <see cref="WorldHydrology"/>, and optional road/hub pads so the
    /// full 6,144 m × 6,144 m world draws real ocean, rivers, marshes, pads, contours, hillshade,
    /// and 3D ridge line-of-sight everywhere.
    /// </summary>
    public sealed class HeightFieldTerrainSource : IMapTerrainSource
    {
        readonly NoiseField _noise;
        readonly HeightField _baked;
        readonly MainlandRouteGraph _routes;
        readonly List<(float x, float z, float r2)> _pads = new List<(float, float, float)>();
        public WetlandWaterState WaterState { get; set; }

        public HeightFieldTerrainSource(
            CampaignContent content = null,
            NoiseField noise = null,
            HeightField baked = null,
            MainlandRouteGraph routes = null,
            WetlandWaterState waterState = WetlandWaterState.LowWater)
        {
            _noise = noise ?? new NoiseField();
            _baked = baked;
            _routes = routes;
            WaterState = waterState;

            if (content != null)
            {
                foreach (var g in content.Garages.Values)
                    _pads.Add((g.Position.X, g.Position.Z, 24f * 24f));
                foreach (var c in content.Camps.Values)
                    _pads.Add((c.Position.X, c.Position.Z, 14f * 14f));
            }
        }

        public float HeightAt(float x, float z)
        {
            if (!WorldConstants.InBounds(x, z)) return float.NaN;
            return _baked != null && _baked.IsBaked ? _baked.Sample(x, z) : _noise.BaseHeight(x, z);
        }

        public MapTerrainSample Sample(float x, float z)
        {
            float h = HeightAt(x, z);
            if (float.IsNaN(h)) return new MapTerrainSample(float.NaN, MapSurface.Unbuilt);

            float water = WorldHydrology.WaterDepthAt(x, z, WaterState, _noise);
            if (h <= WorldConstants.SeaLevelY || water >= 0.12f)
                return new MapTerrainSample(Math.Max(h, WorldConstants.SeaLevelY), MapSurface.Water, water);

            for (int i = 0; i < _pads.Count; i++)
            {
                float dx = x - _pads[i].x, dz = z - _pads[i].z;
                if (dx * dx + dz * dz <= _pads[i].r2)
                    return new MapTerrainSample(h, MapSurface.Pad);
            }

            if (_routes != null && _routes.DistanceToRoadEdgeMeters(x, z, out _) <= 1.5f)
                return new MapTerrainSample(h, MapSurface.Pad);

            var surf = WorldHydrology.SurfaceAt(x, z, 20f, WaterState, _noise);
            if (surf == SurfaceType.DeepMud || surf == SurfaceType.ShallowMud)
                return new MapTerrainSample(h, MapSurface.Mud);

            return new MapTerrainSample(h, MapSurface.Ground);
        }
    }

    /// <summary>
    /// Composite campaign terrain source: exact slice greybox inside [512..1536]×[1792..2816] and
    /// full continental <see cref="HeightFieldTerrainSource"/> everywhere else across the 6,144 m world.
    /// </summary>
    public sealed class MainlandMapTerrain : IMapTerrainSource
    {
        readonly VerticalSliceManifest _slice;
        readonly SliceMapTerrain _sliceTerrain;
        readonly HeightFieldTerrainSource _mainland;

        public HeightFieldTerrainSource Mainland => _mainland;

        public MainlandMapTerrain(CampaignContent content, MainlandRouteGraph routes = null)
        {
            _slice = content.Slice;
            _sliceTerrain = new SliceMapTerrain(_slice);
            _mainland = new HeightFieldTerrainSource(content, routes: routes);
        }

        public float HeightAt(float x, float z) =>
            _slice.Contains(new WorldPoint(x, 0f, z)) ? _sliceTerrain.HeightAt(x, z) : _mainland.HeightAt(x, z);

        public MapTerrainSample Sample(float x, float z) =>
            _slice.Contains(new WorldPoint(x, 0f, z)) ? _sliceTerrain.Sample(x, z) : _mainland.Sample(x, z);
    }
}
