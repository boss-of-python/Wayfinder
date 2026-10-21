# Batch P — MAP-03 (§8.4): exploration rewards, the finale's 60% gate, and a target the game does not meet

**Date:** 2026-10-21 · **Branch:** `arena/0d5e229d-wayfinder` · **Decision:** D-046 · **New issue:** KI-048
**Requirement:** MAP-03, blueprint §8.4 (`Wayfinder_Production_Blueprint.txt:572`)

---

## 1. The headline finding

§8.4 mandates a test the project did not have:

> "Target progression tests must show that story routes plus reasonable detours can achieve 60%;
> otherwise adjust route exposure or the threshold."

The test now exists — `StoryRoutesPlusEveryPlacedDetourDoNotReachTheSixtyPercentGate` — and **the target
is not met**:

| Stage | Countable cells revealed | Coverage |
|---|---|---|
| New game | 0 | 0.00% |
| Q01–Q04 played end to end (the story) | 112 | **1.22%** |
| Plus every detour content places — each landmark, cache, site, garage and camp socket, plus each checkpoint route walked at 32 m steps (332 reveal stops) | 665 | **7.22%** |
| Finale gate (§8.4) | 5530 | **60%** |

Measured with the real reveal path (`CampaignSession.RevealAround` → `DiscoveryService.RevealVisibleAround`
with terrain line-of-sight), not with bits set directly.

**Two causes compound:**

1. **There are almost no story routes to drive.** `vertical_slice.json` places **one** checkpoint route
   (`route.forest.ford`, 2 checkpoints) and 15 positioned points in total. Missions Q05–Q20 have no
   placed sites at all.
2. **The denominator is the whole map.** `BakeCountableMask` keeps every cell whose base height is above
   `SeaLevelY`, and on the canonical seed **no cell falls below it** — so the mask counts all
   **9216 = 96×96** cells. Its own doc comment says "Precomputed countable-cell mask excluding
   ocean/decorative cliffs (§8.1)"; nothing is excluded.

**Neither was tuned away.** Lowering `FinaleMapCoveragePercent` would falsify a blueprint number written
for the full game in order to make a vertical-slice test pass. Shrinking the mask to the placed content's
bounding box would change what is measured instead of changing the game. Both real fixes need M3 — the
route graph (KI-030) and a bake/sea-level decision (KI-027/038) — so this is recorded as **KI-048**,
scoped to the M4 finale gate. It does not block M2: nothing in the slice asks the player to reach 60%.

The mask's actual behaviour is pinned by a test (`TheCountableMaskCountsEveryCell_...`, asserting 9216),
so a future change to the bake is deliberate and surfaces as a failing test rather than as a coverage
number that quietly moved.

---

## 2. What else this batch verified

| §8.4 clause | Test |
|---|---|
| Every new landmark pays 120 Salvage once | `ALandmarkPays120Once_UnderALandmarkKey_NotAMappingKey`, `TwoLandmarksPayTwoRewards_UnderTwoDistinctKeys` |
| Every 50 newly revealed countable cells grants 50 Salvage once | `EveryFiftyCountableCellsPaysFiftySalvageOnce_UnderItsOwnRewardKey` |
| Persistent count + paid-threshold index prevent duplicate claims | `ThePaidThresholdIndexSurvivesReload_AndCannotPayTheSameThresholdTwice`, `ARestoreThatLostProgressCannotRepayWhatTheRewardLedgerAlreadyClaimed` |
| Rewards are escrowed, not banked | asserted in the mapping and landmark tests via `Economy.EscrowedSalvage` |
| Separate reward keys in one ledger | `MappingAndLandmarkRewardsShareOneLedger_WithSeparateKeys` (`reward.map.N` vs `reward.landmark.<id>`) |
| The finale needs 60% countable coverage | `TheFinaleGateIsExactlyTheSixtyPercentCoverageRule` (flips at 5530 of 9216 cells) |
| §8.4's literal numbers | `TheSection84NumbersAreTheBlueprintsNumbers` |
| Coverage is measured against the countable mask | `CoverageIsZeroOnANewGame_AndIsMeasuredAgainstTheCountableMask`, `TheCountableMaskCountsEveryCell_...` |

