using System;
using System.Collections.Generic;
using System.Globalization;
using Wayfinder.Core;
using Wayfinder.Progression;
using Wayfinder.Simulation;
using Wayfinder.World;

namespace Wayfinder.Campaign
{
    /// <summary>
    /// The exploration map for one campaign session: starts as a blank survey sheet and fills in
    /// as the session reveals cells (§8.1). <see cref="Refresh"/> diffs the session's revealed and
    /// surveyed bitsets against what is already drawn and redraws only the changed cells plus
    /// their revealed neighbours (soft fog edges), optionally within a per-call cell budget so a
    /// big vista reveal is spread over several frames. Rollbacks and loads are handled by the
    /// same diff (cells can also return to fog). Engine-free; the Unity layer uploads
    /// <see cref="MapRaster.Pixels"/> to a texture when <see cref="MapRaster.Version"/> changes.
    /// </summary>
    public sealed class ExplorationMap
    {
        public const int UnlimitedBudget = int.MaxValue;

        readonly BitGrid _shownRevealed;
        readonly BitGrid _shownSurveyed;
        readonly MapRasterizer _rasterizer;
        readonly List<int> _changed = new List<int>();
        readonly Queue<int> _queue = new Queue<int>();
        readonly bool[] _queued;
        readonly int _gridSide;

        public CampaignSession Session { get; }
        public IMapTerrainSource Terrain { get; }
        public MapRaster Raster { get; }
        /// <summary>Markers from the last <see cref="RebuildMarkers"/> call (player arrow last).</summary>
        public readonly List<MapMarker> Markers = new List<MapMarker>();
        public int CellsDrawnLastRefresh { get; private set; }
        public int PendingCells => _queue.Count;

        /// <param name="fullMainland">Cover all 96 × 96 cells instead of the slice area (M3+).</param>
        /// <param name="pixelsPerCell">0 = densest that keeps the texture ≤ 768 px (32 px/cell for the slice).</param>
        public ExplorationMap(CampaignSession session, IMapTerrainSource terrain = null, bool fullMainland = false,
            int pixelsPerCell = 0)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Terrain = terrain ?? session.MapTerrain;
            _gridSide = WorldConstants.MapCellsPerSide;
            var (x0, z0, cells) = fullMainland ? (0, 0, _gridSide) : SliceWindow(session.Content.Slice);
            int ppc = pixelsPerCell > 0 ? pixelsPerCell : MapRaster.ChoosePixelsPerCell(cells);
            Raster = new MapRaster(x0, z0, cells, cells, ppc, WorldConstants.MapCellSizeMeters);
            _rasterizer = new MapRasterizer(Raster, Terrain);
            _shownRevealed = new BitGrid(_gridSide, _gridSide);
            _shownSurveyed = new BitGrid(_gridSide, _gridSide);
            _queued = new bool[_gridSide * _gridSide];
            RebuildAll();
            RebuildMarkers();
        }

        /// <summary>Cell window covering the slice area (16 × 16 cells for the 1024 m forest slice).</summary>
        public static (int originX, int originZ, int cells) SliceWindow(VerticalSliceManifest slice)
        {
            float cell = WorldConstants.MapCellSizeMeters;
            int x0 = Math.Max(0, (int)Math.Floor(slice.AreaMinX / cell));
            int z0 = Math.Max(0, (int)Math.Floor(slice.AreaMinZ / cell));
            int x1 = Math.Min(WorldConstants.MapCellsPerSide, (int)Math.Ceiling((slice.AreaMinX + slice.AreaSizeMeters) / cell));
            int z1 = Math.Min(WorldConstants.MapCellsPerSide, (int)Math.Ceiling((slice.AreaMinZ + slice.AreaSizeMeters) / cell));
            int cells = Math.Max(1, Math.Max(x1 - x0, z1 - z0));
            x0 = Math.Min(x0, WorldConstants.MapCellsPerSide - cells);
            z0 = Math.Min(z0, WorldConstants.MapCellsPerSide - cells);
            return (Math.Max(0, x0), Math.Max(0, z0), cells);
        }

        /// <summary>Redraws every cell in the window from the session's current flags.</summary>
        public void RebuildAll()
        {
            var revealed = Session.Discovery.RevealedCells;
            var surveyed = Session.Discovery.SurveyedCells;
            for (int cz = Raster.OriginCellZ; cz < Raster.OriginCellZ + Raster.CellsZ; cz++)
            for (int cx = Raster.OriginCellX; cx < Raster.OriginCellX + Raster.CellsX; cx++)
                _rasterizer.RenderCell(cx, cz, revealed, surveyed);
            _shownRevealed.CopyFrom(revealed);
            _shownSurveyed.CopyFrom(surveyed);
            _queue.Clear();
            Array.Clear(_queued, 0, _queued.Length);
            Raster.BumpVersion();
        }

