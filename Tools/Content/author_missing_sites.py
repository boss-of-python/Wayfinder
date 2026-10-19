#!/usr/bin/env python3
"""Author Assets/Wayfinder/Content/sites.json — the world sites missions reference but no catalog defines.

Why this file exists
--------------------
DEF-003 (Docs/AI_BUG_REPORT.md): 24 objective target ids used by Q05–Q20 (`relay.canyon`,
`junction.city.01`, `marker.canyon.02`, `pole.wetlands.03`, `route.wetlands.firm`,
`site.highlands.observatory`, …) appear *only* in missions.json and MissionService.cs. No catalog
defines them, and `CampaignSession` resolves inspect/relay/quest-item targets through the forest
vertical slice alone, so those 16 missions can never be completed.

This script writes the missing site records. It is deterministic and idempotent: running it twice
produces byte-identical output, so regenerating after a mission edit is safe.

Provenance of the positions
---------------------------
* `"provenance": "spec-anchor"` — the position is a hard spec value. Every `relay.*` site uses its
  §6.1 region relay anchor from `World/WorldConstants.cs` (CanyonRelayAnchor, CityRelayAnchor,
  WetlandRelayAnchor, HighlandRelayAnchor). These are not invented.
* `"provenance": "procedural"` — the position is placed by this script: a golden-angle spiral around
  the biome's relay anchor, radius 220 m + 130 m per site index, clamped inside the 6,144 m world
  with a 150 m margin. Route checkpoints run in a straight line from the anchor at 180 m spacing.
  These are placeholder placements, not level design: sites.json is the canonical, human-editable
  source (§17.1), so a designer moves a site by editing this file. Every procedural site still
  satisfies the reachability rule in `Tools/Validation/validate_content.py` (inside world bounds,
  unique id, one record per mission target).

Usage:  python3 Tools/Content/author_missing_sites.py [--check]
        --check  exits 1 when the file on disk differs from what would be generated.
"""

import json
import math
import os
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
CONTENT = os.path.join(ROOT, "Assets", "Wayfinder", "Content")
OUT = os.path.join(CONTENT, "sites.json")

WORLD_SIZE = 6144.0
MARGIN = 150.0

# §6.1 region relay layout anchors — mirrored from Assets/Wayfinder/Scripts/World/WorldDefinition.cs.
RELAY_ANCHORS = {
    "forest": (1408.0, 2816.0),
    "canyon": (4864.0, 4608.0),
    "city": (4608.0, 2304.0),
    "wetlands": (3328.0, 768.0),
    "highlands": (1792.0, 5120.0),
}

# Mission code → biome (the chain walks forest → canyon → city → wetlands → highlands).
MISSION_BIOME = {
    "Q01": "forest", "Q02": "forest", "Q03": "forest", "Q04": "forest",
    "Q05": "canyon", "Q06": "canyon", "Q07": "canyon", "Q08": "canyon",
    "Q09": "city", "Q10": "city", "Q11": "city", "Q12": "city",
    "Q13": "wetlands", "Q14": "wetlands", "Q15": "wetlands", "Q16": "wetlands",
    "Q17": "highlands", "Q18": "highlands", "Q19": "highlands", "Q20": "highlands",
}

# Objective type → (site kind, interaction radius in metres, how many checkpoints for a route).
KIND_OF = {
    "InspectObject": ("inspect", 12.0),
    "ActivateRelay": ("relay", 15.0),
    "CollectQuestItem": ("questItem", 8.0),
    "DeliverQuestItem": ("delivery", 10.0),
    "VisitVolume": ("visit", 40.0),
    "TraverseCheckpoints": ("route", 25.0),
    "EstablishCamp": ("campSocket", 15.0),
}

GOLDEN_ANGLE = math.pi * (3.0 - math.sqrt(5.0))


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def load(name):
    with open(os.path.join(CONTENT, name)) as f:
        return json.load(f)


def known_ids(slice_manifest):
    """Every target id that already resolves without sites.json."""
    ids = set()
    for name in ("landmarks.json", "caches.json", "garages.json", "camps.json", "items.json"):
        ids.update(item["id"] for item in load(name))
    for key in ("campSockets", "inspectTargets", "questItemSites", "relays", "checkpointRoutes"):
        ids.update(site["id"] for site in slice_manifest.get(key, []))
    ids.update(str(site["id"]) for site in slice_manifest.get("landmarks", []))
    # RevealMapPercent carries the pseudo-target "map"; it is a percentage, not a place.
    ids.add("map")
    return ids


