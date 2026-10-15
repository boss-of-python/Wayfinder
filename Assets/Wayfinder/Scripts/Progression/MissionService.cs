using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Progression
{
    /// <summary>Typed mission condition vocabulary (§17.1). No arbitrary embedded code in content.</summary>
    public enum MissionConditionType
    {
        DiscoverLandmark,
        VisitVolume,
        CollectQuestItem,
        DeliverQuestItem,
        RevealMapPercent,
        ActivateRelay,
        EstablishCamp,
        InspectObject,
        TraverseCheckpoints,
        ReturnToHub
    }

    public sealed class MissionObjective
    {
        public MissionConditionType Type;
        public ContentId TargetId;
        public int Count = 1;
    }

    public sealed class MissionDefinition
    {
        public ContentId Id;
        public string DisplayCode;            // "Q01" .. "Q20"
        public readonly List<ContentId> Prerequisites = new List<ContentId>();
        public readonly List<MissionObjective> Objectives = new List<MissionObjective>();
        /// <summary>Items granted on accept (e.g. Q03 grants a camp kit to garage storage).</summary>
        public readonly List<(string itemId, int quantity, string destination)> OnAcceptGrant =
            new List<(string, int, string)>();
        public ContentId TurnInHub;
        public int RewardSalvage;
        public string RewardKey;              // stable idempotency key, e.g. "reward.story.Q03"
        public string NameKey;
    }

    /// <summary>
    /// Mission lifecycle (§2.5): Locked → Available → Accepted → Active → ReadyToTurnIn → Completed.
    /// Missions never regress on vehicle recovery. Reward keys are idempotent (QA case 1/16).
    /// Full main catalog Q01–Q20 per §9.2.
    /// </summary>
    public sealed class MissionService
    {
        /// <summary>
        /// Legacy process-wide instance kept for older call sites/tests. Campaign code creates one
        /// <see cref="MissionService"/> per save slot (KI-023) — never use this from gameplay.
        /// </summary>
        public static readonly MissionService Global = new MissionService();

        readonly Dictionary<string, MissionDefinition> _definitions =
            new Dictionary<string, MissionDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, MissionState> _states =
            new Dictionary<string, MissionState>(StringComparer.Ordinal);
        readonly Dictionary<string, int[]> _objectiveCounters =
            new Dictionary<string, int[]>(StringComparer.Ordinal);
        readonly HashSet<string> _completedCodes = new HashSet<string>(StringComparer.Ordinal);

        public MissionService()
        {
            // Q01–Q20 (§9.2) — stable IDs, rewards, and prerequisites. Objectives use typed vocabulary.
            Define("Q01", "First Tracks", 150, new[]
            {
                Obj(MissionConditionType.CollectQuestItem, "salvage_bundle", 1),
                Obj(MissionConditionType.DiscoverLandmark, "landmark.forest.01", 1),
                Obj(MissionConditionType.ReturnToHub, "garage.home", 1)
            });
            Define("Q02", "The Creek Gauge", 200, new[]
            {
                Obj(MissionConditionType.InspectObject, "landmark.forest.04", 1),
                Obj(MissionConditionType.TraverseCheckpoints, "route.forest.ford", 2)
            }, "Q01");
            Define("Q03", "A Place to Stay", 300, new[]
            {
                Obj(MissionConditionType.EstablishCamp, "camp.forest.a", 1)
            }, "Q02", onAccept: new[] { ("camp_kit", 1, "garageStorage") });
            Define("Q04", "A Signal Through the Trees", 500, new[]
            {
                Obj(MissionConditionType.CollectQuestItem, "relay_fuse", 1),
                Obj(MissionConditionType.ActivateRelay, "relay.forest", 1)
            }, "Q03");
            Define("Q05", "Red Dust", 300, new[]
            {
                Obj(MissionConditionType.VisitVolume, "garage.canyon", 1),
                Obj(MissionConditionType.InspectObject, "site.canyon.washed_out", 1)
            }, "Q04");
            Define("Q06", "The Safer Line", 400, new[]
            {
                Obj(MissionConditionType.InspectObject, "marker.canyon.01", 1),
                Obj(MissionConditionType.InspectObject, "marker.canyon.02", 1),
                Obj(MissionConditionType.InspectObject, "marker.canyon.03", 1)
            }, "Q05");
            Define("Q07", "Bridge Ledger", 500, new[]
            {
                Obj(MissionConditionType.CollectQuestItem, "quest.brace_manifest", 1),
                Obj(MissionConditionType.DeliverQuestItem, "site.canyon.bridge", 1)
            }, "Q06");
            Define("Q08", "The Echo Relay", 700, new[]
            {
                Obj(MissionConditionType.ActivateRelay, "relay.canyon", 1)
            }, "Q07");
            Define("Q09", "Streets Without Names", 400, new[]
            {
                Obj(MissionConditionType.InspectObject, "junction.city.01", 1),
                Obj(MissionConditionType.InspectObject, "junction.city.02", 1),
                Obj(MissionConditionType.InspectObject, "junction.city.03", 1),
                Obj(MissionConditionType.VisitVolume, "garage.city", 1)
            }, "Q08");
            Define("Q10", "The Archive Van", 500, new[]
            {
                Obj(MissionConditionType.CollectQuestItem, "quest.archive_case", 1)
            }, "Q09");
            Define("Q11", "Power for the Crossing", 650, new[]
            {
                Obj(MissionConditionType.DeliverQuestItem, "quest.cable_spool", 1)
            }, "Q10", onAccept: new[] { ("quest.cable_spool", 1, "garageStorage") });
            Define("Q12", "Meridian Broadcast", 850, new[]
            {
                Obj(MissionConditionType.ActivateRelay, "relay.city", 1)
            }, "Q11");
            Define("Q13", "Reading the Water", 500, new[]
            {
                Obj(MissionConditionType.InspectObject, "pole.wetlands.01", 1),
                Obj(MissionConditionType.InspectObject, "pole.wetlands.02", 1),
                Obj(MissionConditionType.InspectObject, "pole.wetlands.03", 1),
                Obj(MissionConditionType.TraverseCheckpoints, "route.wetlands.crossing", 1)
            }, "Q12");
            Define("Q14", "Pump House Notes", 650, new[]
            {
                Obj(MissionConditionType.CollectQuestItem, "document.pump_diagram", 1),
                Obj(MissionConditionType.InspectObject, "site.wetlands.alt_causeway", 1)
            }, "Q13");
            Define("Q15", "Ground That Holds", 800, new[]
            {
                Obj(MissionConditionType.TraverseCheckpoints, "route.wetlands.firm", 3),
                Obj(MissionConditionType.DeliverQuestItem, "quest.stabilizer_plates", 1)
            }, "Q14", onAccept: new[] { ("quest.stabilizer_plates", 1, "garageStorage") });
            Define("Q16", "Tideglass Signal", 1000, new[]
            {
                Obj(MissionConditionType.ActivateRelay, "relay.wetlands", 1)
            }, "Q15");
            Define("Q17", "The High Road", 650, new[]
            {
                Obj(MissionConditionType.VisitVolume, "garage.highlands", 1),
                Obj(MissionConditionType.TraverseCheckpoints, "route.highlands.allweather", 1)
            }, "Q16");
            Define("Q18", "Before the Whiteout", 800, new[]
            {
                Obj(MissionConditionType.InspectObject, "station.highlands.east", 1),
                Obj(MissionConditionType.InspectObject, "station.highlands.west", 1),
                Obj(MissionConditionType.CollectQuestItem, "sensor_core", 1)
            }, "Q17");
            Define("Q19", "The Last Mast", 1000, new[]
            {
                Obj(MissionConditionType.DeliverQuestItem, "sensor_core", 1),
                Obj(MissionConditionType.ActivateRelay, "relay.highlands", 1)
            }, "Q18");
            Define("Q20", "An Atlas for Everyone", 1500, new[]
            {
                Obj(MissionConditionType.ActivateRelay, "relay.forest", 1),
                Obj(MissionConditionType.ActivateRelay, "relay.canyon", 1),
                Obj(MissionConditionType.ActivateRelay, "relay.city", 1),
                Obj(MissionConditionType.ActivateRelay, "relay.wetlands", 1),
                Obj(MissionConditionType.ActivateRelay, "relay.highlands", 1),
                Obj(MissionConditionType.RevealMapPercent, "map", 60),
                Obj(MissionConditionType.VisitVolume, "site.highlands.observatory", 1)
            }, "Q19");

            foreach (var kv in _definitions)
                _states[kv.Key] = MissionState.Locked;
            RefreshAvailability();
        }

        static MissionObjective Obj(MissionConditionType type, string target, int count) =>
            new MissionObjective { Type = type, TargetId = new ContentId(target), Count = count };

        void Define(string code, string nameKey, int rewardSalvage, MissionObjective[] objectives,
            string prerequisiteCode = null, (string itemId, int qty, string dest)[] onAccept = null)
        {
            var def = new MissionDefinition
            {
                Id = new ContentId("mission." + code),
                DisplayCode = code,
                RewardSalvage = rewardSalvage,
                RewardKey = "reward.story." + code,
                NameKey = "mission." + code + ".name." + nameKey.ToLowerInvariant().Replace(' ', '_')
            };
            if (!string.IsNullOrEmpty(prerequisiteCode))
                def.Prerequisites.Add(new ContentId("mission." + prerequisiteCode));
            def.Objectives.AddRange(objectives);
            if (onAccept != null)
                foreach (var g in onAccept) def.OnAcceptGrant.Add(g);
            def.TurnInHub = new ContentId(code switch
            {
                "Q01" or "Q02" or "Q03" or "Q04" => "garage.home",
                "Q05" or "Q06" or "Q07" or "Q08" => "garage.canyon",
                "Q09" or "Q10" or "Q11" or "Q12" => "garage.city",
                "Q13" or "Q14" or "Q15" or "Q16" => "garage.wetlands",
                _ => "garage.highlands"
            });
            _definitions[def.DisplayCode] = def;
        }

        public IReadOnlyDictionary<string, MissionDefinition> Definitions => _definitions;

        public MissionState StateOf(string code) => _states.TryGetValue(code, out var s) ? s : MissionState.Locked;

        public bool IsCompletedCode(string code) => _completedCodes.Contains(code);

        /// <summary>Recomputes Locked→Available from completed prerequisites.</summary>
        public void RefreshAvailability()
        {
            foreach (var kv in _definitions)
            {
                if (_states[kv.Key] != MissionState.Locked) continue;
                bool open = true;
                foreach (var pre in kv.Value.Prerequisites)
                {
                    string preCode = pre.Value.Replace("mission.", "");
                    if (!_completedCodes.Contains(preCode)) { open = false; break; }
                }
                if (open) _states[kv.Key] = MissionState.Available;
            }
        }

        public CommandResult Accept(string code) => Accept(code, null);

        /// <summary>
        /// Accepts an Available mission. <paramref name="currentWorldProgress"/> lets the caller
        /// pre-satisfy objectives that are already true in the persistent world (landmark already
        /// discovered, camp already established, relay already active…) so accepting late can never
        /// dead-lock a one-time objective. Returns the progress applied, capped at each Count.
        /// </summary>
        public CommandResult Accept(string code, Func<MissionObjective, int> currentWorldProgress)
        {
            if (string.IsNullOrEmpty(code) || !_definitions.TryGetValue(code, out var def))
                return CommandResult.Fail(FailureReason.InvalidTarget, "unknown mission");
            if (_states[code] != MissionState.Available)
                return CommandResult.Fail(FailureReason.MissionLocked, _states[code].ToString());
            _states[code] = MissionState.Active;
            var counters = new int[def.Objectives.Count];
            if (currentWorldProgress != null)
                for (int i = 0; i < counters.Length; i++)
                    counters[i] = Math.Max(0, Math.Min(def.Objectives[i].Count, currentWorldProgress(def.Objectives[i])));
            _objectiveCounters[code] = counters;
            UpdateReadiness(code);
            return CommandResult.Ok();
        }

        /// <summary>
        /// Routes a typed world event to every Active mission objective with the same condition type
        /// and target (e.g. DiscoverLandmark/landmark.forest.01). Counters are capped at Count, so
        /// replayed events are harmless. Returns the number of objectives that advanced.
        /// </summary>
        public int ReportCondition(MissionConditionType type, string targetId, int amount = 1)
        {
            if (amount <= 0 || string.IsNullOrEmpty(targetId)) return 0;
            int advanced = 0;
            foreach (var code in ActiveCodesSnapshot())
            {
                var def = _definitions[code];
                var counters = _objectiveCounters[code];
                for (int i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type != type || objective.TargetId.Value != targetId) continue;
                    if (counters[i] >= objective.Count) continue;
                    counters[i] = Math.Min(objective.Count, counters[i] + amount);
                    advanced++;
                }
                UpdateReadiness(code);
            }
            return advanced;
        }

        /// <summary>
        /// Monotonic absolute progress (e.g. RevealMapPercent = current coverage %). Never lowers a counter.
        /// </summary>
        public int SetConditionProgress(MissionConditionType type, string targetId, int value)
        {
            int advanced = 0;
            foreach (var code in ActiveCodesSnapshot())
            {
                var def = _definitions[code];
                var counters = _objectiveCounters[code];
                for (int i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type != type || objective.TargetId.Value != targetId) continue;
                    int next = Math.Min(objective.Count, Math.Max(counters[i], value));
                    if (next == counters[i]) continue;
                    counters[i] = next;
                    advanced++;
                }
                UpdateReadiness(code);
            }
            return advanced;
        }

        /// <summary>
        /// Ordered checkpoint routes (TraverseCheckpoints): checkpoint <paramref name="checkpointIndex"/>
        /// counts only when it is the next one, so re-driving an earlier gate (even after a reload)
        /// can never inflate the counter. Returns the number of objectives that advanced.
        /// </summary>
        public int ReportCheckpoint(string routeId, int checkpointIndex)
        {
            int advanced = 0;
            foreach (var code in ActiveCodesSnapshot())
            {
                var def = _definitions[code];
                var counters = _objectiveCounters[code];
                for (int i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type != MissionConditionType.TraverseCheckpoints || objective.TargetId.Value != routeId) continue;
                    if (counters[i] >= objective.Count || counters[i] != checkpointIndex) continue;
                    counters[i]++;
                    advanced++;
                }
                UpdateReadiness(code);
            }
            return advanced;
        }

        List<string> ActiveCodesSnapshot()
        {
            var codes = new List<string>();
            foreach (var kv in _states)
                if (kv.Value == MissionState.Active && _objectiveCounters.ContainsKey(kv.Key)) codes.Add(kv.Key);
            codes.Sort(StringComparer.Ordinal);
            return codes;
        }

        void UpdateReadiness(string code)
        {
            if (_states[code] != MissionState.Active) return;
            var def = _definitions[code];
            var counters = _objectiveCounters[code];
            for (int i = 0; i < counters.Length; i++)
                if (counters[i] < def.Objectives[i].Count) return;
            _states[code] = MissionState.ReadyToTurnIn;
        }

        /// <summary>Mission codes currently in <paramref name="state"/>, in code order (deterministic UI/save order).</summary>
        public List<string> CodesInState(MissionState state)
        {
            var codes = new List<string>();
            foreach (var kv in _states)
                if (kv.Value == state) codes.Add(kv.Key);
            codes.Sort(StringComparer.Ordinal);
            return codes;
        }

        public IEnumerable<string> CompletedCodes
        {
            get
            {
                var codes = new List<string>(_completedCodes);
                codes.Sort(StringComparer.Ordinal);
                return codes;
            }
        }

        /// <summary>
        /// Advances objective counters. Returns ReadyToTurnIn when all objectives complete.
        /// Counters never exceed required counts (idempotent progress events).
        /// </summary>
        public CommandResult ReportObjective(string code, int objectiveIndex)
        {
            if (!_states.TryGetValue(code, out var state) || state != MissionState.Active)
                return CommandResult.Fail(FailureReason.MissionNotAccepted);
            if (!_objectiveCounters.TryGetValue(code, out var counters))
                return CommandResult.Fail(FailureReason.InternalError);
            if (objectiveIndex < 0 || objectiveIndex >= counters.Length)
                return CommandResult.Fail(FailureReason.InvalidTarget, "objective index");

            var objective = _definitions[code].Objectives[objectiveIndex];
            counters[objectiveIndex] = Math.Min(objective.Count, counters[objectiveIndex] + 1);
            UpdateReadiness(code);
            return CommandResult.Ok();
        }

        /// <summary>Turn-in pays via the reward ledger exactly once (QA case 1/16).</summary>
        public CommandResult TryTurnIn(string code, ContentId hubId, EconomyService economy,
            ClaimedRewardLedger rewardLedger)
        {
            if (!_states.TryGetValue(code, out var state) || state != MissionState.ReadyToTurnIn)
                return CommandResult.Fail(FailureReason.WrongState, state.ToString());
            var def = _definitions[code];
            if (def.TurnInHub.Value != hubId.Value)
                return CommandResult.Fail(FailureReason.InvalidTarget, $"turn in at {def.TurnInHub}");

            if (rewardLedger.TryClaim(def.RewardKey))
            {
                economy.Grant(def.RewardSalvage);
                _states[code] = MissionState.Completed;
                _completedCodes.Add(code);
                RefreshAvailability();
                return CommandResult.Ok();
            }
            // Already claimed previously (replay) — state converges without paying again, and the
            // successor still unlocks (the previous version left it Locked, KI-018).
            _states[code] = MissionState.Completed;
            _completedCodes.Add(code);
            RefreshAvailability();
            return CommandResult.Fail(FailureReason.AlreadyApplied);
        }

        public int[] ObjectiveCounters(string code) =>
            _objectiveCounters.TryGetValue(code, out var c) ? c : Array.Empty<int>();

        /// <summary>
        /// Restores mission progress. Completed codes are restored as Completed — the previous
        /// version reset them to Locked and RefreshAvailability then re-opened them as Available
        /// (mission regression after every load, KI-018). Unknown codes are ignored and counter
        /// arrays are normalised to the current objective count so content edits cannot crash
        /// ReportObjective.
        /// </summary>
        public void Restore(IEnumerable<string> completedCodes,
            IEnumerable<(string code, MissionState state, int[] counters)> active)
        {
            _completedCodes.Clear();
            _states.Clear();
            _objectiveCounters.Clear();
            foreach (var kv in _definitions) _states[kv.Key] = MissionState.Locked;
            if (completedCodes != null)
                foreach (var c in completedCodes)
                {
                    if (string.IsNullOrEmpty(c) || !_definitions.ContainsKey(c)) continue;
                    _completedCodes.Add(c);
                    _states[c] = MissionState.Completed;
                }
            if (active != null)
                foreach (var (code, state, counters) in active)
                {
                    if (string.IsNullOrEmpty(code) || !_definitions.TryGetValue(code, out var def)) continue;
                    if (_completedCodes.Contains(code)) continue; // completion never regresses
                    if (state == MissionState.Completed)
                    {
                        _completedCodes.Add(code);
                        _states[code] = MissionState.Completed;
                        continue;
                    }
                    if (state == MissionState.Locked || state == MissionState.Available) continue; // derived below
                    var normalized = new int[def.Objectives.Count];
                    if (counters != null)
                        for (int i = 0; i < normalized.Length && i < counters.Length; i++)
                            normalized[i] = Math.Max(0, Math.Min(def.Objectives[i].Count, counters[i]));
                    // Accepted/Active/ReadyToTurnIn resume as Active; ReadyToTurnIn is then re-derived
                    // from the counters, so a record can only be turn-in-ready if it really is complete.
                    _states[code] = MissionState.Active;
                    _objectiveCounters[code] = normalized;
                    UpdateReadiness(code);
                }
            RefreshAvailability();
        }
    }
}
