# Evidence — Batch K: §10.2 equipment slots and swappable tire sets (2026-10-21)

Closes the last open half of **KI-028**. Decision record: **D-041** in `Docs/DECISIONS.md`.

Everything here was produced by `Tools/Build/headless_cs.py` (a real C# compiler + NUnit outside
Unity, D-014) and `Tools/Validation/*.py` on Linux x86_64. It is **not** a Unity build result
(KI-016) and proves nothing about on-device behaviour (KI-006, KI-009).

## What was implemented

| Area | Change |
| --- | --- |
| `Progression/ResearchService.cs` | `ModuleSlot` enum + a slot on all 20 canonical tiers; `ApplyTo` composes **only fitted** modules; `Fit`/`Unfit`/`CanFit`, `FittedIds`/`IsFitted`, `ModulesInSlot`/`ModuleInSlot`/`SlotOf`/`SlotCapacity`; `SelectedTireSet`/`SelectTireSet`/`IsTireSetUnlocked`/`UnlockedTireSets`; `SlotName`/`TryParseSlot` deferring to `Simulation.EquipmentSlots`; auto-fit on purchase; `Restore(researched, fitted, tireSet)` reconciling pre-slot saves |
| §11.2 content defects fixed | tier 13 now sets `Stat.RockTires` (nothing ever set it — the Rock tire set was **unreachable in every save**); tier 20 carries `PeakTorqueNm ×1.10` forward from tier 10 (§11.2:739 explicit final composition) |
| `Campaign/CampaignSession*.cs` | `EquipModule`/`UnequipModule`/`SelectTireSet` — garage-only (§10.4), inside `RunCritical`, re-configuring containers, clamping fuel to a shrinking tank (§5.4), then the **QA case 9** over-capacity refusal with snapshot rollback; `SelectedTireSet`/`FittedModules`; `ResolveStats` uses the mounted set + research-derived unlock flags instead of `AllTerrain, false, false, false` |
| `Campaign/CampaignSnapshot.cs` | captures the fitted loadout (not everything researched), the unlocked sets (not a hardcoded `AllTerrain`) and the mounted set; restores through the reconciling `Restore` |
| `Persistence/SaveDtos.cs` | additive `VehicleDto.equippedTireSet = "AllTerrain"` — **no schema bump** (D-021 precedent) |
| `Campaign/CampaignViewModels.cs` | `GarageModel.ResearchRow` gains `Slot`/`SlotName`/`IsFitted`/`CanFit`/`CanUnfit`/`ReplacesId`; new `TireRow` list with the unlocking tier's name |
| `Content/upgrades.json` | `moduleSlot` on every tier + the two effect amendments above |
| `Tools/Validation/validate_content.py` | pins the slot per tier, the slot vocabulary, exactly two utility slots, and the existence of all three tire-set unlocks |
| `Presentation/VerticalSliceDriver.cs` | assigns `vehicle.tireSet = _session.SelectedTireSet` (nothing did before — narrows KI-025) and offers fit/remove/mount in the research panel; **syntax-checked only** |

## Files in this folder

| File | What it shows |
| --- | --- |
| `headless-console.log` | `headless_cs.py all` — 7 assemblies compiled at warning level 4 with 0 warnings, 239/239 EditMode tests, .NET Standard 2.1 profile OK, 30 Unity-dependent files parse |
| `EditMode.headless.xml` | NUnit result XML for the same run (per-test names and durations) |
| `compile-report.json` | per-assembly compile report from the same run |
| `python-checks.log` | `validate_content.py` and `formula_checks.py`, both clean |
| `mutation_check.py` | the 27-mutant harness for this batch (refuses to run on a dirty `Assets/`, restores every source, verifies the tree is clean afterwards) |
| `mutation-check.log` | its output: every mutant caught, with the test that caught it |

## Test results

* **239/239** EditMode tests pass (216 → 239). New: `CampaignModuleSlotTests` (22 tests) and
  `ContentCatalogTests.UpgradeTable_MatchesCanonicalSlotsAndEffects_D041`.
* **27/27** mutants caught — see `mutation-check.log`.
* `validate_content.py`: 0 errors, 0 warnings. `formula_checks.py`: clean.

The new fixture covers: buying fits; slot-less tiers are unlocks and cannot be fitted; a named slot
holds one module (the newer tier replaces the older, and the older can be refit); two utility slots;
unequip drops the effect and refit restores it; fuel clamps to a smaller tank; tire sets locked until
their tier; mounting Mud changes surface grip **without** a second Trail Tires bonus (§11.2:739);
tier 13 unlocks Rock and tier 18 unlocks Snow with the exact §10.2 multipliers; unlocks survive
unequipping the module that carried them (in the service *and* in the resolver's flags); sets are
mutually exclusive; tier 20 keeps the torque curve and does not stack the drain; garage == driving;
garage-only refusals; unknown/unresearched/not-fitted refusals leave the campaign byte-identical;
QA case 9 over-capacity refusal **with rollback** and the legal retry after unloading; notices; save →
reload round-trip of the loadout and mounted set; and a hand-built pre-slot save reconciling to the
highest tier per slot with no research lost.

## What this evidence does NOT prove

* No Unity Editor, PlayMode, physics, device, frame-time, thermal, build or store evidence
  (KI-006, KI-009, KI-016, KI-026). The `VerticalSliceDriver` changes are syntax-checked, never run.
* Per-wheel grip and suspension geometry still reach the rig only partially (KI-025); this batch only
  made the mounted tire set visible to the rig's resolver.
* The "both utility slots are occupied" refusal in `ResearchService.Fit` is **not** mutation-covered:
  canonical content has exactly two utility tiers and an already-fitted module is refused earlier as
  `AlreadyApplied`, so the branch is unreachable through the public API. It is defensive and recorded
  as such in D-041 rather than claimed as tested.
* M2's exit gate is a 30-minute on-device playtest, which needs the owner and an APK (KI-002/KI-026).

## Reproduce

```bash
python3 Tools/Build/headless_cs.py all          # compile + test + profile + syntax
python3 Tools/Validation/validate_content.py
python3 Tools/Validation/formula_checks.py
python3 Docs/evidence/module-slots-2026-10-21/mutation_check.py   # clean Assets/ tree required
```
