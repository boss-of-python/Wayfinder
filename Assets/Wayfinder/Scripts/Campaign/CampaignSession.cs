using System;
using System.Collections.Generic;
using System.Globalization;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;

namespace Wayfinder.Campaign
{
    /// <summary>
    /// Per-save-slot campaign session: the composition root for the domain services (§15.3) and
    /// the expedition lifecycle (§2.3). It owns ONE instance of every progression service, so the
    /// three save slots never share state (no static singletons, KI-023).
    ///
    /// Persistence model: the whole campaign is a snapshot (<see cref="SavePayload"/>). Critical
    /// operations (new game, depart, extraction/banking, rescue, research, crafting, mission accept,
    /// camp establishment/deposit, relay activation, quest items) run as commit-or-rollback units:
    /// mutate in memory → write + verify the save → only then acknowledge. If the write fails the
    /// in-memory state is restored from the pre-operation snapshot and the caller gets a typed
    /// SaveIoFailure — success is never claimed without a verified save (§12.4, §17.4, QA case 4).
    /// Field progress (pickups, discoveries, map reveal, pose) is autosaved on a 15 s debounce.
    /// </summary>
    public sealed partial class CampaignSession
    {
        public static readonly InventoryId Cargo = new InventoryId("cargo");
        public static readonly InventoryId GarageStorage = new InventoryId("garageStorage");
        public static readonly InventoryId CampStorage = new InventoryId("campStorage");

        public const int GarageStorageSlots = 10000;          // "Garage storage is unlimited" (§10.4)
        public const float GarageStorageMassKg = 1000000f;
        public const int CampStorageSlots = 10000;            // camp storage is mass-limited (500 kg)
        public const string TutorialFlag = "tutorial.active";
        public const string ScoutVehicleId = "vehicle.scout.s1";
        public const float MaxPlausibleStepMeters = 50f;      // larger pose jumps are resets, not distance

        /// <summary>Starting loadout (§10.3): 2 fuel cans, 2 repair kits, 2 tire patches, 1 boards.</summary>
        public static readonly (string itemId, int quantity)[] StartingLoadout =
        {
            ("fuel_can", 2), ("repair_kit", 2), ("tire_patch", 2), ("recovery_boards", 1)
        };

        readonly SaveService _saves;
        readonly AutosavePolicy _autosave = new AutosavePolicy();
        readonly List<CampaignNotice> _notices = new List<CampaignNotice>();
        List<CampaignNotice> _pending;

        public CampaignContent Content { get; }
        public SaveSlotId Slot { get; }
        public EconomyService Economy { get; } = new EconomyService();
        public InventoryService Inventory { get; } = new InventoryService();
        public DiscoveryService Discovery { get; } = new DiscoveryService();
        public MissionService Missions { get; } = new MissionService();
        public ResearchService Research { get; }
        public GarageService Garage { get; } = new GarageService();
        public ContractService Contracts { get; } = new ContractService();
        public TransactionService Transactions { get; }
        public SimulationTime Clock { get; } = new SimulationTime();
        public DamageModel Damage { get; } = new DamageModel();
        public VehicleSpec Vehicle { get; }
        public ExpeditionState Expedition { get; } = new ExpeditionState();

        public float FuelLiters { get; internal set; }
        /// <summary>Researched tank capacity (cached by <see cref="ConfigureContainers"/>; avoids per-frame resolves).</summary>
        public float FuelCapacityLiters { get; private set; }
        public GameMode Mode { get; internal set; } = GameMode.Expedition;
        public string WeatherState { get; internal set; } = "Clear";
        public int WeatherSeed { get; internal set; } = 9871;
        public int Revision { get; internal set; }
        public SaveResult LastSaveResult { get; private set; }
        public bool HasUnsavedChanges => _autosave.IsDirty;
        public bool LastSaveFailed => _autosave.LastAttemptFailed;
        public string ActiveVehicleId => ScoutVehicleId;
        public int PendingNoticeCount => _notices.Count;
        public bool InGarage => !Expedition.IsActive;
        public bool TutorialActive => TutorialFlags.Contains(TutorialFlag);

        internal readonly SortedSet<string> TutorialFlags = new SortedSet<string>(StringComparer.Ordinal);
        internal int NextSequence;

