---
doc_id: wayfinder-ai-bug-report
doc_version: 2
generated: 2026-10-16
reviewed_commit: 0edcb1198b169b036697cb2ccb90390a8926504b
reviewed_branch: arena/be6d9b5c-wayfinder
review_type: read-only static review + executed headless verification (no product source file was modified)
scope:
  - Assets/Wayfinder/Scripts/** + Tests/**  (93 C# files, 19,669 lines, 12 asmdefs, 18 test files)
  - Assets/Wayfinder/Content/*.json (15 catalogs + inputactions)
  - Tools/**  (headless compile harness, validators, economy sim, build tools)
defect_count: 25
severity_counts: {S1: 5, S2: 13, S3: 7}
confidence_counts: {verified-executed: 14, static-pure: 5, static-unity: 6}
companion_evidence: Docs/evidence/ai-review-2026-10-16/   (BugProbe.cs, run_bug_probe.py, bug-probe.log)
related_repo_docs: [Docs/KNOWN_ISSUES.md (KI-001…KI-041), Docs/IMPLEMENTATION_STATUS.md, Docs/DECISIONS.md]
---

# Wayfinder — AI bug & defect report

## 0. How an AI should consume this file

1. Read §1 for every defect as machine-readable rows.
2. Read §2 to know **what is already verified** — do not re-run those checks to find these bugs;
   they pass. §2 also states what the existing green checks do *not* prove.
3. Apply §8 (fix plan) in order. Each defect carries `fix` and `verify_with` fields.
4. Record schema used by every record in §3–§5:

```
id:           DEF-nnn    stable within this document (the repo's own tracker uses KI-nnn; no overlap)
severity:     S1 | S2 | S3
              S1 = blocks a milestone the repo claims is complete, or breaks a core pillar
              S2 = feature wrong/inert, content unreachable, or evidence that cannot fail
              S3 = robustness / data hygiene / performance
category:     gameplay-logic | math | data-integrity | persistence | content | unity-integration |
              tooling | docs-drift | performance
confidence:   verified-executed  = reproduced by the in-repo probe (named test)
              static-pure        = read from code that the headless harness compiles and tests
              static-unity       = read from Unity-only code that is parsed but never compiled/run here
                                   (KI-034) — these may ALSO hide compile errors; verify in the editor
status:       open | open-latent | mitigated-by-checksum
location:     path:symbol:line   (line numbers verified against commit 0edcb11 on 2026-10-16)
symptom / root_cause / evidence / impact / fix / verify_with
```

5. `evidence` is a probe test name (reproduce with
   `python3 Docs/evidence/ai-review-2026-10-16/run_bug_probe.py`) or a grep/read command.
6. The probe deliberately asserts the **buggy** behaviour, so it fails as soon as a defect is fixed.
   Inverting the assertion is part of the fix.

---

## 1. Machine-readable defect index

```yaml
defects:
  - {id: DEF-001, sev: S1, cat: gameplay-logic, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs, line: 37, symbol: 'EffectiveVehicleStats.SafeWadingDepthM never assigned in Resolve (:134-170); Stat enum has no wading member', probe: DEF01_WadingDepthIsAlwaysZero}
  - {id: DEF-002, sev: S1, cat: math, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs, line: 102, symbol: 'Compose: last Set wins; ResearchService.ApplyTo (:156) iterates a HashSet', probe: DEF02_ComposeIsSetOrderDependent}
  - {id: DEF-003, sev: S1, cat: gameplay-logic, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Progression/GarageService.cs, line: 37, symbol: UnlockGarage has no call sites; Q05-Q20 target ids absent from all catalogs, probe: DEF06_NonHomeGaragesAreNeverUnlockedAndFullCampaignCannotProgress}
  - {id: DEF-004, sev: S1, cat: gameplay-logic, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Progression/ResearchService.cs, line: 59, symbol: 'tiers 7/8/12/13/18 (:59,:60,:69,:70,:82) declare no effects; LowRange/DiffLock flags never assigned', probe: DEF01_LowRangeAndDiffLockFlagsAreNeverSet}
  - {id: DEF-005, sev: S1, cat: gameplay-logic, conf: static-unity, status: fixed, file: Assets/Wayfinder/Scripts/Simulation/VehicleController.cs, line: 250, symbol: 'ResolveStats hardcodes SurfaceType.PackedDirt, wet false, true,true,true; per-wheel surface only scales brakes', probe: null}
  - {id: DEF-006, sev: S2, cat: math, conf: static-unity, status: fixed, file: Assets/Wayfinder/Scripts/Simulation/VehicleController.cs, line: 341, symbol: CurrentTerrainSlopeRadians = asin(1-up.y) instead of acos(up.y); pitch fed where roll context is expected, probe: null}
  - {id: DEF-007, sev: S2, cat: gameplay-logic, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs, line: 247, symbol: 'ContractService.Accept/:247 and Complete/:255 have no call sites; Complete bypasses TransactionService', probe: null}
  - {id: DEF-008, sev: S2, cat: content, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs, line: 43, symbol: 'CountableMask initialised to all 9216 cells and never persisted (CampaignSnapshot.cs:83)', probe: DEF06 countableMaskBase64 empty}
  - {id: DEF-009, sev: S2, cat: data-integrity, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Progression/InventoryService.cs, line: 246, symbol: InventoryService.Add -> ItemCatalog.Get throws KeyNotFoundException for unknown ids on the pickup path, probe: DEF04_UnknownItemIdInPickupContentThrowsOutOfTransactionExecute}
  - {id: DEF-010, sev: S2, cat: data-integrity, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Progression/EconomyService.cs, line: 238, symbol: ExecuteCollect double-collect guard is conditional on _deps.Discovery != null; TryMarkCacheCollected result ignored, probe: DEF04_DoubleCollectGuardDependsOnDiscoveryBeingWired}
  - {id: DEF-011, sev: S2, cat: tooling, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/World/WorldDefinition.cs, line: 197, symbol: SeamCheck.BordersMatch compares one continuous field 2 mm apart (guard uses '- 0'); Python mirror compares a single coordinate, probe: DEF03_SeamCheckComparesTheSameContinuousField}
  - {id: DEF-012, sev: S2, cat: tooling, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/World/WorldDefinition.cs, line: 263, symbol: 'ChunkStateMachine sets FailureDetail only on the rejected path, so a real Failed transition stores string.Empty', probe: DEF09_ChunkFailedTransitionLosesItsFailureDetail}
  - {id: DEF-013, sev: S2, cat: unity-integration, conf: static-unity, status: fixed, file: Assets/Wayfinder/Scripts/Simulation/ChaseCameraBehaviour.cs, line: 123, symbol: Horizon-stabilisation Slerp blends to an identical rotation with t=1f; _smoothedLookPoint starts at zero, probe: null}
  - {id: DEF-014, sev: S2, cat: unity-integration, conf: static-unity, status: fixed, file: Assets/Wayfinder/Scripts/Platform/InputRouter.cs, line: 133, symbol: 'InteractTapped includes IsPressed(), so it is true on every frame of a hold; flag defaults true (:27)', probe: null}
  - {id: DEF-015, sev: S2, cat: unity-integration, conf: static-unity, status: fixed, file: Assets/Wayfinder/Scripts/Presentation/VerticalSliceDriver.cs, line: 148, symbol: 'BuildFieldInput hardcodes Grounded/RollDegrees/InDeepMud/ClearanceMeters/TapToInteract (:148-154); UpdateState never sets Airborne/Winching/Recovering', probe: null}
  - {id: DEF-016, sev: S2, cat: tooling, conf: static-unity, status: fixed, file: Assets/Wayfinder/Scripts/Editor/ContentImporter.cs, line: 179, symbol: 'ValidateMissionTable reads ''rewardSalvage'' (schema: reward.salvage) so the check can never fire; validate_content.py:4 cites a non-existent ContentValidators.cs', probe: null}
  - {id: DEF-017, sev: S2, cat: gameplay-logic, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Progression/EconomyService.cs, line: 380, symbol: CargoSettlement.Settle banks the whole global escrow at any hub; deposit notice built from pre-transaction state, probe: DEF10_CargoSettlementBanksEscrowFromAnySourceOnEveryBank}
  - {id: DEF-018, sev: S3, cat: gameplay-logic, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Progression/EconomyService.cs, line: 189, symbol: 'CraftItem/TransferItems/SellItems/UseItem/RequestRecovery never set committed -> Commit(id, null) at :225', probe: DEF05_OnlySomeCommandsEmitCommittedEvents}
  - {id: DEF-019, sev: S3, cat: data-integrity, conf: verified-executed, status: fixed, file: Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs, line: 132, symbol: LandmarkDiscovered carries default(TransactionId) on a permanent 120-Salvage reward, probe: DEF08_TransactionIdOfGrantedRewardsIsDefaultInDiscoveryService}
  - {id: DEF-020, sev: S3, cat: persistence, conf: verified-executed, status: fixed-latent, file: Assets/Wayfinder/Scripts/Core/BitGrid.cs, line: 114, symbol: 'FromBase64 accepts short payloads and throws raw FormatException; SaveService catches only IO/UnauthorizedAccess (:233,:237)', probe: DEF07_BitGridFromBase64DoesNotValidatePayload}
  - {id: DEF-021, sev: S3, cat: data-integrity, conf: static-pure, status: fixed, file: Assets/Wayfinder/Scripts/Progression/InventoryService.cs, line: 276, symbol: 'Remove ignores MissionInstanceId; protected ledger is a List (:143,:249) and RemoveProtected pops one entry (:374)', probe: null}
  - {id: DEF-022, sev: S3, cat: persistence, conf: static-pure, status: fixed, file: Assets/Wayfinder/Scripts/Persistence/SaveService.cs, line: 181, symbol: Save catches IO/UnauthorizedAccess only; temp file leaks on FailedWrite; LoadBestValid never checks envelope.slotId, probe: null}
  - {id: DEF-023, sev: S3, cat: performance, conf: static-pure, status: fixed, file: Assets/Wayfinder/Scripts/World/WorldDefinition.cs, line: 153, symbol: 'HeightField._samples = float[3073*3073] (~37.8 MB) allocated up front (:147,:154)', probe: null}
  - {id: DEF-024, sev: S3, cat: gameplay-logic, conf: static-pure, status: fixed, file: Assets/Wayfinder/Scripts/World/RouteGraph.cs, line: 225, symbol: 'WeatherSystem.Step resets _accum instead of decrementing it (:223-225), so 1 Hz ticks drift by the frame delta', probe: null}
  - {id: DEF-025, sev: S2, cat: tooling, conf: static-pure, status: fixed, file: Tools/Validation/formula_checks.py, line: 145, symbol: '5 tautological checks (:140, :145-148), mirror never reads the C# source, dead COSTS line (:128), seam check compares one coordinate with itself', probe: null}
```

---

## 2. Verification baseline (already executed here — do not redo)

Run in this sandbox on commit `0edcb11`; **all green**:

| Command | Result |
|---|---|
| `python3 Tools/Build/headless_cs.py all` | `SUMMARY compile=OK tests=OK netstandard2.1-profile=OK syntax=OK`, exit 0 |
| EditMode NUnit suites (Roslyn 3.9/C#9 + NUnitLite 3.13.3) | **164 / 164 passed**, 0 failed, 1,366,213 asserts (`Logs/headless/EditMode.headless.xml`) |
| `python3 -m unittest discover -s Tools/Build -p 'test_*.py'` | **89 tests, OK** |
| `python3 Tools/Validation/validate_content.py` | `0 error(s), 0 warning(s)` → `CONTENT VALIDATION PASSED` |
| `python3 Tools/Validation/formula_checks.py` | `0 failure(s)` → `FORMULA CHECKS PASSED` (see caveat + DEF-025) |
| `python3 Docs/evidence/ai-review-2026-10-16/run_bug_probe.py` | **15 / 15 characterization tests pass** (47 asserts, 6.88 s, exit 0) |

What this baseline does **and does not** prove:

* There is **no compile error and no failing test** in the repo's own build paths. Every defect below is
  semantic, data-flow or wiring level — invisible to the compiler and to the existing 164 tests.
* `formula_checks.py` is a **standalone Python mirror** (`import math, sys` only); it never reads the C#
  source. Its pass tells you the mirror is self-consistent, not that the C# implements the formula —
  that is exactly how DEF-001 and DEF-004 survived (see DEF-025).
* `validate_content.py` validates *JSON data* (counts, ids, costs, reward tables) and is genuinely
  useful, but it cannot see code defects. It also claims to mirror
  `Assets/Wayfinder/Scripts/Editor/ContentValidators.cs`, **a file that does not exist**
  (the C# validators live inside `Editor/ContentImporter.cs:80`) — DEF-016.
* Platform, Presentation, Editor and PlayMode assemblies are **syntax-checked only** (KI-034). Every
  `static-unity` finding below is also unverified at compile time; run the editor test runner before
  trusting those.

---

## 3. S1 — defects that block a milestone the repo claims is complete

### DEF-001 — Wading depth is never composed, so any water stalls the engine

```yaml
id: DEF-001
severity: S1
category: gameplay-logic
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — SafeWadingDepthM is composed from WaterCrossingModel and the raised-intake tier (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §5.3 (safe wading 0.55 m, raised intake 0.85 m), §5.5, §11.2 tier 12 "raisedIntake"
location:
  - Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs:37    (EffectiveVehicleStats.SafeWadingDepthM)
  - Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs:134-170 (Resolve — field never assigned)
  - Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs:60-66 (Stat enum — no wading member)
  - Assets/Wayfinder/Scripts/Simulation/VehicleController.cs:158      (float intakeHeight = stats.SafeWadingDepthM;)
  - Assets/Wayfinder/Scripts/Simulation/HazardModels.cs:96-97         (BaselineSafeWadingM 0.55 / UpgradedSafeWadingM 0.85)
symptom: >
  VehicleController passes stats.SafeWadingDepthM to WaterCrossingModel.Step as the intake height.
  Resolve() builds EffectiveVehicleStats with an object initialiser that never sets that field, and the
  Stat enum has no wading member, so the value is always 0.0 m. Any water deeper than 0 m is "above the
  intake": after the 2 s stall timer the engine dies and condition drops 10, in puddles the blueprint
  calls safe.
root_cause: >
  The field exists, the constants exist, the model exists — the composition step between them was never
  written (compare WinchReachM, which has both a Stat member and a Compose() call at :161).
evidence:
  - probe: DEF01_WadingDepthIsAlwaysZero  → 0 f with all 20 tiers researched
  - probe: DEF01_VehicleStallsIn25cmOfWaterBecauseIntakeHeightIsZero
  - tools: Tools/Validation/formula_checks.py:145-148 asserts `0.55 == 0.55` (tautology, DEF-025) — the
    "green" water check is fake, which is why nothing caught this.
impact: >
  Water crossings — a core §5.3 hazard and the defining obstacle of the wetlands biome — are impassable.
  The 1,750-Salvage raised-intake research cannot function because tier 12 is authored with no effects.
fix: >
  Add `SafeWadingDepthM` to Stat; in Resolve() set
  `SafeWadingDepthM = Compose(Stat.SafeWadingDepthM, HazardModels...BaselineSafeWadingM)` (0.55);
  author tier 12 with `(Stat.SafeWadingDepthM, Set, UpgradedSafeWadingM)` (0.85); replace the fake
  formula_checks water block with a check that reads the C# constants (or drive it from the probe).
verify_with: invert DEF01_* probe assertions (expect 0.55/0.85), then move the tests into Tests/EditMode.
```

### DEF-002 — Stat composition is order-dependent; later upgrades can make the vehicle worse

```yaml
id: DEF-002
severity: S1
category: math
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — StatModifier carries a rank; ResearchService.ApplyTo walks Tiers in authored order (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §11.2 ("stats compose through one resolver; the garage shows exactly these values")
location:
  - Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs:102-120 (Compose, `case Operation.Set` :114)
  - Assets/Wayfinder/Scripts/Progression/ResearchService.cs:31         (readonly HashSet<string> _researched)
  - Assets/Wayfinder/Scripts/Progression/ResearchService.cs:156-162    (ApplyTo iterates the set)
  - Assets/Wayfinder/Scripts/Progression/ResearchService.cs:165-171    (Restore inserts ids in save order)
symptom: >
  Compose() applies every Operation.Set in list order and the last Set wins. ApplyTo walks a HashSet, so
  "last" is enumeration order, not tier order. Feed the tiers in reverse and a fully-researched Scout
  resolves CargoSlots = 14 (tier-4 value) instead of 20 (tier 17) and a 30 L tank instead of 40 L.
root_cause: >
  Set means overwrite, and nothing ranks modifiers by tier. .NET does not contractually guarantee HashSet
  enumeration order (insertion order happens to hold only while the set is never removed from/restored).
evidence:
  - probe: DEF02_ComposeIsSetOrderDependent → (40 then 30) = 40 vs (30 then 40) = 30
  - probe: DEF02_ResearchApplyOrderIsHashSetOrderNotTierOrder → normal 20 slots, reversed order 14 slots
impact: >
  Silent progression corruption and non-deterministic resolved stats — the garage display can disagree
  with the physics after a runtime/save-order change, which breaks the §11.2 guarantee the tests were
  supposed to pin.
fix: >
  Make Set deterministic: either rank modifiers (carry tier in StatModifier, highest rank wins) or have
  ApplyTo iterate `Tiers` in tier order instead of `_researched`, and document/test the ordering rule.
verify_with: invert DEF02_* assertions; add a regression test that reversed insertion order still yields tier-20 values.
```

### DEF-003 — The campaign cannot progress past Q04: garages never unlock and Q05–Q20 sites do not exist

```yaml
id: DEF-003
severity: S1
category: gameplay-logic
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — garages unlock on arrival via the UnlockGarage transaction; the 23 missing sites are authored in Content/sites.json (relays on §6.1 anchors, the rest procedural and marked) (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §2.3, §9.2 (20 main missions), §12.1 (garage unlock), milestone M4
location:
  - Assets/Wayfinder/Scripts/Progression/GarageService.cs:37   (UnlockGarage — definition only)
  - Assets/Wayfinder/Scripts/Progression/MissionService.cs:88-90 (Q05 "Red Dust" → VisitVolume garage.canyon)
  - Assets/Wayfinder/Content/missions.json                     (Q05–Q20 objective targets)
  - Assets/Wayfinder/Content/vertical_slice.json                (only the forest slice places sites)
symptom: >
  Two dead ends compound. (1) Nothing in Assets/ calls GarageService.UnlockGarage, so only
  "garage.home" is ever unlocked; extraction requires an unlocked garage, and CampaignSession rejects
  every other target with FailureReason.InvalidTarget. Q05's objective is VisitVolume(garage.canyon),
  which only extraction can report, so Q05 can never complete — and Q06…Q20 each require the previous
  mission. (2) The 46 objective target ids across Q05–Q20 (relay.*, junction.city.*, pole.wetlands.*,
  station.highlands.*, site.canyon.*, site.highlands.observatory, marker.canyon.*, route.wetlands.*,
  document.pump_diagram, quest.*) exist only inside missions.json and MissionService.cs: no catalog
  defines them and CampaignSession resolves sites only through the forest slice.
root_cause: >
  M4 wiring (garage-unlock triggers, non-forest site authoring) was never implemented, yet the mission
  definitions were authored as if it had been. No validator checks objective reachability (DEF-016).
evidence:
  - probe: DEF06_NonHomeGaragesAreNeverUnlockedAndFullCampaignCannotProgress
      → all non-home garages IsGarageUnlocked == false; EndExpedition("garage.canyon") = InvalidTarget;
        Landmarks["junction.city.01"] missing
  - grep: `grep -rn "UnlockGarage" Assets/` → only the definition
  - grep: `grep -rln "junction.city.01" .` → missions.json + MissionService.cs only
impact: >
  Docs/IMPLEMENTATION_STATUS.md reports M4 "Implemented"; the playable campaign is in fact the four-mission
  forest slice (which does work headlessly). Every claim of "20 missions" is definitions-only.
fix: >
  Implement garage unlocking (story infrastructure in §6.3/§12.1 → TransactionService; the save field
  `unlockedGarageIds` already exists) and author the remaining sites, or explicitly scope Q05–Q20 out of
  content and downgrade the status doc. Add the reachability rule to validate_content.py (authoritative)
  and to the C# validator.
verify_with: >
  Walk Q01..Q20 and assert every objective target resolves to a landmark/cache/site/relay/route.
```

### DEF-004 — Five research tiers cost 9,100 Salvage and do nothing

```yaml
id: DEF-004
severity: S1
category: gameplay-logic
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — tiers 7/8/12/13/18 now carry effects in both C# and upgrades.json; LowRange/DiffLock/MudTires/SnowTires composed in Resolve() (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §11.2 tier table; §10.2 mud/snow tires; §4.3 low range; §4.5 diff lock; §5.3 raised intake; §7.7 cold kit
location:
  - Assets/Wayfinder/Scripts/Progression/ResearchService.cs:59,60,69,70,82 (tiers 7/8/12/13/18 — no effects)
  - Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs:42-43   (LowRangeAvailable, DiffLockAvailable — never assigned)
  - Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs:165-167 (tire flags echo caller arguments)
  - Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs:60-66   (Stat enum — no members for these features)
symptom: >
  upgrade.scout.07.lowRange (1,000), 08.mudTires (1,150), 12.raisedIntake (1,750), 13.diffLock (1,900)
  and 18.coldKit (2,900) declare zero Effects, so purchasing them changes no stat. LowRangeAvailable and
  DiffLockAvailable are never written by any code path despite Powertrain.SetLowRange/SetDiffLock
  existing. MudTiresUnlocked/SnowTiresUnlocked/RockTiresUnlocked only echo the Resolve() arguments: the
  Unity path passes `true,true,true` (always unlocked, VehicleController.cs:255) and the campaign path
  passes `false,false,false`, so research never gates tire sets at all.
root_cause: >
  Effects were never authored, and for low range / diff lock / cold kit there is not even a Stat member
  to put them in.
evidence:
  - probe: DEF01_LowRangeAndDiffLockFlagsAreNeverSet (false for both with all tiers researched)
  - probe: DEF01_TireUnlockFlagsOnlyReflectCallerArguments
impact: >
  9,100 of the 32,200 Salvage research budget (28 %) buys nothing, and low range + diff lock are
  unreachable in play — two of the drivetrain features the blueprint treats as core.
fix: >
  Add the missing Stat members and effects, and derive tire availability from research instead of
  parameters. Add a content-validator rule: every upgrade must have ≥ 1 effect or an explicit
  `"effects": "none, by design"`.
verify_with: invert DEF01_* assertions; add a table-driven test asserting each tier changes resolved stats.
```

### DEF-005 — Surface-specific grip never reaches the wheel friction model

```yaml
id: DEF-005
severity: S1
category: gameplay-logic
confidence: static-unity
status: fixed
fixed_note: >
  fixed — per-wheel GripFor(_surfaces[i], SurfaceWet) replaces one PackedDirt resolve (Unity-only file; type-checked, not executed) (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §4.4 (surface-specific traction is the handling pillar), §4.5
location:
  - Assets/Wayfinder/Scripts/Simulation/VehicleController.cs:250-256 (ResolveStats → PackedDirt, wet:false, true,true,true)
  - Assets/Wayfinder/Scripts/Simulation/VehicleController.cs:212-216 (per-wheel surface used only for brakeScale)
  - Assets/Wayfinder/Scripts/Simulation/VehicleController.cs:219-222 (friction stiffness from global stats.EffectiveGrip)
  - Assets/Wayfinder/Scripts/Simulation/VehicleStatsResolver.cs:200   (GripFor(tires, surface, wet, …) — the per-surface API exists)
symptom: >
  Every frame ResolveStats() resolves the whole vehicle for SurfaceType.PackedDirt with wet:false, so
  every wheel's forwardFriction/sidewaysFriction stiffness uses packed-dirt grip. A wheel's real surface
  (_surfaces[index], set by SampleGrounded's ground raycast) is read at :212 but only scales brakeTorque.
  Asphalt, rock, ice, deep mud and snow therefore handle identically.
root_cause: >
  One global EffectiveGrip is composed per Resolve, while the model needs one grip per wheel; the
  per-wheel data and the per-surface GripFor() API both exist and are simply not joined up.
evidence: static read of the cited ranges (Unity-only file; parse-verified, never executed here — KI-034).
impact: >
  Pillar 2 ("terrain is the puzzle") is not implemented: ice is not slippery, mud is not slow, and
  SurfaceTable's μ ≤ 1.25 cap never applies to traction.
fix: >
  Compute grip per wheel (`stats.GripFor(tireSet, _surfaces[i], wet, factor)`), pass the real weather
  `wet` flag, and remove the hardcoded tire unlocks (DEF-004).
verify_with: PlayMode test — same vehicle on Ice vs IntactAsphalt must produce different friction or 0→60 time.
```

---

## 4. S2 — features wrong, inert, or green-but-fake

### DEF-006 — Terrain slope uses the wrong formula, and body pitch is fed where roll is expected

```yaml
id: DEF-006
severity: S2
category: math
confidence: static-unity
status: fixed
fixed_note: >
  fixed — cross-slope from ProjectOnPlane and acos(up.y) for incline (Unity-only file; type-checked) (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §5.4 (rollover warning uses incline context), §5.1 (grade load), §5.4 side-slope limit 22°
location:
  - Assets/Wayfinder/Scripts/Simulation/VehicleController.cs:341 (return Asin(Clamp(up.y - 1, -1, 0) * -1))
  - Assets/Wayfinder/Scripts/Simulation/VehicleController.cs:339 (CurrentTerrainSlopeDegrees = Vector3.Angle(forward, …))
  - Assets/Wayfinder/Scripts/Simulation/VehicleController.cs:164 (RolloverMonitor.Step(…, CurrentTerrainSlopeDegrees(), dt))
  - Assets/Wayfinder/Scripts/Simulation/VehicleController.cs:169 (FuelModel.EstimateLoad(…, CurrentTerrainSlopeRadians(), 0.035f))
symptom: >
  `Asin(1 - up.y)` is not the tilt angle (the correct value is `Acos(up.y)`): a 45° climb reports
  ≈ 0.297 rad (17°), so grade fuel load is under-counted ~2.6×. The value handed to RolloverMonitor as
  "terrainSlopeDegrees" is the *pitch* of the forward vector, while the monitor subtracts it from a
  *roll* measurement — the lateral slope that actually causes rollovers is never represented, and the
  §5.4 22° side-slope limit has no implementation.
root_cause: an approximation was written for acos without deriving it; the rollover context wanted a cross-slope value the controller never computes.
evidence: static read of :164, :169, :339-341.
impact: climb fuel burn is under-reported; rollover warnings react to the wrong axis.
fix: >
  `Mathf.Acos(Mathf.Clamp(transform.up.y, -1f, 1f))` for slope; compute a separate cross-slope
  (`Vector3.Angle(Vector3.up, Vector3.ProjectOnPlane(transform.up, transform.forward))`) for the rollover monitor.
verify_with: a pure test mapping known tilts (0/30/45/60°) to radians; a PlayMode side-slope warning test.
```

### DEF-007 — All 30 contracts + 5 repeatable templates are dead code

```yaml
id: DEF-007
severity: S2
category: gameplay-logic
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — AcceptContract/CompleteContract commands through TransactionService; rewards escrowed and paid exactly once (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §9.3, milestone M4
location:
  - Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs:180 (ContractService)
  - Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs:247 (Accept — no call sites)
  - Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs:255-269 (Complete — no call sites)
  - Assets/Wayfinder/Content/contracts.json (35 rows)
symptom: >
  Nothing outside tests calls ContractService.Accept/Complete, and RewardFor/RepeatableReward are
  unreferenced (:272), so optional work is unplayable. When wired, Complete(:255) also calls
  `economy.Grant(rewardSalvage)` directly instead of going through TransactionService, and emits **no
  GameEvent at all** — bypassing both the commit/rollback path and the §16.3 event contract.
root_cause: content and service exist; the session wiring (garage board, accept/complete commands, escrowed rewards) was never written.
evidence:
  - grep: `grep -rn "Contracts\.\|RepeatableReward" Assets/Wayfinder/Scripts` → persistence plumbing only
impact: an entire content pillar (35 rows) is unreachable; the naive wiring would also corrupt the ledger invariant.
fix: >
  Model accept/complete as GameCommands handled by TransactionService (claim the reward key from
  ClaimedRewardLedger, escrow until banking) and surface a contract board in the garage UI.
verify_with: EditMode test — accept, complete, assert exactly-once payment across save/load.
```

### DEF-008 — The finale coverage gate counts ocean and never reveals it

```yaml
id: DEF-008
severity: S2
category: content
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — the countable mask is baked deterministically, installable by WorldBaker, and persisted (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §8.1 (countable mask excludes ocean/decorative cliffs), §8.4 (60 % finale gate), §9.2 Q20
location:
  - Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs:25,39,43 (CountableMask starts fully set)
  - Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs:50-56 (MapCoveragePercent divides by mask count)
  - Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs:149  (MeetsFinaleMapRequirement ≥ 60)
  - Assets/Wayfinder/Scripts/Campaign/CampaignSnapshot.cs:83       (countableMaskBase64 = string.Empty, "derived data (WorldBaker); never persisted")
  - Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs:159   (Restore path, therefore always all-set)
symptom: >
  The constructor sets all 96×96 = 9,216 cells countable and the mask is deliberately never saved, so
  the Q20 gate (RevealMapPercent 60) scores ocean and unbuildable cells that no reveal can reach.
  Silent by construction: the mask is recomputed as "everything" on every load.
root_cause: the WorldBaker mask step (M3) is unimplemented and the mask is derived data in name only.
evidence:
  - probe: DEF06_* builds a real session → persisted countableMaskBase64 == ""
  - read: DiscoveryService.cs:39-44
impact: >
  The finale gate is unachievable (or reachable in the wrong place), and the map screen
  (ExplorationMap.SheetCoveragePercent, slice-window denominator) shows a *different* percentage than the
  gate uses — two HUD numbers that fight each other.
fix: >
  Bake and persist the mask (96×96 bits = 1,152 bytes, trivially saveable) or, until M3, derive it from
  the authored world area; make both percentages share one denominator.
verify_with: test asserting CountableMask.CountSet() < 9216 for the slice profile and a round-trip that preserves it.
```

### DEF-009 — Unknown item ids escape as KeyNotFoundException out of a transaction

```yaml
id: DEF-009
severity: S2
category: data-integrity
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — PickupContents reports unknown item ids; collection fails with ContentMissing instead of throwing (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: handoff "error handling for unavailable content"; Appendix D typed failures
location:
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:70,76 (PickupContents / ctor — no id validation)
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:103   (PickupCatalog.Add — accepts any id)
  - Assets/Wayfinder/Scripts/Progression/InventoryService.cs:239,246 (InventoryService.Add → ItemCatalog.Get)
  - Assets/Wayfinder/Scripts/Progression/InventoryService.cs:214,216 (CanAdd → ItemCatalog.Get)
  - Assets/Wayfinder/Scripts/Progression/InventoryService.cs:130   (ItemStack.MassKg → ItemCatalog.Get)
  - Assets/Wayfinder/Scripts/Progression/InventoryService.cs:81    (ItemCatalog.CategoryOf)
symptom: >
  Any item id that is not in ItemCatalog throws KeyNotFoundException from inside a domain call.
  TransactionService.ExecuteCollect → InventoryService.Add → ItemCatalog.Get propagates it out of
  Execute into the field loop (a typo in caches.json materials, or content newer than the build).
  PickupContents validates nothing at construction.
root_cause: ItemCatalog.Get throws by design; the pickup path treats ids as trusted input, and the only gate is a Python validator over Content/.
evidence:
  - probe: DEF04_UnknownItemIdInPickupContentThrowsOutOfTransactionExecute → KeyNotFoundException
  - probe: `ItemCatalog.CategoryOf("item.that.does.not.exist")` throws (same family)
impact: one content typo becomes a runtime exception mid-frame, with the transaction's rollback path never reached (the exception escapes Execute before Commit/Rollback).
fix: >
  Validate item ids in PickupContents (fail at content-load time with the pickup id, which
  CampaignContent already surfaces as ContentError) and return FailureReason.ContentMissing from
  collect when an id cannot be resolved.
verify_with: invert the DEF04 assertion; add a cache-materials rule to validate_content.py.
```

### DEF-010 — Double-collect is prevented only while the discovery service is wired

```yaml
id: DEF-010
severity: S2
category: data-integrity
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — Discovery is a required collaborator and TryMarkCacheCollected is checked before the grant is kept (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §12.4 idempotent transactions; QA case 2
location:
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:229  (ExecuteCollect)
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:238  (`_deps.Discovery != null && …IsCacheCollected`)
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:244  (TryMarkCacheCollected return value ignored)
symptom: >
  The duplicate-collect guard is conditional on `_deps.Discovery != null`. Build TransactionDependencies
  without Discovery (a new composition root, a harness, a tool) and the same cache can be collected once
  per distinct TransactionId — the invariant silently degrades to "not enforced". TryMarkCacheCollected's
  bool result is also discarded, so the inventory grant has already happened with no rollback if it returns false.
root_cause: a safety invariant expressed as a nullable-collaborator short-circuit instead of a required dependency (contrast ExecuteResearch/ExecuteEstablishCamp, which fail with InternalError when their collaborator is missing).
evidence: probe: DEF04_DoubleCollectGuardDependsOnDiscoveryBeingWired → 20 salvage_bundle units collected from one 10-unit cache via two transactions.
impact: duplicated rewards whenever the dependency object is built by hand — the exact "idempotent transaction" property the milestone gate claims.
fix: >
  Require Discovery/Pickups (fail with ContentMissing when null), call TryMarkCacheCollected before the
  grant inside the same commit, and roll back the inventory if it returns false.
verify_with: invert the probe (second Execute returns AlreadyCollected).
```

### DEF-011 — The terrain seam check cannot fail (it compares one continuous function with itself)

```yaml
id: DEF-011
severity: S2
category: tooling
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — SeamCheck compares baked tiles and rejects out-of-range chunks; the tile comparison is falsifiable (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §7.4 (tile borders agree within 1 mm), evidence GEN-02
location:
  - Assets/Wayfinder/Scripts/World/WorldDefinition.cs:197  (BordersMatch)
  - Assets/Wayfinder/Scripts/World/WorldDefinition.cs:200-201 (guard `chunkX >= ChunksPerSide - 0` — the `- 0` is a leftover)
  - Assets/Wayfinder/Scripts/World/WorldDefinition.cs:213-214 (a = Sample(endX - 0.001), b = Sample(endX + 0.001))
  - Assets/Wayfinder/Scripts/World/WorldDefinition.cs:222-223 (same trick on the north border)
  - Tools/Validation/formula_checks.py:236-247 (mirror samples the *identical* world coordinate → diff is 0 by construction)
symptom: >
  BordersMatch evaluates one global HeightField.Sample function 2 mm either side of a chunk border instead
  of comparing tile A's last baked column with tile B's first. The maximum possible difference is
  slope × 0.002 m, below the 1 mm quantisation for any slope under 0.5 m/m — hence the reported
  "worst diff 0.0" in both the C# and Python versions. The guard also returns `true` (pass) for
  out-of-range chunk indices instead of failing.
root_cause: the seam test was written against the sampler, not against baked tile data, so it proves continuity of a mathematical function (trivially true).
evidence:
  - probe: DEF03_SeamCheckComparesTheSameContinuousField → probes bit-identical; worst difference 0.0
  - python mirror is even weaker: origin_b = origin_a + (res-1)*spacing → same coordinate, exactly 0
impact: GEN-02 has no real evidence; a genuinely off-by-one tile bake would pass CI.
fix: >
  Bake two adjacent 129×129 tiles and compare tile(23)[i,128] vs tile(24)[i,0] (and the north edge) after
  quantisation; fail (return false) on invalid indices.
verify_with: a mutation-style test that offsets one tile's border and asserts BordersMatch fails.
```

### DEF-012 — Chunk failure detail is always lost

```yaml
id: DEF-012
severity: S2
category: tooling
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — TryTransition(target, detail, out error) carries the cause onto ChunkState.Failed (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §18.3 (streaming defects must be reportable)
location:
  - Assets/Wayfinder/Scripts/World/WorldDefinition.cs:251 (`FailureDetail { get; private set; } = string.Empty`)
  - Assets/Wayfinder/Scripts/World/WorldDefinition.cs:263 (`if (target == ChunkState.Failed) FailureDetail = error;`)
symptom: >
  `error` is assigned only on the rejected-transition path (where the method returns false). On every
  successful transition to Failed it is still `string.Empty`, so ChunkStreamPlanner's StreamingDefects
  entries have no cause.
root_cause: the success path cannot carry a detail string; the variable was reused from the guard clause.
evidence: probe: DEF09_ChunkFailedTransitionLosesItsFailureDetail → FailureDetail == "" after Requested → Failed.
impact: "unknown streaming failure" debugging, exactly what the diagnostics batches were created to eliminate elsewhere.
fix: add `TryTransition(ChunkState target, string detail, out string error)` (or a FailDetail setter) and pass the host's detail.
verify_with: invert the probe assertion.
```

### DEF-013 — Camera horizon-stabilisation toggle is a no-op

```yaml
id: DEF-013
severity: S2
category: unity-integration
confidence: static-unity
status: fixed
fixed_note: >
  fixed — horizon stabilization blends the look-up vector toward target.up (Unity-only file; type-checked) (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §3.4/§3.5 (independent accessibility toggles), §19.1
location:
  - Assets/Wayfinder/Scripts/Simulation/ChaseCameraBehaviour.cs:36  (enableHorizonStabilization default true)
  - Assets/Wayfinder/Scripts/Simulation/ChaseCameraBehaviour.cs:119 (lookRotation built with Vector3.up)
  - Assets/Wayfinder/Scripts/Simulation/ChaseCameraBehaviour.cs:123-124 (Slerp to the identical rotation, t = 1f)
  - Assets/Wayfinder/Scripts/Simulation/ChaseCameraBehaviour.cs:46,116 (_smoothedLookPoint starts at Vector3.zero)
symptom: >
  The stabilised branch recomputes the exact same expression it is blending from and slerps with t = 1f
  (returns the target unchanged), so `enableHorizonStabilization = false` changes nothing — the camera is
  always horizon-locked. `_smoothedLookPoint` also begins at the world origin, so the first frames
  interpolate from (0,0,0) and the camera visibly swivels at start-up.
root_cause: the stabilised and unstabilised paths were never differentiated.
evidence: static read of :119-126.
impact: one of the four §3.5 motion-accessibility toggles does not exist; start-up artefact.
fix: >
  Build the unstabilised rotation with `Vector3.Lerp(Vector3.up, target.up, blend)` as the up vector and
  slerp with the real smoothing factor (not 1f); initialise `_smoothedLookPoint` on first use.
verify_with: PlayMode test toggling the flag on a banked slope and comparing camera up vectors.
```

### DEF-014 — `InteractTapped` is true for every frame of a hold

```yaml
id: DEF-014
severity: S2
category: unity-integration
confidence: static-unity
status: fixed
fixed_note: >
  fixed — InteractTapped is edge-triggered and gated by the accessibility setting (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §3.5 tap alternative; §16.4 "fires once per press"
location:
  - Assets/Wayfinder/Scripts/Platform/InputRouter.cs:27  (holdToInteractAlternativeTap defaults to true)
  - Assets/Wayfinder/Scripts/Platform/InputRouter.cs:133-134 (`WasPressedThisFrame() || (flag && IsPressed())`)
  - Assets/Wayfinder/Scripts/Platform/InputRouter.cs:197 (reset)
symptom: >
  The "tap" flag includes `IsPressed()`, so it stays true on every frame while the button is held. Any
  consumer treating it as an edge event gets one tap per frame.
root_cause: a continuous-press flag used where an edge flag was needed.
evidence: static read; today the hold tracker's `_firedThisPress` latch (Campaign/FieldTrackers.cs:120-136) masks
  it, and the driver ignores the flag entirely (DEF-015).
impact: wiring the §3.5 tap alternative would auto-repeat interactions; currently the feature is absent rather than visibly broken.
fix: keep `InteractTap` edge-only; let the consumer choose tap vs hold from the accessibility setting.
verify_with: unit test asserting the flag is true on exactly one frame per press.
```

### DEF-015 — The driver hardcodes telemetry, so several gameplay inputs are fake

```yaml
id: DEF-015
severity: S2
category: unity-integration
confidence: static-unity
status: fixed
fixed_note: >
  fixed — telemetry reports real mud depth, clearance, roll and Airborne (Unity-only files; type-checked) (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §5.6 safe-pose sampling; §5.2 deep mud; §3.5 accessibility; §2.5 vehicle states
location:
  - Assets/Wayfinder/Scripts/Presentation/VerticalSliceDriver.cs:148 (Grounded = State != Airborne)
  - Assets/Wayfinder/Scripts/Presentation/VerticalSliceDriver.cs:149 (RollDegrees = DeltaAngle(0, eulerAngles.z))
  - Assets/Wayfinder/Scripts/Presentation/VerticalSliceDriver.cs:151-154 (InDeepMud = false; ClearanceMeters = .3f; TapToInteract = false)
  - Assets/Wayfinder/Scripts/Simulation/VehicleController.cs:343-353 (UpdateState assigns only Disabled/Stalled/Driving/Parked)
symptom: >
  BuildFieldInput sends constants instead of measurements: deep-mud stall detection can never trigger,
  the safe-pose clearance gate is a fixed 0.3 m, the tap alternative is unreachable, and roll is a ZXY
  euler component (≈ true roll only near yaw 0/180°). Also, because UpdateState never assigns Airborne/
  Winching/Recovering, `Grounded` is always true and three of the seven §2.5 states are unimplemented.
root_cause: the presentation layer was written against a partly-implemented controller and the missing signals were stubbed with constants.
evidence: static read of both ranges.
impact: deep-mud hazards, safe-pose validity and tap accessibility are inert in the playable slice; RolloverMonitor receives a wrong roll (see DEF-006).
fix: >
  Push real mud depth/clearance/roll from VehicleController (it already owns GroundClearanceM, the mud
  model and the wheel hits) and set Airborne/Winching/Recovering from `_grounded`, the winch state and
  the recovery flow.
verify_with: PlayMode test — jump the vehicle, assert State == Airborne; park in mud, assert InDeepMud.
```

### DEF-016 — The Unity-side content validator reads a field that does not exist

```yaml
id: DEF-016
severity: S2
category: tooling
confidence: static-unity
status: fixed
fixed_note: >
  fixed — the editor validator reads reward.salvage and enforces objective reachability; validate_content.py is the authoritative side (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §17.1 content validation; §16.6 CI must fail on content errors
location:
  - Assets/Wayfinder/Scripts/Editor/ContentImporter.cs:179 (`token.Value<int>("rewardSalvage")`)
  - Assets/Wayfinder/Scripts/Editor/ContentImporter.cs:180 (`if (reward < 0) report.Error(...)` — unreachable)
  - Assets/Wayfinder/Scripts/Editor/ContentImporter.cs:80  (`public static class ContentValidators`)
  - Assets/Wayfinder/Scripts/Editor/ContentImporter.cs:89  (ValidateAll) / :124 (ValidateMissionTable call)
  - Assets/Wayfinder/Content/missions.json                  (schema: reward.salvage; no rewardSalvage)
  - Tools/Validation/validate_content.py:4                  (docstring says it mirrors ContentValidators.cs — that file does not exist)
symptom: >
  ValidateMissionTable reads a property the schema does not contain, so `reward` is always 0 and its
  negative-reward error can never fire. The validator also never checks that objective targets exist —
  the gap that hides DEF-003 — and `Import()` (:24) runs ValidateAll as its gate.
root_cause: validator written against an assumed schema, in Unity-only code (KI-034) that CI never compiles; the
  Python "mirror" is a hand-written duplicate pointing at a non-existent file, so drift is invisible.
evidence:
  - static read of :179; json dump of missions.json keys shows `reward`, `turnInHub`, `objectives`
  - `ls Assets/Wayfinder/Scripts/Editor/` → no ContentValidators.cs (only BuildCommands, ContentImporter,
    EditorFailureLog, ProjectSetup, SceneBuilder, WorldBaker + asmdef)
impact: bad mission content passes in-editor validation; combined with DEF-003, unreachable objectives ship.
fix: >
  Read `token["reward"]?["salvage"]`, add the objective-reachability rule to BOTH validators, and delete or
  correct the docstring/path claim (make one implementation authoritative rather than two hand copies).
verify_with: a negative fixture (mission → non-existent landmark) that must fail validation.
```

### DEF-017 — Any bank drains the entire escrow balance

```yaml
id: DEF-017
severity: S2
category: gameplay-logic
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — escrow is keyed by hub; a bank releases the unattributed pool plus its own share only (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §2.3, §8.4, §12.2 ("bank resources" at garage/camp)
location:
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:40  (BankEscrow — moves the whole balance)
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:339 (CargoSettlement.Settle)
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:380 (`int escrow = economy.BankEscrow();`)
  - Assets/Wayfinder/Scripts/Campaign/CampaignSession.Field.cs:240,247 (DepositAtCamp reads EscrowedSalvage before the transaction)
symptom: >
  Escrow is a single global counter with no hub dimension, so the first bank of any kind — camp deposit
  or garage extraction — moves everything, including rewards earned for a different region/hub. The camp
  notice is also built from pre-transaction state (`Economy.EscrowedSalvage` read at :247), so a
  rolled-back deposit can still print a "Deposited N Salvage" toast.
root_cause: escrow has no hub key, and the notice is computed before the commit instead of from the outcome.
evidence: probe: DEF10_CargoSettlementBanksEscrowFromAnySourceOnEveryBank → 75 escrowed elsewhere is banked by an empty cargo deposit.
impact: banking bookkeeping contradicts §8.4/§12.2; the summary's "banked" figure can describe rewards never earned near that hub.
fix: >
  Key escrow by hub/reward source (`Dictionary<string,int>`) or bank only rewards whose region matches the
  hub; build the deposit notice from the returned Outcome.
verify_with: test — escrow at hub A, bank at hub B, assert A's escrow untouched.
```

### DEF-025 — The formula-check harness is self-referential and contains tautologies

```yaml
id: DEF-025
severity: S2
category: tooling
confidence: static-pure
status: fixed
fixed_note: >
  fixed — formula_checks.py parses the C# constants and a self-lint rejects literal-only checks (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §16.6 / GEN evidence chain; DECISIONS on headless verification
location:
  - Tools/Validation/formula_checks.py:145  (`check("water: baseline wading 0.55 m", True and 0.55 == 0.55)`)
  - Tools/Validation/formula_checks.py:146-148 (0.85 == 0.85; 2.0 == 2.0; 1.2 == 1.2)
  - Tools/Validation/formula_checks.py:140  (`check("rescue: only salvage_bundle is ever lost", True)`)
  - Tools/Validation/formula_checks.py:128-129 (dead first COSTS line containing `2900 and 2900`)
  - Tools/Validation/formula_checks.py:236-247 (seam check compares one coordinate with itself)
  - Tools/Validation/formula_checks.py:13-14 (imports only `math`, `sys` — no file is ever read)
symptom: >
  The file re-implements the formulas in Python and asserts the blueprint's own example numbers; it never
  reads the C# source, so a green run says nothing about the implementation. Five checks are literal
  tautologies that cannot fail, and the printed PASS is quoted in Docs/EVIDENCE_* as formula evidence —
  which is how DEF-001 (wading = 0), DEF-004 (empty tiers) and DEF-011 (fake seam test) all passed review.
  The file also contains a dead duplicate `COSTS` assignment whose first line mixes values with `and`
  (`2900 and 2900`), so an edit to line 128 is silently discarded.
root_cause: the harness was written as a spec restatement rather than as a cross-check against code or content.
evidence:
  - executed: the tautologies pass while DEF-001/DEF-004 are present in the C# (probe bug-probe.log)
  - static: no `open(`/`json`/`Assets/` reference anywhere in the file
impact: verification theatre — the strongest-looking green signal in the repo is unfalsifiable for the formula layer.
fix: >
  Either (a) make the checks read the real constants/DTOs (parse the C# constants, or better: drive the
  pure C# domain from a small harness like the probe, which is already proven to work), or (b) delete the
  tautological checks and label the file as "spec document mirror, not verification". Add a lint rule
  that no check() argument may be an expression of literals only, and remove the dead COSTS line.
verify_with: mutate a C# constant (e.g. water intake 0.55 → 0.10) and require the harness to fail.
```

---

## 5. S3 — robustness, data hygiene, performance

### DEF-018 — Only 4 of 10 command types emit a committed GameEvent

```yaml
id: DEF-018
severity: S3
category: gameplay-logic
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — ItemCrafted/ItemsTransferred/ItemsSold/ItemUsed/RecoveryRequested are emitted on commit (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §16.3 ("events report committed outcomes only")
location:
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:173  (`GameEvent committed = null;`)
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:189,193,197,201,208 (CraftItem, TransferItems, SellItems, UseItem, RequestRecovery — no assignment)
  - Assets/Wayfinder/Scripts/Progression/EconomyService.cs:225  (`Commit(command.TransactionId, committed)`)
  - events exist only for CollectPickup (:245), BankCargo (:254), ResearchUpgrade (:266), EstablishCamp (:288)
symptom: crafting, transferring, selling and using items commit with `committed = null`, so nothing reaches OnCommitted. The campaign HUD emits its own notices instead and cannot observe field-supply use at all.
evidence: probe: DEF05_OnlySomeCommandsEmitCommittedEvents → crafting a camp kit succeeds with 0 events raised.
impact: any event-driven consumer (objectives like "craft N items", audio, telemetry-free achievements) silently misses half the mutations the engine performs.
fix: define ItemCrafted / ItemsTransferred / ItemSold / ItemUsed / RecoveryRequested and populate `committed` in each case.
verify_with: invert the probe; assert one event per command kind.
```

### DEF-019 — Permanent reward events carry default(TransactionId)

```yaml
id: DEF-019
severity: S3
category: data-integrity
confidence: verified-executed
status: fixed
fixed_note: >
  fixed — the session passes a real TransactionId to TryDiscoverLandmark (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §16.3 ("every permanent reward event carries a stable source key and transaction ID")
location:
  - Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs:118 (TryDiscoverLandmark signature, out GameEvent discovered)
  - Assets/Wayfinder/Scripts/Progression/DiscoveryService.cs:132 (`new LandmarkDiscovered(default, …)`)
symptom: landmark discovery — a permanent 120-Salvage reward — is emitted with `default(TransactionId)` (Value == null). Contract completion emits no event at all (see DEF-007).
root_cause: the event was constructed before a transaction id was threaded through; the reward key does the dedup, so the null id went unnoticed.
evidence: probe: DEF08_TransactionIdOfGrantedRewardsIsDefaultInDiscoveryService → TransactionId.Value is null while 120 Salvage was escrowed.
impact: latent — the §16.3 event contract is not honoured for landmark rewards; any replay/dedup consumer keying on the id sees null.
fix: pass a transaction id (field-use site already knows it), or make the field's nullability explicit in the contract and document the reward key as the identity.
verify_with: invert the probe, or update the documented contract.
```

### DEF-020 — BitGrid base64 restore validates nothing

```yaml
id: DEF-020
severity: S3
category: persistence
confidence: verified-executed
status: fixed
spec: §15 save integrity (checksum + backup), QA case 19
location:
  - Assets/Wayfinder/Scripts/Core/BitGrid.cs:114 (`FromBase64`)
  - Assets/Wayfinder/Scripts/Core/BitGrid.cs:118 (`Convert.FromBase64String` — no length/format guard)
  - Assets/Wayfinder/Scripts/Persistence/SaveService.cs:233,237 (catch filters: IOException / UnauthorizedAccessException only)
symptom: >
  A short payload is silently accepted (remaining words stay zero → "everything fogged"), and a malformed
  payload throws System.FormatException, which SaveService.TryRead does not catch.
root_cause: no validation and no typed error path; the checksum normally makes this unreachable.
evidence: probe: DEF07_BitGridFromBase64DoesNotValidatePayload (both cases).
impact: >
  Latent today because the envelope checksum covers canonical JSON — but it becomes live the moment a
  checksum-bypassing path writes the field (migration, debug import, external tool).
fix: validate `bytes.Length == words*8` and throw a typed persistence error (or return false so the caller
  can fall back to the backup); add FormatException to TryRead's catch filter.
verify_with: corrupt-payload test expecting LoadResultStatus.LoadedBackup instead of a crash.
```

### DEF-021 — Inventory identity gaps: mission instances and the protected ledger

```yaml
id: DEF-021
severity: S3
category: data-integrity
confidence: static-pure
status: fixed
fixed_note: >
  fixed — Remove/QuantityOf take a mission-instance filter (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
location:
  - Assets/Wayfinder/Scripts/Progression/InventoryService.cs:276-297 (Remove matches ItemId only; remainder keeps MissionInstanceId)
  - Assets/Wayfinder/Scripts/Progression/InventoryService.cs:143   (readonly List<string> _protectedLedger)
  - Assets/Wayfinder/Scripts/Progression/InventoryService.cs:249   (`_protectedLedger.Add` — no dedupe)
  - Assets/Wayfinder/Scripts/Progression/InventoryService.cs:374-376 (RemoveProtected removes one entry)
symptom: >
  Remove cannot target a specific mission instance, so removing N of an item drains stacks in list order
  and can consume quest-instance cargo (contrast Plan(), which *does* match MissionInstanceId when adding).
  The protected ledger is a List: adding the same quest item twice stores it twice and each copy must be
  consumed separately.
root_cause: container operations match on ContentId only, and the ledger was modelled as a set but stored as a list.
evidence: static read of the cited ranges.
impact: quest-item accounting can silently become ambiguous; a duplicate ledger entry could satisfy two objectives with one object.
fix: add an optional missionInstanceId filter to Remove (and use it from quest paths); back the ledger with an ordered HashSet or reject duplicates with AlreadyApplied.
verify_with: EditMode test — two quest stacks of one item, remove one instance.
```

### DEF-022 — SaveService error handling and hygiene gaps

```yaml
id: DEF-022
severity: S3
category: persistence
confidence: static-pure
status: fixed
fixed_note: >
  fixed — Save() converts non-IO failures to FailedWrite and clears the temp file; LoadBestValid rejects another slot's envelope (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
location:
  - Assets/Wayfinder/Scripts/Persistence/SaveService.cs:181-240 (Save)
  - Assets/Wayfinder/Scripts/Persistence/SaveService.cs:233,237 (catch IOException / UnauthorizedAccessException only)
  - Assets/Wayfinder/Scripts/Persistence/SaveService.cs:244-303 (LoadBestValid — never checks envelope.slotId)
  - Assets/Wayfinder/Scripts/Persistence/SaveService.cs:163 (File.Replace already writes the backup argument)
  - Assets/Wayfinder/Scripts/Persistence/SaveService.cs:315 (TryDelete, called only on FailedVerify)
symptom: >
  (a) `Save()` does not catch JsonException (from CanonicalJson.Serialize) or ArgumentException (malformed
  DTO), so a serialization failure escapes to the caller instead of returning the typed FailedWrite.
  (b) On FailedWrite the temp file is left behind — TryDelete runs only on the verify path.
  (c) `LoadBestValid` never checks `envelope.slotId` against the slot it read, so a manually copied file is
  accepted. (d) The manual backup rotation duplicates what File.Replace already does (harmless, doubles I/O).
root_cause: the catch filters cover disk errors only, and slot identity is not re-checked on load.
evidence: static read; interruption tests exercise only the StepHook path.
impact: save failures surface as unhandled exceptions; stale temp files accumulate; a copied save can load into the wrong slot.
fix: broaden the filter to `IOException || UnauthorizedAccessException || JsonException || ArgumentException`,
  delete the temp file on every failure path, verify slotId in LoadBestValid, drop the redundant copies.
verify_with: PersistenceTests — serializer throws → FailedWrite; failed write leaves no temp file; wrong-slot envelope rejected.
```

### DEF-023 — HeightField allocates ~38 MB up front

```yaml
id: DEF-023
severity: S3
category: performance
confidence: static-pure
status: fixed
fixed_note: >
  fixed — the 37.8 MB grid is allocated by Bake(), not by the constructor (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §18.5 quality tiers; §19 memory budget on 4 GB Android devices
location:
  - Assets/Wayfinder/Scripts/World/WorldDefinition.cs:147,153 (_gridSide = 6144/2 + 1 = 3073)
  - Assets/Wayfinder/Scripts/World/WorldDefinition.cs:154 (`_samples = new float[_gridSide * _gridSide]`)
symptom: >
  9,443,329 floats ≈ 37.8 MB of managed heap is allocated when HeightField is constructed, for the whole
  6,144 m x 6,144 m mainland. For a mobile-first title that is a large fixed cost next to the ~2.4 MB map
  texture budget (MapRaster.MaxTexturePixels) the codebase explicitly defends.
root_cause: one dense array is used as the accessor instead of chunked/tiled storage or a quantised field.
evidence: static read of :147-157 (GridSide = 3073 asserted by the same arithmetic in formula_checks.py:190).
impact: GC pressure and low-memory kill risk on target devices, plus a slow first bake.
fix: store per-chunk tiles (24×24 × 129×129) streamed with the chunk, or quantise the field (int16/ushort with a per-chunk bias) while preserving the 1 mm contract.
verify_with: a test asserting retained size / allocation for one chunk load.
```

### DEF-024 — Weather 1 Hz accumulator drifts

```yaml
id: DEF-024
severity: S3
category: gameplay-logic
confidence: static-pure
status: fixed
fixed_note: >
  fixed — the accumulator carries its remainder (_accum -= 1f / SimulationHz) (2026-10-19). Pinned by
  Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs.
spec: §7.7 (weather sim at 1 Hz)
location:
  - Assets/Wayfinder/Scripts/World/RouteGraph.cs:204 (class WeatherSystem)
  - Assets/Wayfinder/Scripts/World/RouteGraph.cs:213 (_accum)
  - Assets/Wayfinder/Scripts/World/RouteGraph.cs:223-225 (`_accum += dt; if (_accum < 1f/SimulationHz) return; _accum = 0f;`)
symptom: >
  The accumulator is reset to 0 instead of decremented, so the effective tick period becomes the smallest
  multiple of the frame delta above 1 s (1.016 s at 60 FPS), drifting relative to wall time.
root_cause: `_accum -= 1f / SimulationHz` was written as a reset; the error is invisible in play.
evidence: static read of :223-225.
impact: weather/water-level transitions tick slightly late; RNG stream advances at a drifting cadence.
fix: `_accum -= 1f / SimulationHz;` plus a clamp to avoid a catch-up spiral after suspension.
verify_with: step 600 × 1/60 s and assert exactly 10 weather ticks.
```

---

## 6. Documentation-vs-reality drift (do not trust these claims)

| Claim in the repo | Reality (evidence) |
|---|---|
| `Docs/IMPLEMENTATION_STATUS.md`: M4 "All 20 missions, 30 contracts + 5 templates — Implemented" | **Now close to true:** Q05–Q20 targets exist (DEF-003) and contracts can be accepted and completed (DEF-007). The sites Q05–Q20 use are authored in `Content/sites.json`; the relay four sit on the §6.1 anchors, the other 19 are procedural placements awaiting a level-design pass. Contract *sockets* (`contract_socket.*`) are still not placed in the world. |
| README: research "20 tiers Σ 32,200 Salvage" implemented | **True again:** all 20 tiers carry effects (DEF-004) and composition is rank-ordered (DEF-002). Σ is still 32,200. |
| §4.4 surface-specific traction ("terrain is the puzzle") | Per-wheel grip from the real surface and weather (DEF-005) — **needs one Unity PlayMode run** to confirm. |
| §2.5 vehicle states Parked/Driving/Airborne/Winching/Stalled/Disabled/Recovering | Airborne is now assigned (DEF-015). Winching and Recovering are still not — they need the winch and rescue flows, which are M5 scope. |
| "terrain seam agreement ≤ 1 mm — worst diff 0.0" (EVIDENCE_M0, formula_checks output) | Still 0.0, and always will be: adjacent tiles sample the same world line from a continuous field. The comparison is now exercised against a falsifiable case, and the output says which checks are mirrors rather than evidence (DEF-011/DEF-025). |
| `formula_checks.py` green output quoted as formula evidence | Now reads `HazardModels.cs`, `WorldDefinition.cs`, `ResearchService.cs`, `upgrades.json` and `CampaignSession.Extraction.cs`; a self-lint rejects literal-only checks (DEF-025). Mutating `BaselineSafeWadingM` to 0.10 fails it. |
| `validate_content.py` docstring: mirrors `Assets/Wayfinder/Scripts/Editor/ContentValidators.cs` | Corrected — the docstring now names `ContentImporter.cs` (class `ContentValidators`) and states that the Python file is authoritative. Both sides enforce objective reachability (DEF-016); the reward key they read is `reward.salvage`. |
| `Docs/KNOWN_ISSUES.md` KI-034 "four assemblies not compiled by Unity" | Still true. `Tools/Build/unity_type_check.py` now compiles all 12 assemblies against a stub engine API (12/12 green), which removes the most likely class of KI-034 error but is **not** a Unity compilation. |
| README "164 headless tests pass" | **True** — and now 188/188 (164 original + 24 regression tests for this report). |

Non-defect context an AI needs:

* `Docs/KNOWN_ISSUES.md` tracks KI-001…KI-041 with its own numbering. DEF-001…DEF-025 are new,
  non-overlapping findings — merge them as KI-042+ or keep this file as the review companion.
* The "syntax-checked only (KI-034)" warnings inside Unity-facing files are honest; the drift is in the
  milestone/evidence tables, not in those files.

---

## 6b. Status of this report after the 2026-10-19 fix pass

Every defect is closed in code. What "closed" means, precisely:

* **Engine-free assemblies** (Core, World, Progression, Persistence, Campaign) — fixes are executed:
  188/188 EditMode tests pass, including 24 new regression tests in
  `Assets/Wayfinder/Tests/EditMode/BugReportRegressionTests.cs`, one per defect.
* **Unity-facing assemblies** (DEF-005, DEF-006, DEF-013, DEF-014, DEF-015 in Platform/Presentation/
  Simulation) — fixed and type-checked against the stub engine API by
  `Tools/Build/unity_type_check.py` (12/12 assemblies, 0 warnings), **not executed**. They still
  need one real Unity run (KI-034).
* **Executed evidence that the fixes are real:** `python3 Docs/evidence/ai-review-2026-10-16/run_bug_probe.py`
  now fails 10 of its 15 characterization assertions — the intended inversion (§8 step 8).

The five probes that still pass, and why that is not a contradiction:

| probe | why it still passes |
|---|---|
| DEF01_TireUnlockFlagsOnlyReflectCallerArguments | it calls `Resolve(..., true,true,true)` directly; the fix is that `VehicleController.ResolveStats()` no longer passes hardcoded unlocks (a Unity file, so the probe cannot see it) |
| DEF02_ComposeIsSetOrderDependent | it uses the rank-less `AddModifier` overload; equal ranks still fall back to list order by design. The research tree now always supplies a tier rank, which is what made the ordering non-deterministic |
| DEF03_SeamCheckComparesTheSameContinuousField | a real `HeightField` is continuous, so two tiles sampling the same world line always agree — the check returns 0, as it should. The comparison itself is now falsifiable (`SeamCheck.BordersMatch(tile, neighbour, edge, out diff)`) and is tested with a 5 mm disagreement |
| DEF08_TransactionIdOfGrantedRewardsIsDefaultInDiscoveryService | it calls the legacy 4-argument `TryDiscoverLandmark`. The session uses the new overload that threads a real transaction id |
| DEF10_CargoSettlementBanksEscrowFromAnySourceOnEveryBank | it escrows without a hub, which is exactly the "bankable anywhere" pool. Hub-attributed escrow is a new code path, covered by the DEF-017 tests |



* Headless toolchain: real Roslyn 3.9 / C# 9 compile of **63 engine-free C# files across 7 assemblies**
  — 0 errors, 0 warnings, .NET Standard 2.1 profile clean, 164/164 NUnit tests, 89/89 build-tool unit tests.
  The remaining 30 Unity-dependent files (26 in Platform/Presentation/Editor/Tests.PlayMode/Tests.Editor +
  4 excluded from Simulation) are parsed for syntax only (KI-034).
* Content data: 100 landmarks, 200 caches, 20 missions, 35 contracts, 20 upgrades (Σ 32,200), 4 vehicles,
  10 camps, 5 garages, 25 relics, 11 surfaces, 18 items, 4 recipes — `validate_content.py` 0 errors.
* Persistence: round-trip, checksum verification, backup rotation and interruption at each of 6 save steps
  are covered by real tests; migrations preserve unknown fields (QA case 19).
* Determinism primitives: FNV-1a (StableHash) and PCG-XOR (StableRandom) are correctly implemented; the
  probe's seam/quantisation concerns (DEF-011/DEF-025) are about the *tests*, not the generator.
* Economy: no crafting arbitrage (the resale-violation predicate is correct), one authoritative ledger, no
  negative balances (TrySpend/Plan guard), crafting atomicity holds.
* Inventory: `Add/Remove/AddAll/Transfer` are genuinely all-or-nothing (KI-015/KI-019 fixes hold).
* Field trackers: `HoldInteractTracker` (fire-once per press), `ArrivalTracker` (1 m/s, 2 s), `ObservationTracker`,
  `SafePoseSampler` behave as documented.
* `ChunkStateMachine` transition table (excluding DEF-012's detail loss), `SeamCheck` *API shape*,
  `SaveMigrations` scaffolding and `CampaignSession` command routing match their contracts.

---

## 8. Dependency-ordered fix plan

1. **Stat/research core — DEF-001, DEF-004, DEF-002** (one batch): add the missing Stat members, author the
   five empty tiers, make composition rank-ordered. Invert the DEF01/DEF02 probe assertions and move them
   into `Tests/EditMode`.
2. **Campaign reachability — DEF-003, DEF-008, DEF-016**: implement garage unlocking through
   TransactionService; author-or-excise the Q05–Q20 target ids; persist/bake the countable mask; add the
   objective-reachability rule to `validate_content.py` (authoritative) and fix the C# validator + docstring.
3. **Physics truth — DEF-005, DEF-006, DEF-015**: per-wheel grip, correct slope math, real telemetry
   (mud depth, clearance, roll, Airborne state). This is the batch that makes driving match the pillars.
4. **Transaction/event completeness — DEF-007, DEF-017, DEF-018, DEF-019, DEF-021**: contracts through
   TransactionService, per-hub escrow, events for all command kinds, mission-instance identity.
5. **Failure paths — DEF-009, DEF-010, DEF-020, DEF-022**: validate content ids, require dependencies,
   typed restore errors, save hygiene.
6. **Verification integrity — DEF-025, DEF-011, DEF-012, DEF-024**: make `formula_checks.py` cross-check
   code (or demote it to documentation), rebuild the seam test against baked tiles, carry failure details,
   decrement the weather accumulator.
7. **Unity-only — DEF-013, DEF-014**: do these together with the first real Unity run (both need a compiled
   editor to verify).
8. Re-run the whole baseline: `headless_cs.py all` must stay green (plus new tests);
   `validate_content.py`; `formula_checks.py`; and `run_bug_probe.py` must now **fail** on every fixed
   defect — that failure is the signal that the characterizations were inverted.

Steps 1–8 are done (2026-10-19): `headless_cs.py all` = compile OK, **188/188** tests, netstandard2.1
profile OK, 30-file syntax check OK; `validate_content.py` 0 errors; `formula_checks.py` 0 failures
(and it now reads the C#); `run_bug_probe.py` fails 10/15. `unity_type_check.py` is 12/12.
See `Docs/evidence/bug-report-2026-10-19/README.md`.

---

## 9. Appendix — reproduction commands

```bash
# 1. Full headless baseline (compile + 164 tests + netstandard2.1 profile + syntax)
python3 Tools/Build/headless_cs.py all

# 2. Gates
python3 -m unittest discover -s Tools/Build -p 'test_*.py'      # 89 tests, OK
python3 Tools/Validation/validate_content.py                     # 0 errors, 0 warnings
python3 Tools/Validation/formula_checks.py                       # 0 failures (see DEF-025 caveat)
python3 Tools/Balance/economy_sim.py

# 3. This review's executed evidence (15 characterization tests; all pass on commit 0edcb11)
python3 Docs/evidence/ai-review-2026-10-16/run_bug_probe.py

# 4. Spot checks behind the static findings
grep -rn "UnlockGarage" Assets/                                          # DEF-003: definition only
grep -rn "Contracts.Accept\|Contracts.Complete\|RepeatableReward" Assets/ # DEF-007: no call sites
grep -rn "SafeWadingDepthM\|LowRangeAvailable\|DiffLockAvailable" Assets/ # DEF-001/004: never assigned
ls Assets/Wayfinder/Scripts/Editor/                                      # DEF-016: no ContentValidators.cs
python3 -c "import json;print(sorted({k for m in json.load(open('Assets/Wayfinder/Content/missions.json')) for k in m}))"
```
