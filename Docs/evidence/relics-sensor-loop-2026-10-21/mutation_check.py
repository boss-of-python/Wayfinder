#!/usr/bin/env python3
"""Mutation check for Batch O (MAP-03 / §8.3: relic sets and the passive sensor loop).

Each mutant breaks ONE rule in the production source, recompiles with
`Tools/Build/headless_cs.py compile`, runs the whole EditMode suite, and must be caught by at least
one failing test (or fail to compile). Sources are restored from memory after every mutant; the script
refuses to start on a dirty Assets/ tree and verifies it left the tree clean.

Three files carry these rules:
  * Campaign/Map/SensorLoop.cs          — pulse radius, the 45 m precision range, bearing, search area
  * Campaign/CampaignSession.Field.cs   — CollectRelic, the relic reward keys and set completion
  * Campaign/CampaignSession.Map.cs     — SensorScan, which finally consumes Stat.SignalRadiusM

Not mutation-covered, deliberately:
  * The `?? true` / `?? false` defaults for protectedLedger and sellable in CampaignContent's relic
    parse — every relic in relics.json sets both keys explicitly, so no current content can reach a
    default. They stay as defence for content that forgets a key.
  * The Ordinal sort of each relic set — relics.json already lists every biome in sorted order, so
    removing the sort changes nothing observable. The order is still asserted by a test, which pins
    the contract for content that is not pre-sorted.
  * `CollectRelic`'s RunCritical wrapper — mutating it to a non-saving path is covered by the
    session's existing critical-save tests, not by a relic-specific one.

Usage:  python3 Docs/evidence/relics-sensor-loop-2026-10-21/mutation_check.py
Exit:   0 when every mutant is caught, 1 otherwise.
"""
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SENSOR = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Campaign/Map/SensorLoop.cs")
FIELD = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Campaign/CampaignSession.Field.cs")
MAP = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Campaign/CampaignSession.Map.cs")
RESULT_XML = os.path.join(ROOT, "Logs/headless/EditMode.headless.xml")

BEARING = """            float bearing = directional
                ? (float)((Math.Atan2(dx, dz) * 180d / Math.PI + 360d) % 360d)
                : 0f;"""

