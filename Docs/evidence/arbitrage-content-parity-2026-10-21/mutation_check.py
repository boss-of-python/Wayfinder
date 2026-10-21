#!/usr/bin/env python3
"""Mutation check for Batch M (§10.4 economy invariants + content-table parity, D-043).

Each mutant breaks ONE rule in the production source, recompiles with
`Tools/Build/headless_cs.py compile`, runs the whole EditMode suite, and must be caught by at least
one failing test (or fail to compile). Sources are restored from memory after every mutant; the script
refuses to start on a dirty Assets/ tree and verifies it left the tree clean.

Three files carry these rules:
  * Progression/InventoryService.cs  — ItemCatalog (the hardcoded item table) and SellPrice (40% floor)
  * Progression/EconomyService.cs    — CraftingService.Recipes, CraftResaleCreatesCurrency, TryCraft
  * Campaign/CampaignSession.Supplies.cs — CheckSell (category refusals and per-unit pricing)

Not mutation-covered, deliberately:
  * CheckSell's ProtectedLedger/MissionObject refusal — every such item is also `Sellable = false`, so
    removing the category clause changes no observable outcome (the `!def.Sellable` clause still
    refuses all nine). It stays as defence in depth.
  * CheckSell's `unit <= 0` refusal — no sellable item has a zero sell price (the only zero-price
    sellable item, salvage_bundle, is refused earlier as Currency), so no test can observe it.
  * CraftResaleCreatesCurrency's `+ recipe.SalvageFee` — removing it makes the predicate STRICTER, so
    nothing fails; §10.4's own sentence is asserted separately, without the fee.

Usage:  python3 Docs/evidence/arbitrage-content-parity-2026-10-21/mutation_check.py
Exit:   0 when every mutant is caught, 1 otherwise.
"""
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
INV = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Progression/InventoryService.cs")
ECO = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Progression/EconomyService.cs")
SUP = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Campaign/CampaignSession.Supplies.cs")
RESULT_XML = os.path.join(ROOT, "Logs/headless/EditMode.headless.xml")

MUTANTS = [
    # ------------------------------------------------------- §10.4 "40% of purchase price"
    ("M1", "the sell price is 45% instead of 40%", INV,
     "            return (int)(def.PurchasePriceSalvage * 0.40f);",
     "            return (int)(def.PurchasePriceSalvage * 0.45f);"),
    ("M2", "the sell price is rounded to nearest instead of down", INV,
     "            return (int)(def.PurchasePriceSalvage * 0.40f);",
     "            return (int)(def.PurchasePriceSalvage * 0.40f + 0.5f);"),

    # ------------------------------------------------------- content-table parity
    ("M3", "the code's polymer price drifts from items.json", INV,
     '            ["polymer"] = Def("polymer", "item.polymer.name", 10, 0.5f, false, true, 12),',
     '            ["polymer"] = Def("polymer", "item.polymer.name", 10, 0.5f, false, true, 13),'),
    ("M4", "the code marks camp_kit unsellable while items.json says sellable", INV,
     '            ["camp_kit"] = Def("camp_kit", "item.camp_kit.name", 1, 12.0f, false, true, 100),',
     '            ["camp_kit"] = Def("camp_kit", "item.camp_kit.name", 1, 12.0f, false, false, 100),'),
    ("M5", "the code's camp-kit crafting fee drifts from recipes.json", ECO,
     '''                Id = new ContentId("recipe.camp_kit"), OutputItemId = "camp_kit", OutputQuantity = 1,
                SalvageFee = 80''',
     '''                Id = new ContentId("recipe.camp_kit"), OutputItemId = "camp_kit", OutputQuantity = 1,
                SalvageFee = 0'''),
    ("M6", "the code's tire-patch inputs drift from recipes.json", ECO,
     '''                Id = new ContentId("recipe.tire_patch"), OutputItemId = "tire_patch", OutputQuantity = 1,
                SalvageFee = 10
            }.With("polymer", 1),''',
     '''                Id = new ContentId("recipe.tire_patch"), OutputItemId = "tire_patch", OutputQuantity = 1,
                SalvageFee = 10
            }.With("polymer", 3),'''),

    # ------------------------------------------------------- the arbitrage predicate
    ("M7", "the arbitrage predicate never reports a profitable recipe", ECO,
     "            return outputSellValue > inputPurchaseValue;",
     "            return false;"),

    # ------------------------------------------------------- the craft transaction
    ("M8", "crafting does not charge the salvage fee (KI-022 regression)", ECO,
     "            var fee = economy.TrySpend(recipe.SalvageFee, default);",
     "            var fee = economy.TrySpend(0, default);"),
    ("M9", "crafting produces the output without consuming the inputs", ECO,
     "                var remove = inventory.Remove(materialSource, new ContentId(itemId), qty);",
     "                var remove = CommandResult.Ok();"),

    # ------------------------------------------------------- the sell path
    ("M10", "the sale floors the total instead of each unit", SUP,
     "            totalPrice = unit * quantity;",
     "            totalPrice = (int)(def.PurchasePriceSalvage * quantity * 0.40f);"),
    ("M11", "items marked unsellable in content can be sold", SUP,
     "            if (category == ItemCategory.ProtectedLedger || category == ItemCategory.MissionObject || !def.Sellable)",
     "            if (category == ItemCategory.ProtectedLedger || category == ItemCategory.MissionObject)"),
    ("M12", "Salvage bundles can be sold on top of banking 1:1", SUP,
     '''            if (category == ItemCategory.Currency)
                return CommandResult.Fail(FailureReason.ItemNotUsableHere, "Salvage bundles bank automatically at 1:1");
''', ""),
]


def run(cmd):
    return subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True)


def dirty():
    return run(["git", "status", "--porcelain", "Assets"]).stdout.strip()


def main():
    if dirty():
        print("refusing to run: Assets/ is dirty\n" + dirty())
        return 2
    survived, caught = [], []
    for mid, what, path, old, new in MUTANTS:
        original = open(path, encoding="utf-8").read()
        if old not in original:
            print(f"PATTERN-MISSING {mid} {what}")
            survived.append(mid)
            continue
        try:
            open(path, "w", encoding="utf-8").write(original.replace(old, new, 1))
            compiled = run(["python3", "Tools/Build/headless_cs.py", "compile"])
            compile_ok = compiled.returncode == 0 and ": FAILED" not in compiled.stdout
            failed, total = [], None
            if compile_ok:
                run(["python3", "Tools/Build/headless_cs.py", "test"])
                root = ET.parse(RESULT_XML).getroot()
                total = f"{root.get('passed')}/{root.get('total')}"
                failed = [tc.get("name") for tc in root.iter("test-case") if tc.get("result") != "Passed"]
            if compile_ok and not failed:
                survived.append(mid)
                print(f"SURVIVED  {mid:4} {what}  (tests {total})")
            else:
                caught.append(mid)
                reason = "does not compile" if not compile_ok else ", ".join(failed[:3])
                print(f"CAUGHT    {mid:4} {what}  (tests {total}) by {reason}")
        finally:
            open(path, "w", encoding="utf-8").write(original)

    left = dirty()
    print(f"\n{len(caught)}/{len(MUTANTS)} mutants caught; survivors: {survived or 'none'}")
    print("tree clean after run" if not left else "TREE LEFT DIRTY:\n" + left)
    return 0 if not survived and not left else 1


if __name__ == "__main__":
    sys.exit(main())
