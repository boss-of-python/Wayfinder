# Batch N evidence — `ApplyEffectiveStats` seam (KI-025, D-044), 2026-10-21

Executed in this environment (headless, no Unity):

| Check | Command | Result |
|---|---|---|
| C# compile (Roslyn 3.9, C# 9, `-warn:4`) | `python3 Tools/Build/headless_cs.py compile` | OK — 0 errors, 0 warnings (`compile-report.json`) |
| EditMode NUnit suites | `python3 Tools/Build/headless_cs.py test` | **278/278 passed** (`EditMode.headless.xml`), incl. 12 new `WheelRigMathTests` |
| .NET Standard 2.1 profile | `python3 Tools/Build/headless_cs.py profile` | OK — Simulation now 8 pure files |
| Unity-file syntax | `python3 Tools/Build/headless_cs.py syntax` | 30 files, 0 syntax errors (not type-checked) |
| Content / formulas / economy | `validate_content.py`, `formula_checks.py`, `economy_sim.py` | all PASS |
| Mutation check | `python3 Docs/evidence/rig-stats-seam-2026-10-21/mutation_check.py` | **8/8 caught** (`mutation-check.log`); first run 7/8 — N6 survivor fixed by isolating travel/clearance (D-044) |

Not proven here: `VehicleController.ApplyEffectiveStats` executing inside Unity, any
WheelCollider behaviour, spring/friction calibration, center-of-mass solve, winch pull
behaviour, or the M1/M2 device gates. Everything above is engine-free math executed
outside Unity (KI-016/KI-026 still apply).
