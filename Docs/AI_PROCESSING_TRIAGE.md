---
doc_id: wayfinder-ai-processing-triage
doc_version: 1
date: 2026-10-20
source_document: "AI Processing Format.md (8-issue schema, `total_issues: 8`)"
branch: arena/a9ae82a2-wayfinder
method: read the source of every named file at HEAD, then implement only what the code actually lacks
---

# AI processing format — triage, fixes and evidence

## 0. How to read this document

The attached `AI Processing Format.md` is an issue directory with eight entries
(`SYS-SIM-001/002`, `SYS-SAV-001/002`, `SYS-WRD-001/002`, `SYS-CICD-001/002`). Each entry names a
target path, a failure mode, a root cause and a "machine instruction" list.

**Every entry was checked against the real repository before changing anything**, because seven of
the eight named files sit at a different path here, and several already contained the mechanism the
entry asks for:

| Reported path | Reality at `HEAD` |
|---|---|
| `Assets/Scripts/Simulation/Vehicle/VehicleController.cs` | `Assets/Wayfinder/Scripts/Simulation/VehicleController.cs` |
| `Assets/Scripts/Simulation/Fuel/FuelModel.cs` | `Assets/Wayfinder/Scripts/Simulation/FuelModel.cs` |
| `Assets/Scripts/Persistence/SaveService.cs` | `Assets/Wayfinder/Scripts/Persistence/SaveService.cs` |
| `Assets/Scripts/Persistence/SaveDtos.cs` | `Assets/Wayfinder/Scripts/Persistence/SaveDtos.cs` |
| `Assets/Scripts/World/Streaming/ChunkStreamPlanner.cs` | `Assets/Wayfinder/Scripts/World/ChunkStreamPlanner.cs` |
| `Assets/Scripts/World/Map/MapMarkerService.cs` | `Assets/Wayfinder/Scripts/Progression/MapMarkerService.cs` |
| `.github/workflows/android-cloud-build.yml` | exists, as described |
| `Assets/Editor/Build/BuildCommands.cs` | `Assets/Wayfinder/Scripts/Editor/BuildCommands.cs` |

A wrong path is not by itself evidence that the defect is absent, so each claim was re-derived from
the code. The table below is the verdict. "Already satisfied" means the named mechanism exists and
is pinned by an executed test *today*; it does **not** mean the surrounding feature is finished.

| ID | Verdict | What changed in this batch |
|---|---|---|
| SYS-SIM-001 | **Partly real — fixed** | Undefined surface labels now fall back instead of indexing the table; every friction coefficient and stiffness is clamped/NaN-safe; the §4.4 0.15 s cross-fade (written in `SurfaceTable.Blend` but never called) is now actually applied per wheel; a zero/NaN physics step no longer reaches the maths |
| SYS-SIM-002 | **Not adopted as specified — the underlying gap fixed instead** | No `IdleBurnRate` multiplier (it would break §5.1 and its QA example). The blueprint's own rule — auto-stop after 15 s parked — was declared (`FuelModel.AutoStopIdleSeconds`) and unused; it is now implemented and wired, so parked drain really becomes zero |
| SYS-SAV-001 | **Already satisfied — hardened** | Temp-file write → verify → atomic replace already existed with interruption tests at every step. Added: a stale `.tmp` from a killed process is cleared before writing and never trusted, and no `.tmp` is left behind after a failed write |
| SYS-SAV-002 | **Already satisfied — defects closed** | `schemaVersion` + a sequential migration hook already existed; both `saveVersion` and the interface shape were deviations. Real defects fixed: a **missing migration step silently relabelled the save** instead of failing, and a mid-pipeline failure left a **half-applied payload**. Pipeline is now atomic and gap-checked, with an `ISaveMigrator` contract |
| SYS-WRD-001 | **Not applicable to the named file** | `ChunkStreamPlanner` is a pure, allocation-free planner with a per-step activation budget and no `Update()` at all; there is no main-thread matrix work in it and no Unity host yet (KI-034/KI-006). Nothing was "moved to the Job System", because there is nothing on the frame path — inventing a Jobs rewrite would be unverifiable and unsafe in an engine-free assembly |
| SYS-WRD-002 | **Partly real — fixed at the right layer** | `MapMarkerService` never polled and has no `Update()`. The actual polling was in the view (`MapPresenter.Tick`, 4 Hz rebuild). Added a domain `CampaignSession.MapRevision` dirty counter raised only by committed changes; the presenter now rebuilds on the commit frame and keeps the timer only for the moving player arrow |
| SYS-CICD-001 | **Real — fixed** | `ProjectSetup` never set `minSdkVersion`/`targetSdkVersion`, so Unity's "Auto" resolved per image. Both are now pinned constants (29/36), logged into the build log, passed to unity-builder, and a repository test fails if the workflow and the C# constant ever disagree |
| SYS-CICD-002 | **Real — fixed** | `BuildPipeline.BuildPlayer` failure threw a `BuildFailedException` but never exited. Batchmode now records the breadcrumb and calls `EditorApplication.Exit(1)`; the interactive editor still throws instead of dying |

