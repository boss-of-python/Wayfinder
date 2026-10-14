#!/usr/bin/env python3
"""Generate Wayfinder content catalog JSON from the blueprint (authoritative).

Outputs (under Assets/Wayfinder/Content/):
  landmarks.json  - 100 landmarks, 20 per biome (Appendix A names/hooks)
  caches.json     - 200 caches, 40 per biome (20 small / 15 standard / 5 remote)
  contracts.json  - 30 authored contracts + 5 repeatable templates (§9.3)
  relics.json     - 25 relics, 5 per biome (Appendix B)

Deterministic: payouts chosen from stable FNV-1a hash of the content ID (§7.3
"deterministic in the baked content, not rerolled by reloading").
"""

import json
import os

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Wayfinder", "Content")

FNV_OFFSET = 2166136261
FNV_PRIME = 16777619


def stable_hash32(text: str) -> int:
    h = FNV_OFFSET
    for b in text.encode("utf-8"):
        h ^= b
        h = (h * FNV_PRIME) & 0xFFFFFFFF
    return h


LANDMARKS = {
    "forest": [
        ("Ranger Rise", "first safe vista and tutorial discovery"),
        ("Hollow Crown", "giant hollow tree beside a shallow alternate track"),
        ("Old Logging Trestle", "intact viewing approach, bridge silhouette clue"),
        ("Creek Gauge", "tutorial depth poles; story inspection"),
        ("Mosswheel Mill", "waterwheel landmark and easy cache"),
        ("Firewatch Nine", "switchback climb and survey reveal"),
        ("Ferncut Tunnel", "short mesh tunnel through a rocky spur"),
        ("Two Pines Crossing", "safe ford with dry detour"),
        ("The Quiet Yard", "abandoned forestry equipment and salvage"),
        ("Amber Pool", "reflective clearing, no deep-water requirement"),
        ("Ridge Radio Hut", "clue to forest relay"),
        ("Rootbridge Hollow", "visual root arch over trail"),
        ("North Logger's Camp", "camp-adjacent historical remains"),
        ("The Split Boulder", "narrow optional shortcut"),
        ("Rain Shelter", "visible refuge and weather tutorial cue"),
        ("Stone Lantern Bend", "old trail marker and relic clue"),
        ("Greenwater Spillway", "water control structure"),
        ("The Leaning Stack", "kiln/chimney silhouette above trees"),
        ("Lost Survey Wagon", "remote cache and story fragment"),
        ("Mosswood Relay", "regional mission culmination"),
    ],
    "canyon": [
        ("Red Gate Overlook", "entry panorama"),
        ("Echo Arch", "natural arch with route choice"),
        ("Copper Steps", "terraced old mine approach"),
        ("Dry River Shrine", "optional relic socket"),
        ("The Hanging Road", "narrow visible service shelf with safe detour"),
        ("Dustwell Station", "refueling-era ruins, not free field fuel"),
        ("Twin Chimneys", "rock pillars visible at long range"),
        ("Switchback Seven", "grade-reading challenge"),
        ("Sunken Quarry", "descent planning and return route"),
        ("The Broken Conveyor", "salvage-rich industrial silhouette"),
        ("Salt Ribbon", "firm white track through soft sand"),
        ("Surveyor's Needle", "distant navigational reference"),
        ("The Painted Wall", "geological strata and photo socket"),
        ("Old Cable Landing", "winch practice on optional route"),
        ("Windcut Basin", "protected campsite neighborhood"),
        ("Bridge Ledger Site", "story delivery/inspection"),
        ("The Narrow Shade", "rock corridor with width warning"),
        ("Redwater Tanks", "utility landmark and electronics cache"),
        ("Evening Mesa", "long but nontechnical viewpoint route"),
        ("Echo Relay", "regional relay and city connection"),
    ],
    "city": [
        ("South Transit Gate", "city entry marker"),
        ("Clockless Square", "open turning/tutorial area for urban navigation"),
        ("The Archive Van", "main-story protected case"),
        ("Greenline Station", "overgrown train platform viewed from service lane"),
        ("Rooftop Orchard", "authored parking-ramp viewpoint"),
        ("Flooded Underpass", "depth warning and marked detour"),
        ("The Leaning Crane", "skyline orientation cue"),
        ("Depot Seventeen", "cargo contract hub"),
        ("Blue Tile Courtyard", "compact-vehicle optional route"),
        ("Last Cinema", "facade lore and relic clue"),
        ("Power Exchange", "story cable delivery"),
        ("Canal Service Bridge", "infrastructure unlock"),
        ("Rust Garden", "reclaimed machine sculptures"),
        ("Observatory Annex", "hint toward final biome"),
        ("The Silent Loop", "circular rail turnaround"),
        ("East Reservoir", "road-level viewpoint"),
        ("Market Arcade", "driveable covered lane, no interior simulation"),
        ("Meridian Workshop", "regional garage neighborhood"),
        ("Signal Rooftop", "observation from adjacent ramp"),
        ("Meridian Relay", "city broadcast restoration"),
    ],
    "wetlands": [
        ("Reedwatch Causeway", "safe biome introduction"),
        ("Three Depth Poles", "main-story water reading"),
        ("Old Pump House", "diagram recovery"),
        ("Lantern Pier", "shoreline observation socket"),
        ("Glasswater Bend", "reflection/photo site"),
        ("Sunken Bus Stop", "viewed safely from bank"),
        ("The Salt Works", "machinery and alloy cache"),
        ("Heron Platform", "noninteractive wildlife viewpoint"),
        ("Mangrove Gate", "firm trail through dense vegetation"),
        ("Broken Sluice", "current demonstration from safe ground"),
        ("Weatherboat Memorial", "relic clue, boat is static scenery"),
        ("White Reed Island", "reachable by validated shallow causeway"),
        ("Tide Marker Twelve", "dual water-state inspection"),
        ("Longboard Crossing", "narrow but safe authored bridge"),
        ("The Dry Knoll", "forward camp neighborhood"),
        ("Cable Marsh", "avoidable bog and raised route"),
        ("Estuary Window", "broad panorama"),
        ("Pumpkeeper's House", "exterior narrative site"),
        ("Firmground Survey", "main-story route markers"),
        ("Tideglass Relay", "raised regional mast"),
    ],
    "highlands": [
        ("High Road Gate", "mountain entry view"),
        ("Shepherd's Turn", "first gentle snow patch"),
        ("Slate Basin", "gravel-to-rock handling contrast"),
        ("White Cairn", "visible ridge marker"),
        ("Weather Station East", "story sensor inspection"),
        ("Weather Station West", "alternate approach comparison"),
        ("The Old Funicular", "static rail slope, drive alongside only"),
        ("Cloudbreak Shelter", "safe view and camp neighborhood"),
        ("Frozen Mirror", "ice edge observed from stable road"),
        ("Needle Pass", "technical optional shortcut"),
        ("Summit Service Yard", "cargo turnaround and salvage"),
        ("The Copper Dome", "observatory silhouette clue"),
        ("Meltwater Steps", "seasonal-looking but validated crossing"),
        ("Stonewind Bridge", "highland infrastructure landmark"),
        ("Last Green Meadow", "quiet contrast after snowy climb"),
        ("Surveyor's Rest", "relic set finale"),
        ("Sensor Cache", "protected story core site"),
        ("The Last Mast", "fifth relay"),
        ("Atlas Approach", "final climb with loaded-return-safe route"),
        ("Frontier Observatory", "final transmission and credits"),
    ],
}

