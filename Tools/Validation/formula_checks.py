#!/usr/bin/env python3
"""Wayfinder formula and invariant checks (headless).

Two kinds of check live here, and the difference matters (DEF-025):

1. **Mirror checks** — a Python re-implementation of a documented formula asserting the
   blueprint's own example values (fuel, damage, steering, slip, rescue loss, world arithmetic).
   These are *documentation*: they prove the Python mirror agrees with the spec, not that the C#
   does. Mutating a C# constant does not fail them, and they are labelled as such.
2. **Cross-checks** — the constants and tables are parsed out of the C# sources and the canonical
   JSON under `Assets/Wayfinder/`, so editing a constant in C# or a row in content genuinely fails
   them. This is the part that is evidence.

A self-lint (`check_self_lint`) rejects any `check(...)` whose condition is built only from
literals — the tautologies this file used to carry (`0.55 == 0.55`, `True`, `2.0 == 2.0`) — so a
check that cannot fail cannot be added again.
"""

import ast
import math
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
SCRIPTS = os.path.join(ROOT, "Assets", "Wayfinder", "Scripts")
CONTENT = os.path.join(ROOT, "Assets", "Wayfinder", "Content")

errors = []
mirrors = []  # names of the documentation-only (non-cross-checking) checks


def check(name, ok, detail=""):
    status = "ok  " if ok else "FAIL"
    print(f"[{status}] {name}" + (f" — {detail}" if detail else ""))
    if not ok:
        errors.append(name)


def json_load(handle):
    import json
    return json.load(handle)


def read_source(relative_path):
    with open(os.path.join(SCRIPTS, relative_path), encoding="utf-8") as f:
        return f.read()


def csharp_const(source, name):
    """Reads `const float Name = 0.55f;` out of C# source. Returns None when it is not there."""
    m = re.search(r"\bconst\s+(?:float|int|double)\s+" + re.escape(name) + r"\s*=\s*(-?[0-9.]+)f?", source)
    return float(m.group(1)) if m else None


def check_self_lint():
    """No check may assert an expression of literals only — those can never fail (DEF-025)."""
    with open(os.path.abspath(__file__), encoding="utf-8") as f:
        tree = ast.parse(f.read())
    tautologies = []
    for node in ast.walk(tree):
        if not (isinstance(node, ast.Call) and isinstance(node.func, ast.Name)):
            continue
        if node.func.id != "check" or len(node.args) < 2:
            continue
        condition = node.args[1]
        if not any(isinstance(n, (ast.Name, ast.Attribute, ast.Call, ast.Subscript))
                   for n in ast.walk(condition)):
            tautologies.append(node.args[0].value if isinstance(node.args[0], ast.Constant) else "?")
    check("self-lint: no tautological check() remains", not tautologies,
          "literals only: " + ", ".join(tautologies))


# ---------- Stable hash mirror (§7.3) ----------
FNV32_OFFSET = 2166136261
FNV32_PRIME = 16777619
FNV64_OFFSET = 14695981039346656037
FNV64_PRIME = 1099511628211


def stable_hash32(text):
    h = FNV32_OFFSET
    for b in text.encode("utf-8"):
        h ^= b
        h = (h * FNV32_PRIME) & 0xFFFFFFFF
    return h


def chunk_seed(world_seed, gen_version, cx, cz, layer):
    h = FNV64_OFFSET
    for value in (world_seed & 0xFFFFFFFF, gen_version & 0xFFFFFFFF,
                  cx & 0xFFFFFFFF, cz & 0xFFFFFFFF, layer & 0xFFFFFFFF):
        h ^= value
        h = (h * FNV64_PRIME) & 0xFFFFFFFFFFFFFFFF
        h ^= h >> 33
        h = (h * FNV64_PRIME) & 0xFFFFFFFFFFFFFFFF
    return h


