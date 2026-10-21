#!/usr/bin/env python3
"""
Wayfinder complete offline verification harness (M0-M6 headless gates).

Runs every deterministic gate in sequence with strict fail-fast exit semantics:
  1. Tools/Validation/validate_content.py   — content schemas, reachability, vertical slice, banned deps
  2. Tools/Validation/formula_checks.py     — physics/damage/fuel/water/research/world/seam/apron invariants
  3. Tools/Balance/economy_sim.py           — 3-pattern progression economy simulation + deadlock/arbitrage scan
  4. Tools/Build/headless_cs.py all         — Roslyn C# 9 compile, 319+ NUnit EditMode tests, .NET Standard 2.1 profile, syntax check
  5. Tools/Build/unity_type_check.py        — full 12-assembly Unity 6 API + URP + InputSystem type check (0 warnings)
  6. unittest discover Tools/Build          — 117 CI/build/diagnostic pipeline tests
"""

from __future__ import annotations

import os
import subprocess
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

STEPS = [
    ("Content & Schema Validation", [sys.executable, "Tools/Validation/validate_content.py"]),
    ("Physics, World & Formula Invariants", [sys.executable, "Tools/Validation/formula_checks.py"]),
    ("Economy Progression & Arbitrage Sim", [sys.executable, "Tools/Balance/economy_sim.py"]),
    ("Headless C# Compile + NUnit Suite + .NET Standard 2.1", [sys.executable, "Tools/Build/headless_cs.py", "all"]),
    ("12-Assembly Unity API Type Check", [sys.executable, "Tools/Build/unity_type_check.py"]),
    ("Build & Cloud CI Diagnostic Unit Suite", [sys.executable, "-m", "unittest", "discover", "-s", "Tools/Build", "-p", "test_*.py"]),
]


def main() -> int:
    started = time.monotonic()
    print("=================================================================")
    print(" Wayfinder Full Verification Gate Suite")
    print("=================================================================")
    for idx, (label, cmd) in enumerate(STEPS, start=1):
        t0 = time.monotonic()
        print(f"\n[{idx}/{len(STEPS)}] {label}: {' '.join(cmd)}", flush=True)
        proc = subprocess.run(cmd, cwd=ROOT)
        dt = time.monotonic() - t0
        if proc.returncode != 0:
            print(f"\n[FAIL] Gate {idx}/{len(STEPS)} ({label}) failed with exit code {proc.returncode} after {dt:.1f}s.", file=sys.stderr)
            return proc.returncode
        print(f"[PASS] Gate {idx}/{len(STEPS)} ({label}) completed in {dt:.1f}s.", flush=True)

    total = time.monotonic() - started
    print("\n=================================================================")
    print(f" ALL {len(STEPS)}/{len(STEPS)} WAYFINDER GATES PASSED IN {total:.1f}s (0 ERRORS, 0 WARNINGS)")
    print("=================================================================")
    return 0


if __name__ == "__main__":
    sys.exit(main())
