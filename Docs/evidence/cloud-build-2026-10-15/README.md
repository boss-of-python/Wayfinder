# Evidence — first real Unity run of this repository (cloud build, 2026-10-15)

**What ran:** GitHub Actions workflow `Android APK (free cloud build)`
(`.github/workflows/android-cloud-build.yml`), run **38754979851** on `main` at commit
`61cfc2e`, dispatched manually by the repository owner on 2026-10-15 14:50 UTC. Job
`Build Wayfinder-dev.apk` failed at step *Unity 1 of 2: Setup Project* in 4 m 07 s.

**Provenance:** this environment has no Unity, no Docker and no access to the Actions log
host, so it could not run or read this build. The owner supplied the complete step logs; the
excerpt committed here is verbatim from that log except for one redaction (see below). The
GitHub API independently confirms the run id, the failing step, its duration and the
annotation `Build failed with exit code 1`.

## What this run proves

| Fact | Where in the log |
|---|---|
| The GameCI image for `6000.3.26f1` + Android exists and starts (`unityci/editor`, `--containerRegistryImageVersion=3`) | CLI invocation, lines 1–42 |
| The repository secrets activate a **Unity Personal license** — KI-006's "no license" assumption is gone | `Requesting activation (personal license via Unity account)`, `Successfully updated license` |
| The editor really is **6000.3.26f1 (`de8167e7440c`)**, Linux batchmode — the pin in `ProjectSettings/ProjectVersion.txt` is now verified, not provisional | `Unity Editor version:    6000.3.26f1 (de8167e7440c)` |
| Every package pinned in `Packages/manifest.json` **resolved**; there was no registry failure | 50+ `Library/PackageCache/...` paths |
| Unity **compiled all six engine-free Wayfinder assemblies** (`Core`, `Campaign`, `Simulation`, `World`, `Progression`, `Persistence`) — including `VehicleController` and `ChaseCameraBehaviour`, which until now were only parse-checked | `Csc Library/Bee/artifacts/.../Wayfinder.*.dll` |
| Exactly **one warning** in all of `Assets/Wayfinder` (now fixed) | `ChaseCameraBehaviour.cs(148,14): warning CS0414` |
| The build aborted on exactly **one compile error, inside Unity's own Input System package** — not in Wayfinder code | `InputSystemPluginControl.cs(47,25): error CS0117: 'BuildTarget' does not contain a definition for 'ReservedCFE'` |

The cause: `com.unity.inputsystem` **1.12.0** references `BuildTarget.ReservedCFE`, which
Unity 6000.3 removed. Verified against the upstream package source
(`Unity-Technologies/InputSystem`): the reference is replaced by the literal
`(BuildTarget)49` from **1.13.1** onwards. The pin is now **1.20.1** (D-031).

## What this run does **not** prove

- **No APK exists.** The build never reached step 2; `actions/upload-artifact` never ran and
  the repository has zero artifacts.
- `Wayfinder.Platform`, `Wayfinder.Presentation`, `Wayfinder.Editor` and the PlayMode/EditMode
  test assemblies were **never compiled**: they reference `Unity.InputSystem`, whose assembly
  failed to build, so Unity aborted before reaching them (KI-034).
- `ProjectSetup.Setup` never executed. Scene generation, the URP asset, quality tiers, the
  input backend switch and the `Resources/Content` copy are all still unverified (KI-026).
- No PlayMode test, no device install, no frame-time or thermal measurement. The M0/M1/M2
  gates are unchanged.

## Redaction

GitHub masked `UNITY_EMAIL` / `UNITY_PASSWORD` / `UNITY_LICENSE` as `***` in the original
log. Unity itself printed a truncated access-token prefix
(`Successfully updated the access token 5r-ZUJx…`); that prefix is replaced with
`[REDACTED]` here. Nothing else was altered.

## Files

| File | Contents |
|---|---|
| `unity-setup-failure-38754979851.log` | Curated 230-line excerpt of the 1,996-line failing step, with the original line ranges marked |
| `diagnostics-tests.log` | `python3 Tools/Build/test_build_diagnostics.py` → 33 tests, OK |
| `mutation-check.log` | `python3 Docs/evidence/cloud-build-2026-10-15/mutation_check.py` → 9/9 mutations caught |
| `mutation_check.py` | The reproducible mutation check |
| `headless-cs-summary.log` | `python3 Tools/Build/headless_cs.py all` after the fixes: compile OK, **143/143** tests, profile OK, 29 Unity files parse |
| `EditMode.headless.xml`, `compile-report.json` | Raw NUnit results and the Roslyn compile report (7 assemblies, 0 warnings) from that run |

## Reproduce the local half

```bash
python3 Tools/Build/test_build_diagnostics.py            # 33 tests
python3 Docs/evidence/cloud-build-2026-10-15/mutation_check.py   # 9/9
python3 Tools/Build/headless_cs.py all                   # 143/143 EditMode tests
python3 Tools/Build/pin_toolchain.py --editor-version 6000.3.26f1  # idempotent: leaves the committed pin alone
```

The Unity half needs one `workflow_dispatch` run of *Android APK (free cloud build)* by the
owner. With `Tools/Build/report_unity_failure.sh` in place, the next failure reports its real
cause as check-run annotations, readable from
`GET /repos/boss-of-python/Wayfinder/check-runs/{job_id}/annotations` without downloading the
raw log.
