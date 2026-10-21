#!/usr/bin/env python3
"""Wayfinder content validation (headless).

**This file is the authoritative content validator.** It runs in CI on every push. The in-editor
mirror lives in `Assets/Wayfinder/Scripts/Editor/ContentImporter.cs` (class `ContentValidators`,
inside that file — there is no `Editor/ContentValidators.cs`; an earlier version of this docstring
pointed at one, and the drift went unnoticed because nothing checked the path (DEF-016)). Keep the
two in step: the Python side is what gates a merge.

Exit code 0 = pass, 1 = validation errors.

Checks (§16.6, CONTENT-01, UPG-01, QUEST-01, ITEM-01, PROD-01):
  * exact release-scope content counts and per-biome splits
  * unique stable IDs everywhere
  * upgrade table: sequential tiers, exact incremental costs, Σ = 32,200, story gates exist
  * mission table: Q01–Q20, rewards match §9.2 exactly, prerequisite chain acyclic
  * recipe inputs exist; §10.4's crafted-good rule (sell < input purchase value, fee excluded)
    and QA case 17 (sell <= inputs + fee), so crafting+resale can never create currency.
    Note: the runtime tables are hardcoded in C# (ItemCatalog, CraftingService.Recipes) and
    CampaignContent loads neither file, so EconomyArbitrageTests pins this file to the code.
  * cache payouts inside authored bands; contract rewards follow §9.3 formula
  * surface table matches §4.4 exactly; vehicle table matches §4.2/§10.1
  * Packages/manifest.json contains no ad / IAP / analytics / tracking SDK (PROD-01)
  * vertical_slice.json (§21.3): 1024 m area, 6 landmarks, 12 caches, Q01–Q04, 1 garage,
    2 camp sockets, tiers 1–6; every id resolves, every site lies inside the area, every slice
    mission objective has a reachable site, and the slice economy affords tiers 1–6 plus a
    crafted second camp kit without mapping income (mirrors CampaignFieldTests/CampaignSessionTests)
"""

import math

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

# §10.2 equipment slots: named slots hold one module each, utility holds two, and "none" is a
# research unlock (a tire set) rather than a module that competes for a slot.
UPGRADE_SLOTS = ["tires", "utility", "winch", "cargoRack", "suspension", "tank", "utility", "none",
                 "sensor", "engineTune", "cargoRack", "intake", "differential", "suspension", "winch",
                 "tank", "cargoRack", "none", "sensor", "engineTune"]

# Spelled exactly as Wayfinder.Simulation.EquipmentSlots spells them (utility is one kind, two slots).
MODULE_SLOT_NAMES = {"tires", "suspension", "engineTune", "tank", "intake", "differential", "cargoRack",
                     "sensor", "winch", "utility", "none"}

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
        # §10.2: every tier belongs to exactly one slot, spelled the way the code spells it.
        if u.get("moduleSlot") != UPGRADE_SLOTS[i - 1]:
            err(f"upgrade {u['id']}: moduleSlot {u.get('moduleSlot')!r} != table value "
                f"{UPGRADE_SLOTS[i-1]!r}")
        if i > 1 and u["prerequisites"] != [upgrades[i - 2]["id"]]:
            err(f"upgrade {u['id']}: prerequisite must be previous tier (sequential tree)")

    # Slot vocabulary, the two utility slots, and one unlock per specialized tire set — without all
    # three a tire set is unreachable in every save (the rock set was, before this batch).
    counts = {}
    flags = set()
    for u in upgrades:
        slot = u.get("moduleSlot")
        if slot not in MODULE_SLOT_NAMES:
            err(f"upgrade {u['id']}: moduleSlot {slot!r} is not a §10.2 slot")
        counts[slot] = counts.get(slot, 0) + 1
        for e in u["effects"]:
            flags.add(e["stat"])
    if counts.get("utility", 0) != 2:
        err(f"§10.2 provides two utility slots but {counts.get('utility', 0)} tiers use one")
    for flag in ("mudTires", "rockTires", "snowTires"):
        if flag not in flags:
            err(f"no tier sets '{flag}', so that tire set can never be mounted (§10.2)")

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
        material_value = 0
        for inp in r["inputs"]:
            if inp["itemId"] not in items:
                err(f"recipe {r['id']}: unknown input item {inp['itemId']}")
                continue
            material_value += items[inp["itemId"]]["purchasePriceSalvage"] * inp["quantity"]
        fee = r.get("salvageFee", 0)
        sell_value = int(items[out["itemId"]]["purchasePriceSalvage"] * 0.40) * out["quantity"]
        # §10.4: "Crafted consumables sell for less than their total input purchase value." The fee is
        # not part of that sentence, so the strict rule is checked on materials alone...
        if sell_value >= material_value:
            err(f"recipe {r['id']}: §10.4 — sell value {sell_value} must be below the input purchase "
                f"value {material_value} (the crafting fee does not count toward it)")
        # ...and QA case 17 ("crafting/reselling must never create currency") on materials + fee.
        if sell_value > material_value + fee:
            err(f"recipe {r['id']}: arbitrage — sell value {sell_value} > inputs + fee {material_value + fee}")
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


