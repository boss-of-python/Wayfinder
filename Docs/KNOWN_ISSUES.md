# Wayfinder — Known Issues

Severity: **Blocker** (milestone gate cannot pass) / **Major** (feature wrong or missing) / **Minor** (cosmetic or deferred) / **Info**.
Statuses: Open / Mitigated / Resolved.

| ID | Severity | Summary | Detail / repro | Affected | Status |
|---|---|---|---|---|---|
| KI-001 | Blocker | No Unity editor in the implementation environment | `which unity` empty; Unity's own build pipeline, PlayMode tests and device builds cannot run **here**. **Narrowed 2026-10-15 (Batch F):** a real Unity 6000.3.26f1 (`de8167e7440c`) now runs this project on a GitHub-hosted runner through `.github/workflows/android-cloud-build.yml` — the owner's Personal-license secrets activate, every pinned package resolves and Unity compiles the six engine-free Wayfinder assemblies (`Docs/evidence/cloud-build-2026-10-15/`). So Unity is reachable; it is just not reachable *from this environment*, and every run needs an owner dispatch (KI-035) | Unity-dependent C# (Platform/Presentation/Editor/PlayMode), Android/iOS builds | Open — but no longer the reason the project cannot be compiled by Unity. Remaining Unity-only gap is KI-034 (assemblies behind the failed Input System compile) and KI-006 (no APK yet). |
| KI-002 | Blocker | Exact `6000.3.x` patch and package versions not verifiable from this environment | **Resolved for the editor 2026-10-15 (Batch F, D-032):** run 38754979851 proves the project opens, resolves and compiles under **6000.3.26f1 (`de8167e7440c`)**, and `ProjectSettings/ProjectVersion.txt` now commits exactly that, including the changeset hash Unity itself printed — nothing fabricated. **Resolved for the manifest:** every pin in `Packages/manifest.json` resolved on the real registry except that `com.unity.inputsystem` 1.12.0 does not *compile* on 6000.3.26f1 (`BuildTarget.ReservedCFE` was removed); repinned to 1.20.1 (D-031). **Still open:** `Packages/packages-lock.json` is not committed. The workflow now uploads it in the `Unity-logs` artifact so the owner can commit the exact resolved set | `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt` | Mitigated — editor pin committed and verified; commit `packages-lock.json` from the next successful run's artifact to close it. |
| KI-003 | Major | Unity-dependent C# is **implemented but uncompiled/unverified**; pure-domain C# is now headless-verified | 2026-10-14: `Wayfinder.Core/Simulation(pure)/World/Progression/Persistence` + `Tests.EditMode` compile with Roslyn 3.9 (C# 9) at warning level 4 with 0 warnings, pass the .NET Standard 2.1 API-profile check and **49/49 EditMode tests pass** (`Docs/EVIDENCE_CS_HEADLESS.md`). The harness surfaced and fixed 5 compile errors (float cast, cross-asmdef `SurfaceType`, `_atomicReplace` delegate type, private `ResearchService` ctor, UnityEngine use in ContentCatalogTests) and 3 runtime defects (KI-013/014/015). The 12 Unity-dependent files are **parse-checked only** | `Scripts/Platform/**`, `Scripts/Presentation/**`, `Scripts/Editor/**`, `Simulation/VehicleController.cs`, `Simulation/ChaseCameraBehaviour.cs`, `Tests/PlayMode/**` | **Partly resolved 2026-10-15 (Batch F):** Unity 6000.3.26f1 compiled `Wayfinder.Simulation` (so `VehicleController` and `ChaseCameraBehaviour` are now Unity-compiled, not parse-checked) with one warning, fixed. Open for Unity-only files — first editor open + `Window → General → Test Runner` will surface any UnityEngine API drift; record the fix-list here. |
| KI-004 | Major | Scenes/prefabs/URP asset are not committed as serialized Unity assets | They are produced deterministically by `Tools → Wayfinder → Setup Project` (`SceneBuilder`, `ProjectSetup`) on first editor open | Bootstrap/Title/PhysicsTestGround/VerticalSlice scenes, URP pipeline asset, quality settings | Mitigated — editor setup script is committed and documented; generated assets must be committed by the owner afterwards. |
| KI-005 | Major | `.meta` GUID files not committed | Hand-authoring GUIDs risks broken references; Unity generates them on first open | Entire `Assets/` tree | Open — after first open, commit the generated `.meta` files immediately so GUIDs stay stable across machines (step in BUILD_AND_RUN.md). |
| KI-006 | Major | Device smoke builds (M0 gate) not produced | Requires Android SDK/NDK or macOS+Xcode and signing identity owned by the owner. A free GitHub-hosted cloud route now exists (`.github/workflows/android-cloud-build.yml`, see KI-033) but it also needs owner-provided Unity license secrets and has never been run | M0 exit gate | Open — exact batch commands in `Tools/Build/` + `Docs/BUILD_AND_RUN.md`. |
| KI-007 | Minor | `Wayfinder_Content_Seed.json` referenced by the handoff is missing from the repository | Only the two spec `.txt` files and README were in the initial commit | Content authoring | Info — content authored from the blueprint (authoritative). Decision D-011. |
| KI-008 | Minor | Landmark/cache/contract world **positions** are layout anchors, not final validated coordinates | Blueprint §6.1 allows anchors to move during route validation; M3 `RouteValidator` + `WorldBaker` place/validate final sockets | Content JSON `positionHint` fields | Open — expected at M3/M5; IDs must not change when positions do. |
| KI-009 | Minor | PlayMode tests and physics handling gates (M1) cannot be demonstrated as driving behaviour here | Requires running Unity physics on device/desktop | M1 gate | Open — PhysicsTestGround scene builder + handling test scripts are implemented; evidence pending toolchain. |
| KI-010 | Info | Economy simulation is a model, not playtest proof | §11.5 says simulation cannot certify fun or playtime | Balance claims | Open — human playtests still required (M2/M6). |
| KI-011 | Info | Art/audio assets are tracked placeholders | `Docs/ASSET_MANIFEST.csv` lists provenance/placeholders | M5 content completion | Open — M5 is explicitly "replace tracked placeholders"; do not count placeholder art as final. |
| KI-012 | Minor | Localization is English-source only with keys everywhere; string tables not yet imported | §1.3 says localization-ready text and layouts | UI text | Mitigated — all UI strings use localization keys in content; actual string tables are M5/M6 work. |
| KI-013 | Blocker (found + fixed) | `SaveService.Save()` always returned `FailedVerify` | Newtonsoft's default `ObjectCreationHandling.Auto` **appends** JSON arrays to DTO list fields that are initialised in field initialisers (`ownedVehicleIds`, inventory, ledgers), so the parse-verify step saw doubled lists → checksum mismatch → no save ever succeeded. Found by executing `PersistenceTests` headlessly (7 of 8 failed) | Persistence, every save/load, M2 banking | **Resolved** 2026-10-14 — `SaveJson` settings class (`ObjectCreationHandling.Replace`, `DateParseHandling.None`, invariant culture) used for all serialize/deserialize/verify paths (D-017); `PersistenceTests` 8/8 pass. |
| KI-014 | Major (found + fixed) | Water stall drained −10 condition **every 2 s** while submerged | §5.3 says −10 once per incident. `VehicleController.ApplyWaterStall` called `WaterCrossingModel.ClearStall()` immediately after applying the penalty, so the next physics tick re-triggered it. Found by `DamageModel_WaterStallsOnce` | Simulation water rules, condition economy | **Resolved** 2026-10-14 — `DamageModel` owns the incident latch (`ApplyWaterStall()` returns true once; `EndWaterIncident()`; `RepairFully()` clears). **M2 wiring note:** the recovery flow must call `VehicleController.RestartAfterWaterRecovery()` — it is the only method that ends the incident and restarts the engine. |
| KI-015 | Major (found + fixed) | `InventoryService.Add` could leave one stack above `MaxStack`, under-counting slots | Capacity math then disagreed with `CanAdd`, so a "full" inventory could still accept items. Found by `Research_CapacityModuleRemovalBlockedWhenOver_QA9` (expected 2 slots for 20 scrap @ MaxStack 10) | Inventory, QA case 3/9 | **Resolved** 2026-10-14 — all-or-nothing `PlacementPlan` (fill partial stacks, then open new slots, split at `MaxStack`); `ProgressionTests` 14/14 pass. |
| KI-016 | Minor | Headless harness ≠ Unity build | `headless_cs.py` uses Roslyn 3.9 + NUnit 3.13.3 + Newtonsoft 13.0.3 built from source and a .NET Standard 2.1 reference assembly; Unity 6000.3.x ships its own Roslyn, UTF embeds NUnit 3.5 and `com.unity.nuget.newtonsoft-json` 3.2.1. The common API surface is used deliberately, but Unity's own compile/test run remains the gate evidence | Evidence interpretation | Info — see `Docs/EVIDENCE_CS_HEADLESS.md` §5; Unity results, when produced, supersede headless ones. |
| KI-017 | Major | `REQUIREMENT_MAP` previously listed `World/RoadGraph.cs`, `World/ScatterPlacer.cs` and `World/WorldStreamer.cs` as implemented — **they do not exist** | Found while cross-checking the map against the headless compile report (which lists every compiled file). What exists: `RouteGraph`/`RouteValidator`/`CapabilityBands` (route constraints), river/road carve *hooks* in `NoiseField`, and `ChunkStateMachine` (state transitions only, no MonoBehaviour streamer) | GEN-03, ARCH-03, PERF-01 (M3) | Open — map corrected 2026-10-14 (GEN-03 → IP, ARCH-03 streamer → missing). Implement `ScatterPlacer` (§7.6 rules) and a `WorldStreamer` host around `ChunkStateMachine` in the M3 batch; QA case 12 depends on it. |
| KI-018 | Major (found + fixed) | `MissionService.Restore` regressed missions after every load | Restore reset every state to Locked and `RefreshAvailability` then re-opened completed missions (e.g. Q01) as **Available** — they could be accepted again (§2.5 forbids regression). A replayed turn-in (reward key already claimed) completed the mission but left the successor Locked; unknown codes/short counter arrays were not guarded. Found while composing save/load in `CampaignSession` | Missions, every load, QUEST-01 | **Resolved** 2026-10-15 — completed codes restore as Completed, unknown codes ignored, counters normalised, `ReadyToTurnIn` re-derived from counters, replay path refreshes availability. Pinned by `ProgressionRegressionTests` + campaign reload tests; mutation check: 4 tests fail if reverted. |
| KI-019 | Major (found + fixed) | `InventoryService.Remove` mutated before failing | Removing more than available removed what it found, then returned "insufficient quantity"; `Transfer` therefore destroyed items on failure (its rollback re-add could also re-split stacks) | Inventory, transfers, banking, ITEM-01 | **Resolved** 2026-10-15 — quantity checked first; `Transfer`/`AddAll`/multi-step commands roll back exactly via `Checkpoint()`/`Rollback()`. |
| KI-020 | Major (found + fixed) | Cache collection ignored authored contents | `TransactionService.ExecuteCollect` always added exactly **1** `salvage_bundle` (caches.json: 60–300 Salvage + materials) and relied on the caller to mark the cache collected | Collection, economy pacing, QA 2/3 | **Resolved** 2026-10-15 — `IPickupCatalog` (`CampaignContent` loads caches.json); all stacks placed atomically or nothing; collected flag set in the same commit; unknown pickups are typed `ContentMissing`. |
| KI-021 | Major (found + fixed) | Camp establishment not atomic; camp deposit moved only scrap metal | `EstablishCamp` removed the kit and emitted an event but never activated the camp (no subscriber existed) — the QA-10 failure mode. `CampService.Deposit` transferred only `scrap_metal` and ignored the result | CAMP-01, QA 10 | **Resolved** 2026-10-15 — activation inside the same commit (unknown/already-active sockets keep the kit); shared `CargoSettlement` banks Salvage + escrow and stores every material that fits (500 kg). |
| KI-022 | Major (found + fixed) | Crafting never charged the recipe Salvage fee | `CraftingService.TryCraft` had no wallet parameter: camp kit cost 0 instead of 80 (§10.4), repair kit 0 instead of 20. The QA-17 arbitrage test inspects definitions, so it could not see this | Economy, ITEM-02 | **Resolved** 2026-10-15 — fee validated before consuming and charged in the same rollback unit. |
| KI-023 | Major (found + fixed) | Hidden static singletons prevented per-slot state | `MissionService` had a private constructor + `Global`; `TransactionService` research used `ResearchService.Global`. Three save slots (§1.4) could not hold isolated progress | ARCH-01/02, FLOW-03 | **Resolved** 2026-10-15 — public constructor; `TransactionDependencies` injection; `CampaignSession` owns one instance per slot (D-019). `Global` remains only for legacy callers. |
| KI-024 | Minor (found + fixed) | Day cycle was 20 minutes | `SimulationTime.DayLengthSeconds = 1200`; §7.7 targets 48 real minutes | Time of day, weather visuals | **Resolved** 2026-10-15 — 2880 s; `Restore()` wraps and sanitises NaN. |
| KI-025 | Major | Researched stat effects reach Unity physics only partially | **Partially resolved 2026-10-15 (D-030):** the rig's own resolver is now configured with research (`CampaignSession.ConfigureResolver` → `VehicleController.StatsResolver`), so torque (Engine Tune), fuel drain, tank capacity, wading depth and the grip baseline include research, and the runtime spec widening is gone. **Still open:** WheelCollider friction uses the packed-dirt baseline for all four wheels (per-wheel surface grip only scales braking), so Trail Tires I applies on every surface in physics (the garage display is exact); suspension travel/clearance research is not applied to WheelCollider geometry; winch reach is unused by the rig | UPG-01 runtime, VEH-03, DRIVE-02 | Open — M1 calibration: per-wheel `VehicleStatsResolver.GripFor(tireSet, surface, …)` friction plus suspension geometry, tuned in-editor. |
| KI-026 | Minor | Campaign Unity glue is syntax-checked only | `CampaignHost`, the slot UI in `TitleFlowHost`, `VerticalSliceDriver`, `SceneBuilder.BuildVerticalSlice`, `ProjectSetup.CopyCampaignContent`, 2 new PlayMode tests and 2 new editor tests have never been compiled or run by Unity. A scratch type-check against hand-written UnityEngine stubs passed and caught one real ambiguity (`UnityEngine.Pose`, fixed with an alias), but stubs cannot detect real API drift | M2 scene flow | Open — first editor open: Setup Project → Test Runner (PlayMode) → play Title → New → VerticalSlice. |
| KI-027 | Minor | Slice placements are compact and the slice ground is flat | Content `positionHint`s for forest landmarks/caches cluster within ~260 m of the home garage (KI-008), so most of the 1,024 m area is empty and trips are short; the greybox scene has a flat ground plane plus one hill, a ford and a mud patch — no heightfield terrain | M2 slice feel, 30-minute target | Open — M3 route validation/WorldBaker may move positions (ids stable); terrain bake needed before slice playtests. |
| KI-028 | Minor | Storage/modules/hub travel simplified for the slice | One shared 500 kg camp store (save has a single `campStorage`), researched modules auto-installed (no §10.2 slot equipping), no camp↔garage hub-travel action in `CampaignSession` yet (`GarageService.TryHubTravel` exists), selling and fuel-can/repair-kit/tire-patch use are now exposed (D-029) | CAMP-01, UPG-01, ITEM-01 | Open — D-023; needs a schema bump for per-camp storage. |
| KI-029 | Minor | Map terrain comes from the slice manifest, not baked data | `SliceMapTerrain` rebuilds the greybox from `vertical_slice.json` (flat ground, 8 m hill, ford, mud, arrival pad), so the map's relief and the reveal line-of-sight are only as rich as the greybox. The map sheet covers the 1,024 m slice area; cells outside it count toward mainland % but are not drawn, and outside the area heights are unknown (NaN, never occlude). Forest/meadow colour variation is not invented. | MAP-01/MAP-04 fidelity | Open — M3: implement `IMapTerrainSource` over the baked heightfield/minimap tiles and pass `fullMainland: true` to `ExplorationMap`. |
| KI-030 | Minor | No route graph: routes and return estimates are straight-line estimates | The route line to a custom marker is a dashed straight line, and the "Return" estimate is straight-line distance × 1.3 detour × L/km (measured once ≥ 300 m has been driven, otherwise the 2.7 L/km nominal). There are no solid "known route" segments (§13.4) and no pathing around water or cliffs | MAP-04, §13.4 routes | Open — needs the M3 `RouteGraph` bake plus driven-road tracking. |
| KI-031 | Minor | Map/minimap Unity UI is syntax-checked only | `MapTextureBinding`, `MapGlyphs`, `MinimapView`, `MapOverlayView` and the `VerticalSliceDriver` map wiring were never compiled or run by Unity. A scratch stub type-check against the real Wayfinder assemblies passed, but it cannot detect Unity API drift. Unverified: real touch pinch and long-press feel, `RectMask2D` clipping, the 1 MB texture upload cost at ≤ 4 Hz on a phone, and rich-text legend glyphs in `LegacyRuntime.ttf`. The map pause uses `Time.timeScale`/`SuppressInputs` in the driver, not a central `AppState.MapPaused` state machine. Map strings are English literals (no localization keys yet) and have no TalkBack/VoiceOver labels | MAP-04, UI-01, §19 | Open — first editor open: run `MapPlayModeTests` (2), then play: M / minimap tap → pinch, pan, long-press → place → route → close. |
| KI-032 | Major | Researched Trail Tires I grip and Engine Tune torque had no effect anywhere | `VehicleStatsResolver` recorded the `DirtGravelGrip` ×1.10 modifier but never composed it into grip, and `Powertrain` read only the base torque curve, ignoring the resolved `PeakTorqueNm`. Both upgrades were bought and displayed but changed nothing | UPG-01, §11.1 tiers 1 and 10 | **Fixed 2026-10-15** (D-030): `VehicleStatsResolver.GripFor` (all-terrain set on packed dirt/gravel only) and `Powertrain.ResearchTorqueScale`. Regression tests `TrailTiresI_…_KI032` and `EngineTune_…_KI032`; both re-introductions are caught by the mutation check. |
| KI-033 | Minor | The free cloud build route has never been executed | **Executed 2026-10-15 (Batch F).** The owner dispatched it five times; every run failed at *Unity 1 of 2: Setup Project* with Unity's undescriptive `Build failed with exit code 1`. The owner-supplied log of run 38754979851 shows one cause, in Unity's own package: `com.unity.inputsystem@1.12.0` `InputSystemPluginControl.cs(47,25): error CS0117: 'BuildTarget' does not contain a definition for 'ReservedCFE'` — an enum member Unity 6000.3 removed. Fixed by repinning Input System to 1.20.1 (D-031; the upstream reference is gone from 1.13.1 on). The workflow now also reports its own failures as check-run annotations (`Tools/Build/report_unity_failure.sh`), so a future failure no longer needs a manual log paste | M0 gate (KI-006), build pipeline | Mitigated — route proven to reach a real, licensed Unity 6000.3.26f1 and to compile this project; still **no APK** (KI-006) and the Input System fix is not yet proven by a run (KI-036). |
| KI-034 | Major | The four Unity-facing Wayfinder assemblies have still never been compiled by Unity | They sit behind the failed `Unity.InputSystem` assembly, so Unity aborted before reaching them: `Wayfinder.Platform`, `Wayfinder.Presentation`, `Wayfinder.Editor` and the EditMode/PlayMode test assemblies remain **parse-checked + stub-type-checked only**. The six engine-free assemblies *are* now Unity-compiled (Batch F), so the remaining API-drift risk is confined to the ~23 files that touch `UnityEngine.InputSystem`, `UnityEngine.UI`, URP and `UnityEditor` | M0/M1/M2 Unity glue, KI-026/KI-031 | Open — the next cloud run compiles them; `report_unity_failure.sh` turns any `error CS` into a file/line annotation. |
| KI-035 | Minor | The agent cannot dispatch the cloud build | The installed GitHub token returns `HTTP 403: Resource not accessible by integration` for `POST /repos/.../actions/workflows/{id}/dispatches` (no `actions: write`), even though it can push and open PRs | Build iteration speed | Open — the owner runs **Actions → Android APK (free cloud build) → Run workflow** (a branch can be selected), or grants the integration `actions: write`. |
| KI-036 | Minor | The Input System 1.20.1 repin is reasoned, not yet executed | 1.20.1 is the newest 1.x (2026-10-05, package minimum `"unity": "6000.0"`); the `ReservedCFE` reference is gone from 1.13.1 on, verified in the upstream package source. No cloud run has yet confirmed that 1.20.1 resolves on the registry and compiles on 6000.3.26f1. Fallback ladder if it does not: 1.16.0 (first release with an explicit Unity 6.3 fix) → 1.14.2 → 1.13.1. **Never** 1.12.0/1.13.0 | Build pipeline, D-031 | Open — one dispatch settles it; `report_unity_failure.sh` reports a resolution failure distinctly from a compile error. |
## 15 October 2026 — free cloud build route

