#!/usr/bin/env python3
"""Mutation check for Batch K (KI-028 remainder): §10.2 equipment slots + swappable tire sets.

Each mutant breaks ONE rule of the batch in the production source, recompiles with
`Tools/Build/headless_cs.py compile`, runs the whole EditMode suite, and must be caught by at least
one failing test (or fail to compile). Sources are restored from a backup after every mutant, and the
script refuses to start on a dirty Assets/ tree and verifies it left the tree clean.

Not mutation-covered, deliberately: the "both utility slots are occupied" refusal in
`ResearchService.Fit`. Canonical content has exactly two utility tiers (2 Underbody Guard, 7 Low
Range), and fitting an already-fitted module is refused earlier as AlreadyApplied, so a third utility
module is unreachable through the public API. The guard is defensive; it is documented rather than
claimed as covered.

Usage:  python3 Docs/evidence/module-slots-2026-10-21/mutation_check.py
Exit:   0 when every mutant is caught, 1 otherwise.
"""
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
PROGRESSION = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Progression")
CAMPAIGN = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Campaign")
RESEARCH = os.path.join(PROGRESSION, "ResearchService.cs")
GARAGE = os.path.join(CAMPAIGN, "CampaignSession.Garage.cs")
SESSION = os.path.join(CAMPAIGN, "CampaignSession.cs")
SNAPSHOT = os.path.join(CAMPAIGN, "CampaignSnapshot.cs")
RESULT_XML = os.path.join(ROOT, "Logs/headless/EditMode.headless.xml")

