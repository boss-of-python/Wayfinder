using System;
using System.Collections.Generic;
using Wayfinder.Core;
using Wayfinder.Progression;

namespace Wayfinder.Campaign
{
    public enum FieldPromptKind
    {
        None,
        CollectCache,
        CollectQuestItem,
        Inspect,
        EstablishCamp,
        DepositAtCamp,
        ActivateRelay,
        DeliverQuestItem,
        CollectRelic,
        CompleteContractTarget
    }

    /// <summary>The single interaction candidate offered this frame (§16.4: one candidate, by distance).</summary>
    public readonly struct FieldPrompt
    {
        public readonly FieldPromptKind Kind;
        public readonly string TargetId;
        public readonly string Label;
        public readonly float DistanceMeters;
        public readonly bool Substantial;
        public readonly string BlockedReason;
        public readonly string ContractId;

        public FieldPrompt(FieldPromptKind kind, string targetId, string label, float distance, bool substantial,
            string blocked, string contractId = null)
        {
            Kind = kind;
            TargetId = targetId ?? string.Empty;
            Label = label ?? string.Empty;
            DistanceMeters = distance;
            Substantial = substantial;
            BlockedReason = blocked ?? string.Empty;
            ContractId = contractId ?? string.Empty;
        }

        public bool Visible => Kind != FieldPromptKind.None;
        public bool Available => Visible && BlockedReason.Length == 0;

        internal FieldPrompt WithBlock(string reason) =>
            new FieldPrompt(Kind, TargetId, Label, DistanceMeters, Substantial, reason, ContractId);
    }

    /// <summary>Per-frame inputs supplied by the presentation layer (vehicle + touch/keyboard state).</summary>
    public struct FieldInput
    {
        public Pose Pose;
        public float SpeedKph;
        /// <summary>Simulation delta (0 while paused) — fuel, observation and holds never advance while paused.</summary>
        public float DeltaSeconds;
        public float FuelLiters;
        public int EngineCondition, SuspensionCondition, TiresCondition, BodyCondition;
        public bool Grounded;
        public float RollDegrees;
        public bool InWater, InDeepMud;
        public float ClearanceMeters;
        public bool InteractPressed;
        public bool TapToInteract;
    }

    /// <summary>Results of one field frame. The instance is reused; read it before the next Update.</summary>
    public sealed class FieldFrame
    {
        public FieldPrompt Prompt;
        public float HoldProgress01;
        public bool InteractionCommitted;
        public CommandResult InteractionResult;
        public string ObservingLandmarkId = string.Empty;
        public float ObservationSeconds;
        public string DiscoveredLandmarkId = string.Empty;
        public int CellsRevealed;
        public bool ArrivalTriggered;
        public ExpeditionSummary Extraction;

        internal void Reset()
        {
            Prompt = default;
            HoldProgress01 = 0f;
            InteractionCommitted = false;
            InteractionResult = CommandResult.Ok();
            ObservingLandmarkId = string.Empty;
            ObservationSeconds = 0f;
            DiscoveredLandmarkId = string.Empty;
            CellsRevealed = 0;
            ArrivalTriggered = false;
            Extraction = null;
        }
    }

    /// <summary>
    /// Engine-free field loop across the full 6,144 m × 6,144 m mainland (§2.3, §8, §9, §12, §16.4):
    /// telemetry → session, safe-pose sampling, 2 Hz map reveal cadence, passive landmark observation
    /// across all 100 landmarks, ordered route checkpoints across all mainland routes, visit volumes
    /// (including Frontier Observatory in Q20), single nearest hold/tap interaction across all caches,
    /// quest items, inspect sites, deliveries, relays, camps, relics, and contract sockets, and multi-hub
    /// arrival & settlement at all 5 regional garages.
    /// </summary>
    public sealed class SliceFieldController
    {
        public const float InteractionMaxSpeedKph = 15f;
        public const float RevealIntervalSeconds = 0.5f;
        public const float RevealMinTravelMeters = 16f;
        public const float DefaultLandmarkObservationRadiusMeters = 30f;
        public const float DefaultRegionalGarageArrivalRadiusMeters = 18f;

