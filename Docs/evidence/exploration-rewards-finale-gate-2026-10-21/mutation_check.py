#!/usr/bin/env python3
"""Mutation check for Batch P (MAP-03 / §8.4: exploration rewards and the finale's map gate).

Each mutant breaks ONE rule in the production source, recompiles with
`Tools/Build/headless_cs.py compile`, runs the whole EditMode suite, and must be caught by at least
one failing test (or fail to compile). Sources are restored from memory after every mutant; the script
refuses to start on a dirty Assets/ tree and verifies it left the tree clean.

Two files carry these rules:
  * Progression/DiscoveryService.cs  — §8.4's constants, PayMappingThresholds, the landmark reward
  * Campaign/CampaignSnapshot.cs     — persistence of the paid-threshold index (capture and restore)

Not mutation-covered, deliberately — both are PROVABLY equivalent mutants, not oversights:
  * `MapCoveragePercent` dividing by the whole grid instead of `CountableMask.CountSet()`. The mask
    counts all 9216 cells because `BakeCountableMask` keeps every cell above sea level and on the
    canonical seed none falls below it, so the two denominators are identical. That the distinction is
    unobservable is itself the KI-048 finding, pinned by
    `TheCountableMaskCountsEveryCell_BecauseNoCanonicalCellSitsAtOrBelowSeaLevel`.
  * `MeetsFinaleMapRequirement` using `>` instead of `>=`. 60% of 9216 is 5529.6 cells, so coverage can
    never equal exactly 60.0% and the two comparisons agree on every reachable state. The crossing
    point (5530 cells) is asserted absolutely instead.

Usage:  python3 Docs/evidence/exploration-rewards-finale-gate-2026-10-21/mutation_check.py
Exit:   0 when every mutant is caught, 1 otherwise.
"""
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
DISC = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs")
SNAP = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Campaign/CampaignSnapshot.cs")
RESULT_XML = os.path.join(ROOT, "Logs/headless/EditMode.headless.xml")

MUTANTS = [
    # ------------------------------------------------- §8.4's literal numbers
    ("M01", "a mapping threshold is 40 cells instead of 50 (§8.4)", DISC,
     "        public const int MappingRewardThresholdCells = 50;",
     "        public const int MappingRewardThresholdCells = 40;"),
    ("M02", "a mapping threshold pays 45 Salvage instead of 50 (§8.4)", DISC,
     "        public const int MappingRewardSalvage = 50;",
     "        public const int MappingRewardSalvage = 45;"),
    ("M03", "a landmark pays 110 Salvage instead of 120 (§8.4)", DISC,
     "        public const int LandmarkDiscoveryRewardSalvage = 120;",
     "        public const int LandmarkDiscoveryRewardSalvage = 110;"),
    ("M04", "the finale gate drops to 50% coverage (§8.4)", DISC,
     "        public const float FinaleMapCoveragePercent = 60f;",
     "        public const float FinaleMapCoveragePercent = 50f;"),

    # --------------------------------- §8.4 "every 50 newly revealed cells grants 50 Salvage once"
    ("M05", "only one mapping threshold is paid per reveal instead of all of them", DISC,
     "            while (revealed >= MappingRewardsPaidThroughCount + MappingRewardThresholdCells)",
     "            if (revealed >= MappingRewardsPaidThroughCount + MappingRewardThresholdCells)"),
    ("M06", "every mapping threshold shares one reward key, so only the first pays", DISC,
     '                string key = $"reward.map.{MappingRewardsPaidThroughCount}";',
     '                string key = "reward.map.50";'),
    ("M07", "the paid-threshold index advances by 1 cell, paying almost every reveal", DISC,
     "                MappingRewardsPaidThroughCount += MappingRewardThresholdCells;",
     "                MappingRewardsPaidThroughCount += 1;"),
    ("M08", "a mapping reward is escrowed even when its key was already claimed", DISC,
     """                if (_rewardLedger.TryClaim(key))
                    _economy?.Escrow(MappingRewardSalvage); // escrow until banking (§8.4)""",
     """                _rewardLedger.TryClaim(key);
                _economy?.Escrow(MappingRewardSalvage); // escrow until banking (§8.4)"""),
    ("M09", "revealing cells pays no mapping reward at all", DISC,
     "            PayMappingThresholds();\n            return newly;",
     "            return newly;"),

    # ------------------------------------- §8.4 "every new landmark pays 120 Salvage once"
    ("M10", "every landmark shares one reward key, so only the first pays", DISC,
     '            string rewardKey = $"reward.landmark.{landmarkId}";',
     '            string rewardKey = "reward.landmark";'),
    ("M11", "a landmark reward is escrowed even when its key was already claimed", DISC,
     """            if (_rewardLedger.TryClaim(rewardKey))
                _economy?.Escrow(LandmarkDiscoveryRewardSalvage);""",
     """            _rewardLedger.TryClaim(rewardKey);
            _economy?.Escrow(LandmarkDiscoveryRewardSalvage);"""),

    # ------------------- §8.4 "maintain a persistent count and paid-threshold index"
    ("M12", "the paid-threshold index is saved as 0, so a reload can re-earn every threshold", SNAP,
     "                mappingRewardsPaidThroughCount = s.Discovery.MappingRewardsPaidThroughCount,",
     "                mappingRewardsPaidThroughCount = 0,"),
    ("M13", "the paid-threshold index is restored as 0", SNAP,
     "                world.discoveredLandmarkIds, world.collectedCacheIds, relics, world.mappingRewardsPaidThroughCount);",
     "                world.discoveredLandmarkIds, world.collectedCacheIds, relics, 0);"),
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
            print(f"PATTERN-MISSING {mid} {what}", flush=True)
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
                print(f"SURVIVED  {mid:4} {what}  (tests {total})", flush=True)
            else:
                caught.append(mid)
                reason = "does not compile" if not compile_ok else ", ".join(failed[:3])
                print(f"CAUGHT    {mid:4} {what}  (tests {total}) by {reason}", flush=True)
        finally:
            open(path, "w", encoding="utf-8").write(original)

    left = dirty()
    print(f"\n{len(caught)}/{len(MUTANTS)} mutants caught; survivors: {survived or 'none'}")
    print("tree clean after run" if not left else "TREE LEFT DIRTY:\n" + left)
    return 0 if not survived and not left else 1


if __name__ == "__main__":
    sys.exit(main())
