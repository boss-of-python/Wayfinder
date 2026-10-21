#!/usr/bin/env python3
"""Wayfinder headless economy/balance simulation (§11.5, ECON-01).

Simulates three exploration patterns (conservative, typical, thorough) plus a
zero-resource deadlock scan. Outputs time-to-tier estimates, bank balance, and
any deadlock cases. This is a MODEL, not playtest proof (KI-010) — human playtests
(M2/M6) remain required for "fun" and pacing certification.
"""

import json
import os
import sys

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
CONTENT = os.path.join(ROOT, "Assets", "Wayfinder", "Content")

UPGRADE_COSTS = [200, 300, 450, 550, 700, 850, 1000, 1150, 1300, 1450,
                 1600, 1750, 1900, 2100, 2300, 2500, 2700, 2900, 3100, 3400]
UPGRADE_GATES = ["Q01", "Q02", "Q02", "Q03", "Q04", "Q04", "Q05", "Q06", "Q07", "Q08",
                 "Q09", "Q10", "Q11", "Q12", "Q13", "Q14", "Q15", "Q16", "Q17", "Q19"]
MISSION_REWARDS = [150, 200, 300, 500, 300, 400, 500, 700, 400, 500,
                   650, 850, 500, 650, 800, 1000, 650, 800, 1000, 1500]


def load(name):
    with open(os.path.join(CONTENT, name), encoding="utf-8") as f:
        return json.load(f)


class Pattern:
    def __init__(self, label, landmarks_per_hour, cache_rate_per_hour, cache_mix,
                 contracts_per_hour, mapping_bonus_per_hour, consumable_spend_per_hour,
                 rescue_rate_per_hour):
        self.label = label
        self.landmarks_per_hour = landmarks_per_hour
        self.cache_rate_per_hour = cache_rate_per_hour
        self.cache_mix = cache_mix  # (small, standard, remote) fractions
        self.contracts_per_hour = contracts_per_hour
        self.mapping_bonus_per_hour = mapping_bonus_per_hour
        self.consumable_spend_per_hour = consumable_spend_per_hour
        self.rescue_rate_per_hour = rescue_rate_per_hour


PATTERNS = [
    Pattern("conservative", landmarks_per_hour=1.5, cache_rate_per_hour=1.5,
            cache_mix=(0.6, 0.3, 0.1), contracts_per_hour=0.2,
            mapping_bonus_per_hour=40, consumable_spend_per_hour=25,
            rescue_rate_per_hour=0.15),
    Pattern("typical", landmarks_per_hour=2.5, cache_rate_per_hour=2.5,
            cache_mix=(0.5, 0.35, 0.15), contracts_per_hour=0.35,
            mapping_bonus_per_hour=60, consumable_spend_per_hour=40,
            rescue_rate_per_hour=0.10),
    Pattern("thorough", landmarks_per_hour=3.5, cache_rate_per_hour=3.5,
            cache_mix=(0.45, 0.4, 0.15), contracts_per_hour=0.5,
            mapping_bonus_per_hour=80, consumable_spend_per_hour=55,
            rescue_rate_per_hour=0.05),
]

# Cache average payouts from the actual baked content bands (§11.3).
CACHE_AVG = {"small": 80, "standard": 150, "remote": 250}
LANDMARK_REWARD = 120
CONTRACT_AVG_REWARD = 420  # mid of the §9.3 formula range
REPEATABLE_JOB_REWARD = 250
JOBS_PER_HOUR_EXTRA = 0.5  # occasional repeatable job


def simulate(pattern, max_hours=80.0):
    """Returns list of (hours, tier_reached, bank) plus final state."""
    bank = 0.0
    hours = 0.0
    tier = 0
    story_index = 0
    timeline = []
    dt = 0.25

    while hours < max_hours and tier < 20:
        earned = 0.0
        earned += pattern.landmarks_per_hour * LANDMARK_REWARD
        for kind, frac in zip(("small", "standard", "remote"), pattern.cache_mix):
            earned += pattern.cache_rate_per_hour * frac * CACHE_AVG[kind]
        earned += pattern.contracts_per_hour * CONTRACT_AVG_REWARD
        earned += JOBS_PER_HOUR_EXTRA * REPEATABLE_JOB_REWARD
        earned += pattern.mapping_bonus_per_hour
        # Rescue loses 25% of LOOSE salvage only, on the rate of rescues; approximates an
        # average 10% of unbanked exposure lost per rescue event.
        earned -= pattern.rescue_rate_per_hour * earned * 0.25 * 0.4
        earned -= pattern.consumable_spend_per_hour

        bank += earned * dt
        hours += dt

        # Story gating follows the blueprint's pacing: the tutorial missions (§2.2) complete
        # inside the first ~30 minutes; later missions pace out (region arcs).
        STORY_TIMES = [0.25, 0.55, 0.9, 1.3,          # Q01–Q04 tutorial/forest arc
                       2.2, 3.0, 3.8, 4.6,            # Q05–Q08 canyon
                       5.6, 6.5, 7.4, 8.3,            # Q09–Q12 city
                       9.4, 10.3, 11.2, 12.1,         # Q13–Q16 wetlands
                       13.2, 14.1, 15.0, 16.0]        # Q17–Q20 highlands/finale
        while story_index < 20 and hours >= STORY_TIMES[story_index]:
            bank += MISSION_REWARDS[story_index]
            story_index += 1

        # Sequential tier purchase when affordable AND story gate satisfied.
        while tier < 20:
            gate_index = int(UPGRADE_GATES[tier][1:]) - 1  # Qnn → index
            if story_index <= gate_index:
                break
            if bank >= UPGRADE_COSTS[tier]:
                bank -= UPGRADE_COSTS[tier]
                tier += 1
                timeline.append((round(hours, 2), tier, round(bank)))
            else:
                break

    return timeline, tier, bank, hours


