using System;
using System.Collections.Generic;

namespace Wayfinder.Persistence
{
    /// <summary>
    /// Save envelope (§17.3). Human-readable JSON during development; checksum detects
    /// accidental corruption (not malicious edits). Single-player — no encryption needed.
    /// </summary>
    [Serializable]
    public sealed class SaveEnvelope
    {
        public int schemaVersion = SaveService.CurrentSchemaVersion;
        public string contentVersion = "1.0.0";
        public int generatorVersion = 1;
        public int worldSeed = 481516;
        public string slotId = "slot-0";
        public int revision;
        public string savedAtUtc;
        public string payloadChecksum;
        public SavePayload payload;
    }

    [Serializable]
    public sealed class SavePayload
    {
        public WalletDto wallet = new WalletDto();
        public string activeVehicleId = "vehicle.scout.s1";
        public List<VehicleDto> vehicles = new List<VehicleDto>();
        public List<string> researchedUpgradeIds = new List<string>();
        public List<string> unlockedGarageIds = new List<string>();
        public List<string> activatedCampIds = new List<string>();
        public List<string> infrastructureFlags = new List<string>();
        public List<string> completedMissionIds = new List<string>();
        public List<ActiveMissionDto> activeMissions = new List<ActiveMissionDto>();
        public WorldStateDto world = new WorldStateDto();
        public ExpeditionDto expedition = new ExpeditionDto();
        public List<ItemStackDto> garageStorage = new List<ItemStackDto>();
        /// <summary>
        /// v1 pooled camp store, superseded by <see cref="campStores"/> in schema 2 (D-039, §12.2).
        /// Kept so a v1 file still deserializes; <c>SaveMigrations</c> step 1 moves its stacks into a
        /// per-camp store (or into garage storage when no camp was established) and leaves this empty.
        /// Nothing is ever dropped (§17.5).
        /// </summary>
        public List<ItemStackDto> campStorage = new List<ItemStackDto>();
        /// <summary>One store per activated camp (§12.2): what you leave at camp A is not at camp B. Schema 2.</summary>
        public List<CampStoreDto> campStores = new List<CampStoreDto>();
        public List<string> claimedRewardKeys = new List<string>();
        public List<string> appliedTransactionIds = new List<string>();
        public SimulationDto simulation = new SimulationDto();
        public List<string> tutorialFlags = new List<string>();
        public List<ActiveContractDto> acceptedContracts = new List<ActiveContractDto>();
        public List<string> completedContractIds = new List<string>();
        public List<string> protectedLedgerItemIds = new List<string>();
        public List<string> relicIds = new List<string>();
        public List<string> ownedVehicleIds = new List<string> { "vehicle.scout.s1" };
        public List<string> equippedCosmeticIds = new List<string>();
        public List<string> deliveredSiteIds = new List<string>();
        public List<string> visitedVolumeIds = new List<string>();
        public int repeatableCompletionCount;
        public bool campaignCompleted;
        /// <summary>
        /// Next append-only transaction sequence number (§2.3). Persisted so ids issued after a
        /// reload can never collide with ids already in appliedTransactionIds — a collision would
        /// turn a legitimate new pickup into a silent idempotent no-op. Additive field (D-021).
        /// </summary>
        public int nextTransactionSequence;
    }

    [Serializable]
    public sealed class WalletDto
    {
        public int salvage;
        public int escrowedSalvage;
        /// <summary>Escrow only the matching hub may bank (DEF-017). Null on older saves.</summary>
        public List<EscrowEntryDto> escrowByHub;
    }
    /// <summary>One hub's pending escrow (DEF-017). Additive: an older save simply has none.</summary>
    [Serializable] public sealed class EscrowEntryDto { public string hubId; public int amount; }
    [Serializable] public sealed class ConditionDto { public int engine = 100; public int suspension = 100; public int tires = 100; public int body = 100; }