        readonly CampaignSession _session;
        readonly VerticalSliceManifest _slice;
        readonly Dictionary<string, float> _sliceLandmarkRadii = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly ArrivalTracker _arrival = new ArrivalTracker();
        readonly ObservationTracker _observation = new ObservationTracker();
        readonly HoldInteractTracker _hold = new HoldInteractTracker();
        readonly SafePoseSampler _safePose = new SafePoseSampler();
        readonly FieldFrame _frame = new FieldFrame();
        float _sinceReveal;
        WorldPoint _lastReveal;
        bool _hasLastReveal;
        string _heldTarget = string.Empty;
        string _previewTarget = string.Empty;
        int _previewSlots = -1, _previewCapacitySlots = -1;
        float _previewMass = -1f, _previewCapacityMass = -1f;
        CommandResult _previewResult = CommandResult.Ok();

        /// <summary>Optional terrain line-of-sight test (vehicle → landmark). Null = unobstructed.</summary>
        public Func<WorldPoint, WorldPoint, bool> LineOfSight;

        public SliceFieldController(CampaignSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _slice = session.Content.Slice;
            foreach (var l in _slice.Landmarks)
                _sliceLandmarkRadii[l.Id] = l.ObservationRadiusMeters;
            ResetForExpedition();
        }

        public bool InArrivalVolume(WorldPoint p) => InAnyGarageArrivalVolume(p, out _);

        public bool InAnyGarageArrivalVolume(WorldPoint p, out string garageId)
        {
            if (p.DistanceXZ(_slice.ArrivalCenter) <= _slice.ArrivalRadiusMeters)
            {
                garageId = _slice.GarageId;
                return true;
            }
            foreach (var g in _session.Content.Garages.Values)
            {
                if (g.Id == _slice.GarageId) continue;
                if (p.DistanceXZ(g.Position) <= DefaultRegionalGarageArrivalRadiusMeters)
                {
                    garageId = g.Id;
                    return true;
                }
            }
            garageId = string.Empty;
            return false;
        }

        /// <summary>Call after Depart/Continue so arrival needs a fresh exit from the garage volume.</summary>
        public void ResetForExpedition()
        {
            _arrival.Reset(InArrivalVolume(_session.ResumePose.Position));
            _observation.Clear();
            _hold.Reset();
            _hasLastReveal = false;
            _sinceReveal = RevealIntervalSeconds;
            _heldTarget = string.Empty;
            _previewTarget = string.Empty;
        }

        public FieldFrame Update(in FieldInput input)
        {
            _frame.Reset();
            if (!_session.Expedition.IsActive) return _frame;
            float dt = Math.Max(0f, input.DeltaSeconds);
            var position = input.Pose.Position;
            int prevBody = _session.Damage.BodyCondition;

            _session.UpdateVehicle(input.Pose, input.FuelLiters, input.EngineCondition, input.SuspensionCondition,
                input.TiresCondition, input.BodyCondition, dt);
            if (prevBody - _session.Damage.BodyCondition >= 12)
                _session.NotifyHeavyImpact();

            if (_safePose.Update(dt, input.Grounded, input.SpeedKph / 3.6f, input.RollDegrees, input.InWater, input.InDeepMud,
                    input.ClearanceMeters, false))
                _session.RecordSafePose(input.Pose);

            _sinceReveal += dt;
            if (_sinceReveal >= RevealIntervalSeconds &&
                (!_hasLastReveal || position.DistanceXZ(_lastReveal) >= RevealMinTravelMeters))
            {
                _frame.CellsRevealed = _session.RevealAround(position.X, position.Z, vista: false);
                _lastReveal = position;
                _hasLastReveal = true;
                _sinceReveal = 0f;
            }

            UpdateObservation(position, input.SpeedKph, dt);
            UpdateCheckpoints(position);
            UpdateVisitVolumes(position);

            _frame.Prompt = SelectPrompt(position, input.SpeedKph);
            if (_frame.Prompt.TargetId != _heldTarget)
            {
                _hold.Reset();
                _heldTarget = _frame.Prompt.TargetId;
            }
            bool tapMode = input.TapToInteract || (_session.Settings != null && !_session.Settings.holdToInteract);
            bool fire = _hold.Update(input.InteractPressed && _frame.Prompt.Available, _frame.Prompt.Substantial,
                tapMode, dt);
            _frame.HoldProgress01 = _hold.Progress01;
            if (fire)
            {
                _frame.InteractionCommitted = true;
                _frame.InteractionResult = Execute(_frame.Prompt);
            }

            bool inAnyGarage = InAnyGarageArrivalVolume(position, out string garageId);
            if (_arrival.Update(inAnyGarage, input.SpeedKph / 3.6f, dt))
            {
                _frame.ArrivalTriggered = true;
                _frame.Extraction = _session.EndExpedition(garageId);
            }
            return _frame;
        }

