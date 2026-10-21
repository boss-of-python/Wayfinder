using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Wayfinder.Core;

namespace Wayfinder.World
{
    public enum RoadClass
    {
        Primary = 0,
        Secondary = 1,
        Track = 2
    }

    public sealed class AuthoredRoutePolyline
    {
        public string Id = string.Empty;
        public string FromNodeId = string.Empty;
        public string ToNodeId = string.Empty;
        public RoadClass Class = RoadClass.Secondary;
        public CapabilityBand Band = CapabilityBand.Demanding;
        public string RequiredInfrastructureFlag = string.Empty;
        public float LengthMeters;
        public float RawLengthMeters;
        public float MaxGradeDegrees;
        public float LowWaterDepthMeters;
        public float HighWaterDepthMeters;
        public bool SingleTankSafe = true;
        public readonly List<(float x, float y, float z)> Waypoints = new List<(float, float, float)>();
    }

    /// <summary>
    /// Full five-biome mainland route network (§6.2, KI-030) parsed from Content/routes.json with a
    /// zero-dependency reader so <c>Wayfinder.World</c> requires only <c>Wayfinder.Core</c>.
    /// Builds <see cref="RouteGraph"/> + <see cref="RouteValidator"/> reports across both Coastal
    /// Wetlands water states, computes water- and infrastructure-aware shortest paths, and provides
    /// O(1)-bucketed road-edge distance queries for scatter, surface, and map systems.
    /// </summary>
    public sealed class MainlandRouteGraph
    {
        const float BucketMeters = 256f;
        const float HalfRoadWidthPrimary = 5.5f;
        const float HalfRoadWidthSecondary = 4.0f;
        const float HalfRoadWidthTrack = 3.0f;

        readonly List<(string id, float x, float z)> _nodes = new List<(string, float, float)>();
        readonly List<AuthoredRoutePolyline> _polylines = new List<AuthoredRoutePolyline>();

        struct SegmentRef
        {
            public float Ax, Az, Bx, Bz, HalfWidth;
            public RoadClass Class;
        }

        readonly Dictionary<int, List<SegmentRef>> _spatialBuckets = new Dictionary<int, List<SegmentRef>>();

        public IReadOnlyList<AuthoredRoutePolyline> Polylines => _polylines;
        public IReadOnlyList<(string id, float x, float z)> Nodes => _nodes;

        public static MainlandRouteGraph FromFile(string routesJsonPath) =>
            FromJson(File.ReadAllText(routesJsonPath));

        public static MainlandRouteGraph FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new ArgumentException("routes json required");
            int pos = 0;
            var root = (Dictionary<string, object>)ReadValue(json, ref pos);
            var net = new MainlandRouteGraph();

            if (root.TryGetValue("nodes", out var nodesObj) && nodesObj is List<object> nodesList)
            {
                foreach (Dictionary<string, object> n in nodesList)
                {
                    var p = (Dictionary<string, object>)n["position"];
                    net._nodes.Add((Str(n, "id"), Num(p, "x"), Num(p, "z")));
                }
            }

            if (root.TryGetValue("edges", out var edgesObj) && edgesObj is List<object> edgesList)
            {
                foreach (Dictionary<string, object> e in edgesList)
                {
                    var poly = new AuthoredRoutePolyline
                    {
                        Id = Str(e, "id"),
                        FromNodeId = Str(e, "from"),
                        ToNodeId = Str(e, "to"),
                        Class = (RoadClass)Enum.Parse(typeof(RoadClass), Str(e, "roadClass", "Secondary")),
                        Band = (CapabilityBand)Enum.Parse(typeof(CapabilityBand), Str(e, "capabilityBand", "Demanding")),
                        RequiredInfrastructureFlag = Str(e, "requiredInfrastructureFlag"),
                        LengthMeters = Num(e, "lengthMeters", 100f),
                        RawLengthMeters = Num(e, "rawLengthMeters", Num(e, "lengthMeters", 100f)),
                        MaxGradeDegrees = Num(e, "maxGradeDegrees"),
                        LowWaterDepthMeters = Num(e, "lowWaterDepthMeters"),
                        HighWaterDepthMeters = Num(e, "highWaterDepthMeters"),
                        SingleTankSafe = Bool(e, "singleTankSafe", true)
                    };
                    if (e.TryGetValue("waypoints", out var wpsObj) && wpsObj is List<object> wpsList)
                    {
                        foreach (Dictionary<string, object> wp in wpsList)
                            poly.Waypoints.Add((Num(wp, "x"), Num(wp, "y"), Num(wp, "z")));
                    }
                    net._polylines.Add(poly);
                    net.IndexPolyline(poly);
                }
            }
            return net;
        }

