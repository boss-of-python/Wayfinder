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
| KI-006 | Major | No installable device smoke APK produced (M0 gate) | **Narrowed 2026-10-20:** a cloud-build APK is now **installed and running on a Moto G45** — the greybox vertical-slice scene renders, the HUD shows fuel/cargo/salvage/speed, touch controls (BRAKE/GAS/RIGHT/HOLD INTERACT) respond, the pause overlay (Resume/Rescue/Save & quit) works, the minimap draws, and Q01 is active on a moving vehicle. The build pipeline now produces a usable APK. **Still open:** the Development Console reports `InvalidOperationException` for the `Steer` composite bindings (new KI-045). Until the binding order is fixed and a clean rebuild re-runs on device, the M0 smoke gate is "reached but not closed". Earlier runs 39139527909 / 39141218150 / 39141401948 (2026-10-18) each failed in *Unity 1 of 2: Setup Project*; the escape-sequence fetch fix (D-036) and the `Wayfinder.Editor` asmdef fix (PR #15) together cleared the way to the APK. | M0 exit gate | **Narrowed** — APK exists and runs; blocked on composite binding fix (KI-045) and a clean rebuild/re-test. |
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
| KI-017 | Major | `REQUIREMENT_MAP` previously listed `World/RoadGraph.cs`, `World/ScatterPlacer.cs` and `World/WorldStreamer.cs` as implemented — **they did not exist** | Found 2026-10-14 while cross-checking the map against the headless compile report. **Engine-free part resolved 2026-10-16 (Batch G):** `World/ScatterPlacer.cs` (§7.6 rules, per-candidate seeding, border-safe spacing) and `World/ChunkStreamPlanner.cs` (§18.3 streaming state, hysteresis, pinning, budget, handle ledger) exist and are executed headlessly (164/164, 15/15 mutants). The `WorldStreamer` MonoBehaviour host, Addressables handles and real-terrain scatter are still missing (KI-037, KI-038). `RoadGraph.cs` was never created: road carve remains a hook | GEN-03, ARCH-03, PERF-01 (M3) | **Mitigated** — planner and placer verified headless; host and baked-terrain integration open (KI-037/038). |
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
| KI-028 | Minor | Storage/modules/hub travel simplified for the slice | **Hub travel and per-camp storage closed 2026-10-21 (Batch J, D-039/D-040).** `CampaignSession.CurrentHubId` + `TravelToHub(destinationHubId, inWater, inDeepMud)` implement §10.4: free and instant, only from an activated garage or camp, only to another activated hub whose content declares the `hubTravel` service, never out of water or deep mud (full rescue is that way out), never re-basing the extraction target mid-expedition, and committed only with a verified save (full rollback otherwise). `SpawnFor` now resolves activated camp sockets, so a session parked at a camp departs from that camp. `AtGarage` separates "parked at a garage" from "parked", so a camp offers save/deposit/fuel/repair supplies/time of day/map/hub travel but no research, mission board, contracts, loadout, crafting or selling. Camp storage is per-camp (`CampStorageFor(campId)`, capacity from that camp's `storageCapacityKg`), persisted as `campStores` at **schema 2** with the project's first migration merging the pooled v1 `campStorage` into the first activated camp (or into garage storage when none exists) — no unit lost (§17.5). **Still open in this row:** researched modules are auto-installed; §10.2 module *slot equipping* is not implemented. **Not covered:** hub travel has no Unity UI yet (KI-026), and `DepositAtCamp` does not require physical proximity to that camp (pre-existing, no camp-visit check exists in the domain). | CAMP-01, UPG-01, ITEM-01 | **Partly resolved 2026-10-21** — hub travel + per-camp storage Verified (headless): 28 new tests, 27/27 mutation checks, 216/216 suite. Module slot equipping remains open. |
| KI-029 | Minor | Map terrain comes from the slice manifest, not baked data | `SliceMapTerrain` rebuilds the greybox from `vertical_slice.json` (flat ground, 8 m hill, ford, mud, arrival pad), so the map's relief and the reveal line-of-sight are only as rich as the greybox. The map sheet covers the 1,024 m slice area; cells outside it count toward mainland % but are not drawn, and outside the area heights are unknown (NaN, never occlude). Forest/meadow colour variation is not invented. | MAP-01/MAP-04 fidelity | Open — M3: implement `IMapTerrainSource` over the baked heightfield/minimap tiles and pass `fullMainland: true` to `ExplorationMap`. |
| KI-030 | Minor | No route graph: routes and return estimates are straight-line estimates | The route line to a custom marker is a dashed straight line, and the "Return" estimate is straight-line distance × 1.3 detour × L/km (measured once ≥ 300 m has been driven, otherwise the 2.7 L/km nominal). There are no solid "known route" segments (§13.4) and no pathing around water or cliffs | MAP-04, §13.4 routes | Open — needs the M3 `RouteGraph` bake plus driven-road tracking. |
| KI-031 | Minor | Map/minimap Unity UI is syntax-checked only | `MapTextureBinding`, `MapGlyphs`, `MinimapView`, `MapOverlayView` and the `VerticalSliceDriver` map wiring were never compiled or run by Unity. A scratch stub type-check against the real Wayfinder assemblies passed, but it cannot detect Unity API drift. Unverified: real touch pinch and long-press feel, `RectMask2D` clipping, the 1 MB texture upload cost at ≤ 4 Hz on a phone, and rich-text legend glyphs in `LegacyRuntime.ttf`. The map pause uses `Time.timeScale`/`SuppressInputs` in the driver, not a central `AppState.MapPaused` state machine. Map strings are English literals (no localization keys yet) and have no TalkBack/VoiceOver labels | MAP-04, UI-01, §19 | Open — first editor open: run `MapPlayModeTests` (2), then play: M / minimap tap → pinch, pan, long-press → place → route → close. |
| KI-032 | Major | Researched Trail Tires I grip and Engine Tune torque had no effect anywhere | `VehicleStatsResolver` recorded the `DirtGravelGrip` ×1.10 modifier but never composed it into grip, and `Powertrain` read only the base torque curve, ignoring the resolved `PeakTorqueNm`. Both upgrades were bought and displayed but changed nothing | UPG-01, §11.1 tiers 1 and 10 | **Fixed 2026-10-15** (D-030): `VehicleStatsResolver.GripFor` (all-terrain set on packed dirt/gravel only) and `Powertrain.ResearchTorqueScale`. Regression tests `TrailTiresI_…_KI032` and `EngineTune_…_KI032`; both re-introductions are caught by the mutation check. |
| KI-033 | Minor | The free cloud build route has not produced an APK | **Executed 2026-10-15 (Batch F):** five owner dispatches reached *Unity 1 of 2: Setup Project* and failed with `Build failed with exit code 1`. Run 38754979851 exposed the Unity Input System 1.12.0 `BuildTarget.ReservedCFE` error; the manifest is now repinned to 1.20.1 (D-031), but that fix remains unverified (KI-036). Run 38858427636 then stopped before Unity because preflight wrongly required `UNITY_LICENSE`; preflight now accepts account credentials or a license file. Runs 38875757162, 38880573866 and the three of 2026-10-18 (39139527909/39141218150/39141401948) all reached Setup and failed; none could be diagnosed, because the log fetcher was defeated by gh's escape-sequence guard (KI-041, fixed by D-036 — probe `--allow-escape-sequences`, sanitise the log, save a 400-line excerpt). | M0 gate (KI-006), build pipeline | Mitigated — preflight and diagnostics code corrected and now rehearsed against a faithful gh ≥ 2.63 stub; still open until a run verifies package resolution/build and produces an APK. |
| KI-034 | Major (**one concrete error found 2026-10-19; the rest still uncompiled**) | The four Unity-facing Wayfinder assemblies have still never been compiled by Unity | They sat behind the failed `Unity.InputSystem` assembly, so Unity aborted before reaching them: `Wayfinder.Platform`, `Wayfinder.Presentation`, `Wayfinder.Editor` and the EditMode/PlayMode test assemblies remain **parse-checked + stub-type-checked only**. The six engine-free assemblies *are* Unity-compiled (Batch F). The 1.20.1 repin (D-031) removed the blocker that was hiding them, so **runs 39139527909 → 39141401948 are the first in which Unity reaches them** — and each dies in 5–6 min with **no `wayfinder-editor-failure.log`**, i.e. before `ProjectSetup.Setup` can throw. **Found by source review (PR #15):** `Wayfinder.Editor`'s `ContentImporter.cs` uses `Newtonsoft.Json.Linq` (`:112`, `:135`, `:162`, `:171`, `:181`, `:204`) but the asmdef did not reference `Unity.Newtonsoft.Json`; Unity requires direct references, so this is a CS0234 that aborts batchmode before `-executeMethod` — exactly the observed shape, and now fixed. The other ~22 files (touching `UnityEngine.InputSystem`, `UnityEngine.UI`, `UnityEngine.EventSystems`, URP and `UnityEditor`) remain uncompiled by Unity, and stub type-checks cannot detect real API drift | M0/M1/M2 Unity glue, KI-026/KI-031, and KI-041 | Open — narrowed from "four assemblies unverified" to "one error fixed, three assemblies still unverified". The next dispatch settles it: `report_unity_failure.sh` turns any `error CS` into a file/line/col annotation, and the fixed fetcher (D-036) is what delivers the log to it. |
| KI-035 | Minor | The agent cannot dispatch the cloud build | The installed GitHub token returns `HTTP 403: Resource not accessible by integration` for `POST /repos/.../actions/workflows/{id}/dispatches` (no `actions: write`), even though it can push and open PRs | Build iteration speed | Open — the owner runs **Actions → Android APK (free cloud build) → Run workflow** (a branch can be selected), or grants the integration `actions: write`. |
| KI-036 | Minor | The Input System 1.20.1 repin is reasoned, not yet executed | 1.20.1 is the newest 1.x (2026-10-05, package minimum `"unity": "6000.0"`); the `ReservedCFE` reference is gone from 1.13.1 on, verified in upstream source. **Narrowed 2026-10-19:** *resolution* is now unlikely to be the problem — runs 39139527909 → 39141401948 each ran Unity for 5–6 minutes and left four shader-compiler logs behind, which a UPM resolution failure would not reach. What is still unproven is that 1.20.1 **compiles** on 6000.3.26f1, and that is now folded into KI-034/KI-041. Fallback ladder if 1.20.1 fails: 1.16.0 (first release with an explicit Unity 6.3 fix) → 1.14.2 → 1.13.1. **Never** 1.12.0/1.13.0 | Build pipeline, D-031 | Open — narrowed; a fresh dispatch with the fixed fetcher (D-036) reports resolution *and* compile results. |
| KI-037 | Major | No Unity host drives the streaming planner | `ChunkStreamPlanner` is pure C# and tested against a simulated host (`StreamHostSim`). The MonoBehaviour that calls `Step`, performs the async loads, instantiates chunks within the per-frame budget and calls `MarkDataReady`/`MarkActive`/`MarkDeactivated`/`MarkFailed` does not exist. Addressables handle release, cancellation of in-flight loads, and the "loading hold" UI are therefore unverified; QA case 12 is verified at planner level only | M3 gate, PERF-01, QA-01 case 12 | Open — write the host after the cloud run (KI-035/036) can compile it; then PlayMode test: repeated crossings with real handles. |
| KI-038 | Minor | Scatter is verified on an analytic flat surface, not on baked terrain | `ScatterPlacer` takes an `IScatterSurface` (height, slope, road-edge distance, reserved pads). The tests use one flat plane, one straight road and one reserved disc. Real density, slope limits on the baked heightfield, road-carve geometry and POI footprints are unmeasured. Caches and landmarks are not placed (they need validated sockets, §7.2 step 9) | GEN-03, §7.6 | Open — implement the baker's `IScatterSurface` in `Editor/WorldBaker.cs` and record density/slope statistics per biome. |
| KI-039 | Blocker (found + fixed) | The build diagnostics were blind: run 38875757162 produced **zero** useful diagnostic annotations | Run 38875757162 proved that game-ci logs Unity to `/dev/stdout`, while the workflow attempted to read nonexistent `Logs/Unity-Setup.log`; only worker/shader logs survived. The new diagnose job downloads the finished build job log, and the reporter identifies inspected files and missing editor logs. Follow-up run 38880573866 exposed a second gap: when only worker logs existed, the missing-log error suppressed their tail; the reporter now emits a separately labeled workspace-log tail. Its diagnose attempt still used pre-PR-#12 code and did not preserve the underlying download error. The current manual workflow also pre-creates `error_logs/apk-build-errors.log` and uploads the raw log and report in `Wayfinder-APK-error-logs`. | Every cloud-build failure since D-033 | **A third gap was found and fixed 2026-10-19 (D-036):** the fetcher itself never received a byte, because `gh api` refuses raw bodies containing terminal escape sequences (KI-041). All three layers are now covered by regression tests — 91/91 diagnostics tests, 19/19 mutations for this batch and 22/22 for Batch H, plus a six-part dress rehearsal that reproduces the refusal with the *old* fetcher, downloads over both paths with the new one, and shows with an unsanitised control why sanitising is load-bearing. Still **not verified in CI**; a dispatch is what proves it. |
| KI-040 | Minor | GameCI cannot auto-detect the Android API level for this project | `dist/platforms/ubuntu/entrypoint.sh` greps `ProjectSettings/ProjectSettings.asset` for `AndroidTargetSdkVersion`; this repository does not commit that file (KI-004/005), so the SDK manager is asked for `platforms;android-` and logs `Warning: Failed to find package 'platforms;android-'` (seen in run 38754979851). The Unity image ships an Android SDK, so it is a warning, not the failure | Step 2 (APK build) | Open — harmless today. Fix by committing generated `ProjectSettings.asset` after the first editor open, or by setting the `androidTargetSdkVersion` input (the CLI then passes `platforms;android-<n>` to sdkmanager). Do not guess the level before Unity has written one. |
| KI-041 | Blocker | Why the Unity Setup Project step fails is still unknown — but the reason it stayed unknown is found and fixed | **2026-10-19:** three consecutive runs (**39139527909**, **39141218150**, **39141401948**) each failed in *Unity 1 of 2: Setup Project* after 5.3–6.5 min, and each diagnose job reported the same refusal 12 times: `the response contains terminal escape sequences; pass --allow-escape-sequences to output it anyway`. `gh api` guards raw non-JSON bodies and a Unity job log is full of ANSI colour and `\r` redraws, so **nothing was ever written** — and redirecting stdout does not bypass the guard (`bodyWriter != io.Discard` in `cli/cli`'s `pkg/cmd/api/api.go`). The "needs `actions: read`" hint in that notice was wrong: the permission was granted, the CLI refused the payload. Fixed (D-036): the fetcher probes `gh api --help` for the flag, passes it when supported, sanitises escape sequences/`\r` out of the log it saves, writes a 400-line `.excerpt` tail for the artefact and reports the byte delta it stripped. **What the run data does establish:** Unity really ran for minutes; four shader-compiler logs (`buildPlatform=13`) survived, so packages resolved and import was under way; and **no `wayfinder-editor-failure.log` exists**, so `ProjectSetup.Setup` threw no C# exception — the cause is earlier than our entry point (compile error, licence, unresolvable `-executeMethod`) or the process was killed. **Root cause identified from source (PR #15, merged mid-batch): KI-034 was right about the shape** — `ContentImporter.cs` uses `Newtonsoft.Json.Linq` at six places while `Wayfinder.Editor.asmdef` did not reference `Unity.Newtonsoft.Json`, and Unity asmdefs require *direct* references. That is a CS0234 in an Editor-only assembly, which aborts batchmode **before** `-executeMethod` and so leaves no breadcrumb — matching every observation above. The reference is now added. | M0 gate (KI-006), every cloud-build diagnosis | **Narrowed to one unverified fix.** Diagnostics: 91/91 tests, 19/19 mutations, 22/22 for Batch H, dress rehearsal green over both download paths (`Docs/evidence/cloud-build-2026-10-19/`). The asmdef fix is reasoned from source, **not compiled by Unity**, and a second error may hide behind it. One dispatch now yields either an APK or `file=…,line=…,col=…` annotations. |
| KI-042 | Minor | The Unity `Library` import cache can never warm up while the build fails | `GET /repos/{owner}/{repo}/actions/caches` returns exactly **one** entry (`game-ci-cli-v0.1.72-linux-x64`, 37,856,426 B); there is no `Library` cache. `Post Reuse Unity's import cache from earlier runs` is **skipped in all four recent runs** because the pinned `actions/cache@55cc834` declares `post-if: "success()"` (verified in its `action.yml`), so the cache is saved only when the whole job succeeds. Consequence: every dispatch pays a full cold import of URP/Addressables/Localization/Cinemachine/Input System, and restore takes 0.00–0.02 min. **Not a cause of any failure** — the Unity step still runs 5–6 min. Deliberately left alone: `actions/cache/save` with `if: always()` would cache a `Library` written by a *failed* import, adding an uncontrolled variable to a diagnosis that is not finished (D-036) | Cloud-build runtime/cost, `Library` cache key in `.github/workflows/android-cloud-build.yml` | Open — revisit after the first successful Unity run, when a warm cache is both safe and useful. |
## 15 October 2026 — free cloud build route (historical introduction)

The GitHub-hosted GameCI workflow was added so a development APK no longer requires the
owner's Unity machine or the self-hosted runner. At its introduction it was unexecuted;
Batch F below records its first real Unity run. It still has not produced an APK: the
latest attempt and missing-secret requirement are tracked in KI-006/KI-033. The
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
self-reports: `Tools/Build/report_unity_failure.sh` converts the surviving Unity log into
check-run annotations (compile errors with file/line/column, batchmode abort reason, editor
exceptions, package-resolution failures, warnings in `Assets/Wayfinder`, which Wayfinder
assemblies Unity compiled, and the APK size), and `Tools/Build/check_unity_secrets.sh` names a
missing secret in seconds without ever printing a value. 33 tests and a 9/9 mutation check
cover both (`mutation-check.log`).

**Still no APK, no scene generation, no PlayMode run and no device result.** KI-006 stays open
and the M0/M1/M2 gates are unchanged; KI-034 is the newly-visible next risk.

## 16 October 2026 — Batch G: scatter placement and streaming planner (KI-017, KI-037, KI-038)

Engine-free M3 work. Two real defects were found by the new tests and fixed: integer division in the scatter cell size
(the candidate grid did not tile the chunk) and chunk-index hysteresis that thrashed at boundaries (≈3 deactivations
per crossing). Details and bounds: `Docs/evidence/world-streaming-2026-10-16/README.md`, decision **D-034**.
Headless only: 164/164 EditMode tests, 15/15 mutants caught. KI-037 and KI-038 are the new open items.

## 16 October 2026 — Batch H: the cloud build now explains itself (KI-039, KI-041)

Run **38875757162** (the first dispatch after PR #10) failed in *Unity 1 of 2: Setup Project* and
produced exactly two annotations: GameCI's `Build failed with exit code 1` and GitHub's notice
that `ubuntu-latest` migrates to Ubuntu 26 on 2026-10-19. The diagnostics step that exists to
prevent precisely that said nothing — not even its own "no log" notice.

The cause was not Unity's: game-ci starts the editor with `-logfile /dev/stdout` before any
custom parameter, so Unity's editor log is the **job log**, and the workflow's
`customParameters: -logFile …` delivered nothing. What the reporter actually globbed were Unity's
`Logs/AssetImportWorker*.log` and shader-compiler logs, which match none of its patterns, so
"a log exists but explains nothing" looked exactly like a healthy build.

Fixed in this batch (D-035): a second job downloads the finished build job's log from the Actions
API and annotates from it; `report_unity_failure.sh` is never silent (it names every file it
inspected with sizes, flags a missing editor log, and publishes a 40-line tail when a failure
matches no pattern); `ProjectSetup.Setup` and the `BuildCommands` entry points record their
exception in `Logs/wayfinder-editor-failure.log` before rethrowing; the dead `-logFile` override
is removed and the runner is pinned to `ubuntu-24.04`. Evidence:
`Docs/evidence/cloud-build-2026-10-16/` — 86/86 Python tests, 17/17 mutations caught, headless C#
164/164 with 30 Unity files parsed.

**Still no APK and no Unity verification of any of this.** The Unity failure itself is KI-041 and
needs one owner dispatch; KI-006/KI-033/KI-034/KI-036 stay open, and the M0 gate is unchanged.

## 19 October 2026 — Batch I: the log was unreachable, and the compile error behind it (KI-041, KI-042, KI-034)

Five dispatches in a row ended the same way: *Unity 1 of 2: Setup Project* fails after five or six
minutes, GameCI prints `Build failed with exit code 1`, and the job whose whole purpose is to
explain that prints a refusal instead. On 2026-10-19 the refusal was finally read as the finding
it was. All three diagnose jobs of 2026-10-18 said, twelve times each:

```
the response contains terminal escape sequences; pass --allow-escape-sequences to output it anyway
```

`gh api` guards raw non-JSON response bodies, and a game-ci Unity job log is full of ANSI colour
and `\r` progress redraws. Confirmed in gh's own source (`pkg/cmd/api/api.go`): the guard is
`!isJSON && !opts.AllowEscapeSequences && bodyWriter != io.Discard`, so **redirecting stdout to a
file does not bypass it** — the workflow's `> error_logs/unity-job-log.txt` never received a byte.
The notice's own advice ("Needs `actions: read`") was wrong and had sent three previous
investigations after a permission that was already granted. D-035 designed the right mechanism; a
CLI safety check added later defeated it, and the tests had stubbed a permissive `gh`.

Fixed (D-036): probe `gh api --help` for the flag and pass it only when supported (on older `gh` it
is an unknown-flag hard error), sanitise CSI/OSC sequences and `\r` out of the saved log, write a
400-line `.excerpt` tail into the artefact, and report the byte delta sanitising removed. The
reporter gained two things the run data asked for: crash/OOM-kill/fatal-error detection (a killed
editor throws no C# exception, so it leaves no breadcrumb and previously looked like "nothing
matched"), and an explicit statement when the breadcrumb is *absent* — which is itself evidence.
Writing the sanitiser exposed one more bug, kept as a fixture: `[ -\/]` is not an escaped slash
inside a POSIX bracket expression, it is the range space..backslash, and it ate the text following
`ESC[K`.

What the Actions API established about the Unity failure, without a dispatch: Unity ran for minutes,
four shader-compiler logs for `buildPlatform=13` survived (so packages resolved and import was under
way — KI-036 narrows), and `Logs/wayfinder-editor-failure.log` does **not** exist, so
`ProjectSetup.Setup` threw nothing. The cause had to be earlier than our entry point, or the process
was killed. That pointed at KI-034 — and KI-034 then produced a concrete error by source review
(PR #15, merged while this batch was in flight): `ContentImporter.cs` uses `Newtonsoft.Json.Linq`
while `Wayfinder.Editor.asmdef` did not reference `Unity.Newtonsoft.Json`, and Unity asmdefs require
*direct* references. A CS0234 in an Editor-only assembly aborts batchmode before `-executeMethod`,
which is exactly the observed shape. The reference is added; it is reasoned from source, not
compiled by Unity, and another error may hide behind it.

The two halves are complementary rather than competing, which is why they were merged instead of
chosen between: PR #15's `curl`-first chain gets the bytes past gh's guard, and sanitising them is
what makes them parsable. The dress rehearsal proves the second half is load-bearing — handed the
same bytes unsanitised, the reporter still announces `Aborting batchmode due to failure: Scripts
have compiler errors.` (that line carries no escapes) while losing the `file=…,line=…,col=…`
annotation for the compile error itself. A verbatim download therefore *looks* diagnosed while the
one thing worth knowing is missing.

Also verified and deliberately not changed: the repository holds exactly one cache entry, and
`Post Reuse Unity's import cache` is skipped in every run, because `actions/cache@55cc834` declares
`post-if: "success()"`. No warm `Library` is possible until a run succeeds once (KI-042).

Evidence: `Docs/evidence/cloud-build-2026-10-19/` — 91/91 diagnostics tests, 23/23 pipeline tests,
19/19 mutations for this batch and 22/22 for Batch H, and a six-part dress rehearsal that runs the
*base-commit* fetcher against a faithful gh ≥ 2.63 stub (reproduces the refusal, writes nothing),
then the current one over **both** download paths, then an unsanitised control, then the reporter.

**Still no APK, and nothing here has run in CI.** The fetch path is proven against stubs and gh's
own source, not against a live Unity log; the asmdef fix is proven by reading the source, not by
Unity compiling it. One owner dispatch now closes KI-041 either way — an APK, or file/line/col
annotations for whatever fails next. KI-006/KI-033/KI-034 stay open and M0 is unchanged.


---

## KI-043 — Contract sockets are not placed in the world (open)

`Content/contracts.json` names 35 rows (30 authored + 5 repeatable templates) and each row lists
`contract_socket.*` targets. `Assets/Wayfinder/Content/sites.json` (added 2026-10-19) places every
*mission* objective target, but no catalog places a contract socket, and `SliceFieldController`
only considers vertical-slice sites. Accepting and completing a contract now works end to end
(DEF-007), so the missing piece is the in-world geometry that drives the objectives, not the
reward path. `validate_content.py` enforces reachability for mission objectives only — extending
it to contract targets is the next step, and it is expected to fail until the sockets are authored.

## KI-045 — Input System composite binding order error on device (**resolved 2026-10-20**)

**Severity:** Minor (touch controls worked; the exception was dev-console noise; keyboard steering silently broken).

**Fixed:** inserted a `2DVector` `KeyboardMouse` composite binding at the top of the `Steer` binding list in `WayfinderControls.inputactions`, matching the working `UI/Navigate` pattern already in the same file. Verified by JSON parse + structural check: every `isPartOfComposite` binding now has a preceding `isComposite` binding. File is a data-only change — no C#, no asmdef, no package.

**Verification pending:** rebuild the APK, install on the Moto G45, open the Development Console and confirm (a) no `InvalidOperationException` at startup, (b) WASD / arrow-key steering produces non-zero `Steer` values, (c) touch controls still respond. Then attempt Q01 end-to-end.

**Affects:** DRIVE-01, UI-01, M2 gate. Evidence: screenshot `Screenshot_20261020-192309.Wayfinder_ The Quiet Frontier.png` (Development Console panel); fix in `Assets/Wayfinder/Content/Input/WayfinderControls.inputactions`.

---

## KI-044 — The Unity-facing defect fixes are type-checked, not compiled (open,KI-034)

DEF-005, DEF-006, DEF-013, DEF-014 and DEF-015 changed files Unity compiles and nothing else does
(`VehicleController.cs`, `ChaseCameraBehaviour.cs`, `InputRouter.cs`, `VerticalSliceDriver.cs`).
`Tools/Build/unity_type_check.py` compiles all 12 assemblies against hand-written engine stubs and
reports 12/12 clean with 0 warnings, which removes the most likely class of KI-034 error. It is
**not** a Unity compilation: the stubs were shaped by the errors this code produced, so they prove
the absence of the errors they can see. One PlayMode or editor run is required to close this.
