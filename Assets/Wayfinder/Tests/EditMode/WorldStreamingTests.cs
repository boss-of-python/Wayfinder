using System;
using System.Collections.Generic;
using NUnit.Framework;
using Wayfinder.Core;
using Wayfinder.World;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Fake analytic surface for scatter tests: flat ground at 10 m, one straight east–west road
    /// corridor centred on z = 1000 (half width 3.5 m), and an optional steep band and reserved disc.
    /// </summary>
    sealed class FlatTestSurface : IScatterSurface
    {
        public float Ground = 10f;
        public float SteepBandMinX = float.MaxValue;
        public float ReservedX = float.NaN, ReservedZ = float.NaN, ReservedRadius = 0f;

        public float Height(float x, float z) => Ground;

        public float SlopeDegrees(float x, float z) => x >= SteepBandMinX ? 40f : 0f;

        public float DistanceToRoadEdgeMeters(float x, float z) => Math.Abs(z - 1000f) - 3.5f;

        public bool IsReserved(float x, float z)
        {
            if (float.IsNaN(ReservedX)) return false;
            float dx = x - ReservedX, dz = z - ReservedZ;
            return dx * dx + dz * dz <= ReservedRadius * ReservedRadius;
        }
    }

    /// <summary>§7.6 scatter rules, §7.3 determinism, §7.4 chunk-border behaviour (ARCH/GEN-03).</summary>
    public class ScatterPlacerTests
    {
        static ScatterRules Trees(float spacing = 4f, float density = 1f, int layer = 11) =>
            ScatterRules.Trees("tree.test", layer, spacing, density);

        [Test]
        public void CellGrid_TilesEachChunkExactly_AndCellIsNeverSmallerThanSpacing()
        {
            foreach (float spacing in new[] { 3f, 4f, 5f, 6f, 7f, 8f })
            {
                int n = ScatterPlacer.CellsPerChunk(spacing);
                float cell = ScatterPlacer.CellSizeMeters(spacing);
                Assert.AreEqual(WorldConstants.ChunkSizeMeters, n * cell, 1e-3f, $"spacing {spacing}");
                Assert.GreaterOrEqual(cell, spacing, $"spacing {spacing}");
            }
            Assert.AreEqual(85, ScatterPlacer.CellsPerChunk(3f));
            Assert.AreEqual(32, ScatterPlacer.CellsPerChunk(8f));
        }

        [Test]
        public void Rules_RejectInvalidParameters()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScatterRules("x", 1, 0f, 1f, 30f, 0f, 10f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScatterRules("x", 1, 4f, 1.5f, 30f, 0f, 10f, 1f));
            Assert.Throws<ArgumentException>(() => new ScatterRules("", 1, 4f, 1f, 30f, 0f, 10f, 1f));
            Assert.Throws<ArgumentException>(() => new ScatterRules("x", 1, 4f, 1f, 30f, 10f, 0f, 1f));
        }

        [Test]
        public void PlaceChunk_IsDeterministic_AcrossCallsAndCallOrder()
        {
            var surface = new FlatTestSurface();
            var rules = Trees(4f, 0.8f);
            var first = ScatterPlacer.PlaceChunk(5, 6, rules, surface);
            // Interleave other chunks; results for (5,6) must not change.
            ScatterPlacer.PlaceChunk(0, 0, rules, surface);
            ScatterPlacer.PlaceChunk(9, 2, rules, surface);
            var second = ScatterPlacer.PlaceChunk(5, 6, rules, surface);
            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].CandidateIndex, second[i].CandidateIndex);
                Assert.AreEqual(first[i].X, second[i].X);
                Assert.AreEqual(first[i].Z, second[i].Z);
                Assert.AreEqual(first[i].YawDegrees, second[i].YawDegrees);
            }
        }

        [Test]
        public void EveryAcceptedPoint_IsInsideItsOwnerChunk_AndCandidateIndexIsUnique()
        {
            var surface = new FlatTestSurface();
            var rules = Trees(5f, 1f);
            var seen = new HashSet<int>();
            for (int cz = 2; cz <= 4; cz++)
            for (int cx = 3; cx <= 5; cx++)
            {
                foreach (var p in ScatterPlacer.PlaceChunk(cx, cz, rules, surface))
                {
                    Assert.AreEqual(cx, WorldConstants.ChunkCoordFromWorld(p.X), "chunk X ownership");
                    Assert.AreEqual(cz, WorldConstants.ChunkCoordFromWorld(p.Z), "chunk Z ownership");
                    Assert.IsTrue(seen.Add(p.CandidateIndex), "candidate owned by two chunks");
                }
            }
        }

        [Test]
        public void MinimumSpacing_HoldsAcrossChunkBorders_ForAllSpeciesBands()
        {
            var surface = new FlatTestSurface();
            foreach (float spacing in new[] { 3f, 5f, 8f })
            {
                var rules = Trees(spacing, 1f, layer: 20 + (int)spacing);
                // 3 × 3 block centred on (6, 6): every border between owned chunks is exercised.
                var points = new List<ScatterPoint>();
                for (int cz = 5; cz <= 7; cz++)
                for (int cx = 5; cx <= 7; cx++)
                    points.AddRange(ScatterPlacer.PlaceChunk(cx, cz, rules, surface));
                Assert.Greater(points.Count, 100, $"spacing {spacing} should produce a populated block");
                float minSq = float.MaxValue;
                for (int a = 0; a < points.Count; a++)
                for (int b = a + 1; b < points.Count; b++)
                {
                    float dx = points[a].X - points[b].X, dz = points[a].Z - points[b].Z;
                    minSq = Math.Min(minSq, dx * dx + dz * dz);
                }
                Assert.GreaterOrEqual(Math.Sqrt(minSq), spacing - 1e-3f, $"spacing {spacing} violated");
            }
        }

        [Test]
        public void Exclusions_RoadCorridorShoulderSlopeReservedAndDensityAreRespected()
        {
            var surface = new FlatTestSurface
            {
                SteepBandMinX = 1500f,
                ReservedX = 800f, ReservedZ = 800f, ReservedRadius = 40f,
            };
            var rules = Trees(4f, 1f);
            for (int cz = 0; cz < WorldConstants.ChunksPerSide; cz++)
            for (int cx = 0; cx < WorldConstants.ChunksPerSide; cx++)
            foreach (var p in ScatterPlacer.PlaceChunk(cx, cz, rules, surface))
            {
                Assert.GreaterOrEqual(Math.Abs(p.Z - 1000f) - 3.5f, rules.RoadClearanceMeters - 1e-3f, "road shoulder");
                Assert.Less(p.X, 1500f + 1e-3f, "steep band");
                float dx = p.X - 800f, dz = p.Z - 800f;
                Assert.Greater(dx * dx + dz * dz, 40f * 40f - 1e-2f, "reserved footprint");
                Assert.GreaterOrEqual(p.Y, rules.MinHeightMeters, "underwater");
                Assert.GreaterOrEqual(p.Scale, rules.MinScale);
                Assert.LessOrEqual(p.Scale, rules.MaxScale);
            }
        }

        [Test]
        public void ZeroDensity_ProducesNothing_AndDenseLayerFillsMostOfTheChunk()
        {
            var surface = new FlatTestSurface();
            Assert.AreEqual(0, ScatterPlacer.PlaceChunk(4, 4, Trees(4f, 0f), surface).Count);
            int dense = ScatterPlacer.PlaceChunk(4, 4, Trees(4f, 1f), surface).Count;
            // Random-sequential packing at 4 m spacing with 85×85 candidate cells gives a few thousand
            // instances per 256 m chunk; a near-empty result would mean a broken acceptance rule.
            // One candidate per 4 m cell (64 × 64 = 4096 per chunk); the priority rule keeps about a third.
            int cells = ScatterPlacer.CellsPerChunk(4f) * ScatterPlacer.CellsPerChunk(4f);
            Assert.Greater(dense, cells / 4, "dense layer should keep at least a quarter of its candidate cells");
            Assert.Less(dense, cells);
        }

        [Test]
        public void Layers_UseIndependentSeeds()
        {
            var surface = new FlatTestSurface();
            var a = ScatterPlacer.PlaceChunk(7, 7, Trees(6f, 0.5f, layer: 31), surface);
            var b = ScatterPlacer.PlaceChunk(7, 7, Trees(6f, 0.5f, layer: 32), surface);
            Assert.AreNotEqual(a.Count, 0);
            bool identical = a.Count == b.Count;
            for (int i = 0; identical && i < a.Count; i++)
                identical = a[i].X == b[i].X && a[i].Z == b[i].Z;
            Assert.IsFalse(identical, "different layers must not share placements");
        }

        [Test]
        public void LocalChange_DoesNotRenumberOrMoveDistantInstances()
        {
            // Reserving a small footprint must only affect instances near it. Stable candidate indices
            // (§7.3) mean every other instance keeps its index and its exact position.
            var rules = Trees(4f, 0.9f, layer: 41);
            var before = new FlatTestSurface();
            var after = new FlatTestSurface { ReservedX = 1200f, ReservedZ = 1200f, ReservedRadius = 12f };

            var distant = new List<ScatterPoint>();
            var beforeAll = new List<ScatterPoint>();
            var afterAll = new List<ScatterPoint>();
            for (int cz = 4; cz <= 5; cz++)
            for (int cx = 4; cx <= 5; cx++)
            {
                beforeAll.AddRange(ScatterPlacer.PlaceChunk(cx, cz, rules, before));
                afterAll.AddRange(ScatterPlacer.PlaceChunk(cx, cz, rules, after));
            }
            var afterIndex = new Dictionary<int, ScatterPoint>();
            foreach (var p in afterAll) afterIndex[p.CandidateIndex] = p;

            int checkedFar = 0;
            foreach (var p in beforeAll)
            {
                float dx = p.X - 1200f, dz = p.Z - 1200f;
                if (dx * dx + dz * dz < 60f * 60f) continue; // footprint and its conflict cascade
                distant.Add(p);
                Assert.IsTrue(afterIndex.TryGetValue(p.CandidateIndex, out var q),
                    $"distant instance {p.CandidateIndex} disappeared after an unrelated local change");
                Assert.AreEqual(p.X, q.X);
                Assert.AreEqual(p.Z, q.Z);
                checkedFar++;
            }
            Assert.Greater(checkedFar, 500);
        }

        [Test]
        public void PlacementsWorkAtWorldCorners()
        {
            var surface = new FlatTestSurface();
            var rules = Trees(4f, 1f);
            foreach (var p in ScatterPlacer.PlaceChunk(0, 0, rules, surface))
                Assert.IsTrue(WorldConstants.InBounds(p.X, p.Z));
            Assert.AreEqual(0, ScatterPlacer.PlaceChunk(-1, 3, rules, surface).Count);
            Assert.AreEqual(0, ScatterPlacer.PlaceChunk(24, 3, rules, surface).Count);
        }
    }

    /// <summary>
    /// Simulated Unity host for the planner: applies commands, completes them one frame later, and
    /// counts asset handles independently of the planner's own ledger (QA case 12).
    /// </summary>
    sealed class StreamHostSim
    {
        public readonly ChunkStreamPlanner Planner;
        public int HostHandles;
        public readonly HashSet<int> ActiveSet = new HashSet<int>();
        public readonly List<string> Errors = new List<string>();
        public int ActivateCommands, DeactivateCommands, CancelCommands, RequestCommands;
        public Func<int, int, bool> FailLoad = (x, z) => false;

        readonly List<(StreamCommandKind kind, int chunkX, int chunkZ)> _pending =
            new List<(StreamCommandKind, int, int)>();

        public StreamHostSim(ChunkStreamPlanner planner) => Planner = planner;

        public void Tick(float x, float z, float vx = 0f, float vz = 0f, IReadOnlyList<(int, int)> pinned = null)
        {
            Apply(Planner.Step(x, z, vx, vz, pinned));
            // Mid-tick: a chunk being deactivated still holds its handle until the host reports completion.
            Assert.AreEqual(HostHandles, Planner.OutstandingHandles, "handle released before deactivation completed");
            Pump();
            CheckInvariants();
        }

        void Apply(List<StreamCommand> commands)
        {
            foreach (var c in commands)
            {
                switch (c.Kind)
                {
                    case StreamCommandKind.Request:
                        RequestCommands++;
                        HostHandles++;
                        _pending.Add((StreamCommandKind.Request, c.ChunkX, c.ChunkZ));
                        break;
                    case StreamCommandKind.Activate:
                        ActivateCommands++;
                        _pending.Add((StreamCommandKind.Activate, c.ChunkX, c.ChunkZ));
                        break;
                    case StreamCommandKind.Deactivate:
                        DeactivateCommands++;
                        _pending.Add((StreamCommandKind.Deactivate, c.ChunkX, c.ChunkZ));
                        break;
                    case StreamCommandKind.Cancel:
                        CancelCommands++;
                        HostHandles--;
                        _pending.RemoveAll(p => p.chunkX == c.ChunkX && p.chunkZ == c.ChunkZ &&
                                                p.kind == StreamCommandKind.Request);
                        break;
                }
            }
        }

        void Pump()
        {
            var work = new List<(StreamCommandKind kind, int chunkX, int chunkZ)>(_pending);
            _pending.Clear();
            foreach (var w in work)
            {
                int idx = w.chunkZ * Planner.ChunksPerSide + w.chunkX;
                switch (w.kind)
                {
                    case StreamCommandKind.Request:
                        if (FailLoad(w.chunkX, w.chunkZ))
                        {
                            Planner.MarkFailed(w.chunkX, w.chunkZ, "injected load failure");
                            HostHandles--;
                        }
                        else if (!Planner.MarkDataReady(w.chunkX, w.chunkZ))
                        {
                            HostHandles--; // rejected late data: release what we loaded
                        }
                        break;
                    case StreamCommandKind.Activate:
                        if (!Planner.MarkActive(w.chunkX, w.chunkZ))
                        {
                            Errors.Add($"MarkActive rejected for ({w.chunkX},{w.chunkZ})");
                        }
                        else if (!ActiveSet.Add(idx))
                        {
                            Errors.Add($"chunk ({w.chunkX},{w.chunkZ}) activated twice (duplicate props)");
                        }
                        break;
                    case StreamCommandKind.Deactivate:
                        if (!Planner.MarkDeactivated(w.chunkX, w.chunkZ, out bool retained))
                        {
                            Errors.Add($"MarkDeactivated rejected for ({w.chunkX},{w.chunkZ})");
                            break;
                        }
                        ActiveSet.Remove(idx);
                        if (!retained) HostHandles--; // dormant chunks keep their data handle
                        break;
                }
            }
        }

        /// <summary>Runs after every tick: the host and planner must agree, and nothing may leak or duplicate.</summary>
        public void CheckInvariants()
        {
            Assert.AreEqual(HostHandles, Planner.OutstandingHandles, "host and planner disagree on handles (leak)");
            Assert.AreEqual(ActiveSet.Count,
                Planner.CountInState(ChunkState.Active) + Planner.CountInState(ChunkState.Deactivating),
                "active set diverged from planner");
            Assert.AreEqual(Planner.ActivationCount - Planner.DeactivationCount,
                Planner.CountInState(ChunkState.Active), "activation ledger diverged");
            Assert.LessOrEqual(ActiveSet.Count, 16, "instantiated chunks stay within 4 × 4 during crossings");
            Assert.LessOrEqual(Planner.OutstandingHandles, 36, "retained data must stay inside the 640 m zone (≤ 6 × 6)");
            Assert.IsEmpty(Errors);
        }
    }

    /// <summary>§18.3 chunk streaming planner and QA case 12 (handle leaks / duplicate props).</summary>
    public class ChunkStreamPlannerTests
    {
        static StreamHostSim NewHost(int budget = 9) =>
            new StreamHostSim(new ChunkStreamPlanner { ActivationsPerStep = budget });

        static void Settle(StreamHostSim host, float x, float z, int frames = 6)
        {
            for (int i = 0; i < frames; i++) host.Tick(x, z);
        }

        [Test]
        public void InitialLoad_ActivatesThreeByThreeRing_AndHoldsUntilCurrentChunkIsActive()
        {
            var host = NewHost();
            var pl = host.Planner;
            host.Tick(1000f, 1000f); // chunk (3,3)
            Assert.IsTrue(pl.LoadingHoldRequired, "nothing is active yet");
            Settle(host, 1000f, 1000f);
            Assert.AreEqual(9, host.ActiveSet.Count);
            Assert.AreEqual(9, pl.CountInState(ChunkState.Active));
            Assert.IsFalse(pl.LoadingHoldRequired);
        }

        [Test]
        public void ActivationBudget_LimitsInstantiationPerStep()
        {
            var planner = new ChunkStreamPlanner { ActivationsPerStep = 2 };
            planner.Step(1000f, 1000f, 0f, 0f); // requests only
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                Assert.IsTrue(planner.MarkDataReady(3 + dx, 3 + dz));
            var cmds = planner.Step(1000f, 1000f, 0f, 0f);
            int activates = 0;
            foreach (var c in cmds) if (c.Kind == StreamCommandKind.Activate) activates++;
            Assert.AreEqual(2, activates);
        }

        [Test]
        public void RepeatedBoundaryCrossing_NeverLeaksHandles_NeverDuplicatesActivation()
        {
            // QA case 12: repeated chunk boundary crossing does not leak retained handles or duplicate props.
            var host = NewHost();
            Settle(host, 1000f, 1000f);
            int maxHandles = 0;
            for (int lap = 0; lap < 300; lap++)
            {
                // Walk east across chunk boundaries (1024 m and 1280 m), then back west.
                for (int step = 0; step < 40; step++)
                {
                    host.Tick(800f + step * 20f, 1000f, 20f, 0f);
                    maxHandles = Math.Max(maxHandles, host.Planner.OutstandingHandles);
                }
                for (int step = 0; step < 40; step++)
                {
                    host.Tick(1580f - step * 20f, 1000f, -20f, 0f);
                    maxHandles = Math.Max(maxHandles, host.Planner.OutstandingHandles);
                }
            }
            Settle(host, 800f, 1000f, 10);
            Assert.AreEqual(host.HostHandles, host.Planner.OutstandingHandles);
            Assert.AreEqual(9, host.ActiveSet.Count, "steady state: exactly the 3 × 3 ring is instantiated");
            Assert.LessOrEqual(maxHandles, 36, "retained handles stay inside the 640 m zone (≤ 6 × 6)");
            Assert.IsEmpty(host.Errors);
        }

        [Test]
        public void Hysteresis_BoundaryOscillation_DoesNotDeactivateOrReload()
        {
            var host = NewHost();
            Settle(host, 1279f, 1000f);     // chunk (4,3)
            // Warm up both sides of the boundary so the 5 × 5 retained zone is fully populated.
            for (int i = 0; i < 4; i++) { host.Tick(1279f, 1000f); host.Tick(1281f, 1000f); }
            int deactivatesBefore = host.DeactivateCommands;
            int requestsBefore = host.RequestCommands;
            int activatesBefore = host.ActivateCommands;
            for (int i = 0; i < 200; i++)
            {
                host.Tick(1279f, 1000f);
                host.Tick(1281f, 1000f);
            }
            Assert.AreEqual(deactivatesBefore, host.DeactivateCommands, "boundary jitter must not deactivate");
            Assert.AreEqual(requestsBefore, host.RequestCommands, "boundary jitter must not reload");
            Assert.AreEqual(activatesBefore, host.ActivateCommands, "boundary jitter must not re-instantiate");
        }

        [Test]
        public void Dormant_ChunkReenteringTheRing_ReactivatesWithoutReloadingData()
        {
            // §18.3 hysteresis: leaving the ring keeps data; re-entry instantiates but costs no IO.
            var host = NewHost();
            Settle(host, 800f, 1000f);      // chunk (3,3)
            int requestsAfterLoad = host.RequestCommands;
            // Move east to x = 1450: chunk 3 is now 426 m away (beyond the 384 m activation band) → dormant.
            Settle(host, 1450f, 1000f, 3);
            Assert.AreEqual(ChunkState.DataReady, host.Planner.StateOf(3, 3), "dormant chunk keeps its data");
            // The 3 × 3 ring around chunk 5 needs columns 5 and 6 (column 4 was already loaded): six new requests.
            Assert.AreEqual(requestsAfterLoad + 6, host.RequestCommands, "only the new ring columns are requested");
            int requestsBefore = host.RequestCommands;
            Settle(host, 800f, 1000f, 3);   // back inside chunk 3
            Assert.AreEqual(ChunkState.Active, host.Planner.StateOf(3, 3));
            // Column 2 was unloaded beyond 640 m, so re-entry requests only that column; chunk 3 reactivates for free.
            Assert.AreEqual(requestsBefore + 3, host.RequestCommands, "returning reloads only the unloaded column");
        }

        [Test]
        public void Pinned_Chunk_IsNeverUnloaded_EvenFarAway()
        {
            var host = NewHost();
            Settle(host, 600f, 600f, 8);    // chunk (2,2)
            Assert.AreEqual(ChunkState.Active, host.Planner.StateOf(2, 2));
            var pinned = new List<(int, int)> { (2, 2) };
            for (int i = 0; i < 30; i++) host.Tick(4500f, 4500f, 0f, 0f, pinned);
            Assert.AreEqual(ChunkState.Active, host.Planner.StateOf(2, 2), "pinned chunk unloaded");
            Assert.AreEqual(ChunkState.Unloaded, host.Planner.StateOf(2, 3), "unpinned neighbour should unload");
        }

        [Test]
        public void Cancel_ReleasesHandle_AndLateDataIsRejected()
        {
            var planner = new ChunkStreamPlanner();
            planner.Step(1000f, 1000f, 0f, 0f);                    // requests 3×3 around (3,3)
            Assert.AreEqual(9, planner.OutstandingHandles);
            // Teleport far away: the old ring is cancelled (never activated).
            var cmds = planner.Step(4500f, 4500f, 0f, 0f);
            int cancels = 0;
            foreach (var c in cmds) if (c.Kind == StreamCommandKind.Cancel) cancels++;
            Assert.AreEqual(9, cancels);
            // Only the new ring (requested at the destination) holds handles; the cancelled ring released all nine.
            Assert.AreEqual(9, planner.OutstandingHandles);
            Assert.AreEqual(9, planner.CountInState(ChunkState.Requested));
            // A host's late completion for a cancelled chunk must be rejected so the host releases its data.
            Assert.IsFalse(planner.MarkDataReady(3, 3));
            Assert.IsFalse(planner.MarkActive(3, 3));
            Assert.IsFalse(planner.MarkDeactivated(3, 3, out _));
        }

        [Test]
        public void FailedLoad_RetriesThenRecordsStreamingDefect_WithoutLeaking()
        {
            var host = NewHost();
            host.FailLoad = (x, z) => x == 10 && z == 10;
            Settle(host, 2000f, 2000f, 6);      // chunk (7,7): not the failing chunk
            Assert.AreEqual(0, host.Planner.StreamingDefects.Count);

            // Walk so that chunk (10,10) becomes the current chunk: it fails on every attempt.
            Settle(host, 2700f, 2700f, 12);     // chunk (10,10)
            Assert.AreEqual(ChunkState.Failed, host.Planner.StateOf(10, 10));
            Assert.AreEqual(1, host.Planner.StreamingDefects.Count, "exactly one defect after retries are exhausted");
            Assert.AreEqual(host.HostHandles, host.Planner.OutstandingHandles, "failed loads must not hold handles");
            Assert.IsTrue(host.Planner.LoadingHoldRequired, "the player's own chunk is not loaded");
        }

        [Test]
        public void IllegalCompletionCallbacks_AreRejected_AndDoNotChangeState()
        {
            var planner = new ChunkStreamPlanner();
            Assert.IsFalse(planner.MarkActive(0, 0));
            Assert.IsFalse(planner.MarkDataReady(-1, 0));
            Assert.IsFalse(planner.MarkDeactivated(5, 5, out _));
            Assert.IsFalse(planner.MarkFailed(5, 5, "nothing loaded"));
            Assert.AreEqual(ChunkState.Unloaded, planner.StateOf(5, 5));
            Assert.AreEqual(0, planner.OutstandingHandles);
        }

        [Test]
        public void WorldEdge_NeverRequestsChunksOutsideTheWorld()
        {
            var host = NewHost();
            Settle(host, 5f, 5f, 8);
            Assert.AreEqual(4, host.ActiveSet.Count, "corner has four in-world chunks in the 3×3 ring");
            Assert.IsEmpty(host.Errors);
            Assert.AreEqual(4, host.Planner.OutstandingHandles, "only the in-world 2 × 2 corner ring is loaded");
        }

        [Test]
        public void LoadingHold_IsRaised_WhenCurrentChunkIsNotActive()
        {
            var planner = new ChunkStreamPlanner();
            planner.Step(1000f, 1000f, 0f, 0f);
            Assert.IsTrue(planner.LoadingHoldRequired);
        }
    }
}
