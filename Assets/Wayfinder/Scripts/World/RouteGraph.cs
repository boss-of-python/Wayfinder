using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.World
{
    /// <summary>
    /// Capability bands for authored routes (§5.4): gentle &lt;15°, demanding 15–25°,
    /// technical 25–32°, expert optional 32–38°. Sustained &gt;38° is excluded from mandatory
    /// routes. Required side slopes stay below 22° with full cargo.
    /// </summary>
    public enum CapabilityBand
    {
        Gentle = 0,       // < 15°
        Demanding = 1,    // 15–25°
        Technical = 2,    // 25–32°
        ExpertOptional = 3 // 32–38°
    }

    public static class CapabilityBands
    {
        public const float GentleMaxDegrees = 15f;
        public const float DemandingMaxDegrees = 25f;
        public const float TechnicalMaxDegrees = 32f;
        public const float ExpertMaxDegrees = 38f;
        public const float RequiredRouteSideSlopeLimitDegrees = 22f;

        public static CapabilityBand FromSlopeDegrees(float slopeDegrees)
        {
            if (slopeDegrees < GentleMaxDegrees) return CapabilityBand.Gentle;
            if (slopeDegrees < DemandingMaxDegrees) return CapabilityBand.Demanding;
            if (slopeDegrees < TechnicalMaxDegrees) return CapabilityBand.Technical;
            return CapabilityBand.ExpertOptional;
        }

        public static bool MandatoryRouteAllowed(float slopeDegrees) => slopeDegrees < ExpertMaxDegrees;
    }

    /// <summary>
    /// Authored road/route graph (§7.5). Nodes are world positions; edges carry capability band,
    /// surface and infrastructure locks (e.g. bridges opened by story materials — never currency
    /// or timers). Used for route estimates ("estimate", never a guarantee — §5.1) and for
    /// validated reachability.
    /// </summary>
    public sealed class RouteGraph
    {
        public sealed class Node
        {
            public ContentId Id;
            public float X;
            public float Z;
            public readonly List<Edge> Edges = new List<Edge>();
        }

        public sealed class Edge
        {
            public Node A;
            public Node B;
            public CapabilityBand Band;
            public SurfaceType Surface = SurfaceType.PackedDirt;
            /// <summary>Infrastructure flag required to traverse (e.g. "bridge.canyon.01"). Empty = open.</summary>
            public string RequiredInfrastructureFlag = string.Empty;
            public bool Muted; // scenic only, not a route edge
            public float LengthMeters => (float)Math.Sqrt(
                (B.X - A.X) * (B.X - A.X) + (B.Z - A.Z) * (B.Z - A.Z));
        }

        readonly Dictionary<string, Node> _nodes = new Dictionary<string, Node>(StringComparer.Ordinal);

        public int NodeCount => _nodes.Count;

        public Node GetOrAddNode(string id, float x, float z)
        {
            if (_nodes.TryGetValue(id, out var node)) return node;
            node = new Node { Id = new ContentId(id), X = x, Z = z };
            _nodes[id] = node;
            return node;
        }

        public Edge Connect(string aId, string bId, CapabilityBand band, SurfaceType surface,
            string requiredInfrastructureFlag = "")
        {
            var a = _nodes[aId];
            var b = _nodes[bId];
            var edge = new Edge
            {
                A = a, B = b, Band = band, Surface = surface,
                RequiredInfrastructureFlag = requiredInfrastructureFlag ?? string.Empty
            };
            a.Edges.Add(edge);
            b.Edges.Add(edge);
            return edge;
        }

        public bool TryGetNode(string id, out Node node) => _nodes.TryGetValue(id, out node);

        /// <summary>
        /// Dijkstra shortest path under the player's current capability band and unlocked
        /// infrastructure. Returns estimated length (report as ESTIMATE only).
        /// </summary>
        public bool TryEstimateRoute(string fromId, string toId,
            CapabilityBand vehicleBand, ISet<string> unlockedInfrastructureFlags,
            out float estimatedMeters, out List<string> pathNodeIds)
        {
            estimatedMeters = 0f;
            pathNodeIds = new List<string>();
            if (!_nodes.TryGetValue(fromId, out var start) || !_nodes.TryGetValue(toId, out var goal))
                return false;

            var dist = new Dictionary<string, float>(StringComparer.Ordinal);
            var prev = new Dictionary<string, string>(StringComparer.Ordinal);
            var frontier = new SortedSet<(float d, string id)>();
            foreach (var kv in _nodes) dist[kv.Key] = float.PositiveInfinity;
            dist[start.Id.Value] = 0f;
            frontier.Add((0f, start.Id.Value));

            while (frontier.Count > 0)
            {
                var (d, id) = frontier.Min;
                frontier.Remove(frontier.Min);
                if (id == goal.Id.Value) break;
                if (d > dist[id]) continue;
                var node = _nodes[id];
                foreach (var edge in node.Edges)
                {
                    if (edge.Muted) continue;
                    if ((int)edge.Band > (int)vehicleBand) continue;
                    if (!string.IsNullOrEmpty(edge.RequiredInfrastructureFlag) &&
                        (unlockedInfrastructureFlags == null || !unlockedInfrastructureFlags.Contains(edge.RequiredInfrastructureFlag)))
                        continue;

                    string other = edge.A.Id.Value == id ? edge.B.Id.Value : edge.A.Id.Value;
                    float nd = d + edge.LengthMeters;
                    if (nd < dist[other])
                    {
                        dist[other] = nd;
                        prev[other] = id;
                        frontier.Add((nd, other));
                    }
                }
            }

            if (float.IsInfinity(dist[goal.Id.Value])) return false;
            estimatedMeters = dist[goal.Id.Value];

            string cursor = goal.Id.Value;
            while (cursor != null)
            {
                pathNodeIds.Add(cursor);
                prev.TryGetValue(cursor, out cursor);
            }
            pathNodeIds.Reverse();
            return true;
        }
    }

    /// <summary>
    /// Route validation (§7.2 step 10): every required route and objective must be solvable
    /// against vehicle capability profiles in prerequisite order. A world with mandatory-route
    /// errors cannot ship.
    /// </summary>
    public static class RouteValidator
    {
        public sealed class Report
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public bool Ok => Errors.Count == 0;
        }

        public static Report ValidateMandatoryRoutes(
            RouteGraph graph,
            IEnumerable<(string from, string to, string label, CapabilityBand requiredBand)> mandatoryRoutes,
            Func<string, ISet<string>> infrastructureAtStoryPoint)
        {
            var report = new Report();
            foreach (var route in mandatoryRoutes)
            {
                var flags = infrastructureAtStoryPoint?.Invoke(route.label) ?? new HashSet<string>();
                if (!graph.TryEstimateRoute(route.from, route.to, route.requiredBand, flags, out _, out var path))
                {
                    report.Errors.Add($"mandatory route '{route.label}' {route.from} → {route.to} unreachable");
                    continue;
                }
                if (path.Count == 0)
                    report.Warnings.Add($"mandatory route '{route.label}' produced empty path");
            }
            return report;
        }

        /// <summary>Sustained slopes above 38° must never appear on mandatory routes (§5.4).</summary>
        public static bool ValidateSlopeBand(CapabilityBand band, bool mandatory)
        {
            if (!mandatory) return true;
            return band != CapabilityBand.ExpertOptional;
        }
    }

    /// <summary>
    /// Weather/time generation (§7.7): weather sim at 1 Hz with visual interpolation.
    /// Water bodies may only switch between prevalidated low/high states; every mandatory
    /// objective must have a route valid at both states (§6.2 D).
    /// </summary>
    public sealed class WeatherSystem
    {
        public const float SimulationHz = 1f;

        public WeatherState State { get; private set; } = WeatherState.Clear;
        public float TransitionBlend { get; private set; } = 1f;
        public bool IsWet { get; private set; }
        public float WaterLevelState { get; private set; } // 0 = low, 1 = high

        float _accum;
        readonly StableRandom _random;

        public WeatherSystem(int weatherSeed)
        {
            _random = new StableRandom((ulong)(uint)weatherSeed);
        }

        public void Step(float simulationDeltaSeconds)
        {
            _accum += simulationDeltaSeconds;
            if (_accum < 1f / SimulationHz) return;
            // DEF-024: resetting the accumulator to zero dropped the overshoot, so a 60 Hz caller
            // ticked at 1/60 s intervals late every second and weather drifted ~1 % per hour.
            _accum -= 1f / SimulationHz;
            TickCount++;

            // Simple deterministic markov-ish weather cycle; never hostile-random punishment (§9.4).
            if (_random.Chance(0.02))
            {
                State = (WeatherState)_random.Range(0, 7);
                TransitionBlend = 0f;
            }
            TransitionBlend = MathUtil.Clamp01(TransitionBlend + 0.1f);
            IsWet = State == WeatherState.Rain || State == WeatherState.Storm || State == WeatherState.Fog;

            if (_random.Chance(0.01))
                WaterLevelState = _random.Chance(0.5) ? 1f : 0f; // prevalidated low/high only
        }

        /// <summary>Surface wet multiplier source for SurfaceTable blends.</summary>
        public bool SurfaceWet => IsWet;

        /// <summary>Number of 1 Hz ticks simulated so far — the observable that pins DEF-024.</summary>
        public int TickCount { get; private set; }
    }
}