# (id, what rule it breaks, file, exact source text, replacement)
MUTANTS = [
    # ------------------------------------------------------- composition is fit-driven (§10.2)
    ("K1", "resolver composes every researched tier again, fitted or not", RESEARCH,
     "                if (def.Slot != ModuleSlot.None && !_selected.Contains(def.Id.Value)) continue;\n", ""),
    ("K2", "buying a module no longer fits it", RESEARCH,
     "            // module unfitted; it stays researched and can be fitted later.\n            AutoFit(def);",
     "            // module unfitted; it stays researched and can be fitted later."),
    ("K3", "utility slot capacity drops to one", RESEARCH,
     "public static int SlotCapacity(ModuleSlot slot) => slot == ModuleSlot.Utility ? UtilitySlots : 1;",
     "public static int SlotCapacity(ModuleSlot slot) => 1;"),

    # ------------------------------------------------------- Fit / Unfit refusals
    ("K4", "fitting an already-fitted module is allowed", RESEARCH,
     '''            if (_selected.Contains(def.Id.Value))
                return CommandResult.Fail(FailureReason.AlreadyApplied, "that module is already fitted");
''', ""),
    ("K5", "a slot-less tire unlock can be 'fitted'", RESEARCH,
     '''            if (def.Slot == ModuleSlot.None)
                return CommandResult.Fail(FailureReason.InvalidTarget,
                    "that tier unlocks a tire set; it occupies no slot (§10.2)");
''', ""),
    ("K6", "fitting before researching is allowed", RESEARCH,
     '''            if (!_researched.Contains(def.Id.Value))
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet, "research the module first (§11.2)");
''', ""),

    # ------------------------------------------------------- tire sets (§10.2, §11.2 tiers 8/13/18)
    ("K7", "the rock set is never unlocked (the gap this batch closes)", RESEARCH,
     "                case TireSet.Rock: return ResearchedFlag(VehicleStatsResolver.Stat.RockTires);",
     "                case TireSet.Rock: return false;"),
    ("K8", "unlocks follow the fitted loadout instead of research", RESEARCH,
     '''                if (!_researched.Contains(def.Id.Value)) continue;
                foreach (var e in def.Effects)''',
     '''                if (!_selected.Contains(def.Id.Value)) continue;
                foreach (var e in def.Effects)'''),
    ("K9", "an unresearched tire set can be mounted", RESEARCH,
     '''            if (!IsTireSetUnlocked(set))
                return CommandResult.Fail(FailureReason.PrerequisiteNotMet,
                    $"the {set} tire set is not researched yet (§11.2)");
''', ""),
    ("K10", "tier 13 no longer carries the rock-tire unlock", RESEARCH,
     '''                (VehicleStatsResolver.Stat.DiffLock, VehicleStatsResolver.Operation.Set, 1f),
                // §11.2 tier 13 also unlocks the rock tire set. `Stat.RockTires` existed and nothing
                // ever set it, so the Rock set was unreachable (found while wiring §10.2 slots).
                (VehicleStatsResolver.Stat.RockTires, VehicleStatsResolver.Operation.Set, 1f));''',
     "                (VehicleStatsResolver.Stat.DiffLock, VehicleStatsResolver.Operation.Set, 1f));"),
    ("K11", "tier 20 drops Engine Tune I's torque curve (§11.2:739)", RESEARCH,
     '''                (VehicleStatsResolver.Stat.PeakTorqueNm, VehicleStatsResolver.Operation.Multiply, 1.10f),
                (VehicleStatsResolver.Stat.PoweredFuelDrainFactor, VehicleStatsResolver.Operation.Multiply, 0.90f));''',
     "                (VehicleStatsResolver.Stat.PoweredFuelDrainFactor, VehicleStatsResolver.Operation.Multiply, 0.90f));"),

    # ------------------------------------------------------- load reconciliation (D-041)
    ("K12", "a pre-slot save fits everything researched instead of reconciling", RESEARCH,
     '''            _selected.Clear();
            foreach (var def in _tiers)   // ascending tier order, so the highest tier wins each slot
            {
                if (!_researched.Contains(def.Id.Value) || !wanted.Contains(def.Id.Value)) continue;''',
     '''            _selected.Clear();
            foreach (var def in _tiers)
            {
                if (!_researched.Contains(def.Id.Value)) continue;'''),
    ("K13", "a save's mounted tire set is ignored on load", RESEARCH,
     "            SelectedTireSet = IsTireSetUnlocked(tireSet) ? tireSet : TireSet.AllTerrain;",
     "            SelectedTireSet = TireSet.AllTerrain;"),
    ("K14", "the engine slot is spelled differently from EquipmentSlots", RESEARCH,
     "            ModuleSlot.Engine => EquipmentSlots.EngineTune,",
     '            ModuleSlot.Engine => "engine",'),

    # ------------------------------------------------------- session commands (§10.4 garage service)
    ("K15", "loadout changes are allowed in the open field", GARAGE,
     '''            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState,
                    "vehicle changes are only available at a garage (§10.4)");
''', ""),
    ("K16", "the QA case 9 over-capacity guard is dropped", GARAGE,
     '''                if (Inventory.IsOverCapacity(Cargo))
                    return CommandResult.Fail(FailureReason.OverCapacity,
                        "the loaded cargo no longer fits that rack; unload first or keep the larger one (QA case 9)");
''', ""),
    ("K17", "fuel is not clamped when the tank shrinks (§5.4)", GARAGE,
     "                FuelLiters = Math.Min(FuelLiters, FuelCapacityLiters);\n", ""),
    ("K18", "the loadout swap never re-configures the containers", GARAGE,
     '''                if (!result.Success) return result;
                ConfigureContainers();
                if (Inventory.IsOverCapacity(Cargo))''',
     '''                if (!result.Success) return result;
                if (Inventory.IsOverCapacity(Cargo))'''),
    ("K19", "the fit/remove notice is dropped (§13.2)", GARAGE,
     '''                Notice(fit ? CampaignNoticeKind.ModuleFitted : CampaignNoticeKind.ModuleRemoved,
                    (fit ? "Fitted " : "Removed ") + upgradeId, 0, upgradeId);
''', ""),
    ("K20", "mounting tires is allowed in the open field", GARAGE,
     '''            if (!AtGarage)
                return CommandResult.Fail(FailureReason.WrongState, "tire sets are mounted at a garage (§10.4)");
''', ""),
    ("K21", "the mounted-tire notice is dropped", GARAGE,
     '''                Notice(CampaignNoticeKind.TireSetMounted, $"Mounted {tireSet} tires", 0, tireSet.ToString());
''', ""),

    # ------------------------------------------------------- the one shared resolution (§11.2:741)
    ("K22", "the garage resolves all-terrain regardless of the mounted set", SESSION,
     "            return resolver.Resolve(Vehicle, FuelLiters, Inventory.TotalMassKg(Cargo), Research.SelectedTireSet,",
     "            return resolver.Resolve(Vehicle, FuelLiters, Inventory.TotalMassKg(Cargo), TireSet.AllTerrain,"),
    ("K23", "the resolver's tire-unlock flags revert to hardcoded false", SESSION,
     '''                Research.IsTireSetUnlocked(TireSet.Mud), Research.IsTireSetUnlocked(TireSet.Snow),
                Research.IsTireSetUnlocked(TireSet.Rock));''',
     "                false, false, false);"),

    # ------------------------------------------------------- persistence (additive, no schema bump)
    ("K24", "the save records everything researched instead of the fitted loadout", SNAPSHOT,
     "                    equippedModuleIds = new List<string>(s.Research.FittedIds),",
     "                    equippedModuleIds = ResearchedInTierOrder(s),"),
    ("K25", "the save always records the all-terrain set", SNAPSHOT,
     "                    equippedTireSet = s.Research.SelectedTireSet.ToString()",
     '                    equippedTireSet = "AllTerrain"'),
    ("K26", "the saved loadout is ignored on restore", SNAPSHOT,
     "            var fitted = KnownTiers(s, FittedIdsOf(p, s.ActiveVehicleId));",
     "            var fitted = new List<string>();"),
    ("K27", "the saved tire set is ignored on restore", SNAPSHOT,
     "            s.Research.Restore(researched, fitted, SelectedTireSetOf(p, s.ActiveVehicleId));",
     "            s.Research.Restore(researched, fitted, TireSet.AllTerrain);"),
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