SLICE_OBJECTIVE_SITES = {
    "DiscoverLandmark": lambda sl, t, n: t in [l["id"] for l in sl["landmarks"]],
    "InspectObject": lambda sl, t, n: t in [i["id"] for i in sl.get("inspectTargets", [])],
    "TraverseCheckpoints": lambda sl, t, n: any(r["id"] == t and len(r["checkpoints"]) >= n
                                                for r in sl.get("checkpointRoutes", [])),
    "EstablishCamp": lambda sl, t, n: t in [c["id"] for c in sl.get("campSockets", [])],
    "ActivateRelay": lambda sl, t, n: t in [r["id"] for r in sl.get("relays", [])],
    "CollectQuestItem": lambda sl, t, n: t == "salvage_bundle" or t in [q["itemId"] for q in sl.get("questItemSites", [])],
    "ReturnToHub": lambda sl, t, n: t == sl["garage"]["id"],
}


def check_vertical_slice():
    """§21.3 vertical-slice manifest — the C# twin is CampaignFieldTests.SliceManifest_MatchesBlueprintScope."""
    sl = load("vertical_slice.json")
    landmarks = {l["id"]: l for l in load("landmarks.json")}
    caches = {c["id"]: c for c in load("caches.json")}
    camps = {c["id"]: c for c in load("camps.json")}
    garages = {g["id"]: g for g in load("garages.json")}
    missions = {m["displayCode"]: m for m in load("missions.json")}
    upgrades = sorted(load("upgrades.json"), key=lambda u: u["tier"])
    recipes = {r["id"]: r for r in load("recipes.json")}

    area = sl["area"]
    size = area["sizeMeters"]

    def inside(p, what):
        if not (area["minX"] <= p["x"] < area["minX"] + size and area["minZ"] <= p["z"] < area["minZ"] + size):
            err(f"slice: {what} at ({p['x']}, {p['z']}) lies outside the {size} m area")

    def dist(a, b):
        return math.hypot(a["x"] - b["x"], a["z"] - b["z"])

    if size != 1024:
        err(f"slice: area must be 1024 m (found {size})")
    if len(sl["landmarks"]) != 6:
        err(f"slice: expected 6 landmarks, found {len(sl['landmarks'])}")
    if len(sl["caches"]) != 12 or len(set(sl["caches"])) != 12:
        err(f"slice: expected 12 distinct caches, found {len(sl['caches'])}")
    if len(sl.get("campSockets", [])) != 2:
        err("slice: expected 2 camp sockets")
    if sl["missions"] != ["Q01", "Q02", "Q03", "Q04"]:
        err(f"slice: missions must be Q01–Q04 (found {sl['missions']})")
    if sl.get("researchTierCount") != 6:
        err("slice: researchTierCount must be 6")

    garage = sl["garage"]
    if garage["id"] not in garages:
        err(f"slice: unknown garage {garage['id']}")
    inside(garage["spawn"], "garage spawn")
    if dist(garage["spawn"], garage["arrivalCenter"]) > garage["arrivalRadiusMeters"]:
        err("slice: garage spawn must lie inside the arrival volume")

    for l in sl["landmarks"]:
        if l["id"] not in landmarks:
            err(f"slice: unknown landmark {l['id']}")
            continue
        inside(landmarks[l["id"]]["positionHint"], l["id"])
    positions = []
    for cid in sl["caches"]:
        if cid not in caches:
            err(f"slice: unknown cache {cid}")
            continue
        p = caches[cid]["positionHint"]
        inside(p, cid)
        if dist(p, garage["arrivalCenter"]) < 60:
            err(f"slice: {cid} sits inside the garage yard (< 60 m from the arrival centre)")
        for other_id, other in positions:
            if dist(p, other) < 10:
                err(f"slice: {cid} and {other_id} are closer than 10 m")
        positions.append((cid, p))
    for c in sl.get("campSockets", []):
        if c["id"] not in camps:
            err(f"slice: unknown camp socket {c['id']}")
        inside(c["position"], c["id"])
    for key in ("inspectTargets", "questItemSites", "relays"):
        for site in sl.get(key, []):
            inside(site["position"], site["id"])
    for route in sl.get("checkpointRoutes", []):
        for i, cp in enumerate(route["checkpoints"]):
            inside(cp, f"{route['id']}[{i}]")
    for feature in sl.get("greyboxFeatures", []):
        inside(feature["center"], f"greybox {feature['kind']}")

    for code in sl["missions"]:
        m = missions.get(code)
        if m is None:
            err(f"slice: unknown mission {code}")
            continue
        for o in m["objectives"]:
            check = SLICE_OBJECTIVE_SITES.get(o["type"])
            if check is None or not check(sl, o["targetId"], o["count"]):
                err(f"slice: {code} objective {o['type']} {o['targetId']} has no reachable site")

    tiers = upgrades[:sl.get("researchTierCount", 0)]
    for t in tiers:
        gate = t.get("requiredMission") or ""
        if gate and gate not in sl["missions"]:
            err(f"slice: tier {t['tier']} gated by {gate}, which is not in the slice")

    # Economy: affordable WITHOUT mapping income (mapping only adds margin).
    income = sum(caches[c]["salvageUnits"] for c in sl["caches"] if c in caches)
    income += sum(landmarks[l["id"]].get("discoveryRewardSalvage", 120) for l in sl["landmarks"] if l["id"] in landmarks)
    income += sum(missions[c]["reward"]["salvage"] for c in sl["missions"] if c in missions)
    camp_kit = recipes["recipe.camp_kit"]
    spend = sum(t["costSalvage"] for t in tiers) + camp_kit.get("salvageFee", 0)
    if income < spend:
        err(f"slice economy dead-end: income {income} < tiers 1–6 + camp kit {spend}")
    materials = {}
    for cid in sl["caches"]:
        for m in caches.get(cid, {}).get("materials", []):
            materials[m["itemId"]] = materials.get(m["itemId"], 0) + m["quantity"]
    for inp in camp_kit["inputs"]:
        if materials.get(inp["itemId"], 0) < inp["quantity"]:
            err(f"slice: second camp kit needs {inp['quantity']} {inp['itemId']}, caches give {materials.get(inp['itemId'], 0)}")
    print(f"vertical slice: income without mapping {income} vs spend {spend} (margin {income - spend}); "
          f"materials {materials}")


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