Severity mapping used below (from the source document): Critical/High/Medium. Statuses follow
`Docs/IMPLEMENTATION_STATUS.md`: **Implemented** = real source, not compiled by Unity here;
**Verified (headless)** = compiled by Roslyn 3.9 at `-warn:4` with zero warnings, API-profile
checked against .NET Standard 2.1, and executed by NUnit outside Unity (D-014).

---

## 1. SYS-SIM-001 — surface transitions (High) → fixed

**What was real.** Three separate hazards, each reachable from serialized data rather than from the
reported "high speed":

1. `SurfaceTable.Get` was `Table[(int)surface]`. A `SurfaceLabel.surface` whose enum value is
   out of range — an inspector edit, or a baked chunk map written by an older content build —
   throws `IndexOutOfRangeException` inside `FixedUpdate` and kills the physics tick.
   `SetSurfaceAtWheel(int, SurfaceType)` had no index bound either.
2. `SurfaceCoefficients` accepted any float. `MathUtil.Clamp` propagates NaN (both comparisons are
   false), and a NaN `WheelCollider.forwardFriction.stiffness` makes PhysX silently discard that
   wheel's friction — which reads exactly like "grip disappears at a seam".
3. `SurfaceTable.Blend` documented the §4.4 0.15 s cross-fade but **had no call sites**.
   `VehicleController` wrote the raw coefficient of whichever surface the wheel was on, once per
   `FixedUpdate`. At 20 m/s a 3 m patch is crossed in 0.15 s, so the entire transition happened
   between two ticks: a step change, i.e. a jolt.

**What changed.**

| File | Change |
|---|---|
| `Simulation/SurfaceTable.cs` | `IsDefined` (range **and** row agrees with the enum), `Sanitize`, `TryGet`, safe `Get`, `ClampFrictionStiffness`, NaN-safe `Blend`; coefficients clamped in the constructor (grip ≤ 1.25, wet ∈ [0,1], Crr ≤ 0.20) |
| `Simulation/SurfaceTransition.cs` *(new)* | Per-wheel cross-fade struct: 0.15 s, frame-rate independent, restart-from-current on a second boundary, `immediate`/`SnapTo` for the §4.4 water path, no allocation |
| `Simulation/VehicleController.cs` | Per-wheel fade stepped each tick; composed grip scaled by the blended/raw ratio (so tire-set, research and condition bonuses ride through the fade); every stiffness clamped; airborne wheels keep their last surface instead of resetting to packed dirt; `dt ≤ 0`/NaN guarded; bounds-checked, sanitised `SetSurfaceAtWheel` |

**Deliberately not done.** No Unity Job System, no `Task.Run`, and no change to how `WheelCollider`
friction is written: `VehicleController` is a Unity-only file that cannot be compiled or run here
(KI-034), so a rewrite of the actuation path could not be verified. The maths that decides the
coefficients is engine-free, and that is the part that got moved, clamped and tested.

