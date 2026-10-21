# Batch O — MAP-03 (§8.3): relic sets from content, and the sensor loop that consumes `SignalRadiusM`

**Date:** 2026-10-21 · **Branch:** `arena/0754e0e3-wayfinder` · **Decision:** D-045 · **New issue:** KI-047
**Requirement:** MAP-03, blueprint §8.3 (`Wayfinder_Production_Blueprint.txt:566`), part of §8.4

---

## 1. What this batch verified, and what it found

MAP-03 was the last dependency-ready M2 gap — its files are engine-free, unlike DRIVE-04's
`Presentation/PresentationModels.cs` and `Platform/InputRouter.cs`, which need Unity. Recon split §8.3
into three different states of "implemented":

| §8.3 clause | State before this batch | State after |
|---|---|---|
| Five relic sets, one per biome, five named relics each | **No code.** `DiscoveryService` held a `HashSet<string>` behind an unvalidated `TryAddRelic(string)`. `relics.json` was validated by `validate_content.py` and loaded by nobody — `CampaignContent.RequiredFiles` did not list it | Loaded, parsed into `RelicDefinition`, grouped per biome and Ordinal-sorted; `CollectRelic` validates against content |
| Relics are protected, weightless, unsellable, unlock cosmetics not traction | Nothing enforced it | Ledger-only (no cargo slot, no mass, no catalog entry, so no sell path reaches one); a completed set moves none of eight resolved stats |
| Sensor pulses at 80 m, precision at 45 m, Radar I 140 m, Radar II 220 m | **Composed and consumed by nothing.** `VehicleStatsResolver` set the 80 m baseline, `ResearchService` raised it to 140/220 m, `upgrades.json` matched §11.2:726/:736, and a determinism test pinned the numbers — but no behaviour read them, so both paid radar upgrades were inert | `Campaign/Map/SensorLoop.cs` is the first consumer; `CampaignSession.SensorScan` feeds it the resolved radius |
| "The map shows a search area, not exact vertical navigation" | `MapMarkerRules.SearchAreaCenter` existed for markers only | The sensor reuses that same centre, so §8.3 has one implementation and the sensor and map cannot disagree |
| §8.4 mapping rewards and the 60% finale gate | Implemented, partly tested | Unchanged — still outstanding (see §5) |

**The finding worth recording:** a composed stat can have zero consumers and still be pinned by a green
test. `SignalRadiusM` was correct at every tier and nothing behaved differently because of it. Grepping
for *definitions* said "implemented"; grepping for *consumers* said otherwise.

---

## 2. Commands executed and results

```bash
python3 Tools/Build/headless_cs.py compile   # 7 assemblies, 0 errors, 0 warnings
python3 Tools/Build/headless_cs.py test      # Overall result: Passed — 282/282
python3 Tools/Validation/validate_content.py # 0 error(s), 0 warning(s) — CONTENT VALIDATION PASSED
python3 Tools/Build/headless_cs.py syntax    # 30 Unity-dependent file(s), 0 syntax error(s)
```

Suite: **266 → 282** (16 new tests in `CampaignRelicSensorTests`). No existing test was weakened or
deleted; `relics.json` joining `RequiredFiles` caused no churn because both custom `FromJson` helpers in
the suite enumerate `Directory.GetFiles(ContentDir, "*.json")`.

Artifacts in this directory: `headless-console.log`, `python-checks.log`, `compile-report.json`,
`EditMode.headless.xml` (copied from `Logs/headless/` after a clean `headless_cs.py all`, never after a
mutation run), `mutation_check.py`, `mutation-check.log` (final run), `mutation-check-run1.log` (first
run, 21/23).

---

## 3. Mutation check

`mutation_check.py` breaks one rule at a time in the three files that carry §8.3, recompiles, runs the
whole EditMode suite, and requires at least one failing test. It refuses to start on a dirty `Assets/`
tree and verifies it left the tree clean.

**Run 1: 21/23 caught, EXIT 1.** Two survivors, both the same *test* weakness rather than equivalent
mutants:

* **M15** (the pose overload sweeps the world origin instead of the vehicle) and **M16** (the sensor
  sweeps even when parked) survived because at a new game the vehicle's pose *is* the origin and no
  placed site is within 80 m of it. A sweep that ignored the pose and read `(0,0)` was therefore
  indistinguishable from one that followed the vehicle, and a sweep that ignored the parked state still
  returned zero hits — the assertions passed for the wrong reason.

