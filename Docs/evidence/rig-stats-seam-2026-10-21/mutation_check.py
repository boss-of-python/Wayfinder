#!/usr/bin/env python3
"""Mutation check for Batch N (KI-025 ApplyEffectiveStats seam, D-044).

Each mutant breaks ONE rule in Simulation/WheelRigMath.cs, recompiles with
`Tools/Build/headless_cs.py compile`, runs the whole EditMode suite, and must be caught by at least
one failing test (or fail to compile). Sources are restored from memory after every mutant; the
script refuses to start on a dirty Assets/ tree and verifies it left the tree clean.

VehicleController's Unity-side application cannot execute here (syntax-checked only); every
formula it calls lives in WheelRigMath, which is what these mutants cover.

Usage:  python3 Docs/evidence/rig-stats-seam-2026-10-21/mutation_check.py
Exit:   0 when every mutant is caught, 1 otherwise.
"""
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
RIG = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Simulation/WheelRigMath.cs")
RESULT_XML = os.path.join(ROOT, "Logs/headless/EditMode.headless.xml")

MUTANTS = [
    ("N1", "clearance lift moves the wheel centers the wrong way", RIG,
     "                -clearanceDelta,",
     "                clearanceDelta,"),
    ("N2", "front damper ignores suspension condition", RIG,
     "                spec.DamperFrontNsm * damping,",
     "                spec.DamperFrontNsm,"),
    ("N3", "suspension travel is not clamped", RIG,
     "            float travel = MathUtil.Clamp(stats.SuspensionTravelM, MinTravelM, MaxTravelM);",
     "            float travel = stats.SuspensionTravelM;"),
    ("N4", "forward friction ignores mud sink", RIG,
     "            Math.Max(0f, grip * (1f - 0.5f * MathUtil.Clamp01(sinkFraction)) * tractionMultiplier);",
     "            Math.Max(0f, grip * tractionMultiplier);"),
    ("N5", "brake envelope loses its 10% floor (zero brake on ice-grip 0)", RIG,
     "            MathUtil.Clamp01(Math.Max(0f, surfaceGrip) / 0.9f + 0.1f);",
     "            MathUtil.Clamp01(Math.Max(0f, surfaceGrip) / 0.9f);"),
    ("N6", "a travel change never counts as a geometry update", RIG,
     "            Math.Abs(previous.SuspensionDistanceM - next.SuspensionDistanceM) > GeometryEpsilon ||",
     "            false ||"),
    ("N7", "an anchor exactly at winch reach is refused", RIG,
     "            HasWinch && distanceM >= 0f && distanceM <= WinchReachM;",
     "            HasWinch && distanceM >= 0f && distanceM < WinchReachM;"),
    ("N8", "spring rate wrongly scales with suspension condition", RIG,
     "                spec.SpringRateFrontNm,",
     "                spec.SpringRateFrontNm * damping,"),
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