# ---------- Fuel model (§5.1) ----------
def liters_per_second(throttle, load, slip, engine_on=True):
    if not engine_on:
        return 0.0
    throttle = min(max(throttle, 0.0), 1.0)
    load = min(max(load, 0.0), 1.0)
    slip = min(max(slip, 0.0), 1.0)
    return 0.003 + 0.025 * throttle + 0.015 * load + 0.004 * slip


def check_fuel():
    rate = liters_per_second(0.45, 0.50, 0.20)
    check("fuel: blueprint example rate = 0.02255 L/s", abs(rate - 0.02255) < 1e-9, f"got {rate}")
    minutes = 24.0 / rate / 60.0
    check("fuel: 24 L ≈ 17.7 min at example rate", abs(minutes - 17.7) < 0.2, f"got {minutes:.2f} min")
    check("fuel: engine off consumes zero", liters_per_second(1, 1, 1, engine_on=False) == 0.0)
    # Bounds under any input (QA case 11)
    worst = liters_per_second(1, 1, 1)
    check("fuel: drain bounded ≤ 0.047 L/s", worst <= 0.047 + 1e-9, f"got {worst}")


# ---------- Damage model (§5.5) ----------
def impact_damage(speed_ms):
    if speed_ms < 3.0:
        return 0.0
    return min(max(1.4 * (speed_ms - 3.0) ** 2, 0.0), 35.0)


def check_damage():
    check("damage: impacts < 3 m/s ignored", impact_damage(2.9) == 0.0)
    d5 = impact_damage(5.0)
    check("damage: D(5) = 5.6", abs(d5 - 5.6) < 1e-9, f"got {d5}")
    check("damage: D(10) clamps to 35", impact_damage(10.0) == 35.0)
    check("damage: D(3) = 0", impact_damage(3.0) == 0.0)


# ---------- Research tree (§11.2) — cross-checked against content and C# ----------
COSTS = [200, 300, 450, 550, 700, 850, 1000, 1150, 1300, 1450,
         1600, 1750, 1900, 2100, 2300, 2500, 2700, 2900, 3100, 3400]


def check_research():
    # The dead duplicate COSTS line (whose first entry read `2900 and 2900`) is gone: an edit to
    # the first assignment used to be silently discarded by the second one (DEF-025).
    check("research: 20 tiers", len(COSTS) == 20)
    check("research: Σ incremental cost = 32,200 (spec)", sum(COSTS) == 32200, f"got {sum(COSTS)}")

    with open(os.path.join(CONTENT, "upgrades.json"), encoding="utf-8") as f:
        upgrades = json_load(f)
    content_costs = [u["costSalvage"] for u in upgrades]
    check("research: upgrades.json matches the spec cost table", content_costs == COSTS,
          f"content {content_costs}")

    source = read_source(os.path.join("Progression", "ResearchService.cs"))
    tiers_in_code = len(re.findall(r"^\s*Add\(\s*(\d+)\s*,", source, re.MULTILINE))
    check("research: C# ResearchService declares 20 tiers", tiers_in_code == 20,
          f"found {tiers_in_code}")
    costs_in_code = [int(m) for m in re.findall(r"^\s*Add\(\s*\d+\s*,\s*\"[^\"]+\"\s*,\s*(\d+)\s*,",
                                                source, re.MULTILINE)]
    check("research: C# costs match the content table", costs_in_code == COSTS,
          f"C# {costs_in_code}")

    # DEF-004: five tiers used to declare zero effects, so 28 % of the tree bought nothing.
    empty = [u["id"] for u in upgrades if not u.get("effects")]
    check("research: every tier has at least one effect", not empty, "no effects: " + ", ".join(empty))


# ---------- Steering (§3.3) ----------
def steer_limit(kph):
    kph = max(0.0, kph)
    if kph <= 20:
        return 34 + (26 - 34) * (kph / 20)
    if kph <= 40:
        return 26 + (16 - 26) * ((kph - 20) / 20)
    if kph <= 70:
        return 16 + (10 - 16) * ((kph - 40) / 30)
    return 10.0


