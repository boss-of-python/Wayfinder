using System;
using System.Collections.Generic;
using Wayfinder.Core;
using Wayfinder.Progression;
using Wayfinder.World;

namespace Wayfinder.Campaign
{
    public enum MapMarkerKind : byte
    {
        Player,
        Garage,
        GarageLocked,
        CampActive,
        CampSite,
        LandmarkDiscovered,
        LandmarkSuspected,
        CacheOpened,
        Relay,
        RelayOffline,
        Objective,
        ObjectiveArea,
        Waypoint
    }

    /// <summary>Map filter groups (§13.4 [Filter]). The player arrow is always drawn.</summary>
    [Flags]
    public enum MapLayers
    {
        None = 0,
        SafeHubs = 1,
        Landmarks = 2,
        Caches = 4,
        Infrastructure = 8,
        Objectives = 16,
        Waypoints = 32,
        All = SafeHubs | Landmarks | Caches | Infrastructure | Objectives | Waypoints
    }

    public readonly struct MapMarker
    {
        public readonly MapMarkerKind Kind;
        public readonly string Id;
        public readonly string Label;
        public readonly float X, Z;
        /// <summary>Search-area radius (ObjectiveArea); 0 for point markers.</summary>
        public readonly float RadiusMeters;
        public readonly float YawDegrees;
        /// <summary>The HUD-tracked objective, the turn-in garage, or the route-target waypoint.</summary>
        public readonly bool Tracked;
        /// <summary>Mission display code for objective markers, else empty.</summary>
        public readonly string MissionCode;

        public MapMarker(MapMarkerKind kind, string id, string label, float x, float z, float radius = 0f,
            float yaw = 0f, bool tracked = false, string missionCode = null)
        {
            Kind = kind;
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
            X = x;
            Z = z;
            RadiusMeters = radius;
            YawDegrees = yaw;
            Tracked = tracked;
            MissionCode = missionCode ?? string.Empty;
        }

        public MapLayers Layer => LayerOf(Kind);

        public static MapLayers LayerOf(MapMarkerKind kind)
        {
            switch (kind)
            {
                case MapMarkerKind.Garage:
                case MapMarkerKind.GarageLocked:
                case MapMarkerKind.CampActive:
                case MapMarkerKind.CampSite:
                    return MapLayers.SafeHubs;
                case MapMarkerKind.LandmarkDiscovered:
                case MapMarkerKind.LandmarkSuspected:
                    return MapLayers.Landmarks;
                case MapMarkerKind.CacheOpened:
                    return MapLayers.Caches;
                case MapMarkerKind.Relay:
                case MapMarkerKind.RelayOffline:
                    return MapLayers.Infrastructure;
                case MapMarkerKind.Objective:
                case MapMarkerKind.ObjectiveArea:
                    return MapLayers.Objectives;
                case MapMarkerKind.Waypoint:
                    return MapLayers.Waypoints;
                default:
                    return MapLayers.None; // player: always shown
            }
        }
    }

    /// <summary>
    /// What the map may show (§1 "map icons never reveal everything in advance", §7.6 route
    /// spoilers, §8.2–8.3 search areas, §13.4 safe hubs/markers). Rules:
    /// <list type="bullet">
    /// <item>Unopened caches are NEVER shown, even inside revealed cells; opened caches are.</item>
    /// <item>Undiscovered landmarks appear only as "unconfirmed" once their cell is revealed
    /// (the silhouette has been seen); discovered ones show their name.</item>
    /// <item>Undiscovered landmark objectives show a search circle that contains, but is not
    /// centred on, the landmark (deterministic offset ≤ 45 % of the radius).</item>
    /// <item>Mission-given sites (inspect target, next ford checkpoint, camp socket, quest item,
    /// relay) are pinned while their objective is pending — the briefing names the place.</item>
    /// <item>Unlocked garages and activated camps (safe hubs) are always shown; locked garages,
    /// inactive camp sockets and offline relays once their cell is revealed.</item>
    /// </list>
    /// Only slice content is considered so the map never hints at sites the scene does not contain.
    /// </summary>
    public static class MapMarkerRules
    {
        public const float SearchAreaRadiusMeters = 110f;
        public const float SearchAreaMaxOffsetFraction = 0.45f;

        public static void Build(CampaignSession s, List<MapMarker> into, MapLayers layers = MapLayers.All)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            var slice = s.Content.Slice;
            var tracked = TrackedObjective(s, out string turnInHub);

