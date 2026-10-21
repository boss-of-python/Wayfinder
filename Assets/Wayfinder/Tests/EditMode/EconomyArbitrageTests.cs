using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    /// <summary>
    /// §10.4's economy invariants: "Materials can be sold at 40% of purchase price, rounded down.
    /// Crafted consumables sell for less than their total input purchase value. Add an automated
    /// arbitrage test; crafting/reselling must never create currency."
    ///
    /// Two gaps this closes.
    /// (1) The tables that actually run — <see cref="ItemCatalog"/> and <see cref="CraftingService"/>'s
    /// <c>Recipes</c> — are hardcoded in C#, while <c>items.json</c> / <c>recipes.json</c> are what
    /// <c>validate_content.py</c> checks and what the editor importer collects ids from;
    /// <c>CampaignContent.RequiredFiles</c> lists neither. Nothing pinned the two together, so the
    /// validator's price, mass and arbitrage rules applied to files the runtime never loads — the
    /// shape of DEF-016, which the validator's own header warns about. The parity tests make the JSON
    /// and the code agree by assertion instead of by luck.
    /// (2) The pre-existing QA-17 predicate adds the salvage fee to the input side, which is *weaker*
    /// than the spec sentence. Both rules are asserted here, separately (D-043).
    /// </summary>
    public class EconomyArbitrageTests
    {
        string _root;
        SaveService _saves;

        [SetUp]
        public void SetUp()
        {
            _root = NewTempRoot();
            _saves = new SaveService(_root);
        }

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        static JArray Content(string file) =>
            JArray.Parse(File.ReadAllText(Path.Combine(ContentDir, file)));

        /// <summary>§10.4's "total input purchase value" — materials only, no crafting fee.</summary>
        static int InputPurchaseValue(CraftingService.Recipe r) =>
            r.Inputs.Sum(i => ItemCatalog.Get(i.itemId).PurchasePriceSalvage * i.quantity);

        static int OutputSellValue(CraftingService.Recipe r) =>
            ItemCatalog.SellPrice(r.OutputItemId) * r.OutputQuantity;

        CampaignSession AtTheGarage()
        {
            var s = NewGame(_saves);
            Assert.IsTrue(s.AtGarage, "a new game is parked at the home garage, where crafting and selling happen");
            return s;
        }

        // ============================================ the two content tables must not drift

        [Test]
        public void TheItemTableInCodeMatchesTheContentFile()
        {
            var json = Content("items.json");
            CollectionAssert.AreEquivalent(json.Select(t => t.Value<string>("id")).ToList(),
                ItemCatalog.All.Keys.ToList(),
                "items.json and ItemCatalog are two sources of truth for one table, and neither is " +
                "loaded by CampaignContent — so they are pinned together here");

            foreach (var token in json)
            {
                string id = token.Value<string>("id");
                var def = ItemCatalog.Get(id);
                Assert.AreEqual(token.Value<string>("nameKey"), def.NameKey, $"{id}: nameKey");
                Assert.AreEqual(token.Value<int>("maxStack"), def.MaxStack, $"{id}: maxStack");
                Assert.AreEqual(token.Value<float>("unitMassKg"), def.UnitMassKg, 1e-4f, $"{id}: unitMassKg");
                Assert.AreEqual(token.Value<bool>("protectedLedger"), def.ProtectedLedger, $"{id}: protectedLedger");
                Assert.AreEqual(token.Value<bool>("sellable"), def.Sellable, $"{id}: sellable");
                Assert.AreEqual(token.Value<int>("purchasePriceSalvage"), def.PurchasePriceSalvage,
                    $"{id}: purchasePriceSalvage — every price rule below is derived from this number");
            }
        }

        [Test]
        public void TheRecipeTableInCodeMatchesTheContentFile()
        {
            var json = Content("recipes.json");
            CollectionAssert.AreEquivalent(json.Select(t => t.Value<string>("id")).ToList(),
                CraftingService.Recipes.Select(r => r.Id.Value).ToList(),
                "recipes.json and CraftingService.Recipes are two sources of truth for one table");

            foreach (var token in json)
            {
                string id = token.Value<string>("id");
                var r = CraftingService.GetRecipe(new ContentId(id));
                var output = (JObject)token["output"];
                Assert.AreEqual(output.Value<string>("itemId"), r.OutputItemId, $"{id}: output item");
                Assert.AreEqual(output.Value<int>("quantity"), r.OutputQuantity, $"{id}: output quantity");
                Assert.AreEqual(token.Value<int?>("salvageFee") ?? 0, r.SalvageFee, $"{id}: salvage fee");

                var inputs = (JArray)token["inputs"];
                Assert.AreEqual(inputs.Count, r.Inputs.Count, $"{id}: input count");
                foreach (var input in inputs)
                {
                    string itemId = input.Value<string>("itemId");
                    Assert.IsTrue(r.Inputs.Any(x => x.itemId == itemId), $"{id}: {itemId} is not an input in code");
                    Assert.AreEqual(input.Value<int>("quantity"), r.Inputs.First(x => x.itemId == itemId).quantity,
                        $"{id}: {itemId} quantity");
                }
            }
        }

        [Test]
        public void TheSweepsBelowHaveSomethingToSweep()
        {
            // A foreach over an empty table passes without testing anything, which is how an
            // "automated arbitrage test" can exist and still prove nothing.
            Assert.AreEqual(19, ItemCatalog.All.Count, "release-scope item count (items.json)");
            Assert.AreEqual(4, CraftingService.Recipes.Length, "release-scope recipe count (recipes.json)");
            Assert.AreEqual(4, Content("recipes.json").Count);
        }

        // ============================================ §10.4: crafted goods must not be profitable

        [Test]
        public void CraftedOutputsSellForLessThanTheirInputPurchaseValue_Spec10_4()
        {
            foreach (var r in CraftingService.Recipes)
                Assert.Less(OutputSellValue(r), InputPurchaseValue(r),
                    $"{r.Id.Value}: §10.4 asks for less than the total INPUT PURCHASE value " +
                    $"({InputPurchaseValue(r)}), before the {r.SalvageFee} Salvage fee is even counted");
        }

        [Test]
        public void CraftedOutputsAlsoStayUnderInputsPlusTheFee_QA17()
        {
            foreach (var r in CraftingService.Recipes)
            {
                Assert.IsFalse(CraftingService.CraftResaleCreatesCurrency(r), $"{r.Id.Value} must never create currency");
                Assert.Less(OutputSellValue(r), InputPurchaseValue(r) + r.SalvageFee, $"{r.Id.Value}: QA case 17");
            }
        }

        [Test]
        public void TheArbitragePredicateFiresOnARecipeThatWouldProfit()
        {
            // Proves the sweep above is not vacuous: a recipe whose output outsells its inputs must be
            // detected, or "no recipe creates currency" is an unfalsifiable claim.
            var profitable = new CraftingService.Recipe
            {
                Id = new ContentId("recipe.test_arbitrage"),
                OutputItemId = "camp_kit",      // sells for 40
                OutputQuantity = 1,
                SalvageFee = 0
            }.With("polymer", 1);               // costs 12 to buy
            Assert.AreEqual(40, OutputSellValue(profitable), "test premise: camp_kit sells for 40");
            Assert.AreEqual(12, InputPurchaseValue(profitable), "test premise: one polymer costs 12");
            Assert.IsTrue(CraftingService.CraftResaleCreatesCurrency(profitable),
                "40 > 12, so the predicate must say so");

            var honest = new CraftingService.Recipe
            {
                Id = new ContentId("recipe.test_honest"),
                OutputItemId = "tire_patch",    // sells for 8
                OutputQuantity = 1,
                SalvageFee = 10
            }.With("polymer", 1);               // costs 12
            Assert.IsFalse(CraftingService.CraftResaleCreatesCurrency(honest), "8 < 12 + 10");
        }

        // ============================================ §10.4: 40% rounded down, everywhere

        [Test]
        public void EveryItemSellsAtFortyPercentFloored_AndNeverForWhatItCost()
        {
            foreach (var def in ItemCatalog.All.Values)
            {
                int sell = ItemCatalog.SellPrice(def.Id.Value);
                Assert.AreEqual((int)(def.PurchasePriceSalvage * 0.40f), sell,
                    $"{def.Id.Value}: 40% of purchase price, rounded down");
                if (def.PurchasePriceSalvage > 0)
                    Assert.Less(sell, def.PurchasePriceSalvage,
                        $"{def.Id.Value}: reselling must destroy value, never preserve it");
                else
                    Assert.AreEqual(0, sell, $"{def.Id.Value}: worth nothing to buy, worth nothing to sell");
            }
        }

        [Test]
        public void ResellingAnyQuantityOfAnyItemDestroysValue()
        {
            foreach (var def in ItemCatalog.All.Values.Where(d => d.Sellable && d.PurchasePriceSalvage > 0))
                for (int qty = 1; qty <= 10; qty++)
                    Assert.Less(ItemCatalog.SellPrice(def.Id.Value) * qty, def.PurchasePriceSalvage * qty,
                        $"{def.Id.Value} × {qty}: buying and reselling must never break even");
        }

        [Test]
        public void SellingPaysThePerUnitFloor_NotTheFloorOfTheTotal()
        {
            var s = AtTheGarage();
            // polymer buys for 12, so one unit sells for floor(4.8) = 4. Three units must pay 12 — not
            // floor(36 × 0.4) = 14. Rounding per unit is the conservative direction (§10.4 "rounded
            // down"), and rounding the total instead would leak 2 Salvage per stack of three.
            Ok(s.Inventory.Add(CampaignSession.GarageStorage, new ItemStack(new ContentId("polymer"), 3)),
                "seed three polymer");
            Assert.AreEqual(4, ItemCatalog.SellPrice("polymer"), "floor(12 × 0.4) per unit");

            Assert.IsTrue(s.CheckSell("polymer", 3, false, out int price).Success, "check the sale");
            Assert.AreEqual(12, price, "3 × 4, not floor(3 × 12 × 0.4) = 14");

            int before = s.Economy.BankedSalvage;
            Ok(s.SellItems("polymer", 3, false), "sell three polymer");
            Assert.AreEqual(before + 12, s.Economy.BankedSalvage,
                "and the wallet receives exactly the per-unit floor");
            Assert.AreEqual(0, s.Inventory.QuantityOf(CampaignSession.GarageStorage, "polymer"), "the stock is gone");
        }

        [Test]
        public void NonSellableItemsAreRefusedByCheckSell()
        {
            var s = AtTheGarage();
            int refused = 0;
            foreach (var def in ItemCatalog.All.Values.Where(d => !d.Sellable))
            {
                var check = s.CheckSell(def.Id.Value, 1, false, out int price);
                Assert.IsFalse(check.Success, $"{def.Id.Value} is marked unsellable in content");
                Assert.AreEqual(0, price, $"{def.Id.Value}: nothing is offered for it");
                Assert.AreEqual(FailureReason.ItemNotUsableHere, check.Reason,
                    $"{def.Id.Value}: refused as a category, not because the stock is missing");
                // The reason alone cannot distinguish this refusal from the "worth nothing to sell"
                // guard below it — every unsellable object also has a zero purchase price, so both
                // paths return ItemNotUsableHere. Only the detail says *why* the player is told no.
                StringAssert.Contains("cannot be sold", check.Detail,
                    $"{def.Id.Value}: refused because it is a mission or protected object, not because it is worthless");
                refused++;
            }
            Assert.AreEqual(9, refused, "items.json marks nine objects unsellable (quest, protected, survey, supply)");

            // Salvage itself IS marked sellable in content, but it banks 1:1 on extraction, so selling
            // it would be a second way to turn one bundle into two.
            var currency = s.CheckSell(ItemCatalog.SalvageBundle, 1, false, out int bundlePrice);
            Assert.IsFalse(currency.Success, "Salvage bundles bank automatically; selling them must be refused");
            Assert.AreEqual(FailureReason.ItemNotUsableHere, currency.Reason);
            Assert.AreEqual(0, bundlePrice);
            // A bundle costs 1, so floor(1 × 0.4) = 0 and the zero-price guard would refuse it too —
            // with the wrong explanation. The category refusal is what makes the reason correct.
            StringAssert.Contains("bank automatically", currency.Detail,
                "refused because Salvage banks 1:1, not because a bundle happens to be worth nothing");
        }

        [Test]
        public void TheSellableFlagAloneRefusesASale_WhateverTheCategory()
        {
            // Every unsellable object in today's content is ALSO ProtectedLedger or MissionObject, so
            // the two category refusals alone reject all nine and `!def.Sellable` looks redundant —
            // mutant M11 removes it and the whole suite still passes. It is not redundant: content may
            // mark ANY item unsellable, and a plain Material is the case no current item exercises.
            var s = AtTheGarage();
            Ok(s.Inventory.Add(CampaignSession.GarageStorage, new ItemStack(new ContentId("scrap_metal"), 2)),
                "seed a material that content marks sellable");
            Assert.AreEqual(ItemCategory.Material, ItemCatalog.CategoryOf("scrap_metal"),
                "test premise: not a quest, protected or currency object");
            Assert.IsTrue(s.CheckSell("scrap_metal", 2, false, out int price).Success, "and it sells today");
            Assert.AreEqual(8, price, "2 × floor(10 × 0.4)");

            var def = ItemCatalog.Get("scrap_metal");
            bool original = def.Sellable;
            try
            {
                def.Sellable = false;                    // what a content author can do to any item
                var refused = s.CheckSell("scrap_metal", 2, false, out int offered);
                Assert.IsFalse(refused.Success, "the sellable flag alone must refuse the sale");
                Assert.AreEqual(FailureReason.ItemNotUsableHere, refused.Reason,
                    "refused as a rule about the item, not as missing stock");
                Assert.AreEqual(0, offered);
                StringAssert.Contains("cannot be sold", refused.Detail);
            }
            finally
            {
                def.Sellable = original;                 // ItemCatalog is a shared static table
            }

            Assert.IsTrue(ItemCatalog.Get("scrap_metal").Sellable, "the catalog is restored");
            Ok(s.CheckSell("scrap_metal", 2, false, out _), "and the material sells again");
        }

        // ============================================ the executed loop, per recipe

        [Test]
        public void BuyCraftSell_NeverCreatesCurrency_EndToEnd()
        {
            foreach (var r in CraftingService.Recipes)
            {
                var s = AtTheGarage();
                int buy = InputPurchaseValue(r);
                int sell = OutputSellValue(r);
                int start = buy + r.SalvageFee;                    // everything the loop costs to attempt

                s.Economy.Grant(start);
                Ok(s.Economy.TrySpend(buy, new TransactionId("buy-inputs")),
                    $"{r.Id.Value}: buy the inputs at purchase price");
                Assert.AreEqual(r.SalvageFee, s.Economy.BankedSalvage, $"{r.Id.Value}: only the fee is left");

                foreach (var (itemId, qty) in r.Inputs)
                    Ok(s.Inventory.Add(CampaignSession.GarageStorage, new ItemStack(new ContentId(itemId), qty)),
                        $"{r.Id.Value}: the purchase delivers {qty} × {itemId}");

                Ok(s.Craft(r.Id.Value), $"{r.Id.Value}: craft");
                Assert.AreEqual(0, s.Economy.BankedSalvage,
                    $"{r.Id.Value}: the salvage fee is charged exactly once (KI-022)");
                Assert.AreEqual(r.OutputQuantity,
                    s.Inventory.QuantityOf(CampaignSession.GarageStorage, r.OutputItemId),
                    $"{r.Id.Value}: the output landed in garage storage");
                foreach (var (itemId, qty) in r.Inputs)
                    Assert.AreEqual(0, s.Inventory.QuantityOf(CampaignSession.GarageStorage, itemId),
                        $"{r.Id.Value}: crafting consumed the {qty} × {itemId} it was given — nothing is duplicated");

                Ok(s.SellItems(r.OutputItemId, r.OutputQuantity, false), $"{r.Id.Value}: sell the output");
                Assert.AreEqual(sell, s.Economy.BankedSalvage,
                    $"{r.Id.Value}: the sale pays exactly the 40% floor, nothing more");
                Assert.Less(s.Economy.BankedSalvage, start,
                    $"{r.Id.Value}: §10.4 — the loop ends with less currency than it started with, " +
                    $"so crafting and reselling cannot create any");
            }
        }
    }
}
