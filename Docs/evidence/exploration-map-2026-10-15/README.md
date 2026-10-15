# Evidence — exploration map (Batch D, 2026-10-15)

Headless evidence for the fog-of-war exploration map. **Nothing here is a Unity, device or build
result.** The Unity views are syntax-checked only (KI-031).

| File | What it is |
|---|---|
| `headless-console.log` | Full `python3 Tools/Build/headless_cs.py all` console (run with `WAYFINDER_MAP_SNAPSHOT_DIR` set): 7 assemblies / 57 files compile with **0 errors, 0 warnings**; **130/130** EditMode tests; the .NET Standard 2.1 profile check passes; 29 Unity-dependent files parse. `EXIT=0`. |
| `EditMode.headless.xml` | NUnit3 result XML from that run. |
| `compile-report.json` | Per-assembly compile report from that run. |
| `mutation_check.py`, `mutation-check.log` | 11 deliberate regressions to map rules, applied one at a time. **11/11 are caught** by the suite (`MUTATION CHECK: ALL CAUGHT`). |
| `mutation-check-first-run-gap.log` | The first run of the check: 10/11 caught. *"Survey-only changes never redrawn"* survived because a vista also revealed cells next to the hill, and that neighbour redraw hid the missing redraw. The new test `SurveyOfGroundAlreadyDrivenAround_StillRedrawsContours_S8_1` closes the gap. |
| `snapshots/*.png` + `*.json` | The scripted exploration in `ExplorationMapTests.ScriptedExploration_FillsTheSheetStageByStage_AndSurvivesReload_Evidence`, rendered by the real `ExplorationMap` raster (2 m/px, north up). The test writes raw RGBA; `Tools/Validation/rgba_to_png.py` converts it. The marker glyphs are a **test-only compositor standing in for the Unity icons**, not the shipped presentation. |
| `snapshots/zoom-hill-contours.png` | 5× crop of the 8 m hill after the Firewatch Nine survey. Contours cover only the west cells: the east cells sit behind the crest as seen from the vista (§8.1 occlusion), so they are revealed but not surveyed. |
| `scratch-unity-stub-typecheck/` | **Scratch, not evidence.** Type-checks the 8 Unity glue files, including the 4 new map views and the modified `VerticalSliceDriver`, against the *real* compiled Wayfinder assemblies plus hand-written UnityEngine stubs: 0 errors, 0 warnings, and 3/3 negative controls are caught. A pass cannot detect real Unity API drift. |

## Scripted exploration (from the snapshot JSON)

| Stage | Sheet mapped | Revealed cells (mainland) | Surveyed cells | What happened |
|---|---|---|---|---|
| 00-new-game | 0.0 % | 0 | 0 | Blank sheet: home garage, you, and the Q01 search circle only |
| 01-first-drive | 6.6 % | 17 | 0 | Drove south, then west toward Ranger Rise at 36 km/h (96 m reveal at 2 Hz) |
| 02-ranger-rise-vista | 43.4 % | 112 | 112 | Stopped < 15 km/h for 2 s, so the landmark is discovered and the vista surveys 384 m |
| 03-ford-camp-relay | 52.0 % | 134 | 112 | Drove across the ford, past camp A, to the relay |
| 04-firewatch-vista | 68.4 % | 176 | 165 | Second vista; the hill now shows contour lines |
| 05-home | 68.4 % | 176 | 165 | Stopped in the arrival volume, so the expedition was extracted; after reload the sheet is byte-identical |

Fog share per stage (from the test log): 1.000, 0.943, 0.587, 0.509, 0.346, 0.346.

## Reproduce

```bash
python3 Tools/Build/headless_cs.py compile && python3 Tools/Build/headless_cs.py test   # 130/130
WAYFINDER_MAP_SNAPSHOT_DIR=/tmp/map python3 Tools/Build/headless_cs.py test && python3 Tools/Validation/rgba_to_png.py /tmp/map
python3 Docs/evidence/exploration-map-2026-10-15/mutation_check.py                       # ~4 min, edits + restores sources
python3 Docs/evidence/exploration-map-2026-10-15/scratch-unity-stub-typecheck/run_stub_typecheck.py
```

Note: `headless_cs.py test` runs the **last compiled** assemblies. Always run `compile` first, or
use `all`, after editing sources.
