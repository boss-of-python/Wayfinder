# Wayfinder — Implementation Status

Last updated: 2026-10-15. Status values: **Not started / In progress / Implemented / Verified / Blocked**.
"Implemented" for C# means *real source exists but is uncompiled/unverified in this environment* (D-003, KI-001).
"Verified" requires executed evidence — see `Docs/EVIDENCE_M0.md`, `Docs/EVIDENCE_CS_HEADLESS.md` and `Docs/REQUIREMENT_MAP.md`.
**Verified (headless)** = compiled by a real C# 9 compiler at warning level 4 with 0 warnings, passed the .NET Standard 2.1
API-profile check, and executed under NUnit outside Unity (D-014). It is not a Unity build result (KI-016).

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
| **M2 Forest vertical slice** | Inventory/wallet/transactions, garage/camp, missions Q01–Q04 definitions, mapping/discovery, research tiers 1–6 data, save/atomic banking, recovery, touch UI models, weather example | **Services Verified (headless) + data Verified; UI/wiring Implemented (unverified)** | Inventory/economy/transactions/missions/research/discovery/save executed (22 tests; KI-013 fixed). Needs playable 30-minute slice + interruption tests on device. |
| **M3 World pipeline** | Deterministic hash/PRNG, noise heightfield, 24×24 chunk bake, seam checks, route graph/validator, chunk state machine | **In progress; hash/PRNG/bitset Verified (headless) + seam/hash Verified (Python mirror)** — scatter placer and streamer host **not yet implemented** (KI-017, corrected from earlier "Implemented") | Needs scatter placer + streamer, then in-editor bake + full-mainland traversal. |
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
| C# compile (Roslyn 3.9, C# 9, `-warn:4`) of Core/Simulation(pure)/World/Progression/Persistence/Tests.EditMode | `python3 Tools/Build/headless_cs.py compile` | OK — 31 files, 0 errors, 0 warnings; 2 Unity files excluded |
| C# EditMode NUnit suites (headless, NUnitLite 3.13.3) | `python3 Tools/Build/headless_cs.py test` | **49 / 49 passed** (`Docs/evidence/headless-cs/EditMode.headless.xml`) |
| .NET Standard 2.1 API-profile check | `python3 Tools/Build/headless_cs.py profile` | OK — 5 runtime assemblies |
| C# 9 parse check of 12 Unity-dependent files | `python3 Tools/Build/headless_cs.py syntax` | OK — 0 syntax errors (not type-checked) |
| C# NUnit suites **in Unity** | Unity Test Runner / `Tools/Build/build.sh test-editmode` | **Not run** (KI-001) |
| Android/iOS smoke builds | `Tools/Build/build.sh android-dev` / `ios-dev` | **Not run** (KI-006) |

## QA §20.2 mandatory case coverage

| # | Case | Coverage |
|---|---|---|
| 1 | Landmark reward granted once | C# test **passed (headless)** + ledger mirror |
| 2 | Already-collected cache yields nothing | C# transaction replay test **passed (headless)** |
| 3 | Insufficient cargo blocks pickup without consuming | C# inventory test **passed (headless)** (KI-015 fixed) |
| 4 | Banking interruption → old or new state, never mixed | C# save protocol test **passed (headless)** (KI-013 fixed) |
| 5 | Backup after corruption preserves latest valid | C# backup rotation test **passed (headless)** |
| 6 | Zero-resource rescue/playability | C# **passed (headless)** + Python deadlock scan |
| 7 | Tier costs & prerequisites canonical | C# **passed (headless)** + Python (both PASSED) |
| 8 | Tire sets never double-dip research bonus | C# resolver test **passed (headless)** |
| 9 | Capacity-module removal blocked over capacity | C# test **passed (headless)** |
| 10 | Camp establishment atomic | C# test **passed (headless)** |
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

1. **Owner**: install Unity 6.3 LTS + Android module; run `Tools/Build/pin_toolchain.py`; open project; `Tools → Wayfinder → Setup Project`; commit generated scenes/meta/lock (resolves KI-002/004/005).
2. Fix any UnityEngine API drift surfaced by the first editor open in the 12 parse-checked-only files; record here. (Domain-layer drift is already fixed — D-014 evidence.)
3. Run EditMode tests **in Unity** → attach XML to `Docs/evidence/` (expected to match the headless 49/49); run `build.sh android-dev` for the M0 smoke build (resolves KI-006 → M0 gate evidence).
4. M1 calibration batch: WheelCollider friction tuning against PhysicsTestGround obstacles (§20.3 handling targets), camera collision polish, touch pedal ergonomics on device.
5. M2 slice batch: wire services into scene flow (garage UI, HUD, pickup interactions, **water-recovery → `VehicleController.RestartAfterWaterRecovery()`**, pass `MissionService.Global.IsCompletedCode` into `ResearchService`), autosave debounce, interruption tests on device.
6. Keep `python3 Tools/Build/headless_cs.py all` green on every batch (it is the fastest real compile+test signal available without Unity); extend `WAYFINDER_HEADLESS` seams only where a file is otherwise pure.
