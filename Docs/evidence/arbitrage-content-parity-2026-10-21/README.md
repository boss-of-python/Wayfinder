# Batch M evidence — §10.4 arbitrage rule, and the content tables the runtime never loads

Date: 2026-10-21. Branch `arena/0754e0e3-wayfinder`. Decision: **D-043**. Issue: **KI-046** (opened
here).

§10.4:698 — "Materials can be sold at 40% of purchase price, rounded down. Crafted consumables sell for
less than their total input purchase value. **Add an automated arbitrage test; crafting/reselling must
never create currency.**"

## Result

| Check | Result |
| --- | --- |
| `headless_cs.py compile` | 7 assemblies / 60 files, **0 errors, 0 warnings** (`compile-report.json`) |
| `headless_cs.py test` | **266 / 266 passed**, 0 failed, 0 skipped (254 → 266) |
| `headless_cs.py profile` | .NET Standard 2.1 API profile **OK** |
| `headless_cs.py syntax` | 30 Unity-dependent files parse, **0 syntax errors** |
| `validate_content.py` | 0 errors, 0 warnings — **PASSED** |
| `formula_checks.py` | 0 failures — **PASSED** |
| `Tools/Balance/economy_sim.py` | deadlock scan OK, **arbitrage scan OK** — completed with no deadlocks |
| Mutation check | **12 / 12 caught**, survivors: none, tree clean, exit 0 |
| Validator negative control | strengthened rule **fires** on a recipe the old check passed |

New fixture: `Assets/Wayfinder/Tests/EditMode/EconomyArbitrageTests.cs` (12 tests).

## The two findings

**1. All three existing arbitrage checks were weaker than the spec sentence.** `CraftingService.Craft-
ResaleCreatesCurrency` (QA case 17), `validate_content.py`'s `check_items_recipes` and
`economy_sim.py`'s `arbitrage_scan` each added `salvageFee` to the input side before comparing. §10.4
counts *materials only*; the fee is QA-17's separate concern. A recipe selling above its materials but
below materials + fee would have passed all three while breaking the rule as written. Today's four
recipes clear both bars (repair_kit 18 < 32, tire_patch 8 < 12, recovery_boards 14 < 34, camp_kit
40 < 89), so this was a latent hole rather than a live defect. All three checkers now assert both rules
separately.

**2. The checks read files the runtime never loads.** `ItemCatalog` (19 items,
`Progression/InventoryService.cs:40`) and `CraftingService.Recipes` (4 recipes,
`Progression/EconomyService.cs:582`) are hardcoded C# tables. `items.json` / `recipes.json` are read by
`validate_content.py`, by `economy_sim.py`, and by the editor's `ContentImporter` — which only collects
ids. `CampaignContent.RequiredFiles` lists **neither**. So every price, mass, stack-size,
protected-ledger and arbitrage rule this project validated applied to content the game ignores, and the
two representations agreed by luck. That is DEF-016's shape; the validator's own header warns that such
a drift "went unnoticed because nothing checked the path". Now pinned by assertion (tests 1–2). The
duplication itself is **KI-046** — the real fix (load both through `CampaignContent`, delete the
hardcoded tables) needs a Unity-side load path that cannot be verified from this environment.

## What the 12 tests execute

