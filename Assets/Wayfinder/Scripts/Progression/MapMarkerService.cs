using System;
using System.Collections.Generic;
using System.Globalization;
using Wayfinder.Core;
using Wayfinder.World;

namespace Wayfinder.Progression
{
    /// <summary>A player-placed map marker (§13.4 "long-press custom marker with confirm").</summary>
    public readonly struct CustomMapMarker
    {
        public readonly string Id;
        public readonly float X, Z;
        public readonly string Label;

        public CustomMapMarker(string id, float x, float z, string label)
        {
            Id = id ?? string.Empty;
            X = x;
            Z = z;
            Label = label ?? string.Empty;
        }
    }

    /// <summary>
    /// Up to ten custom map markers plus the optional route target (§13.4). Marker ids are stable
    /// (<c>marker.N</c>, N never reused within a save) so a save/load or a replayed command can
    /// never retarget a different marker. Markers are player annotations, not rewards: placing or
    /// removing one never touches currency, cells or discoveries.
    /// </summary>
    public sealed class MapMarkerService
    {
        public const int MaxMarkers = 10;
        public const int MaxLabelLength = 24;
        public const string IdPrefix = "marker.";

        readonly List<CustomMapMarker> _markers = new List<CustomMapMarker>();

        public IReadOnlyList<CustomMapMarker> Markers => _markers;
        public int Count => _markers.Count;
        public bool IsFull => _markers.Count >= MaxMarkers;
        /// <summary>Next id suffix; persisted so a removed marker's id is never handed out again.</summary>
        public int NextIndex { get; private set; } = 1;
        /// <summary>Id of the marker the route line points at, or empty.</summary>
        public string RouteTargetId { get; private set; } = string.Empty;

        public CommandResult TryAdd(float x, float z, string label, out CustomMapMarker marker)
        {
            marker = default;
            if (float.IsNaN(x) || float.IsNaN(z) || !WorldConstants.InBounds(x, z))
                return CommandResult.Fail(FailureReason.InvalidTarget, "markers must be placed inside the mainland");
            if (IsFull)
                return CommandResult.Fail(FailureReason.OverCapacity,
                    $"marker limit reached ({MaxMarkers}) — remove one first");
            string id = IdPrefix + NextIndex.ToString(CultureInfo.InvariantCulture);
            marker = new CustomMapMarker(id, x, z, CleanLabel(label, NextIndex));
            NextIndex++;
            _markers.Add(marker);
            return CommandResult.Ok();
        }

        public CommandResult Remove(string markerId)
        {
            int index = IndexOf(markerId);
            if (index < 0) return CommandResult.Fail(FailureReason.ItemNotFound, $"no marker '{markerId}'");
            _markers.RemoveAt(index);
            if (RouteTargetId == markerId) RouteTargetId = string.Empty;
            return CommandResult.Ok();
        }

        /// <summary>Points the route line at a marker; an empty id clears the route (§13.4 "Clear route").</summary>
        public CommandResult SetRouteTarget(string markerId)
        {
            if (string.IsNullOrEmpty(markerId))
            {
                RouteTargetId = string.Empty;
                return CommandResult.Ok();
            }
            if (IndexOf(markerId) < 0) return CommandResult.Fail(FailureReason.ItemNotFound, $"no marker '{markerId}'");
            RouteTargetId = markerId;
            return CommandResult.Ok();
        }

        public bool TryGet(string markerId, out CustomMapMarker marker)
        {
            int index = IndexOf(markerId);
            marker = index >= 0 ? _markers[index] : default;
            return index >= 0;
        }

        /// <summary>
        /// Tolerant restore: drops malformed, duplicate or out-of-bounds entries and anything past the
        /// limit, and lifts <see cref="NextIndex"/> above every surviving id so ids stay unique.
        /// </summary>
        public void Restore(IEnumerable<CustomMapMarker> markers, int nextIndex, string routeTargetId)
        {
            _markers.Clear();
            NextIndex = Math.Max(1, nextIndex);
            if (markers != null)
                foreach (var m in markers)
                {
                    if (_markers.Count >= MaxMarkers) break;
                    if (!TryParseIndex(m.Id, out int suffix) || IndexOf(m.Id) >= 0) continue;
                    if (float.IsNaN(m.X) || float.IsNaN(m.Z) || !WorldConstants.InBounds(m.X, m.Z)) continue;
                    _markers.Add(new CustomMapMarker(m.Id, m.X, m.Z, CleanLabel(m.Label, suffix)));
                    if (suffix >= NextIndex) NextIndex = suffix + 1;
                }
            RouteTargetId = !string.IsNullOrEmpty(routeTargetId) && IndexOf(routeTargetId) >= 0 ? routeTargetId : string.Empty;
        }

        int IndexOf(string markerId)
        {
            if (string.IsNullOrEmpty(markerId)) return -1;
            for (int i = 0; i < _markers.Count; i++)
                if (string.Equals(_markers[i].Id, markerId, StringComparison.Ordinal)) return i;
            return -1;
        }

        static bool TryParseIndex(string id, out int index)
        {
            index = 0;
            return !string.IsNullOrEmpty(id) && id.StartsWith(IdPrefix, StringComparison.Ordinal) &&
                   int.TryParse(id.Substring(IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
                   index > 0;
        }

        static string CleanLabel(string label, int index)
        {
            string trimmed = (label ?? string.Empty).Trim();
            var chars = new System.Text.StringBuilder(Math.Min(trimmed.Length, MaxLabelLength));
            foreach (char c in trimmed)
            {
                if (chars.Length >= MaxLabelLength) break;
                if (!char.IsControl(c)) chars.Append(c);
            }
            return chars.Length > 0 ? chars.ToString() : "Waypoint " + index.ToString(CultureInfo.InvariantCulture);
        }
    }
}