        CampaignSession(SaveService saves, CampaignContent content, SaveSlotId slot)
        {
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Slot = slot;
            Vehicle = VehicleSpec.ScoutS1();
            Research = new ResearchService(code => Missions.IsCompletedCode(code));
            Discovery.BindEconomy(Economy);
            // Land-only coverage from the first frame (DEF-008); a loaded save overrides it.
            Discovery.BakeCountableMask();
            Transactions = new TransactionService(Economy, Inventory, new TransactionDependencies
            {
                Research = Research,
                Discovery = Discovery,
                Garage = Garage,
                Pickups = content,
                Contracts = Contracts
            });
            Transactions.OnCommitted += OnTransactionCommitted;
            ConfigureContainers();
        }

        // ================================================================== open / slots

        /// <summary>
        /// Creates a new campaign in <paramref name="slot"/> and saves it before any movement is
        /// allowed (§17.5). An occupied slot is replaced — the UI must confirm first (§17.5).
        /// </summary>
        public static CampaignOpenResult StartNew(SaveService saves, CampaignContent content, SaveSlotId slot,
            GameMode mode = GameMode.Expedition)
        {
            if (saves == null) throw new ArgumentNullException(nameof(saves));
            var session = new CampaignSession(saves, content, slot) { Mode = mode };
            session.FuelLiters = session.FuelCapacityLiters;
            foreach (var (itemId, quantity) in StartingLoadout)
                session.Inventory.Add(Cargo, new ItemStack(new ContentId(itemId), quantity));
            // The onboarding mission starts accepted so its one-time objectives can never be missed (D-022).
            session.Missions.Accept("Q01", session.WorldProgress);
            session.TutorialFlags.Add(TutorialFlag);
            session.Expedition.LastGarageId = content.Slice.GarageId;
            session.Expedition.Pose = content.Slice.GarageSpawn;
            session.Expedition.LastSafePose = content.Slice.GarageSpawn;

            saves.DeleteSlot(slot); // fresh revision history: an old game's backups must never outrank it
            var save = session.PersistNow();
            return new CampaignOpenResult(session,
                save.Success ? CampaignOpenStatus.NewGameSaved : CampaignOpenStatus.NewGameSaveFailed, save.Detail);
        }

        /// <summary>Loads the best valid revision (current → backup1 → backup2, §17.4) and migrates it.</summary>
        public static CampaignOpenResult Continue(SaveService saves, CampaignContent content, SaveSlotId slot)
        {
            if (saves == null) throw new ArgumentNullException(nameof(saves));
            var (status, envelope, detail) = saves.LoadBestValid(slot);
            switch (status)
            {
                case LoadResultStatus.NoSave:
                    return new CampaignOpenResult(null, CampaignOpenStatus.NoSave, detail);
                case LoadResultStatus.SchemaTooNewReadOnly:
                    return new CampaignOpenResult(null, CampaignOpenStatus.SchemaTooNew, detail);
                case LoadResultStatus.LoadedCurrent:
                case LoadResultStatus.LoadedBackup:
                    break;
                default:
                    return new CampaignOpenResult(null, CampaignOpenStatus.Failed, detail);
            }
            envelope = SaveMigrations.MigrateToCurrent(envelope);
            var session = new CampaignSession(saves, content, slot);
            CampaignSnapshot.Apply(session, envelope.payload);
            session.Revision = envelope.revision;
            return new CampaignOpenResult(session,
                status == LoadResultStatus.LoadedBackup ? CampaignOpenStatus.LoadedBackup : CampaignOpenStatus.LoadedCurrent,
                status == LoadResultStatus.LoadedBackup ? "current save unreadable; restored the newest valid backup" : detail);
        }

        public static SlotSummary PeekSlot(SaveService saves, SaveSlotId slot)
        {
            if (saves == null) throw new ArgumentNullException(nameof(saves));
            var (status, envelope, detail) = saves.LoadBestValid(slot);
            var summary = new SlotSummary { Slot = slot, Status = status, Detail = detail ?? string.Empty };
            if (envelope?.payload == null) return summary;
            var p = envelope.payload;
            summary.HasSave = true;
            summary.Revision = envelope.revision;
            summary.SavedAtUtc = envelope.savedAtUtc ?? string.Empty;
            summary.BankedSalvage = p.wallet?.salvage ?? 0;
            summary.CompletedMissions = p.completedMissionIds?.Count ?? 0;
            summary.LandmarksDiscovered = p.world?.discoveredLandmarkIds?.Count ?? 0;
            summary.ExpeditionActive = string.Equals(p.expedition?.status, nameof(ExpeditionStatus.Active), StringComparison.Ordinal);
            return summary;
        }