| # | Test | Rule |
| --- | --- | --- |
| 1 | `TheItemTableInCodeMatchesTheContentFile` | every field of all 19 items: id set, `nameKey`, `maxStack`, `unitMassKg`, `protectedLedger`, `sellable`, `purchasePriceSalvage` |
| 2 | `TheRecipeTableInCodeMatchesTheContentFile` | every field of all 4 recipes: id set, output item and quantity, `salvageFee`, each input quantity |
| 3 | `TheSweepsBelowHaveSomethingToSweep` | table sizes pinned at 19 and 4, so a `foreach` cannot pass vacuously |
| 4 | `CraftedOutputsSellForLessThanTheirInputPurchaseValue_Spec10_4` | §10.4's sentence, **materials only** |
| 5 | `CraftedOutputsAlsoStayUnderInputsPlusTheFee_QA17` | QA case 17, materials + fee, through the shipped predicate |
| 6 | `TheArbitragePredicateFiresOnARecipeThatWouldProfit` | one polymer in, a camp kit out: sell 40 > materials 12 must be reported |
| 7 | `EveryItemSellsAtFortyPercentFloored_AndNeverForWhatItCost` | `floor(price × 0.40)` for all 19, and sell < buy wherever buy > 0 |
| 8 | `ResellingAnyQuantityOfAnyItemDestroysValue` | sell < buy at every quantity 1–10 for every sellable priced item |
| 9 | `SellingPaysThePerUnitFloor_NotTheFloorOfTheTotal` | three polymer pay **12** (3 × floor 4.8), not 14 (floor of 14.4) |
| 10 | `NonSellableItemsAreRefusedByCheckSell` | the nine unsellable objects **and** Salvage itself, by category, with the right detail |
| 11 | `TheSellableFlagAloneRefusesASale_WhateverTheCategory` | the `sellable` flag on a plain Material, with stock present, catalog restored in `finally` |
| 12 | `BuyCraftSell_NeverCreatesCurrency_EndToEnd` | per recipe: grant materials + fee → spend the purchase value → craft → sell; fee charged exactly once (KI-022), inputs consumed not duplicated, wallet ends **strictly below** where it started |

## Mutation check

`mutation_check.py` breaks one rule at a time in production source, recompiles, runs the whole EditMode
suite, and requires at least one failing test (or a compile failure). It refuses to start on a dirty
`Assets/` tree, restores every file from memory after each mutant, and verifies the tree is clean at the
end.

| Mutant | Rule broken | Tests | Caught by |
| --- | --- | --- | --- |
| M1 | the sell price is 45% instead of 40% | 260/266 | `Selling_AtTheGarage_FortyPercentRoundedDown_NoArbitrage_S10_4`, `Selling_IsACriticalSave_RolledBackOnWriteFailure_AndKeepsMissionCampKits_D020`, `EveryItemSellsAtFortyPercentFloored_AndNeverForWhatItCost` |
| M2 | the sell price is rounded to nearest instead of down | 262/266 | `Selling_AtTheGarage_…_S10_4`, `EveryItemSellsAtFortyPercentFloored…`, `SellingPaysThePerUnitFloor_NotTheFloorOfTheTotal` |
| M3 | the code's polymer price drifts from `items.json` | 262/266 | `SellingPaysThePerUnitFloor…`, `TheArbitragePredicateFiresOnARecipeThatWouldProfit`, `TheItemTableInCodeMatchesTheContentFile` |
| M4 | the code marks `camp_kit` unsellable while `items.json` says sellable | 262/266 | `Selling_IsACriticalSave…_D020`, `BuyCraftSell_NeverCreatesCurrency_EndToEnd`, `NonSellableItemsAreRefusedByCheckSell` |
| M5 | the code's camp-kit crafting fee drifts from `recipes.json` | 263/266 | `VerticalSlice_PlaythroughQ01toQ04_AffordsTiers1to6AndSecondCamp_LedgerBalances`, `TheRecipeTableInCodeMatchesTheContentFile`, `Crafting_ChargesRecipeSalvageFee_KI022` |
| M6 | the code's tire-patch inputs drift from `recipes.json` | 265/266 | `TheRecipeTableInCodeMatchesTheContentFile` |
| M7 | the arbitrage predicate never reports a profitable recipe | 265/266 | `TheArbitragePredicateFiresOnARecipeThatWouldProfit` |
| M8 | crafting does not charge the salvage fee (KI-022 regression) | 263/266 | `VerticalSlice_Playthrough…LedgerBalances`, `BuyCraftSell_NeverCreatesCurrency_EndToEnd`, `Crafting_ChargesRecipeSalvageFee_KI022` |
| M9 | crafting produces the output without consuming the inputs | 264/266 | `BuyCraftSell_NeverCreatesCurrency_EndToEnd`, `Crafting_ChargesRecipeSalvageFee_KI022` |
| M10 | the sale floors the total instead of each unit | 265/266 | `SellingPaysThePerUnitFloor_NotTheFloorOfTheTotal` |
| M11 | items marked unsellable in content can be sold | 265/266 | `TheSellableFlagAloneRefusesASale_WhateverTheCategory` |
| M12 | Salvage bundles can be sold on top of banking 1:1 | 265/266 | `NonSellableItemsAreRefusedByCheckSell` |