def check_objective_reachability():
    """Every mission objective target must resolve to content the game can place (DEF-003/DEF-016).

    Missions Q05-Q20 were authored against sites (`relay.canyon`, `junction.city.01`, …) that no
    catalog defined, and nothing checked, so 16 of 20 missions were uncompletable while the status
    document claimed M4 complete. Those sites now live in `sites.json`; this rule is what stops the
    next mission from being authored the same way.
    """
    known = set()
    for name in ("landmarks.json", "caches.json", "garages.json", "camps.json", "items.json",
                 "sites.json", "contracts.json"):
        for row in load(name):
            if row.get("id"):
                known.add(row["id"])

    slice_ = load("vertical_slice.json")
    for key in ("campSockets", "inspectTargets", "questItemSites", "relays", "checkpointRoutes", "landmarks"):
        for row in slice_.get(key, []):
            if row.get("id"):
                known.add(row["id"])
    for cache_id in slice_.get("caches", []):
        known.add(cache_id)

    for mission in load("missions.json"):
        code = mission.get("displayCode", mission.get("id"))
        for objective in mission.get("objectives", []):
            target = objective.get("targetId")
            kind = objective.get("type")
            # RevealMapPercent carries a percentage, not a place.
            if not target or kind == "RevealMapPercent" or target == "map":
                continue
            if target not in known:
                err(f"mission {code}: objective {kind} targets '{target}', which no catalog defines")

    # Every site outside the slice must be placeable: inside the world and with a usable radius.
    for site in load("sites.json"):
        pos = site.get("positionHint") or site.get("position") or {}
        x, z = pos.get("x"), pos.get("z")
        if x is None or z is None:
            err(f"site {site['id']}: no position")
            continue
        if not (0 <= x < 6144 and 0 <= z < 6144):
            err(f"site {site['id']}: position ({x}, {z}) is outside the 6144 m world")
        if (site.get("radiusMeters") or 0) <= 0:
            err(f"site {site['id']}: radiusMeters must be positive")
        if site.get("kind") == "route":
            checkpoints = site.get("checkpoints") or []
            if len(checkpoints) < 2:
                err(f"site {site['id']}: a checkpoint route needs at least two checkpoints")
        if site.get("kind") not in ("inspect", "relay", "questItem", "delivery", "visit", "route", "campSocket"):
            err(f"site {site['id']}: unknown kind '{site.get('kind')}'")


def main():
    check_counts()
    check_objective_reachability()
    check_upgrades()
    check_missions()
    check_items_recipes()
    check_caches_contracts()
    check_surfaces_vehicles()
    check_vertical_slice()
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
