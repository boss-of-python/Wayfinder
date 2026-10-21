using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Wayfinder.Core;
using Wayfinder.Progression;
using Wayfinder.Simulation;

namespace Wayfinder.Campaign
{
    /// <summary>
    /// Readable English placeholders derived from stable ids until localization string tables are
    /// imported (KI-012). Everything user-visible in the slice goes through here or content names.
    /// </summary>
    public static class DisplayNames
    {
        public static string Garage(string garageId)
        {
            if (string.IsNullOrEmpty(garageId)) return "garage";
            string tail = garageId.StartsWith("garage.", StringComparison.Ordinal) ? garageId.Substring(7) : garageId;
            return Title(tail) + " Garage";
        }

        /// <summary>"camp.forest.a" → "Forest A Camp".</summary>
        public static string Camp(string campId)
        {
            if (string.IsNullOrEmpty(campId)) return "camp";
            string tail = campId.StartsWith("camp.", StringComparison.Ordinal) ? campId.Substring(5) : campId;
            return Title(tail.Replace('.', ' ')) + " Camp";
        }

        /// <summary>A hub's display name whether it is a garage or a camp (§10.4 logistics rows).</summary>
        public static string Hub(string hubId) =>
            !string.IsNullOrEmpty(hubId) && hubId.StartsWith("camp.", StringComparison.Ordinal)
                ? Camp(hubId) : Garage(hubId);

        public static string Item(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return string.Empty;
            string tail = itemId.StartsWith("quest.", StringComparison.Ordinal) ? itemId.Substring(6) : itemId;
            string words = tail.Replace('_', ' ');
            return words.Length == 0 ? itemId : char.ToUpperInvariant(words[0]) + words.Substring(1);
        }

