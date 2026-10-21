#!/usr/bin/env python3
"""
Wayfinder continental world layout & route network generator (KI-027, KI-029, KI-030, KI-047, KI-048).

Deterministically authors validated world coordinates across the full 6,144 m x 6,144 m mainland for:
  - Content/routes.json      (full 5-biome MainlandRouteGraph: trunks, loops, dual-water wetlands, canyon bridge)
  - Content/landmarks.json   (all 100 landmarks; 25 scenic vistas; slice 01..06 preserved byte-for-byte in XZ)
  - Content/caches.json      (all 200 caches; exact salvage & material rolls preserved; slice 12 preserved)
  - Content/camps.json       (all 10 field camps across 5 biomes; slice forest.a/b matched to slice sockets)
  - Content/relics.json      (all 25 relics, 5 per biome, validated mainland coordinates)
  - Content/garages.json     (ground Y synced to continental heightfield; XZ preserved)
  - Content/sites.json       (all mission sites, 5 relays, fuses, deliveries, observatory, routes & contract sockets)
"""
from __future__ import annotations

import json
import math
import sys
from collections import deque
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "Tools" / "Validation"))
from formula_checks import base_height as _base_height, hash01 as _hash01  # noqa: E402

CONTENT = REPO / "Assets" / "Wayfinder" / "Content"

WORLD_SIZE = 6144.0
WORLD_SEED = 481516
CELL_SIZE = 64.0
GRID_N = 96
CLIFF_SLOPE_DEG = 38.0
REVEAL_VEHICLE_M = 96.0
REVEAL_VISTA_M = 280.0


def hash_u(x: int, z: int, seed: int) -> float:
    return _hash01(seed, x, z)


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def clamp01(t: float) -> float:
    return 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)


def height_at(x: float, z: float, seed: int = WORLD_SEED) -> float:
    return round(_base_height(x, z, seed, 1) * 1000.0) * 0.001


def slope_deg(x: float, z: float, step: float = 8.0) -> float:
    x0 = max(0.0, x - step)
    x1 = min(WORLD_SIZE, x + step)
    z0 = max(0.0, z - step)
    z1 = min(WORLD_SIZE, z + step)
    dhdx = (height_at(x1, z) - height_at(x0, z)) / max(1e-3, x1 - x0)
    dhdz = (height_at(x, z1) - height_at(x, z0)) / max(1e-3, z1 - z0)
    return math.degrees(math.atan(math.sqrt(dhdx * dhdx + dhdz * dhdz)))


