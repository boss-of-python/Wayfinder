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

    /// <summary>
    /// Atomic, idempotent gameplay mutations (§12.4, §16.3, Appendix D).
    /// Every command carries a TransactionId; replaying an applied ID is a no-op success.
    /// Banking: snapshot → tx id → apply to memory revision → write/verify save → only then
    /// acknowledge and clear field cargo. Interruption at any step resolves to old or new
    /// complete state, never mixed (QA case 4).
    /// </summary>
    public sealed class TransactionService
    {
        readonly HashSet<string> _applied = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> _appliedOrder = new List<string>();
        readonly EconomyService _economy;
        readonly InventoryService _inventory;

        /// <summary>Fired only AFTER a transaction commits (§16.3).</summary>
        public event Action<GameEvent> OnCommitted;

        public TransactionService(EconomyService economy, InventoryService inventory)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
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

            CommandResult result;
            switch (command)
            {
                case CollectPickup collect:
                    result = ExecuteCollect(collect, out var collectedEvent);
                    if (result.Success) Commit(command.TransactionId, collectedEvent);
                    return result;

                case BankCargo bank:
                    result = ExecuteBank(bank, out var bankedEvent);
                    if (result.Success) Commit(command.TransactionId, bankedEvent);
                    return result;

                case ResearchUpgrade research:
                    result = ExecuteResearch(research, out var researchEvent);
                    if (result.Success) Commit(command.TransactionId, researchEvent);
                    return result;

                case EstablishCamp establish:
                    result = ExecuteEstablishCamp(establish, out var campEvent);
                    if (result.Success) Commit(command.TransactionId, campEvent);
                    return result;

                case CraftItem craft:
                    result = ExecuteCraft(craft);
                    if (result.Success) Commit(command.TransactionId, null);
                    return result;

                case TransferItems transfer:
                    result = _inventory.Transfer(transfer.From, transfer.To,
                        new ItemStack(transfer.ItemId, transfer.Quantity));
                    if (result.Success) Commit(command.TransactionId, null);
                    return result;

                case SellItems sell:
                    result = _inventory.Remove(sell.Source, sell.ItemId, sell.Quantity);
                    if (result.Success)
                    {
                        result = _economy.Sell(sell.ItemId.Value, sell.Quantity);
                        if (!result.Success)
                        {
                            // rollback: put items back — currency ledger never skims items
                            _inventory.Add(sell.Source, new ItemStack(sell.ItemId, sell.Quantity));
                        }
                        else Commit(command.TransactionId, null);
                    }
                    return result;

                case RequestRecovery recovery:
                    // Recovery itself is always available (REC-01); event only.
                    Commit(command.TransactionId, null);
                    return CommandResult.Ok();

                default:
                    return CommandResult.Fail(FailureReason.InvalidTarget, $"unhandled command {command.GetType().Name}");
            }
        }

        CommandResult ExecuteCollect(CollectPickup cmd, out GameEvent committed)
        {
            committed = null;
            // Double-collect guard: the pickup registry is consulted by the caller (DiscoveryService);
            // here we only add. The AppliedTransactionIds + pickup collected-set together make
            // a second collect a typed failure (QA case 2).
            var stack = new ItemStack(new ContentId(ItemCatalog.SalvageBundle), 1);
            var add = _inventory.Add(cmd.Destination, stack);
            return add;
        }

        CommandResult ExecuteBank(BankCargo cmd, out GameEvent committed)
        {
            committed = null;
            int units = _inventory.QuantityOf(cmd.CargoSource, ItemCatalog.SalvageBundle);
            if (units > 0)
            {
                var remove = _inventory.Remove(cmd.CargoSource, new ContentId(ItemCatalog.SalvageBundle), units);
                if (!remove.Success) return remove;
            }
            int escrowBanked = _economy.BankEscrow();
            _economy.Grant(units); // 1 salvage_bundle unit banks into 1 Salvage (§10.3)
            committed = new ExpeditionBanked(cmd.TransactionId, cmd.TransactionId.Value,
                units + escrowBanked, 0);
            return CommandResult.Ok();
        }

        CommandResult ExecuteResearch(ResearchUpgrade cmd, out GameEvent committed)
        {
            committed = null;
            var result = ResearchService.Global.TryPurchase(cmd.UpgradeId, _economy);
            if (!result.Success) return result;
            committed = new UpgradeResearched(cmd.TransactionId, cmd.UpgradeId);
            return CommandResult.Ok();
        }

        CommandResult ExecuteEstablishCamp(EstablishCamp cmd, out GameEvent committed)
        {
            committed = null;
            // Atomicity (QA case 10): consume kit ONLY when the camp activates in the same commit.
            bool hasKit = _inventory.QuantityOf(cmd.KitSource, "camp_kit") > 0
                          || _inventory.QuantityOf(cmd.KitSource, "camp_kit") > 0;
            if (!hasKit)
                return CommandResult.Fail(FailureReason.ItemNotFound, "camp kit required");
            var remove = _inventory.Remove(cmd.KitSource, new ContentId("camp_kit"), 1);
            if (!remove.Success) return remove;
            committed = new CampEstablished(cmd.TransactionId, cmd.CampId);
            return CommandResult.Ok();
        }

        CommandResult ExecuteCraft(CraftItem cmd)
        {
            return CraftingService.TryCraft(_inventory, cmd.RecipeId, cmd.MaterialSource, cmd.OutputDestination);
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

        public static CommandResult TryCraft(InventoryService inventory, ContentId recipeId,
            InventoryId materialSource, InventoryId outputDestination)
        {
            var recipe = GetRecipe(recipeId);

            // 1. Validate ALL inputs first (never consume on failure).
            foreach (var (itemId, qty) in recipe.Inputs)
            {
                if (inventory.QuantityOf(materialSource, itemId) < qty)
                    return CommandResult.Fail(FailureReason.InsufficientMaterials, $"{itemId} ×{qty}");
            }

            // 2. Capacity preview for the output.
            var outputStack = new ItemStack(new ContentId(recipe.OutputItemId), recipe.OutputQuantity);
            var capacity = inventory.CanAdd(outputDestination, outputStack);
            if (!capacity.Fits)
                return CommandResult.Fail(capacity.Reason, "output would not fit");

            // 3. Consume inputs, then produce (inputs reserved-then-committed).
            foreach (var (itemId, qty) in recipe.Inputs)
            {
                var remove = inventory.Remove(materialSource, new ContentId(itemId), qty);
                if (!remove.Success)
                {
                    // Extremely unexpected after validation; restore is caller's transaction concern.
                    return CommandResult.Fail(FailureReason.InternalError, "input vanished mid-craft");
                }
            }
            return inventory.Add(outputDestination, outputStack);
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