**Evidence.** `SurfaceLookup_UndefinedLabelFallsBackInsteadOfIndexingPastTheTable`,
`SurfaceTable_EveryDeclaredLabelHasItsOwnRow`,
`SurfaceCoefficients_AreClampedAndNaNNeverReachesTheSolver`,
`SurfaceTransition_FadesOverFifteenHundredthsAndSnapsForWater`,
`SurfaceTransition_IsFrameRateIndependentAndSurvivesBadDeltas`
(`Docs/evidence/ai-processing-2026-10-20/EditMode.headless.xml`). Mutations M1 and M2
(`mutation-check.log`) fail these tests when the fallback or the fade is removed.

**Still open.** The physics *feel* of the fade (does 0.15 s read as smooth at 60 Hz on a phone?) is a
PlayMode/handling question. It is **unverified** and belongs to KI-009/KI-034.

---

## 2. SYS-SIM-002 — idle fuel (Medium) → fixed differently, on purpose

**Why the requested fix was rejected.** The blueprint (§5.1) fixes the drain formula:

```
litersPerSecond = 0.003 + 0.025·throttle + 0.015·load + 0.004·slipSeverity
```

with a QA example (throttle 0.45, load 0.50, slip 0.20 → 0.02255 L/s) that `SimulationTests` pins.
Injecting an `IdleBurnRate` multiplier "when throttle < 0.01" changes a specified formula and
breaks that example. The very next sentence of §5.1 supplies the intended rule:

> Idle at zero throttle still consumes the base amount **unless auto-stop is enabled after 15
> seconds parked.**

`FuelModel.AutoStopIdleSeconds = 15f` existed and **no code read it**. So a parked vehicle burned
0.003 L/s (≈10.8 L/h — a 24 L tank empty in a bit over two hours of standing still) and the
documented behaviour was unreachable.

**What changed.** `Simulation/IdleAutoStop.cs` (new, pure): accumulates simulated time only while
the engine is on, throttle ≤ 0.01, brake released and speed ≤ 0.2 m/s; fires exactly once at 15 s;
a pedal wakes it; `Clear()` releases the latch without restarting; `Progress` is exposed for a HUD
hint. `VehicleController` steps it at the top of `FixedUpdate` (before torque is applied), stops the
engine when it fires, restarts it on a pedal only if the engine is not disabled and fuel remains, and
clears the latch on any explicit `SetEngineOn(true)`. The drain formula is untouched.

**Evidence.** `IdleAutoStop_StopsTheEngineAfterFifteenSimulatedSecondsParked`,
`IdleAutoStop_DoesNotStopWhileDrivingThrottlingOrBraking`,
`IdleAutoStop_PedalRestartsAndParkedFuelDrainIsZero`,
`IdleAutoStop_ClearReleasesTheLatchWithoutRestarting`; `FuelModel` tests and the §5.1 formula checks
still pass unchanged (`formula-checks.log`). Mutation M3 kills the suite when the 15 s rule is
removed.

**Still open.** Auto-stop is wired but Unity-only (parse-checked, not compiled here), and there is no
audio/HUD cue yet. Recorded as KI-046.

---

## 3. SYS-SAV-001 — atomic writes (Critical) → already satisfied, hardened

Checked line by line against the report's three instructions:

| Instruction | Reality at `HEAD` |
|---|---|
| 1. Write to a `.tmp` buffer first | `Save()` writes `current.json.tmp` through a `FileStream` with `stream.Flush(true)` |
| 2. Atomic replacement after verification | `VerifyFile(temp)` reparses and re-checksums **before** `File.Replace(temp, current, backup1)` (or `File.Move` when there is no current). Backups rotate first |
| 3. Try-catch with backup fallback | IO/access/format/JSON/argument failures return typed `SaveResult`s; `LoadBestValid` walks current → backup1 → backup2, rejecting incomplete files, mismatched slot ids and newer schemas |

The existing tests already simulate the process dying at each step through `SaveService.StepHook`
(`InterruptedWrite_NeverMixesStates_QA4`, `BackupRecovery_LoadsLatestValid_QA5`,
`SchemaTooNew_OpensReadOnly_QA19`). So "unhandled process suspension corrupts the primary save"
did not reproduce: the primary is never written in place.

