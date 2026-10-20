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

        int _mapRevision = 1;

        /// <summary>
        /// Monotonic counter of committed map changes: markers placed/removed, route target changed,
        /// cells revealed, landmarks discovered, caches collected, and a load (§16.3, SYS-WRD-002).
        /// <para>
        /// The map views used to poll the domain on a 0.25 s timer, so a placed marker could take a
        /// quarter of a second to appear and every poll rebuilt every icon whether anything had
        /// changed or not. A view now rebuilds when this number moves — a dirty flag driven by the
        /// commit, exactly like the other §16.3 events — and the timer is left only for the player
        /// arrow, which genuinely moves every frame.
        /// </para>
        /// </summary>
        public int MapRevision => _mapRevision;

        /// <summary>Marks map state dirty after a change that bypassed the domain (load, terrain swap).</summary>
        public void InvalidateMap() => _mapRevision++;

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
        /// Long-press → confirm → place (§13.4). Allowed in the garage and while the map pauses an
        /// expedition. Field-progress persistence: marks the autosave dirty, never a critical save.
        /// </summary>
        public CommandResult PlaceMapMarker(float x, float z, string label, out string markerId)
        {
            var result = MapMarkers.TryAdd(x, z, label, out var marker);
            markerId = result.Success ? marker.Id : string.Empty;
            if (result.Success)
            {
                _autosave.MarkDirty();
                _mapRevision++;
            }
            return result;
        }

        public CommandResult RemoveMapMarker(string markerId)
        {
            var result = MapMarkers.Remove(markerId);
            if (result.Success)
            {
                _autosave.MarkDirty();
                _mapRevision++;
            }
            return result;
        }

        /// <summary>Points the dashed route line at a marker; empty clears it (§13.4 [Clear route]).</summary>
        public CommandResult SetRouteTarget(string markerId)
        {
            if ((markerId ?? string.Empty) == MapMarkers.RouteTargetId) return CommandResult.Ok();
            var result = MapMarkers.SetRouteTarget(markerId);
            if (result.Success)
            {
                _autosave.MarkDirty();
                _mapRevision++;
            }
            return result;
        }
    }
}
