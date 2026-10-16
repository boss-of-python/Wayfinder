# Wayfinder — Implementation Status

Last updated: 2026-10-16. Status values: **Not started / In progress / Implemented / Verified / Blocked**.
"Implemented" for C# means *real source exists but is uncompiled/unverified in this environment* (D-003, KI-001).
"Verified" requires executed evidence — see `Docs/EVIDENCE_M0.md`, `Docs/EVIDENCE_CS_HEADLESS.md` and `Docs/REQUIREMENT_MAP.md`.
**Verified (headless)** = compiled by a real C# 9 compiler at warning level 4 with 0 warnings, passed the .NET Standard 2.1
API-profile check, and executed under NUnit outside Unity (D-014). It is not a Unity build result (KI-016).

## Latest Android APK workflow attempt — 2026-10-16

The latest cloud run [38858427636](https://github.com/boss-of-python/Wayfinder/actions/runs/38858427636)
failed in preflight because the check incorrectly required `UNITY_LICENSE`. The earlier successful
Unity log shows Personal-license activation through `UNITY_EMAIL`/`UNITY_PASSWORD` with
`UNITY_LICENSE` empty. Unity, package resolution, scene generation and APK building were skipped;
no artifact exists from this attempt. The workflow now accepts either the license-file secret or
both account secrets, verifies the editor pin, and does so before disk cleanup. Its Unity-log
reporter is skipped unless a Unity step was attempted.

For the established account-based path, no `.ulf` or `UNITY_LICENSE` secret needs to be created;
keep the Unity email/password secrets configured, then dispatch the updated branch. The Input
System 1.20.1 repin remains unverified until that run reaches package resolution/compilation
(KI-036). Latest local checks are **164/164** headless C# tests, **62/62** mocked Python
build/pipeline tests, content validation, formula checks and economy simulation — none substitute
for the Unity/APK/device gates.

## Batch 2026-10-16-G — Scatter placement + chunk streaming planner (M3, headless-verified; Unity host not yet written)

Milestone **M3 World pipeline**. Requirements: GEN-03 (§7.6 scatter), ARCH-03 / PERF-01 (§18.3 streaming planner),
QA-01 case 12 (planner level). Closes the engine-free part of **KI-017**. Decision **D-034**. Evidence:
`Docs/evidence/world-streaming-2026-10-16/`.

| Item | Before | After | Evidence |
|---|---|---|---|
| `World/ScatterPlacer.cs` — per-candidate seeding (§7.3), chunk-independent candidate grid whose cells tile each 256 m chunk, priority-based Poisson spacing (3–8 m) that holds across chunk borders, road shoulder 1 m, slope, reserved pads, density | no scatter placer (KI-017) | **Verified (headless)** — 10 tests | `headless-console.log`, `mutation-check.log` (9 scatter mutants caught) |
| `World/ChunkStreamPlanner.cs` — request ring 3 × 3, prefetch 200 m ahead, distance hysteresis (256 / 384 / 640 m), dormant data retention, pinning, activation budget, retry → streaming defect, handle ledger, loading hold | only the state machine (`ChunkStateMachine`) | **Verified (headless)** — 11 tests, including 300 crossings with host/planner handle agreement on every tick | idem |
| `ChunkStateMachine`: `Deactivating → DataReady` (dormant retention) | no retention path | Added as a documented extension (D-034); no existing test depended on it | idem |
| Simulated Unity host (`StreamHostSim`) used by the tests; counts asset handles independently of the planner | — | Test harness; the real host is not written | `WorldStreamingTests.cs` |

- **164/164** headless EditMode tests (143 → 164), 0 compile warnings, .NET Standard 2.1 profile OK, 29 Unity files parse.
  Content validator, formula checks and economy simulation still pass (`python-checks.log`).
- **Mutation check: 15/15 caught**, baseline green before and after. Note: the first run missed one mutant (a handle
  accounting break visible only between a step and its completions); a mid-tick invariant closed it.
- **Defects found and fixed in this batch:** integer division in the scatter cell size (grid did not tile the chunk);
  chunk-index hysteresis thrashed (≈3 deactivations per boundary crossing); diagonal ring corners dropped near edges;
  chunks leaving the ring were unloaded and reloaded on re-entry. Each is pinned by a regression test.
- **Executable now (headless):** given a player position and velocity, the planner produces request, activate,
  deactivate, cancel and unload commands; the scatter placer produces the same tree/rock instances for a chunk on every
  run, with spacing respected across borders.
- **Not done (open):** the `WorldStreamer` MonoBehaviour host and Addressables handles (KI-037); scatter over the baked
  real terrain, road carve and POI surfaces (KI-038); cache/landmark socket placement (§7.2 step 9); the full-mainland
  bake and greybox traversal. **M3 gate not passed.**

## Batch 2026-10-15-F — First real Unity run: the cloud build's actual failure, found and fixed

Requirements touched: **BUILD-01**, **ENG-01**, KI-001/002/003/006/033, new KI-034/035/036.
Evidence: `Docs/evidence/cloud-build-2026-10-15/` (owner-supplied log of run 38754979851 +
33 diagnostics tests + 9/9 mutation check + headless re-run).

| Item | Before | After | Evidence |
|---|---|---|---|
| Why five cloud builds failed | `Build failed with exit code 1`, undiagnosable without the raw log | **One cause, in Unity's own package:** `com.unity.inputsystem@1.12.0` `InputSystemPluginControl.cs(47,25): error CS0117: 'BuildTarget' does not contain a definition for 'ReservedCFE'` — an enum member Unity 6000.3 removed | `unity-setup-failure-38754979851.log` lines 181/186 |
| `Packages/manifest.json` | Input System 1.12.0 (blueprint pin, cannot compile on 6000.3) | **1.20.1** (D-031; the reference is gone upstream from 1.13.1) — reasoned, **not yet executed** (KI-036) | upstream package source at tags 1.12.0…1.20.1 |
| `ProjectSettings/ProjectVersion.txt` | template placeholder `6000.3.UNVERIFIED-PIN-REQUIRED`, rewritten per CI run | **Committed verified pin** `6000.3.26f1 (de8167e7440c)`, read from the real editor banner (D-032) | same log |
| `Tools/Build/pin_toolchain.py` | always rewrote the file, dropping any revision hash | Idempotent: leaves a matching committed pin alone, exits 1 on a deliberate mismatch, accepts `--editor-revision` | `test_build_diagnostics.py`, manual 3-case check |
| Unity-facing C# verification | 29 files parse-checked only | Unity **compiled all six engine-free assemblies**; `VehicleController`/`ChaseCameraBehaviour` are now Unity-compiled, not parse-checked. The four Input System-dependent assemblies are still uncompiled (KI-034) | `Csc Library/Bee/artifacts/…/Wayfinder.*.dll` |
| `ChaseCameraBehaviour` | the run's only warning: `_photoMode` write-only (CS0414) | **Fixed:** photo mode bounds the orbit per frame via `EffectiveDistance` instead of permanently mutating the authored `distance`, and exposes `PhotoMode` | 143/143 headless, 29/29 syntax |
| `ProjectSetup` | created the generated `Resources` folders then imported into them | `AssetDatabase.Refresh()` after each created folder, so `ImportAsset` has a folder to attach to and the campaign content really lands in the APK | source diff |
| CI diagnosability | none: Unity's exit code is undescriptive, the log host is unreachable from here | `report_unity_failure.sh` → check-run annotations (compile errors with file/line/col, abort reason, exceptions, package-resolution failures, warnings in `Assets/Wayfinder`, compiled assemblies, APK size) + job summary + `Unity-logs` artifact **including `packages-lock.json`**; `check_unity_secrets.sh` → names a missing secret in seconds, never printing a value (D-033) | `diagnostics-tests.log` (33 tests), `mutation-check.log` (9/9) |

- **Verified here:** 33/33 diagnostics tests, 9/9 mutation check, 23/23 runner contract tests,
  `headless_cs.py all` → compile OK / **143/143** EditMode tests / .NET Standard 2.1 profile OK /
  29 Unity files parse, `pin_toolchain.py` idempotent on the committed pin.
- **Verified by a real Unity run (owner-supplied log):** license activation, image existence,
  package resolution, editor version/changeset, six assemblies compiling, one warning.
- **Not verified:** the Input System repin (needs one dispatch), scene generation, the
  PlayMode/EditMode suites in Unity, an APK, a device install. **No APK exists** — KI-006 and
  the M0/M1/M2 gates are unchanged.
- **Blocked:** the agent cannot dispatch the workflow (`403 Resource not accessible by
  integration`, no `actions: write`) — KI-035. The owner runs **Actions → Android APK (free
  cloud build) → Run workflow** and may select branch `arena/700fea1a-wayfinder`.

## Batch 2026-10-15-E — Free cloud build route (Implemented; never executed)

Requirements touched: **BUILD-01** (narrowed, not closed), KI-006, new KI-033. No game
code, no content and no C# changed.

| Item | Before | After | Evidence |
|---|---|---|---|
| `.github/workflows/android-cloud-build.yml` — GameCI development APK on a GitHub-hosted `ubuntu-latest` runner (manual `workflow_dispatch` only) | no cloud route; only a self-hosted Windows runner workflow | **Implemented — statically validated, never run** (KI-033) | PR #5; YAML parsed, all five action SHAs confirmed upstream, every `with:` key confirmed as a declared input, both `buildMethod` targets and the artifact path confirmed in the C# source |
| Owner prerequisites | — | repository secrets `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` must be created by the owner before the first run | documented in `Docs/BUILD_AND_RUN.md` |

- **Verified here:** workflow structure, SHA pinning, action input names, expression
  contexts, `pin_toolchain.py --editor-version 6000.3.26f1` (runs clean),
  `ProjectSetup.Setup` / `BuildCommands.BuildAndroidDev` exist as public static methods,
  artifact path == `BuildCommands.AndroidDevPath`.
- **Not verified:** no Unity, no Docker image pull, no license secrets in this environment
  — **no build log, no APK, no device result may be claimed** (KI-033, KI-006 open).
- **M0 gate:** still **Blocked** (KI-001/002/005/006). This batch only gives the owner a
  cheaper route to a development APK.

## Batch 2026-10-15-D — Exploration map + field supplies/selling + research → physics (domain Verified headless; Unity UI syntax-checked)

Milestone **M2 Forest vertical slice**. Requirements: MAP-01 (§8.1), new **MAP-04** (§13.2 minimap, §13.4
map screen, §7.6 no cache spoilers), UI-01, SAVE-01 (additive marker fields), §16.2 (2 Hz map reveal).

| Layer | Before | After | Evidence |
|---|---|---|---|
| `Campaign/Map/` (5 new files, engine-free): `ExplorationMap` (blank sheet → live fog-of-war raster, incremental redraw), `MapRaster`/palette/rasterizer, `MapMarkerRules` (spoiler policy, D-026), `MapViewport` + `MapGestureRecognizer`, `MapScreenModel`, `SliceMapTerrain` | — | **Verified (headless)** — 21 new tests | `Docs/evidence/exploration-map-2026-10-15/` |
| `World/TerrainLineOfSight` + `DiscoveryService.RevealVisibleAround` (§8.1 ridge occlusion for drive + vista; surveyed flags now obey it, D-028) | no line of sight in the campaign path | **Verified (headless)** | idem |
| `Progression/MapMarkerService` + `CampaignSession.Map` (≤ 10 custom markers, stable ids, route target) + `WorldStateDto.customMarkers` (additive, D-027) | — | **Verified (headless)** — byte-identical save/load, rescue keeps markers + cells | idem |
| `SliceFieldController` reveal cadence 1 Hz → **2 Hz** (§16.2) | 1 Hz | **Verified (headless)** | idem |
| `Presentation/MapTextureBinding`, `MapGlyphs`, `MinimapView`, `MapOverlayView`; `VerticalSliceDriver` (minimap on the HUD, M key, garage Map button, MapPaused freeze); `Tests/PlayMode/MapPlayModeTests` (2) | — | **Implemented — syntax-checked only** (+ scratch stub type-check vs real assemblies, not Unity) | `scratch-unity-stub-typecheck/` |

- **130/130** headless EditMode tests (109 → 130); 7 assemblies / 57 files, **0 warnings**; .NET Standard 2.1
  profile OK; 29 Unity-dependent files parse. **11/11** map mutation check (the first run exposed one
  coverage gap, now closed by a dedicated test).
- **Executable now (headless):** new game → the map is a blank sheet (only you, home garage, Q01 search
  circle) → driving reveals cells live at 2 Hz (soft fog edges, hillshade, ford/mud/pad) → vistas survey
  384 m with contours → markers follow spoiler rules → place/route/remove custom markers → save/load and
  rescue keep every revealed cell and marker. PNG snapshots of a scripted exploration: 0 % → 68 % mapped.
- **Open:** Unity compile/run of the map UI (KI-031), greybox-derived map terrain (KI-029), straight-line
  route estimates (KI-030). **M2 gate not passed.**

**Continuation in the same batch** (ITEM-01, ECON-01, UPG-01; D-029, D-030):

| Layer | Before | After | Evidence |
|---|---|---|---|
| `Campaign/CampaignSession.Supplies.cs`: fuel can (≥ 1 L rule, waste confirmation), repair kit (stopped, out of water, never wasted), tire patch (puncture band), selling (garage only, 40 % floor, critical save, mission camp kits kept), plus `TransactionService` `UseItem` and `SuppliesModel` / garage sell info | not exposed (KI-028) | **Verified (headless)** — 9 tests | `Docs/evidence/supplies-and-stats-2026-10-15/` |
| `CampaignSession.ConfigureResolver`; `VehicleController.StatsResolver` + resolved tank clamp; driver configures the rig | research reached physics only as tank/cargo (KI-025) | **Verified (headless)** session side — 4 tests; rig side **syntax-checked**; KI-025 partially resolved | idem |
| **KI-032 fixed:** Trail Tires I grip (`VehicleStatsResolver.GripFor`) and Engine Tune torque (`Powertrain.ResearchTorqueScale`) | no effect anywhere | **Verified (headless)** | idem |
| Supplies overlay (HUD **Supplies** button) and garage **Sell +N → Confirm?** buttons in `VerticalSliceDriver` | — | **Implemented — syntax-checked** (+ scratch stub type-check) | idem |

- **143/143** headless EditMode tests (130 → 143); 7 assemblies / 60 files, 0 warnings; 29 Unity files parse;
  **13/13** supplies/stats mutation check.

## Batch 2026-10-15-C — M2 campaign composition (domain Verified headless; Unity glue syntax-checked)

Milestone **M2 Forest vertical slice**; requirements FLOW-01/03, REC-01, MAP-02, QUEST-01/03,
ITEM-01/02, UPG-01, ECON-01, CAMP-01/02, UI-01, ARCH-01, SAVE-01, MOBILE-01, §21.3 (SLICE-21.3).

| Layer | Before | After | Evidence |
|---|---|---|---|
| `Wayfinder.Campaign` (new, 11 files, engine-free) — per-slot `CampaignSession` composition root, snapshot mapper, autosave policy, field trackers, `SliceFieldController`, HUD/garage/summary view models, content loader | — | **Verified (headless)** — 34 tests | `Docs/evidence/campaign-session-2026-10-15/` |
| Progression/Core/Persistence defect fixes KI-018 … KI-024 | defects | **Verified (headless)** — 16 regression tests; 8/8 mutation check | idem (`mutation-check.log`) |
| `Content/vertical_slice.json` + `validate_content.py` slice check | — | **Verified** — validator passes, 5/5 negative controls rejected | idem |
| `CampaignHost`, Title save slots, `VerticalSliceDriver`, world-coordinate VerticalSlice scene builder, Resources content copy, 2 PlayMode + 2 editor tests | prototype only | **Implemented — syntax-checked only** (+ scratch stub type-check, not Unity) | `headless-console.log`, `scratch-unity-stub-typecheck/` |

- **109/109** headless EditMode tests (59 → 109), 7 assemblies / 47 files, **0 warnings**, 6 runtime
  assemblies pass the .NET Standard 2.1 profile; 24 Unity-dependent files parse.
- **Executable now (headless):** new game → depart → collect → discover → return → bank → research →
  reload (byte-identical); banking interrupted after each of six persistence steps resolves to old or new
  complete state; full Q01–Q04 slice playthrough affords tiers 1–6 + a second camp with an exact ledger.
- **Open:** Unity compile/run of the glue (KI-026), partial stat → physics wiring (KI-025), compact
  placeholder placements/flat slice ground (KI-027), simplified storage/modules/hub travel (KI-028).
  **M2 gate not passed** — it requires a playable 30-minute slice and interruption tests on a device.

## Later 2026-10-15 continuation — mobile lifecycle and phone build route

- **59/59 headless C# tests pass**, including four new pause/focus ordering and
  duplicate-event regressions. **23/23** mocked Python runner tests pass. Latest
  engine-source syntax check covers 21 files with no syntax errors.
- **Implemented, not Unity-verified:** independent pause/focus adapter, authored
  water-trigger depth/stall wiring, two more PlayMode tests (seven total), mobile
  30 FPS default plus a 30/60 target toggle.
- Prepared an owner-only, manual, dedicated self-hosted Windows-runner APK workflow.
  YAML/security contracts were statically checked; no workflow/Unity build was run.
- A Moto G45 can control a desktop and install an APK, but not run desktop Unity.
  No paid machine, runner, editor license, APK or Play submission was provisioned.
- Guide: [MOTO_G45_BUILD_AND_TEST.md](MOTO_G45_BUILD_AND_TEST.md). Evidence:
  `Docs/evidence/mobile-continuation-2026-10-15/`.

## Batch 2026-10-15 — Unity preparation and driving prototype

- **Verified headlessly:** 55/55 C# domain tests pass (6 new steering/powertrain
  regressions), zero compile warnings/errors, profile checks pass. Twenty-one
  Unity-dependent sources parse successfully, **not type-checked by Unity**.
