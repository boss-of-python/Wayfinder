using System;
using Wayfinder.Core;

namespace Wayfinder.Campaign
{
    public readonly struct MapColor : IEquatable<MapColor>
    {
        public readonly byte R, G, B, A;

        public MapColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public MapColor Scaled(float k) => new MapColor(Channel(R * k), Channel(G * k), Channel(B * k), A);

        public static MapColor Lerp(MapColor a, MapColor b, float t)
        {
            t = MathUtil.Clamp01(t);
            return new MapColor(Channel(a.R + (b.R - a.R) * t), Channel(a.G + (b.G - a.G) * t),
                Channel(a.B + (b.B - a.B) * t), Channel(a.A + (b.A - a.A) * t));
        }

        static byte Channel(float v) => (byte)(v <= 0f ? 0 : (v >= 255f ? 255 : (int)(v + 0.5f)));

        public bool Equals(MapColor o) => R == o.R && G == o.G && B == o.B && A == o.A;
        public override bool Equals(object obj) => obj is MapColor o && Equals(o);
        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
    }

    /// <summary>Map colours: a blank survey sheet that fills in with stylized terrain (§13.4, §14.1).</summary>
    public static class MapPalette
    {
        public static readonly MapColor Fog = new MapColor(222, 213, 188);
        public static readonly MapColor Ground = new MapColor(98, 128, 82);
        public static readonly MapColor Water = new MapColor(66, 118, 160);
        public static readonly MapColor DeepWater = new MapColor(38, 78, 120);
        public static readonly MapColor Mud = new MapColor(112, 88, 60);
        public static readonly MapColor Pad = new MapColor(170, 162, 140);
        public static readonly MapColor Unbuilt = new MapColor(176, 176, 168);
        public static readonly MapColor Contour = new MapColor(64, 70, 44);

        public const float GridDarken = 0.90f;        // 256 m chunk grid on fog and land
        public const float GrainAmplitude = 0.035f;   // ±3.5 % paper/canopy grain
        public const float ContourIntervalMeters = 2f;
        public const float WaterDepthForDeepTint = 1.5f;

        public static MapColor ForSurface(in MapTerrainSample s)
        {
            switch (s.Surface)
            {
                case MapSurface.Water:
                    return MapColor.Lerp(Water, DeepWater, s.WaterDepthMeters / WaterDepthForDeepTint);
                case MapSurface.Mud: return Mud;
                case MapSurface.Pad: return Pad;
                case MapSurface.Unbuilt: return Unbuilt;
                default: return Ground;
            }
        }
    }

    /// <summary>
    /// CPU-side RGBA32 image of a rectangular window of map cells (the slice area by default, the
    /// whole 96 × 96 mainland later): <see cref="CellsX"/> × <see cref="CellsZ"/> cells of
    /// <see cref="PixelsPerCell"/>² pixels. Row 0 is the window's SOUTH edge and column 0 its west
    /// edge — the bottom-up row order Unity's <c>Texture2D.LoadRawTextureData</c> expects — so the
    /// presentation layer uploads <see cref="Pixels"/> verbatim whenever <see cref="Version"/>
    /// changes. The dirty rectangle accumulates until <see cref="ClearDirty"/> for partial uploads.
    /// </summary>
    public sealed class MapRaster
    {
        public const int MaxTexturePixels = 768;   // ≤ 2.4 MB RGBA32 (§18 memory budget)
        public const int MaxPixelsPerCell = 32;

        public readonly int OriginCellX, OriginCellZ;
        public readonly int CellsX, CellsZ;
        public readonly int PixelsPerCell;
        public readonly int Width;
        public readonly int Height;
        public readonly float CellSizeMeters;
        public readonly float MetersPerPixel;
        public readonly byte[] Pixels;

        public int Version { get; private set; }
        public bool HasDirty { get; private set; }
        public int DirtyMinX { get; private set; }
        public int DirtyMinY { get; private set; }
        public int DirtyMaxX { get; private set; }
        public int DirtyMaxY { get; private set; }

        public MapRaster(int originCellX, int originCellZ, int cellsX, int cellsZ, int pixelsPerCell, float cellSizeMeters)
        {
            if (cellsX <= 0 || cellsZ <= 0) throw new ArgumentOutOfRangeException(nameof(cellsX));
            if (pixelsPerCell <= 0 || pixelsPerCell > MaxPixelsPerCell) throw new ArgumentOutOfRangeException(nameof(pixelsPerCell));
            if (cellSizeMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSizeMeters));
            OriginCellX = originCellX;
            OriginCellZ = originCellZ;
            CellsX = cellsX;
            CellsZ = cellsZ;
            PixelsPerCell = pixelsPerCell;
            Width = cellsX * pixelsPerCell;
            Height = cellsZ * pixelsPerCell;
            CellSizeMeters = cellSizeMeters;
            MetersPerPixel = cellSizeMeters / pixelsPerCell;
            Pixels = new byte[Width * Height * 4];
        }

