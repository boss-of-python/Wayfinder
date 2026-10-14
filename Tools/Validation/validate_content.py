#!/usr/bin/env python3
"""Wayfinder content validation (headless).

Mirrors Assets/Wayfinder/Scripts/Editor/ContentValidators.cs so it can run in CI or any
environment without Unity. Exit code 0 = pass, 1 = validation errors.

Checks (§16.6, CONTENT-01, UPG-01, QUEST-01, ITEM-01, PROD-01):
  * exact release-scope content counts and per-biome splits
  * unique stable IDs everywhere
  * upgrade table: sequential tiers, exact incremental costs, Σ = 32,200, story gates exist
  * mission table: Q01–Q20, rewards match §9.2 exactly, prerequisite chain acyclic
  * recipe inputs exist; crafting+resale can never create currency (QA case 17)
  * cache payouts inside authored bands; contract rewards follow §9.3 formula
  * surface table matches §4.4 exactly; vehicle table matches §4.2/§10.1
  * Packages/manifest.json contains no ad / IAP / analytics / tracking SDK (PROD-01)
"""

import json
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
CONTENT = os.path.join(ROOT, "Assets", "Wayfinder", "Content")

errors = []
warnings = []


def err(msg):
    errors.append(msg)


def warn(msg):
    warnings.append(msg)


def load(name):
    with open(os.path.join(CONTENT, name), encoding="utf-8") as f:
        return json.load(f)


MISSION_REWARDS = {
    "Q01": 150, "Q02": 200, "Q03": 300, "Q04": 500, "Q05": 300,
    "Q06": 400, "Q07": 500, "Q08": 700, "Q09": 400, "Q10": 500,
    "Q11": 650, "Q12": 850, "Q13": 500, "Q14": 650, "Q15": 800,
    "Q16": 1000, "Q17": 650, "Q18": 800, "Q19": 1000, "Q20": 1500,
}

UPGRADE_COSTS = [200, 300, 450, 550, 700, 850, 1000, 1150, 1300, 1450,
                 1600, 1750, 1900, 2100, 2300, 2500, 2700, 2900, 3100, 3400]

UPGRADE_GATES = ["Q01", "Q02", "Q02", "Q03", "Q04", "Q04", "Q05", "Q06", "Q07", "Q08",
                 "Q09", "Q10", "Q11", "Q12", "Q13", "Q14", "Q15", "Q16", "Q17", "Q19"]

SURFACE_TABLE = {  # §4.4
    "intact_asphalt": (1.00, 0.75, 0.015),
    "broken_asphalt": (0.90, 0.75, 0.025),
    "packed_dirt": (0.82, 0.72, 0.035),
    "gravel": (0.70, 0.85, 0.045),
    "grass": (0.65, 0.65, 0.050),
    "rock": (0.90, 0.65, 0.030),
    "shallow_mud": (0.48, 0.80, 0.110),
    "deep_mud": (0.30, 0.80, 0.200),
    "sand": (0.50, 1.00, 0.100),
    "packed_snow": (0.38, 0.85, 0.070),
    "ice": (0.12, 1.00, 0.010),
}

BANNED_PACKAGE_TOKENS = [
    "advert", "admob", "ironsource", "applovin", "unity.ads", "unity.monetization",
    "iap", "inapp", "purchasing", "analytics", "tracking", "firebase", "amplitude",
    "mixpanel", "appsflyer", "adjust.", "kochava", "multiplayer", "netcode", "narrative",
]


def check_counts():
    landmarks = load("landmarks.json")
    caches = load("caches.json")
    missions = load("missions.json")
    contracts = load("contracts.json")
    upgrades = load("upgrades.json")
    vehicles = load("vehicles.json")
    items = load("items.json")
    camps = load("camps.json")
    garages = load("garages.json")
    relics = load("relics.json")
    recipes = load("recipes.json")
    surfaces = load("surfaces.json")
    biomes = load("biomes.json")

    def count(label, actual, expected):
        if actual != expected:
            err(f"{label}: expected {expected}, found {actual}")

    count("landmarks total", len(landmarks), 100)
    count("caches total", len(caches), 200)
    count("missions total", len(missions), 20)
    count("authored contracts", len([c for c in contracts if c.get("oneTime")]), 30)
    count("repeatable templates", len([c for c in contracts if not c.get("oneTime")]), 5)
    count("upgrades", len(upgrades), 20)
    count("vehicles", len(vehicles), 4)
    count("camps", len(camps), 10)
    count("garages", len(garages), 5)
    count("relics", len(relics), 25)
    count("recipes", len(recipes), 4)
    count("surfaces", len(surfaces), 11)
    count("biomes", len(biomes), 5)

    for biome in ("forest", "canyon", "city", "wetlands", "highlands"):
        count(f"landmarks.{biome}", len([l for l in landmarks if l["biome"] == biome]), 20)
        count(f"caches.{biome}", len([c for c in caches if c["biome"] == biome]), 40)
        count(f"contracts.{biome}", len([c for c in contracts if c.get("biome") == biome]), 6)
        count(f"camps.{biome}", len([c for c in camps if c["biome"] == biome]), 2)
        kinds = {}
        for c in caches:
            if c["biome"] == biome:
                kinds[c["kind"]] = kinds.get(c["kind"], 0) + 1
        count(f"caches.{biome}.small", kinds.get("small", 0), 20)
        count(f"caches.{biome}.standard", kinds.get("standard", 0), 15)
        count(f"caches.{biome}.remote", kinds.get("remote", 0), 5)

    # Unique IDs everywhere
    all_ids = []
    for collection, key in [(landmarks, "id"), (caches, "id"), (missions, "id"),
                            (contracts, "id"), (upgrades, "id"), (vehicles, "id"),
                            (items, "id"), (camps, "id"), (garages, "id"),
                            (relics, "id"), (recipes, "id"), (surfaces, "id"), (biomes, "id")]:
        for entry in collection:
            all_ids.append(entry[key])
    seen = set()
    for i in all_ids:
        if i in seen:
            err(f"duplicate id: {i}")
        seen.add(i)
    if len(all_ids) != len(set(all_ids)):
        pass  # already reported


