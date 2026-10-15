# Evidence — field supplies, selling, research → physics (Batch D continuation, 2026-10-15)

Headless evidence only. **Nothing here is a Unity, device or build result.** The supplies overlay,
garage Sell buttons and the `VehicleController`/driver resolver wiring are syntax-checked only,
plus the scratch stub type-check.

| File | What it is |
|---|---|
| `headless-console.log` | `python3 Tools/Build/headless_cs.py all`: 7 assemblies / 60 files compile with **0 errors, 0 warnings**; **143/143** EditMode tests (130 → 143: 9 `CampaignSuppliesTests`, 4 `CampaignVehicleStatsTests`); the .NET Standard 2.1 profile check passes; 29 Unity-dependent files parse. `EXIT=0`. |
| `EditMode.headless.xml`, `compile-report.json` | Raw NUnit3 XML and compile report from that run. |
| `mutation_check.py`, `mutation-check.log` | 13 rule breaks or defect re-introductions (fuel-can waste/1 L rule, repair while moving or at 100, supplies consuming nothing, tire patch over-repair, sell price rounding, selling without the critical save, mission camp-kit sale, resolver double-apply, both KI-032 defects, Trail Tires on specialized sets). **13/13 caught.** |
| `stub-typecheck.log` | **Scratch, not evidence:** the 8 Unity glue files (driver with the supplies overlay and Sell buttons, plus the map views) type-check against the real Wayfinder assemblies and stubs: 0 errors, 3/3 negative controls caught. |
| `python-checks.log` | Content validator, formula checks, 23 runner unittests, `rgba_to_png --self-test`, `git diff --check`: all pass. |

Defect found by this work: **KI-032**. Trail Tires I's `DirtGravelGrip` modifier was never composed,
and Engine Tune's `PeakTorqueNm` never reached `Powertrain`, so both upgrades were bought and shown
but did nothing. Both are fixed (D-030) and pinned by regression tests.