The GitHub-hosted GameCI workflow was added so a development APK no longer requires the
owner's Unity machine or the self-hosted runner. It is the cheapest way to close KI-006,
but it is **unexecuted** (KI-033): no build log, no APK and no device result may be
claimed from it until the owner runs it once with license secrets configured. The
self-hosted dedicated-runner route remains the trusted one for owner-controlled builds.

## 15 October 2026 source-level update (not Unity gate closure)

The setup/prototype/build batch in [UNITY_HANDOFF.md](UNITY_HANDOFF.md) partially
mitigates KI-003/004/006: input/test assembly references and known API-use errors
are corrected, setup now generates URP/quality/input/scenes, a four-wheel prototype
is wired, and Android release source explicitly requests a signed AAB. These
engine-facing changes are **syntax-checked only**. KI-001/002/005 and all actual
Unity/device gates remain open. The pure-domain suite now passes 55/55 tests;
runner unit tests pass 23/23 using mocked subprocesses, not Unity.

Source defects corrected in this batch: wrong tilt method, read-only quality-name
assignment, input-asset disposal, nonexistent WheelHit distance, static AWD method
called through an instance, missing wheel rig, touch state overwritten by Update,
engine-off/empty-tank propulsion, zero-steer phantom turn, left Ackermann swap,
no reverse propulsion and incorrect negative AWD caps. Six new pure C# regression
tests execute the steering/reverse/AWD/handbrake fixes. Added Unity editor/physics
regressions have not executed. Runtime water-depth and several hazard probes remain
placeholders; a generated obstacle is not proof of implemented hazard gameplay.

