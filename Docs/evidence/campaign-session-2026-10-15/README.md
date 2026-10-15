# Campaign session batch evidence — 15 October 2026

Actual sandbox execution on branch `arena/cf9d9328-wayfinder` (base `main` @ `88cd221`).
**Not Unity, device, build or store evidence** (KI-001, KI-016).

## What ran and what it showed

| File | Command | Result |
|---|---|---|
| `headless-console.log`, `compile-report.json` | `python3 Tools/Build/headless_cs.py all` | 7 assemblies / 47 files compiled by Roslyn 3.9 (C# 9) at `-warn:4`: **0 errors, 0 warnings**. New `Wayfinder.Campaign` assembly: 11 files. 6 runtime assemblies pass the .NET Standard 2.1 reference-only profile check. |
| `EditMode.headless.xml` | same (`test` step, NUnitLite 3.13.3) | **109 / 109 passed** (59 pre-existing + 50 new): `CampaignSessionTests` 24 (6 are the per-step interruption cases), `CampaignFieldTests` 10, `ProgressionRegressionTests` 16. |
| `headless-console.log` (tail) | same (`syntax` step) | **24** Unity-dependent files parse as C# 9 with and without Unity symbols (was 21; +`CampaignHost`, +`VerticalSliceDriver`, +`CampaignPlayModeTests`). Parse only — not type-checked. |
| `mutation-check.log`, `mutation_check.py` | `python3 Docs/evidence/campaign-session-2026-10-15/mutation_check.py` (from repo root) | Re-introduces each fixed defect or removes each safeguard **one at a time**, recompiles and reruns the suite, then restores the file. **8 / 8 mutations caught** (each makes specific tests fail). This is the evidence that the new tests detect the bugs they claim to pin. |
| `content-formula-economy.log` | `validate_content.py`, `formula_checks.py`, `economy_sim.py` | All pass. New slice check: income without mapping **3436** vs tiers 1–6 + second camp kit **3130** (margin 306); slice caches supply polymer 4 / scrap 4 / electronics 2 for the second camp kit. `validator-negative-check.log`: the new slice check accepts the real manifest and **rejects 5 / 5** injected faults (in-memory copies; the file is never modified). |
| `python-runner-tests.log` | `python3 -m unittest discover -s Tools/Build -p 'test_*.py' -v` | 23 / 23 pass (mocked Unity processes; unchanged runner). |
| `scratch-unity-stub-typecheck/*` | `python3 Docs/evidence/campaign-session-2026-10-15/scratch-unity-stub-typecheck/run_stub_typecheck.py` | **Scratch only.** The new Unity glue compiles against the *real* campaign assemblies plus hand-written stubs of the few UnityEngine members it touches; two negative controls (removed `Pose` alias → CS0104, misnamed session member → CS1061) are caught. It found that `UnityEngine.Pose` would make `Pose` ambiguous — fixed with an alias before commit. It proves nothing about real Unity API drift. |

## Defects found and fixed by this batch (all reproduced by the mutation check)

| KI | Defect | Mutation that re-introduces it | Tests that catch it |
|---|---|---|---|
| KI-018 | `MissionService.Restore` turned every completed mission back into *Available* after each load; replayed turn-in left the successor locked | restore leaves completed codes Locked | 4 (incl. the full-loop reload test) |
| KI-019 | `InventoryService.Remove` removed items before reporting "insufficient quantity" → failed transfers destroyed items | drop the pre-check | 2 |
| KI-020 | Cache collection granted 1 Salvage unit regardless of `caches.json` (60–300 + materials) | grant 1 unit | 20 |
| KI-021 | Camp establishment consumed the kit without activating the camp; camp deposit moved only scrap metal | drop `ActivateCamp` | 5 |
| KI-022 | Crafting never charged the recipe's Salvage fee (camp kit 80) | fee → no-op | 2 |
| — | New safeguard: transaction-sequence counter persisted/derived on load | counter restarts at 0 | 4 |
| — | New safeguard: in-memory rollback when a critical save fails | drop rollback | 2 |
| — | New safeguard: arrival must first leave the garage volume | arm immediately | 1 |

KI-023 (hidden static singletons) and KI-024 (20-minute day cycle) are pinned by
`ProgressionRegressionTests.Research_TransactionUsesInjectedServiceNotGlobal_KI023`,
`CampaignSessionTests.SaveSlots_AreFullyIsolated_KI023` and
`ProgressionRegressionTests.SimulationTime_DayCycleIs48Minutes_KI024`.

## What the domain tests prove (headless)

- **FLOW-01 / M2-gate loop at the domain level:** new game (saved before movement) → depart →
  collect `cache.forest.001` (77 Salvage, authored) → discover Ranger Rise (120 + vista mapping
  escrow) → return → extraction banks cargo + escrow, turns in Q01 (+150), refuels/repairs →
  research Trail Tires I (−200) → reload is byte-identical (canonical JSON) and Q01 stays
  *Completed*.
- **QA case 4 at campaign level:** banking interrupted after each of the six `SaveService`
  steps (`Begin`, `TempWritten`, `TempVerified`, `Backup2Rotated`, `Backup1Rotated`,
  `CurrentReplaced`) reloads to exactly the old or exactly the new complete state, never mixed;
  retrying from the old state pays once. A failing write rolls memory back and never announces
  the rolled-back banking.
- **Vertical slice ledger:** Q01–Q04, all 12 slice caches (banking whenever the Scout is full),
  six landmarks, tiers 1–6, a crafted second camp kit and `camp.forest.b`; final wallet equals
  Σcaches + 6×120 + Σmissions + mapping − 3050 − 80 exactly, and the finished slice reloads
  identically.
- Rescue (25 % of loose salvage only; free in tutorial and Relaxed), zero-resource recovery,
  camp atomicity with its save, camp deposit, autosave debounce/retry/suspend, resume at the last
  safe pose, corrupt-current → newest valid backup, occupied-slot replacement, slot isolation,
  full-cargo typed failure, field prompts/holds/observation/checkpoints/arrival.

## Not evidence of

Unity compilation of `Platform/CampaignHost.cs`, `Presentation/TitleFlowHost.cs`,
`Presentation/VerticalSliceDriver.cs`, `Editor/SceneBuilder.cs`, `Editor/ProjectSetup.cs`;
the two new PlayMode tests and two new editor tests; the generated VerticalSlice scene; physics/driving feel; touch
ergonomics; device frame time/thermal; Android build. M2's gate ("playable 30-minute slice +
interruption tests on device") is **not** passed by this batch.