        /// <summary>"upgrade.trailTires1.name" → "Trail Tires 1".</summary>
        public static string Upgrade(string nameKey)
        {
            if (string.IsNullOrEmpty(nameKey)) return string.Empty;
            var parts = nameKey.Split('.');
            string core = parts.Length >= 2 ? parts[1] : nameKey;
            var sb = new StringBuilder(core.Length + 4);
            for (int i = 0; i < core.Length; i++)
            {
                char c = core[i];
                bool boundary = i > 0 && (char.IsUpper(c) || (char.IsDigit(c) && !char.IsDigit(core[i - 1])));
                if (boundary) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString();
        }

        /// <summary>"relic.forest.01" → "Relic Forest 01"; content supplies the real name key.</summary>
        public static string Relic(string relicId) => Title(relicId);

        /// <summary>"forest" → "Forest Relic Set" (§8.3: five sets, one per biome).</summary>
        public static string RelicSet(string biome) => Title(biome) + " Relic Set";

        static string Title(string id)
        {
            var words = id.Replace('_', ' ').Replace('.', ' ').Split(' ');
            for (int i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }
    }

    public static class ObjectiveText
    {
        public static string Describe(MissionObjective objective, CampaignContent content)
        {
            string target = objective.TargetId.Value;
            switch (objective.Type)
            {
                case MissionConditionType.CollectQuestItem:
                    return target == ItemCatalog.SalvageBundle ? "Collect Salvage from a cache" : $"Collect the {DisplayNames.Item(target)}";
                case MissionConditionType.DiscoverLandmark:
                    return $"Discover {content.LandmarkName(target)}";
                case MissionConditionType.InspectObject:
                    return $"Inspect {content.LandmarkName(target)}";
                case MissionConditionType.TraverseCheckpoints:
                    return target == "route.forest.ford" ? "Cross the creek ford checkpoints" : "Pass the route checkpoints in order";
                case MissionConditionType.EstablishCamp:
                    return "Establish a camp at the marked socket";
                case MissionConditionType.ActivateRelay:
                    return "Restore the relay";
                case MissionConditionType.ReturnToHub:
                    return $"Return to the {DisplayNames.Garage(target)}";
                case MissionConditionType.VisitVolume:
                    return $"Visit {DisplayNames.Garage(target)}";
                case MissionConditionType.RevealMapPercent:
                    return $"Reveal {objective.Count}% of the map";
                case MissionConditionType.DeliverQuestItem:
                    return $"Deliver the {DisplayNames.Item(target)}";
                default:
                    return objective.Type.ToString();
            }
        }

        /// <summary>The HUD objective line (§13.2): first active mission's first unfinished objective.</summary>
        public static string TrackedLine(CampaignSession session)
        {
            var ready = session.Missions.CodesInState(MissionState.ReadyToTurnIn);
            if (ready.Count > 0)
            {
                var hub = session.Missions.Definitions[ready[0]].TurnInHub.Value;
                return $"{ready[0]} {session.Content.MissionName(ready[0])} — complete! Return to the {DisplayNames.Garage(hub)}";
            }
            foreach (var code in session.Missions.CodesInState(MissionState.Active))
            {
                var def = session.Missions.Definitions[code];
                var counters = session.Missions.ObjectiveCounters(code);
                for (int i = 0; i < def.Objectives.Count && i < counters.Length; i++)
                {
                    var o = def.Objectives[i];
                    if (counters[i] >= o.Count) continue;
                    string progress = o.Count > 1 ? $" ({counters[i]}/{o.Count})" : string.Empty;
                    return $"{code} {session.Content.MissionName(code)} — {Describe(o, session.Content)}{progress}";
                }
            }
            var available = session.Missions.CodesInState(MissionState.Available);
            return available.Count > 0
                ? $"New mission available at the garage: {available[0]} {session.Content.MissionName(available[0])}"
                : "Explore freely — every discovery is kept";
        }
    }

    /// <summary>HUD strings (§13.2). Built at ~10 Hz by the presentation layer.</summary>
    public sealed class HudModel
    {
        public string FuelLine = string.Empty;
        public float FuelFraction;
        public bool FuelWarning;
        public string CargoLine = string.Empty;
        public bool CargoWarning;
        public string SalvageLine = string.Empty;
        public string ObjectiveLine = string.Empty;
        public string SaveLine = string.Empty;

        public static HudModel Build(CampaignSession s, HudModel into = null)
        {
            var m = into ?? new HudModel();
            float capacity = Math.Max(0.01f, s.FuelCapacityLiters);
            m.FuelFraction = MathUtil.Clamp01(s.FuelLiters / capacity);
            m.FuelWarning = m.FuelFraction <= Simulation.FuelModel.WarningFractionThirty;
            m.FuelLine = string.Format(CultureInfo.InvariantCulture, "Fuel {0:0.0} / {1:0} L", s.FuelLiters, capacity);
            var (slots, payload) = s.Inventory.CapacityOf(CampaignSession.Cargo);
            int used = s.Inventory.UsedSlots(CampaignSession.Cargo);
            float mass = s.Inventory.TotalMassKg(CampaignSession.Cargo);
            m.CargoWarning = used >= slots - 1 || mass >= payload * 0.9f;
            m.CargoLine = string.Format(CultureInfo.InvariantCulture, "Cargo {0}/{1} slots · {2:0}/{3:0} kg", used, slots, mass, payload);
            int loose = s.Inventory.QuantityOf(CampaignSession.Cargo, ItemCatalog.SalvageBundle);
            m.SalvageLine = string.Format(CultureInfo.InvariantCulture, "Salvage {0} banked · {1} in cargo · {2} pending",
                s.Economy.BankedSalvage, loose, s.Economy.EscrowedSalvage);
            m.ObjectiveLine = ObjectiveText.TrackedLine(s);
            m.SaveLine = s.LastSaveFailed ? "Save failed — retrying" : (s.HasUnsavedChanges ? "Autosave pending" : "Saved");
            return m;
        }
    }

    /// <summary>Garage panel rows (§13.1/§13.3): research with exact change and one confirm, missions, storage.</summary>
    public sealed class GarageModel
    {
        public enum ResearchState { Researched, Available, NeedsPreviousTier, NeedsMission, NeedsSalvage }

        public sealed class ResearchRow
        {
            public string UpgradeId, Name, Result, RequiredMission;
            public int Tier, Cost;
            public ResearchState State;
            public bool CanPurchase => State == ResearchState.Available;

            /// <summary>The §10.2 slot this tier occupies, and its content spelling for labels.</summary>
            public ModuleSlot Slot;
            public string SlotName = string.Empty;
            /// <summary>True while this module is fitted, i.e. actually composing the resolved stats.</summary>
            public bool IsFitted;
            /// <summary>Garage-only loadout changes (§10.4 "vehicle change").</summary>
            public bool CanFit, CanUnfit;
            /// <summary>The module this one would displace, since a named slot holds one (§10.2).</summary>
            public string ReplacesId = string.Empty;
        }

        /// <summary>
        /// A §10.2 tire-set row. One set is mounted at a time; the specialized sets stay unlocked by
        /// research even while another set is mounted (§11.2 tiers 8, 13, 18).
        /// </summary>
        public sealed class TireRow
        {
            public string TireSetId, Name, UnlockName;
            public bool Unlocked, Mounted;
            public bool CanMount => Unlocked && !Mounted;
        }

        public sealed class MissionRow
        {
            public string Code, Name;
            public MissionState State;
            public readonly List<string> Objectives = new List<string>();
            public bool CanAccept => State == MissionState.Available;
        }

        public sealed class StackRow
        {
            public string ItemId, Name;
            public int Quantity;
            public float MassKg;
            public bool Loadable;
            /// <summary>One unit may be sold from this container now (§10.4 rules, <see cref="CampaignSession.CheckSell"/>).</summary>
            public bool Sellable;
            public int SellPriceEach;
            public string SellBlockedReason = string.Empty;
        }

        /// <summary>
        /// An activated hub the player may exchange property with (§10.4 community logistics). A
        /// garage row is the ONE shared unlimited store, so two garages can never exchange with each
        /// other and <see cref="CanExchange"/> says so before a UI offers the button.
        /// </summary>
        public sealed class LogisticsRow
        {
            public string HubId, Name;
            public bool IsCurrent, SharedStore, CanExchange;
            public int UsedSlots;
            public float MassKg, CapacityKg;
            /// <summary>What that hub is holding. Sell fields stay default: you cannot sell from a hub you are not at.</summary>
            public readonly List<StackRow> Stacks = new List<StackRow>();
        }

        public int Wallet, Escrow;
        public string CapacityLine = string.Empty;
        /// <summary>The hub the player is standing at, and every hub property may move between (§10.4).</summary>
        public string CurrentHubId = string.Empty;
        public readonly List<LogisticsRow> Logistics = new List<LogisticsRow>();
        /// <summary>The mounted tire set, and every set with its unlock state (§10.2).</summary>
        public string SelectedTireSet = string.Empty;
        public readonly List<TireRow> TireSets = new List<TireRow>();
        public readonly List<ResearchRow> Research = new List<ResearchRow>();
        public readonly List<MissionRow> Missions = new List<MissionRow>();
        public readonly List<StackRow> Cargo = new List<StackRow>();
        public readonly List<StackRow> Storage = new List<StackRow>();

        public static GarageModel Build(CampaignSession s, int maxResearchRows = 20)
        {
            var m = new GarageModel { Wallet = s.Economy.BankedSalvage, Escrow = s.Economy.EscrowedSalvage };
            var stats = s.ResolveStats();
            m.CapacityLine = string.Format(CultureInfo.InvariantCulture, "Scout S1 · {0} slots · {1:0} kg payload · {2:0} L tank",
                stats.CargoSlots, stats.PayloadKg, stats.FuelCapacityLiters);

            bool previousResearched = true;
            foreach (var tier in s.Research.Tiers)
            {
                if (m.Research.Count >= maxResearchRows) break;
                var row = new ResearchRow
                {
                    UpgradeId = tier.Id.Value,
                    Tier = tier.Tier,
                    Cost = tier.CostSalvage,
                    Name = DisplayNames.Upgrade(tier.NameKey),
                    Result = s.Content.UpgradeResult(tier.Id.Value),
                    RequiredMission = tier.RequiredMissionCode
                };
                row.Slot = tier.Slot;
                row.SlotName = ResearchService.SlotName(tier.Slot);
                row.IsFitted = s.Research.IsFitted(tier.Id.Value);
                row.CanFit = s.AtGarage && s.Research.CanFit(tier.Id.Value);
                row.CanUnfit = s.AtGarage && row.IsFitted;
                row.ReplacesId = tier.Slot == ModuleSlot.None || row.IsFitted
                    ? string.Empty : s.Research.ModuleInSlot(tier.Slot);
                if (s.Research.IsResearched(tier.Id.Value)) row.State = ResearchState.Researched;
                else if (!previousResearched) row.State = ResearchState.NeedsPreviousTier;
                else if (!string.IsNullOrEmpty(tier.RequiredMissionCode) && !s.Missions.IsCompletedCode(tier.RequiredMissionCode))
                    row.State = ResearchState.NeedsMission;
                else if (s.Economy.BankedSalvage < tier.CostSalvage) row.State = ResearchState.NeedsSalvage;
                else row.State = ResearchState.Available;
                previousResearched = row.State == ResearchState.Researched;
                m.Research.Add(row);
            }

            m.SelectedTireSet = s.Research.SelectedTireSet.ToString();
            foreach (var set in new[] { TireSet.AllTerrain, TireSet.Mud, TireSet.Rock, TireSet.Snow })
                m.TireSets.Add(new TireRow
                {
                    TireSetId = set.ToString(),
                    Name = TireSetLabel(set),
                    UnlockName = UnlockTierName(s, set),
                    Unlocked = s.Research.IsTireSetUnlocked(set),
                    Mounted = s.Research.SelectedTireSet == set
                });

            m.CurrentHubId = s.CurrentHubId ?? string.Empty;
            bool standingAtGarage = s.Garage.IsGarageUnlocked(m.CurrentHubId);
            foreach (var hubId in s.LogisticsHubs())
            {
                var store = s.HubStore(hubId);
                if (store == null) continue;
                bool shared = s.Garage.IsGarageUnlocked(hubId);
                var row = new LogisticsRow
                {
                    HubId = hubId,
                    Name = DisplayNames.Hub(hubId),
                    IsCurrent = string.Equals(hubId, m.CurrentHubId, StringComparison.Ordinal),
                    SharedStore = shared,
                    UsedSlots = s.Inventory.UsedSlots(store.Value),
                    MassKg = s.Inventory.TotalMassKg(store.Value),
                    CapacityKg = s.Inventory.CapacityOf(store.Value).massKg
                };
                row.CanExchange = !row.IsCurrent && !(shared && standingAtGarage);
                AddStacks(s, store.Value, row.Stacks);   // §10.4: "Camp storage holds 500 kg; show capacity"
                m.Logistics.Add(row);
            }

            foreach (var code in s.Content.Slice.MissionCodes)
            {
                if (!s.Missions.Definitions.TryGetValue(code, out var def)) continue;
                var row = new MissionRow { Code = code, Name = s.Content.MissionName(code), State = s.Missions.StateOf(code) };
                var counters = s.Missions.ObjectiveCounters(code);
                for (int i = 0; i < def.Objectives.Count; i++)
                {
                    var o = def.Objectives[i];
                    bool done = row.State == MissionState.Completed || (i < counters.Length && counters[i] >= o.Count);
                    row.Objectives.Add((done ? "✓ " : "• ") + ObjectiveText.Describe(o, s.Content));
                }
                m.Missions.Add(row);
            }

            AddStacks(s, CampaignSession.Cargo, m.Cargo);
            AddStacks(s, CampaignSession.GarageStorage, m.Storage);
            AddSellInfo(s, m.Cargo, fromCargo: true);
            AddSellInfo(s, m.Storage, fromCargo: false);
            return m;
        }

        /// <summary>Display label for a §10.2 tire set, until localization string tables exist.</summary>
        static string TireSetLabel(TireSet set) => set switch
        {
            TireSet.AllTerrain => "All-terrain",
            TireSet.Mud => "Mud",
            TireSet.Rock => "Rock",
            TireSet.Snow => "Snow",
            _ => set.ToString()
        };

        /// <summary>
        /// The name of the §11.2 tier that unlocks a tire set, read from the effect table rather than
        /// hardcoded, so the garage can say which research a locked set still needs.
        /// </summary>
        static string UnlockTierName(CampaignSession s, TireSet set)
        {
            VehicleStatsResolver.Stat? flag = set switch
            {
                TireSet.Mud => VehicleStatsResolver.Stat.MudTires,
                TireSet.Rock => VehicleStatsResolver.Stat.RockTires,
                TireSet.Snow => VehicleStatsResolver.Stat.SnowTires,
                _ => null
            };
            if (flag == null) return string.Empty;
            foreach (var tier in s.Research.Tiers)
                foreach (var effect in tier.Effects)
                    if (effect.stat == flag) return DisplayNames.Upgrade(tier.NameKey);
            return string.Empty;
        }

        static void AddSellInfo(CampaignSession s, List<StackRow> rows, bool fromCargo)
        {
            foreach (var row in rows)
            {
                var check = s.CheckSell(row.ItemId, 1, fromCargo, out int price);
                row.Sellable = check.Success;
                row.SellPriceEach = check.Success ? price : 0;
                row.SellBlockedReason = check.Success ? string.Empty : check.Detail ?? check.Reason.ToString();
            }
        }

        static void AddStacks(CampaignSession s, InventoryId container, List<StackRow> into)
        {
            var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var stack in s.Inventory.Stacks(container))
            {
                totals.TryGetValue(stack.ItemId.Value, out int q);
                totals[stack.ItemId.Value] = q + stack.Quantity;
            }
            foreach (var kv in totals)
            {
                var def = ItemCatalog.Get(kv.Key);
                into.Add(new StackRow
                {
                    ItemId = kv.Key,
                    Name = DisplayNames.Item(kv.Key),
                    Quantity = kv.Value,
                    MassKg = def.UnitMassKg * kv.Value,
                    Loadable = !def.ProtectedLedger
                });
            }
        }
    }