**What was genuinely missing** was cleanup of the *leftover* buffer: a `.tmp` from a killed process
survived every later save (harmless in practice because `FileMode.Create` truncates it, but a
read-only stale file would fail the write), and an `IOException` from the replace left the buffer in
the slot directory for the next boot to explain. `CleanupStaleTemp(slot)` now runs before every
write and in both IO catch blocks.

**Evidence.** `StaleTempBufferFromAKilledProcessIsClearedAndNeverTrusted`,
`FailedReplaceLeavesNoTempBufferBehind`, plus the existing interruption tests.

---

## 4. SYS-SAV-002 — versioning & migration (High) → already satisfied, two defects closed

The root DTO already carries an integer `schemaVersion` (`SaveEnvelope`), and an ordered payload
transform hook existed. Two deviations are deliberate and recorded as **D-038**:

* the field is named `schemaVersion`, not `saveVersion` — renaming it would rewrite every existing
  save for a naming preference. A payload that has `schemaVersion: 1` is a valid save today;
* steps are payload transforms behind a small registry rather than a bespoke `V1_To_V2`/`V2_To_V3`
  class pair. The pipeline is now expressed as `ISaveMigrator` (`FromVersion`, `Apply(JObject)`), and
  `SaveMigration` keeps a step a one-line lambda.

Two real defects were found while verifying it:

1. **A missing step silently relabelled the save.** The old loop incremented `schemaVersion` even
   when `Steps` had no entry, so a v1 save read by a v3 build became "v3" with no transformation and
   was then written back as v3. The next load would trust un-migrated data. It now throws
   `InvalidOperationException("no save migration from vN to vN+1")`.
2. **A mid-pipeline failure left a half-applied payload.** Steps mutated `envelope.payload` one at a
   time. The pipeline now runs against a working `JObject` and commits the payload and version only
   when the whole chain succeeds.

`ValidatePipeline(targetVersion, steps)` reports gaps, duplicate starts and steps that can never run.

**Evidence.** `MigrationPipeline_RunsSequentialStepsAndKeepsStableIds`,
`MigrationPipeline_MissingStepFailsLoudly_QA19`,
`MigrationPipeline_ValidateReportsGapsDuplicatesAndDeadSteps`,
`Migration_SchemaNewerThanThisBuildIsStillRefused`, `Migration_PreservesStableIds_QA19`. Mutation M4
kills the suite when the gap check is removed.

**Still open.** The built-in registry is empty because schema 1 is current; the pipeline is proven
with synthetic v1→v2→v3 steps, not with a shipped v2 (KI-048).

---

## 5. SYS-WRD-001 — "main thread hitching in chunk streaming" (High) → not applicable to the named file

`ChunkStreamPlanner` is not a `MonoBehaviour` and has no `Update()`; it contains no mesh generation
and no visibility matrix. It is a pure state machine over 24 × 24 chunks that:

* requests a 3 × 3 ring plus a prefetch lead along the velocity,
* activates inside 256 m and retains to 384 m, unloads beyond 640 m (hysteresis, D-034),
* never unloads pinned chunks,
* **budgets activations per step** (`DefaultActivationsPerStep = 2`, exercised by
  `WorldStreamingTests`), and
* keeps a handle ledger so the host can prove one handle per live chunk.

Grep evidence: no `Update()`, `Task`, `Job`, or `Coroutine` in the file. The Unity host that would
execute those commands **does not exist yet** — that is KI-034's missing piece, not a main-thread
defect to slice. Adding `IJobParallelFor` code to a pure assembly with no Unity reference would not
compile and could not be tested, so it was not done. What the report is really asking for (bounded
per-frame work, no synchronous bake on the frame path) is already the design.

**Recommendation if this is re-opened:** implement the host as a coroutine that drains
`Planner.Step(...)` commands within a frame budget, and add a PlayMode test that crosses a chunk
boundary at 20 m/s and asserts the per-frame instantiation count never exceeds the budget.