    [Serializable]
    public sealed class VehicleDto
    {
        public string id;
        public float fuelLiters;
        public ConditionDto condition = new ConditionDto();
        public List<string> equippedModuleIds = new List<string>();
        public List<string> unlockedTireSets = new List<string>();
        /// <summary>
        /// The tire set mounted on this vehicle (§10.2: one tire set, mutually exclusive). Additive —
        /// an older save has none and mounts all-terrain (D-021, D-041), so no schema bump is needed.
        /// </summary>
        public string equippedTireSet = "AllTerrain";
    }

    [Serializable]
    public sealed class ActiveMissionDto
    {
        public string id;
        public string state;
        public List<int> objectiveCounters = new List<int>();
    }

    [Serializable]
    public sealed class ActiveContractDto
    {
        public string id;
        public string state;
        public List<int> objectiveCounters = new List<int>();
    }

    [Serializable]
    public sealed class WorldStateDto
    {
        public string revealedCellsBase64 = "";
        public string surveyedCellsBase64 = "";
        public string countableMaskBase64 = "";
        public List<string> discoveredLandmarkIds = new List<string>();
        public List<string> collectedCacheIds = new List<string>();
        public List<string> relicIds = new List<string>();
        public int mappingRewardsPaidThroughCount;
        /// <summary>Player map markers, at most ten (§13.4), in placement order. Additive (D-021, D-027).</summary>
        public List<MapMarkerDto> customMarkers = new List<MapMarkerDto>();
        /// <summary>Next marker id suffix, so a removed marker's id is never reused. Additive (D-027).</summary>
        public int nextCustomMarkerIndex = 1;
        /// <summary>Marker the route line points at, or empty. Additive (D-027).</summary>
        public string routeTargetMarkerId = "";
    }

    [Serializable]
    public sealed class MapMarkerDto
    {
        public string id;
        public float x, z;
        public string label = "";
    }

    [Serializable]
    public sealed class PoseDto
    {
        public float x, y, z, yawDegrees;
    }

    [Serializable]
    public sealed class ExpeditionDto
    {
        public string id = "";
        public string status = "None"; // None | Active | Ended
        public string lastGarageId = "garage.home";
        public PoseDto pose = new PoseDto();
        public PoseDto lastSafePose = new PoseDto();
        public List<ItemStackDto> cargo = new List<ItemStackDto>();
        public List<string> pendingRewardKeys = new List<string>();
        public float elapsedSimulationSeconds;
        /// <summary>Expedition summary counters (§2.3: distance, discoveries, fuel used). Additive (D-021).</summary>
        public ExpeditionStatsDto stats = new ExpeditionStatsDto();
    }

    [Serializable]
    public sealed class ExpeditionStatsDto
    {
        public float distanceMeters;
        public float fuelUsedLiters;
        public int cachesCollected;
        public int landmarksDiscovered;
        public int cellsRevealed;
    }

    /// <summary>One camp's own store (§12.2, schema 2). Additive at v2; v1 saves carry pooled campStorage.</summary>
    [Serializable]
    public sealed class CampStoreDto
    {
        public string campId;
        public List<ItemStackDto> items = new List<ItemStackDto>();
    }

    [Serializable]
    public sealed class ItemStackDto
    {
        public string itemId;
        public int quantity;
        public string missionInstanceId = "";
    }

    [Serializable]
    public sealed class SimulationDto
    {
        public float dayTimeSeconds;
        public string weatherState = "Clear";
        public int weatherSeed = 9871;
        public string gameMode = "Expedition";
    }

    /// <summary>Settings are versioned separately so audio/language changes never rewrite progression (§17.3).</summary>
    [Serializable]
    public sealed class SettingsFile
    {
        public int schemaVersion = 1;
        public string language = "en";
        public float uiScale = 1.0f;          // 100–150% (§3.5)
        public bool highContrast;
        public bool subtitlesEnabled = true;
        public float subtitleSize = 1.0f;
        public float musicVolume = 0.8f;
        public float sfxVolume = 1.0f;
        public bool tiltSteering;
        public bool holdToInteract = true;     // false = tap alternative
        public bool leftHandedLayout;
        public bool hapticsEnabled = true;
        public bool reducedCameraMotion;
        public string qualityTier = "Balanced";
    }
}
