# Evidence — why three consecutive cloud builds could not be diagnosed (2026-10-19)

**What this batch is:** two halves of the same five-run failure, landed together.

1. **Why it could not be diagnosed** — runs **39139527909**, **39141218150** and **39141401948**
   each ended with an unexplained `Build failed with exit code 1` because the diagnostic pipeline
   refused to hand over Unity's log (§1). That was not a Unity problem.
2. **What Unity was actually failing on** — PR #15, merged while this batch was in flight, found a
   concrete compile error: `ContentImporter.cs` uses `Newtonsoft.Json.Linq` but
   `Wayfinder.Editor.asmdef` did not reference `Unity.Newtonsoft.Json`, and Unity asmdefs require
   *direct* references (§3). That aborts batchmode before `-executeMethod`, which is exactly the
   shape every run showed.

The two fixes are complementary, not alternatives: PR #15's `curl`-first download gets the bytes
past gh's guard, and this batch makes those bytes **parsable** — verbatim is not enough (§2, §5).
They were merged, and the merge is what this directory now documents.

**What ran:** no Unity. This batch reads the GitHub Actions API (`api.github.com`, the only log
host reachable from the implementation sandbox) and executes the repository's own shell/Python
diagnostics against fixtures. **No APK exists.** The compile error is identified from source and
still unverified by a real editor run; other errors may hide behind it (KI-034). The next dispatch
either builds or reports why in file/line/col.

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
| Download through a chain: `curl` → `gh api … --allow-escape-sequences` → plain `gh api` | `Tools/Build/fetch_job_log.sh` | **PR #15's fix, kept.** curl writes bytes verbatim and is immune to gh's guard; the flagged `gh` call covers environments with no token/curl; the plain call covers `gh` < 2.63, where the flag is an unknown-flag *hard error* |
| Sanitise whatever arrives: strip CSI/OSC sequences, lone `ESC` and `\r` | `Tools/Build/fetch_job_log.sh` | **The half PR #15 was missing.** Verbatim is not parsable: game-ci colourises the lines that matter, so a raw log holds `…cs(112,44): ESC[31merror CS0234: …ESC[0m CR` and the reporter's `): error CS[0-9]+:` grep does not match across the escape. Proven in §5 |
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
| No `error CS` line could be checked — the editor log was never retrieved | `escape-sequence-refusals.txt` | A compile error was neither confirmed nor excluded from the logs. It was confirmed from the **source** instead (below) |

**Root cause, identified from source (PR #15, merged mid-batch):** KI-034 was right about the
*shape*. `Wayfinder.Platform`, `Wayfinder.Presentation`, `Wayfinder.Editor` and the two test
assemblies had never been compiled by Unity — they sat behind the `Unity.InputSystem` assembly that
failed on `BuildTarget.ReservedCFE` (D-031), and the 1.20.1 repin removed that blocker, so these
runs are the first in which Unity reaches them. In `Wayfinder.Editor`,
`Assets/Wayfinder/Scripts/Editor/ContentImporter.cs` uses `Newtonsoft.Json.Linq` at six places
(`:112`, `:135`, `:162`, `:171`, `:181`, `:204`) while `Wayfinder.Editor.asmdef` referenced only
`Unity.RenderPipelines.Universal.Runtime`. Unity asmdefs require **direct** references, so that is
a CS0234 in an Editor-only assembly — which aborts batchmode **before** `-executeMethod` runs and
therefore leaves no `EditorFailureLog` breadcrumb. Every observation above is consistent with it,
and PR #15 adds the missing reference.

This is reasoned from source, **not executed by Unity**: no editor here can confirm the compile,
and a second error may hide behind the first. What makes that acceptable is that the next dispatch
now reports either an APK or a `file=…,line=…,col=…` annotation.

**Secondary finding (verified, not a cause):** the repository has exactly **one** cache entry
(`GET /actions/caches` → `game-ci-cli-v0.1.72-linux-x64`, 37,856,426 B). There is no `Library`
cache, and `Post Reuse Unity's import cache` is **skipped in every run**, because the pinned
`actions/cache@55cc834` declares `post-if: "success()"` — the cache is only saved when the whole
job succeeds. So every run pays a full cold import, and no warm cache is possible until a run
succeeds once. Left as-is deliberately (KI-042): saving a `Library` written by a failed import
would add a new variable to a diagnosis that is not finished.

## 4. Reproducing

```bash
python3 Tools/Build/test_build_diagnostics.py                       # 91 tests, OK
python3 Tools/Build/test_unity_pipeline.py                          # 23 tests, OK
python3 Docs/evidence/cloud-build-2026-10-19/mutation_check.py      # 19/19 mutations caught
python3 Docs/evidence/cloud-build-2026-10-16/mutation_check.py      # 22/22, N10 retargeted
bash    Docs/evidence/cloud-build-2026-10-19/dress_rehearsal.sh     # 20/20 checks passed
```

The dress rehearsal is the strongest evidence here, and it has six parts: it runs the fetcher **as
it was at base commit `8611c6c`** against a `gh` stub faithful to gh ≥ 2.63's guard (reproduces the
exact reported refusal, leaves no file); then the current fetcher over **both** download paths
(`gh` with no token, `curl` with one) and checks each output for surviving `ESC`/`\r`; then a
**control** that hands the reporter the same bytes unsanitised; then the reporter over the sanitised
log.