### Three runs, two survivors, two different causes

The first run reported **10/12**; the second **11/12**; the third **12/12**. Both survivors were real
findings, and they were *not* the same kind of finding:

- **M12 survived because the test asserted a reason two paths share.** Removing the Currency refusal
  still refused `salvage_bundle` — a bundle costs 1, so `floor(1 × 0.4) = 0` and the later `unit <= 0`
  guard rejects it with the same `FailureReason.ItemNotUsableHere`. Only the player-facing detail
  differed ("worth nothing to sell" instead of "Salvage bundles bank automatically at 1:1"). Asserting
  the detail caught it. This is the lesson Batch L's L4 taught, now recorded in D-043 as a convention:
  **when two refusal paths share a `FailureReason`, the test must assert the detail.**
- **M11 survived because no content exercises the clause.** All nine unsellable objects are *also*
  `protectedLedger` or quest/delivery objects, so the two category refusals reject them with the
  identical reason *and* identical message — no assertion over current content can see the difference.
  The fix was not a sharper assertion but a new test (11) that flips `scrap_metal.Sellable` off inside
  a `try`/`finally`, with stock present so the failure cannot be mistaken for missing materials.

A survivor can mean "the test is weak" or "the content never reaches the code". Guessing wrong leaves a
guard that looks tested and is not.

### Validator negative control

`validator-negative-control.log` proves the strengthened Python rule fires, and that it is *strictly
stronger* than what shipped before. It injects `recipe.tire_patch` with output quantity 2 — sell 16,
materials 12, materials + fee 22 — which the old fee-inclusive check **passed** and the new §10.4 check
reports as the only error. A second injection (quantity 4, sell 32) breaches both and reports two
errors. `recipes.json` is restored from memory afterwards and `git status` confirms the content is
byte-identical.

### Deliberately not mutation-covered

- `CheckSell`'s `ProtectedLedger || MissionObject` clause — every such item is also `Sellable = false`,
  so removing the clause leaves the same reason *and* the same detail. Defence in depth.
- `CheckSell`'s `unit <= 0` refusal — no sellable item has a zero sell price (the only zero-price
  sellable item, `salvage_bundle`, is refused earlier as Currency).
- `CraftResaleCreatesCurrency`'s `+ recipe.SalvageFee` — removing it makes the predicate *stricter*, so
  nothing fails. §10.4's own rule is asserted separately, without the fee.

## Files

| File | What it is |
| --- | --- |
| `headless-console.log` | full `headless_cs.py all` console output for this batch |
| `EditMode.headless.xml` | NUnit3 result XML: 266 total / 266 passed / 0 failed |
| `compile-report.json` | per-assembly compile report, 0 warnings |
| `python-checks.log` | `validate_content.py` + `formula_checks.py` + `economy_sim.py` output |
| `validator-negative-control.log` | the strengthened rule firing on content the old rule passed |
| `mutation_check.py` | the 12-mutant harness (M1–M12) |
| `mutation-check.log` | its third run: 12/12 caught, survivors none, tree clean, `EXIT 0` |

## Reproduce

```bash
python3 Tools/Build/headless_cs.py all                                     # ~60 s
python3 Tools/Validation/validate_content.py && python3 Tools/Balance/economy_sim.py
python3 Docs/evidence/arbitrage-content-parity-2026-10-21/mutation_check.py  # ~8 min, needs a clean Assets/ tree
```

## What this evidence does not prove

Nothing here is a Unity result. The domain, progression and persistence code was compiled and executed
outside Unity (D-014); no Unity-side behaviour, PlayMode test, physics, frame-time or thermal evidence
exists (KI-003, KI-006, KI-009, KI-026). The M2 gate still needs the 30-minute on-device playtest.

The parity tests prove the two tables agree **today**; they do not remove the duplication (KI-046).
`economy_sim.py` remains a model (KI-010) and cannot certify fun or playtime. And the arbitrage rule is
about *purchase-value accounting*: collecting materials in the field and selling what they craft into
is the intended economy, not arbitrage, and is deliberately not restricted by any of this.
