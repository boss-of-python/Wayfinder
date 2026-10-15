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
        public List<ItemStackDto> campStorage = new List<ItemStackDto>();
        public List<string> claimedRewardKeys = new List<string>();
        public List<string> appliedTransactionIds = new List<string>();
        public SimulationDto simulation = new SimulationDto();
        public List<string> tutorialFlags = new List<string>();
        public List<ActiveContractDto> acceptedContracts = new List<ActiveContractDto>();
        public List<string> completedContractIds = new List<string>();
        public List<string> protectedLedgerItemIds = new List<string>();
        public List<string> relicIds = new List<string>();
        public List<string> ownedVehicleIds = new List<string> { "vehicle.scout.s1" };
        /// <summary>
        /// Next append-only transaction sequence number (§2.3). Persisted so ids issued after a
        /// reload can never collide with ids already in appliedTransactionIds — a collision would
        /// turn a legitimate new pickup into a silent idempotent no-op. Additive field (D-021).
        /// </summary>
        public int nextTransactionSequence;
    }

    [Serializable] public sealed class WalletDto { public int salvage; public int escrowedSalvage; }
    [Serializable] public sealed class ConditionDto { public int engine = 100; public int suspension = 100; public int tires = 100; public int body = 100; }

    [Serializable]
    public sealed class VehicleDto
    {
        public string id;
        public float fuelLiters;
        public ConditionDto condition = new ConditionDto();
        public List<string> equippedModuleIds = new List<string>();
        public List<string> unlockedTireSets = new List<string>();
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