        void UpdateObservation(WorldPoint position, float speedKph, float dt)
        {
            string target = null;
            WorldPoint targetPosition = default;
            float best = float.MaxValue;
            foreach (var kv in _session.Content.Landmarks)
            {
                var def = kv.Value;
                if (_session.Discovery.IsLandmarkDiscovered(def.Id)) continue;
                float radius = _sliceLandmarkRadii.TryGetValue(def.Id, out float sr)
                    ? sr : DefaultLandmarkObservationRadiusMeters;
                float d = position.DistanceXZ(def.Position);
                if (d <= radius && d < best)
                {
                    best = d;
                    target = def.Id;
                    targetPosition = def.Position;
                }
            }
            bool lineOfSight = target != null && (LineOfSight == null || LineOfSight(position, targetPosition));
            float held = _observation.Update(target, lineOfSight, speedKph, dt);
            if (target == null) return;
            _frame.ObservingLandmarkId = target;
            _frame.ObservationSeconds = held;
            if (held < DiscoveryService.LandmarkDiscoveryHoldSeconds) return;
            if (_session.DiscoverLandmark(target, speedKph, held).Success)
            {
                _frame.DiscoveredLandmarkId = target;
                _observation.Clear();
            }
        }

        void UpdateCheckpoints(WorldPoint position)
        {
            foreach (var route in _slice.CheckpointRoutes)
                StepRoute(position, route);
            foreach (var route in _session.Content.Routes.Values)
            {
                bool alreadyHandled = false;
                foreach (var sr in _slice.CheckpointRoutes)
                    if (sr.Id == route.Id) { alreadyHandled = true; break; }
                if (!alreadyHandled) StepRoute(position, route);
            }
        }

        void StepRoute(WorldPoint position, SliceCheckpointRoute route)
        {
            for (int i = 0; i < route.Checkpoints.Count; i++)
            {
                if (position.DistanceXZ(route.Checkpoints[i]) > route.RadiusMeters) continue;
                if (_session.NextCheckpointIndex(route.Id) == i) _session.PassCheckpoint(route.Id, i);
            }
        }

        void UpdateVisitVolumes(WorldPoint position)
        {
            foreach (var s in _session.Content.Sites.Values)
            {
                if (s.Kind != "visit" || _session.HasVisitedVolume(s.Id)) continue;
                if (position.DistanceXZ(s.Position) <= s.RadiusMeters)
                    _session.EnterVisitVolume(s.Id);
            }
        }