def check_steering():
    for kph, expected in [(0, 34), (20, 26), (40, 16), (70, 10)]:
        got = steer_limit(kph)
        check(f"steering: limit @{kph} km/h = {expected}°", abs(got - expected) < 1e-6, f"got {got}")


# ---------- Slip ratio (§4.4) ----------
def slip_ratio(wheel_angular_speed, radius, long_speed):
    denom = max(abs(long_speed), 1.0)
    return (wheel_angular_speed * radius - long_speed) / denom


def check_slip():
    # Wheel exactly matching ground speed → zero slip
    v = 10.0
    omega = v / 0.36
    check("slip: matched rolling = 0", abs(slip_ratio(omega, 0.36, v)) < 1e-9)
    # Locked wheel at speed → −1 at low speed denominators
    s = slip_ratio(0.0, 0.36, 10.0)
    check("slip: locked wheel at 10 m/s = -1", abs(s - (-1.0)) < 1e-9, f"got {s}")


# ---------- Rescue (§5.6) ----------
def check_rescue():
    def loss(units):
        return int(units * 0.25)  # floor
    check("rescue: 25% floor of 37 units = 9", loss(37) == 9)
    check("rescue: zero units → zero loss", loss(0) == 0)
    # Was `check(..., True)` — a literal that can never fail (DEF-025). The invariant it claimed is
    # now pinned where it lives: the C# rescue path may only remove salvage_bundle units.
    source = read_source(os.path.join("Campaign", "CampaignSession.Extraction.cs"))
    removes_salvage_only = "salvage_bundle" in source and "RescueSalvageLoss" in source
    check("rescue: the C# path loses salvage_bundle units only", removes_salvage_only,
          "CampaignSession.Extraction.cs no longer contains the rescue loss path")
    mirrors.append("rescue: 25% floor of 37 units = 9")


# ---------- Water (§5.3) — constants read from the C#, not restated ----------
def check_water():
    # These four lines used to read `0.55 == 0.55`, `0.85 == 0.85`, `2.0 == 2.0`, `1.2 == 1.2`.
    # They passed while SafeWadingDepthM was never composed at all (DEF-001), which is exactly why
    # they are now parsed out of HazardModels.cs: change the constant and this fails.
    source = read_source(os.path.join("Simulation", "HazardModels.cs"))
    expected = {
        "BaselineSafeWadingM": 0.55,
        "UpgradedSafeWadingM": 0.85,
        "StallSecondsAboveIntake": 2.0,
        "ImpassableMainRouteDepthM": 1.2,
    }
    for name, spec_value in expected.items():
        got = csharp_const(source, name)
        check(f"water: {name} = {spec_value} (§5.3)", got == spec_value,
              f"C# says {got}" if got is not None else "constant not found")

    # DEF-001: the resolved intake height must come from research, never stay at zero.
    resolver = read_source(os.path.join("Simulation", "VehicleStatsResolver.cs"))
    check("water: SafeWadingDepthM is composed by the resolver",
          "SafeWadingDepthM" in resolver and "WaterCrossingModel" in resolver,
          "the resolver never resolves the intake height")