        /// <summary>Where to place the vehicle after Continue: last safe pose, else last pose, else the garage (§17.4).</summary>
        public Pose ResumePose
        {
            get
            {
                if (!Expedition.IsActive) return SpawnFor(Expedition.LastGarageId);
                if (Expedition.LastSafePose.IsSet) return Expedition.LastSafePose;
                return Expedition.Pose.IsSet ? Expedition.Pose : SpawnFor(Expedition.LastGarageId);
            }
        }

        // ================================================================== persistence / lifecycle

        /// <summary>Drives the autosave debounce with unscaled real time.</summary>
        public AutosaveTickResult Tick(float unscaledDeltaSeconds)
        {
            if (!_autosave.Tick(unscaledDeltaSeconds)) return AutosaveTickResult.Idle;
            return PersistNow().Success ? AutosaveTickResult.Saved : AutosaveTickResult.Failed;
        }

        /// <summary>App suspend (§16.2): save immediately when there is anything unsaved.</summary>
        public SaveResult? OnSuspend() => _autosave.IsDirty ? PersistNow() : (SaveResult?)null;

        public IReadOnlyList<CampaignNotice> DrainNotices()
        {
            var copy = _notices.ToArray();
            _notices.Clear();
            return copy;
        }

        /// <summary>The exact payload a save would write now (used by tests and diagnostics).</summary>
        public SavePayload CaptureSnapshot() => CampaignSnapshot.Capture(this);

        SaveResult PersistNow()
        {
            var payload = CampaignSnapshot.Capture(this);
            var result = _saves.Save(Slot, payload, Revision + 1);
            LastSaveResult = result;
            if (result.Success)
            {
                Revision = result.Revision;
                _autosave.MarkSaved();
            }
            else
            {
                _autosave.MarkFailed();
                _notices.Add(new CampaignNotice(CampaignNoticeKind.SaveFailed,
                    "Save failed — progress is kept in memory; retry or continue without saving", 0, result.Detail));
            }
            return result;
        }

        /// <summary>
        /// Commit-or-rollback wrapper for critical operations. Notices raised inside are published
        /// only after the verified save, so the HUD never reports work that was rolled back.
        /// An exception escaping <see cref="SaveService.Save"/> models process death and propagates
        /// without rollback (the next boot loads whatever complete file is on disk).
        /// </summary>
        CommandResult RunCritical(Func<CommandResult> mutate)
        {
            if (_pending != null) throw new InvalidOperationException("critical operations cannot nest");
            var before = CampaignSnapshot.Capture(this);
            var pending = new List<CampaignNotice>();
            _pending = pending;
            try
            {
                CommandResult result;
                try
                {
                    result = mutate();
                }
                catch
                {
                    CampaignSnapshot.Apply(this, before);
                    throw;
                }
                if (!result.Success)
                {
                    CampaignSnapshot.Apply(this, before);
                    return result;
                }
                _pending = null; // PersistNow's failure notice must survive the rollback below
                var save = PersistNow();
                if (!save.Success)
                {
                    CampaignSnapshot.Apply(this, before);
                    return CommandResult.Fail(FailureReason.SaveIoFailure, save.Detail);
                }
                _notices.AddRange(pending);
                return result;
            }
            finally
            {
                _pending = null;
            }
        }

        // ================================================================== helpers

        internal void ConfigureContainers()
        {
            var stats = ResolveStats();
            FuelCapacityLiters = stats.FuelCapacityLiters;
            Inventory.ConfigureContainer(Cargo, stats.CargoSlots, stats.PayloadKg);
            Inventory.ConfigureContainer(GarageStorage, GarageStorageSlots, GarageStorageMassKg);
            Inventory.ConfigureContainer(CampStorage, CampStorageSlots, CampService.CampStorageCapacityKg);
        }

        /// <summary>
        /// Configures any stat resolver — the garage display's or the Unity <c>VehicleController</c>'s —
        /// with exactly the researched modules, so researched torque, grip, wading depth, fuel drain
        /// and tank size reach physics (KI-025, D-030). Clears first: calling it after every research,
        /// load or extraction can never double-apply a bonus (QA case 8).
        /// </summary>
        public void ConfigureResolver(VehicleStatsResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            resolver.Clear();
            Research.ApplyTo(resolver);
        }

        /// <summary>The single authoritative stat resolution shared by garage display and driving (§11.2).</summary>
        public EffectiveVehicleStats ResolveStats()
        {
            var resolver = new VehicleStatsResolver();
            ConfigureResolver(resolver);
            return resolver.Resolve(Vehicle, FuelLiters, Inventory.TotalMassKg(Cargo), TireSet.AllTerrain, SurfaceType.PackedDirt,
                false, Damage.EngineCondition, Damage.SuspensionCondition, Damage.TiresCondition, false, false, false);
        }

