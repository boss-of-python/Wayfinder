using System.Collections.Generic;
using System;
using Wayfinder.Core;
using Wayfinder.Progression;

namespace Wayfinder.Campaign
{
    /// <summary>Exploration-map state owned by the session (§8.1, §13.4): terrain source and custom markers.</summary>
    public sealed partial class CampaignSession
    {
        IMapTerrainSource _mapTerrain;
        Func<float, float, float> _heightSampler;

        /// <summary>Up to ten player markers + route target; persisted in the save (D-027).</summary>
        public MapMarkerService MapMarkers { get; } = new MapMarkerService();

        /// <summary>
        /// Terrain used for reveal line-of-sight AND for drawing the map, so the map never shows
        /// relief the reveal ignored. Defaults to the slice greybox profile; the M3 terrain bake will
        /// supply baked heights here (KI-029). Runtime dependency only — never saved.
        /// </summary>
        public IMapTerrainSource MapTerrain
        {
            get => _mapTerrain ??= new SliceMapTerrain(Content.Slice);
            set
            {
                _mapTerrain = value;
                _heightSampler = null;
            }
        }

        Func<float, float, float> HeightSampler => _heightSampler ??= MapTerrain.HeightAt;

        /// <summary>
        /// §8.3: the passive sensor sweep. Signals are the world's placed sites — relays, quest items,
        /// inspect targets, deliveries and routes — and the radius is the resolved
        /// <c>SignalRadiusM</c>, so Radar I (140 m) and Radar II (220 m) actually extend what pulses.
        /// Before this the stat was composed by research and read by nothing. Pure: the observer
        /// position is a parameter, so the sweep is testable without a vehicle.
        /// </summary>
        public List<SensorLoop.Reading> SensorScan(float observerX, float observerZ)
        {
            var signals = new List<SensorLoop.Signal>();
            foreach (var site in Content.Sites.Values)
                signals.Add(new SensorLoop.Signal(site.Id, site.Position.X, site.Position.Z));
            return SensorLoop.Scan(signals, observerX, observerZ, ResolveStats().SignalRadiusM);
        }

        /// <summary>The same sweep from the vehicle's pose; empty when parked, since there is no field.</summary>
        public List<SensorLoop.Reading> SensorScan() =>
            Expedition.IsActive
                ? SensorScan(Expedition.Pose.Position.X, Expedition.Pose.Position.Z)
                : new List<SensorLoop.Reading>();

        /// <summary>
        /// Long-press → confirm → place (§13.4). Allowed in the garage and while the map pauses an
        /// expedition. Field-progress persistence: marks the autosave dirty, never a critical save.
        /// </summary>
        public CommandResult PlaceMapMarker(float x, float z, string label, out string markerId)
        {
            var result = MapMarkers.TryAdd(x, z, label, out var marker);
            markerId = result.Success ? marker.Id : string.Empty;
            if (result.Success) _autosave.MarkDirty();
            return result;
        }

        public CommandResult RemoveMapMarker(string markerId)
        {
            var result = MapMarkers.Remove(markerId);
            if (result.Success) _autosave.MarkDirty();
            return result;
        }

        /// <summary>Points the dashed route line at a marker; empty clears it (§13.4 [Clear route]).</summary>
        public CommandResult SetRouteTarget(string markerId)
        {
            if ((markerId ?? string.Empty) == MapMarkers.RouteTargetId) return CommandResult.Ok();
            var result = MapMarkers.SetRouteTarget(markerId);
            if (result.Success) _autosave.MarkDirty();
            return result;
        }
    }
}