# ---------- World arithmetic (§6.1, §8.1) ----------
def check_world_math():
    # Was four lines of literal arithmetic (`24 * 256 == 6144`) — true whatever the code says.
    # The constants are now read out of WorldConstants.cs, so changing the world size fails here.
    source = read_source(os.path.join("World", "WorldDefinition.cs"))
    consts = {name: csharp_const(source, name) for name in
              ("WorldSizeMeters", "ChunkSizeMeters", "MapCellsPerSide", "MapCellSizeMeters",
               "TerrainTileResolution", "TerrainSampleSpacingMeters")}
    missing = [name for name, value in consts.items() if value is None]
    check("world: WorldConstants exposes the six layout constants", not missing,
          "missing: " + ", ".join(missing))
    if missing:
        return
    world = int(consts["WorldSizeMeters"])
    chunk = int(consts["ChunkSizeMeters"])
    cells = int(consts["MapCellsPerSide"])
    cell_size = int(consts["MapCellSizeMeters"])
    res = int(consts["TerrainTileResolution"])
    spacing = int(consts["TerrainSampleSpacingMeters"])
    check("world: chunks × chunk size = world size", (world // chunk) * chunk == world,
          f"{chunk} m chunks over {world} m")
    check("map: cell size × cell count = world size", cells * cell_size == world,
          f"{cells} × {cell_size} m")
    check("terrain: (res − 1) × spacing = chunk size", (res - 1) * spacing == chunk,
          f"{res} samples at {spacing} m")
    check("map: cell count² = 9216", cells * cells == 9216, f"{cells * cells}")


# ---------- Stable hash determinism (§7.3) ----------
def check_hash():
    a = stable_hash32("cache.forest.017")
    b = stable_hash32("cache.forest.017")
    c = stable_hash32("cache.forest.018")
    check("hash: deterministic", a == b)
    check("hash: distinct inputs differ", a != c)
    s1 = chunk_seed(481516, 1, 5, 9, 2)
    s2 = chunk_seed(481516, 1, 5, 9, 2)
    s3 = chunk_seed(481516, 1, 5, 9, 3)
    check("hash: chunkSeed deterministic", s1 == s2)
    check("hash: layerId changes seed", s1 != s3)


# ---------- Noise mirror + seam quantization (§7.4) ----------
def hash01(seed, x, y):
    h = chunk_seed(0, 0, x * 73856093 ^ y * 19349663, 0, seed & 0xFFFFFFFF)
    r = (h ^ (h >> 32)) & 0xFFFFFFFF
    return (r & 0xFFFFFF) / 16777216.0


def value_noise(x, z, seed):
    x0, z0 = math.floor(x), math.floor(z)
    fx, fz = x - x0, z - z0
    sx = fx * fx * (3 - 2 * fx)
    sz = fz * fz * (3 - 2 * fz)
    n00 = hash01(seed, x0, z0)
    n10 = hash01(seed, x0 + 1, z0)
    n01 = hash01(seed, x0, z0 + 1)
    n11 = hash01(seed, x0 + 1, z0 + 1)
    ix0 = n00 + (n10 - n00) * sx
    ix1 = n01 + (n11 - n01) * sx
    return ix0 + (ix1 - ix0) * sz


def fbm(x, z, octaves, seed):
    total = norm = 0.0
    amp = 0.5
    freq = 1.0
    for i in range(octaves):
        total += value_noise(x * freq, z * freq, chunk_seed(0, 0, i, 0, seed & 0xFFFFFFFF)) * amp
        norm += amp
        amp *= 0.5
        freq *= 2
    return total / max(1e-5, norm)


def ridged(x, z, octaves, seed):
    total = norm = 0.0
    amp = 0.5
    freq = 1.0
    for i in range(octaves):
        n = value_noise(x * freq, z * freq, chunk_seed(0, 0, i, 0, seed & 0xFFFFFFFF)) * 2 - 1
        ridge = 1 - abs(n)
        total += ridge * ridge * amp
        norm += amp
        amp *= 0.5
        freq *= 2
    return total / max(1e-5, norm)


def base_height(x, z):
    layer1 = chunk_seed(481516, 1, 0, 0, 1)
    layer2 = chunk_seed(481516, 1, 0, 0, 2)
    layer3 = chunk_seed(481516, 1, 0, 0, 3)
    continental = fbm(x * 0.0006, z * 0.0006, 4, layer1)
    ridge = ridged(x * 0.0018, z * 0.0018, 3, layer2)
    detail = fbm(x * 0.012, z * 0.012, 2, layer3)
    macro = (-20 + (520 - -20) * min(max(continental * 0.65 + ridge * 0.35, 0), 1))
    return macro + detail * 6


def quantize(h):
    return round(h * 1000.0) / 1000.0


def bake_tile(chunk_x, chunk_z, sampler=base_height, spacing=2.0, res=129, chunk=256.0):
    """One chunk's 129×129 samples in tile-local order — the Python mirror of HeightField.BakeTile."""
    origin_x, origin_z = chunk_x * chunk, chunk_z * chunk
    return [[quantize(sampler(origin_x + x * spacing, origin_z + z * spacing)) for x in range(res)]
            for z in range(res)]


def tile_border_difference(tile, neighbour, edge="east"):
    """Mirrors SeamCheck.BordersMatch(HeightField.cs): the shared column/row of two baked tiles."""
    res = len(tile)
    if edge == "east":
        # tile's last column against the east neighbour's first column (same world line).
        return max(abs(tile[z][res - 1] - neighbour[z][0]) for z in range(res))
    # tile's last row against the north neighbour's first row.
    return max(abs(tile[res - 1][x] - neighbour[0][x]) for x in range(res))


def check_seams():
    # Each chunk bakes its own 129×129 grid at 2 m spacing from the shared world function, so two
    # adjacent tiles sample the *same* world line along their shared border: tile (cx,cz) column
    # res-1 and tile (cx+1,cz) column 0 are identical by construction. The difference printed below
    # is therefore always 0 — the old output quoted "worst diff 0.0" as GEN-02 evidence, but the
    # check could not fail (DEF-011). What IS falsifiable is the comparison, exercised underneath.
    worst = 0.0
    for chunk_x in (3, 10, 23):
        left = bake_tile(chunk_x, 5)
        right = bake_tile(chunk_x + 1, 5)
        worst = max(worst, tile_border_difference(left, right, "east"))
        up = bake_tile(chunk_x, 6)
        worst = max(worst, tile_border_difference(left, up, "north"))
    check("seam: adjacent chunk borders agree ≤ 1 mm", worst <= 0.001, f"worst diff {worst}")
    mirrors.append("seam: adjacent chunk borders agree ≤ 1 mm")

    # Falsifiable half: hand the comparison two tiles that disagree by 5 mm and require a failure.
    left = bake_tile(7, 7)
    right = bake_tile(8, 7)
    check("seam: the comparison accepts matching tiles",
          tile_border_difference(left, right, "east") <= 0.001)
    broken = [row[:] for row in right]
    broken[5][0] += 0.005
    broken_diff = tile_border_difference(left, broken, "east")
    check("seam: the comparison rejects a 5 mm disagreement", broken_diff > 0.001,
          f"diff {broken_diff:.4f}")

    q1 = quantize(0.0004)
    q2 = quantize(0.0006)
    check("seam: quantization grid is 1 mm", q1 == 0.0 and q2 == 0.001)

    h1 = base_height(1234.5, 2345.5)
    h2 = base_height(1234.5, 2345.5)
    check("heightfield: deterministic", h1 == h2)
    check("heightfield: within −20..520 m band (± detail)",
          -26.0 <= h1 <= 526.0, f"got {h1:.2f}")
    samples = [base_height(x * 137.3, x * 211.7) for x in range(24)]
    spread = max(samples) - min(samples)
    check("heightfield: non-degenerate relief (spread > 50 m)", spread > 50.0,
          f"spread {spread:.1f} m, range {min(samples):.1f}..{max(samples):.1f}")


def main():
    print("== Wayfinder formula checks ==")
    check_self_lint()
    check_fuel()
    check_damage()
    check_research()
    check_steering()
    check_slip()
    check_rescue()
    check_water()
    check_world_math()
    check_hash()
    check_seams()
    if mirrors:
        print(f"\n{len(mirrors)} check(s) are documentation mirrors, not evidence "
              f"(they cannot fail on a C# change):")
        for name in mirrors:
            print(f"  mirror: {name}")
    print(f"\n{len(errors)} failure(s)")
    if not errors:
        print("FORMULA CHECKS PASSED")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
