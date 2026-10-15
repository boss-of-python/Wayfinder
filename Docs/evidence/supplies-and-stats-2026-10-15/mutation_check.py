#!/usr/bin/env python3
"""Field supplies, selling, research → physics wiring (Batch D continuation): break each rule /
re-introduce each fixed defect one at a time and confirm the headless EditMode suite catches it.
Restores every file afterwards. Run from the repo root; never edit sources while it runs."""
import subprocess, sys, xml.etree.ElementTree as ET

S = "Assets/Wayfinder/Scripts/"
SUP = S + "Campaign/CampaignSession.Supplies.cs"
MUTATIONS = [
    ("Fuel-can waste used without confirmation (§10.3)", SUP,
     "if (preview.NeedsWasteConfirmation && !acceptWaste)",
     "if (preview.NeedsWasteConfirmation && !acceptWaste && preview.LitersWasted > 99f)"),
    ("Fuel can usable with < 1 L missing (§10.3)", SUP,
     "if (missing < FuelCanMinMissingLiters)", "if (missing < 0f)"),
    ("Repair kit usable while moving (§5.5)", SUP,
     "            if (speedKph > SupplyMaxSpeedKph) return CommandResult.Fail(FailureReason.VehicleMoving, \"stop the vehicle to repair\");\n",
     ""),
    ("Repair kit wasted on a subsystem at 100", SUP,
     "if (Condition(subsystem) >= 100)", "if (Condition(subsystem) > 100)"),
    ("Field supplies consume nothing", S + "Progression/EconomyService.cs",
     "? _inventory.Remove(use.Source, use.ItemId, 1)", "? CommandResult.Ok()"),
    ("Tire patch fully repairs the tires", S + "Simulation/FuelModel.cs",
     "            TiresCondition = PunctureThreshold;\n", "            TiresCondition = 100;\n"),
    ("Sell price rounded up instead of down (§10.4)", S + "Progression/InventoryService.cs",
     "return (int)(def.PurchasePriceSalvage * 0.40f);", "return (int)Math.Ceiling(def.PurchasePriceSalvage * 0.40f);"),
    ("Selling skips the critical verified save (D-020)", SUP,
     "            return RunCritical(() =>\n            {\n                var result = Transactions.Execute(new SellItems(",
     "            return new Func<Func<CommandResult>, CommandResult>(f => f())(() =>\n            {\n                var result = Transactions.Execute(new SellItems("),
    ("Mission camp kits sellable", SUP,
     "int reserved = ReservedForMissions(itemId);", "int reserved = 0;"),
    ("Resolver configuration double-applies research (QA 8)", S + "Campaign/CampaignSession.cs",
     "            resolver.Clear();\n            Research.ApplyTo(resolver);", "            Research.ApplyTo(resolver);"),
    ("KI-032 Trail Tires I never composed (old behaviour)", S + "Simulation/VehicleStatsResolver.cs",
     "grip *= Compose(Stat.DirtGravelGrip, 1f);", "grip *= 1f;"),
    ("KI-032 Engine Tune never reaches the powertrain (old behaviour)", S + "Simulation/Powertrain.cs",
     "float engineTorque = TorqueAtRpm(spec, EngineRpm) * ResearchTorqueScale(spec, stats);",
     "float engineTorque = TorqueAtRpm(spec, EngineRpm);"),
    ("Trail Tires bonus granted on specialized tire sets too", S + "Simulation/VehicleStatsResolver.cs",
     "if (tires == TireSet.AllTerrain && (surface == SurfaceType.PackedDirt", "if ((surface == SurfaceType.PackedDirt"),
]


def run_suite():
    c = subprocess.run([sys.executable, "Tools/Build/headless_cs.py", "compile"], capture_output=True, text=True)
    if "FAILED" in c.stdout or c.returncode != 0:
        return None, "compile failed"
    subprocess.run([sys.executable, "Tools/Build/headless_cs.py", "test"], capture_output=True, text=True)
    root = ET.parse("Logs/headless/EditMode.headless.xml").getroot()
    failed = [tc.get("name") for tc in root.iter("test-case") if tc.get("result") == "Failed"]
    return failed, root.get("total")


ok = True
for label, path, old, new in MUTATIONS:
    original = open(path, encoding="utf-8").read()
    if old not in original:
        print(f"[SKIP-BROKEN] {label}: anchor not found in {path}")
        ok = False
        continue
    open(path, "w", encoding="utf-8").write(original.replace(old, new, 1))
    try:
        failed, total = run_suite()
    finally:
        open(path, "w", encoding="utf-8").write(original)
    if failed is None:
        print(f"[COMPILE-ERROR] {label}")
        ok = False
    elif failed:
        print(f"[CAUGHT] {label}: {len(failed)}/{total} test(s) fail -> {', '.join(sorted(failed))}")
    else:
        print(f"[SURVIVED] {label}: no test failed")
        ok = False
print("MUTATION CHECK:", "ALL CAUGHT" if ok else "GAPS FOUND")
sys.exit(0 if ok else 1)
