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
            float before = RevealedCountableCells;

            RevealedCells.RevealCircle(worldX, worldZ, radius, WorldConstants.MapCellSizeMeters,
                (x, z) => CountableMask.Get(x, z) && (lineOfSightFilter == null || lineOfSightFilter(x, z)));

            if (fromVista)
                SurveyedCells.RevealCircle(worldX, worldZ, radius, WorldConstants.MapCellSizeMeters,
                    (x, z) => CountableMask.Get(x, z));

            int newly = RevealedCountableCells - (int)before;
            PayMappingThresholds();
            return (int)newly;
        }

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
            out GameEvent discovered)
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

            discovered = new LandmarkDiscovered(default, new ContentId(landmarkId), rewardKey,
                LandmarkDiscoveryRewardSalvage);
            return true;
        }

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
            if (!string.IsNullOrEmpty(countableMaskBase64))
                CountableMask = BitGrid.FromBase64(WorldConstants.MapCellsPerSide, WorldConstants.MapCellsPerSide, countableMaskBase64);
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

        public CommandResult Accept(string contractId)
        {
            if (_accepted.Contains(contractId) || _completed.Contains(contractId))
                return CommandResult.Fail(FailureReason.AlreadyApplied);
            _accepted.Add(contractId);
            return CommandResult.Ok();
        }

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