## Later same-day mobile continuation

Android callback overlap is now handled by a pure `MobileLifecycleGate` with four
executed regressions; total domain suite is 59/59. A focus gain cannot undo an
application pause. Authored water triggers now feed depth/stall checks, but full
water forces and mud depth remain incomplete. Seven PlayMode tests are written,
not executed. Mobile starts with a 30 FPS target and an optional 60 FPS target;
no Moto G45 frame-rate/thermal measurement exists.

The Unity installation/package/network blocker remains **open**. A manual
self-hosted Windows build workflow and Moto G45 instructions are prepared, not
operationally verified. The public repository needs a dedicated secured runner;
no everyday personal machine should be exposed to untrusted branch/PR code.

## 15 October 2026 — campaign composition batch (Batch C)

Composing the services into a per-slot `CampaignSession` exposed seven domain defects
(KI-018 … KI-024), all fixed with regression tests; an 8-case mutation check shows the
new tests fail when each fix or safeguard is reverted. The pure campaign layer is
**Verified (headless)**: 109/109 EditMode tests, 0 warnings, .NET Standard 2.1 profile OK.
The Unity-facing campaign glue is **syntax-checked only** (KI-026). KI-001/002/005/006 and
every Unity/device gate remain open; M2's gate is not passed.

## 15 October 2026 — exploration map batch (Batch D)