        void IndexPolyline(AuthoredRoutePolyline poly)
        {
            float halfW = poly.Class == RoadClass.Primary
                ? HalfRoadWidthPrimary
                : (poly.Class == RoadClass.Secondary ? HalfRoadWidthSecondary : HalfRoadWidthTrack);
            for (int i = 1; i < poly.Waypoints.Count; i++)
            {
                var a = poly.Waypoints[i - 1];
                var b = poly.Waypoints[i];
                var seg = new SegmentRef { Ax = a.x, Az = a.z, Bx = b.x, Bz = b.z, HalfWidth = halfW, Class = poly.Class };
                int minBx = Math.Max(0, (int)Math.Floor((Math.Min(a.x, b.x) - 128f) / BucketMeters));
                int maxBx = Math.Min(23, (int)Math.Floor((Math.Max(a.x, b.x) + 128f) / BucketMeters));
                int minBz = Math.Max(0, (int)Math.Floor((Math.Min(a.z, b.z) - 128f) / BucketMeters));
                int maxBz = Math.Min(23, (int)Math.Floor((Math.Max(a.z, b.z) + 128f) / BucketMeters));
                for (int bz = minBz; bz <= maxBz; bz++)
                for (int bx = minBx; bx <= maxBx; bx++)
                {
                    int key = bz * 24 + bx;
                    if (!_spatialBuckets.TryGetValue(key, out var list))
                        _spatialBuckets[key] = list = new List<SegmentRef>();
                    list.Add(seg);
                }
            }
        }

        public RouteGraph BuildGraph(
            WetlandWaterState waterState = WetlandWaterState.LowWater,
            float wadingCeilingMeters = 0.55f)
        {
            var graph = new RouteGraph();
            foreach (var (id, x, z) in _nodes)
                graph.GetOrAddNode(id, x, z);
            foreach (var p in _polylines)
            {
                float depth = waterState == WetlandWaterState.HighWater ? p.HighWaterDepthMeters : p.LowWaterDepthMeters;
                var edge = graph.Connect(
                    p.FromNodeId,
                    p.ToNodeId,
                    p.Band,
                    depth >= 0.12f ? SurfaceType.ShallowMud : SurfaceType.PackedDirt,
                    p.RequiredInfrastructureFlag);
                if (depth > wadingCeilingMeters || !p.SingleTankSafe)
                    edge.Muted = true;
            }
            return graph;
        }

        public float ShortestDistanceMeters(
            string fromId,
            string toId,
            CapabilityBand maxBand,
            WetlandWaterState waterState = WetlandWaterState.LowWater,
            ISet<string> unlockedInfrastructure = null,
            float wadingCeilingMeters = 0.55f)
        {
            var dist = new Dictionary<string, float>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (id, _, _) in _nodes)
                dist[id] = float.PositiveInfinity;
            if (!dist.ContainsKey(fromId) || !dist.ContainsKey(toId))
                return float.PositiveInfinity;
            dist[fromId] = 0f;

