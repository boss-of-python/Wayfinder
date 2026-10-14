# Wayfinder — Requirement Map

Maps blueprint requirements (by section / Appendix E acceptance ID) to milestones and implementation files.
Status values: **NS** Not started · **IP** In progress · **IMP** Implemented (unverified) · **VER** Verified (evidence exists) · **VER-H** Verified headless (compiled with real C# 9 compiler, .NET Standard 2.1 profile-checked, NUnit tests executed outside Unity — D-014, `Docs/EVIDENCE_CS_HEADLESS.md`; not a Unity run, KI-016) · **BLK** Blocked.

| Req ID | Source | Requirement (condensed) | Milestone | Primary implementation | Status |
|---|---|---|---|---|---|
| PROD-01 | §1.3, App E | No ads / IAP / premium currency / trackers / required backend | M0 (rule for all) | dependency manifest `Packages/manifest.json`; `Tools/Validation/validate_content.py` dependency scan | VER (dependency scan passes; runtime audit pending device build) |
| FLOW-01 | §2.3 | Plan→drive→discover→return→bank→upgrade loop | M2 | `Progression/*`, `GarageService`, `MissionService`, `TransactionService` | VER-H (service layer; scene wiring IMP) |
| FLOW-02 | §2.5 | Explicit app/vehicle/mission state machines | M0 | `Core/GameState.cs` (`AppState`, `VehicleState`, `MissionState` enums + transition tables) | VER-H (compiles; pause-token ownership tested) — scene-level wiring IMP |
| FLOW-03 | §1.4 | Three save slots, Expedition + Relaxed modes | M2 | `Persistence/SaveService.cs`, `Core/GameState.cs` (`GameMode`) | VER-H (save service; slot UI IMP) |
| DRIVE-01 | §3.1–3.2 | Touch buttons/joystick/tilt + controller + desktop dev input | M1 | `Content/Input/WayfinderControls.inputactions`, `Platform/InputRouter.cs` | IMP |
| DRIVE-02 | §3.3, 4.4 | Speed-sensitive steering, Ackermann blend, surface-specific handling | M1 | `Simulation/HazardModels.cs` (`SteeringModel`), `Simulation/SurfaceTable.cs`, `Simulation/VehicleStatsResolver.cs` | VER-H (steering/surface math; `VehicleController` IMP) |
| DRIVE-03 | §3.4 | Chase camera: collision, recenter, orbit bounds, FOV | M1 | `Simulation/ChaseCameraBehaviour.cs` | IMP |
| DRIVE-04 | §3.5 | Accessibility: non-tilt path, scale, hold/toggle, reduced motion, left-handed | M2 | `Presentation/PresentationModels.cs` (`AccessibilitySettings`), `Platform/InputRouter.cs` | IMP (parse-checked only) |
| VEH-01 | §4.1–4.3 | Four WheelColliders, powertrain, gears, torque curve, brakes | M1 | `Simulation/VehicleController.cs`, `Simulation/Powertrain.cs` | VER-H (Powertrain torque/AWD split) + IMP (WheelCollider controller, parse-checked only) |
| VEH-02 | §4.2 | Scout S1 exact spec (masses, gears, torque curve, suspension) | M1 | `Content/vehicles.json`, `Simulation/VehicleSpec.cs` | VER (data audit) + VER-H (code) |
| VEH-03 | §4.5 | Cargo/fuel mass effects, anti-roll cap, COM updates | M1 | `Simulation/VehicleStatsResolver.cs`, `Simulation/HazardModels.cs` (`AntiRoll`) | VER-H (stat resolver, no double-dip) + IMP (runtime application) |
| VEH-04 | §4.6 | Physics safety, PhysicsTestGround obstacles | M1 | `Editor/SceneBuilder.cs` (ramps 10/20/30/35, side slopes, ledges, mud, water, jump) | IMP (scene generated at setup) |
| HAZ-01 | §5.1–5.4 | Fuel model, mud sink, water crossing, slope/rollover rules | M1 | `Simulation/FuelModel.cs`, `Simulation/HazardModels.cs` (`MudSinkModel`, `WaterCrossingModel`, `RolloverMonitor`) | VER-H (fuel/mud/water models executed; KI-014 fixed) + VER (formula checks) |
| HAZ-02 | §5.5 | Damage model (engine/suspension/tires/body), impact formula, debounce | M1 | `Simulation/FuelModel.cs` (`DamageModel`) | VER-H (impact formula, debounce, water once) + VER (formula checks) |
| REC-01 | §5.6 | Reset/winch/supplies/camp recall/full rescue; no softlock | M1 | `Simulation/RecoveryRules.cs` (`RecoveryRules`, `WinchMath`) | VER-H (reset/rescue rules executed) + VER (Python mirror) |
| WORLD-01 | §6.1–6.4 | 6,144 m mainland, five irregular biomes, anchors, density targets | M3 | `World/WorldDefinition.cs` (`WorldConstants`), `Content/biomes.json` | IMP (constants) / NS (authored biome dressing) |
| WORLD-02 | §6.3 | Connectivity, progression order, capability gating | M3 | `World/RouteGraph.cs` (`RouteGraph`, `RouteValidator`, `CapabilityBands`) | VER-H (compiles, profile-checked; validator logic mirrored in Python) |
| GEN-01 | §7.1–7.3 | Hybrid authored/deterministic/baked pipeline, seed 481516, stable hash/PRNG | M3 | `Core/StableHash.cs`, `Core/StableRandom.cs`, `World/WorldDefinition.cs` (`NoiseField`), `Editor/WorldBaker.cs` | VER-H (hash/PRNG/chunk-seed executed; World compiles) + VER (Python mirror); baker IMP |
| GEN-02 | §7.4 | 256 m chunks, 129×129 tiles, seams < 1 mm quantized | M3 | `World/WorldDefinition.cs` (`HeightField`, `SeamCheck`), `Editor/WorldBaker.cs` | VER-H (compiles) + VER (seam check mirrored in Python); baker IMP |
| GEN-03 | §7.5–7.6 | Road/route carve constraints, scatter placement rules | M3 | `World/RouteGraph.cs` (route/capability-band constraints), `World/WorldDefinition.cs` (river/road carve hooks only) | IP — **no scatter placer exists yet**; road carve is a hook, not an implementation (corrected 2026-10-14; earlier entry overstated) |
| GEN-04 | §7.7 | Weather/time generation (1 Hz sim, interpolated visuals) | M2/3 | `World/RouteGraph.cs` (`WeatherSystem`) | VER-H (compiles) — visual interpolation IMP |
| MAP-01 | §8.1 | 96×96 × 64 m map grid, bitsets, reveal rules, countable mask | M2 | `Progression/DiscoveryService.cs`, `Core/BitGrid.cs` | VER-H (bitset reveal/base64, thresholds executed) + VER (Python mirror) |
| MAP-02 | §8.2 | Landmark discovery rules + reward once | M2 | `Progression/DiscoveryService.cs` | VER-H (reward once, QA18) |
| MAP-03 | §8.3–8.4 | Relic ledger, sensor ranges, mapping rewards, 60% finale gate | M2/M4 | `Progression/DiscoveryService.cs`, `Progression/MissionService.cs` | IMP |
| QUEST-01 | §9.2 | 20 main missions Q01–Q20, stable IDs, gates, rewards | M2 (Q01–Q04) / M4 (all) | `Content/missions.json`, `Progression/MissionService.cs` | VER (data) + VER-H (lifecycle/single reward, QA1) |
| QUEST-02 | §9.3 | 30 authored contracts + 5 repeatable templates | M4 | `Content/contracts.json`, `Progression/DiscoveryService.cs` (`ContractService`) | VER (data) + VER-H (compiles; reward formula checked in Python) |
| QUEST-03 | §17.1 | Typed mission condition vocabulary only | M4 | `Progression/MissionService.cs` (`MissionConditionType`) | IMP |
| ITEM-01 | §10.3–10.4 | Item catalog, stacks, mass, protected ledger, no silent deletion | M2 | `Content/items.json`, `Progression/InventoryService.cs` | VER (data) + VER-H (slots/mass/protected ledger; KI-015 fixed) |
| ITEM-02 | §12.3 | Deterministic recipes, atomic craft, no arbitrage | M2/M4 | `Content/recipes.json`, `Progression/EconomyService.cs` (`CraftingService`) | VER (arbitrage check) + VER-H (atomic craft, QA17) |
| UPG-01 | §11.2 | 20 tiers, exact incremental costs (Σ 32,200), story gates, stat composition | M2 (1–6) / M4 (all) | `Content/upgrades.json`, `Progression/ResearchService.cs`, `Simulation/VehicleStatsResolver.cs` | VER (cost table + no-double-bonus checks) + VER-H (sequential tiers, story gates, QA7/8/9) |
| ECON-01 | §11.3–11.5 | Payouts, sell 40%, no softlock, no arbitrage, headless balance sim | M2/M6 | `Progression/EconomyService.cs`, `Tools/Balance/economy_sim.py` | VER (sim runs; softlock scan included) |
| CAMP-01 | §12.1–12.2 | Garage ops, camp establishment atomicity, hub-only logistics | M2 | `Progression/GarageService.cs` (`GarageService`, `CampService`) | VER-H (camp establishment atomic, QA10) |
| CAMP-02 | §12.4 | Extraction banking transaction, idempotent, interruption-safe | M2 | `Progression/EconomyService.cs` (`TransactionService`), `Persistence/SaveService.cs` | VER-H (banking once, replayed id grants nothing, interrupted write never mixes states) + VER (mirror) |
| UI-01 | §13 | Screen hierarchy, wireframes, error states, safe area | M2 | `Presentation/*` | IMP (controllers/view models; layout polish M5) |
| ART-01 | §14 | Art/audio budgets, provenance | M5 | `Docs/ASSET_MANIFEST.csv` | NS (placeholders tracked) |
| ENG-01 | §15 | Unity 6.3 LTS, URP, packages minimal, structure | M0 | `Packages/manifest.json`, tree layout | IMP — BLK on exact pin (KI-002) |
| ARCH-01 | §16.1–16.2 | Service ownership table, update frequencies, no God-object | M0/M1 | asmdefs + service classes | IMP |
| ARCH-02 | §16.3–16.4 | Commands/events, stable reward keys, interaction contract | M2 | `Core/GameCommands.cs`, `Core/GameEvents.cs`, `Progression/GarageService.cs` (`InteractionService`) | VER-H (compiles; ledger/reward-key tests) — bus wiring IMP |
| ARCH-03 | §16.5–16.6 | Threading/handle rules, build-time validators | M3 | `Editor/ContentImporter.cs` (`ContentValidators`), `World/WorldDefinition.cs` (`ChunkStateMachine`) | IMP (validators, parse-checked only) / VER-H (chunk state machine compiles) — **no streamer component yet** (corrected 2026-10-14) |
| SAVE-01 | §17.3–17.5 | Envelope, checksum, atomic write, backups, migration, no double-pay | M2 | `Persistence/*` | VER-H (round-trip, checksum, corruption, backup recovery, schema-too-new read-only, migration — after KI-013 fix) + VER (mirror) |
| PERF-01 | §18 | Quality tiers, budgets, streaming state machine | M3/M6 | `Content/quality_tiers.json`, `Platform/PlatformServices.cs` (`QualityTierService`), `World/WorldDefinition.cs` (`ChunkStateMachine`) | IMP (config+state machine) / BLK (device measurement) |
| MOBILE-01 | §19 | Lifecycle pause/resume/kill safety, no throttle latch | M2 | `Platform/PlatformServices.cs` (`LifecycleHandler`), `Platform/InputRouter.cs` | IMP (parse-checked only) / BLK (device test) |
| PRIV-01 | §19.3–19.4 | No permissions/telemetry; local diagnostics export | M0 | `Platform/PlatformServices.cs` (`LocalDiagnostics`); manifest has no network/identity packages | VER (dependency scan) |
| QA-01 | §20.2 | 20 mandatory automated cases | M1–M4 | `Tests/EditMode/*` (C#) + `Tools/Validation/*` (mirror subset executed here) | IP — 16/20 have executed evidence (49/49 EditMode tests headless + Python); cases 12, 14, 15, 20 need Unity runtime/device |
| BUILD-01 | §23.2, App E | Reproducible Android/iOS builds | M0/M7 | `Editor/BuildCommands.cs`, `Tools/Build/*` | BLK (KI-001/KI-006) |
| LEGAL-01 | §23.2 | Licensed final assets, provenance manifest | M5 | `Docs/ASSET_MANIFEST.csv` | IP (placeholders tracked; nothing third-party imported) |
| FINISH-01 | §23.1 | Finale (Q20), credits, postgame | M4/M5 | `Content/missions.json` Q20, `Presentation/PresentationModels.cs` (`CreditsController`) | IMP (definition+flow) / NS (final presentation art) |

Milestone gate summary: **M0** implemented, domain layer headless-verified (D-014), gate still **blocked on Unity/device evidence** (KI-001/002/006). **M1–M4** substantially implemented at source and content level; the non-Unity service/math layer is compiled and unit-tested (VER-H), the engine-facing layer is unverified. **M5–M7** not started (art/audio/optimization/device/release), tracked above.
