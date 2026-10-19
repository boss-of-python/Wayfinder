using System;
using System.Collections.Generic;
using Wayfinder.Core;
using Wayfinder.World;

namespace Wayfinder.Progression
{
    /// <summary>
    /// Map/discovery progress and reward keys (§8.1–8.4, §16.1).
    /// Never mutates currency outside a transaction: mapping/landmark rewards are ESCROWED
    /// until garage/camp banking. Known map cells never disappear on rescue.
    /// </summary>
    public sealed class DiscoveryService
    {
        public const int MappingRewardThresholdCells = 50;
        public const int MappingRewardSalvage = 50;
        public const int LandmarkDiscoveryRewardSalvage = 120;
        public const float LandmarkDiscoveryMaxSpeedKph = 15f;
        public const float LandmarkDiscoveryHoldSeconds = 2f;
        public const float FinaleMapCoveragePercent = 60f;

        public BitGrid RevealedCells { get; private set; }
        public BitGrid SurveyedCells { get; private set; }
        /// <summary>Precomputed countable-cell mask excluding ocean/decorative cliffs (§8.1).</summary>
        public BitGrid CountableMask { get; private set; }

        readonly HashSet<string> _discoveredLandmarks = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _collectedCaches = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _relics = new HashSet<string>(StringComparer.Ordinal);
        readonly ClaimedRewardLedger _rewardLedger = new ClaimedRewardLedger();

        /// <summary>Mapping rewards paid through this count of newly revealed countable cells (§8.4).</summary>
        public int MappingRewardsPaidThroughCount { get; private set; }

        public DiscoveryService()
        {
            RevealedCells = new BitGrid(WorldConstants.MapCellsPerSide, WorldConstants.MapCellsPerSide);
            SurveyedCells = new BitGrid(WorldConstants.MapCellsPerSide, WorldConstants.MapCellsPerSide);
            CountableMask = new BitGrid(WorldConstants.MapCellsPerSide, WorldConstants.MapCellsPerSide);
            // Default: all in-bounds cells countable; WorldBaker refines with the real mask.
            for (int z = 0; z < WorldConstants.MapCellsPerSide; z++)
            for (int x = 0; x < WorldConstants.MapCellsPerSide; x++)
                CountableMask.Set(x, z, true);
        }

        public ClaimedRewardLedger RewardLedger => _rewardLedger;

        /// <summary>
        /// Marks ocean cells uncountable so map coverage is measured over land only (§8.1/§8.4).
        /// <para>
        /// DEF-008: the mask defaulted to "every cell counts" and was never refined — nothing in the
        /// repo called it, and `CampaignSnapshot` wrote `countableMaskBase64 = ""` ("derived data,
        /// never persisted"), so after a load the denominator silently reverted to all 9,216 cells
        /// including ocean. The finale gate (60 % mainland coverage) and the §8.4 mapping rewards
        /// were therefore measured against a different map before and after every save.
        /// </para>
        /// A cell counts when its centre is above sea level. Deterministic for a given seed, so the
        /// result is memoized: the bake is ~9 k noise samples and never needs repeating.
        /// </para>
        /// </summary>
        public void BakeCountableMask(NoiseField noise = null, int worldSeed = WorldConstants.CanonicalWorldSeed,
            int generatorVersion = WorldConstants.GeneratorVersion)
        {
            if (TryCachedMask(worldSeed, generatorVersion, out var cached))
            {
                CountableMask = cached.Clone();
                return;
            }
            noise = noise ?? new NoiseField(worldSeed, generatorVersion);
            var mask = new BitGrid(WorldConstants.MapCellsPerSide, WorldConstants.MapCellsPerSide);
            float half = WorldConstants.MapCellSizeMeters * 0.5f;
            for (int z = 0; z < WorldConstants.MapCellsPerSide; z++)
            for (int x = 0; x < WorldConstants.MapCellsPerSide; x++)
            {
                float wx = x * WorldConstants.MapCellSizeMeters + half;
                float wz = z * WorldConstants.MapCellSizeMeters + half;
                if (noise.BaseHeight(wx, wz) > WorldConstants.SeaLevelY) mask.Set(x, z, true);
            }
            CountableMask = mask;
            Cache(mask, worldSeed, generatorVersion);
        }

