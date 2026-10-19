# Evidence — why three consecutive cloud builds could not be diagnosed (2026-10-19)

**What this batch is:** the reason runs **39139527909**, **39141218150** and **39141401948** all
ended with an unexplained `Build failed with exit code 1` was found, reproduced and fixed. It was
**not** a Unity problem — it was the diagnostic pipeline refusing to hand over Unity's log.

**What ran:** no Unity. This batch reads the GitHub Actions API (`api.github.com`, the only log
host reachable from the implementation sandbox) and executes the repository's own shell/Python
diagnostics against fixtures. **No APK exists, and the Unity root cause is still unknown**
(KI-041) — but the next dispatch will now name it instead of shrugging.

## 1. The finding, verbatim

All three diagnose jobs reported the same refusal twelve times each
(`escape-sequence-refusals.txt`, pulled from `check-runs/{id}/annotations`):

```
Could not download the log of job 117718330570 after 12 attempts: the response contains
terminal escape sequences; pass --allow-escape-sequences to output it anyway.
Needs actions: read; open the run in the Actions tab instead.
```

`gh api` guards raw non-JSON response bodies. A game-ci Unity job log is full of ANSI colour and
`\r` progress redraws, so the guard fired, `gh` wrote **nothing**, and `fetch_job_log.sh` — which
was written and tested against a permissive `gh` stub — treated it as a transient download error
and retried. D-035 built the right mechanism; it was defeated by a CLI safety check added later.

Verified against gh's own source (`cli/cli`, `pkg/cmd/api/api.go`), fetched through
`api.github.com`:

```go
cmd.Flags().BoolVar(&opts.AllowEscapeSequences, "allow-escape-sequences", false, "Allow printing terminal escape sequences")
...
// A raw non-JSON body is the only response the transport does not sanitize.
if !isJSON && !opts.AllowEscapeSequences && bodyWriter != io.Discard {
        err = iostreams.CopyGuardedContent(bodyWriter, responseBody, opts.IO.IsStdoutTTY())
        ...
        } else if errors.Is(err, iostreams.ErrEscapeSequence) {
                err = errors.New("the response contains terminal escape sequences; pass --allow-escape-sequences to output it anyway")
```

`bodyWriter != io.Discard` is the important part: **redirecting stdout to a file does not bypass
the guard**, which is why the workflow's `> error_logs/unity-job-log.txt` never received a byte.

## 2. What was fixed

| Change | File | Why |
|---|---|---|
| Probe `gh api --help` once and pass `--allow-escape-sequences` only when this `gh` supports it | `Tools/Build/fetch_job_log.sh` | On `gh` < 2.63 the flag is an unknown-flag *hard error*, so it must be detected, not assumed. Capability detection, not a version-string comparison |
| Always sanitise what is written: strip CSI/OSC sequences, lone `ESC` and `\r` | `Tools/Build/fetch_job_log.sh` | Escape sequences split the very lines the reporter greps (`error CS0117:`, `Aborting batchmode due to failure:`) and GitHub's workflow-command parser rejects `ESC` in annotation text |
| Write `<output>.excerpt` — the last 400 lines, ≤400 chars each | `Tools/Build/fetch_job_log.sh` | Annotations carry a few hundred characters; the artefact must carry the *end* of Unity's output, which is where a hard stop's cause is |
| Report the byte delta sanitising removed | `Tools/Build/fetch_job_log.sh` | If the sanitiser ever ate real content, the delta is the evidence |
| Derive the failure hint from the error text instead of always saying "Needs `actions: read`" | `Tools/Build/fetch_job_log.sh` | That blanket hint is what sent three investigations after a permission the workflow had already granted. An escape-sequence refusal now names the `gh` version; only a real 403/404/scope error mentions the token; anything else points at the Actions tab |
| Keep the verbatim download in a temp file, not beside the artefact | `Tools/Build/fetch_job_log.sh` | A failing Unity job log runs to tens of MB; uploading it twice buys nothing |
| New reporter section: crash / OOM-kill / fatal-error detection | `Tools/Build/report_unity_failure.sh` | These kill the editor process, so no C# `catch` runs and no breadcrumb is written — previously indistinguishable from "diagnostics found nothing". Patterns are deliberately narrow: Unity also logs benign `Received signal SIGUSR1` and `out of memory` retry lines |
| New reporter section: an *absent* breadcrumb is evidence | `Tools/Build/report_unity_failure.sh` | `EditorFailureLog` writes `Logs/wayfinder-editor-failure.log` before rethrowing, so no breadcrumb means no C# exception inside the build method — the cause is earlier (compile/package/licence/`-executeMethod`) or the process was killed. Only stated where Unity actually ran, never in the diagnose job's fresh checkout |
| Raw error-line grep widened (crash, OOM, licence, disk) and bounded (`head -400`, `cut -c1-400`); the log tail is appended to `apk-build-errors.log` | `.github/workflows/android-cloud-build.yml` | A failed Unity run emits tens of thousands of shader-compiler lines and this text is also tee'd into the job summary |

