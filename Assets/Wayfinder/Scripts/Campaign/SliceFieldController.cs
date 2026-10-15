using System;
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
        ActivateRelay
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

        public FieldPrompt(FieldPromptKind kind, string targetId, string label, float distance, bool substantial, string blocked)
        {
            Kind = kind;
            TargetId = targetId ?? string.Empty;
            Label = label ?? string.Empty;
            DistanceMeters = distance;
            Substantial = substantial;
            BlockedReason = blocked ?? string.Empty;
        }

        public bool Visible => Kind != FieldPromptKind.None;
        public bool Available => Visible && BlockedReason.Length == 0;

        internal FieldPrompt WithBlock(string reason) =>
            new FieldPrompt(Kind, TargetId, Label, DistanceMeters, Substantial, reason);
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
    /// Engine-free field loop for the vertical slice: telemetry → session, safe-pose sampling,
    /// map reveal cadence, landmark observation (§8.2), ordered checkpoints, the single best
    /// interaction prompt with hold/tap commit (§16.4), and garage arrival → extraction (§2.3).
    /// The Unity driver only renders <see cref="FieldFrame"/> and forwards input, so all of this
    /// is covered by headless tests. Allocation-free on frames where nothing is in range.
    /// </summary>
    public sealed class SliceFieldController
    {
        public const float InteractionMaxSpeedKph = 15f;
        public const float RevealIntervalSeconds = 1f;
        public const float RevealMinTravelMeters = 16f;

        readonly CampaignSession _session;
        readonly VerticalSliceManifest _slice;
        readonly ArrivalTracker _arrival = new ArrivalTracker();
        readonly ObservationTracker _observation = new ObservationTracker();
        readonly HoldInteractTracker _hold = new HoldInteractTracker();
        readonly SafePoseSampler _safePose = new SafePoseSampler();
        readonly FieldFrame _frame = new FieldFrame();
        float _sinceReveal;
        WorldPoint _lastReveal;
        bool _hasLastReveal;
        string _heldTarget = string.Empty;
        // Capacity-preview cache: re-evaluated only when the target or the cargo changes, so
        // standing next to a cache does not trial-place its contents every frame.
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
            ResetForExpedition();
        }

        public bool InArrivalVolume(WorldPoint p) => p.DistanceXZ(_slice.ArrivalCenter) <= _slice.ArrivalRadiusMeters;

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

            _session.UpdateVehicle(input.Pose, input.FuelLiters, input.EngineCondition, input.SuspensionCondition,
                input.TiresCondition, input.BodyCondition, dt);
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

            _frame.Prompt = SelectPrompt(position, input.SpeedKph);
            if (_frame.Prompt.TargetId != _heldTarget)
            {
                _hold.Reset(); // switching targets never carries a half-finished hold over
                _heldTarget = _frame.Prompt.TargetId;
            }
            bool fire = _hold.Update(input.InteractPressed && _frame.Prompt.Available, _frame.Prompt.Substantial,
                input.TapToInteract, dt);
            _frame.HoldProgress01 = _hold.Progress01;
            if (fire)
            {
                _frame.InteractionCommitted = true;
                _frame.InteractionResult = Execute(_frame.Prompt);
            }

            if (_arrival.Update(InArrivalVolume(position), input.SpeedKph / 3.6f, dt))
            {
                _frame.ArrivalTriggered = true;
                _frame.Extraction = _session.EndExpedition(_slice.GarageId);
            }
            return _frame;
        }

        void UpdateObservation(WorldPoint position, float speedKph, float dt)
        {
            string target = null;
            WorldPoint targetPosition = default;
            float best = float.MaxValue;
            foreach (var l in _slice.Landmarks)
            {
                if (_session.Discovery.IsLandmarkDiscovered(l.Id)) continue;
                if (!_session.Content.Landmarks.TryGetValue(l.Id, out var def)) continue;
                float d = position.DistanceXZ(def.Position);
                if (d <= l.ObservationRadiusMeters && d < best)
                {
                    best = d;
                    target = l.Id;
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
                for (int i = 0; i < route.Checkpoints.Count; i++)
                {
                    if (position.DistanceXZ(route.Checkpoints[i]) > route.RadiusMeters) continue;
                    if (_session.NextCheckpointIndex(route.Id) == i) _session.PassCheckpoint(route.Id, i);
                }
        }

        FieldPrompt SelectPrompt(WorldPoint position, float speedKph)
        {
            var best = default(FieldPrompt);
            float radius = _slice.InteractionRadiusMeters;

            foreach (var cacheId in _slice.CacheIds)
            {
                if (_session.Discovery.IsCacheCollected(cacheId)) continue;
                if (!_session.Content.Caches.TryGetValue(cacheId, out var cache)) continue;
                float d = position.DistanceXZ(cache.Position);
                if (d > radius || (best.Visible && d >= best.DistanceMeters)) continue;
                best = new FieldPrompt(FieldPromptKind.CollectCache, cacheId, "Hold to collect cache", d, false, null);
            }
            foreach (var site in _slice.QuestItemSites)
            {
                if (_session.Discovery.IsCacheCollected(site.Id) ||
                    _session.Missions.StateOf(site.MissionCode) != MissionState.Active) continue;
                Consider(ref best, position, site, FieldPromptKind.CollectQuestItem, "Hold to collect mission item", false, null);
            }
            foreach (var site in _slice.InspectTargets)
            {
                if (!Near(position, site, best)) continue;
                if (!_session.IsObjectivePending(MissionConditionType.InspectObject, site.Id)) continue;
                Consider(ref best, position, site, FieldPromptKind.Inspect, "Hold to inspect", false, null);
            }
            foreach (var site in _slice.CampSockets)
            {
                if (!Near(position, site, best)) continue;
                if (_session.Garage.IsCampActivated(site.Id))
                    Consider(ref best, position, site, FieldPromptKind.DepositAtCamp, "Hold to deposit & save at camp", true, null);
                else
                    Consider(ref best, position, site, FieldPromptKind.EstablishCamp, "Hold to establish camp", true,
                        _session.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.CampKit) > 0
                            ? null
                            : "Camp kit required — load one at the garage");
            }
            foreach (var site in _slice.Relays)
            {
                if (!Near(position, site, best) || _session.Garage.HasInfrastructure(site.Id)) continue;
                if (_session.Missions.StateOf(site.MissionCode) != MissionState.Active) continue;
                Consider(ref best, position, site, FieldPromptKind.ActivateRelay, "Hold to restore relay", true,
                    _session.Inventory.ProtectedLedgerContains(site.ItemId) ? null : "Relay fuse required");
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
                case FieldPromptKind.CollectQuestItem: return _session.CollectQuestItem(prompt.TargetId);
                case FieldPromptKind.Inspect: return _session.InspectObject(prompt.TargetId);
                case FieldPromptKind.EstablishCamp: return _session.EstablishCamp(prompt.TargetId);
                case FieldPromptKind.DepositAtCamp: return _session.DepositAtCamp(prompt.TargetId);
                case FieldPromptKind.ActivateRelay: return _session.ActivateRelay(prompt.TargetId);
                default: return CommandResult.Fail(FailureReason.InvalidTarget, "nothing to interact with");
            }
        }
    }
}