- **Verified Python tooling:** 23/23 mocked runner contract tests; data/formula/
  economy checks pass. No Unity test/build result is claimed.
- **Implemented, Unity-unverified:** generated URP/quality/input setup; serializable
  matching-filename MonoBehaviours; Bootstrap → Title → proving-ground UI; four-wheel
  rig, touch input, pause/reset and camera bridge; explicit signed-AAB release path
  and failing build gates; cross-platform setup/test/Android runner.
- **Still blocked:** actual Unity/package resolution/compile, generated scenes and
  `.meta` files, physical-device gates. Domain fixes do not close M0/M1/M2 gates.
  Campaign UI/save wiring, scatter/streaming, production assets and M6/M7 remain
  incomplete. Historical summaries below retain their original 14 October counts.
- Evidence, commands and detailed limitations: **[UNITY_HANDOFF.md](UNITY_HANDOFF.md)**
  and `Docs/evidence/unity-prep-2026-10-15/`.

## Batch 2026-10-14-B — headless C# compile + EditMode execution

| Layer | Before | After | Evidence |
|---|---|---|---|
| `Wayfinder.Core` (9 files) | Implemented | **Verified (headless)** — 7 tests | `Docs/evidence/headless-cs/` |
| `Wayfinder.Simulation` pure math (7 of 9 files) | Implemented | **Verified (headless)** — 14 tests | idem |
| `Wayfinder.World` (2 files) | Implemented | **Verified (headless)** compile + profile; behaviour mirrored in Python | idem |
| `Wayfinder.Progression` (6 files) | Implemented | **Verified (headless)** — 14 tests | idem |
| `Wayfinder.Persistence` (2 files) | Implemented | **Verified (headless)** — 8 tests (after fixing KI-013) | idem |
| `Wayfinder.Tests.EditMode` (49 tests) | written, never run | **49 / 49 pass**, order-independent | `EditMode.headless.xml` |
| `VehicleController.cs`, `ChaseCameraBehaviour.cs`, `Platform/**`, `Presentation/**`, `Editor/**`, `Tests/PlayMode/**` (12 files) | Implemented | Implemented — **parse-checked only** (C# 9 syntax, no type check) | console.log |

Defects found and fixed by this batch: 5 compile errors + KI-013 (save checksum always failed — would have been a release blocker),
KI-014 (water stall re-drain), KI-015 (inventory oversize stack), hidden static coupling in `ResearchService` (D-018).
Two incorrect test expectations were corrected against the blueprint (torque 212.5 N·m at 1950 rpm; fuel maximum is attained exactly).

## Summary

| Milestone | Scope | Status | Gate |
|---|---|---|---|
| **M0 Foundation** | Unity project layout, URP/package pins, asmdefs, Bootstrap/Title/PhysicsTestGround/VerticalSlice scene builders, app state flow, input actions, test assemblies, build scripts, docs | **Implemented — domain layer Verified (headless); gate Blocked on toolchain** | Android/iOS smoke builds require owner toolchain (KI-001/002/006). Core/World/Progression/Persistence compile + 49/49 EditMode tests pass headlessly; Unity-only files parse-checked. |
| **M1 Vehicle proving ground** | Scout S1 spec, WheelCollider controller, powertrain, steering/assists, camera, surfaces, suspension, fuel/damage/water/mud, winch, reset, PhysicsTestGround rig | **Math Verified (headless); WheelCollider/camera Implemented (unverified)** | Powertrain/fuel/damage/water/mud/recovery math executed (14 tests). Needs Unity physics demonstration on device (KI-009). |
| **M2 Forest vertical slice** | Inventory/wallet/transactions, garage/camp, missions Q01–Q04, mapping/discovery, research tiers 1–6, save/atomic banking, recovery, campaign session + slice manifest, touch UI | **Campaign composition Verified (headless) — 109/109; Unity glue Implemented (syntax-checked only)** | Domain loop, interruption-at-every-step, rescue, camp atomicity, autosave and full Q01–Q04 slice ledger executed headlessly (Batch C). Gate still needs the playable 30-minute slice + interruption tests **on device** (KI-001/026/027). |
| **M3 World pipeline** | Deterministic hash/PRNG, noise heightfield, 24×24 chunk bake, seam checks, route graph/validator, chunk state machine, scatter placer, streaming planner | **In progress; hash/PRNG/bitset, seam/hash, scatter placer and streaming planner Verified (headless) — 164/164** (Batch G). The `WorldStreamer` MonoBehaviour host, real-terrain scatter and the full-mainland bake are **not yet implemented** (KI-037/038) | Needs the Unity streamer host, socket placement, in-editor bake + full-mainland traversal and route validation on real terrain. |
| **M4 Full campaign systems** | All 20 missions, 30 contracts + 5 templates, 20 research tiers, 4 vehicles, item/recipe catalog, camps/garages, relic ledger, finale Q20 | **Implemented; services Verified (headless) + content Verified** | Needs greybox full-campaign playthrough. |
| **M5 Content & assets** | Final art/audio, five visual regions, vehicle art, UI polish, icons, lore, sound | **Not started** (placeholders tracked in ASSET_MANIFEST.csv) | Hero assets must not be placeholders at this gate. |
| **M6 Optimization & usability** | Device frame-time/memory/thermal, accessibility verification, economy pacing playtests | **Not started** (quality tiers defined; simulator model runs) | Requires measured device profiles. |
| **M7 Release candidate** | Clean builds, save migrations from public versions, airplane-mode suite, store audit | **Not started** (build scripts + checklist ready) | Owner credentials + authorization required for any signing/submission. |

## Requirement IDs addressed in this batch

PROD-01 (Verified: dependency scan), FLOW-01/02/03 (Implemented), DRIVE-01..04 (Implemented),
VEH-01..04 (Implemented; VEH-02 data Verified), HAZ-01/02 (Implemented; formulas Verified),
REC-01 (Implemented; rules Verified), WORLD-01/02 (Implemented constants/graph),
GEN-01/02/04 (Implemented; hash/seam Verified), GEN-03 (In progress — scatter placer missing, KI-017), MAP-01..03 (Implemented; bitset/thresholds tested),
QUEST-01..03 (Implemented; data Verified), ITEM-01/02 (Implemented; data/arbitrage Verified),
UPG-01 (Implemented; costs/no-double-bonus Verified), ECON-01 (Verified: sim + scans),
CAMP-01/02 (Implemented; atomicity/idempotency tests written), UI-01 (Implemented models),
ENG-01 (Implemented; exact pin Blocked KI-002), ARCH-01..03 (Implemented),
SAVE-01 (Implemented; protocol tests written + round-trip mirrored), PERF-01 (config Implemented;
device Blocked), MOBILE-01 (Implemented; device Blocked), PRIV-01 (Verified: scan),
QA-01 (In progress: 16/20 mandatory cases have executed evidence — 15 via headless C# and/or Python; cases 12, 14, 15, 20 need Unity runtime/device),
BUILD-01 (Blocked KI-006), LEGAL-01 (In progress: manifest clean, placeholders tracked),
FINISH-01 (Implemented definitions; credits flow coded).

## Evidence index

| Evidence | Command | Result |
|---|---|---|
| Content counts/IDs/gates/recipes/PROD-01 | `python3 Tools/Validation/validate_content.py` | PASSED (0 errors) |
| Formulas & invariants (35 checks) | `python3 Tools/Validation/formula_checks.py` | PASSED |
| Economy pacing/deadlock/arbitrage | `python3 Tools/Balance/economy_sim.py` | PASSED (no deadlocks) |
| C# compile (Roslyn 3.9, C# 9, `-warn:4`) of Core/Simulation(pure)/World/Progression/Persistence/Campaign/Tests.EditMode | `python3 Tools/Build/headless_cs.py compile` | OK — 47 files, 0 errors, 0 warnings; 4 Unity files excluded (Batch C) |
| C# EditMode NUnit suites (headless, NUnitLite 3.13.3) | `python3 Tools/Build/headless_cs.py test` | **109 / 109 passed** (`Docs/evidence/campaign-session-2026-10-15/EditMode.headless.xml`; earlier 49/55/59 snapshots in older folders) |
| .NET Standard 2.1 API-profile check | `python3 Tools/Build/headless_cs.py profile` | OK — 6 runtime assemblies |
| C# 9 parse check of 24 Unity-dependent files | `python3 Tools/Build/headless_cs.py syntax` | OK — 0 syntax errors (not type-checked) |
| Mutation check of Batch C fixes/safeguards | `python3 Docs/evidence/campaign-session-2026-10-15/mutation_check.py` | 8 / 8 mutations caught |
| C# NUnit suites **in Unity** | Unity Test Runner / `Tools/Build/build.sh test-editmode` | **Not run** (KI-001) |
| Android/iOS smoke builds | `Tools/Build/build.sh android-dev` / `ios-dev` | **Not run** (KI-006) |

## QA §20.2 mandatory case coverage

| # | Case | Coverage |
|---|---|---|
| 1 | Landmark reward granted once | C# test **passed (headless)** + ledger mirror |
| 2 | Already-collected cache yields nothing | C# transaction replay + campaign reload/preview tests **passed (headless)** (authored contents, KI-020) |
| 3 | Insufficient cargo blocks pickup without consuming | C# inventory + campaign full-cargo + field-prompt tests **passed (headless)** (KI-015/019/020 fixed) |
| 4 | Banking interruption → old or new state, never mixed | C# save protocol test + **campaign banking interrupted after each of 6 persistence steps** + failed-write rollback **passed (headless)** |
| 5 | Backup after corruption preserves latest valid | C# backup rotation test **passed (headless)** |
| 6 | Zero-resource rescue/playability | C# rules + campaign rescue/zero-supply recovery **passed (headless)** + Python deadlock scan |
| 7 | Tier costs & prerequisites canonical | C# **passed (headless)** + Python (both PASSED) |
| 8 | Tire sets never double-dip research bonus | C# resolver test **passed (headless)** |
| 9 | Capacity-module removal blocked over capacity | C# test **passed (headless)** |
| 10 | Camp establishment atomic | C# transaction + campaign test (atomic with its save) **passed (headless)** (KI-021 fixed) |
| 11 | Fuel/condition bounds; frame-rate-independent integration | C# tests **passed (headless)** + Python bounds |
| 12 | Chunk boundary handle leaks | Streaming state machine coded; PlayMode test pending (Unity) |
| 13 | 100 landmark / 200 cache unique & reachable | IDs unique PASSED; reachability at M3 bake |
| 14 | Wetland routes valid in both water states | Water model states coded; route validation at M3 |
| 15 | Background/resume never latches throttle | C# + PlayMode test written |
| 16 | No reward farming by closing on reward screen | Transaction idempotency tests **passed (headless)** |
| 17 | Crafted-item resale never nets currency | C# **passed (headless)** + Python (both PASSED) |
| 18 | Mapping threshold never duplicated | C# restore test **passed (headless)** |
| 19 | Migration preserves discoveries/modules | C# test **passed (headless)** |
| 20 | 150% scale / 30% text expansion layout | Not covered — needs UI layout pass (M2/M6 device check) |

## Next dependency-ordered batch

0. **After Batch G (2026-10-16):** the engine-free M3 work is done. Remaining M3 work is Unity-dependent (streamer host,
   scatter on baked terrain, bake) and needs one owner cloud run first (item 1 below). Cache/landmark socket placement
   (§7.2 step 9) is the next headless-only M3 item.

1. **Owner, ~10 minutes**: dispatch **Actions → Android APK (free cloud build) → Run workflow** on branch `arena/700fea1a-wayfinder` (KI-035 — the agent's token lacks `actions: write`). That single run compiles the four remaining Unity-facing assemblies (KI-034), runs `ProjectSetup.Setup` and, if it gets there, produces `Wayfinder-dev.apk` as an artifact. Any failure now self-reports as annotations (D-033), so no log paste is needed. Then commit `Packages/packages-lock.json` from the `Unity-logs` artifact (KI-002).
   *Local alternative:* install Unity 6.3 LTS + Android module, open the project, `Tools → Wayfinder → Setup Project`, commit generated scenes/meta (KI-004/005).
2. Fix any UnityEngine API drift in the 24 parse-checked files — first the new campaign glue (KI-026); run EditMode tests **in Unity** (expect 109/109) and the 9 PlayMode tests; play Title → New → VerticalSlice and attach logs/screens to `Docs/evidence/`.
3. M2 follow-ups that are dependency-ready without Unity: `VehicleController.ApplyEffectiveStats` seam so researched grip/suspension/torque reach physics (KI-025); camp↔garage hub travel + per-camp storage with a schema bump/migration (KI-028); expose repair-kit/tire-patch use and selling in the session.
4. M3 batch: `ScatterPlacer` (§7.6) + pure `WorldStreamer` planner with handle-leak tests (KI-017, QA case 12); slice terrain bake so the 1,024 m area has real relief (KI-027).
5. Device: Moto G45 slice playtest (30 minutes, interruption/kill tests, 30 FPS budget) to close the M2 gate.
6. Keep `python3 Tools/Build/headless_cs.py all` green on every batch.