New: KI-029 (map terrain from the slice manifest), KI-030 (straight-line route/return
estimates), KI-031 (map UI syntax-checked only). Fixed in this batch: vista *surveyed* flags
ignored terrain occlusion (D-028). Before this batch the reveal had no line-of-sight filter at
all in the campaign path, and it now has one; the map-reveal cadence follows §16.2 (2 Hz).
Headless evidence: `Docs/evidence/exploration-map-2026-10-15/` (130/130 tests, 11/11 mutation
check). The M2 gate is still **not** passed.

Continuation (same batch): field supplies and selling exposed (D-029, KI-028 narrowed). Research now
reaches the rig resolver (D-030, KI-025 partially resolved). KI-032 was found and fixed: Trail Tires I
and Engine Tune had no effect. Evidence: `Docs/evidence/supplies-and-stats-2026-10-15/`.

## 15 October 2026 — Batch F: the cloud build ran for real

The first executed Unity evidence for this repository. Five owner dispatches of
`.github/workflows/android-cloud-build.yml` all failed at the same step with
`Build failed with exit code 1`; the owner-supplied log of run **38754979851**
(`Docs/evidence/cloud-build-2026-10-15/`) shows:

- a Unity **Personal license activated** from the repository secrets — the "no license"
  assumption behind KI-006 is gone;
