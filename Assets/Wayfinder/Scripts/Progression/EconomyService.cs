using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Progression
{
    /// <summary>
    /// The single authoritative currency ledger (§10.3, §11.4). Salvage is the ONLY spendable
    /// currency. All mutations go through TransactionService so they are idempotent.
    /// No operation can make the balance negative (QA case 6/17).
    /// </summary>
    public sealed class EconomyService
    {
        public int BankedSalvage { get; private set; }
        public int EscrowedSalvage { get; private set; } // pending banking (mapping rewards, mission escrow)

        public CommandResult TrySpend(int amount, TransactionId transactionId)
        {
            if (amount < 0) return CommandResult.Fail(FailureReason.InvalidTarget, "negative spend");
            if (amount > BankedSalvage)
                return CommandResult.Fail(FailureReason.InsufficientCurrency, $"have {BankedSalvage}, need {amount}");
            BankedSalvage -= amount;
            return CommandResult.Ok();
        }

        public void Grant(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            BankedSalvage += amount;
        }

        /// <summary>Escrow holds rewards until garage/camp banking (§2.3, §8.4).</summary>
        public void Escrow(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            EscrowedSalvage += amount;
        }

        /// <summary>Banks escrow → wallet. Idempotent via TransactionService wrapper.</summary>
        public int BankEscrow()
        {
            int banked = EscrowedSalvage;
            EscrowedSalvage = 0;
            BankedSalvage += banked;
            return banked;
        }

        /// <summary>
        /// Sells items at 40% of purchase price, rounded down (§10.4).
        /// Crafted consumables cost more to make than they sell for — enforced by the arbitrage test.
        /// </summary>
        public CommandResult Sell(string itemId, int quantity)
        {
            if (quantity <= 0) return CommandResult.Fail(FailureReason.InvalidTarget);
            var def = ItemCatalog.Get(itemId);
            if (!def.Sellable) return CommandResult.Fail(FailureReason.ItemNotUsableHere, "not sellable");
            int unit = ItemCatalog.SellPrice(itemId);
            Grant(unit * quantity);
            return CommandResult.Ok();
        }

        public void Restore(int banked, int escrow)
        {
            BankedSalvage = Math.Max(0, banked);
            EscrowedSalvage = Math.Max(0, escrow);
        }
    }

    /// <summary>Contents of a one-time pickup (cache), resolved from content (caches.json).</summary>
    public sealed class PickupContents
    {
        public readonly string PickupId;
        public readonly int SalvageUnits;
        public readonly IReadOnlyList<ItemStack> Stacks;

        public PickupContents(string pickupId, int salvageUnits, IEnumerable<(string itemId, int quantity)> materials)
        {
            if (string.IsNullOrEmpty(pickupId)) throw new ArgumentException("pickup id required", nameof(pickupId));
            if (salvageUnits < 0) throw new ArgumentOutOfRangeException(nameof(salvageUnits));
            PickupId = pickupId;
            SalvageUnits = salvageUnits;
            var stacks = new List<ItemStack>();
            if (salvageUnits > 0)
                stacks.Add(new ItemStack(new ContentId(ItemCatalog.SalvageBundle), salvageUnits));
            if (materials != null)
                foreach (var (itemId, quantity) in materials)
                    if (quantity > 0) stacks.Add(new ItemStack(new ContentId(itemId), quantity));
            Stacks = stacks;
        }
    }

    /// <summary>Resolves pickup ids to their authored contents (implemented by the campaign content catalog).</summary>
    public interface IPickupCatalog
    {
        bool TryGetContents(string pickupId, out PickupContents contents);
    }

    /// <summary>Dictionary-backed pickup catalog (tests and the campaign content loader).</summary>
    public sealed class PickupCatalog : IPickupCatalog
    {
        readonly Dictionary<string, PickupContents> _byId = new Dictionary<string, PickupContents>(StringComparer.Ordinal);

        public PickupCatalog Add(PickupContents contents)
        {
            if (contents == null) throw new ArgumentNullException(nameof(contents));
            _byId[contents.PickupId] = contents;
            return this;
        }

        public bool TryGetContents(string pickupId, out PickupContents contents) =>
            _byId.TryGetValue(pickupId ?? string.Empty, out contents);

        public int Count => _byId.Count;
    }

    /// <summary>
    /// Explicit collaborators of <see cref="TransactionService"/> (§15.3 composition root). A
    /// command whose collaborator is missing fails with a typed error — there is no hidden
    /// static fallback (the previous research path silently used ResearchService.Global, KI-023).
    /// </summary>
    public sealed class TransactionDependencies
    {
        public ResearchService Research;
        public DiscoveryService Discovery;
        public GarageService Garage;
        public IPickupCatalog Pickups;
    }

    /// <summary>
    /// Atomic, idempotent gameplay mutations (§12.4, §16.3, Appendix D).
    /// Every command carries a TransactionId; replaying an applied ID is a no-op success.
    /// Multi-step commands checkpoint inventory + wallet and roll back on any failure, so a
    /// failed command never leaves partial state. Persistence (write/verify before
    /// acknowledging) is layered on top by the campaign session (QA case 4).
    /// </summary>
    public sealed class TransactionService
    {
        readonly HashSet<string> _applied = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> _appliedOrder = new List<string>();
        readonly EconomyService _economy;
        readonly InventoryService _inventory;
        readonly TransactionDependencies _deps;

        /// <summary>Fired only AFTER a transaction commits (§16.3).</summary>
        public event Action<GameEvent> OnCommitted;

        public TransactionService(EconomyService economy, InventoryService inventory)
            : this(economy, inventory, null) { }

        public TransactionService(EconomyService economy, InventoryService inventory, TransactionDependencies dependencies)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _deps = dependencies ?? new TransactionDependencies();
        }

        public bool WasApplied(TransactionId transactionId) =>
            _applied.Contains(transactionId.Value);

        public IReadOnlyList<string> AppliedOrder => _appliedOrder;

        /// <summary>
        /// Executes a command exactly once per transaction ID (Appendix D ITransactionService).
        /// </summary>
        public CommandResult Execute(GameCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (WasApplied(command.TransactionId))
                return CommandResult.Ok(); // idempotent replay — never grants twice (QA cases 1/2/16)

            var inventoryCheckpoint = _inventory.Checkpoint();
            int bankedBefore = _economy.BankedSalvage, escrowBefore = _economy.EscrowedSalvage;
            GameEvent committed = null;
            CommandResult result;
            switch (command)
            {
                case CollectPickup collect:
                    result = ExecuteCollect(collect, out committed);
                    break;
                case BankCargo bank:
                    result = ExecuteBank(bank, out committed);
                    break;
                case ResearchUpgrade research:
                    result = ExecuteResearch(research, out committed);
                    break;
                case EstablishCamp establish:
                    result = ExecuteEstablishCamp(establish, out committed);
                    break;
                case CraftItem craft:
                    result = CraftingService.TryCraft(_inventory, _economy, craft.RecipeId, craft.MaterialSource,
                        craft.OutputDestination);
                    break;
                case TransferItems transfer:
                    result = _inventory.Transfer(transfer.From, transfer.To,
                        new ItemStack(transfer.ItemId, transfer.Quantity));
                    break;
                case SellItems sell:
                    result = _inventory.Remove(sell.Source, sell.ItemId, sell.Quantity);
                    if (result.Success) result = _economy.Sell(sell.ItemId.Value, sell.Quantity);
                    break;
                case UseItem use:
                    // §5.6 "Field supplies: consume one item on successful use". The caller validated
                    // the use conditions and applies the effect only after this commit succeeds.
                    result = ItemCatalog.TryGet(use.ItemId.Value, out _) &&
                             ItemCatalog.CategoryOf(use.ItemId.Value) == ItemCategory.Consumable
                        ? _inventory.Remove(use.Source, use.ItemId, 1)
                        : CommandResult.Fail(FailureReason.ItemNotUsableHere, $"'{use.ItemId.Value}' is not a field supply");
                    break;
                case RequestRecovery _:
                    // Recovery itself is always available (REC-01); the campaign session applies
                    // the rescue rules and persists them. Here it is an event-only marker.
                    result = CommandResult.Ok();
                    break;
                default:
                    return CommandResult.Fail(FailureReason.InvalidTarget, $"unhandled command {command.GetType().Name}");
            }

            if (!result.Success)
            {
                // Atomicity: whatever a failed command touched is restored exactly.
                _inventory.Rollback(inventoryCheckpoint);
                _economy.Restore(bankedBefore, escrowBefore);
                return result;
            }
            Commit(command.TransactionId, committed);
            return result;
        }

        CommandResult ExecuteCollect(CollectPickup cmd, out GameEvent committed)
        {
            committed = null;
            string pickupId = cmd.PickupId.Value;
            if (_deps.Pickups == null)
                return CommandResult.Fail(FailureReason.ContentMissing, "pickup catalog not configured");
            if (!_deps.Pickups.TryGetContents(pickupId, out var contents))
                return CommandResult.Fail(FailureReason.ContentMissing, $"unknown pickup '{pickupId}'");
            // Double-collect guard lives in the same commit as the grant (QA case 2).
            if (_deps.Discovery != null && _deps.Discovery.IsCacheCollected(pickupId))
                return CommandResult.Fail(FailureReason.AlreadyCollected, pickupId);

            // All stacks fit or nothing is added and the cache stays uncollected (QA case 3).
            var add = _inventory.AddAll(cmd.Destination, contents.Stacks);
            if (!add.Success) return add;
            _deps.Discovery?.TryMarkCacheCollected(pickupId);
            committed = new PickupCollected(cmd.TransactionId, cmd.PickupId, contents.SalvageUnits);
            return CommandResult.Ok();
        }

        CommandResult ExecuteBank(BankCargo cmd, out GameEvent committed)
        {
            committed = null;
            var settlement = CargoSettlement.Settle(_inventory, _economy, cmd.CargoSource, cmd.MaterialDestination);
            if (!settlement.Result.Success) return settlement.Result;
            committed = new ExpeditionBanked(cmd.TransactionId, cmd.TransactionId.Value,
                settlement.SalvageUnitsBanked + settlement.EscrowBanked, 0);
            return CommandResult.Ok();
        }

        CommandResult ExecuteResearch(ResearchUpgrade cmd, out GameEvent committed)
        {
            committed = null;
            if (_deps.Research == null)
                return CommandResult.Fail(FailureReason.InternalError, "research service not configured");
            var result = _deps.Research.TryPurchase(cmd.UpgradeId, _economy);
            if (!result.Success) return result;
            committed = new UpgradeResearched(cmd.TransactionId, cmd.UpgradeId);
            return CommandResult.Ok();
        }

        CommandResult ExecuteEstablishCamp(EstablishCamp cmd, out GameEvent committed)
        {
            committed = null;
            // Atomicity (QA case 10): the kit is consumed in the SAME commit that activates the
            // camp. The previous version only removed the kit and relied on an event subscriber
            // to activate the camp (KI-021).
            if (_deps.Garage == null)
                return CommandResult.Fail(FailureReason.InternalError, "garage service not configured");
            string campId = cmd.CampId.Value;
            if (!GarageService.IsKnownCamp(campId))
                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown camp socket '{campId}'");
            if (_deps.Garage.IsCampActivated(campId))
                return CommandResult.Fail(FailureReason.AlreadyApplied, "camp already established");
            if (_inventory.QuantityOf(cmd.KitSource, ItemCatalog.CampKit) < 1)
                return CommandResult.Fail(FailureReason.ItemNotFound, "camp kit required");
            var remove = _inventory.Remove(cmd.KitSource, new ContentId(ItemCatalog.CampKit), 1);
            if (!remove.Success) return remove;
            _deps.Garage.ActivateCamp(campId);
            committed = new CampEstablished(cmd.TransactionId, cmd.CampId);
            return CommandResult.Ok();
        }

        void Commit(TransactionId transactionId, GameEvent committedEvent)
        {
            _applied.Add(transactionId.Value);
            _appliedOrder.Add(transactionId.Value);
            if (committedEvent != null)
                OnCommitted?.Invoke(committedEvent);
        }

        public void Restore(IEnumerable<string> appliedIds)
        {
            _applied.Clear();
            _appliedOrder.Clear();
            if (appliedIds == null) return;
            foreach (var id in appliedIds)
            {
                if (!string.IsNullOrEmpty(id) && _applied.Add(id))
                    _appliedOrder.Add(id);
            }
        }
    }

    /// <summary>
    /// Hub settlement shared by garage extraction and camp deposit (§2.3, §12.2, §12.4):
    /// every salvage_bundle unit banks into 1 Salvage, escrow banks, and crafting materials
    /// move to hub storage when a destination is given. Materials that do not fit (camp storage
    /// is 500 kg) simply stay in cargo — nothing is lost. Consumables and mission objects stay loaded.
    /// </summary>
    public static class CargoSettlement
    {
        public readonly struct Outcome
        {
            public readonly CommandResult Result;
            public readonly int SalvageUnitsBanked;
            public readonly int EscrowBanked;
            public readonly int MaterialUnitsStored;
            public readonly int MaterialUnitsLeftInCargo;

            public Outcome(CommandResult result, int salvage, int escrow, int stored, int left)
            {
                Result = result;
                SalvageUnitsBanked = salvage;
                EscrowBanked = escrow;
                MaterialUnitsStored = stored;
                MaterialUnitsLeftInCargo = left;
            }
        }

        public static Outcome Settle(InventoryService inventory, EconomyService economy,
            InventoryId cargo, InventoryId materialDestination)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (economy == null) throw new ArgumentNullException(nameof(economy));

            int units = inventory.QuantityOf(cargo, ItemCatalog.SalvageBundle);
            if (units > 0)
            {
                var remove = inventory.Remove(cargo, new ContentId(ItemCatalog.SalvageBundle), units);
                if (!remove.Success) return new Outcome(remove, 0, 0, 0, 0);
            }

            int stored = 0, left = 0;
            if (!string.IsNullOrEmpty(materialDestination.Value))
            {
                // Snapshot the material totals first: moving mutates the stack list.
                var totals = new List<(string itemId, int quantity)>();
                foreach (var stack in inventory.Stacks(cargo))
                {
                    if (ItemCatalog.CategoryOf(stack.ItemId.Value) != ItemCategory.Material) continue;
                    int index = totals.FindIndex(t => t.itemId == stack.ItemId.Value);
                    if (index < 0) totals.Add((stack.ItemId.Value, stack.Quantity));
                    else totals[index] = (totals[index].itemId, totals[index].quantity + stack.Quantity);
                }
                foreach (var (itemId, quantity) in totals)
                {
                    // Largest quantity that fits, so a nearly full camp store still takes something.
                    int moved = 0;
                    for (int q = quantity; q > 0; q--)
                    {
                        if (!inventory.CanAdd(materialDestination, new ItemStack(new ContentId(itemId), q)).Fits) continue;
                        if (inventory.Transfer(cargo, materialDestination, new ItemStack(new ContentId(itemId), q)).Success)
                            moved = q;
                        break;
                    }
                    stored += moved;
                    left += quantity - moved;
                }
            }

            int escrow = economy.BankEscrow();
            economy.Grant(units); // 1 salvage_bundle unit banks into 1 Salvage (§10.3)
            return new Outcome(CommandResult.Ok(), units, escrow, stored, left);
        }
    }

    /// <summary>
    /// Deterministic crafting (§12.3). Recipes validate, reserve, produce, commit atomically.
    /// If capacity would fail, materials are NOT consumed. Automated arbitrage test (QA case 17):
    /// crafting + reselling must never create currency.
    /// </summary>
    public static class CraftingService
    {
        public sealed class Recipe
        {
            public ContentId Id;
            public readonly List<(string itemId, int quantity)> Inputs = new List<(string, int)>();
            public string OutputItemId;
            public int OutputQuantity = 1;
            /// <summary>Optional Salvage fee paid on craft (e.g. repair_kit costs 20).</summary>
            public int SalvageFee;

            public Recipe With(string itemId, int quantity)
            {
                Inputs.Add((itemId, quantity));
                return this;
            }
        }

        public static readonly Recipe[] Recipes =
        {
            new Recipe
            {
                Id = new ContentId("recipe.repair_kit"), OutputItemId = "repair_kit", OutputQuantity = 1,
                SalvageFee = 20
            }.With("scrap_metal", 2).With("polymer", 1),
            new Recipe
            {
                Id = new ContentId("recipe.tire_patch"), OutputItemId = "tire_patch", OutputQuantity = 1,
                SalvageFee = 10
            }.With("polymer", 1),
            new Recipe
            {
                Id = new ContentId("recipe.recovery_boards"), OutputItemId = "recovery_boards", OutputQuantity = 1,
                SalvageFee = 15
            }.With("polymer", 2).With("scrap_metal", 1),
            new Recipe
            {
                Id = new ContentId("recipe.camp_kit"), OutputItemId = "camp_kit", OutputQuantity = 1,
                SalvageFee = 80
            }.With("scrap_metal", 4).With("polymer", 2).With("electronics", 1)
        };

        public static Recipe GetRecipe(ContentId id)
        {
            foreach (var r in Recipes)
                if (r.Id.Value == id.Value) return r;
            throw new KeyNotFoundException($"Unknown recipe '{id}'.");
        }

        /// <summary>
        /// Crafts one recipe: validates materials, fee and output capacity BEFORE consuming
        /// anything, then consumes inputs, charges the Salvage fee and produces the output.
        /// The fee was previously never charged (KI-022), so e.g. a camp kit cost 0 instead of 80.
        /// </summary>
        public static CommandResult TryCraft(InventoryService inventory, EconomyService economy, ContentId recipeId,
            InventoryId materialSource, InventoryId outputDestination)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            var recipe = GetRecipe(recipeId);

            // 1. Validate ALL inputs first (never consume on failure).
            foreach (var (itemId, qty) in recipe.Inputs)
            {
                if (inventory.QuantityOf(materialSource, itemId) < qty)
                    return CommandResult.Fail(FailureReason.InsufficientMaterials, $"{itemId} ×{qty}");
            }

            if (economy.BankedSalvage < recipe.SalvageFee)
                return CommandResult.Fail(FailureReason.InsufficientCurrency,
                    $"fee {recipe.SalvageFee}, have {economy.BankedSalvage}");

            // 2. Capacity preview for the output.
            var outputStack = new ItemStack(new ContentId(recipe.OutputItemId), recipe.OutputQuantity);
            var capacity = inventory.CanAdd(outputDestination, outputStack);
            if (!capacity.Fits)
                return CommandResult.Fail(capacity.Reason, "output would not fit");

            // 3. Consume inputs, charge the fee, then produce — rolled back as one unit on failure.
            var checkpoint = inventory.Checkpoint();
            int banked = economy.BankedSalvage, escrow = economy.EscrowedSalvage;
            foreach (var (itemId, qty) in recipe.Inputs)
            {
                var remove = inventory.Remove(materialSource, new ContentId(itemId), qty);
                if (!remove.Success)
                {
                    inventory.Rollback(checkpoint);
                    return CommandResult.Fail(FailureReason.InternalError, "input vanished mid-craft");
                }
            }
            var fee = economy.TrySpend(recipe.SalvageFee, default);
            var add = fee.Success ? inventory.Add(outputDestination, outputStack) : fee;
            if (!add.Success)
            {
                inventory.Rollback(checkpoint);
                economy.Restore(banked, escrow);
            }
            return add;
        }

        /// <summary>QA case 17 predicate: sell price of output must be less than total input purchase value + fee.</summary>
        public static bool CraftResaleCreatesCurrency(Recipe recipe)
        {
            int inputPurchaseValue = 0;
            foreach (var (itemId, qty) in recipe.Inputs)
                inputPurchaseValue += ItemCatalog.Get(itemId).PurchasePriceSalvage * qty;
            inputPurchaseValue += recipe.SalvageFee;

            int outputSellValue = ItemCatalog.SellPrice(recipe.OutputItemId) * recipe.OutputQuantity;
            return outputSellValue > inputPurchaseValue;
        }
    }
}