---

## 6. SYS-WRD-002 — marker state desync (Medium) → real, fixed at the domain layer

`Progression/MapMarkerService` is a pure service: no `Update()`, no polling, no Unity types. Its
state is restored tolerantly and its ids are stable (`marker.N`).

The reported *symptom* was real, though, one layer up: `Presentation/MapTextureBinding`
(`MapPresenter.Tick`) rebuilt every marker on a 0.25 s timer, whether or not anything had changed, so
a marker placed by the player could take up to a quarter second to appear and every poll re-laid out
every icon. That is the "inefficient frame polling" the report points at, in the file it names
indirectly (`MapMarkerService` is its data source).

**What changed.** `CampaignSession.MapRevision` — a monotonic counter raised **only** by committed
changes: marker placed/removed, route target changed, cells revealed, landmark discovered, cache
collected, and a restored save (`InvalidateMap()`). Rejected commands and re-selecting the same
route target do not raise it. `MapPresenter` rebuilds when the number moves and keeps the 0.25 s
cadence only for the player arrow, which genuinely moves every frame.

**Evidence.** `MapRevision_IsADirtyFlagRaisedByCommitsOnly_SYS_WRD_002` (marker add/remove/route,
rejected command, duplicate route, reveal, no-op reveal, reload). Mutation M5 kills it.

**Still open.** The view-side change is parse-checked only (Unity-only file). No PlayMode proof that
a marker appears on the commit frame on a device (KI-047).

---

## 7. SYS-CICD-001 — non-deterministic Android builds (High) → fixed

`ProjectSetup` set scripting backend, architecture and identifier — but **never** `minSdkVersion` or
`targetSdkVersion`, so both stayed on Unity's "Auto" and resolved from whatever SDK the editor image
carried. Two runs of the same commit could therefore target different API levels.

**What changed.**

| Layer | Change |
|---|---|
| `Editor/ProjectSetup.cs` | `AndroidMinSdkApiLevel = 29` (blueprint OS floor: Android 10+) and `AndroidTargetSdkApiLevel = 36` as named constants; `PinAndroidSdkLevels()` writes both into `PlayerSettings` and prints the resolved values into the setup breadcrumb, so the build log states the levels it used |
| workflow `env` | `ANDROID_TARGET_SDK: '36'`, commented with why it is duplicated |
| both `unity-builder` steps | `androidTargetSdkVersion: ${{ env.ANDROID_TARGET_SDK }}` |
| `Tools/Build/test_build_diagnostics.py` | fails if `ProjectSetup` mentions `AndroidApiLevelAuto`, if either setting disappears, if the workflow stops declaring the level, if the two numbers disagree, or if only one Unity step receives it |

**What was deliberately not claimed.** The JDK, NDK and SDK *build tools* are supplied by the pinned
editor image (`unityci/editor:<UNITY_VERSION>-android`), so they are pinned transitively by
`UNITY_VERSION`; a `setup-java` step on the host runner would **not** change the JDK Unity uses, and
a `~/.gradle` cache step would not cover the container's home directory. Both were rejected as
theatre and are explained in the workflow comments instead. The real remaining risk is that the
pinned target level is not installed in the image — that fails loudly at build time (KI-044).

---

## 8. SYS-CICD-002 — error exit propagation (High) → fixed

At `HEAD`, `BuildCommands.LogReport` inspected `report.summary.result` and threw a
`BuildFailedException`, and every entry point already ran through `EditorFailureLog.Run`, which
records a breadcrumb and rethrows. So the "passes silently" half was **not** reproducible — a thrown
exception fails a `-executeMethod` run.

What was missing is the part the report names explicitly: an **unconditional non-zero exit**.
`BuildPlayer` can return `BuildResult.Failed` and some batchmode configurations still exit 0, which
turns a failed APK build into a green workflow with no artefact. `FailBuild(platform, report)` now:

1. appends the result line to `Logs/build_report.txt` (unchanged),
2. records the cause through `EditorFailureLog` into `Logs/wayfinder-editor-failure.log` (so the
   annotation reporter can quote it),