def check_upgrades():
    upgrades = load("upgrades.json")
    missions = {m["displayCode"] for m in load("missions.json")}
    total = 0
    for i, u in enumerate(upgrades, start=1):
        if u["tier"] != i:
            err(f"upgrade {u['id']}: tier {u['tier']} out of sequence (expected {i})")
        if u["costSalvage"] != UPGRADE_COSTS[i - 1]:
            err(f"upgrade {u['id']}: cost {u['costSalvage']} != table value {UPGRADE_COSTS[i-1]}")
        total += u["costSalvage"]
        if u["requiredMission"] not in missions:
            err(f"upgrade {u['id']}: story gate '{u['requiredMission']}' does not exist")
        if u["requiredMission"] != UPGRADE_GATES[i - 1]:
            err(f"upgrade {u['id']}: gate {u['requiredMission']} != table value {UPGRADE_GATES[i-1]}")
        if i > 1 and u["prerequisites"] != [upgrades[i - 2]["id"]]:
            err(f"upgrade {u['id']}: prerequisite must be previous tier (sequential tree)")
    if total != 32200:
        err(f"upgrade total cost {total} != 32200 (§11.2)")


def check_missions():
    missions = load("missions.json")
    by_code = {m["displayCode"]: m for m in missions}
    for code, reward in MISSION_REWARDS.items():
        m = by_code.get(code)
        if m is None:
            err(f"mission {code} missing")
            continue
        if m["reward"]["salvage"] != reward:
            err(f"mission {code}: reward {m['reward']['salvage']} != table value {reward}")
        if m["reward"]["rewardKey"] != f"reward.story.{code}":
            err(f"mission {code}: reward key must be 'reward.story.{code}'")
        if not m["id"].startswith("mission."):
            err(f"mission {code}: id must be canonical 'mission.{code}'")
    # Prerequisite chain: Q(n) requires Q(n-1); no cycles possible in this linear chain,
    # verify the chain explicitly.
    for n in range(2, 21):
        code = f"Q{n:02d}"
        m = by_code.get(code)
        if m and m["prerequisites"] != [f"mission.Q{n-1:02d}"]:
            err(f"mission {code}: prerequisites must be [mission.Q{n-1:02d}]")
    # Objective vocabulary is typed (§17.1)
    allowed = {"DiscoverLandmark", "VisitVolume", "CollectQuestItem", "DeliverQuestItem",
               "RevealMapPercent", "ActivateRelay", "EstablishCamp", "InspectObject",
               "TraverseCheckpoints", "ReturnToHub"}
    for m in missions:
        for obj in m["objectives"]:
            if obj["type"] not in allowed:
                err(f"mission {m['displayCode']}: non-typed objective '{obj['type']}'")


def check_items_recipes():
    items = {i["id"]: i for i in load("items.json")}
    recipes = load("recipes.json")
    for r in recipes:
        out = r["output"]
        if out["itemId"] not in items:
            err(f"recipe {r['id']}: unknown output item")
            continue
        input_value = r.get("salvageFee", 0)
        for inp in r["inputs"]:
            if inp["itemId"] not in items:
                err(f"recipe {r['id']}: unknown input item {inp['itemId']}")
                continue
            input_value += items[inp["itemId"]]["purchasePriceSalvage"] * inp["quantity"]
        # QA case 17: sell (40% floor) must never exceed input purchase value + fee.
        sell_value = int(items[out["itemId"]]["purchasePriceSalvage"] * 0.40) * out["quantity"]
        if sell_value > input_value:
            err(f"recipe {r['id']}: arbitrage — sell value {sell_value} > inputs {input_value}")
    for iid, item in items.items():
        if item["unitMassKg"] < 0:
            err(f"item {iid}: negative mass")
        if item["maxStack"] < 1:
            err(f"item {iid}: invalid stack size")
        if item["protectedLedger"] and item["sellable"]:
            err(f"item {iid}: protected ledger items cannot be sellable")


