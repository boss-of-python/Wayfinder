#!/usr/bin/env python3
"""Exploration map (Batch D): break each map rule / safeguard one at a time and confirm the
headless EditMode suite catches it. Restores every file afterwards. Run from the repo root.
Never edit Assets/Wayfinder/Scripts while this runs (it rewrites files temporarily)."""
import subprocess, sys, xml.etree.ElementTree as ET

S = "Assets/Wayfinder/Scripts/"
MUTATIONS = [
    ("Session reveal ignores terrain line-of-sight (§8.1)", S + "Campaign/CampaignSession.Field.cs",
     "int newly = Discovery.RevealVisibleAround(x, z, vista, HeightSampler);",
     "int newly = Discovery.RevealAround(x, z, vista);"),
    ("Vista survey ignores occlusion (pre-batch behaviour)", S + "Progression/DiscoveryService.cs",
     "SurveyedCells.RevealCircle(worldX, worldZ, radius, WorldConstants.MapCellSizeMeters, eligible);",
     "SurveyedCells.RevealCircle(worldX, worldZ, radius, WorldConstants.MapCellSizeMeters, (x, z) => CountableMask.Get(x, z));"),
    ("Unopened caches drawn on the map (§7.6 spoiler)", S + "Campaign/Map/MapMarkers.cs",
     "if (!s.Discovery.IsCacheCollected(cacheId) || !s.Content.Caches.TryGetValue(cacheId, out var cache)) continue;",
     "if (!s.Content.Caches.TryGetValue(cacheId, out var cache)) continue;"),
    ("Undiscovered landmarks show their names", S + "Campaign/Map/MapMarkers.cs",
     "into.Add(new MapMarker(MapMarkerKind.LandmarkSuspected, l.Id, \"Unconfirmed landmark\",",
     "into.Add(new MapMarker(MapMarkerKind.LandmarkSuspected, l.Id, def.Name,"),
    ("Search circle centred exactly on the hidden landmark", S + "Campaign/Map/MapMarkers.cs",
     "double offset = radiusMeters * SearchAreaMaxOffsetFraction * fraction;",
     "double offset = 0.0 * fraction;"),
    ("Revealed neighbours not redrawn (stale soft edges)", S + "Campaign/Map/ExplorationMap.cs",
     "if ((dx != 0 || dz != 0) && nx >= 0 && nz >= 0 && nx < _gridSide && nz < _gridSide && revealed.Get(nx, nz))",
     "if (dx == 99 && revealed.Get(nx, nz))"),
    ("Survey-only changes never redrawn (no contours)", S + "Campaign/Map/ExplorationMap.cs",
     "            surveyed.CollectDifferences(_shownSurveyed, _changed);\n", ""),
    ("Reveal cadence back to 1 Hz (§16.2 says 2 Hz)", S + "Campaign/SliceFieldController.cs",
     "public const float RevealIntervalSeconds = 0.5f;", "public const float RevealIntervalSeconds = 1f;"),
    ("Custom markers not written to the save", S + "Campaign/CampaignSnapshot.cs",
     "customMarkers = MarkerDtos(s.MapMarkers),", "customMarkers = new List<MapMarkerDto>(),"),
    ("Ten-marker limit not enforced (§13.4)", S + "Progression/MapMarkerService.cs",
     "            if (IsFull)\n", "            if (Count > MaxMarkers)\n"),
    ("Marker ids reused after removal", S + "Progression/MapMarkerService.cs",
     "            NextIndex++;\n            _markers.Add(marker);", "            _markers.Add(marker);"),
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