        FieldPrompt SelectPrompt(WorldPoint position, float speedKph)
        {
            var best = default(FieldPrompt);
            float radius = _slice.InteractionRadiusMeters;

            // Caches across all 5 biomes
            foreach (var kv in _session.Content.Caches)
            {
                string cacheId = kv.Key;
                if (_session.Discovery.IsCacheCollected(cacheId)) continue;
                float d = position.DistanceXZ(kv.Value.Position);
                if (d > radius || (best.Visible && d >= best.DistanceMeters)) continue;
                best = new FieldPrompt(FieldPromptKind.CollectCache, cacheId, "Hold to collect cache", d, false, null);
            }

            // Relics across all 5 biomes
            foreach (var kv in _session.Content.Relics)
            {
                string relicId = kv.Key;
                if (_session.Discovery.HasRelic(relicId)) continue;
                float d = position.DistanceXZ(kv.Value.Position);
                if (d > radius || (best.Visible && d >= best.DistanceMeters)) continue;
                best = new FieldPrompt(FieldPromptKind.CollectRelic, relicId, "Hold to recover relic", d, false, null);
            }

            // Slice & mainland quest item sites
            foreach (var site in _slice.QuestItemSites)
            {
                if (_session.Discovery.IsCacheCollected(site.Id) ||
                    _session.Missions.StateOf(site.MissionCode) != MissionState.Active) continue;
                Consider(ref best, position, site, FieldPromptKind.CollectQuestItem, "Hold to collect mission item", false, null);
            }
            foreach (var site in _session.Content.Sites.Values)
            {
                if (site.Kind == "questItem")
                {
                    if (_session.Discovery.IsCacheCollected(site.Id) ||
                        _session.Missions.StateOf(site.MissionCode) != MissionState.Active) continue;
                    Consider(ref best, position, site, FieldPromptKind.CollectQuestItem, "Hold to collect mission item", false, null);
                }
                else if (site.Kind == "inspect")
                {
                    if (!Near(position, site, best)) continue;
                    if (!_session.IsObjectivePending(MissionConditionType.InspectObject, site.Id)) continue;
                    Consider(ref best, position, site, FieldPromptKind.Inspect, "Hold to inspect", false, null);
                }
                else if (site.Kind == "delivery")
                {
                    if (!Near(position, site, best) || _session.HasSiteDelivered(site.Id)) continue;
                    if (!string.IsNullOrEmpty(site.MissionCode) &&
                        _session.Missions.StateOf(site.MissionCode) != MissionState.Active) continue;
                    bool hasCargo = ItemCatalog.TryGet(site.ItemId, out var itemDef) && itemDef.ProtectedLedger
                        ? _session.Inventory.ProtectedLedgerContains(site.ItemId)
                        : _session.Inventory.QuantityOf(CampaignSession.Cargo, site.ItemId) > 0;
                    Consider(ref best, position, site, FieldPromptKind.DeliverQuestItem, "Hold to deliver mission item", true,
                        hasCargo ? null : $"{DisplayNames.Item(site.ItemId)} required in cargo");
                }
                else if (site.Kind == "relay")
                {
                    if (!Near(position, site, best) || _session.Garage.HasInfrastructure(site.Id)) continue;
                    if (_session.Missions.StateOf(site.MissionCode) != MissionState.Active) continue;
                    bool coreReady = site.Id != "relay.highlands" ||
                                     _session.HasSiteDelivered("delivery.highlands.sensor_core");
                    bool hasFuse = string.IsNullOrEmpty(site.ItemId) ||
                                   _session.Inventory.ProtectedLedgerContains(site.ItemId);
                    string block = !coreReady ? "Sensor core required first"
                        : (!hasFuse ? "Relay fuse required" : null);
                    Consider(ref best, position, site, FieldPromptKind.ActivateRelay, "Hold to restore relay", true, block);
                }
            }

            // Slice inspect targets (preserves exact slice radii)
            foreach (var site in _slice.InspectTargets)
            {
                if (!Near(position, site, best)) continue;
                if (!_session.IsObjectivePending(MissionConditionType.InspectObject, site.Id)) continue;
                Consider(ref best, position, site, FieldPromptKind.Inspect, "Hold to inspect", false, null);
            }

            // Slice & mainland camp sockets
            foreach (var site in _slice.CampSockets)
                ConsiderCamp(ref best, position, site.Id, site.Position, site.RadiusMeters);
            foreach (var camp in _session.Content.Camps.Values)
            {
                if (_slice.FindSite(_slice.CampSockets, camp.Id) != null) continue;
                ConsiderCamp(ref best, position, camp.Id, camp.Position, 10f);
            }

            // Slice relays (preserves exact slice radii)
            foreach (var site in _slice.Relays)
            {
                if (!Near(position, site, best) || _session.Garage.HasInfrastructure(site.Id)) continue;
                if (_session.Missions.StateOf(site.MissionCode) != MissionState.Active) continue;
                Consider(ref best, position, site, FieldPromptKind.ActivateRelay, "Hold to restore relay", true,
                    _session.Inventory.ProtectedLedgerContains(site.ItemId) ? null : "Relay fuse required");
            }

            // Active contract targets
            foreach (var contractId in _session.Contracts.AcceptedIds)
            {
                var targets = _session.ContractTargetsFor(contractId);
                for (int i = 0; i < targets.Count; i++)
                {
                    string tid = targets[i];
                    if (_session.IsContractTargetDone(contractId, tid)) continue;
                    WorldPoint tp;
                    float tr = 12f;
                    if (_session.Content.Sites.TryGetValue(tid, out var ts)) { tp = ts.Position; tr = ts.RadiusMeters; }
                    else if (_session.Content.Landmarks.TryGetValue(tid, out var tl)) { tp = tl.Position; tr = 20f; }
                    else continue;
                    float d = position.DistanceXZ(tp);
                    if (d <= tr && (!best.Visible || d < best.DistanceMeters))
                        best = new FieldPrompt(FieldPromptKind.CompleteContractTarget, tid,
                            $"Hold to survey {contractId}", d, false, null, contractId);
                }
            }

            if (!best.Visible) return best;
            if (best.Kind == FieldPromptKind.CollectCache && best.BlockedReason.Length == 0)
            {
                var preview = CachedPreview(best.TargetId);
                if (!preview.Success) best = best.WithBlock(CapacityText(preview.Reason));
            }
            if (best.BlockedReason.Length == 0 && speedKph > InteractionMaxSpeedKph)
                best = best.WithBlock("Slow down to interact");
            return best;
        }