Fixed in the test, not waved off: `TheSensorSweepsFromTheVehiclesOwnPose_AndIsSilentWhenParked` now
places the pose directly on a placed site, so (a) a **parked** session must stay silent while a signal
is underneath it, (b) a **driving** session must hear it, (c) the pose overload must agree with the
explicit one at that position, and (d) driving 300 m away must make the same site go quiet. Commit
`b545890`.

**Run 2 (final): **23/23 caught, survivors: none, tree clean after run, EXIT 0** (`mutation-check.log`). Every mutant was caught by at least one failing test; none needed a compile failure to be caught.**

| Mutant | What it breaks | Suite | Caught by |
|---|---|---|---|
| M01 | the pulse test excludes the boundary (80 m exactly goes quiet) | 280/282 | `TheScanReportsNearestFirst_AndOnlyPulsingSignals`, `TheSensorPulsesInsideTheResolvedRadius_AndNotBeyond` |
| M02 | the baseline sensor reaches only 45 m | 276/282 | `AMissingSignalRadiusFallsBackToTheBaseline_NotToSilenceOrToUnlimitedRange`, `BeyondFortyFiveMetresTheMapShowsTheSearchAreaCentre_NotTheExactPosition` +1 more |
| M03 | precision arrives at 80 m instead of 45 m | 279/282 | `BeyondFortyFiveMetresTheMapShowsTheSearchAreaCentre_NotTheExactPosition`, `DirectionalPrecisionArrivesAtFortyFiveMetres_AndWithItTheTruePosition` +1 more |
| M04 | precision follows the resolved radius, so Radar II is precise at 220 m | 280/282 | `BeyondFortyFiveMetresTheMapShowsTheSearchAreaCentre_NotTheExactPosition`, `DirectionalPrecisionArrivesAtFortyFiveMetres_AndWithItTheTruePosition` |
| M05 | a bearing is reported even beyond the precision range | 281/282 | `BeyondFortyFiveMetresTheMapShowsTheSearchAreaCentre_NotTheExactPosition` |
| M06 | the bearing transposes east and north | 281/282 | `DirectionalPrecisionArrivesAtFortyFiveMetres_AndWithItTheTruePosition` |
| M07 | a distant signal reveals its exact position | 281/282 | `BeyondFortyFiveMetresTheMapShowsTheSearchAreaCentre_NotTheExactPosition` |
| M08 | a missing radius deafens the sensor instead of falling back to 80 m | 281/282 | `AMissingSignalRadiusFallsBackToTheBaseline_NotToSilenceOrToUnlimitedRange` |
| M09 | a missing radius becomes unlimited range | 281/282 | `AMissingSignalRadiusFallsBackToTheBaseline_NotToSilenceOrToUnlimitedRange` |
| M10 | the scan reports out-of-range signals too | 278/282 | `RadarIAndRadarIIExtendWhatTheSensorHears_EndToEndThroughResearch`, `TheScanReportsNearestFirst_AndOnlyPulsingSignals` +1 more |
| M11 | the scan reports farthest first | 281/282 | `TheScanReportsNearestFirst_AndOnlyPulsingSignals` |
| M12 | Radar I is recorded as 150 m instead of 140 m (§11.2 tier 9) | 281/282 | `RadarIAndRadarIIExtendWhatTheSensorHears_EndToEndThroughResearch` |
| M13 | Radar II is recorded as 140 m, so tier 19 changes nothing (§11.2 tier 19) | 281/282 | `RadarIAndRadarIIExtendWhatTheSensorHears_EndToEndThroughResearch` |
| M14 | SensorScan ignores the resolved radius and always uses the 80 m baseline | 281/282 | `RadarIAndRadarIIExtendWhatTheSensorHears_EndToEndThroughResearch` |
| M15 | the pose overload sweeps the world origin instead of the vehicle | 281/282 | `TheSensorSweepsFromTheVehiclesOwnPose_AndIsSilentWhenParked` |
| M16 | the sensor sweeps even when parked | 281/282 | `TheSensorSweepsFromTheVehiclesOwnPose_AndIsSilentWhenParked` |
| M17 | a relic's own reward key is never claimed (§8.4 separate keys) | 281/282 | `ARelicIsCollectedOnce_AndClaimsItsOwnRewardKeyOnce` |
| M18 | a completed set claims no set reward key | 280/282 | `CompletingABiomesSetClaimsTheSetKeyOnce_AndAnnouncesIt`, `RelicsAndCompletedSetsSurviveSaveAndReload` |
| M19 | any relic at all completes its biome's set | 279/282 | `ARelicIsCollectedOnce_AndClaimsItsOwnRewardKeyOnce`, `CompletingABiomesSetClaimsTheSetKeyOnce_AndAnnouncesIt` +1 more |
| M20 | set progress counts the set's members instead of the ones found | 278/282 | `ARelicIsCollectedOnce_AndClaimsItsOwnRewardKeyOnce`, `CollectingARelicFillsTheLedger_NotTheCargo` +1 more |
| M21 | the same relic can be collected again | 280/282 | `ARelicIsCollectedOnce_AndClaimsItsOwnRewardKeyOnce`, `CompletingABiomesSetClaimsTheSetKeyOnce_AndAnnouncesIt` |
| M22 | an unknown relic is refused as a bad target, not as missing content (§14) | 281/282 | `AnUnknownRelicAndNoActiveExpeditionAreBothRefused` |
| M23 | recovering a relic is silent — no per-relic notice | 281/282 | `CompletingABiomesSetClaimsTheSetKeyOnce_AndAnnouncesIt` |

