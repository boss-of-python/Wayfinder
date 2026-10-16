using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.World
{
    public enum StreamCommandKind
    {
        /// <summary>Start loading a chunk's data (host acquires one asset handle).</summary>
        Request,
        /// <summary>Instantiate a chunk whose data is ready (host reports MarkActive when done).</summary>
        Activate,
        /// <summary>Begin removing a chunk's active objects (host reports MarkDeactivated when done). Check the retained flag on completion.</summary>
        Deactivate,
        /// <summary>Stop a load that has not activated yet; the host must drop any late data it receives.</summary>
        Cancel,
    }

    public readonly struct StreamCommand
    {
        public StreamCommand(StreamCommandKind kind, int chunkX, int chunkZ)
        {
            Kind = kind;
            ChunkX = chunkX;
            ChunkZ = chunkZ;
        }

        public StreamCommandKind Kind { get; }
        public int ChunkX { get; }
        public int ChunkZ { get; }

        public override string ToString() => $"{Kind}({ChunkX},{ChunkZ})";
    }

    /// <summary>
    /// Pure streaming planner for the 24 × 24 chunk world (Blueprint §18.3). It owns the per-chunk state
    /// machine and decides what should load, activate, deactivate or be cancelled. The Unity host (a
    /// MonoBehaviour, not yet written) executes the commands and reports completions back.
    ///
    /// Rules encoded here:
    ///  * Request ring: 3 × 3 chunks around the player's chunk are always requested (data loads ahead of need).
    ///  * Prefetch: the chunk containing the point PrefetchLeadMeters ahead along the velocity, and its
    ///    3 × 3 neighbourhood, are requested but not activated.
    ///  * Activation: the 3 × 3 ring is always instantiated. Outside it, a chunk is instantiated inside 256 m and kept
    ///    until 384 m (distance based, D-034). A chunk that leaves the activation band but stays inside 640 m is
    ///    deactivated and keeps its data (dormant); only beyond 640 m is it unloaded. Boundary oscillation therefore
    ///    causes no deactivation or reload.
    ///  * Pinned chunks (current chunk, predicted stopping corridor, recovery destination) are never unloaded.
    ///  * Activation is budgeted per step (prop instantiation budget, §18.3). Worst-case instantiated chunks are
    ///    4 × 4 during a crossing; retained data is at most 6 × 6.
    ///  * Failed loads retry up to MaxRetries times, then record a streaming defect (§18.3).
    ///  * Handle ledger: one asset handle is held exactly while a chunk is in Requested, DataReady,
    ///    Activating, Active or Deactivating. The planner counts them; the host must release on Cancel,
    ///    on a rejected Mark* call, on Failed, and on Deactivate completion when data is not retained.
    /// </summary>
    public sealed class ChunkStreamPlanner
    {
        public const int ActiveRadiusChunks = WorldConstants.ActiveRingChunks / 2;   // 3 × 3 request ring
        /// <summary>Instantiate a chunk whose rectangle is within this distance (≈ detail radius 180–250 m, §18.3).</summary>
        public const float ActivateEnterMeters = WorldConstants.ChunkSizeMeters;     // 256 m
        /// <summary>Keep an active chunk until it is this far away (hysteresis band, D-034).</summary>
        public const float ActivateRetainMeters = 1.5f * WorldConstants.ChunkSizeMeters; // 384 m
        /// <summary>Unload a chunk's data only beyond this distance (hysteresis band, D-034).</summary>
        public const float UnloadMeters = 2.5f * WorldConstants.ChunkSizeMeters;     // 640 m
        public const int MaxRetries = 2;                                             // 3 attempts in total
        public const int DefaultActivationsPerStep = 2;
        public const float MinPrefetchSpeedMetersPerSecond = 1f;

        readonly int _side;
        readonly ChunkStateMachine[] _chunks;
        readonly int[] _failures;
        readonly bool[] _deactivateRetainsData;
        readonly List<string> _defects = new List<string>();
        int _currentX = -1;
        int _currentZ = -1;
        float _lastX, _lastZ; // last player position seen by Step (for completion callbacks)

        public ChunkStreamPlanner(int chunksPerSide = WorldConstants.ChunksPerSide)
        {
            if (chunksPerSide <= 0) throw new ArgumentOutOfRangeException(nameof(chunksPerSide));
            _side = chunksPerSide;
            _chunks = new ChunkStateMachine[_side * _side];
            _failures = new int[_side * _side];
            _deactivateRetainsData = new bool[_side * _side];
            for (int i = 0; i < _chunks.Length; i++) _chunks[i] = new ChunkStateMachine();
        }

        public int ChunksPerSide => _side;

        public int ActivationsPerStep { get; set; } = DefaultActivationsPerStep;

        /// <summary>Asset handles the planner believes the host currently holds (see class remarks).</summary>
        public int OutstandingHandles { get; private set; }

        /// <summary>Monotonic counters; ActivationCount − DeactivationCount must equal the Active chunk count.</summary>
        public int ActivationCount { get; private set; }
        public int DeactivationCount { get; private set; }

        public IReadOnlyList<string> StreamingDefects => _defects;

        /// <summary>
        /// True while the host should show a short loading state: the player's own chunk is not active,
        /// or an active-ring chunk within PrefetchLeadMeters is not yet active (never cross into missing terrain).
        /// Recomputed by every Step and by every completion callback.
        /// </summary>
        public bool LoadingHoldRequired { get; private set; }

        public ChunkState StateOf(int chunkX, int chunkZ)
        {
            if (!InWorld(chunkX, chunkZ)) return ChunkState.Unloaded;
            return _chunks[Index(chunkX, chunkZ)].State;
        }

        public int CountInState(ChunkState state)
        {
            int count = 0;
            for (int i = 0; i < _chunks.Length; i++)
                if (_chunks[i].State == state) count++;
            return count;
        }

        /// <summary>Plans one streaming step for the current player state.</summary>
        /// <param name="x">Player world X (meters).</param>
        /// <param name="z">Player world Z (meters).</param>
        /// <param name="velocityX">Player velocity X (m/s), used for prefetch direction.</param>
        /// <param name="velocityZ">Player velocity Z (m/s).</param>
        /// <param name="pinned">Chunks that must stay loaded and active (stopping corridor, recovery destination).</param>
        public List<StreamCommand> Step(float x, float z, float velocityX, float velocityZ,
            IReadOnlyList<(int chunkX, int chunkZ)> pinned = null)
        {
            var commands = new List<StreamCommand>();
            int cx = Clamp(WorldConstants.ChunkCoordFromWorld(x));
            int cz = Clamp(WorldConstants.ChunkCoordFromWorld(z));
            _currentX = cx;
            _currentZ = cz;
            _lastX = x;
            _lastZ = z;

            var pinnedSet = new HashSet<int>();
            if (pinned != null)
                foreach (var p in pinned)
                    if (InWorld(p.chunkX, p.chunkZ)) pinnedSet.Add(Index(p.chunkX, p.chunkZ));

            // Request zone: the 3 × 3 ring around the player's chunk, plus the prefetch zone ahead of travel.
            var requestZone = new HashSet<int>();
            AddRing(requestZone, cx, cz, ActiveRadiusChunks);

            float speed = (float)Math.Sqrt(velocityX * velocityX + velocityZ * velocityZ);
            if (speed >= MinPrefetchSpeedMetersPerSecond)
            {
                float px = x + velocityX / speed * WorldConstants.PrefetchLeadMeters;
                float pz = z + velocityZ / speed * WorldConstants.PrefetchLeadMeters;
                AddRing(requestZone, Clamp(WorldConstants.ChunkCoordFromWorld(px)),
                    Clamp(WorldConstants.ChunkCoordFromWorld(pz)), ActiveRadiusChunks);
            }

            // Activation (§18.3): the 3 × 3 ring is always instantiated. Beyond it, a chunk is instantiated inside
            // ActivateEnterMeters and kept until ActivateRetainMeters; data is kept until UnloadMeters. Distances are
            // measured from the player to the chunk rectangle, so jitter across a chunk border shifts none of them.
            for (int i = 0; i < _chunks.Length; i++)
            {
                int ix = i % _side, iz = i / _side;
                ChunkState state = _chunks[i].State;
                float d = DistanceToChunkMeters(x, z, ix, iz);
                bool pinnedHere = pinnedSet.Contains(i);
                bool wantActive = pinnedHere || InRing(i, cx, cz) || d <= ActivateEnterMeters ||
                                  (state == ChunkState.Active && d <= ActivateRetainMeters);
                bool wantLoaded = pinnedHere || requestZone.Contains(i) || d <= UnloadMeters;

                if (!wantLoaded)
                {
                    switch (state)
                    {
                        case ChunkState.Active:
                            Transition(i, ChunkState.Deactivating);
                            _deactivateRetainsData[i] = false;
                            commands.Add(new StreamCommand(StreamCommandKind.Deactivate, ix, iz));
                            break;
                        case ChunkState.Requested:
                        case ChunkState.DataReady:
                            Transition(i, ChunkState.Unloaded);
                            commands.Add(new StreamCommand(StreamCommandKind.Cancel, ix, iz));
                            break;
                        case ChunkState.Failed:
                            Transition(i, ChunkState.Unloaded); // clean retry slate for a later visit
                            _failures[i] = 0;
                            break;
                        // Activating / Deactivating / Unloaded: wait for the host or nothing to do.
                    }
                }
                else if (!wantActive && state == ChunkState.Active)
                {
                    // Left the activation band but still inside the data zone: dormant, data retained.
                    Transition(i, ChunkState.Deactivating);
                    _deactivateRetainsData[i] = true;
                    commands.Add(new StreamCommand(StreamCommandKind.Deactivate, ix, iz));
                }
            }

            // Activate ready chunks that want to be active, nearest first, within the per-step budget.
            var readyActive = new List<int>();
            for (int i = 0; i < _chunks.Length; i++)
            {
                if (_chunks[i].State != ChunkState.DataReady) continue;
                int ix = i % _side, iz = i / _side;
                float d = DistanceToChunkMeters(x, z, ix, iz);
                if (pinnedSet.Contains(i) || InRing(i, cx, cz) || d <= ActivateEnterMeters) readyActive.Add(i);
            }
            readyActive.Sort((a, b) => CompareByDistance(a, b, cx, cz));
            int budget = Math.Max(0, ActivationsPerStep);
            for (int k = 0; k < readyActive.Count && budget > 0; k++, budget--)
            {
                int i = readyActive[k];
                Transition(i, ChunkState.Activating);
                commands.Add(new StreamCommand(StreamCommandKind.Activate, i % _side, i / _side));
            }

            // Request missing chunks in the request zone (ring first, then prefetch) and pinned chunks.
            var requestList = new HashSet<int>(requestZone);
            requestList.UnionWith(pinnedSet);
            RequestAll(requestList, cx, cz, commands);

            UpdateLoadingHold(x, z);
            return commands;
        }

        /// <summary>Host reports that a requested chunk's data is ready. False = rejected; host must release it.</summary>
        public bool MarkDataReady(int chunkX, int chunkZ)
        {
            if (!InWorld(chunkX, chunkZ)) return false;
            int i = Index(chunkX, chunkZ);
            if (_chunks[i].State != ChunkState.Requested) return false;
            Transition(i, ChunkState.DataReady);
            UpdateLoadingHold(_lastX, _lastZ);
            return true;
        }

        /// <summary>Host reports that a chunk finished activating. False = rejected; host must release it.</summary>
        public bool MarkActive(int chunkX, int chunkZ)
        {
            if (!InWorld(chunkX, chunkZ)) return false;
            int i = Index(chunkX, chunkZ);
            if (_chunks[i].State != ChunkState.Activating) return false;
            Transition(i, ChunkState.Active);
            ActivationCount++;
            UpdateLoadingHold(_lastX, _lastZ);
            return true;
        }

        /// <summary>
        /// Host reports that a chunk finished deactivating. When <paramref name="dataRetained"/> is true the chunk
        /// is dormant (DataReady, handle kept); otherwise its data was released and the handle is gone.
        /// </summary>
        public bool MarkDeactivated(int chunkX, int chunkZ, out bool dataRetained)
        {
            dataRetained = false;
            if (!InWorld(chunkX, chunkZ)) return false;
            int i = Index(chunkX, chunkZ);
            if (_chunks[i].State != ChunkState.Deactivating) return false;
            dataRetained = _deactivateRetainsData[i];
            Transition(i, dataRetained ? ChunkState.DataReady : ChunkState.Unloaded);
            DeactivationCount++;
            UpdateLoadingHold(_lastX, _lastZ);
            return true;
        }

        /// <summary>Host reports a load/activation failure. The handle is released; retries are planned later.</summary>
        public bool MarkFailed(int chunkX, int chunkZ, string detail)
        {
            if (!InWorld(chunkX, chunkZ)) return false;
            int i = Index(chunkX, chunkZ);
            ChunkState from = _chunks[i].State;
            if (from == ChunkState.Unloaded || from == ChunkState.Failed) return false;
            if (from == ChunkState.Active) DeactivationCount++; // the active instance is torn down by the failure
            Transition(i, ChunkState.Failed);
            _failures[i]++;
            if (_failures[i] > MaxRetries)
                _defects.Add($"streaming defect: chunk ({chunkX},{chunkZ}) failed {_failures[i]} times: {detail}");
            UpdateLoadingHold(_lastX, _lastZ);
            return true;
        }

        void RequestAll(HashSet<int> set, int cx, int cz, List<StreamCommand> commands)
        {
            var list = new List<int>(set);
            list.Sort((a, b) => CompareByDistance(a, b, cx, cz));
            foreach (int i in list)
            {
                ChunkState s = _chunks[i].State;
                if (s == ChunkState.Failed)
                {
                    if (_failures[i] > MaxRetries) continue; // exhausted: stays failed, defect already recorded
                    Transition(i, ChunkState.Unloaded);
                    s = ChunkState.Unloaded;
                }
                if (s == ChunkState.Unloaded)
                {
                    Transition(i, ChunkState.Requested);
                    commands.Add(new StreamCommand(StreamCommandKind.Request, i % _side, i / _side));
                }
            }
        }

        void UpdateLoadingHold(float x, float z)
        {
            _lastX = x;
            _lastZ = z;
            bool hold = _currentX >= 0 && _chunks[Index(_currentX, _currentZ)].State != ChunkState.Active;
            if (!hold)
            {
                // Active-ring chunks near the player that are not yet active.
                for (int dz = -ActiveRadiusChunks; dz <= ActiveRadiusChunks && !hold; dz++)
                for (int dx = -ActiveRadiusChunks; dx <= ActiveRadiusChunks && !hold; dx++)
                {
                    int nx = _currentX + dx, nz = _currentZ + dz;
                    if (!InWorld(nx, nz)) continue;
                    if (_chunks[Index(nx, nz)].State == ChunkState.Active) continue;
                    if (DistanceToChunkMeters(x, z, nx, nz) < WorldConstants.PrefetchLeadMeters) hold = true;
                }
            }
            LoadingHoldRequired = hold;
        }

        void Transition(int i, ChunkState to)
        {
            ChunkState from = _chunks[i].State;
            bool ok = _chunks[i].TryTransition(to, out string error);
            if (!ok) throw new InvalidOperationException(error);
            bool fromHolds = Holds(from), toHolds = Holds(to);
            if (!fromHolds && toHolds) OutstandingHandles++;
            else if (fromHolds && !toHolds) OutstandingHandles--;
        }

        static bool Holds(ChunkState s) =>
            s == ChunkState.Requested || s == ChunkState.DataReady || s == ChunkState.Activating ||
            s == ChunkState.Active || s == ChunkState.Deactivating;

        void AddRing(HashSet<int> set, int cx, int cz, int radius)
        {
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = cx + dx, nz = cz + dz;
                if (InWorld(nx, nz)) set.Add(Index(nx, nz));
            }
        }

        bool InRing(int chunkIndex, int cx, int cz) =>
            Math.Max(Math.Abs(chunkIndex % _side - cx), Math.Abs(chunkIndex / _side - cz)) <= ActiveRadiusChunks;

        int CompareByDistance(int a, int b, int cx, int cz)
        {
            int d = ChebyshevFrom(a, cx, cz).CompareTo(ChebyshevFrom(b, cx, cz));
            return d != 0 ? d : a.CompareTo(b);
        }

        int ChebyshevFrom(int index, int cx, int cz)
        {
            int ix = index % _side, iz = index / _side;
            return Math.Max(Math.Abs(ix - cx), Math.Abs(iz - cz));
        }

        static float DistanceToChunkMeters(float x, float z, int chunkX, int chunkZ)
        {
            float minX = chunkX * WorldConstants.ChunkSizeMeters;
            float minZ = chunkZ * WorldConstants.ChunkSizeMeters;
            float maxX = minX + WorldConstants.ChunkSizeMeters;
            float maxZ = minZ + WorldConstants.ChunkSizeMeters;
            float dx = x < minX ? minX - x : (x > maxX ? x - maxX : 0f);
            float dz = z < minZ ? minZ - z : (z > maxZ ? z - maxZ : 0f);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        int Clamp(int c) => c < 0 ? 0 : (c >= _side ? _side - 1 : c);

        bool InWorld(int cx, int cz) => cx >= 0 && cz >= 0 && cx < _side && cz < _side;

        int Index(int cx, int cz) => cz * _side + cx;
    }
}