        /// <summary>Densest pixels-per-cell that keeps the texture within <see cref="MaxTexturePixels"/>.</summary>
        public static int ChoosePixelsPerCell(int cellsAcross) =>
            Math.Max(1, Math.Min(MaxPixelsPerCell, MaxTexturePixels / Math.Max(1, cellsAcross)));

        public float MinWorldX => OriginCellX * CellSizeMeters;
        public float MinWorldZ => OriginCellZ * CellSizeMeters;
        public float WidthMeters => CellsX * CellSizeMeters;
        public float HeightMeters => CellsZ * CellSizeMeters;

        public bool ContainsCell(int cellX, int cellZ) =>
            cellX >= OriginCellX && cellX < OriginCellX + CellsX && cellZ >= OriginCellZ && cellZ < OriginCellZ + CellsZ;

        public bool ContainsWorld(float x, float z) =>
            x >= MinWorldX && x < MinWorldX + WidthMeters && z >= MinWorldZ && z < MinWorldZ + HeightMeters;

        public MapColor GetPixel(int x, int y)
        {
            int i = Offset(x, y);
            return new MapColor(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
        }

        /// <summary>Pixel at a world position (x east, z north), clamped to the window.</summary>
        public MapColor GetPixelAtWorld(float x, float z) =>
            GetPixel(Math.Min(Width - 1, Math.Max(0, (int)((x - MinWorldX) / MetersPerPixel))),
                Math.Min(Height - 1, Math.Max(0, (int)((z - MinWorldZ) / MetersPerPixel))));

        internal void SetPixel(int x, int y, MapColor c)
        {
            int i = Offset(x, y);
            Pixels[i] = c.R;
            Pixels[i + 1] = c.G;
            Pixels[i + 2] = c.B;
            Pixels[i + 3] = c.A;
        }

        internal void MarkDirtyCell(int cellX, int cellZ)
        {
            int x0 = (cellX - OriginCellX) * PixelsPerCell, y0 = (cellZ - OriginCellZ) * PixelsPerCell;
            int x1 = x0 + PixelsPerCell - 1, y1 = y0 + PixelsPerCell - 1;
            if (!HasDirty)
            {
                DirtyMinX = x0;
                DirtyMinY = y0;
                DirtyMaxX = x1;
                DirtyMaxY = y1;
                HasDirty = true;
                return;
            }
            if (x0 < DirtyMinX) DirtyMinX = x0;
            if (y0 < DirtyMinY) DirtyMinY = y0;
            if (x1 > DirtyMaxX) DirtyMaxX = x1;
            if (y1 > DirtyMaxY) DirtyMaxY = y1;
        }

        internal void BumpVersion() => Version++;

        /// <summary>Call after uploading the dirty rectangle.</summary>
        public void ClearDirty() => HasDirty = false;

        int Offset(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                throw new ArgumentOutOfRangeException($"pixel ({x},{y}) outside {Width}x{Height}");
            return (y * Width + x) * 4;
        }
    }

    /// <summary>
    /// Draws map cells into a <see cref="MapRaster"/>. Fogged cells are a blank survey sheet
    /// (paper grain + faint 256 m grid, identical before and after any amount of play); revealed
    /// cells show terrain colour × hillshade with a soft fade toward fogged neighbours; surveyed
    /// cells add contour lines (§8.1 "separate flags for surveyed detail"). A fogged cell's pixels
    /// depend on nothing else, and a revealed cell's only on its own and its eight neighbours'
    /// flags, so redrawing changed cells plus their revealed neighbours is exactly equivalent to a
    /// full redraw (asserted byte-for-byte by the headless tests).
    /// </summary>
    internal sealed class MapRasterizer
    {
        const float FadeWidthCells = 0.45f;
        static readonly ulong GrainSeed = StableHash.Hash64("wayfinder.map.grain.v1");

        readonly MapRaster _raster;
        readonly IMapTerrainSource _terrain;
        readonly int _gridPixels;

        public MapRasterizer(MapRaster raster, IMapTerrainSource terrain)
        {
            _raster = raster;
            _terrain = terrain;
            _gridPixels = Math.Max(1, (int)Math.Round(Wayfinder.World.WorldConstants.ChunkSizeMeters / raster.MetersPerPixel));
        }

