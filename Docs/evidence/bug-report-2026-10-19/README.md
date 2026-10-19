# Bug-report fix pass — 2026-10-19

Evidence for closing `Docs/AI_BUG_REPORT.md` (DEF-001 … DEF-025). This folder is the "after"
picture; `Docs/evidence/ai-review-2026-10-16/` remains the "before" picture and is deliberately
left untouched, including `BugProbe.cs`, whose characterization assertions now fail on purpose.

## What was run

| gate | command | result |
|---|---|---|
| compile (7 engine-free assemblies) | `python3 Tools/Build/headless_cs.py compile` | OK — 0 errors, 0 warnings (`compile-report.json`) |
| EditMode tests | `python3 Tools/Build/headless_cs.py test` | **188/188 pass** (`EditMode.headless.xml`) — 164 pre-existing + 24 new in `Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs` |
| .NET Standard 2.1 profile | `headless_cs.py profile` | OK |
| syntax check of 30 Unity-dependent files | `headless_cs.py syntax` | OK, 0 syntax errors |
| all of the above | `headless_cs.py all` | `SUMMARY compile=OK tests=OK netstandard2.1-profile=OK syntax=OK` |
| Unity-API type check of all 12 assemblies | `python3 Tools/Build/unity_type_check.py` | `SUMMARY unity-type-check=OK`, 12/12 assemblies, 0 warnings (`unity-type-check.json`) |
| content validation | `python3 Tools/Validation/validate_content.py` | 0 errors, 0 warnings |
| formula checks | `python3 Tools/Validation/formula_checks.py` | 0 failures |
| build-tool unit tests | `python3 -m unittest discover -s Tools/Build -p 'test_*.py'` | 114 tests, OK (`gates.log`) |
| the original bug probe | `python3 Docs/evidence/ai-review-2026-10-16/run_bug_probe.py` | **15 tests, 5 passed, 10 failed** (`bug-probe-after-fixes.log`) |

## The probe result is the point

The probe asserted the *broken* behaviour so each finding was reproducible. Ten of its fifteen
assertions now fail, which is the inversion step 8 of the report's fix plan asked for:

```
FAIL DEF01_LowRangeAndDiffLockFlagsAreNeverSet
FAIL DEF01_VehicleStallsIn25cmOfWaterBecauseIntakeHeightIsZero
FAIL DEF01_WadingDepthIsAlwaysZero
FAIL DEF02_ResearchApplyOrderIsHashSetOrderNotTierOrder
FAIL DEF04_DoubleCollectGuardDependsOnDiscoveryBeingWired
FAIL DEF04_UnknownItemIdInPickupContentThrowsOutOfTransactionExecute
FAIL DEF05_OnlySomeCommandsEmitCommittedEvents
FAIL DEF06_NonHomeGaragesAreNeverUnlockedAndFullCampaignCannotProgress
FAIL DEF07_BitGridFromBase64DoesNotValidatePayload
FAIL DEF09_ChunkFailedTransitionLosesItsFailureDetail
```

The five that still pass are explained one by one in §6b of the report — none of them is a defect
that was left in place; each is a probe that can no longer observe the code path it was written
against (three of them call APIs the fix deliberately kept for compatibility, one exercises a
legacy overload, one measures a continuous field that is continuous by construction).

## Mutation checks (are the new tests falsifiable?)

* `BaselineSafeWadingM = 0.55f` → `0.10f` in `Simulation/HazardModels.cs` makes
  `formula_checks.py` fail with `water: BaselineSafeWadingM = 0.55 (§5.3) — C# says 0.1`, then
  reverted. Before this pass the same check read `0.55 == 0.55`.
* A mission objective retargeted to `site.canyon.nowhere` makes `validate_content.py` fail with
  `mission Q05: objective VisitVolume targets 'site.canyon.nowhere', which no catalog defines`.

## Honest limits

* DEF-005, DEF-006, DEF-013, DEF-014 and DEF-015 live in Unity-facing files. They are fixed and
  type-checked against `Tools/Build/unity_api_stubs/` (12/12 assemblies clean), but **no Unity
  compiler has run on them** (KI-034). They need one PlayMode/editor run to confirm.
* `unity_type_check.py` proves the absence of the compile errors its stubs can see. The stubs were
  shaped by the errors the real code produced, so a clean run is not a Unity build.
* Content added by this pass: `Assets/Wayfinder/Content/sites.json` (23 sites). The four relays sit
  on the §6.1 region anchors; the other 19 are procedural placements recorded with
  `"provenance": "procedural"` and meant to be replaced by level design. Contract sockets
  (`contract_socket.*`) are still not placed anywhere in the world.
