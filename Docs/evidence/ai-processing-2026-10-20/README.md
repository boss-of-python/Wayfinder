---
doc_id: wayfinder-evidence-ai-processing-2026-10-20
date: 2026-10-20
batch: AI Processing Format triage (SYS-SIM/SAV/WRD/CICD)
decision: D-038
triage: Docs/AI_PROCESSING_TRIAGE.md
---

# Evidence — AI processing format triage (2026-10-20)

Everything in this folder was produced by commands run in this repository on the date above, on the
working branch. Nothing here is a Unity result, a device measurement or a build artefact — none of
those exist yet (KI-006, KI-009, KI-034).

| File | Produced by | Result |
|---|---|---|
| `headless-cs.log` | `python3 Tools/Build/headless_cs.py all` | compile **OK** (7 assemblies, 0 errors, 0 warnings), tests **205/205**, .NET Standard 2.1 profile **OK**, syntax **OK** (30 Unity-only files parsed, not type-checked) |
| `EditMode.headless.xml` | same | NUnit3 XML, 205 test cases |
| `compile-report.json` | same | per-assembly file lists, errors and warnings |
| `unity-type-check.log` | `python3 Tools/Build/unity_type_check.py` | **12/12 assemblies OK, 0 warnings** against the hand-written Unity stubs — this is a type-check, **not** a Unity compile (KI-044) |
| `unity-type-check.json` | same | machine-readable report |
| `diagnostics-tests.log` | `python3 Tools/Build/test_build_diagnostics.py` | **93/93 pass** (2 new: batchmode exit, Android API pin agreement) |
| `pipeline-tests.log` | `python3 Tools/Build/test_unity_pipeline.py` | **23/23 pass** |
| `content-validation.log` | `python3 Tools/Validation/validate_content.py` | 0 errors, 0 warnings |
| `formula-checks.log` | `python3 Tools/Validation/formula_checks.py` | 0 failures (includes the §5.1 fuel example — unchanged by this batch) |
| `economy-sim.log` | `python3 Tools/Balance/economy_sim.py` | no deadlocks; model only (KI-010) |
| `mutation-check.log` | `python3 Docs/evidence/ai-processing-2026-10-20/mutation_check.py` | **8/8 mutations killed** |

## What the 17 new headless tests cover

`Assets/Wayfinder/Tests/EditMode/`:

* **SimulationTests (11)** — undefined surface labels fall back rather than indexing past the table;
  every declared label has a matching row; coefficients clamp and NaN never reaches the solver; the
  §4.4 cross-fade decays over 0.15 s, is frame-rate independent, survives bad deltas, continues from the blended value when a third
  surface arrives mid-fade, and snaps for water; §5.1 auto-stop fires once after 15 simulated seconds, refuses to fire while driving,
  throttling or holding the brake, restarts on a pedal, leaves a manually stopped engine alone, and
  the parked drain is zero **without changing the drain formula**.
* **PersistenceTests (5)** — a stale `.tmp` from a killed process is cleared and never trusted; a
  failed replace leaves no buffer; a v1→v3 pipeline runs its steps in order exactly once and keeps
  stable IDs; a missing step throws and leaves the envelope untouched (no half-apply); the validator
  reports gaps, duplicates and dead steps while accepting out-of-order registration.
* **ExplorationMapTests (1)** — `MapRevision` rises exactly once per committed map change, never on a
  rejected command or a repeated route selection, and on a restored save.

## Mutations

Each row reverts one safeguard; the named suite must fail.

| ID | Reverted safeguard | Killed by |
|---|---|---|
| M1 | bounds-safe `SurfaceTable.Get` | SimulationTests |
| M2 | §4.4 cross-fade (apply the new coefficient immediately) | SimulationTests |
| M3 | §5.1 15 s auto-stop | SimulationTests |
| M4 | migration gap failure | PersistenceTests |
| M5 | map revision bump on marker commit | ExplorationMapTests |
| M6 | `EditorApplication.Exit(1)` on a failed build | test_build_diagnostics.py |
| M7 | pinned Android target API level | test_build_diagnostics.py |
| M8 | workflow/ProjectSetup API-level agreement | test_build_diagnostics.py |

Two findings came out of writing this script, both fixed in the script rather than hidden:

1. `headless_cs.py test` runs the **previously compiled** assembly. The first run used `test`, so
   M1–M5 "survived" because nothing was recompiled; the script now runs `all`.
2. The first version of the batchmode-exit test passed on the **doc comment** that mentions
   `EditorApplication.Exit(1)`. The assertion now matches the call (`EditorApplication.Exit(1);`),
   which is why M6 is killed.

## Reproduce

```bash
python3 Tools/Build/headless_cs.py all
python3 Tools/Build/unity_type_check.py
python3 Tools/Build/test_build_diagnostics.py
python3 Tools/Build/test_unity_pipeline.py
python3 Tools/Validation/validate_content.py
python3 Tools/Validation/formula_checks.py
python3 Tools/Balance/economy_sim.py
python3 Docs/evidence/ai-processing-2026-10-20/mutation_check.py
```

## Limits

* The five changed Unity-only files (`VehicleController.cs`, `MapTextureBinding.cs`,
  `ProjectSetup.cs`, `BuildCommands.cs`, and the `MapPresenter` wiring) are **type-checked against
  stubs and parsed**, never compiled by Unity and never run. KI-034/KI-044.
* No PlayMode run proves the cross-fade feels smooth, that auto-stop produces a comprehensible cue,
  or that a marker appears on the commit frame on a device. KI-045/046/047.
* No APK, no AAB, no device measurement. The pinned Android target level (36) has not been proven to
  exist in the CI editor image. KI-006/KI-049.