**Deliberately not mutation-covered** (documented in the harness docstring):

* The `?? true` / `?? false` defaults for `protectedLedger` and `sellable` in `CampaignContent`'s relic
  parse — every relic in `relics.json` sets both keys explicitly, so no current content can reach a
  default. They stay as defence for content that forgets a key.
* The Ordinal sort of each relic set — `relics.json` already lists every biome in sorted order
  (verified: `forest` file order `01..05` == sorted), so removing the sort changes nothing observable.
  The order is still asserted by a test, which pins the contract for content that is *not* pre-sorted.
* `CollectRelic`'s `RunCritical` wrapper — the critical-save path is covered by the session's existing
  save tests, not by a relic-specific one.

---

## 4. Status change this evidence justifies

`Docs/REQUIREMENT_MAP.md` MAP-03 moves from **IMP** to **VER-H** for the §8.3 clauses above (relic
ledger and sensor ranges), with the §8.4 clauses and relic placement recorded as still **IMP** and as
KI-047. Per D-003/D-014 this is headless verification — a real C# 9 compiler, the .NET Standard 2.1
profile check and NUnit executed outside Unity. It is not a Unity run (KI-016).

---

## 5. What this evidence does NOT prove

* **It is not a Unity run.** No PlayMode test, no device build, no rendered map. `SensorLoop` is pure
  and the session APIs are engine-free, but nothing here proves a HUD draws the readings (KI-016,
  KI-031).
* **Relics are placed nowhere in the world.** `relics.json` has no coordinates, no `sites.json` kind is
  a relic (kinds are inspect 13, relay 4, route 3, questItem 1, delivery 1, visit 1), and
  `vertical_slice.json` places no relic socket. `CollectRelic` is reachable and correct, and the sensor
  sweeps the sites content *does* place, but a player in the shipped slice is never told where a relic
  is. That is **KI-047**, and closing it is M3 world-pipeline work (KI-037/038).
* **§8.4 is not finished.** No test asserts `MapCoveragePercent` or `MeetsFinaleMapRequirement`, and the
  target-progression test §8.4 explicitly mandates — "story routes plus reasonable detours can achieve
  60%; otherwise adjust route exposure or the threshold" — does not exist yet. Mapping rewards are only
  partly covered (`CampaignSessionTests:66`, `:499`).
* **Cosmetic memorabilia and journal content do not exist.** §8.3 says a completed set unlocks them.
  The reward keys are claimed and nothing consumes them: there is no cosmetic or journal service, only a
  `PurchaseCosmetic` command, a `CosmeticNotification` event and Presentation tabs.
* **The 45 m precision range is unverified against feel.** It is §8.3's number and it is now the only
  thing that gates a trustworthy bearing, but no playtest has confirmed 45 m is the right distance in
  practice.

---

## 6. Reproduce

```bash
python3 Tools/Build/headless_cs.py all
python3 Docs/evidence/relics-sensor-loop-2026-10-21/mutation_check.py   # needs a clean Assets/ tree
python3 Tools/Validation/validate_content.py
```