            while (true)
            {
                string best = null;
                float bestD = float.PositiveInfinity;
                foreach (var kv in dist)
                    if (!visited.Contains(kv.Key) && kv.Value < bestD) { bestD = kv.Value; best = kv.Key; }
                if (best == null || best == toId) break;
                visited.Add(best);

                foreach (var p in _polylines)
                {
                    string next = p.FromNodeId == best ? p.ToNodeId : (p.ToNodeId == best ? p.FromNodeId : null);
                    if (next == null || visited.Contains(next)) continue;
                    if ((int)p.Band > (int)maxBand || !p.SingleTankSafe) continue;
                    float depth = waterState == WetlandWaterState.HighWater ? p.HighWaterDepthMeters : p.LowWaterDepthMeters;
                    if (depth > wadingCeilingMeters) continue;
                    if (!string.IsNullOrEmpty(p.RequiredInfrastructureFlag) &&
                        (unlockedInfrastructure == null || !unlockedInfrastructure.Contains(p.RequiredInfrastructureFlag)))
                        continue;

                    float cand = bestD + p.RawLengthMeters;
                    if (cand < dist[next]) dist[next] = cand;
                }
            }
            return dist[toId];
        }

        public float DistanceToRoadEdgeMeters(float x, float z, out RoadClass nearestClass)
        {
            nearestClass = RoadClass.Track;
            int bx = Math.Max(0, Math.Min(23, (int)Math.Floor(x / BucketMeters)));
            int bz = Math.Max(0, Math.Min(23, (int)Math.Floor(z / BucketMeters)));
            float best = 999f;
            if (_spatialBuckets.TryGetValue(bz * 24 + bx, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    float edgeDist = Math.Max(0f, DistancePointToSegment(x, z, s.Ax, s.Az, s.Bx, s.Bz) - s.HalfWidth);
                    if (edgeDist < best)
                    {
                        best = edgeDist;
                        nearestClass = s.Class;
                    }
                }
            }
            return best;
        }

        static float DistancePointToSegment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float vx = bx - ax, vz = bz - az;
            float wx = px - ax, wz = pz - az;
            float len2 = vx * vx + vz * vz;
            float t = len2 <= 1e-4f ? 0f : MathUtil.Clamp01((wx * vx + wz * vz) / len2);
            float dx = px - (ax + vx * t), dz = pz - (az + vz * t);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Validates full mainland reachability under both LowWater and HighWater (§6.2, KI-029, KI-030).</summary>
        public RouteValidator.Report ValidateAll()
        {
            var combined = new RouteValidator.Report();
            var targets = new[]
            {
                "garage.canyon", "garage.city", "garage.wetlands", "garage.highlands",
                "relay.forest", "relay.canyon", "relay.city", "relay.wetlands", "relay.highlands",
                "site.highlands.observatory",
                "camp.forest.a", "camp.forest.b", "camp.canyon.a", "camp.canyon.b",
                "camp.city.a", "camp.city.b", "camp.wetlands.a", "camp.wetlands.b",
                "camp.highlands.a", "camp.highlands.b"
            };
            var mandatory = new List<(string from, string to, string label, CapabilityBand band)>();
            foreach (var t in targets)
                mandatory.Add(("garage.home", t, "mainland:" + t, CapabilityBand.Technical));

            foreach (var p in _polylines)
            {
                if (!RouteValidator.ValidateSlopeBand(p.Band, mandatory: true))
                    combined.Errors.Add($"edge '{p.Id}' uses non-mandatory slope band {p.Band}");
                if (!CapabilityBands.MandatoryRouteAllowed(p.MaxGradeDegrees))
                    combined.Errors.Add($"edge '{p.Id}' grade {p.MaxGradeDegrees:F1}° exceeds {CapabilityBands.ExpertMaxDegrees}°");
            }

            foreach (var state in new[] { WetlandWaterState.LowWater, WetlandWaterState.HighWater })
            {
                var graph = BuildGraph(state, wadingCeilingMeters: 0.55f);
                var rep = RouteValidator.ValidateMandatoryRoutes(graph, mandatory, _ => new HashSet<string>());
                foreach (var err in rep.Errors)
                    combined.Errors.Add($"[{state}] {err}");
            }
            return combined;
        }

        // ---- Minimal self-contained JSON value reader (keeps Wayfinder.World zero-dependency) ----
        static string Str(Dictionary<string, object> d, string k, string fallback = "") =>
            d.TryGetValue(k, out var v) && v is string s ? s : fallback;
        static float Num(Dictionary<string, object> d, string k, float fallback = 0f) =>
            d.TryGetValue(k, out var v) && v is double dv ? (float)dv : fallback;
        static bool Bool(Dictionary<string, object> d, string k, bool fallback = false) =>
            d.TryGetValue(k, out var v) && v is bool bv ? bv : fallback;

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ReadValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            char ch = s[i];
            if (ch == '{') return ReadObject(s, ref i);
            if (ch == '[') return ReadArray(s, ref i);
            if (ch == '"') return ReadString(s, ref i);
            if (ch == 't' || ch == 'f')
            {
                bool val = ch == 't';
                i += val ? 4 : 5;
                return val;
            }
            if (ch == 'n') { i += 4; return null; }
            int start = i;
            while (i < s.Length && ("-+.0123456789eE".IndexOf(s[i]) >= 0)) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // '{'
            SkipWs(s, ref i);
            if (s[i] == '}') { i++; return dict; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ReadString(s, ref i);
                SkipWs(s, ref i);
                i++; // ':'
                dict[key] = ReadValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i] == '}') { i++; break; }
                i++; // ','
            }
            return dict;
        }

        static List<object> ReadArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // '['
            SkipWs(s, ref i);
            if (s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ReadValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i] == ']') { i++; break; }
                i++; // ','
            }
            return list;
        }

        static string ReadString(string s, ref int i)
        {
            i++; // '"'
            var sb = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char esc = s[i++];
                    sb.Append(esc == 'n' ? '\n' : esc);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