        /// <summary>
        /// Brings the raster up to date with the session. Returns true when any pixel changed.
        /// With a budget, remaining cells stay queued for the next call (<see cref="PendingCells"/>).
        /// </summary>
        public bool Refresh(int maxCells = UnlimitedBudget)
        {
            var revealed = Session.Discovery.RevealedCells;
            var surveyed = Session.Discovery.SurveyedCells;
            _changed.Clear();
            revealed.CollectDifferences(_shownRevealed, _changed);
            surveyed.CollectDifferences(_shownSurveyed, _changed);
            foreach (int index in _changed)
            {
                int cx = index % _gridSide, cz = index / _gridSide;
                _shownRevealed.Set(cx, cz, revealed.Get(cx, cz));
                _shownSurveyed.Set(cx, cz, surveyed.Get(cx, cz));
                Enqueue(cx, cz);
                // A revealed neighbour's soft edge depends on this cell; fogged cells depend on nothing.
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, nz = cz + dz;
                    if ((dx != 0 || dz != 0) && nx >= 0 && nz >= 0 && nx < _gridSide && nz < _gridSide && revealed.Get(nx, nz))
                        Enqueue(nx, nz);
                }
            }

            int drawn = 0;
            while (_queue.Count > 0 && drawn < maxCells)
            {
                int index = _queue.Dequeue();
                _queued[index] = false;
                _rasterizer.RenderCell(index % _gridSide, index / _gridSide, revealed, surveyed);
                drawn++;
            }
            if (drawn > 0) Raster.BumpVersion();
            CellsDrawnLastRefresh = drawn;
            return drawn > 0;
        }

        public void RebuildMarkers(MapLayers layers = MapLayers.All) => MapMarkerRules.Build(Session, Markers, layers);

        void Enqueue(int cx, int cz)
        {
            if (!Raster.ContainsCell(cx, cz)) return; // outside the sheet: counted for coverage, not drawn
            int index = cz * _gridSide + cx;
            if (_queued[index]) return;
            _queued[index] = true;
            _queue.Enqueue(index);
        }

        // ------------------------------------------------------------------ coverage

        /// <summary>Percent of the sheet's countable cells revealed ("Region / % mapped", §13.4).</summary>
        public float SheetCoveragePercent
        {
            get
            {
                var revealed = Session.Discovery.RevealedCells;
                var mask = Session.Discovery.CountableMask;
                int total = 0, seen = 0;
                for (int cz = Raster.OriginCellZ; cz < Raster.OriginCellZ + Raster.CellsZ; cz++)
                for (int cx = Raster.OriginCellX; cx < Raster.OriginCellX + Raster.CellsX; cx++)
                {
                    if (!mask.Get(cx, cz)) continue;
                    total++;
                    if (revealed.Get(cx, cz)) seen++;
                }
                return total == 0 ? 0f : 100f * seen / total;
            }
        }
    }

    public readonly struct MapLegendEntry
    {
        public readonly MapMarkerKind Kind;
        public readonly string Label;

        public MapLegendEntry(MapMarkerKind kind, string label)
        {
            Kind = kind;
            Label = label;
        }
    }

    /// <summary>
    /// Text for the map screen (§13.4 wireframe): header "Region / % mapped", mainland coverage
    /// against the 60 % finale gate (§8.4), legend, selected-marker panel, return estimate to the
    /// nearest safe hub, and the route-target line. Distances are straight lines; there is no
    /// road graph in the slice yet, so every route is an *estimate* (drawn dashed, KI-030).
    /// </summary>
    public sealed class MapScreenModel
    {
        /// <summary>Scout consumption at the blueprint example rate (0.02255 L/s) and 30 km/h ≈ 2.7 L/km.</summary>
        public static readonly float NominalLitersPerKm = FuelModel.BlueprintExampleRate / (30f / 3.6f) * 1000f;
        public const float RouteDetourFactor = 1.3f;
        public const float MeasuredConsumptionMinMeters = 300f;

        public string HeaderLine = string.Empty;
        public string CoverageLine = string.Empty;
        public string ReturnLine = string.Empty;
        public bool ReturnFuelWarning;
        public string RouteLine = string.Empty;
        public bool HasRoute;
        public float RouteTargetX, RouteTargetZ;
        public string SelectionTitle = string.Empty;
        public string SelectionBody = string.Empty;
        public string MarkerCountLine = string.Empty;
        public float SheetPercent, MainlandPercent;

        public static readonly MapLegendEntry[] Legend =
        {
            new MapLegendEntry(MapMarkerKind.Player, "You (heading)"),
            new MapLegendEntry(MapMarkerKind.Garage, "Garage — safe hub"),
            new MapLegendEntry(MapMarkerKind.CampActive, "Active camp — safe hub"),
            new MapLegendEntry(MapMarkerKind.CampSite, "Camp socket"),
            new MapLegendEntry(MapMarkerKind.LandmarkDiscovered, "Discovered landmark"),
            new MapLegendEntry(MapMarkerKind.LandmarkSuspected, "Unconfirmed landmark"),
            new MapLegendEntry(MapMarkerKind.CacheOpened, "Opened cache"),
            new MapLegendEntry(MapMarkerKind.RelayOffline, "Relay mast"),
            new MapLegendEntry(MapMarkerKind.Objective, "Objective"),
            new MapLegendEntry(MapMarkerKind.ObjectiveArea, "Search area"),
            new MapLegendEntry(MapMarkerKind.Waypoint, "Your marker")
        };

        public static MapScreenModel Build(ExplorationMap map, int selectedIndex = -1, MapScreenModel into = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var m = into ?? new MapScreenModel();
            var s = map.Session;
            m.SheetPercent = map.SheetCoveragePercent;
            m.MainlandPercent = s.Discovery.MapCoveragePercent;
            m.HeaderLine = string.Format(CultureInfo.InvariantCulture, "{0} · {1:0}% mapped", RegionName(s), m.SheetPercent);
            m.CoverageLine = string.Format(CultureInfo.InvariantCulture,
                "Mainland {0:0.0}% mapped (finale needs {1:0}%) · {2} cells revealed", m.MainlandPercent,
                DiscoveryService.FinaleMapCoveragePercent, s.Discovery.RevealedCountableCells);
            m.MarkerCountLine = string.Format(CultureInfo.InvariantCulture, "Markers {0}/{1}", s.MapMarkers.Count,
                MapMarkerService.MaxMarkers);

            var (px, pz) = PlayerXZ(s);
            BuildReturnEstimate(s, px, pz, m);

            m.HasRoute = s.MapMarkers.TryGet(s.MapMarkers.RouteTargetId, out var target);
            if (m.HasRoute)
            {
                m.RouteTargetX = target.X;
                m.RouteTargetZ = target.Z;
                m.RouteLine = $"Route to {target.Label}: {Distance(px, pz, target.X, target.Z)} {Bearing(px, pz, target.X, target.Z)}" +
                              " · straight-line estimate (dashed)";
            }
            else m.RouteLine = "No route — long-press the map to place a marker, then set it as the route";

            if (selectedIndex >= 0 && selectedIndex < map.Markers.Count) Describe(s, map.Markers[selectedIndex], px, pz, m);
            else
            {
                m.SelectionTitle = string.Empty;
                m.SelectionBody = string.Empty;
            }
            return m;
        }

        public static string RegionName(CampaignSession s)
        {
            var home = s.Content.Garages.TryGetValue(s.Content.Slice.GarageId, out var g) ? g.Biome : "forest";
            string biome = string.IsNullOrEmpty(home) ? "Forest" : char.ToUpperInvariant(home[0]) + home.Substring(1);
            return biome + " test area";
        }

        static (float x, float z) PlayerXZ(CampaignSession s)
        {
            var pose = s.Expedition.IsActive && s.Expedition.Pose.IsSet ? s.Expedition.Pose : s.ResumePose;
            return (pose.Position.X, pose.Position.Z);
        }

        static void BuildReturnEstimate(CampaignSession s, float px, float pz, MapScreenModel m)
        {
            string bestName = string.Empty;
            float bestX = 0f, bestZ = 0f, bestD = float.MaxValue;
            var garageIds = new List<string>(s.Garage.UnlockedGarages);
            garageIds.Sort(StringComparer.Ordinal);
            foreach (var id in garageIds)
            {
                var p = s.Content.GaragePosition(id);
                float d = Dist(px, pz, p.X, p.Z);
                if (d < bestD) (bestD, bestX, bestZ, bestName) = (d, p.X, p.Z, DisplayNames.Garage(id));
            }
            foreach (var camp in s.Content.Slice.CampSockets)
            {
                if (!s.Garage.IsCampActivated(camp.Id)) continue;
                float d = Dist(px, pz, camp.Position.X, camp.Position.Z);
                if (d < bestD) (bestD, bestX, bestZ, bestName) = (d, camp.Position.X, camp.Position.Z, MapMarkerRules.CampName(camp.Id));
            }
            if (bestD == float.MaxValue)
            {
                m.ReturnLine = "No safe hub known";
                m.ReturnFuelWarning = false;
                return;
            }
            if (!s.Expedition.IsActive)
            {
                m.ReturnLine = $"At {bestName} — refuel, repair and bank here";
                m.ReturnFuelWarning = false;
                return;
            }
            var stats = s.Expedition.Stats;
            float perKm = stats.DistanceMeters >= MeasuredConsumptionMinMeters && stats.FuelUsedLiters > 0f
                ? stats.FuelUsedLiters / (stats.DistanceMeters / 1000f)
                : NominalLitersPerKm;
            float liters = bestD / 1000f * RouteDetourFactor * perKm;
            m.ReturnFuelWarning = liters > s.FuelLiters;
            m.ReturnLine = string.Format(CultureInfo.InvariantCulture, "Return: {0} {1} {2} · ≈{3:0.0} L of {4:0.0} L{5}",
                bestName, Distance(px, pz, bestX, bestZ), Bearing(px, pz, bestX, bestZ), liters, s.FuelLiters,
                m.ReturnFuelWarning ? " — not enough fuel, use a fuel can or call rescue" : string.Empty);
        }

        static void Describe(CampaignSession s, MapMarker marker, float px, float pz, MapScreenModel m)
        {
            m.SelectionTitle = marker.Label;
            string where = marker.Kind == MapMarkerKind.Player ? string.Empty
                : $" · {Distance(px, pz, marker.X, marker.Z)} {Bearing(px, pz, marker.X, marker.Z)}";
            switch (marker.Kind)
            {
                case MapMarkerKind.Player:
                    m.SelectionTitle = "You are here";
                    m.SelectionBody = string.Format(CultureInfo.InvariantCulture, "Heading {0} · {1:0.0} km driven this expedition",
                        Compass(marker.YawDegrees), s.Expedition.Stats.DistanceMeters / 1000f);
                    break;
                case MapMarkerKind.Garage:
                    m.SelectionBody = "Safe hub — bank cargo, refuel, repair, research" + where;
                    break;
                case MapMarkerKind.GarageLocked:
                    m.SelectionBody = "Not restored yet" + where;
                    break;
                case MapMarkerKind.CampActive:
                    m.SelectionBody = string.Format(CultureInfo.InvariantCulture,
                        "Safe hub — save and deposit ({0:0} kg camp storage){1}", s.CampStoreCapacityKg(marker.Id), where);
                    break;
                case MapMarkerKind.CampSite:
                    m.SelectionBody = "Camp socket — establish with a camp kit" + where;
                    break;
                case MapMarkerKind.LandmarkDiscovered:
                    m.SelectionBody = $"Discovered · {DiscoveryService.LandmarkDiscoveryRewardSalvage} Salvage reward earned" + where;
                    break;
                case MapMarkerKind.LandmarkSuspected:
                    m.SelectionBody = "Seen but not identified — stay within sight below 15 km/h for 2 s" + where;
                    break;
                case MapMarkerKind.CacheOpened:
                    m.SelectionBody = "Already collected" + where;
                    break;
                case MapMarkerKind.Relay:
                    m.SelectionBody = "Restored — part of the relay chain" + where;
                    break;
                case MapMarkerKind.RelayOffline:
                    m.SelectionBody = "Offline — needs a relay fuse" + where;
                    break;
                case MapMarkerKind.Objective:
                case MapMarkerKind.ObjectiveArea:
                    m.SelectionTitle = $"{marker.MissionCode} {s.Content.MissionName(marker.MissionCode)}";
                    string reward = s.Missions.Definitions.TryGetValue(marker.MissionCode, out var def) && def.RewardSalvage > 0
                        ? $" · reward {def.RewardSalvage} Salvage"
                        : string.Empty;
                    m.SelectionBody = marker.Label + (marker.Kind == MapMarkerKind.ObjectiveArea
                        ? " — somewhere inside the search area" : string.Empty) + reward + where;
                    break;
                case MapMarkerKind.Waypoint:
                    m.SelectionBody = "Your marker" + (marker.Tracked ? " · route target" : string.Empty) + where;
                    break;
            }
        }

        static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static string Distance(float ax, float az, float bx, float bz)
        {
            float d = Dist(ax, az, bx, bz);
            return d >= 1000f
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.0} km", d / 1000f)
                : string.Format(CultureInfo.InvariantCulture, "{0:0} m", Math.Round(d / 10f) * 10f);
        }

        /// <summary>8-point compass bearing from a to b (x east, z north).</summary>
        public static string Bearing(float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            if (dx * dx + dz * dz < 1f) return "here";
            double degrees = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            return Compass((float)degrees);
        }

        /// <summary>Unity yaw / compass degrees (0 = north, 90 = east) → N, NE, … NW.</summary>
        public static string Compass(float degrees)
        {
            string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            double d = ((degrees % 360.0) + 360.0) % 360.0;
            return points[(int)Math.Round(d / 45.0) % 8];
        }
    }
}