def check_caches_contracts():
    bands = {"small": (60, 100), "standard": (120, 180), "remote": (200, 300)}
    for c in load("caches.json"):
        low, high = bands[c["kind"]]
        if not (low <= c["salvageUnits"] <= high):
            err(f"cache {c['id']}: payout {c['salvageUnits']} outside {c['kind']} band {low}-{high}")
        if not c.get("oneTime"):
            err(f"cache {c['id']} must be one-time")

    base = {"Triangulate": 250, "GentleDelivery": 300, "HiddenVista": 300,
            "InfrastructureCheck": 300, "FieldSample": 350, "OldRoute": 400}
    region_index = {"forest": 0, "canyon": 1, "city": 2, "wetlands": 3, "highlands": 4}
    for c in load("contracts.json"):
        if not c.get("oneTime"):
            continue
        expected = base[c["pattern"]] + 100 * c["regionIndex"]
        if c["pattern"] == "GentleDelivery":
            expected += 100
        if c["rewardSalvage"] != expected:
            err(f"contract {c['id']}: reward {c['rewardSalvage']} != formula {expected}")
        if c["regionIndex"] != region_index[c["biome"]]:
            err(f"contract {c['id']}: wrong region index")


def check_surfaces_vehicles():
    for s in load("surfaces.json"):
        expected = SURFACE_TABLE.get(s["id"])
        if expected is None:
            err(f"surface {s['id']}: not in §4.4 table")
            continue
        g, w, c = expected
        if abs(s["dryGrip"] - g) > 1e-6 or abs(s["wetMultiplier"] - w) > 1e-6 or abs(s["rollingResistance"] - c) > 1e-6:
            err(f"surface {s['id']}: coefficients ({s['dryGrip']},{s['wetMultiplier']},{s['rollingResistance']}) "
                f"!= §4.4 ({g},{w},{c})")

    vehicles = {v["id"]: v for v in load("vehicles.json")}
    scout = vehicles.get("vehicle.scout.s1")
    if scout:
        for key, expected in [("dryMassKg", 1350), ("payloadKg", 120), ("fuelCapacityLiters", 24),
                              ("peakTorqueNm", 240), ("cargoSlots", 12), ("wheelbaseM", 2.65),
                              ("trackM", 1.58), ("wheelRadiusM", 0.36), ("groundClearanceM", 0.24),
                              ("comHeightM", 0.62), ("finalDrive", 4.10), ("governedSpeedKph", 70),
                              ("suspensionTravelM", 0.28), ("antiRollCouplingNm", 4000)]:
            if abs(scout[key] - expected) > 1e-6:
                err(f"scout {key}: {scout[key]} != §4.2 value {expected}")
        if scout["forwardGears"] != [3.60, 2.10, 1.35, 1.00]:
            err("scout gear ratios != §4.2")
        if scout["torqueCurve"] != [[800, 110], [1500, 190], [2500, 240], [3500, 225], [4500, 185], [5200, 140]]:
            err("scout torque curve != §4.3")
    for vid, mass, payload, tank, torque, speed in [
            ("vehicle.mule.m2", 1800, 260, 34, 290, 55),
            ("vehicle.lynx.l3", 1100, 90, 22, 225, 75),
            ("vehicle.atlas.a4", 1650, 180, 38, 320, 65)]:
        v = vehicles.get(vid)
        if v is None:
            err(f"vehicle {vid} missing")
            continue
        for key, expected in [("dryMassKg", mass), ("payloadKg", payload),
                              ("fuelCapacityLiters", tank), ("peakTorqueNm", torque),
                              ("governedSpeedKph", speed)]:
            if abs(v[key] - expected) > 1e-6:
                err(f"{vid} {key}: {v[key]} != §10.1 value {expected}")


def check_dependency_scan():
    """PROD-01: no ad/IAP/analytics/tracking/multiplayer dependency may appear."""
    manifest_path = os.path.join(ROOT, "Packages", "manifest.json")
    with open(manifest_path, encoding="utf-8") as f:
        text = f.read().lower()
    for token in BANNED_PACKAGE_TOKENS:
        if token in text:
            err(f"PROD-01 violation: banned token '{token}' found in Packages/manifest.json")
    # Also scan C# sources for hidden network/telemetry endpoints.
    script_root = os.path.join(ROOT, "Assets", "Wayfinder", "Scripts")
    suspicious = re.compile(r"https?://(?!docs\.unity3d\.com|unity\.com|github\.com)[^\s\"']+")
    for dirpath, _, files in os.walk(script_root):
        for name in files:
            if not name.endswith(".cs"):
                continue
            with open(os.path.join(dirpath, name), encoding="utf-8") as f:
                for i, line in enumerate(f, 1):
                    for match in suspicious.findall(line):
                        err(f"PROD-01: external URL '{match}' in {name}:{i} — remote endpoints are forbidden")


def main():
    check_counts()
    check_upgrades()
    check_missions()
    check_items_recipes()
    check_caches_contracts()
    check_surfaces_vehicles()
    check_dependency_scan()

    print(f"validate_content: {len(errors)} error(s), {len(warnings)} warning(s)")
    for e in errors:
        print("  ERROR:", e)
    for w in warnings:
        print("  warn: ", w)
    if not errors:
        print("CONTENT VALIDATION PASSED")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