**One behaviour that looked like a bug and is not.** Discovering a *vista* landmark escrowed **220**
Salvage, not 120. §8.1 makes a vista survey a 384 m disc, which reveals enough countable cells to cross
mapping thresholds — so the landmark pays its own 120 **plus** 50 per threshold, under separate keys.
That is two correct rules composing, not a double payment. It is now asserted deliberately
(`AVistaLandmarkPaysItsOwn120_PlusTheMappingThresholdsItsRevealCrosses`), and the pure-120 case uses a
non-vista landmark (4 of the slice's 6 landmarks are non-vista) so the two are separable.

---

## 3. Commands executed and results

```bash
python3 Tools/Build/headless_cs.py compile   # 7 assemblies, 0 errors, 0 warnings
python3 Tools/Build/headless_cs.py test      # Overall result: Passed — 294/294
python3 Tools/Validation/validate_content.py # 0 error(s), 0 warning(s) — CONTENT VALIDATION PASSED
python3 Tools/Balance/economy_sim.py         # arbitrage scan: OK
python3 Tools/Validation/formula_checks.py   # 0 failure(s) — FORMULA CHECKS PASSED
python3 Tools/Build/headless_cs.py syntax    # 30 Unity-dependent file(s), 0 syntax error(s)
```

Suite: **282 → 294** (12 new tests in `CampaignExplorationProgressionTests`). No existing test was
weakened or deleted.

Artifacts: `headless-console.log`, `python-checks.log`, `compile-report.json`, `EditMode.headless.xml`
(copied from `Logs/headless/` after a clean `headless_cs.py all`, never after a mutation run),
`mutation_check.py`, `mutation-check.log` (final run), `mutation-check-run1.log` (first run, 10/13).

---

## 4. Mutation check

`mutation_check.py` breaks one §8.4 rule at a time in `Progression/DiscoveryService.cs` and
`Campaign/CampaignSnapshot.cs`, recompiles, runs the whole EditMode suite, and requires at least one
failing test. It refuses to start on a dirty `Assets/` tree and verifies it left the tree clean.

**Run 1: 10/13 caught, EXIT 1.** Three survivors, all test weaknesses rather than dead code:

* **M05** (pay one mapping threshold per reveal instead of all of them) survived because the vista test
  derived its expectation from `MappingRewardsPaidThroughCount` itself — an under-paying loop agreed with
  itself. It now derives the expected index from the cells actually revealed, and first asserts that a
  384 m vista disc really does cross two or more thresholds in a single action.
* **M08 / M11** (escrow a mapping or landmark reward even when its key is already claimed) survived
  because the paid-threshold index and the discovered-landmark set already prevent re-entry on their own.
  The ledger guard only matters when the two **disagree**, which nothing constructed. A new test restores
  progress from a payload that lost it while the claimed-key ledger survived — the legacy / partial-
  migration shape — and asserts that re-revealing the same ground and re-discovering the same landmark
  escrow nothing further.

Fixed in the tests, not waved off (`72c4ceb`).

**Run 2 (final): **13/13 caught, survivors: none, tree clean after run, EXIT 0** (`mutation-check.log`). Every mutant was caught by at least one failing test; none needed a compile failure.**

| Mutant | What it breaks | Suite | Caught by |
|---|---|---|---|
| M01 | a mapping threshold is 40 cells instead of 50 (§8.4) | 286/294 | `ARestoreThatLostProgressCannotRepayWhatTheRewardLedgerAlreadyClaimed`, `AVistaLandmarkPaysItsOwn120_PlusTheMappingThresholdsItsRevealCrosses` +1 more |
| M02 | a mapping threshold pays 45 Salvage instead of 50 (§8.4) | 289/294 | `AVistaLandmarkPaysItsOwn120_PlusTheMappingThresholdsItsRevealCrosses`, `MappingAndLandmarkRewardsShareOneLedger_WithSeparateKeys` +1 more |
| M03 | a landmark pays 110 Salvage instead of 120 (§8.4) | 293/294 | `TheSection84NumbersAreTheBlueprintsNumbers` |
| M04 | the finale gate drops to 50% coverage (§8.4) | 292/294 | `TheSection84NumbersAreTheBlueprintsNumbers`, `NewGame_MapStartsBlank_OnlyYouTheHomeGarageAndTheQ01SearchArea_S8_1` |
| M05 | only one mapping threshold is paid per reveal instead of all of them | 293/294 | `AVistaLandmarkPaysItsOwn120_PlusTheMappingThresholdsItsRevealCrosses` |
| M06 | every mapping threshold shares one reward key, so only the first pays | 290/294 | `AVistaLandmarkPaysItsOwn120_PlusTheMappingThresholdsItsRevealCrosses`, `ThePaidThresholdIndexSurvivesReload_AndCannotPayTheSameThresholdTwice` +1 more |
| M07 | the paid-threshold index advances by 1 cell, paying almost every reveal | 287/294 | `ARestoreThatLostProgressCannotRepayWhatTheRewardLedgerAlreadyClaimed`, `AVistaLandmarkPaysItsOwn120_PlusTheMappingThresholdsItsRevealCrosses` +1 more |
| M08 | a mapping reward is escrowed even when its key was already claimed | 293/294 | `ARestoreThatLostProgressCannotRepayWhatTheRewardLedgerAlreadyClaimed` |
| M09 | revealing cells pays no mapping reward at all | 288/294 | `ARestoreThatLostProgressCannotRepayWhatTheRewardLedgerAlreadyClaimed`, `AVistaLandmarkPaysItsOwn120_PlusTheMappingThresholdsItsRevealCrosses` +1 more |
| M10 | every landmark shares one reward key, so only the first pays | 288/294 | `ALandmarkPays120Once_UnderALandmarkKey_NotAMappingKey`, `ARestoreThatLostProgressCannotRepayWhatTheRewardLedgerAlreadyClaimed` +1 more |
| M11 | a landmark reward is escrowed even when its key was already claimed | 293/294 | `ARestoreThatLostProgressCannotRepayWhatTheRewardLedgerAlreadyClaimed` |
| M12 | the paid-threshold index is saved as 0, so a reload can re-earn every threshold | 293/294 | `ThePaidThresholdIndexSurvivesReload_AndCannotPayTheSameThresholdTwice` |
| M13 | the paid-threshold index is restored as 0 | 289/294 | `CampStoresSurviveSaveAndReload_SeparatelyPerCamp`, `ThePaidThresholdIndexSurvivesReload_AndCannotPayTheSameThresholdTwice` +1 more |

**Deliberately not mutation-covered** — both provably equivalent, documented in the harness docstring:

* `MapCoveragePercent` dividing by the whole grid instead of `CountableMask.CountSet()`. The mask counts
  all 9216 cells (see §1), so the two denominators are identical. That the distinction is unobservable is
  itself the KI-048 finding, and it is pinned by a test.
* `MeetsFinaleMapRequirement` using `>` instead of `>=`. 60% of 9216 is 5529.6 cells, so coverage can
  never equal exactly 60.0% and the two comparisons agree on every reachable state. The crossing point
  (5530 cells) is asserted absolutely instead.

---

## 5. Status change this evidence justifies

`Docs/REQUIREMENT_MAP.md` MAP-03's §8.4 clauses move from **IMP** to **VER-H** for the reward mechanics
and the gate, with the mandated progression target recorded as **NOT met** (KI-048) and relic placement
still open (KI-047). Per D-003/D-014 this is headless verification — a real C# 9 compiler, the
.NET Standard 2.1 profile check and NUnit executed outside Unity. It is not a Unity run (KI-016).

MAP-03 is now fully addressed as far as this environment allows: §8.3 (Batch O) and §8.4 (this batch).
What remains open on it is content and world-pipeline work, not missing code.

---

## 6. What this evidence does NOT prove

* **It is not a Unity run.** No PlayMode test, no device build, no rendered map or reward UI (KI-016,
  KI-031).
* **It does not make the 60% gate reachable.** The test measures and pins the miss; closing KI-048 needs
  M3 route content (KI-030) and a bake decision (KI-027/038).
* **Escrow is verified, banking is not re-verified here.** That mapping and landmark rewards land in
  escrow and bank on extraction is covered by `CampaignSessionTests`; this batch asserts the escrow side
  and the reward keys.
* **The reveal numbers are for the canonical seed and current content.** Adding routes, changing the
  slice, or fixing the sea-level bake will move them — which is the point of pinning them.
* **§8.4's "all five relays and the main mission sequence" half of the finale gate is not tested here.**
  `MeetsFinaleMapRequirement()` is coverage-only by design; the relay and mission requirements live in
  `MissionService` (a finale mission requires all five relays) and are covered by the mission tests.

---

## 7. Reproduce

```bash
python3 Tools/Build/headless_cs.py all
python3 Docs/evidence/exploration-rewards-finale-gate-2026-10-21/mutation_check.py   # needs a clean Assets/ tree
python3 Tools/Validation/validate_content.py
```