# Layout anchors per biome (§6.1) used as positionHint; M3/M5 route validation may move them.
POSITION_HINTS = {
    "forest": (1024, 2304),
    "canyon": (4600, 4400),
    "city": (4600, 2300),
    "wetlands": (3300, 800),
    "highlands": (1800, 5100),
}

REGION_INDEX = {"forest": 0, "canyon": 1, "city": 2, "wetlands": 3, "highlands": 4}

RELIC_SETS = {
    "forest": ["brass compass", "ranger badge", "trail sketch", "engraved thermos", "wooden trail marker"],
    "canyon": ["survey lens", "mine token", "cable pulley plate", "annotated rock sample", "weathered field notebook"],
    "city": ["transit token", "archive seal", "workshop emblem", "ceramic sign fragment", "radio dial"],
    "wetlands": ["tide chart tube", "pump key", "lantern plate", "depth-gauge segment", "reedwoven field tag"],
    "highlands": ["observatory patch", "star chart", "altimeter case", "brass mast plaque", "expedition pennant"],
}


def write(name, data):
    path = os.path.join(ROOT, name)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"wrote {path} ({len(data) if isinstance(data, list) else 'object'} entries)")


def gen_landmarks():
    out = []
    for biome, entries in LANDMARKS.items():
        hx, hz = POSITION_HINTS[biome]
        for i, (name, hook) in enumerate(entries, start=1):
            out.append({
                "id": f"landmark.{biome}.{i:02d}",
                "biome": biome,
                "name": name,
                "nameKey": f"landmark.{biome}.{i:02d}.name",
                "hook": hook,
                "heroSite": i <= 5,  # five hero sites per biome (Appendix A)
                "positionHint": {"x": hx + (i * 37) % 400 - 200, "y": 0, "z": hz + (i * 53) % 400 - 200},
                "discoveryRewardSalvage": 120,
                "rewardKey": f"reward.landmark.landmark.{biome}.{i:02d}",
                "clueKey": f"landmark.{biome}.{i:02d}.clue",
                "loreKey": f"landmark.{biome}.{i:02d}.lore",
                "capabilityBand": ["Gentle", "Gentle", "Demanding", "Demanding", "Technical"][min(4, (i - 1) // 4)],
                "validated": False,  # M3/M5 route validation
            })
    return out


def gen_caches():
    out = []
    kinds = [("small", 20, (60, 100)), ("standard", 15, (120, 180)), ("remote", 5, (200, 300))]
    for biome in LANDMARKS:
        hx, hz = POSITION_HINTS[biome]
        idx = 0
        for kind, count, (low, high) in kinds:
            for k in range(count):
                idx += 1
                cid = f"cache.{biome}.{idx:03d}"
                h = stable_hash32(cid)
                payout = low + (h % (high - low + 1))
                materials = []
                if kind in ("standard", "remote"):
                    materials.append({"itemId": "scrap_metal" if h % 2 == 0 else "polymer",
                                      "quantity": 1 + h % 2})
                if kind == "remote":
                    materials.append({"itemId": "electronics", "quantity": 1})
                out.append({
                    "id": cid,
                    "biome": biome,
                    "kind": kind,
                    "positionHint": {"x": hx + (h % 600) - 300, "y": 0, "z": hz + ((h // 7) % 600) - 300},
                    "salvageUnits": payout,
                    "materials": materials,
                    "oneTime": True,
                    "rewardKey": f"reward.cache.{cid}",
                    "validated": False,
                })
    return out


def gen_contracts():
    patterns = ["Triangulate", "GentleDelivery", "HiddenVista", "InfrastructureCheck", "FieldSample", "OldRoute"]
    prefix = {"forest": "C_F", "canyon": "C_C", "city": "C_U", "wetlands": "C_W", "highlands": "C_H"}
    base = {"Triangulate": 250, "GentleDelivery": 300, "HiddenVista": 300,
            "InfrastructureCheck": 300, "FieldSample": 350, "OldRoute": 400}
    out = []
    for biome, region_idx in REGION_INDEX.items():
        for i, pattern in enumerate(patterns, start=1):
            reward = base[pattern] + 100 * region_idx  # §9.3 formula
            if pattern == "GentleDelivery":
                reward += 100  # optional bonus (lost on heavy collision, not completion)
            out.append({
                "id": f"{prefix[biome]}{i:02d}",
                "biome": biome,
                "pattern": pattern,
                "regionIndex": region_idx,
                "rewardSalvage": reward,
                "rewardKey": f"reward.contract.{prefix[biome]}{i:02d}",
                "targets": [f"contract_socket.{biome}.{i}.1", f"contract_socket.{biome}.{i}.2",
                            f"contract_socket.{biome}.{i}.3"][: (3 if pattern in ("Triangulate", "FieldSample") else
                                                                4 if pattern == "InfrastructureCheck" else 1)],
                "oneTime": True,
                "validated": False,
            })
    templates = [
        ("job.resurvey", "resurvey two known points", 2),
        ("job.supply_delivery", "deliver one 20 kg supply case", 1),
        ("job.utility_crate", "retrieve a marked utility crate", 1),
        ("job.weather_instruments", "inspect two weather instruments", 2),
        ("job.vista_photo", "photograph a known vista from a new socket", 1),
    ]
    for tid, desc, points in templates:
        out.append({
            "id": tid, "biome": "any", "pattern": "Repeatable", "regionIndex": 0,
            "rewardSalvage": None, "rewardRange": [150, 350], "rewardFormula": "byValidatedRouteLength",
            "description": desc, "targets": points, "oneTime": False,
            "maxAvailableAtGarage": 3, "refresh": "afterExpeditionEnds",
        })
    return out


def gen_relics():
    out = []
    for biome, relics in RELIC_SETS.items():
        for i, name in enumerate(relics, start=1):
            out.append({
                "id": f"relic.{biome}.{i:02d}",
                "biome": biome,
                "name": name,
                "nameKey": f"relic.{biome}.{i:02d}.name",
                "protectedLedger": True,
                "sellable": False,
                "rewardKey": f"reward.relic.{biome}.{i:02d}",
            })
    return out


if __name__ == "__main__":
    write("landmarks.json", gen_landmarks())
    write("caches.json", gen_caches())
    write("contracts.json", gen_contracts())
    write("relics.json", gen_relics())
