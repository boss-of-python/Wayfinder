# Evidence — run 38875757162 failed *and* said nothing (cloud build, 2026-10-16)

**What ran:** workflow `Android APK (free cloud build)`
(`.github/workflows/android-cloud-build.yml`), run
[**38875757162**](https://github.com/boss-of-python/Wayfinder/actions/runs/38875757162) on `main`
at commit `45e4dbe` (the merge of PR #10), dispatched by the owner on 2026-10-16 08:44:58 UTC.
Job `Build Wayfinder-dev.apk` (`116890311358`) failed after 8 m 38 s at step
*Unity 1 of 2: Setup Project*; step 2 was skipped and no APK exists.

**Provenance:** this environment cannot reach the Actions raw-log host or the artifact storage
host (both resolve but refuse the connection), so only the GitHub REST API on `api.github.com`
was available. Everything below comes from that API (`run-38875757162.json`,
`annotations-38875757162.json`, both verbatim) plus the published source of the build tooling
(`gameci-unity-invocation.txt`, fetched from `codeload.github.com`). Nothing here is a guess
about what Unity printed: where the Unity cause is unknown, this file says so.

## 1. What the annotations said

The entire failed build produced **two** annotations:

| Level | Message |
|---|---|
| failure | `Build failed with exit code 1` (job log line 7580) |
| notice | `"The ubuntu-latest label will migrate to Ubuntu 26 beginning October 19, 2026. For more information, see https://github.com/actions/runner-images/issues/14748"` |

The first is GameCI's generic banner — the very message `Tools/Build/report_unity_failure.sh`
(D-033) exists to replace. The second is GitHub's own runner-image warning.

**There was no diagnostic annotation at all**, not even the script's "No Editor.log left in the
workspace" notice, which run 38858427636 *did* produce. The step
*Report the real Unity failure as annotations* ran and exited 0 in under a second.

## 2. Why the diagnostics were silent (root cause)

| Observation | Source |
|---|---|
| game-ci launches `unity-editor -logfile /dev/stdout ... $CUSTOM_PARAMETERS` — its own `-logfile` comes first, the workflow's `customParameters` last | `gameci-unity-invocation.txt` (game-ci/cli v0.1.72, `dist/platforms/ubuntu/steps/build.sh:161-181`) |
| Unity's editor log therefore goes to the container's stdout, i.e. **the job log** — in run 38754979851 the editor banner, the compile error and the batchmode abort were all in that step's 1,996 printed lines | `Docs/evidence/cloud-build-2026-10-15/unity-setup-failure-38754979851.log` |
| Run 38875757162's failing annotation sits at job-log line **7580**, nearly four times longer than that run: Unity again printed to stdout, and got much further (the 1.20.1 Input System pin did not stop it at line ~1865) | `annotations-38875757162.json` |
| So `Logs/Unity-Setup.log` was never written. The reporter still found *some* `Logs/*.log` — Unity's own `AssetImportWorker*.log` / shader-compiler logs, which exist after any import — so it did not print the "no log" notice either, and none of its patterns matched their contents: **zero annotations** | `report_unity_failure.sh` globs `Logs/*.log`; the `Unity-logs` artifact for this run is 30,699 bytes of exactly such files |

In short: the diagnostics were reading the wrong files, and had no way of saying so. A log that
exists but explains nothing was indistinguishable from a healthy build.

**Not the cause:** a build method that produces no artefact does not fail on its own — the CLI
pre-creates the build directory and takes the step's exit code from Unity
(`gameci-unity-invocation.txt`, note 1). The failing exit code came from Unity itself, during
`ProjectSetup.Setup` or the compilation of the four assemblies that reach Unity for the first
time (KI-034). **Which of the two is still unknown**, and this batch deliberately does not guess:
it makes the next run say so.

## 3. What changed (D-035)

| Change | Effect |
|---|---|
| New `diagnose` job (`needs: apk`, `if: always()`, `actions: read`) runs `Tools/Build/fetch_job_log.sh "Build Wayfinder-dev.apk"` | Downloads the finished build job's plain-text log — Unity's real editor log — from the Actions API, then reruns the reporter over it. Also uploaded as the `Unity-job-log` artifact |
| `report_unity_failure.sh` is **never silent** | Always annotates which files it inspected and their sizes; says loudly when none of them is an editor log; and when a failure matches no pattern, publishes a 40-line log tail as an annotation |
| It also recognises an unresolvable `-executeMethod`, any `*.Exception:` line, and this project's own failure breadcrumb | Covers the "Setup threw" and "Editor assembly did not compile" cases |
| The reporter is told how the Unity steps ended (`UNITY_SETUP_OUTCOME` / `UNITY_BUILD_OUTCOME` / `UNITY_JOB_RESULT`) | A failure whose logs say nothing can no longer look like a success |
| New `Assets/Wayfinder/Scripts/Editor/EditorFailureLog.cs`; `ProjectSetup.Setup` and the four `BuildCommands` entry points run through it | An exception in an `-executeMethod` entry point is appended to `Logs/wayfinder-editor-failure.log` (a workspace file, uploaded in `Unity-logs`) and rethrown, so Unity still fails the build |
| The dead `customParameters: -logFile ...` is removed; `runs-on` pinned to `ubuntu-24.04` | No advertised diagnostic path that does not work; the 2026-10-19 `ubuntu-latest` → Ubuntu 26 migration cannot change the host OS under a 9 GB Unity build without a commit |

## 4. Verification run here (no Unity, no Docker, no network to Unity)

| Check | Result | File |
|---|---|---|
| `python3 -m unittest discover -s Tools/Build -p 'test_*.py'` | **86/86** (63 diagnostics + 23 pipeline; was 62) | `diagnostics-tests.log` |
| `python3 Docs/evidence/cloud-build-2026-10-16/mutation_check.py` | **17/17** mutations caught | `mutation-check.log` |
| `python3 Tools/Build/headless_cs.py all` | compile OK, **164/164** EditMode tests, .NET Standard 2.1 profile OK, **30** Unity files parse (29 + `EditorFailureLog.cs`) | `headless-cs-summary.log` |
| Dress rehearsal: the new `diagnose` path over a **real** Unity job log (the 2026-10-15 excerpt) | reproduces the known cause — `InputSystemPluginControl.cs(47,25): error CS0117 'BuildTarget' … 'ReservedCFE'`, `Scripts have compiler errors`, the `CS0414` warning — as annotations, exit 0 | `dress-rehearsal.log` |

The 2026-10-15 mutation check still runs, but two of its nine mutations
(`M4` secret message, `M6` diagnostics `if:`) went **stale** when PR #10 rewrote those exact
lines; it now reports 7/9 + 2 stale. The script in this folder supersedes it and re-pins both
behaviours (N5, N14 and the workflow assertions).

## 5. What this does **not** prove

- **No APK, and no Unity result of any kind.** Nothing here was executed by Unity; the new job
  and the breadcrumb have never run in CI.
- The cause of run 38875757162's Unity failure is **still unknown** (KI-041). It is readable from
  the next dispatch without a log paste.
- `Packages/packages-lock.json` is still not committed (KI-002).

## 6. Next run — read it with

```bash
run=<run id>
gh api repos/boss-of-python/Wayfinder/actions/runs/$run/jobs --jq '.jobs[] | "\(.id) \(.name) \(.conclusion)"'
# "Explain the Unity result" carries the parsed cause; "Build Wayfinder-dev.apk" carries GameCI's banner
gh api repos/boss-of-python/Wayfinder/check-runs/<job id>/annotations \
  --jq '.[] | "[\(.annotation_level)] \(.title): \(.message)"'
```