            if ((layers & MapLayers.SafeHubs) != 0)
            {
                var garageIds = new List<string>(s.Content.Garages.Keys);
                garageIds.Sort(StringComparer.Ordinal);
                foreach (var id in garageIds)
                {
                    var p = s.Content.Garages[id].Position;
                    bool unlocked = s.Garage.IsGarageUnlocked(id);
                    if (!unlocked && !IsRevealed(s, p.X, p.Z)) continue;
                    into.Add(new MapMarker(unlocked ? MapMarkerKind.Garage : MapMarkerKind.GarageLocked, id,
                        DisplayNames.Garage(id), p.X, p.Z, tracked: unlocked && id == turnInHub));
                }
                foreach (var camp in slice.CampSockets)
                {
                    bool active = s.Garage.IsCampActivated(camp.Id);
                    if (!active && !IsRevealed(s, camp.Position.X, camp.Position.Z)) continue;
                    into.Add(new MapMarker(active ? MapMarkerKind.CampActive : MapMarkerKind.CampSite, camp.Id,
                        CampName(camp.Id), camp.Position.X, camp.Position.Z));
                }
            }

            if ((layers & MapLayers.Landmarks) != 0)
                foreach (var l in slice.Landmarks)
                {
                    if (!s.Content.Landmarks.TryGetValue(l.Id, out var def)) continue;
                    if (s.Discovery.IsLandmarkDiscovered(l.Id))
                        into.Add(new MapMarker(MapMarkerKind.LandmarkDiscovered, l.Id, def.Name, def.Position.X, def.Position.Z));
                    else if (IsRevealed(s, def.Position.X, def.Position.Z))
                        into.Add(new MapMarker(MapMarkerKind.LandmarkSuspected, l.Id, "Unconfirmed landmark",
                            def.Position.X, def.Position.Z));
                }

            if ((layers & MapLayers.Caches) != 0)
                foreach (var cacheId in slice.CacheIds)
                {
                    // §7.6: an unopened cache is never drawn, however much of the map is revealed.
                    if (!s.Discovery.IsCacheCollected(cacheId) || !s.Content.Caches.TryGetValue(cacheId, out var cache)) continue;
                    into.Add(new MapMarker(MapMarkerKind.CacheOpened, cacheId, "Opened cache", cache.Position.X, cache.Position.Z));
                }

            if ((layers & MapLayers.Infrastructure) != 0)
                foreach (var relay in slice.Relays)
                {
                    bool restored = s.Garage.HasInfrastructure(relay.Id);
                    if (!restored && !IsRevealed(s, relay.Position.X, relay.Position.Z)) continue;
                    into.Add(new MapMarker(restored ? MapMarkerKind.Relay : MapMarkerKind.RelayOffline, relay.Id,
                        restored ? "Relay (restored)" : "Relay mast (offline)", relay.Position.X, relay.Position.Z));
                }

            if ((layers & MapLayers.Objectives) != 0)
                AddObjectives(s, into, tracked);

            if ((layers & MapLayers.Waypoints) != 0)
                foreach (var m in s.MapMarkers.Markers)
                    into.Add(new MapMarker(MapMarkerKind.Waypoint, m.Id, m.Label, m.X, m.Z,
                        tracked: m.Id == s.MapMarkers.RouteTargetId));

            var pose = s.Expedition.IsActive && s.Expedition.Pose.IsSet ? s.Expedition.Pose : s.ResumePose;
            into.Add(new MapMarker(MapMarkerKind.Player, "player", "You", pose.Position.X, pose.Position.Z, yaw: pose.YawDegrees));
        }

        /// <summary>Deterministic search-circle centre: inside 45 % of the radius from the true spot.</summary>
        public static (float x, float z) SearchAreaCenter(string targetId, float trueX, float trueZ,
            float radiusMeters = SearchAreaRadiusMeters)
        {
            ulong h = StableHash.Hash64("map.search." + (targetId ?? string.Empty));
            double angle = (h & 0xFFFFFF) / (double)0x1000000 * Math.PI * 2.0;
            double fraction = 0.25 + 0.75 * (((h >> 24) & 0xFFFF) / 65536.0); // 25–100 % of the max offset
            double offset = radiusMeters * SearchAreaMaxOffsetFraction * fraction;
            return ((float)(trueX + Math.Sin(angle) * offset), (float)(trueZ + Math.Cos(angle) * offset));
        }