        void ConsiderCamp(ref FieldPrompt best, WorldPoint position, string campId, WorldPoint campPos, float radius)
        {
            float d = position.DistanceXZ(campPos);
            if (d > radius || (best.Visible && d >= best.DistanceMeters)) return;
            if (_session.Garage.IsCampActivated(campId))
                best = new FieldPrompt(FieldPromptKind.DepositAtCamp, campId, "Hold to deposit & save at camp", d, true, null);
            else
                best = new FieldPrompt(FieldPromptKind.EstablishCamp, campId, "Hold to establish camp", d, true,
                    _session.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.CampKit) > 0
                        ? null
                        : "Camp kit required — load one at the garage");
        }

        CommandResult CachedPreview(string cacheId)
        {
            int slots = _session.Inventory.UsedSlots(CampaignSession.Cargo);
            float mass = _session.Inventory.TotalMassKg(CampaignSession.Cargo);
            var (capacitySlots, capacityMass) = _session.Inventory.CapacityOf(CampaignSession.Cargo);
            if (cacheId != _previewTarget || slots != _previewSlots || Math.Abs(mass - _previewMass) > 1e-4f ||
                capacitySlots != _previewCapacitySlots || Math.Abs(capacityMass - _previewCapacityMass) > 1e-4f)
            {
                _previewTarget = cacheId;
                _previewSlots = slots;
                _previewMass = mass;
                _previewCapacitySlots = capacitySlots;
                _previewCapacityMass = capacityMass;
                _previewResult = _session.PreviewCacheCollect(cacheId);
            }
            return _previewResult;
        }

        static bool Near(WorldPoint position, SliceSite site, FieldPrompt best)
        {
            float d = position.DistanceXZ(site.Position);
            return d <= site.RadiusMeters && (!best.Visible || d < best.DistanceMeters);
        }

        static void Consider(ref FieldPrompt best, WorldPoint position, SliceSite site, FieldPromptKind kind, string label,
            bool substantial, string blocked)
        {
            float d = position.DistanceXZ(site.Position);
            if (d > site.RadiusMeters || (best.Visible && d >= best.DistanceMeters)) return;
            best = new FieldPrompt(kind, site.Id, label, d, substantial, blocked);
        }

        static string CapacityText(FailureReason reason) => reason switch
        {
            FailureReason.InsufficientSlots => "Cargo full — no free slots (bank at a garage or camp)",
            FailureReason.InsufficientMassCapacity => "Cargo too heavy — payload limit reached",
            FailureReason.AlreadyCollected => "Already collected",
            _ => reason.ToString()
        };

        CommandResult Execute(FieldPrompt prompt)
        {
            switch (prompt.Kind)
            {
                case FieldPromptKind.CollectCache: return _session.CollectCache(prompt.TargetId);
                case FieldPromptKind.CollectRelic: return _session.CollectRelic(prompt.TargetId);
                case FieldPromptKind.CollectQuestItem: return _session.CollectQuestItem(prompt.TargetId);
                case FieldPromptKind.Inspect: return _session.InspectObject(prompt.TargetId);
                case FieldPromptKind.DeliverQuestItem: return _session.DeliverQuestItem(prompt.TargetId);
                case FieldPromptKind.EstablishCamp: return _session.EstablishCamp(prompt.TargetId);
                case FieldPromptKind.DepositAtCamp: return _session.DepositAtCamp(prompt.TargetId);
                case FieldPromptKind.ActivateRelay: return _session.ActivateRelay(prompt.TargetId);
                case FieldPromptKind.CompleteContractTarget:
                    return _session.InteractContractTarget(prompt.ContractId, prompt.TargetId);
                default: return CommandResult.Fail(FailureReason.InvalidTarget, "nothing to interact with");
            }
        }
    }
}