        /// <summary>Draws one cell (world cell coordinates; must lie inside the raster window).</summary>
        public void RenderCell(int cx, int cz, BitGrid revealed, BitGrid surveyed)
        {
            int p = _raster.PixelsPerCell;
            int baseX = (cx - _raster.OriginCellX) * p, baseY = (cz - _raster.OriginCellZ) * p;
            bool isRevealed = revealed.Get(cx, cz);
            bool isSurveyed = isRevealed && surveyed.Get(cx, cz);
            for (int j = 0; j < p; j++)
            for (int i = 0; i < p; i++)
            {
                int gx = cx * p + i, gy = cz * p + j; // world-aligned pixel index: grain/grid never shift with the window
                var fog = FogPixel(gx, gy);
                if (!isRevealed)
                {
                    _raster.SetPixel(baseX + i, baseY + j, fog);
                    continue;
                }
                float fade = EdgeFade(cx, cz, (i + 0.5f) / p, (j + 0.5f) / p, revealed);
                var land = LandPixel(gx, gy, isSurveyed);
                _raster.SetPixel(baseX + i, baseY + j, fade >= 1f ? land : MapColor.Lerp(fog, land, fade));
            }
            _raster.MarkDirtyCell(cx, cz);
        }

        MapColor FogPixel(int gx, int gy)
        {
            float k = 1f + (StableRandom.Hash01(GrainSeed, gx, gy) - 0.5f) * 2f * MapPalette.GrainAmplitude * 0.6f;
            if (IsGridPixel(gx, gy)) k *= MapPalette.GridDarken;
            return MapPalette.Fog.Scaled(k);
        }

        MapColor LandPixel(int gx, int gy, bool surveyed)
        {
            float mpp = _raster.MetersPerPixel;
            float x = (gx + 0.5f) * mpp, z = (gy + 0.5f) * mpp;
            var sample = _terrain.Sample(x, z);
            var color = MapPalette.ForSurface(sample);

            float shade = 1f;
            if (!float.IsNaN(sample.HeightMeters))
            {
                float h = 0.5f * mpp;
                float slopeX = (Height(x + h, z, sample.HeightMeters) - Height(x - h, z, sample.HeightMeters)) / mpp;
                float slopeZ = (Height(x, z + h, sample.HeightMeters) - Height(x, z - h, sample.HeightMeters)) / mpp;
                // Light from the north-west: normal ∝ (−sx, 1, −sz) · light (−0.6, ·, 0.6).
                shade = MathUtil.Clamp(1f + 1.6f * (0.6f * slopeX - 0.6f * slopeZ), 0.62f, 1.3f);
                if (surveyed && CrossesContour(x, z, sample.HeightMeters, mpp)) color = MapPalette.Contour;
            }
            float grain = 1f + (StableRandom.Hash01(GrainSeed ^ 0x9E3779B97F4A7C15ul, gx, gy) - 0.5f) * 2f * MapPalette.GrainAmplitude;
            if (IsGridPixel(gx, gy)) grain *= MapPalette.GridDarken;
            return color.Scaled(shade * grain);
        }

        float Height(float x, float z, float fallback)
        {
            float h = _terrain.HeightAt(x, z);
            return float.IsNaN(h) ? fallback : h;
        }

        bool CrossesContour(float x, float z, float h, float mpp)
        {
            int band = (int)Math.Floor(h / MapPalette.ContourIntervalMeters);
            int east = (int)Math.Floor(Height(x + mpp, z, h) / MapPalette.ContourIntervalMeters);
            int north = (int)Math.Floor(Height(x, z + mpp, h) / MapPalette.ContourIntervalMeters);
            return east != band || north != band;
        }

        bool IsGridPixel(int gx, int gy) => gx % _gridPixels == 0 || gy % _gridPixels == 0;

        /// <summary>1 inside the revealed area; ramps to 0 within <see cref="FadeWidthCells"/> of a fogged neighbour.</summary>
        float EdgeFade(int cx, int cz, float fx, float fz, BitGrid revealed)
        {
            float nearest = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = cx + dx, nz = cz + dz;
                if (nx < 0 || nz < 0 || nx >= revealed.Width || nz >= revealed.Height) continue; // world edge: no fade
                if (revealed.Get(nx, nz)) continue;
                float ex = dx < 0 ? fx : (dx > 0 ? 1f - fx : 0f);
                float ez = dz < 0 ? fz : (dz > 0 ? 1f - fz : 0f);
                float d = dx != 0 && dz != 0 ? (float)Math.Sqrt(ex * ex + ez * ez) : (dx != 0 ? ex : ez);
                if (d < nearest) nearest = d;
            }
            if (nearest >= FadeWidthCells) return 1f;
            float t = nearest / FadeWidthCells;
            return t * t * (3f - 2f * t);
        }
    }
}
