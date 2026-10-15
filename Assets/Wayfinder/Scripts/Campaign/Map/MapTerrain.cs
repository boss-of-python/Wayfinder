using System;
using System.Collections.Generic;

namespace Wayfinder.Campaign
{
    /// <summary>Surface classes the map can draw. Gameplay surfaces and map colours share one source (§7.2 step 7).</summary>
    public enum MapSurface : byte
    {
        Ground,
        Water,
        Mud,
        Pad,
        /// <summary>Outside the authored greybox area: real terrain does not exist there yet (KI-027/KI-029).</summary>
        Unbuilt
    }

    public readonly struct MapTerrainSample
    {
        /// <summary>Ground height in metres; NaN when unknown (never occludes, draws flat).</summary>
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

    /// <summary>
    /// Terrain data for the exploration map: heights drive both the §8.1 reveal line-of-sight and
    /// the map's hillshade/contours, so the map can never show relief the reveal ignored.
    /// Implementations must be deterministic and cheap (sampled ≤ 7 times per drawn pixel).
    /// </summary>
    public interface IMapTerrainSource
    {
        float HeightAt(float x, float z);
        MapTerrainSample Sample(float x, float z);
    }

    /// <summary>
    /// Map terrain for the vertical slice, derived from <c>vertical_slice.json</c> exactly as
    /// <c>SceneBuilder.BuildVerticalSlice</c> builds the greybox: flat ground at y = 0 inside the
    /// slice area, the ford water channel, the mud patch, the garage arrival pad and the 8 m hill
    /// (ramps over 35 % of its length at each end, vertical sides). Outside the area there is no
    /// ground in the scene, so heights are unknown (NaN) and the surface is <see cref="MapSurface.Unbuilt"/>.
    /// Replace with baked minimap/heightfield data when the M3 terrain bake lands (KI-029).
    /// </summary>
    public sealed class SliceMapTerrain : IMapTerrainSource
    {
        public const float HillRampFraction = 0.35f; // must match SceneBuilder (run = size.z × 0.35)

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

        /// <summary>Trapezoid profile along z (up-ramp, flat top, down-ramp); vertical sides along x.</summary>
        static float HillHeight(GreyboxFeature hill, float x, float z)
        {
            if (!Inside(hill, x, z)) return 0f;
            float rise = hill.Size.Y;
            float run = hill.Size.Z * HillRampFraction;
            if (run <= 0f) return rise;
            float fromEdge = hill.Size.Z * 0.5f - Math.Abs(z - hill.Center.Z); // 0 at either end
            return fromEdge >= run ? rise : rise * (fromEdge / run);
        }
    }
}