        /// <summary>
        /// Installs an authored mask — the seam WorldBaker needs. Until 2026-10-19 the baker had no
        /// way to hand the refined mask over, so "excluding ocean/decorative cliffs (§8.1)" was only
        /// a comment (DEF-008).
        /// </summary>
        public void SetCountableMask(BitGrid mask)
        {
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            if (mask.Width != WorldConstants.MapCellsPerSide || mask.Height != WorldConstants.MapCellsPerSide)
                throw new ArgumentException(
                    $"mask must be {WorldConstants.MapCellsPerSide}x{WorldConstants.MapCellsPerSide}, was {mask.Width}x{mask.Height}");
            CountableMask = mask;
        }

        // Memoized per (seed, version): one entry per world, and the mask is immutable once baked.
        static readonly object MaskCacheLock = new object();
        static BitGrid _cachedMask;
        static int _cachedSeed = int.MinValue;
        static int _cachedVersion = int.MinValue;

        static bool TryCachedMask(int seed, int version, out BitGrid mask)
        {
            lock (MaskCacheLock)
            {
                if (_cachedMask != null && _cachedSeed == seed && _cachedVersion == version)
                {
                    mask = _cachedMask;
                    return true;
                }
            }
            mask = null;
            return false;
        }

        static void Cache(BitGrid mask, int seed, int version)
        {
            lock (MaskCacheLock)
            {
                _cachedMask = mask.Clone();
                _cachedSeed = seed;
                _cachedVersion = version;
            }
        }

        public int RevealedCountableCells => RevealedCells.CountSetInMask(CountableMask);

        public float MapCoveragePercent
        {
            get
            {
                int total = CountableMask.CountSet();
                return total <= 0 ? 0f : 100f * RevealedCountableCells / total;
            }
        }

        /// <summary>
        /// Driving reveals eligible cells within 96 m; vista survey within 384 m (§8.1).
        /// cellAllowed is the cheap terrain line-of-sight filter.
        /// Returns newly revealed countable cells.
        /// </summary>
        public int RevealAround(float worldX, float worldZ, bool fromVista,
            Func<int, int, bool> lineOfSightFilter = null)
        {
            float radius = fromVista ? WorldConstants.VistaRevealRadiusMeters : WorldConstants.DriveRevealRadiusMeters;
            int before = RevealedCountableCells;
            Func<int, int, bool> eligible = (x, z) =>
                CountableMask.Get(x, z) && (lineOfSightFilter == null || lineOfSightFilter(x, z));

            RevealedCells.RevealCircle(worldX, worldZ, radius, WorldConstants.MapCellSizeMeters, eligible);

            // Surveyed detail obeys the same occlusion as the reveal (§8.1 "also respecting large
            // terrain occlusion"); previously it ignored the filter, so a cell hidden behind a ridge
            // could be "surveyed" without being revealed.
            if (fromVista)
                SurveyedCells.RevealCircle(worldX, worldZ, radius, WorldConstants.MapCellSizeMeters, eligible);

            int newly = RevealedCountableCells - before;
            PayMappingThresholds();
            return newly;
        }

        /// <summary>
        /// <see cref="RevealAround"/> with the §8.1 terrain line-of-sight filter built from a height
        /// sampler (metres; NaN = unknown, never occludes). A null sampler reveals the full radius.
        /// </summary>
        public int RevealVisibleAround(float worldX, float worldZ, bool fromVista, Func<float, float, float> terrainHeight) =>
            RevealAround(worldX, worldZ, fromVista,
                TerrainLineOfSight.CellFilter(terrainHeight, worldX, worldZ, WorldConstants.MapCellSizeMeters,
                    fromVista ? TerrainLineOfSight.VistaEyeHeightMeters : TerrainLineOfSight.EyeHeightMeters));

        /// <summary>
        /// Every 50 newly revealed countable cells grants 50 Salvage ONCE (§8.4).
        /// Persistent count + paid-threshold index prevent duplicate claims (QA case 18).
        /// </summary>
        void PayMappingThresholds()
        {
            int revealed = RevealedCountableCells;
            while (revealed >= MappingRewardsPaidThroughCount + MappingRewardThresholdCells)
            {
                MappingRewardsPaidThroughCount += MappingRewardThresholdCells;
                string key = $"reward.map.{MappingRewardsPaidThroughCount}";
                if (_rewardLedger.TryClaim(key))
                    _economy?.Escrow(MappingRewardSalvage); // escrow until banking (§8.4)
            }
        }

        EconomyService _economy;

        public void BindEconomy(EconomyService economy) => _economy = economy;

