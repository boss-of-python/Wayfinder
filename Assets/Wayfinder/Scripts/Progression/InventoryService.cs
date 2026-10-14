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

    public static class ItemCatalog
    {
        public const string SalvageBundle = "salvage_bundle";

        static readonly Dictionary<string, ItemDefinition> Items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal)
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
            ["quest.stabilizer_plates"] = Def("quest.stabilizer_plates", "item.stabilizer_plates.name", 1, 40.0f, false, false, 0)
        };

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

        public int QuantityOf(InventoryId id, string itemId)
        {
            int q = 0;
            foreach (var s in Stacks(id))
                if (s.ItemId.Value == itemId) q += s.Quantity;
            return q;
        }

        public bool ProtectedLedgerContains(string itemId) => _protectedLedger.Contains(itemId);

        public CapacityResult CanAdd(InventoryId target, ItemStack stack)
        {
            var def = ItemCatalog.Get(stack.ItemId);
            if (!_capacity.TryGetValue(target, out var cap))
                return CapacityResult.Rejected(FailureReason.InvalidTarget, 0, 0, 0f, 0f);

            if (def.ProtectedLedger)
                return CapacityResult.Fit(0, cap.slots, stack.MassKg, cap.massKg);

            int freeSlots = cap.slots - UsedSlots(target);
            float freeMass = cap.massKg - TotalMassKg(target);

            // Merge into an existing compatible stack if possible (same item + mission instance).
            bool canMerge = false;
            foreach (var s in Stacks(target))
            {
                if (s.ItemId.Value == stack.ItemId.Value && s.MissionInstanceId == stack.MissionInstanceId
                    && s.Quantity + stack.Quantity <= def.MaxStack)
                {
                    canMerge = true;
                    break;
                }
            }

            int requiredSlots = canMerge ? 0 : 1;
            if (requiredSlots > freeSlots)
                return CapacityResult.Rejected(FailureReason.InsufficientSlots, requiredSlots, freeSlots, stack.MassKg, freeMass);
            if (stack.MassKg > freeMass + 1e-4f)
                return CapacityResult.Rejected(FailureReason.InsufficientMassCapacity, requiredSlots, freeSlots, stack.MassKg, freeMass);
            return CapacityResult.Fit(requiredSlots, freeSlots, stack.MassKg, freeMass);
        }

        /// <summary>
        /// Adds items. Returns typed failure WITHOUT mutating state when capacity fails
        /// (QA case 3: the pickup source must not be consumed by a failed add).
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

            if (!_stacks.TryGetValue(target, out var list))
            {
                list = new List<ItemStack>();
                _stacks[target] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.ItemId.Value == stack.ItemId.Value && s.MissionInstanceId == stack.MissionInstanceId
                    && s.Quantity + stack.Quantity <= def.MaxStack)
                {
                    list[i] = new ItemStack(s.ItemId, s.Quantity + stack.Quantity, s.MissionInstanceId);
                    return CommandResult.Ok();
                }
            }
            list.Add(stack);
            return CommandResult.Ok();
        }

        public CommandResult Remove(InventoryId target, ContentId itemId, int quantity)
        {
            if (quantity <= 0) return CommandResult.Fail(FailureReason.InvalidTarget);
            if (!_stacks.TryGetValue(target, out var list))
                return CommandResult.Fail(FailureReason.ItemNotFound);

            int remaining = quantity;
            for (int i = 0; i < list.Count && remaining > 0; i++)
            {
                var s = list[i];
                if (s.ItemId.Value != itemId.Value) continue;
                int take = Math.Min(s.Quantity, remaining);
                remaining -= take;
                if (s.Quantity - take <= 0) list.RemoveAt(i--);
                else list[i] = new ItemStack(s.ItemId, s.Quantity - take, s.MissionInstanceId);
            }
            return remaining == 0 ? CommandResult.Ok() : CommandResult.Fail(FailureReason.ItemNotFound, "insufficient quantity");
        }

        /// <summary>Validated transfer (Appendix D IInventoryService.Transfer). Atomic per stack.</summary>
        public CommandResult Transfer(InventoryId from, InventoryId to, ItemStack stack)
        {
            var remove = Remove(from, stack.ItemId, stack.Quantity);
            if (!remove.Success) return remove;
            var add = Add(to, stack);
            if (!add.Success)
            {
                // Roll back — no items may vanish on a failed transfer.
                Add(from, stack);
                return add;
            }
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