3. in **batchmode** logs an error and calls `EditorApplication.Exit(1)` — nothing downstream can
   ignore that,
4. in the **interactive editor** throws instead, so the Console shows the failure and a developer's
   editor is not killed.

**Evidence.** `test_a_failed_build_player_exits_non_zero_in_batchmode` (asserts the call itself, not
a comment that mentions it — the first draft of this test was satisfied by the doc text, and mutation
M6 exposed that). Mutation M6 now kills the suite.

---

## 9. Deviations from the source document (also recorded as D-038)

| Requested | Implemented | Why |
|---|---|---|
| `IdleBurnRate` multiplier at throttle < 0.01 | 15 s auto-stop (§5.1), base drain unchanged | keeps the specified formula and its QA example |
| Jobs/`Task.Run` for chunk streaming | no change; planner already step-budgeted | nothing runs on the frame path; the file cannot reference Unity |
| Polling removal *inside* `MapMarkerService` | domain revision counter + view dirty flag | the service never polled; the view did |
| `public int saveVersion` | existing `schemaVersion` kept | renaming rewrites every save; same role, same type |
| `ISaveMigrator` with `V1_To_V2`/`V2_To_V3` methods | `ISaveMigrator` contract + `SaveMigration` lambda steps + gap/atomicity enforcement | identical semantics, testable before a v2 exists |
| Pin JDK/NDK/SDK in the workflow | SDK **levels** pinned in code + workflow; JDK/NDK pinned via editor image and stated as such | a host `setup-java` step would not affect Unity's build; claiming otherwise would be false |
| `EditorApplication.Exit(1)` on failure | added, batchmode only | killing an interactive editor on a build error is hostile and unnecessary |

## 10. Verification performed in this batch

| Check | Command | Result |
|---|---|---|
| Headless C# compile (7 assemblies, `-warn:4`) | `python3 Tools/Build/headless_cs.py all` | **0 errors, 0 warnings** |
| .NET Standard 2.1 API profile | same | **OK**, 5 assemblies |
| EditMode NUnit suite | same | **205/205 pass** (188 before this batch; 17 new tests) |
| Unity-dependent syntax (Roslyn parser) | same | 30 files, 0 syntax errors — **not type-checked, not run** |
| Build diagnostics | `python3 Tools/Build/test_build_diagnostics.py` | **93/93 pass** (91 before; 2 new) |
| Unity pipeline (mocked) | `python3 Tools/Build/test_unity_pipeline.py` | **23/23 pass** |
| Content / formulas / economy | `Tools/Validation/validate_content.py`, `formula_checks.py`, `Tools/Balance/economy_sim.py` | all pass, 0 failures |
| Mutation check | `python3 Docs/evidence/ai-processing-2026-10-20/mutation_check.py` | **8/8 mutations killed** |

Everything above is in `Docs/evidence/ai-processing-2026-10-20/`.

**Not verified, and not claimed:** Unity compilation of the five changed Unity-only files
(`VehicleController`, `MapTextureBinding`, `ProjectSetup`, `BuildCommands`, `CampaignSession` glue is
headless but its `Unity` consumers are not), PlayMode physics, on-device behaviour of the cross-fade
or auto-stop, any APK or AAB, and whether the pinned API 36 is installed in the CI image. KI-006,
KI-009, KI-034, KI-044 remain open.

## 11. Next dependency-ordered tasks

1. **Owner dispatch of `android-cloud-build.yml`** — the first run that can now (a) pass the SDK pins
   and (b) fail with a non-zero exit and a readable breadcrumb. Either an APK appears or
   `file=…,line=…,col=…` annotations do.
2. Commit `Packages/packages-lock.json` from that run's `Unity-logs` artefact (KI-002).
3. First Unity Test Runner pass for the Unity-only files changed here (KI-034), then a PlayMode
   handling check of the surface fade and the auto-stop cue (KI-009).
4. A device profile of the map presenter: confirm the revision rebuild does not regress the 4 Hz
   player-arrow cost (KI-047, M6).