def biome_of(target_id, mission_code):
    for biome in ("forest", "canyon", "city", "wetlands", "highlands"):
        if "." + biome + "." in target_id or target_id.endswith("." + biome):
            return biome
    return MISSION_BIOME.get(mission_code, "forest")


def build():
    missions = load("missions.json")
    slice_manifest = load("vertical_slice.json")
    known = known_ids(slice_manifest)

    # Collect the missing targets in mission order, one record per target id. A site referenced by
    # two missions (relay.canyon is activated in Q08 and revisited in Q20) is one place, so later
    # missions are merged into `missionCodes` instead of producing a second record.
    needed = {}
    for mission in missions:
        code = mission["displayCode"]
        for obj in mission.get("objectives", []):
            target = obj["targetId"]
            if target in known or target in ("map",):
                continue
            kind = KIND_OF.get(obj["type"])
            if kind is None:
                continue
            entry = needed.get(target)
            if entry is None:
                needed[target] = {
                    "id": target,
                    "kind": kind[0],
                    "radius": kind[1],
                    "missionCodes": [code],
                    "biome": biome_of(target, code),
                    "count": int(obj.get("count", 1)),
                }
            else:
                if code not in entry["missionCodes"]:
                    entry["missionCodes"].append(code)
                entry["count"] = max(entry["count"], int(obj.get("count", 1)))

    # Stable per-biome ordering so the spiral placement never depends on dict iteration order.
    per_biome = {}
    for entry in needed.values():
        per_biome.setdefault(entry["biome"], []).append(entry)
    for entries in per_biome.values():
        entries.sort(key=lambda e: e["id"])

    sites = []
    for biome, entries in per_biome.items():
        anchor_x, anchor_z = RELAY_ANCHORS[biome]
        for index, entry in enumerate(entries):
            radius = 220.0 + 130.0 * index
            angle = index * GOLDEN_ANGLE
            x = clamp(anchor_x + radius * math.cos(angle), MARGIN, WORLD_SIZE - MARGIN)
            z = clamp(anchor_z + radius * math.sin(angle), MARGIN, WORLD_SIZE - MARGIN)
            if entry["id"].startswith("relay."):
                # §6.1: a relay sits exactly on its region anchor.
                x, z = anchor_x, anchor_z
                provenance = "spec-anchor"
            else:
                provenance = "procedural"

            site = {
                "id": entry["id"],
                "kind": entry["kind"],
                "biome": biome,
                "missionCodes": entry["missionCodes"],
                "positionHint": {"x": round(x, 1), "y": 0, "z": round(z, 1)},
                "radiusMeters": entry["radius"],
                "provenance": provenance,
            }
            if entry["kind"] == "relay":
                # Activating any relay consumes one protected relay fuse (§6.3, as Q04 does).
                site["itemId"] = "relay_fuse"
            elif entry["kind"] == "questItem":
                site["itemId"] = entry["id"]
            elif entry["kind"] == "delivery":
                site["itemId"] = ""
            if entry["kind"] == "route":
                site["checkpoints"] = route_checkpoints(anchor_x, anchor_z, index, entry["count"])
            sites.append(site)

    sites.sort(key=lambda s: s["id"])
    return sites


def route_checkpoints(anchor_x, anchor_z, index, count):
    """A straight run of `count` checkpoints heading out from the biome anchor."""
    heading = (index * GOLDEN_ANGLE) % (2 * math.pi)
    dx, dz = math.cos(heading), math.sin(heading)
    points = []
    for i in range(max(2, count)):
        distance = 250.0 + 180.0 * i
        points.append({
            "x": round(clamp(anchor_x + dx * distance, MARGIN, WORLD_SIZE - MARGIN), 1),
            "y": 0,
            "z": round(clamp(anchor_z + dz * distance, MARGIN, WORLD_SIZE - MARGIN), 1),
        })
    return points


def render(sites):
    text = json.dumps(sites, indent=2, sort_keys=False)
    return text + "\n"


def main(argv):
    sites = build()
    text = render(sites)
    if "--check" in argv:
        with open(OUT) as f:
            if f.read() != text:
                print(f"{OUT} is out of date; run author_missing_sites.py")
                return 1
        print(f"sites.json up to date ({len(sites)} sites)")
        return 0
    with open(OUT, "w") as f:
        f.write(text)
    print(f"wrote {OUT} ({len(sites)} sites)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
