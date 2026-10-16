#!/usr/bin/env python3
"""Scatter placement + chunk streaming (Batch G): re-introduce each defect or break each rule, one at a time,
and confirm the headless EditMode suite catches it. Restores every file afterwards (also on error).
Run from the repo root; never edit sources while it runs.

Usage: python3 Docs/evidence/world-streaming-2026-10-16/mutation_check.py
"""
import subprocess, sys, xml.etree.ElementTree as ET

W = "Assets/Wayfinder/Scripts/World/"
SCATTER = W + "ScatterPlacer.cs"
PLAN = W + "ChunkStreamPlanner.cs"

MUTATIONS = [
    ("KI-017 defect: integer division truncates the scatter cell (old behaviour)", SCATTER,
     "(float)WorldConstants.ChunkSizeMeters / CellsPerChunk(minSpacingMeters);",
     "WorldConstants.ChunkSizeMeters / CellsPerChunk(minSpacingMeters);"),
    ("Spacing rule disabled: eligible neighbours never block (§7.6)", SCATTER,
     "if (IsBlocked(self, cands, side, lx + halo, ly + halo, rules.MinSpacingMeters)) continue;",
     ""),
    ("Road corridor + 1 m shoulder ignored (§7.6)", SCATTER,
     "if (surface.DistanceToRoadEdgeMeters(c.X, c.Z) < rules.RoadClearanceMeters) return c;",
     ""),
    ("Reserved POI/camp footprints ignored (§7.6)", SCATTER,
     "if (surface.IsReserved(c.X, c.Z)) return c;", ""),
    ("Steep slopes ignored (§7.6)", SCATTER,
     "if (surface.SlopeDegrees(c.X, c.Z) > rules.MaxSlopeDegrees) return c;", ""),
    ("Candidates share one seed per chunk (no per-candidate index, §7.3)", SCATTER,
     "ulong seed = StableHash.CandidateSeed(chunkSeed, c.Index);",
     "ulong seed = chunkSeed;"),
    ("Activation hysteresis removed: retain band = enter band", PLAN,
     "(state == ChunkState.Active && d <= ActivateRetainMeters);",
     "(state == ChunkState.Active && d <= ActivateEnterMeters);"),
    ("Dormant chunks release their data (handle leak-free but re-IO on re-entry)", PLAN,
     "                    _deactivateRetainsData[i] = true;\n                    commands.Add(new StreamCommand(StreamCommandKind.Deactivate, ix, iz));\n                }\n            }\n\n            // Activate ready",
     "                    _deactivateRetainsData[i] = false;\n                    commands.Add(new StreamCommand(StreamCommandKind.Deactivate, ix, iz));\n                }\n            }\n\n            // Activate ready"),
    ("Pinned chunks ignored (recovery destination / stopping corridor, §18.3)", PLAN,
     "bool pinnedHere = pinnedSet.Contains(i);", "bool pinnedHere = false;"),
    ("Activation budget ignored (§18.3 per-frame activation budget)", PLAN,
     "int budget = Math.Max(0, ActivationsPerStep);", "int budget = int.MaxValue;"),
    ("Handle ledger ignores Deactivating (leak accounting)", PLAN,
     "s == ChunkState.Active || s == ChunkState.Deactivating;", "s == ChunkState.Active;"),
    ("Cancelled load keeps its handle (QA 12 leak)", PLAN,
     "            else if (fromHolds && !toHolds) OutstandingHandles--;", ""),
    ("Late data accepted for a cancelled chunk", PLAN,
     "if (_chunks[i].State != ChunkState.Requested) return false;\n            Transition(i, ChunkState.DataReady);",
     "if (_chunks[i].State == ChunkState.Unloaded) return true;\n            Transition(i, ChunkState.DataReady);"),
    ("Failed loads retry without limit", PLAN,
     "if (_failures[i] > MaxRetries) continue; // exhausted",
     "if (false) continue; // exhausted"),
    ("Loading hold never raised", PLAN,
     "            LoadingHoldRequired = hold;", "            LoadingHoldRequired = false;"),
]


def run_suite():
    c = subprocess.run([sys.executable, "Tools/Build/headless_cs.py", "compile"], capture_output=True, text=True)
    if c.returncode != 0 or "FAILED" in c.stdout:
        return None, "compile failed"
    subprocess.run([sys.executable, "Tools/Build/headless_cs.py", "test"], capture_output=True, text=True)
    root = ET.parse("Logs/headless/EditMode.headless.xml").getroot()
    failed = [tc.get("name") for tc in root.iter("test-case") if tc.get("result") == "Failed"]
    return failed, root.get("total")


# Baseline must be green before mutating anything.
base_failed, base_total = run_suite()
print(f"[baseline] total={base_total} failed={len(base_failed) if base_failed is not None else 'n/a'}")
if base_failed is None or base_failed:
    print("[baseline] NOT GREEN — aborting mutation check")
    sys.exit(2)

ok = True
for label, path, old, new in MUTATIONS:
    original = open(path, encoding="utf-8").read()
    if original.count(old) != 1:
        print(f"[SKIP-BROKEN] {label}: anchor found {original.count(old)} times in {path}")
        ok = False
        continue
    try:
        open(path, "w", encoding="utf-8").write(original.replace(old, new, 1))
        failed, total = run_suite()
    finally:
        open(path, "w", encoding="utf-8").write(original)
    if failed is None:
        print(f"[COMPILE-ERR] {label}")
        ok = False
    elif failed:
        print(f"[CAUGHT] {label} -> {len(failed)} failing test(s), e.g. {failed[0].split('.')[-1]}")
    else:
        print(f"[MISSED] {label} -> no test failed (of {total})")
        ok = False

# Restored state must be green again.
restored_failed, _ = run_suite()
print(f"[restored] failed={len(restored_failed) if restored_failed is not None else 'n/a'}")
print(f"SUMMARY mutations={len(MUTATIONS)} ok={ok}")
sys.exit(0 if ok and not restored_failed else 1)