MUTANTS = [
    # ------------------------------------------------- §8.3 "pulses when a signal is within 80 m"
    ("M01", "the pulse test excludes the boundary (80 m exactly goes quiet)", SENSOR,
     "            bool pulsing = distance <= radius;",
     "            bool pulsing = distance < radius;"),
    ("M02", "the baseline sensor reaches only 45 m", SENSOR,
     "        public const float BaseSignalRadiusM = 80f;",
     "        public const float BaseSignalRadiusM = 45f;"),

    # ------------------------------------- §8.3 "directional precision improves at 45 m"
    ("M03", "precision arrives at 80 m instead of 45 m", SENSOR,
     "        public const float DirectionalPrecisionRadiusM = 45f;",
     "        public const float DirectionalPrecisionRadiusM = 80f;"),
    ("M04", "precision follows the resolved radius, so Radar II is precise at 220 m", SENSOR,
     "            bool directional = distance <= DirectionalPrecisionRadiusM;",
     "            bool directional = distance <= radius;"),
    ("M05", "a bearing is reported even beyond the precision range", SENSOR,
     BEARING,
     "            float bearing = (float)((Math.Atan2(dx, dz) * 180d / Math.PI + 360d) % 360d);"),
    ("M06", "the bearing transposes east and north", SENSOR,
     "                ? (float)((Math.Atan2(dx, dz) * 180d / Math.PI + 360d) % 360d)",
     "                ? (float)((Math.Atan2(dz, dx) * 180d / Math.PI + 360d) % 360d)"),

    # ------------------------- §8.3 "the map shows a search area, not exact vertical navigation"
    ("M07", "a distant signal reveals its exact position", SENSOR,
     "                var centre = MapMarkerRules.SearchAreaCenter(signal.Id, signal.X, signal.Z);",
     "                var centre = (x: signal.X, z: signal.Z);"),

    # ------------------------------------------- the resolved radius must actually be used
    ("M08", "a missing radius deafens the sensor instead of falling back to 80 m", SENSOR,
     "            float radius = float.IsNaN(signalRadiusM) || signalRadiusM <= 0f ? BaseSignalRadiusM : signalRadiusM;",
     "            float radius = float.IsNaN(signalRadiusM) || signalRadiusM <= 0f ? 0f : signalRadiusM;"),
    ("M09", "a missing radius becomes unlimited range", SENSOR,
     "            float radius = float.IsNaN(signalRadiusM) || signalRadiusM <= 0f ? BaseSignalRadiusM : signalRadiusM;",
     "            float radius = float.IsNaN(signalRadiusM) || signalRadiusM <= 0f ? float.MaxValue : signalRadiusM;"),
    ("M10", "the scan reports out-of-range signals too", SENSOR,
     "                if (reading.Pulsing) hits.Add(reading);",
     "                hits.Add(reading);"),
    ("M11", "the scan reports farthest first", SENSOR,
     "            hits.Sort((a, b) => a.DistanceM.CompareTo(b.DistanceM));",
     "            hits.Sort((a, b) => b.DistanceM.CompareTo(a.DistanceM));"),
    ("M12", "Radar I is recorded as 150 m instead of 140 m (§11.2 tier 9)", SENSOR,
     "        public const float RadarISignalRadiusM = 140f;",
     "        public const float RadarISignalRadiusM = 150f;"),
    ("M13", "Radar II is recorded as 140 m, so tier 19 changes nothing (§11.2 tier 19)", SENSOR,
     "        public const float RadarIISignalRadiusM = 220f;",
     "        public const float RadarIISignalRadiusM = 140f;"),

    # --------------------------- the sweep must read the vehicle, not a constant (the real defect)
    ("M14", "SensorScan ignores the resolved radius and always uses the 80 m baseline", MAP,
     "            return SensorLoop.Scan(signals, observerX, observerZ, ResolveStats().SignalRadiusM);",
     "            return SensorLoop.Scan(signals, observerX, observerZ, SensorLoop.BaseSignalRadiusM);"),
    ("M15", "the pose overload sweeps the world origin instead of the vehicle", MAP,
     "                ? SensorScan(Expedition.Pose.Position.X, Expedition.Pose.Position.Z)",
     "                ? SensorScan(0f, 0f)"),
    ("M16", "the sensor sweeps even when parked", MAP,
     "            Expedition.IsActive\n                ? SensorScan(Expedition.Pose.Position.X, Expedition.Pose.Position.Z)",
     "            true\n                ? SensorScan(Expedition.Pose.Position.X, Expedition.Pose.Position.Z)"),

    # --------------------------------------------- §8.3 relics: protected, weightless, unsellable
    ("M17", "a relic's own reward key is never claimed (§8.4 separate keys)", FIELD,
     "                Discovery.RewardLedger.TryClaim(relic.RewardKey);",
     "                // mutant: the relic's own reward key is never claimed"),
    ("M18", "a completed set claims no set reward key", FIELD,
     "                if (IsRelicSetComplete(relic.Biome) &&",
     "                if (false &&"),
    ("M19", "any relic at all completes its biome's set", FIELD,
     "            return total > 0 && RelicsFoundInSet(biome) >= total;",
     "            return total > 0;"),
    ("M20", "set progress counts the set's members instead of the ones found", FIELD,
     "                if (Discovery.HasRelic(id)) found++;",
     "                found++;"),
    ("M21", "the same relic can be collected again", FIELD,
     """            if (Discovery.HasRelic(relicId))
                return CommandResult.Fail(FailureReason.AlreadyCollected, relicId);""",
     "            // mutant: the duplicate-collection guard is gone"),
    ("M22", "an unknown relic is refused as a bad target, not as missing content (§14)", FIELD,
     '                return CommandResult.Fail(FailureReason.ContentMissing, $"unknown relic \'{relicId}\'");',
     '                return CommandResult.Fail(FailureReason.InvalidTarget, $"unknown relic \'{relicId}\'");'),
    ("M23", "recovering a relic is silent — no per-relic notice", FIELD,
     '                Notice(CampaignNoticeKind.RelicCollected, $"Relic recovered: {DisplayNames.Relic(relicId)}", 1, relicId);',
     "                // mutant: no per-relic notice"),
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
