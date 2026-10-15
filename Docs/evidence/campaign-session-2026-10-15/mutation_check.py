#!/usr/bin/env python3
"""Re-introduce each fixed defect / remove each safeguard, one at a time, and confirm the
headless EditMode suite catches it. Restores every file afterwards. Run from the repo root."""
import re, subprocess, sys, xml.etree.ElementTree as ET

S = "Assets/Wayfinder/Scripts/"
MUTATIONS = [
    ("KI-018 restore leaves completed missions Locked (old behaviour)", S + "Progression/MissionService.cs",
     "                    _completedCodes.Add(c);\n                    _states[c] = MissionState.Completed;\n",
     "                    _completedCodes.Add(c);\n"),
    ("KI-019 Remove mutates before checking quantity (old behaviour)", S + "Progression/InventoryService.cs",
     "            if (available < quantity)\n                return CommandResult.Fail(FailureReason.ItemNotFound, $\"insufficient quantity: have {available}, need {quantity}\");\n",
     ""),
    ("KI-020 collect grants 1 unit regardless of content (old behaviour)", S + "Progression/EconomyService.cs",
     "var add = _inventory.AddAll(cmd.Destination, contents.Stacks);",
     "var add = _inventory.Add(cmd.Destination, new ItemStack(new ContentId(ItemCatalog.SalvageBundle), 1));"),
    ("KI-021 camp kit consumed without activation (old behaviour)", S + "Progression/EconomyService.cs",
     "            _deps.Garage.ActivateCamp(campId);\n", ""),
    ("KI-022 crafting fee never charged (old behaviour)", S + "Progression/EconomyService.cs",
     "var fee = economy.TrySpend(recipe.SalvageFee, default);", "var fee = CommandResult.Ok();"),
    ("Sequence counter restarts at 0 after load", S + "Campaign/CampaignSnapshot.cs",
     "s.NextSequence = Math.Max(Math.Max(0, p.nextTransactionSequence), MaxAppliedSequence(p.appliedTransactionIds) + 1);",
     "s.NextSequence = 0;"),
    ("No in-memory rollback when the critical save fails", S + "Campaign/CampaignSession.cs",
     "                if (!save.Success)\n                {\n                    CampaignSnapshot.Apply(this, before);\n",
     "                if (!save.Success)\n                {\n"),
    ("Arrival armed immediately at the departure spawn", S + "Campaign/FieldTrackers.cs",
     "            _armed = !startInsideVolume;", "            _armed = true;"),
]

def run_suite():
    c = subprocess.run([sys.executable, "Tools/Build/headless_cs.py", "compile"], capture_output=True, text=True)
    if "FAILED" in c.stdout:
        return None, "compile failed"
    subprocess.run([sys.executable, "Tools/Build/headless_cs.py", "test"], capture_output=True, text=True)
    root = ET.parse("Logs/headless/EditMode.headless.xml").getroot()
    failed = [tc.get("name") for tc in root.iter("test-case") if tc.get("result") == "Failed"]
    return failed, root.get("total")

ok = True
for label, path, old, new in MUTATIONS:
    original = open(path, encoding="utf-8").read()
    if old not in original:
        print(f"[SKIP-BROKEN] {label}: anchor not found in {path}"); ok = False; continue
    open(path, "w", encoding="utf-8").write(original.replace(old, new, 1))
    try:
        failed, total = run_suite()
    finally:
        open(path, "w", encoding="utf-8").write(original)
    if failed is None:
        print(f"[COMPILE-ERROR] {label}"); ok = False
    elif failed:
        print(f"[CAUGHT] {label}: {len(failed)}/{total} test(s) fail -> {', '.join(sorted(failed))}")
    else:
        print(f"[SURVIVED] {label}: no test failed"); ok = False
print("MUTATION CHECK:", "ALL CAUGHT" if ok else "GAPS FOUND")
sys.exit(0 if ok else 1)