        /// <summary>
        /// Landmark discovery (§8.2): observation volume + line of sight + speed below 15 km/h
        /// for 2 s. Pays 120 Salvage once, via escrow + reward key.
        /// </summary>
        public bool TryDiscoverLandmark(string landmarkId, float speedKph, float observedHoldSeconds,
            out GameEvent discovered) =>
            TryDiscoverLandmark(landmarkId, speedKph, observedHoldSeconds, out discovered, default);

        /// <summary>
        /// Discovery with an explicit transaction id. §16.3 requires every permanent reward event to
        /// carry the transaction that produced it so a replay is idempotent; until 2026-10-19 the
        /// event was built with <c>default(TransactionId)</c>, and the ledger had to carry the
        /// idempotency burden alone (DEF-019).
        /// </summary>
        public bool TryDiscoverLandmark(string landmarkId, float speedKph, float observedHoldSeconds,
            out GameEvent discovered, TransactionId transactionId)
        {
            discovered = null;
            if (speedKph >= LandmarkDiscoveryMaxSpeedKph || observedHoldSeconds < LandmarkDiscoveryHoldSeconds)
                return false;
            if (_discoveredLandmarks.Contains(landmarkId))
                return false; // one reward ever (QA case 1)

            _discoveredLandmarks.Add(landmarkId);
            string rewardKey = $"reward.landmark.{landmarkId}";
            if (_rewardLedger.TryClaim(rewardKey))
                _economy?.Escrow(LandmarkDiscoveryRewardSalvage);

            discovered = new LandmarkDiscovered(transactionId, new ContentId(landmarkId), rewardKey,
                LandmarkDiscoveryRewardSalvage);
            return true;
        }

        public bool IsLandmarkDiscovered(string landmarkId) => _discoveredLandmarks.Contains(landmarkId ?? string.Empty);

        /// <summary>Cache collection registry (QA case 2: re-collect is a typed failure upstream).</summary>
        public bool IsCacheCollected(string cacheId) => _collectedCaches.Contains(cacheId);

        public bool TryMarkCacheCollected(string cacheId) => _collectedCaches.Add(cacheId);

        public bool TryAddRelic(string relicId) => _relics.Add(relicId);
        public bool HasRelic(string relicId) => _relics.Contains(relicId);
        public int RelicCount => _relics.Count;

        /// <summary>Finale gate (§8.4): 60% countable coverage + all five relays + main sequence.</summary>
        public bool MeetsFinaleMapRequirement() => MapCoveragePercent >= FinaleMapCoveragePercent;

        public void Restore(
            string revealedBase64, string surveyedBase64, string countableMaskBase64,
            IEnumerable<string> discoveredLandmarkIds, IEnumerable<string> collectedCacheIds,
            IEnumerable<string> relicIds, int mappingRewardsPaidThroughCount)
        {
            RevealedCells = BitGrid.FromBase64(WorldConstants.MapCellsPerSide, WorldConstants.MapCellsPerSide, revealedBase64);
            SurveyedCells = BitGrid.FromBase64(WorldConstants.MapCellsPerSide, WorldConstants.MapCellsPerSide, surveyedBase64);
            // DEF-008: an empty payload means "save written before the mask was persisted" — keep the
            // freshly baked mask rather than falling back to "every cell counts".
            if (!string.IsNullOrEmpty(countableMaskBase64))
                CountableMask = BitGrid.FromBase64(WorldConstants.MapCellsPerSide, WorldConstants.MapCellsPerSide, countableMaskBase64);
            else if (CountableMask == null || CountableMask.CountSet() == CountableMask.Width * CountableMask.Height)
                BakeCountableMask();
            MappingRewardsPaidThroughCount = Math.Max(0, mappingRewardsPaidThroughCount);

            _discoveredLandmarks.Clear();
            if (discoveredLandmarkIds != null) foreach (var id in discoveredLandmarkIds) _discoveredLandmarks.Add(id);
            _collectedCaches.Clear();
            if (collectedCacheIds != null) foreach (var id in collectedCacheIds) _collectedCaches.Add(id);
            _relics.Clear();
            if (relicIds != null) foreach (var id in relicIds) _relics.Add(id);
        }

        public IEnumerable<string> DiscoveredLandmarkIds => _discoveredLandmarks;
        public IEnumerable<string> CollectedCacheIds => _collectedCaches;
        public IEnumerable<string> RelicIds => _relics;
    }