        public static bool IsRevealed(CampaignSession s, float x, float z)
        {
            if (!WorldConstants.InBounds(x, z)) return false;
            var (cx, cz) = WorldConstants.MapCellOf(x, z);
            return s.Discovery.RevealedCells.Get(cx, cz);
        }

        public static string CampName(string campId)
        {
            if (string.IsNullOrEmpty(campId)) return "Camp";
            var parts = campId.Split('.');
            if (parts.Length == 3 && parts[0] == "camp" && parts[2].Length > 0)
                return char.ToUpperInvariant(parts[1][0]) + parts[1].Substring(1) + " Camp " + parts[2].ToUpperInvariant();
            return DisplayNames.Item(campId);
        }

        /// <summary>The objective the HUD tracks (same rule as <see cref="ObjectiveText.TrackedLine"/>).</summary>
        static (string code, int index) TrackedObjective(CampaignSession s, out string turnInHub)
        {
            turnInHub = string.Empty;
            var ready = s.Missions.CodesInState(MissionState.ReadyToTurnIn);
            if (ready.Count > 0)
            {
                turnInHub = s.Missions.Definitions[ready[0]].TurnInHub.Value;
                return (string.Empty, -1);
            }
            foreach (var code in s.Missions.CodesInState(MissionState.Active))
            {
                var def = s.Missions.Definitions[code];
                var counters = s.Missions.ObjectiveCounters(code);
                for (int i = 0; i < def.Objectives.Count && i < counters.Length; i++)
                    if (counters[i] < def.Objectives[i].Count) return (code, i);
            }
            return (string.Empty, -1);
        }

        static void AddObjectives(CampaignSession s, List<MapMarker> into, (string code, int index) tracked)
        {
            var slice = s.Content.Slice;
            foreach (var code in s.Missions.CodesInState(MissionState.Active))
            {
                var def = s.Missions.Definitions[code];
                var counters = s.Missions.ObjectiveCounters(code);
                for (int i = 0; i < def.Objectives.Count && i < counters.Length; i++)
                {
                    var o = def.Objectives[i];
                    if (counters[i] >= o.Count) continue;
                    bool isTracked = tracked.code == code && tracked.index == i;
                    string target = o.TargetId.Value;
                    string label = ObjectiveText.Describe(o, s.Content);
                    switch (o.Type)
                    {
                        case MissionConditionType.DiscoverLandmark:
                            if (s.Discovery.IsLandmarkDiscovered(target) || !s.Content.Landmarks.TryGetValue(target, out var l)) break;
                            var (ax, az) = SearchAreaCenter(target, l.Position.X, l.Position.Z);
                            into.Add(new MapMarker(MapMarkerKind.ObjectiveArea, target, label, ax, az,
                                SearchAreaRadiusMeters, tracked: isTracked, missionCode: code));
                            break;
                        case MissionConditionType.InspectObject:
                            AddSite(into, slice.FindSite(slice.InspectTargets, target), label, isTracked, code);
                            break;
                        case MissionConditionType.EstablishCamp:
                            AddSite(into, slice.FindSite(slice.CampSockets, target), label, isTracked, code);
                            break;
                        case MissionConditionType.ActivateRelay:
                            AddSite(into, slice.FindSite(slice.Relays, target), label, isTracked, code);
                            break;
                        case MissionConditionType.CollectQuestItem:
                            foreach (var site in slice.QuestItemSites)
                                if (site.ItemId == target && !s.Discovery.IsCacheCollected(site.Id))
                                    AddSite(into, site, label, isTracked, code);
                            break;
                        case MissionConditionType.TraverseCheckpoints:
                            foreach (var route in slice.CheckpointRoutes)
                            {
                                if (route.Id != target) continue;
                                int next = s.NextCheckpointIndex(route.Id);
                                if (next < 0 || next >= route.Checkpoints.Count) continue;
                                var cp = route.Checkpoints[next];
                                into.Add(new MapMarker(MapMarkerKind.Objective, route.Id + "#" + next,
                                    $"{label} ({next + 1}/{route.Checkpoints.Count})", cp.X, cp.Z, tracked: isTracked, missionCode: code));
                            }
                            break;
                    }
                }
            }
        }

        static void AddSite(List<MapMarker> into, SliceSite site, string label, bool tracked, string code)
        {
            if (site == null) return;
            into.Add(new MapMarker(MapMarkerKind.Objective, site.Id, label, site.Position.X, site.Position.Z,
                tracked: tracked, missionCode: code));
        }
    }
}