        internal TransactionId NextTransactionId()
        {
            string scope = Expedition.IsActive ? Expedition.Id : "hub-" + Slot.Index.ToString(CultureInfo.InvariantCulture);
            var id = TransactionId.ExpeditionSequence(scope, NextSequence);
            NextSequence++;
            return id;
        }

        internal Pose SpawnFor(string garageId)
        {
            if (garageId == Content.Slice.GarageId) return Content.Slice.GarageSpawn;
            return new Pose(Content.GaragePosition(garageId), 0f);
        }

        string NearestUnlockedGarage(WorldPoint from)
        {
            var ids = new List<string>(Garage.UnlockedGarages);
            ids.Sort(StringComparer.Ordinal);
            string best = Content.Slice.GarageId;
            float bestDistance = float.MaxValue;
            foreach (var id in ids)
            {
                float d = Content.GaragePosition(id).DistanceXZ(from);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = id;
                }
            }
            return best;
        }

        bool IsKnownUpgrade(string upgradeId)
        {
            foreach (var tier in Research.Tiers)
                if (tier.Id.Value == upgradeId) return true;
            return false;
        }

        int MaterialUnits(InventoryId container)
        {
            int n = 0;
            foreach (var s in Inventory.Stacks(container))
                if (ItemCatalog.CategoryOf(s.ItemId.Value) == ItemCategory.Material) n += s.Quantity;
            return n;
        }

        /// <summary>Accept-time reconciliation: objectives already satisfied by persistent world state.</summary>
        internal int WorldProgress(MissionObjective objective)
        {
            string target = objective.TargetId.Value;
            switch (objective.Type)
            {
                case MissionConditionType.DiscoverLandmark:
                    foreach (var id in Discovery.DiscoveredLandmarkIds)
                        if (id == target) return 1;
                    return 0;
                case MissionConditionType.EstablishCamp:
                    return Garage.IsCampActivated(target) ? 1 : 0;
                case MissionConditionType.ActivateRelay:
                    return !string.IsNullOrEmpty(target) && Garage.HasInfrastructure(target) ? 1 : 0;
                case MissionConditionType.CollectQuestItem:
                    if (target == ItemCatalog.SalvageBundle) return 0; // "collect from a cache" means a new pickup
                    if (Inventory.ProtectedLedgerContains(target)) return 1;
                    return Inventory.QuantityOf(Cargo, target) + Inventory.QuantityOf(GarageStorage, target);
                case MissionConditionType.RevealMapPercent:
                    return (int)Discovery.MapCoveragePercent;
                default:
                    return 0;
            }
        }

        int ReportMission(MissionConditionType type, string target)
        {
            var readyBefore = Missions.CodesInState(MissionState.ReadyToTurnIn);
            int advanced = Missions.ReportCondition(type, target);
            if (advanced == 0) return 0;
            Notice(CampaignNoticeKind.ObjectiveUpdated, "Objective updated", advanced, target);
            AnnounceReadyMissions(readyBefore);
            return advanced;
        }

        int CountReady() => Missions.CodesInState(MissionState.ReadyToTurnIn).Count;

        void AnnounceReadyMissions(List<string> readyBefore)
        {
            foreach (var code in Missions.CodesInState(MissionState.ReadyToTurnIn))
            {
                if (readyBefore != null && readyBefore.Contains(code)) continue;
                var hub = Missions.Definitions[code].TurnInHub.Value;
                Notice(CampaignNoticeKind.MissionReady, $"{code} ready — return to {hub}", 0, code);
            }
        }

        void Notice(CampaignNoticeKind kind, string text, int amount = 0, string id = null)
        {
            var notice = new CampaignNotice(kind, text, amount, id);
            if (_pending != null) _pending.Add(notice);
            else _notices.Add(notice);
        }

        void OnTransactionCommitted(GameEvent committed)
        {
            switch (committed)
            {
                case PickupCollected pickup:
                    Notice(CampaignNoticeKind.CacheCollected, $"Cache: +{pickup.SalvageUnits} Salvage in cargo",
                        pickup.SalvageUnits, pickup.PickupId.Value);
                    break;
                case CampEstablished camp:
                    Notice(CampaignNoticeKind.CampEstablished, "Camp established — save, deposit and rest here", 0, camp.CampId.Value);
                    break;
                case UpgradeResearched upgrade:
                    Notice(CampaignNoticeKind.UpgradeResearched, $"Researched {upgrade.UpgradeId.Value}", 0, upgrade.UpgradeId.Value);
                    break;
            }
        }
    }
}