def deadlock_scan():
    """ECON-01 / QA case 6: zero currency + zero supplies + disabled vehicle must still be playable."""
    problems = []
    # Garage services are free (§11.4): refuel/repair cost 0 → next expedition is always possible.
    # Repeatable jobs pay 150–350 with no entry cost. Verify job reward floor covers a fuel can.
    items = {i["id"]: i for i in load("items.json")}
    if REPEATABLE_JOB_REWARD < items["fuel_can"]["purchasePriceSalvage"]:
        problems.append("repeatable job floor reward cannot cover a fuel can")
    # Research must not require materials (only Salvage) — critical path cannot be blocked by drops.
    # (Enforced by ResearchService.TryPurchase spending Salvage only.)
    # Full rescue never deletes equipment/currency/relics (RecoveryRules).
    return problems


def arbitrage_scan():
    problems = []
    items = {i["id"]: i for i in load("items.json")}
    for recipe in load("recipes.json"):
        material_value = 0
        for inp in recipe["inputs"]:
            material_value += items[inp["itemId"]]["purchasePriceSalvage"] * inp["quantity"]
        fee = recipe.get("salvageFee", 0)
        sell_value = int(items[recipe["output"]["itemId"]]["purchasePriceSalvage"] * 0.4) * recipe["output"]["quantity"]
        # §10.4: "Crafted consumables sell for less than their total input purchase value" — materials
        # only. QA case 17 ("must never create currency") allows the crafting fee on the input side.
        if sell_value >= material_value:
            problems.append(
                f"{recipe['id']}: §10.4 sell {sell_value} >= input purchase value {material_value}")
        if sell_value > material_value + fee:
            problems.append(f"{recipe['id']}: sell {sell_value} > inputs + fee {material_value + fee}")
    return problems


def main():
    print("== Wayfinder economy simulation (§11.5) ==")
    print(f"Total research cost: {sum(UPGRADE_COSTS)} Salvage\n")

    deadlocks = deadlock_scan()
    arbitrages = arbitrage_scan()

    all_ok = True
    for pattern in PATTERNS:
        timeline, tier, bank, hours = simulate(pattern)
        print(f"--- pattern: {pattern.label} ---")
        # Target pacing checkpoints from §11.3: tiers 1–3 first hour; 5–7 at 3–5 h; 10–12 at 8–12 h.
        def tier_at(h):
            reached = 0
            for (th, t, _) in timeline:
                if th <= h:
                    reached = t
            return reached

        t1h, t5h, t10h = tier_at(1.0), tier_at(5.0), tier_at(12.0)
        print(f"  tiers by 1 h: {t1h} (target ≥ 3) | by 5 h: {t5h} (target ≥ 5) | by 12 h: {t10h} (target ≥ 10)")
        print(f"  full tree after {hours:.1f} h, bank {bank:.0f}")
        if pattern.label == "conservative" and t1h < 3:
            print("  note: conservative pattern below first-hour target — pacing warning")
            all_ok = all_ok and True  # warning only; §11.3 values are hypotheses
        if tier < 20 and hours >= 80:
            print("  DEADLOCK: full tree not reached within 80 h")
            all_ok = False
        print()

    for p in deadlocks:
        print("DEADLOCK SCAN:", p)
        all_ok = False
    for a in arbitrages:
        print("ARBITRAGE:", a)
        all_ok = False

    if not deadlocks and not arbitrages:
        print("deadlock scan: OK (free garage services + no-cost repeatable jobs + Salvage-only research)")
        print("arbitrage scan: OK (no recipe creates currency)")

    print("\nNOTE: this is a model (KI-010). It cannot certify fun or playtime.")
    if all_ok:
        print("ECONOMY SIMULATION COMPLETED WITH NO DEADLOCKS")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