A bug found while writing the sanitiser, kept as a fixture because it is easy to reintroduce:
`[ -\/]` is **not** "escaped slash" — a backslash is not special inside a POSIX bracket expression,
so it means the range space..backslash and silently ate real log text, turning
`ESC[KAborting batchmode due to failure:` into `orting batchmode due to failure:`. The class is
`[ -/]` (space..slash, the CSI intermediate bytes). Mutation **P4** pins it.

## 3. What the run data says about the Unity failure itself

From `step-timings.txt` (Actions API) and `annotations-build-job-117718330570.json`:

| Observation | Evidence | What it rules in/out |
|---|---|---|
| The Unity step dies after **5.3–6.5 min** in all four recent runs | `step-timings.txt` | Unity really started and ran; this is not a preflight, licence-secret or image-pull failure |
| Four shader-compiler logs (~108–236 KB) survived; `buildPlatform=13` (Android) | build-job annotations | Package resolution and asset import were well under way — **KI-036's "1.20.1 may not resolve" is now unlikely** |
| **No** `Logs/wayfinder-editor-failure.log` in the workspace | build-job annotations list exactly four files | `ProjectSetup.Setup` did **not** throw a C# exception. Either it never ran, or the process was killed |
| No `error CS` line could be checked — the editor log was never retrieved | `escape-sequence-refusals.txt` | A compile error is neither confirmed nor excluded. It is the leading hypothesis (below) |

**Leading hypothesis (unproven):** KI-034 — `Wayfinder.Platform`, `Wayfinder.Presentation`,
`Wayfinder.Editor` and the two test assemblies have *never* been compiled by Unity. They sat behind
the `Unity.InputSystem` assembly that failed on `BuildTarget.ReservedCFE` (D-031). The 1.20.1 repin
removed that blocker, so these runs are the first in which Unity reaches them; a compile error there
aborts batchmode **before** `-executeMethod` runs, which is exactly the "no breadcrumb, dies in
minutes" shape above. The next dispatch turns this from a hypothesis into file/line/col annotations.

**Secondary finding (verified, not a cause):** the repository has exactly **one** cache entry
(`GET /actions/caches` → `game-ci-cli-v0.1.72-linux-x64`, 37,856,426 B). There is no `Library`
cache, and `Post Reuse Unity's import cache` is **skipped in every run**, because the pinned
`actions/cache@55cc834` declares `post-if: "success()"` — the cache is only saved when the whole
job succeeds. So every run pays a full cold import, and no warm cache is possible until a run
succeeds once. Left as-is deliberately (KI-042): saving a `Library` written by a failed import
would add a new variable to a diagnosis that is not finished.

## 4. Reproducing

```bash
python3 Tools/Build/test_build_diagnostics.py                       # 85 tests, OK
python3 Tools/Build/test_unity_pipeline.py                          # 23 tests, OK
python3 Docs/evidence/cloud-build-2026-10-19/mutation_check.py      # 18/18 mutations caught
bash    Docs/evidence/cloud-build-2026-10-19/dress_rehearsal.sh     # all checks passed
```

The dress rehearsal is the strongest evidence here: it runs the fetcher **as it was at base commit
`8611c6c`** against a `gh` stub that reproduces gh ≥ 2.63's guard, shows it fail with the exact
reported refusal and leave no file, then runs the current fetcher against the same stub and shows
the reporter turn the result into a `file=…,line=…,col=…` annotation. The Unity log inside the stub
is **synthetic** — an illustrative `error CS1061` in `MapOverlayView.cs`, chosen because it is the
shape the leading hypothesis predicts. **It is not evidence about the real failure.**

## 5. Files

| File | Contents |
|---|---|
| `escape-sequence-refusals.txt` | The refusal, verbatim, from all three diagnose jobs' annotations |
| `annotations-build-job-117718330570.json` | Build job annotations of run 39141401948: the four shader logs, "no editor log was inspected", `Build failed with exit code 1` |
| `annotations-diagnose-job-117720307068.json` | Diagnose job annotations of the same run, including the refusal |
| `run-39141401948-jobs.json` | Raw job/step records for run 39141401948 |
| `step-timings.txt` | Step durations for the four most recent Setup-Project failures |
| `dress_rehearsal.sh`, `dress-rehearsal.log` | The reproducible before/after rehearsal and its output |
| `mutation_check.py`, `mutation-check.log` | 16 mutations, each reverting one safeguard above; all caught |
| `diagnostics-tests.log`, `pipeline-tests.log` | 88/88 and 23/23 tests |
| `workflow-validation.log` | Both workflows parsed as YAML; all six multiline `run` blocks and four helper scripts pass `bash -n` |

## 6. What this does **not** prove

- **No APK, no scene generation, no PlayMode run, no device result.** M0/M1/M2 gates unchanged.
- The Unity root cause is still unknown. Nothing here has run in CI yet: the fix is proven against
  a faithful stub and gh's own source, not against a live Unity job log.
- `packages-lock.json` is still not committed (KI-002) — it is in the `Unity-logs` artefact of
  every run, on a host this sandbox cannot reach.