    /// <summary>
    /// Optional contracts (§9.3): 30 authored (C_F01..06, C_C01..06, C_U01..06, C_W01..06, C_H01..06)
    /// plus 5 repeatable job templates. At most three available jobs at a garage; refresh after an
    /// expedition ends, not wall clock. No daily streaks or cooldown walls.
    /// </summary>
    public sealed class ContractService
    {
        public enum Pattern
        {
            Triangulate,        // visit 3 survey points
            GentleDelivery,     // 30 kg crate; heavy collision loses optional bonus only
            HiddenVista,        // viewpoint from textual clue
            InfrastructureCheck, // inspect 4 assets
            FieldSample,        // collect 3 samples
            OldRoute            // ordered checkpoints, no timer
        }

        public sealed class ContractDefinition
        {
            public ContentId Id;
            public Pattern Pattern;
            public int RegionIndex;   // 0..4 → reward = baseForest + 100 × regionIndex
            public int BaseRewardSalvage;
            public readonly List<ContentId> Targets = new List<ContentId>();
        }

        public static int RewardFor(Pattern pattern, int regionIndex)
        {
            int baseForest = pattern switch
            {
                Pattern.Triangulate => 250,
                Pattern.GentleDelivery => 300,
                Pattern.HiddenVista => 300,
                Pattern.InfrastructureCheck => 300,
                Pattern.FieldSample => 350,
                Pattern.OldRoute => 400,
                _ => 300
            };
            return baseForest + 100 * regionIndex;
        }

        public const int MaxAvailableJobs = 3;
        public const int RepeatableJobRewardMin = 150;
        public const int RepeatableJobRewardMax = 350;

        readonly HashSet<string> _completed = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _accepted = new HashSet<string>(StringComparer.Ordinal);

        public bool IsCompleted(string contractId) => _completed.Contains(contractId);
        public bool IsAccepted(string contractId) => _accepted.Contains(contractId);

        /// <summary>Sorted ids for deterministic saves.</summary>
        public IEnumerable<string> AcceptedIds => Sorted(_accepted);
        public IEnumerable<string> CompletedIds => Sorted(_completed);

        static List<string> Sorted(HashSet<string> set)
        {
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public void Restore(IEnumerable<string> accepted, IEnumerable<string> completed)
        {
            _accepted.Clear();
            _completed.Clear();
            if (completed != null) foreach (var id in completed) if (!string.IsNullOrEmpty(id)) _completed.Add(id);
            if (accepted != null)
                foreach (var id in accepted)
                    if (!string.IsNullOrEmpty(id) && !_completed.Contains(id)) _accepted.Add(id);
        }

        public CommandResult Accept(string contractId)
        {
            if (_accepted.Contains(contractId) || _completed.Contains(contractId))
                return CommandResult.Fail(FailureReason.AlreadyApplied);
            _accepted.Add(contractId);
            return CommandResult.Ok();
        }

        /// <summary>Marks a contract finished without touching the wallet. The reward is paid by the
        /// CompleteContract transaction, which also escrows it (DEF-007).</summary>
        public void MarkCompleted(string contractId)
        {
            _completed.Add(contractId);
            _accepted.Remove(contractId);
        }

        /// <summary>
        /// Legacy direct-pay path — superseded by the CompleteContract transaction. Retained for the
        /// tests that pin it; gameplay must not use it, because it grants outside the idempotent
        /// commit/rollback path and emits no event (DEF-007).
        /// </summary>
        public CommandResult Complete(string contractId, EconomyService economy, ClaimedRewardLedger ledger,
            int rewardSalvage, string rewardKey)
        {
            if (!_accepted.Contains(contractId))
                return CommandResult.Fail(FailureReason.MissionNotAccepted);
            if (!ledger.TryClaim(rewardKey))
            {
                _completed.Add(contractId);
                return CommandResult.Fail(FailureReason.AlreadyApplied);
            }
            economy.Grant(rewardSalvage);
            _completed.Add(contractId);
            _accepted.Remove(contractId);
            return CommandResult.Ok();
        }

        /// <summary>Repeatable job reward from validated route length (150–350, §9.3).</summary>
        public static int RepeatableReward(float validatedRouteLengthMeters)
        {
            // 0–3 km mapped linearly into the allowed band; deterministic, no rerolls.
            float t = MathUtil.Clamp01(validatedRouteLengthMeters / 3000f);
            return RepeatableJobRewardMin + (int)Math.Round(t * (RepeatableJobRewardMax - RepeatableJobRewardMin));
        }
    }
}