def build_countable_mask() -> set[tuple[int, int]]:
    passable = set()
    for cz in range(GRID_N):
        for cx in range(GRID_N):
            wx, wz = cx * CELL_SIZE + 32.0, cz * CELL_SIZE + 32.0
            if height_at(wx, wz) > 0.0 and slope_deg(wx, wz, 8.0) < CLIFF_SLOPE_DEG:
                passable.add((cx, cz))
    seed_cell = (int(1408 // 64), int(2816 // 64))
    assert seed_cell in passable
    seen = {seed_cell}
    q = deque([seed_cell])
    while q:
        x, z = q.popleft()
        for nx, nz in ((x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1)):
            if (nx, nz) in passable and (nx, nz) not in seen:
                seen.add((nx, nz))
                q.append((nx, nz))
    return seen


def in_slice(x: float, z: float) -> bool:
    return 512.0 <= x < 1536.0 and 1792.0 <= z < 2816.0


def pt(x: float, z: float) -> dict:
    rx = round(float(x), 1)
    rz = round(float(z), 1)
    return {"x": rx, "y": round(height_at(rx, rz), 2), "z": rz}


def snap_safe(x: float, z: float, countable: set[tuple[int, int]], avoid_slice: bool = False) -> tuple[float, float]:
    best = None
    for r in [0, 32, 64, 96, 140, 190, 250, 320, 400, 500, 620, 760]:
        steps = 1 if r == 0 else 16
        for k in range(steps):
            ang = 2.0 * math.pi * k / steps
            px = min(WORLD_SIZE - 96.0, max(96.0, x + r * math.cos(ang)))
            pz = min(WORLD_SIZE - 96.0, max(96.0, z + r * math.sin(ang)))
            if avoid_slice and in_slice(px, pz):
                continue
            cell = (int(px // 64.0), int(pz // 64.0))
            if cell not in countable:
                continue
            h = height_at(px, pz)
            s = slope_deg(px, pz)
            if h >= 5.5 and s <= 17.5:
                return round(px, 1), round(pz, 1)
            if best is None and h > 1.5 and s < 30.0:
                best = (round(px, 1), round(pz, 1))
    assert best is not None, f"Could not snap ({x}, {z})"
    return best


def main() -> None:
    countable = build_countable_mask()
    print(f"[world] Countable mainland cells: {len(countable)} / {GRID_N * GRID_N}")

    slice_data = json.loads((CONTENT / "vertical_slice.json").read_text())
    slice_cache_ids = set(slice_data["caches"])
    slice_landmark_ids = {l["id"] for l in slice_data["landmarks"]}
    slice_vista_ids = {l["id"] for l in slice_data["landmarks"] if l.get("vista")}

    garages = json.loads((CONTENT / "garages.json").read_text())
    for g in garages:
        gx, gz = g["positionHint"]["x"], g["positionHint"]["z"]
        g["positionHint"] = pt(gx, gz)
        g["validated"] = True
    (CONTENT / "garages.json").write_text(json.dumps(garages, indent=2) + "\n")

    camp_coords = {
        "camp.forest.a": (1240.0, 2520.0),
        "camp.forest.b": (700.0, 1960.0),
        "camp.canyon.a": snap_safe(3720.0, 4120.0, countable, True),
        "camp.canyon.b": snap_safe(5320.0, 4980.0, countable, True),
        "camp.city.a": snap_safe(3880.0, 2720.0, countable, True),
        "camp.city.b": snap_safe(5180.0, 1860.0, countable, True),
        "camp.wetlands.a": snap_safe(2420.0, 1180.0, countable, True),
        "camp.wetlands.b": snap_safe(4120.0, 1120.0, countable, True),
        "camp.highlands.a": snap_safe(1260.0, 4220.0, countable, True),
        "camp.highlands.b": snap_safe(2560.0, 5260.0, countable, True),
    }
    camps = json.loads((CONTENT / "camps.json").read_text())
    for c in camps:
        cx, cz = camp_coords[c["id"]]
        c["positionHint"] = pt(cx, cz)
        c["validated"] = True
    (CONTENT / "camps.json").write_text(json.dumps(camps, indent=2) + "\n")

    biome_boxes = {
        "forest": (560.0, 2950.0, 1650.0, 3650.0),
        "wetlands": (820.0, 4350.0, 560.0, 1680.0),
        "city": (3250.0, 5620.0, 1450.0, 3380.0),
        "canyon": (3150.0, 5650.0, 3420.0, 5580.0),
        "highlands": (680.0, 3220.0, 3680.0, 5660.0),
    }

    def spread_points(biome: str, count: int, phase: int) -> list[tuple[float, float]]:
        x0, x1, z0, z1 = biome_boxes[biome]
        cols = max(2, round(math.sqrt(count * (x1 - x0) / max(1.0, z1 - z0))))
        rows = math.ceil(count / cols)
        pts = []
        for r in range(rows):
            for c in range(cols):
                if len(pts) >= count:
                    break
                jx = (hash_u(c + phase, r * 7 + 3, WORLD_SEED ^ 0x1234) - 0.5) * 0.55
                jz = (hash_u(c * 5 + 11, r + phase, WORLD_SEED ^ 0x5678) - 0.5) * 0.55
                fx = (c + 0.5 + jx) / cols
                fz = (r + 0.5 + jz) / rows
                tx = lerp(x0, x1, clamp01(fx))
                tz = lerp(z0, z1, clamp01(fz))
                sx, sz = snap_safe(tx, tz, countable, avoid_slice=True)
                pts.append((sx, sz))
        return pts

    landmarks = json.loads((CONTENT / "landmarks.json").read_text())
    by_biome_lm: dict[str, list[dict]] = {}
    for l in landmarks:
        by_biome_lm.setdefault(l["biome"], []).append(l)

    for biome, items in by_biome_lm.items():
        non_slice = [l for l in items if l["id"] not in slice_landmark_ids]
        coords = spread_points(biome, len(non_slice), phase=17 + len(biome))
        existing_vistas = 2 if biome == "forest" else 0
        vista_budget = 5 - existing_vistas
        vista_indices = (
            {round(i * (len(non_slice) - 1) / max(1, vista_budget - 1)) for i in range(vista_budget)}
            if vista_budget > 0
            else set()
        )
        for i, lm in enumerate(non_slice):
            px, pz = coords[i]
            lm["positionHint"] = pt(px, pz)
            lm["vista"] = i in vista_indices
            lm["validated"] = True
        for lm in items:
            if lm["id"] in slice_landmark_ids:
                lx, lz = lm["positionHint"]["x"], lm["positionHint"]["z"]
                lm["positionHint"] = pt(lx, lz)
                lm["vista"] = lm["id"] in slice_vista_ids
                lm["validated"] = True
    (CONTENT / "landmarks.json").write_text(json.dumps(landmarks, indent=2) + "\n")

    caches = json.loads((CONTENT / "caches.json").read_text())
    by_biome_ca: dict[str, list[dict]] = {}
    for c in caches:
        by_biome_ca.setdefault(c["biome"], []).append(c)
    for biome, items in by_biome_ca.items():
        non_slice = [c for c in items if c["id"] not in slice_cache_ids]
        coords = spread_points(biome, len(non_slice), phase=91 + len(biome))
        for i, ca in enumerate(non_slice):
            px, pz = coords[i]
            ca["positionHint"] = pt(px, pz)
            ca["validated"] = True
        for ca in items:
            if ca["id"] in slice_cache_ids:
                cx, cz = ca["positionHint"]["x"], ca["positionHint"]["z"]
                ca["positionHint"] = pt(cx, cz)
                ca["validated"] = True
    (CONTENT / "caches.json").write_text(json.dumps(caches, indent=2) + "\n")

    relics = json.loads((CONTENT / "relics.json").read_text())
    by_biome_re: dict[str, list[dict]] = {}
    for r in relics:
        by_biome_re.setdefault(r["biome"], []).append(r)
    for biome, items in by_biome_re.items():
        coords = spread_points(biome, len(items), phase=211 + len(biome))
        for i, re_item in enumerate(items):
            px, pz = coords[i]
            re_item["positionHint"] = pt(px, pz)
            re_item["validated"] = True
    (CONTENT / "relics.json").write_text(json.dumps(relics, indent=2) + "\n")

    contracts = json.loads((CONTENT / "contracts.json").read_text())

    def sp(x: float, z: float) -> dict:
        sx, sz = snap_safe(x, z, countable, avoid_slice=True)
        return pt(sx, sz)

    def polyline(points: list[tuple[float, float]], step_m: float = 96.0) -> list[dict]:
        out: list[dict] = []
        for i, (ax, az) in enumerate(points):
            if i == 0:
                out.append(pt(ax, az))
                continue
            bx, bz = points[i - 1]
            dist = math.hypot(ax - bx, az - bz)
            n = max(1, math.ceil(dist / step_m))
            for k in range(1, n + 1):
                t = k / n
                out.append(pt(lerp(bx, ax, t), lerp(bz, az, t)))
        return out

    def region_road_loop(biome: str) -> list[dict]:
        x0, x1, z0, z1 = biome_boxes[biome]
        waypoints_2d: list[tuple[float, float]] = []
        bands = 7
        for b in range(bands):
            tz = lerp(z0 + 60.0, z1 - 60.0, b / (bands - 1))
            xs = [lerp(x0 + 60.0, x1 - 60.0, k / 6) for k in range(7)]
            if b % 2 == 1:
                xs.reverse()
            for tx in xs:
                sx, sz = snap_safe(tx, tz, countable, avoid_slice=(biome == "forest"))
                waypoints_2d.append((sx, sz))
        return polyline(waypoints_2d, step_m=96.0)

    sites: list[dict] = [
        # --- Forest ---
        {
            "id": "relay.forest",
            "kind": "relay",
            "biome": "forest",
            "positionHint": pt(1420.0, 2700.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q04", "Q20"],
            "itemId": "relay_fuse",
            "validated": True,
        },
        {
            "id": "questsite.forest.relay_fuse",
            "kind": "questItem",
            "biome": "forest",
            "itemId": "relay_fuse",
            "positionHint": pt(1320.0, 1980.0),
            "radiusMeters": 10.0,
            "missionCodes": ["Q04"],
            "validated": True,
        },
        {
            "id": "route.forest.ford",
            "kind": "route",
            "biome": "forest",
            "positionHint": pt(985.0, 2350.0),
            "radiusMeters": 12.0,
            "checkpoints": [pt(930.0, 2350.0), pt(1040.0, 2350.0)],
            "missionCodes": ["Q02"],
            "validated": True,
        },
        {
            "id": "route.forest.scenic_loop",
            "kind": "route",
            "biome": "forest",
            "positionHint": sp(2150.0, 2920.0),
            "radiusMeters": 18.0,
            "checkpoints": region_road_loop("forest"),
            "missionCodes": [],
            "validated": True,
        },
        # --- Arid Canyon (Q05 - Q08) ---
        {
            "id": "site.canyon.washed_out",
            "kind": "inspect",
            "biome": "canyon",
            "positionHint": sp(4360.0, 4180.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q05"],
            "validated": True,
        },
        {
            "id": "marker.canyon.01",
            "kind": "inspect",
            "biome": "canyon",
            "positionHint": sp(5084.0, 4608.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q06"],
            "validated": True,
        },
        {
            "id": "marker.canyon.02",
            "kind": "inspect",
            "biome": "canyon",
            "positionHint": sp(4605.9, 4844.4),
            "radiusMeters": 14.0,
            "missionCodes": ["Q06"],
            "validated": True,
        },
        {
            "id": "marker.canyon.03",
            "kind": "inspect",
            "biome": "canyon",
            "positionHint": sp(4906.0, 4129.8),
            "radiusMeters": 14.0,
            "missionCodes": ["Q06"],
            "validated": True,
        },
        {
            "id": "questsite.canyon.brace_manifest",
            "kind": "questItem",
            "biome": "canyon",
            "itemId": "quest.brace_manifest",
            "positionHint": sp(4280.0, 4260.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q07"],
            "validated": True,
        },
        {
            "id": "site.canyon.bridge",
            "kind": "delivery",
            "biome": "canyon",
            "itemId": "quest.brace_manifest",
            "unlocksInfrastructure": "site.canyon.bridge",
            "positionHint": pt(4420.0, 4608.0),
            "radiusMeters": 18.0,
            "missionCodes": ["Q07"],
            "validated": True,
        },
        {
            "id": "questsite.canyon.relay_fuse",
            "kind": "questItem",
            "biome": "canyon",
            "itemId": "relay_fuse",
            "positionHint": sp(5080.0, 4920.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q08"],
            "validated": True,
        },
        {
            "id": "relay.canyon",
            "kind": "relay",
            "biome": "canyon",
            "positionHint": pt(4864.0, 4608.0),
            "radiusMeters": 15.0,
            "missionCodes": ["Q08", "Q20"],
            "itemId": "relay_fuse",
            "validated": True,
        },
        {
            "id": "route.canyon.loop",
            "kind": "route",
            "biome": "canyon",
            "positionHint": sp(4200.0, 4900.0),
            "radiusMeters": 18.0,
            "checkpoints": region_road_loop("canyon"),
            "missionCodes": [],
            "validated": True,
        },
        # --- Overgrown City (Q09 - Q12) ---
        {
            "id": "junction.city.01",
            "kind": "inspect",
            "biome": "city",
            "positionHint": sp(4828.0, 2304.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q09"],
            "validated": True,
        },
        {
            "id": "junction.city.02",
            "kind": "inspect",
            "biome": "city",
            "positionHint": sp(4349.9, 2540.4),
            "radiusMeters": 14.0,
            "missionCodes": ["Q09"],
            "validated": True,
        },
        {
            "id": "junction.city.03",
            "kind": "inspect",
            "biome": "city",
            "positionHint": sp(4650.0, 1825.8),
            "radiusMeters": 14.0,
            "missionCodes": ["Q09"],
            "validated": True,
        },
        {
            "id": "questsite.city.archive_case",
            "kind": "questItem",
            "biome": "city",
            "itemId": "quest.archive_case",
            "positionHint": sp(4220.0, 2560.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q10"],
            "validated": True,
        },
        {
            "id": "delivery.city.cable_spool",
            "kind": "delivery",
            "biome": "city",
            "itemId": "quest.cable_spool",
            "positionHint": sp(4940.0, 2680.0),
            "radiusMeters": 16.0,
            "missionCodes": ["Q11"],
            "validated": True,
        },
        {
            "id": "questsite.city.relay_fuse",
            "kind": "questItem",
            "biome": "city",
            "itemId": "relay_fuse",
            "positionHint": sp(5120.0, 2140.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q12"],
            "validated": True,
        },
        {
            "id": "relay.city",
            "kind": "relay",
            "biome": "city",
            "positionHint": pt(4608.0, 2304.0),
            "radiusMeters": 15.0,
            "missionCodes": ["Q12", "Q20"],
            "itemId": "relay_fuse",
            "validated": True,
        },
        {
            "id": "route.city.ring",
            "kind": "route",
            "biome": "city",
            "positionHint": sp(4608.0, 2850.0),
            "radiusMeters": 18.0,
            "checkpoints": region_road_loop("city"),
            "missionCodes": [],
            "validated": True,
        },
        # --- Coastal Wetlands (Q13 - Q16) ---
        {
            "id": "pole.wetlands.01",
            "kind": "inspect",
            "biome": "wetlands",
            "positionHint": sp(3069.9, 1004.4),
            "radiusMeters": 14.0,
            "missionCodes": ["Q13"],
            "validated": True,
        },
        {
            "id": "pole.wetlands.02",
            "kind": "inspect",
            "biome": "wetlands",
            "positionHint": sp(3370.0, 680.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q13"],
            "validated": True,
        },
        {
            "id": "pole.wetlands.03",
            "kind": "inspect",
            "biome": "wetlands",
            "positionHint": sp(3699.1, 1252.1),
            "radiusMeters": 14.0,
            "missionCodes": ["Q13"],
            "validated": True,
        },
        {
            "id": "route.wetlands.crossing",
            "kind": "route",
            "biome": "wetlands",
            "positionHint": sp(3538.9, 760.0),
            "radiusMeters": 25.0,
            "checkpoints": [sp(3538.9, 760.0), sp(3690.8, 700.0)],
            "missionCodes": ["Q13"],
            "validated": True,
        },
        {
            "id": "site.wetlands.alt_causeway",
            "kind": "inspect",
            "biome": "wetlands",
            "positionHint": sp(2807.2, 820.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q14"],
            "validated": True,
        },
        {
            "id": "document.pump_diagram",
            "kind": "questItem",
            "biome": "wetlands",
            "itemId": "document.pump_diagram",
            "positionHint": sp(3548.0, 768.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q14"],
            "validated": True,
        },
        {
            "id": "delivery.wetlands.stabilizer_plates",
            "kind": "delivery",
            "biome": "wetlands",
            "itemId": "quest.stabilizer_plates",
            "positionHint": sp(3680.0, 1020.0),
            "radiusMeters": 16.0,
            "missionCodes": ["Q15"],
            "validated": True,
        },
        {
            "id": "route.wetlands.firm",
            "kind": "route",
            "biome": "wetlands",
            "positionHint": sp(3216.4, 1183.3),
            "radiusMeters": 25.0,
            "checkpoints": [sp(3263.1, 1009.4), sp(3216.4, 1183.3), sp(3169.6, 1357.1)],
            "missionCodes": ["Q15"],
            "validated": True,
        },
        {
            "id": "questsite.wetlands.relay_fuse",
            "kind": "questItem",
            "biome": "wetlands",
            "itemId": "relay_fuse",
            "positionHint": sp(3980.0, 880.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q16"],
            "validated": True,
        },
        {
            "id": "relay.wetlands",
            "kind": "relay",
            "biome": "wetlands",
            "positionHint": pt(3328.0, 768.0),
            "radiusMeters": 15.0,
            "missionCodes": ["Q16", "Q20"],
            "itemId": "relay_fuse",
            "validated": True,
        },
        {
            "id": "route.wetlands.scenic_loop",
            "kind": "route",
            "biome": "wetlands",
            "positionHint": sp(2560.0, 1140.0),
            "radiusMeters": 18.0,
            "checkpoints": region_road_loop("wetlands"),
            "missionCodes": [],
            "validated": True,
        },
        # --- Alpine Highlands & Frontier Observatory Finale (Q17 - Q20) ---
        {
            "id": "route.highlands.allweather",
            "kind": "route",
            "biome": "highlands",
            "positionHint": sp(1533.9, 5356.4),
            "radiusMeters": 25.0,
            "checkpoints": [sp(1607.7, 5288.9), sp(1474.9, 5410.5)],
            "missionCodes": ["Q17"],
            "validated": True,
        },
        {
            "id": "station.highlands.east",
            "kind": "inspect",
            "biome": "highlands",
            "positionHint": sp(2163.1, 5480.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q18"],
            "validated": True,
        },
        {
            "id": "station.highlands.west",
            "kind": "inspect",
            "biome": "highlands",
            "positionHint": sp(1063.3, 4991.1),
            "radiusMeters": 14.0,
            "missionCodes": ["Q18"],
            "validated": True,
        },
        {
            "id": "questsite.highlands.sensor_core",
            "kind": "questItem",
            "biome": "highlands",
            "itemId": "sensor_core",
            "positionHint": sp(1420.0, 4680.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q18"],
            "validated": True,
        },
        {
            "id": "delivery.highlands.sensor_core",
            "kind": "delivery",
            "biome": "highlands",
            "itemId": "sensor_core",
            "positionHint": sp(2160.0, 5280.0),
            "radiusMeters": 16.0,
            "missionCodes": ["Q19"],
            "validated": True,
        },
        {
            "id": "questsite.highlands.relay_fuse",
            "kind": "questItem",
            "biome": "highlands",
            "itemId": "relay_fuse",
            "positionHint": sp(1980.0, 5040.0),
            "radiusMeters": 14.0,
            "missionCodes": ["Q19"],
            "validated": True,
        },
        {
            "id": "relay.highlands",
            "kind": "relay",
            "biome": "highlands",
            "positionHint": pt(1792.0, 5120.0),
            "radiusMeters": 15.0,
            "missionCodes": ["Q19", "Q20"],
            "itemId": "relay_fuse",
            "validated": True,
        },
        {
            "id": "site.highlands.observatory",
            "kind": "visit",
            "biome": "highlands",
            "positionHint": pt(1680.0, 5420.0),
            "radiusMeters": 40.0,
            "missionCodes": ["Q20"],
            "validated": True,
        },
        {
            "id": "route.highlands.ascent",
            "kind": "route",
            "biome": "highlands",
            "positionHint": sp(1680.0, 4620.0),
            "radiusMeters": 18.0,
            "checkpoints": region_road_loop("highlands"),
            "missionCodes": [],
            "validated": True,
        },
    ]

    seen_site_ids = {s["id"] for s in sites}
    sockets_by_biome: dict[str, list[str]] = {}
    for c in contracts:
        targets = c.get("targets", [])
        if not isinstance(targets, list):
            continue
        for t in targets:
            if t not in seen_site_ids:
                seen_site_ids.add(t)
                sockets_by_biome.setdefault(c["biome"], []).append(t)

    for biome, target_ids in sockets_by_biome.items():
        coords = spread_points(biome, len(target_ids), phase=331 + len(biome))
        for i, tid in enumerate(target_ids):
            px, pz = coords[i]
            sites.append(
                {
                    "id": tid,
                    "kind": "inspect",
                    "biome": biome,
                    "positionHint": pt(px, pz),
                    "radiusMeters": 15.0,
                    "missionCodes": [],
                    "validated": True,
                }
            )

    (CONTENT / "sites.json").write_text(json.dumps(sites, indent=2) + "\n")

    def site_pos(sid: str) -> tuple[float, float]:
        for g in garages:
            if g["id"] == sid:
                return g["positionHint"]["x"], g["positionHint"]["z"]
        for c in camps:
            if c["id"] == sid:
                return c["positionHint"]["x"], c["positionHint"]["z"]
        for s in sites:
            if s["id"] == sid:
                return s["positionHint"]["x"], s["positionHint"]["z"]
        raise KeyError(sid)

    node_ids = [
        "garage.home", "camp.forest.a", "camp.forest.b", "relay.forest",
        "garage.canyon", "camp.canyon.a", "camp.canyon.b", "site.canyon.bridge", "relay.canyon",
        "garage.city", "camp.city.a", "camp.city.b", "relay.city",
        "garage.wetlands", "camp.wetlands.a", "camp.wetlands.b", "relay.wetlands",
        "garage.highlands", "camp.highlands.a", "camp.highlands.b", "relay.highlands",
        "site.highlands.observatory",
    ]
    route_nodes = [{"id": nid, "position": pt(*site_pos(nid))} for nid in node_ids]

    raw_edges = [
        ("edge.forest.home_camp_a", "garage.home", "camp.forest.a", "Primary", "Gentle", "", 0.15, []),
        ("edge.forest.home_camp_b", "garage.home", "camp.forest.b", "Secondary", "Gentle", "", 0.28, [(985.0, 2350.0)]),
        ("edge.forest.camp_a_relay", "camp.forest.a", "relay.forest", "Secondary", "Gentle", "", 0.0, []),
        ("edge.corridor.forest_canyon", "garage.home", "camp.canyon.a", "Primary", "Gentle", "", 0.10, [(2450.0, 3450.0), (3150.0, 3820.0)]),
        ("edge.canyon.detour", "camp.canyon.a", "garage.canyon", "Secondary", "Demanding", "", 0.18, [(3980.0, 3620.0), (4520.0, 3780.0), (5080.0, 4120.0)]),
        ("edge.canyon.bridge_west", "camp.canyon.a", "site.canyon.bridge", "Primary", "Gentle", "site.canyon.bridge", 0.0, []),
        ("edge.canyon.bridge_east", "site.canyon.bridge", "garage.canyon", "Primary", "Gentle", "site.canyon.bridge", 0.0, []),
        ("edge.canyon.garage_camp_b", "garage.canyon", "camp.canyon.b", "Secondary", "Demanding", "", 0.0, []),
        ("edge.canyon.garage_relay", "garage.canyon", "relay.canyon", "Primary", "Gentle", "", 0.0, []),
        ("edge.corridor.forest_city", "garage.home", "camp.city.a", "Primary", "Gentle", "", 0.12, [(2550.0, 2780.0)]),
        ("edge.corridor.canyon_city", "camp.canyon.a", "camp.city.a", "Secondary", "Gentle", "", 0.15, []),
        ("edge.city.camp_a_garage", "camp.city.a", "garage.city", "Primary", "Gentle", "", 0.20, []),
        ("edge.city.garage_camp_b", "garage.city", "camp.city.b", "Secondary", "Gentle", "", 0.10, []),
        ("edge.city.garage_relay", "garage.city", "relay.city", "Primary", "Gentle", "", 0.0, []),
        ("edge.wetlands.firm_east", "camp.city.b", "camp.wetlands.b", "Primary", "Gentle", "", 0.22, [(4680.0, 1480.0)]),
        ("edge.wetlands.firm_core", "camp.wetlands.b", "garage.wetlands", "Primary", "Gentle", "", 0.26, [(3720.0, 980.0)]),
        ("edge.wetlands.firm_west", "garage.wetlands", "camp.wetlands.a", "Primary", "Gentle", "", 0.24, [(2880.0, 960.0)]),
        ("edge.wetlands.firm_forest", "camp.wetlands.a", "camp.forest.b", "Secondary", "Gentle", "", 0.25, [(1580.0, 1520.0)]),
        ("edge.wetlands.low_ford", "camp.city.a", "garage.wetlands", "Track", "Demanding", "", -1.0, [(3620.0, 1680.0)]),
        ("edge.wetlands.garage_relay", "garage.wetlands", "relay.wetlands", "Primary", "Gentle", "", 0.20, []),
        ("edge.corridor.forest_highlands", "garage.home", "camp.highlands.a", "Primary", "Demanding", "", 0.20, [(1280.0, 3520.0)]),
        ("edge.corridor.canyon_highlands", "camp.canyon.a", "camp.highlands.b", "Secondary", "Demanding", "", 0.12, [(3150.0, 4880.0)]),
        ("edge.highlands.switchbacks", "camp.highlands.a", "garage.highlands", "Primary", "Demanding", "", 0.22, [(1120.0, 4680.0), (1480.0, 4960.0)]),
        ("edge.highlands.ridge_east", "garage.highlands", "camp.highlands.b", "Secondary", "Demanding", "", 0.10, []),
        ("edge.highlands.garage_relay", "garage.highlands", "relay.highlands", "Primary", "Gentle", "", 0.10, []),
        ("edge.highlands.observatory", "garage.highlands", "site.highlands.observatory", "Primary", "Demanding", "", 0.0, [(1720.0, 5280.0)]),
    ]

    route_edges = []
    for eid, a, b, rclass, band, infra, wdepth, mids in raw_edges:
        ax, az = site_pos(a)
        bx, bz = site_pos(b)
        pts_list = [(ax, az)] + mids + [(bx, bz)]
        wps = polyline(pts_list, step_m=80.0)
        length_m = 0.0
        max_grade = 0.0
        for i in range(1, len(wps)):
            dx = wps[i]["x"] - wps[i - 1]["x"]
            dz = wps[i]["z"] - wps[i - 1]["z"]
            horiz = math.hypot(dx, dz)
            dh = abs(wps[i]["y"] - wps[i - 1]["y"])
            length_m += horiz
            if horiz > 1.0:
                max_grade = max(max_grade, math.degrees(math.atan(dh / horiz)))
        is_tidal_ford = wdepth < 0.0
        route_edges.append(
            {
                "id": eid,
                "from": a,
                "to": b,
                "roadClass": rclass,
                "capabilityBand": band,
                "requiredInfrastructureFlag": infra,
                "lengthMeters": round(min(2950.0, max(length_m, 25.0)), 1),
                "rawLengthMeters": round(length_m, 1),
                "maxGradeDegrees": round(max_grade, 2),
                "lowWaterDepthMeters": 0.34 if is_tidal_ford else round(wdepth, 2),
                "highWaterDepthMeters": 0.78 if is_tidal_ford else round(wdepth, 2),
                "singleTankSafe": True,
                "waypoints": wps,
            }
        )

    routes_doc = {"worldSeed": WORLD_SEED, "nodes": route_nodes, "edges": route_edges}
    (CONTENT / "routes.json").write_text(json.dumps(routes_doc, indent=2) + "\n")

    revealed: set[tuple[int, int]] = set()

    def reveal_disc(px: float, pz: float, radius: float) -> None:
        r2 = radius * radius
        cmin_x = max(0, int((px - radius) // 64.0))
        cmax_x = min(GRID_N - 1, int((px + radius) // 64.0))
        cmin_z = max(0, int((pz - radius) // 64.0))
        cmax_z = min(GRID_N - 1, int((pz + radius) // 64.0))
        for cz in range(cmin_z, cmax_z + 1):
            for cx in range(cmin_x, cmax_x + 1):
                if (cx, cz) not in countable:
                    continue
                wx, wz = cx * 64.0 + 32.0, cz * 64.0 + 32.0
                if (wx - px) ** 2 + (wz - pz) ** 2 <= r2:
                    revealed.add((cx, cz))

    for lm in landmarks:
        reveal_disc(lm["positionHint"]["x"], lm["positionHint"]["z"], REVEAL_VEHICLE_M)
    for ca in caches:
        reveal_disc(ca["positionHint"]["x"], ca["positionHint"]["z"], REVEAL_VEHICLE_M)
    for s in sites:
        reveal_disc(s["positionHint"]["x"], s["positionHint"]["z"], REVEAL_VEHICLE_M)
        if "checkpoints" in s:
            cps = s["checkpoints"]
            for i in range(len(cps) - 1):
                ax, az = cps[i]["x"], cps[i]["z"]
                bx, bz = cps[i + 1]["x"], cps[i + 1]["z"]
                ln = math.hypot(bx - ax, bz - az)
                steps = max(1, int(ln / 32.0))
                for k in range(steps + 1):
                    reveal_disc(ax + (bx - ax) * k / steps, az + (bz - az) * k / steps, REVEAL_VEHICLE_M)
    for g in garages:
        reveal_disc(g["positionHint"]["x"], g["positionHint"]["z"], REVEAL_VEHICLE_M)
    for c in camps:
        reveal_disc(c["positionHint"]["x"], c["positionHint"]["z"], REVEAL_VEHICLE_M)

    pct_vehicle_only = 100.0 * len(revealed) / len(countable)
    for lm in landmarks:
        if lm.get("vista"):
            reveal_disc(lm["positionHint"]["x"], lm["positionHint"]["z"], REVEAL_VISTA_M)
    pct_with_vistas = 100.0 * len(revealed) / len(countable)
    max_edge_grade = max(e["maxGradeDegrees"] for e in route_edges)
    print(f"[world] Max route grade across {len(route_edges)} mainland edges: {max_edge_grade:.2f} deg")
    print(f"[world] PlacedTargets vehicle-only coverage: {pct_vehicle_only:.2f}% ({len(revealed)} cells with vistas -> {pct_with_vistas:.2f}%)")
    assert pct_vehicle_only >= 61.0
    assert max_edge_grade < 16.0


if __name__ == "__main__":
    main()