- the `unityci/editor` image for **6000.3.26f1 (`de8167e7440c`)** + Android exists and runs;
- every `Packages/manifest.json` pin **resolved**;
- Unity **compiled all six engine-free Wayfinder assemblies** with exactly one warning
  (`ChaseCameraBehaviour._photoMode` write-only, CS0414 — fixed: photo mode now bounds the
  orbit per frame instead of permanently mutating the authored distance);
- and one fatal error, **inside Unity's Input System package**, not in Wayfinder code:
  `error CS0117: 'BuildTarget' does not contain a definition for 'ReservedCFE'`.

Fixed by repinning `com.unity.inputsystem` 1.12.0 → **1.20.1** (D-031) and committing the
verified editor pin (D-032). Because Unity's exit code says nothing, the workflow now
self-reports: `Tools/Build/report_unity_failure.sh` converts the surviving `Editor.log` into
check-run annotations (compile errors with file/line/column, batchmode abort reason, editor
exceptions, package-resolution failures, warnings in `Assets/Wayfinder`, which Wayfinder
assemblies Unity compiled, and the APK size), and `Tools/Build/check_unity_secrets.sh` names a
missing secret in seconds without ever printing a value. 33 tests and a 9/9 mutation check
cover both (`mutation-check.log`).

**Still no APK, no scene generation, no PlayMode run and no device result.** KI-006 stays open
and the M0/M1/M2 gates are unchanged; KI-034 is the newly-visible next risk.