The control is the part that justifies merging instead of choosing. Unsanitised, the reporter loses
the `file=…,line=…,col=…` compile-error annotation but **still** reports
`Aborting batchmode due to failure: Scripts have compiler errors.` — because that line carries no
escapes. A verbatim download therefore looks diagnosed while the one thing worth knowing is
missing, which is precisely how five runs stayed unexplained. Same result as a unit test:
`test_the_curl_download_is_sanitised_too` and its control
`test_a_raw_unsanitised_log_would_miss_the_compile_error`.

The Unity log inside the stubs is **synthetic**: the real CS0234 text at the real
`ContentImporter.cs(112,44)` location, wrapped in the ANSI colour and `\r` a real job log carries.
It reproduces the *shape* of the failure; **it is not a captured Unity log.**

## 5. Files

| File | Contents |
|---|---|
| `escape-sequence-refusals.txt` | The refusal, verbatim, from all three diagnose jobs' annotations |
| `annotations-build-job-117718330570.json` | Build job annotations of run 39141401948: the four shader logs, "no editor log was inspected", `Build failed with exit code 1` |
| `annotations-diagnose-job-117720307068.json` | Diagnose job annotations of the same run, including the refusal |
| `run-39141401948-jobs.json` | Raw job/step records for run 39141401948 |
| `step-timings.txt` | Step durations for the four most recent Setup-Project failures |
| `dress_rehearsal.sh`, `dress-rehearsal.log` | The reproducible before/after rehearsal and its output |
| `mutation_check.py`, `mutation-check.log` | 19 mutations, each reverting one safeguard above (including PR #15's curl preference and the sanitiser that makes it useful); all caught |
| `diagnostics-tests.log`, `pipeline-tests.log` | 91/91 and 23/23 tests |
| `workflow-validation.log` | Both workflows parsed as YAML; all six multiline `run` blocks and five scripts pass `bash -n` |

## 6. What this does **not** prove

- **No APK, no scene generation, no PlayMode run, no device result.** M0/M1/M2 gates unchanged.
- The CS0234 in `ContentImporter.cs` is read from source, not compiled by Unity. It is certainly *a*
  build-killer; whether it is the *only* one is exactly what the next dispatch answers, and the
  other three Unity-facing assemblies (`Platform`, `Presentation`, both test assemblies) are still
  uncompiled by Unity (KI-034).
- Nothing here has run in CI yet: the fetch path is proven against faithful stubs and gh's own
  source, not against a live Unity job log.
- `packages-lock.json` is still not committed (KI-002) — it is in the `Unity-logs` artefact of
  every run, on a host this sandbox cannot reach.