    /// <summary>Expedition summary text (§2.3): distance, discoveries, Salvage, fuel used, service, next goal.</summary>
    public static class SummaryText
    {
        public static string Describe(ExpeditionSummary summary, CampaignSession session)
        {
            if (summary == null) return string.Empty;
            if (!summary.Success)
                return summary.Result.Reason == FailureReason.SaveIoFailure
                    ? "Could not save — nothing was banked yet. Retry, or keep driving and try again."
                    : $"Could not end the expedition: {summary.Result.Detail}";
            var sb = new StringBuilder();
            sb.AppendLine(summary.WasRescue ? "Rescued to the " + DisplayNames.Garage(summary.GarageId) : "Back at the " + DisplayNames.Garage(summary.GarageId));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Distance {0:0.0} km · fuel used {1:0.0} L",
                summary.Stats.DistanceMeters / 1000f, summary.Stats.FuelUsedLiters));
            sb.AppendLine($"Caches {summary.Stats.CachesCollected} · landmarks {summary.Stats.LandmarksDiscovered} · map cells {summary.Stats.CellsRevealed}");
            sb.AppendLine($"Banked {summary.SalvageUnitsBanked} cargo Salvage + {summary.EscrowBanked} discovery rewards");
            if (summary.RescueSalvageLost > 0) sb.AppendLine($"Rescue cost: {summary.RescueSalvageLost} loose Salvage");
            foreach (var code in summary.MissionsCompleted)
                sb.AppendLine($"Mission complete: {code} {session.Content.MissionName(code)}");
            if (summary.MissionRewardSalvage > 0) sb.AppendLine($"Mission rewards +{summary.MissionRewardSalvage} Salvage");
            if (summary.MaterialUnitsStored > 0) sb.AppendLine($"{summary.MaterialUnitsStored} materials moved to garage storage");
            sb.AppendLine("Vehicle refuelled and repaired (free)");
            sb.Append($"Wallet: {summary.WalletAfter} Salvage · Next: {ObjectiveText.TrackedLine(session)}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Field-supplies panel text (§5.5, §5.6, §10.3): counts in cargo and whether each supply can be
    /// used right now, with the reason when not. Pure; built from the session plus live vehicle speed
    /// and water state.
    /// </summary>
    public sealed class SuppliesModel
    {
        public sealed class SubsystemRow
        {
            public HitZone Zone;
            public string Name = string.Empty;
            public int Condition;
            public bool CanRepair;
        }

        public int FuelCans, RepairKits, TirePatches;
        public bool FuelCanUsable, FuelCanNeedsConfirmation, TirePatchUsable;
        public float FuelCanLiters, FuelCanWasted;
        public string FuelCanLine = string.Empty, RepairLine = string.Empty, TirePatchLine = string.Empty;
        public readonly List<SubsystemRow> Subsystems = new List<SubsystemRow>();

        static readonly HitZone[] Zones = { HitZone.Engine, HitZone.Suspension, HitZone.Tires, HitZone.Body };

        public static SuppliesModel Build(CampaignSession s, float speedKph, bool inWater, SuppliesModel into = null)
        {
            var m = into ?? new SuppliesModel();
            m.FuelCans = s.Inventory.QuantityOf(CampaignSession.Cargo, CampaignSession.FuelCanId);
            m.RepairKits = s.Inventory.QuantityOf(CampaignSession.Cargo, CampaignSession.RepairKitId);
            m.TirePatches = s.Inventory.QuantityOf(CampaignSession.Cargo, CampaignSession.TirePatchId);

            var fuel = s.PreviewFuelCan();
            m.FuelCanUsable = fuel.Usable;
            m.FuelCanNeedsConfirmation = fuel.NeedsWasteConfirmation;
            m.FuelCanLiters = fuel.LitersAdded;
            m.FuelCanWasted = fuel.LitersWasted;
            m.FuelCanLine = string.Format(CultureInfo.InvariantCulture, "Fuel can ×{0} — {1}", m.FuelCans,
                !fuel.Usable ? fuel.Reason
                : fuel.NeedsWasteConfirmation ? string.Format(CultureInfo.InvariantCulture, "+{0:0.0} L ({1:0.0} L wasted — tap twice)", fuel.LitersAdded, fuel.LitersWasted)
                : string.Format(CultureInfo.InvariantCulture, "+{0:0.0} L", fuel.LitersAdded));

            bool stopped = speedKph <= CampaignSession.SupplyMaxSpeedKph;
            string repairBlock = !s.Expedition.IsActive ? "garage repairs are free"
                : m.RepairKits <= 0 ? "none in cargo"
                : !stopped ? "stop the vehicle to repair"
                : inWater ? "get out of the water first"
                : string.Empty;
            m.Subsystems.Clear();
            foreach (var zone in Zones)
            {
                int condition = s.Condition(zone);
                m.Subsystems.Add(new SubsystemRow
                {
                    Zone = zone,
                    Name = DisplayNames.Item(CampaignSession.SubsystemName(zone)),
                    Condition = condition,
                    CanRepair = repairBlock.Length == 0 && condition < 100
                });
            }
            m.RepairLine = $"Repair kit ×{m.RepairKits} — " + (repairBlock.Length > 0 ? repairBlock : "+35 to one subsystem");

            m.TirePatchUsable = s.Expedition.IsActive && m.TirePatches > 0 && stopped && s.Damage.HasPuncture;
            m.TirePatchLine = $"Tire patch ×{m.TirePatches} — " + (!s.Expedition.IsActive ? "garage repairs are free"
                : m.TirePatches <= 0 ? "none in cargo"
                : !s.Damage.HasPuncture ? "no puncture"
                : !stopped ? "stop the vehicle to patch"
                : "patch the puncture (lifts the 25 km/h limit)");
            return m;
        }
    }
}
