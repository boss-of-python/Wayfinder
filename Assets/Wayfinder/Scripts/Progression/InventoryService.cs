using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Progression
{
    /// <summary>Immutable item definition (§10.3). Runtime state must never modify definitions.</summary>
    public sealed class ItemDefinition
    {
        public ContentId Id;
        public string NameKey;
        public int MaxStack;
        public float UnitMassKg;
        /// <summary>Protected ledger objects (quest/relic) do not use ordinary slots and cannot be lost or sold.</summary>
        public bool ProtectedLedger;
        public bool Sellable = true;
        /// <summary>Purchase price in Salvage; sell price is 40% rounded down (§10.4).</summary>
        public int PurchasePriceSalvage;
    }

    /// <summary>
    /// How cargo is settled at a hub (§2.3 "loose cargo is converted or stored", §10.3, §12.4).
    /// Currency banks, crafting materials move to hub storage, consumables stay loaded as the
    /// loadout, mission objects stay with their mission, protected-ledger objects never move.
    /// </summary>
    public enum ItemCategory
    {
        Currency,
        Material,
        Consumable,
        MissionObject,
        ProtectedLedger
    }

    public static class ItemCatalog
    {
        public const string SalvageBundle = "salvage_bundle";
        public const string CampKit = "camp_kit";

        static Dictionary<string, ItemDefinition> BuildDefaults() => new Dictionary<string, ItemDefinition>(StringComparer.Ordinal)
        {
            ["salvage_bundle"] = Def("salvage_bundle", "item.salvage.name", 50, 0.10f, false, true, 1),
            ["scrap_metal"] = Def("scrap_metal", "item.scrap_metal.name", 10, 1.0f, false, true, 10),
            ["polymer"] = Def("polymer", "item.polymer.name", 10, 0.5f, false, true, 12),
            ["electronics"] = Def("electronics", "item.electronics.name", 5, 0.4f, false, true, 25),
            ["alloy"] = Def("alloy", "item.alloy.name", 5, 0.8f, false, true, 35),
            ["fuel_can"] = Def("fuel_can", "item.fuel_can.name", 2, 5.0f, false, true, 60),
            ["repair_kit"] = Def("repair_kit", "item.repair_kit.name", 3, 3.0f, false, true, 45),
            ["tire_patch"] = Def("tire_patch", "item.tire_patch.name", 4, 0.5f, false, true, 22),
            ["recovery_boards"] = Def("recovery_boards", "item.recovery_boards.name", 2, 4.0f, false, true, 35),
            ["camp_kit"] = Def("camp_kit", "item.camp_kit.name", 1, 12.0f, false, true, 100),
            ["survey_sample"] = Def("survey_sample", "item.survey_sample.name", 3, 1.0f, false, false, 0),
            ["supply_case"] = Def("supply_case", "item.supply_case.name", 1, 20.0f, false, false, 0),
            ["relay_fuse"] = Def("relay_fuse", "item.relay_fuse.name", 1, 0f, true, false, 0),
            ["sensor_core"] = Def("sensor_core", "item.sensor_core.name", 1, 0f, true, false, 0),
            ["quest.brace_manifest"] = Def("quest.brace_manifest", "item.brace_manifest.name", 1, 0f, true, false, 0),
            ["quest.archive_case"] = Def("quest.archive_case", "item.archive_case.name", 1, 10.0f, false, false, 0),
            ["quest.cable_spool"] = Def("quest.cable_spool", "item.cable_spool.name", 1, 35.0f, false, false, 0),
            ["quest.stabilizer_plates"] = Def("quest.stabilizer_plates", "item.stabilizer_plates.name", 1, 40.0f, false, false, 0),
            ["document.pump_diagram"] = Def("document.pump_diagram", "item.pump_diagram.name", 1, 1.0f, false, false, 0)
        };

        static Dictionary<string, ItemDefinition> Items = BuildDefaults();

        /// <summary>Replaces the live item catalog with definitions parsed from Content/items.json (KI-046).</summary>
        public static void LoadDefinitions(IEnumerable<ItemDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var next = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            foreach (var def in definitions)
            {
                if (def == null || string.IsNullOrEmpty(def.Id.Value))
                    throw new ArgumentException("item definition requires non-empty id");
                if (def.MaxStack <= 0 || def.UnitMassKg < 0f || def.PurchasePriceSalvage < 0)
                    throw new ArgumentException($"invalid item numeric bounds on '{def.Id.Value}'");
                next[def.Id.Value] = def;
            }
            if (next.Count == 0) throw new ArgumentException("items catalog cannot be empty");
            Items = next;
        }

        public static void ResetToDefaults() => Items = BuildDefaults();

        static ItemDefinition Def(string id, string nameKey, int stack, float mass, bool ledger, bool sellable, int price) =>
            new ItemDefinition
            {
                Id = new ContentId(id), NameKey = nameKey, MaxStack = stack,
                UnitMassKg = mass, ProtectedLedger = ledger, Sellable = sellable, PurchasePriceSalvage = price
            };

        public static ItemDefinition Get(ContentId id) => Get(id.Value);

        public static ItemDefinition Get(string id)
        {
            if (!Items.TryGetValue(id, out var def))
                throw new KeyNotFoundException($"Unknown item id '{id}'.");
            return def;
        }

        public static bool TryGet(string id, out ItemDefinition def) => Items.TryGetValue(id, out def);

        /// <summary>Settlement category (see <see cref="ItemCategory"/>). Unknown ids throw.</summary>
        public static ItemCategory CategoryOf(string id)
        {
            var def = Get(id);
            if (def.ProtectedLedger) return ItemCategory.ProtectedLedger;
            switch (id)
            {
                case SalvageBundle:
                    return ItemCategory.Currency;
                case "scrap_metal":
                case "polymer":
                case "electronics":
                case "alloy":
                    return ItemCategory.Material;
                case "fuel_can":
                case "repair_kit":
                case "tire_patch":
                case "recovery_boards":
                case CampKit:
                    return ItemCategory.Consumable;
                default:
                    return ItemCategory.MissionObject; // survey samples, supply cases, quest.* cargo
            }
        }

        /// <summary>Sell price: 40% of purchase price, rounded down (§10.4).</summary>
        public static int SellPrice(string id)
        {
            var def = Get(id);
            return (int)(def.PurchasePriceSalvage * 0.40f);
        }

        public static IReadOnlyDictionary<string, ItemDefinition> All => Items;
    }

    public readonly struct ItemStack
    {
        public readonly ContentId ItemId;
        public readonly int Quantity;
        /// <summary>Optional mission-instance ID for quest delivery objects.</summary>
        public readonly string MissionInstanceId;

        public ItemStack(ContentId itemId, int quantity, string missionInstanceId = null)
        {
            if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            ItemId = itemId;
            Quantity = quantity;
            MissionInstanceId = missionInstanceId ?? string.Empty;
        }

        public float MassKg => Quantity * ItemCatalog.Get(ItemId).UnitMassKg;
    }

    /// <summary>
    /// Cargo/inventory rules (§10.4): list of stacks; each stack occupies one slot; both total
    /// slots and mass must validate before a pickup. Protected ledger objects bypass ordinary
    /// slots. Full inventory returns a typed capacity error WITHOUT consuming the cache (QA case 3).
    /// </summary>
    public sealed class InventoryService
    {
        readonly Dictionary<InventoryId, List<ItemStack>> _stacks = new Dictionary<InventoryId, List<ItemStack>>();
        readonly Dictionary<InventoryId, (int slots, float massKg)> _capacity =
            new Dictionary<InventoryId, (int, float)>();
        readonly List<string> _protectedLedger = new List<string>();

        public void ConfigureContainer(InventoryId id, int slots, float massKg) =>
            _capacity[id] = (slots, massKg);

        public IReadOnlyList<ItemStack> Stacks(InventoryId id) =>
            _stacks.TryGetValue(id, out var list) ? list : Array.Empty<ItemStack>();

        public int UsedSlots(InventoryId id)
        {
            int n = 0;
            foreach (var s in Stacks(id))
                if (!ItemCatalog.Get(s.ItemId).ProtectedLedger) n++;
            return n;
        }

        public float TotalMassKg(InventoryId id)
        {
            float m = 0f;
            foreach (var s in Stacks(id)) m += s.MassKg;
            return m;
        }

        public int QuantityOf(InventoryId id, string itemId) => QuantityOf(id, itemId, string.Empty);

        /// <summary>Quantity of one item, optionally limited to one mission instance (DEF-021).</summary>
        public int QuantityOf(InventoryId id, string itemId, string missionInstanceId)
        {
            string filter = missionInstanceId ?? string.Empty;
            int q = 0;
            foreach (var s in Stacks(id))
            {
                if (s.ItemId.Value != itemId) continue;
                if (filter.Length > 0 && !string.Equals(s.MissionInstanceId, filter, StringComparison.Ordinal)) continue;
                q += s.Quantity;
            }
            return q;
        }

        public bool ProtectedLedgerContains(string itemId) => _protectedLedger.Contains(itemId);

        /// <summary>
        /// Placement plan for a pickup: top up compatible partial stacks first (same item + mission
        /// instance), then open new stacks of at most <c>MaxStack</c>. A quantity larger than one
        /// stack therefore needs several slots — the previous implementation stored it as one
        /// oversized stack, which under-counted slots (found by ProgressionTests QA case 9).
        /// </summary>
        struct PlacementPlan
        {
            public List<(int index, int add)> Merges;
            public List<int> NewStackQuantities;
            public int RequiredSlots => NewStackQuantities?.Count ?? 0;
        }

        PlacementPlan Plan(InventoryId target, ItemStack stack, ItemDefinition def)
        {
            var plan = new PlacementPlan { Merges = new List<(int, int)>(), NewStackQuantities = new List<int>() };
            int maxStack = Math.Max(1, def.MaxStack);
            int remaining = stack.Quantity;
            var existing = Stacks(target);
            for (int i = 0; i < existing.Count && remaining > 0; i++)
            {
                var s = existing[i];
                if (s.ItemId.Value != stack.ItemId.Value || s.MissionInstanceId != stack.MissionInstanceId) continue;
                int room = maxStack - s.Quantity;
                if (room <= 0) continue;
                int add = Math.Min(room, remaining);
                plan.Merges.Add((i, add));
                remaining -= add;
            }
            while (remaining > 0)
            {
                int q = Math.Min(maxStack, remaining);
                plan.NewStackQuantities.Add(q);
                remaining -= q;
            }
            return plan;
        }

        public CapacityResult CanAdd(InventoryId target, ItemStack stack)
        {
            var def = ItemCatalog.Get(stack.ItemId);
            if (!_capacity.TryGetValue(target, out var cap))
                return CapacityResult.Rejected(FailureReason.InvalidTarget, 0, 0, 0f, 0f);

            if (def.ProtectedLedger)
                return CapacityResult.Fit(0, cap.slots, stack.MassKg, cap.massKg);

            int freeSlots = cap.slots - UsedSlots(target);
            float freeMass = cap.massKg - TotalMassKg(target);

            int requiredSlots = Plan(target, stack, def).RequiredSlots;
            if (requiredSlots > freeSlots)
                return CapacityResult.Rejected(FailureReason.InsufficientSlots, requiredSlots, freeSlots, stack.MassKg, freeMass);
            if (stack.MassKg > freeMass + 1e-4f)
                return CapacityResult.Rejected(FailureReason.InsufficientMassCapacity, requiredSlots, freeSlots, stack.MassKg, freeMass);
            return CapacityResult.Fit(requiredSlots, freeSlots, stack.MassKg, freeMass);
        }

        /// <summary>
        /// Adds items. Returns typed failure WITHOUT mutating state when capacity fails
        /// (QA case 3: the pickup source must not be consumed by a failed add). The whole
        /// quantity is placed or nothing is.
        /// </summary>
        public CommandResult Add(InventoryId target, ItemStack stack)
        {
            var capacity = CanAdd(target, stack);
            if (!capacity.Fits)
                return CommandResult.Fail(capacity.Reason,
                    $"need {capacity.RequiredSlots} slot(s)/{capacity.RequiredMassKg:0.##} kg");

            var def = ItemCatalog.Get(stack.ItemId);
            if (def.ProtectedLedger)
            {
                _protectedLedger.Add(stack.ItemId.Value);
                return CommandResult.Ok();
            }
            if (stack.Quantity == 0) return CommandResult.Ok();

            if (!_stacks.TryGetValue(target, out var list))
            {
                list = new List<ItemStack>();
                _stacks[target] = list;
            }

            var plan = Plan(target, stack, def);
            foreach (var (index, add) in plan.Merges)
            {
                var s = list[index];
                list[index] = new ItemStack(s.ItemId, s.Quantity + add, s.MissionInstanceId);
            }
            foreach (int q in plan.NewStackQuantities)
                list.Add(new ItemStack(stack.ItemId, q, stack.MissionInstanceId));
            return CommandResult.Ok();
        }

        /// <summary>
        /// Removes exactly <paramref name="quantity"/> units or nothing. The previous version
        /// removed what it found and then reported "insufficient quantity", so a failed
        /// remove/transfer silently destroyed items (KI-019).
        /// </summary>
        public CommandResult Remove(InventoryId target, ContentId itemId, int quantity) =>
            Remove(target, itemId, quantity, null);

        /// <summary>
        /// Removes exactly <paramref name="quantity"/> units or nothing.
        /// <para>
        /// <paramref name="missionInstanceId"/> restricts the removal to stacks bound to one mission
        /// instance (empty or null = any stack). Quest items are per-instance: two missions can grant
        /// the same item id, and until 2026-10-19 a removal took from whichever stack it found first,
        /// so turning in mission B could consume mission A's item (DEF-021).
        /// </para>
        /// </summary>
        public CommandResult Remove(InventoryId target, ContentId itemId, int quantity, string missionInstanceId)
        {
            string filter = missionInstanceId ?? string.Empty;
            if (quantity <= 0) return CommandResult.Fail(FailureReason.InvalidTarget);
            if (!_stacks.TryGetValue(target, out var list))
                return CommandResult.Fail(FailureReason.ItemNotFound);
            int available = QuantityOf(target, itemId.Value, filter);
            if (available < quantity)
                return CommandResult.Fail(FailureReason.ItemNotFound, $"insufficient quantity: have {available}, need {quantity}");

            int remaining = quantity;
            for (int i = 0; i < list.Count && remaining > 0; i++)
            {
                var s = list[i];
                if (s.ItemId.Value != itemId.Value) continue;
                if (filter.Length > 0 && !string.Equals(s.MissionInstanceId, filter, StringComparison.Ordinal)) continue;
                int take = Math.Min(s.Quantity, remaining);
                remaining -= take;
                if (s.Quantity - take <= 0) list.RemoveAt(i--);
                else list[i] = new ItemStack(s.ItemId, s.Quantity - take, s.MissionInstanceId);
            }
            return CommandResult.Ok();
        }

        /// <summary>
        /// Validated transfer (Appendix D IInventoryService.Transfer). All-or-nothing: on failure
        /// both containers are restored exactly (re-adding could re-split stacks differently).
        /// </summary>
        public CommandResult Transfer(InventoryId from, InventoryId to, ItemStack stack)
        {
            var checkpoint = Checkpoint();
            var remove = Remove(from, stack.ItemId, stack.Quantity);
            if (!remove.Success) return remove;
            var add = Add(to, stack);
            if (!add.Success)
            {
                Rollback(checkpoint); // no items may vanish on a failed transfer
                return add;
            }
            return CommandResult.Ok();
        }

        /// <summary>
        /// Adds several stacks atomically (a cache can hold Salvage plus materials). Either every
        /// stack is placed or the container is left exactly as it was (QA case 3).
        /// </summary>
        public CommandResult AddAll(InventoryId target, IReadOnlyList<ItemStack> stacks)
        {
            if (stacks == null || stacks.Count == 0) return CommandResult.Ok();
            var checkpoint = Checkpoint();
            for (int i = 0; i < stacks.Count; i++)
            {
                var result = Add(target, stacks[i]);
                if (!result.Success)
                {
                    Rollback(checkpoint);
                    return result;
                }
            }
            return CommandResult.Ok();
        }

        /// <summary>Opaque copy of every container's stacks plus the protected ledger.</summary>
        public sealed class InventoryCheckpoint
        {
            internal readonly Dictionary<InventoryId, List<ItemStack>> Stacks = new Dictionary<InventoryId, List<ItemStack>>();
            internal readonly List<string> Ledger = new List<string>();
        }

        /// <summary>Captures state so a multi-step transaction can roll back exactly.</summary>
        public InventoryCheckpoint Checkpoint()
        {
            var checkpoint = new InventoryCheckpoint();
            foreach (var kv in _stacks)
                checkpoint.Stacks[kv.Key] = new List<ItemStack>(kv.Value);
            checkpoint.Ledger.AddRange(_protectedLedger);
            return checkpoint;
        }

        /// <summary>Restores stacks and ledger captured by <see cref="Checkpoint"/>. Capacities are untouched.</summary>
        public void Rollback(InventoryCheckpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            _stacks.Clear();
            foreach (var kv in checkpoint.Stacks)
                _stacks[kv.Key] = new List<ItemStack>(kv.Value);
            _protectedLedger.Clear();
            _protectedLedger.AddRange(checkpoint.Ledger);
        }

        /// <summary>Configured (slots, massKg) for a container; (0, 0) when unknown.</summary>
        public (int slots, float massKg) CapacityOf(InventoryId id) =>
            _capacity.TryGetValue(id, out var cap) ? cap : (0, 0f);

        public bool HasContainer(InventoryId id) => _capacity.ContainsKey(id);

        /// <summary>Protected-ledger objects in acquisition order (saved as protectedLedgerItemIds).</summary>
        public IReadOnlyList<string> ProtectedLedgerItems => _protectedLedger;

        /// <summary>Consumes one protected-ledger object (e.g. the Q04 relay fuse at the relay).</summary>
        public CommandResult RemoveProtected(string itemId)
        {
            if (!_protectedLedger.Remove(itemId))
                return CommandResult.Fail(FailureReason.ItemNotFound, $"{itemId} not in protected ledger");
            return CommandResult.Ok();
        }

        /// <summary>QA case 9: removing a capacity upgrade while over capacity is blocked upstream;
        /// this predicate is the enforcement point.</summary>
        public bool IsOverCapacity(InventoryId id) =>
            _capacity.TryGetValue(id, out var cap) &&
            (UsedSlots(id) > cap.slots || TotalMassKg(id) > cap.massKg + 1e-3f);

        public void Restore(InventoryId id, IEnumerable<ItemStack> stacks, (int slots, float massKg) capacity)
        {
            _capacity[id] = capacity;
            _stacks[id] = new List<ItemStack>(stacks ?? Array.Empty<ItemStack>());
        }

        public void RestoreProtectedLedger(IEnumerable<string> itemIds)
        {
            _protectedLedger.Clear();
            if (itemIds != null) _protectedLedger.AddRange(itemIds);
        }
    }
}
