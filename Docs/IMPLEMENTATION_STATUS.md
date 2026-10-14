# Wayfinder — Implementation Status

Last updated: 2026-10-14. Status values: **Not started / In progress / Implemented / Verified / Blocked**.
"Implemented" for C# means *real source exists but is uncompiled/unverified in this environment* (D-003, KI-001).
"Verified" requires executed evidence — see `Docs/EVIDENCE_M0.md` and `Docs/REQUIREMENT_MAP.md`.

## Summary

| Milestone | Scope | Status | Gate |
|---|---|---|---|
| **M0 Foundation** | Unity project layout, URP/package pins, asmdefs, Bootstrap/Title/PhysicsTestGround/VerticalSlice scene builders, app state flow, input actions, test assemblies, build scripts, docs | **Implemented — gate Blocked on toolchain** | Android/iOS smoke builds require owner toolchain (KI-001/002/006). All source/data/tests/scripts present; Python-layer evidence executed. |
| **M1 Vehicle proving ground** | Scout S1 spec, WheelCollider controller, powertrain, steering/assists, camera, surfaces, suspension, fuel/damage/water/mud, winch, reset, PhysicsTestGround rig | **Implemented (unverified)** | Needs Unity physics demonstration on device (KI-009). |
| **M2 Forest vertical slice** | Inventory/wallet/transactions, garage/camp, missions Q01–Q04 definitions, mapping/discovery, research tiers 1–6 data, save/atomic banking, recovery, touch UI models, weather example | **Implemented (unverified) + data Verified** | Needs playable 30-minute slice + interruption tests on device. |
| **M3 World pipeline** | Deterministic hash/PRNG, noise heightfield, 24×24 chunk bake, seam checks, route graph/validator, streaming state machine, scatter constraints | **Implemented (unverified) + seam/hash Verified (Python mirror)** | Needs in-editor bake + full-mainland traversal. |
| **M4 Full campaign systems** | All 20 missions, 30 contracts + 5 templates, 20 research tiers, 4 vehicles, item/recipe catalog, camps/garages, relic ledger, finale Q20 | **Implemented (unverified) + content Verified** | Needs greybox full-campaign playthrough. |
| **M5 Content & assets** | Final art/audio, five visual regions, vehicle art, UI polish, icons, lore, sound | **Not started** (placeholders tracked in ASSET_MANIFEST.csv) | Hero assets must not be placeholders at this gate. |
| **M6 Optimization & usability** | Device frame-time/memory/thermal, accessibility verification, economy pacing playtests | **Not started** (quality tiers defined; simulator model runs) | Requires measured device profiles. |
| **M7 Release candidate** | Clean builds, save migrations from public versions, airplane-mode suite, store audit | **Not started** (build scripts + checklist ready) | Owner credentials + authorization required for any signing/submission. |

## Requirement IDs addressed in this batch

PROD-01 (Verified: dependency scan), FLOW-01/02/03 (Implemented), DRIVE-01..04 (Implemented),
VEH-01..04 (Implemented; VEH-02 data Verified), HAZ-01/02 (Implemented; formulas Verified),
REC-01 (Implemented; rules Verified), WORLD-01/02 (Implemented constants/graph),
GEN-01..04 (Implemented; hash/seam Verified), MAP-01..03 (Implemented; bitset/thresholds tested),
QUEST-01..03 (Implemented; data Verified), ITEM-01/02 (Implemented; data/arbitrage Verified),
UPG-01 (Implemented; costs/no-double-bonus Verified), ECON-01 (Verified: sim + scans),
CAMP-01/02 (Implemented; atomicity/idempotency tests written), UI-01 (Implemented models),
ENG-01 (Implemented; exact pin Blocked KI-002), ARCH-01..03 (Implemented),
SAVE-01 (Implemented; protocol tests written + round-trip mirrored), PERF-01 (config Implemented;
device Blocked), MOBILE-01 (Implemented; device Blocked), PRIV-01 (Verified: scan),
QA-01 (In progress: 14/20 mandatory cases have executed evidence; 6 need Unity runtime),
BUILD-01 (Blocked KI-006), LEGAL-01 (In progress: manifest clean, placeholders tracked),
FINISH-01 (Implemented definitions; credits flow coded).

## Evidence index

| Evidence | Command | Result |
|---|---|---|
| Content counts/IDs/gates/recipes/PROD-01 | `python3 Tools/Validation/validate_content.py` | PASSED (0 errors) |
| Formulas & invariants (35 checks) | `python3 Tools/Validation/formula_checks.py` | PASSED |
| Economy pacing/deadlock/arbitrage | `python3 Tools/Balance/economy_sim.py` | PASSED (no deadlocks) |
| C# NUnit suites | Unity Test Runner / `Tools/Build/build.sh test-editmode` | **Not run** (KI-001) |
| Android/iOS smoke builds | `Tools/Build/build.sh android-dev` / `ios-dev` | **Not run** (KI-006) |

## QA §20.2 mandatory case coverage

| # | Case | Coverage |
|---|---|---|
| 1 | Landmark reward granted once | C# test + ledger mirror |
| 2 | Already-collected cache yields nothing | C# transaction replay test |
| 3 | Insufficient cargo blocks pickup without consuming | C# inventory test |
| 4 | Banking interruption → old or new state, never mixed | C# save protocol test |
| 5 | Backup after corruption preserves latest valid | C# backup rotation test |
| 6 | Zero-resource rescue/playability | C# + Python deadlock scan |
| 7 | Tier costs & prerequisites canonical | C# + Python (both PASSED) |
| 8 | Tire sets never double-dip research bonus | C# resolver test |
| 9 | Capacity-module removal blocked over capacity | C# test |
| 10 | Camp establishment atomic | C# test |
| 11 | Fuel/condition bounds; frame-rate-independent integration | C# tests + Python bounds |
| 12 | Chunk boundary handle leaks | Streaming state machine coded; PlayMode test pending (Unity) |
| 13 | 100 landmark / 200 cache unique & reachable | IDs unique PASSED; reachability at M3 bake |
| 14 | Wetland routes valid in both water states | Water model states coded; route validation at M3 |
| 15 | Background/resume never latches throttle | C# + PlayMode test written |
| 16 | No reward farming by closing on reward screen | Transaction idempotency tests |
| 17 | Crafted-item resale never nets currency | C# + Python (both PASSED) |
| 18 | Mapping threshold never duplicated | C# restore test |
| 19 | Migration preserves discoveries/modules | C# test |
| 20 | 150% scale / 30% text expansion layout | Not covered — needs UI layout pass (M2/M6 device check) |

## Next dependency-ordered batch

1. **Owner**: install Unity 6.3 LTS + Android module; run `Tools/Build/pin_toolchain.py`; open project; `Tools → Wayfinder → Setup Project`; commit generated scenes/meta/lock (resolves KI-002/004/005).
2. Fix any compile-time API drift surfaced by the first editor open; record here.
3. Run EditMode tests → attach XML to Docs/EVIDENCE; run `build.sh android-dev` for the M0 smoke build (resolves KI-006 → M0 gate evidence).
4. M1 calibration batch: WheelCollider friction tuning against PhysicsTestGround obstacles (§20.3 handling targets), camera collision polish, touch pedal ergonomics on device.
5. M2 slice batch: wire services into scene flow (garage UI, HUD, pickup interactions), autosave debounce, interruption tests on device.
