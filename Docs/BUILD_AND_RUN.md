# Wayfinder — Build & Run

Status legend used here: **VERIFIED** (ran in this repo/environment) vs **UNVERIFIED** (requires a tool this environment does not have; command is exact and ready to run).

---

## Phone-controlled build route

For a Moto G45, see [MOTO_G45_BUILD_AND_TEST.md](MOTO_G45_BUILD_AND_TEST.md). You
still need an installed/licensed Unity desktop (borrowed or cloud); the phone can
control it and install its APK. The optional workflow is manual and self-hosted,
not a GitHub-hosted editor or a phone-only compiler. The latest headless domain suite
passes 205/205 tests (20 Oct 2026 triage batch); older counts below are historical snapshots.

**Android API levels are pinned in code, not left on Auto.** `ProjectSetup` writes
`minSdkVersion = 29` (blueprint OS floor: Android 10+) and `targetSdkVersion = 36`, and prints both
into the setup log. The workflow passes the same target level to unity-builder and a repository test
fails if the two disagree. The JDK, NDK and SDK build tools come from the pinned editor image, so
they follow `UNITY_VERSION`; a host `setup-java` step would not change the JDK Unity uses. If the
image lacks API 36, the build fails with Unity's own "SDK not installed" error — that is the intended
loud failure (KI-049).

## Free cloud build route (GameCI, GitHub-hosted runner)

**Executed, but no APK yet.** Five owner dispatches on 2026-10-15 reached Unity and exposed the
Input System 1.12.0 `BuildTarget.ReservedCFE` compile error; the manifest was repinned to 1.20.1.
The three latest runs
([39139527909](https://github.com/boss-of-python/Wayfinder/actions/runs/39139527909),
[39141218150](https://github.com/boss-of-python/Wayfinder/actions/runs/39141218150),
[39141401948](https://github.com/boss-of-python/Wayfinder/actions/runs/39141401948), 2026-10-18,
current `main`) all passed preflight and all failed in *Unity 1 of 2: Setup Project* after 5.3–6.5
minutes with `Build failed with exit code 1`; the APK step was skipped each time. Each diagnose job
then reported the same thing twelve times: `the response contains terminal escape sequences; pass
--allow-escape-sequences to output it anyway`. That was the real blocker — **`gh api` refuses a raw
log body containing ANSI colour and `\r` redraws, and redirecting stdout does not bypass it**, so
Unity's log never reached the reporter (KI-041). Fixed on 2026-10-19 (D-036): the fetcher probes
`gh api --help`, passes the flag when the installed `gh` has it, sanitises escape sequences out of
the log it saves, writes a 400-line `unity-job-log.txt.excerpt` tail into the artefact, and reports
how many bytes it stripped. The reporter now also names a crash/OOM kill, and says so when
`Logs/wayfinder-editor-failure.log` is *absent* — which means no C# exception was thrown inside the
build method.

**The Unity failure itself is still unknown** (KI-041). What the run data does establish: Unity ran
for minutes, four shader-compiler logs survived (so packages resolved — KI-036 narrows), and no
breadcrumb exists, so the cause is earlier than `ProjectSetup.Setup` or the process was killed. The
leading suspect is KI-034: the 1.20.1 Input System repin unblocked compilation, so these are the
first runs in which Unity reaches `Wayfinder.Platform`, `Wayfinder.Presentation`,
`Wayfinder.Editor` and the test assemblies — never compiled by Unity, only parse-checked. **One
dispatch now names it** as `file=…,line=…,col=…` annotations. See
`Docs/evidence/cloud-build-2026-10-19/` for the refusals, step timings and the before/after dress
rehearsal. This builds a development APK on GitHub's machines; no local Unity install or
self-hosted runner is required.

Run it: **Actions tab → "Android APK (free cloud build)" → Run workflow** (manual only —
`workflow_dispatch`, never on `pull_request`). You can pick the branch; the agent cannot
dispatch it, because its token has no `actions: write` (KI-035).

What the five runs proved (`Docs/evidence/cloud-build-2026-10-15/`, run 38754979851):

| Proven by a real run | Detail |
|---|---|
| License | The repository secrets activate a **Unity Personal license** — no paid seat, no manual `.ulf` |
| Image | `unityci/editor` for **6000.3.26f1** + Android (image version 3) exists, pulls and starts |
| Packages | Every `Packages/manifest.json` pin resolved from the real registry |
| Compile | Unity compiled **all six engine-free Wayfinder assemblies**, one warning (fixed) |
| Failure | `com.unity.inputsystem@1.12.0` → `error CS0117: 'BuildTarget' does not contain a definition for 'ReservedCFE'` — Unity 6000.3 removed that enum member. Repinned to **1.20.1** (D-031) |

What it does, in order:

| Step | Action | Result |
|---|---|---|
| 1 | `actions/checkout` (`persist-credentials: false`) | source at the selected ref |
| 2 | Pre-create `error_logs/apk-build-errors.log` | creates the destination and records the run, attempt and commit before preflight |
| 3 | `bash Tools/Build/check_unity_secrets.sh` | requires either `UNITY_LICENSE` or both account secrets; prints no values and runs before expensive setup |
| 4 | `python3 Tools/Build/pin_toolchain.py --editor-version "$UNITY_VERSION"` | **verifies** the committed `ProjectSettings/ProjectVersion.txt` still matches (idempotent, D-032); nothing is committed |
| 5 | `jlumbroso/free-disk-space` | frees disk for the ~9 GB Unity Android image |
| 6 | `mkdir -p Library Logs Builds/Logs` | prepares the Unity cache and workspace log directories |
| 7 | `actions/cache` | reuses the `Library` import cache between runs |
| 8 | GameCI `unity-builder` → `Wayfinder.Editor.ProjectSetup.Setup` | generates scenes, URP asset, quality tiers, player settings, campaign `Resources/Content` |
| 9 | GameCI `unity-builder` → `Wayfinder.Editor.BuildCommands.BuildAndroidDev` | `Builds/Android/Wayfinder-dev.apk` (unsigned development build) |
| 10 | `bash Tools/Build/report_unity_failure.sh` (only if Unity was attempted) | annotates whatever survived in the workspace and tees the diagnostic output into `error_logs/apk-build-errors.log` |
| 11 | Record Unity step outcomes | appends setup/build outcomes to `error_logs/apk-build-errors.log`, including if Unity never ran |
| 12 | `actions/upload-artifact` (logs) | `Unity-logs` (workspace logs, `error_logs/`, and **`Packages/packages-lock.json`**), 7-day retention |
| 13 | `actions/upload-artifact` (APK) | `Wayfinder-dev-APK`, 7-day retention; fails if the APK is missing |

Then a second job, **Explain the Unity result** (`needs: apk`, `if: always()`), does the part that
matters after a failure:

| Step | Action | Result |
|---|---|---|
| 1 | `bash Tools/Build/fetch_job_log.sh "Build Wayfinder-dev.apk" error_logs/unity-job-log.txt 12 10` | downloads the finished build job's log — **this is Unity's editor log**, because game-ci runs `unity-editor -logfile /dev/stdout` (D-035). It passes `--allow-escape-sequences` when the installed `gh` supports it (gh ≥ 2.63 refuses raw bodies otherwise, D-036), strips escape sequences and `\r`, writes the 400-line tail to `error_logs/unity-job-log.txt.excerpt`, and prints how many bytes it stripped. Its transcript is saved to `error_logs/job-log-download.txt` |
| 2 | `bash Tools/Build/report_unity_failure.sh "$PWD" error_logs/unity-job-log.txt` | annotates compile errors, abort reasons, unresolved build methods, package failures, exceptions, crashes/OOM kills, and a missing breadcrumb; its output is appended to `error_logs/apk-build-errors.log` |
| 3 | Extract matching Unity error lines | appends compiler errors, exceptions, aborts, crashes, OOM kills, licence and disk-space failures to `error_logs/apk-build-errors.log` (capped at 400 lines × 400 chars — a failed Unity run emits tens of thousands of shader-compiler lines), then the last 400 lines of Unity's own output; if the log could not be downloaded, the report records that and the fetch diagnostic |
| 4 | `actions/upload-artifact` | `Wayfinder-APK-error-logs` contains `error_logs/` (the sanitised job log, its `.excerpt` tail, the fetch transcript and the error report), 7-day retention |

The separate `diagnose` job has job-level `continue-on-error: true`; the fetch, report, error-line
extraction and upload steps also have step-level guards. Commands retain their real exit statuses,
but failures in this reporting-only job cannot fail the workflow or change the `apk` job result.

### Unity authentication secrets

In **Settings → Secrets and variables → Actions**, configure either:

- **Account-based Personal activation (the proven route for this repo):** `UNITY_EMAIL` and
  `UNITY_PASSWORD`, from the Unity account that has an eligible Unity Personal license. You do
  **not** need to create a `.ulf` or `UNITY_LICENSE` secret for this route. The 2026-10-15 run
  activated successfully with `UNITY_LICENSE` empty.
- **License-file authentication:** `UNITY_LICENSE` containing the license file contents, if you
  prefer that method.

Never put the account password or license content in chat or source control. Earlier run
38858427636 stopped because the preflight incorrectly required all three secrets; it did not show
that the Unity account activation was invalid. The updated preflight accepts either method.

### Reading a failure without opening the log

Unity aborts batchmode with exit code 1 for many reasons, and the raw log host is not reachable
from every environment. Both jobs publish what they know as annotations — read the
**Explain the Unity result** job first, it is the one that has Unity's log:

```bash
run=<run-id>
gh api repos/boss-of-python/Wayfinder/actions/runs/$run/jobs \
  --jq '.jobs[] | "\(.id) \(.name) \(.conclusion)"'
gh api repos/boss-of-python/Wayfinder/check-runs/<job-id>/annotations \
  --jq '.[] | "[\(.annotation_level)] \(.title): \(.message)"'
```

You get up to ten `error CS` annotations with file/line/column, the batchmode abort reason, an
unresolvable `-executeMethod`, package-resolution failures, editor exceptions, the
`[Wayfinder] Editor method failed: …` breadcrumb written by `EditorFailureLog`, warnings in
`Assets/Wayfinder`, which Wayfinder assemblies Unity compiled, and the APK size. A failure that
matches none of those is still reported, with the tail of the editor log—or a clearly labeled
workspace-log tail if only worker logs survived—attached. Silence is a bug (KI-039), not a result.
The mocked workflow/diagnostics and pipeline suite is 90/90
(`python3 -m unittest discover -s Tools/Build -p 'test_*.py' -v`); these tests do not execute
Unity.

Limits to know up front:

- The repo is **public**, so the standard runner is free. It is pinned to `ubuntu-24.04`, not
  `ubuntu-latest`: run 38875757162 carried GitHub's notice that the label migrates to Ubuntu 26
  from 2026-10-19, and a 9 GB Docker Unity build should not change host OS without a commit.
  The 180-minute timeout can be exceeded by the first image pull plus an IL2CPP Android build.
  The secret and editor-pin preflights fail before disk cleanup/image pull; a Unity-stage failure
  may still cost several minutes.
- `unity-builder` v6 ignores its `unityVersion` input and reads the version from the
  checked-out `ProjectSettings/ProjectVersion.txt` — that file is now committed and step 4 only
  verifies it.
- The result is a **development, unsigned** APK for on-device smoke testing only. It is
  never a store artifact; release AAB signing stays on the owner's machine (§4 below).
- All actions are pinned to full commit SHAs. The APK job has `contents: read`; the reporting-only
  diagnose job additionally has `actions: read` solely to fetch the completed build job's log.

After a successful run: download `Packages/packages-lock.json` from the `Unity-logs` artifact
and commit it (closes KI-002), then install the APK on the Moto G45
([MOTO_G45_BUILD_AND_TEST.md](MOTO_G45_BUILD_AND_TEST.md)).

This route does **not** replace the dedicated self-hosted runner
(`android-phone-build.yml`, §"Phone-controlled build route"): use that one when the build
must come from a machine you control.

## Campaign slice (Batch C, 15 Oct 2026)

**VERIFIED (headless, no Unity):** `python3 Tools/Build/headless_cs.py all` → 7 assemblies
compile with 0 warnings, **109/109** EditMode tests pass (campaign loop, interruption at every
save step, rescue, camps, autosave, Q01–Q04 slice ledger). `python3 Tools/Validation/validate_content.py`
now also validates `Assets/Wayfinder/Content/vertical_slice.json`.

**UNVERIFIED (needs Unity):** after `Tools → Wayfinder → Setup Project` (which now also copies the
campaign JSON into `Assets/Wayfinder/Resources/Content/` — generated, git-ignored — and builds the
VerticalSlice scene in world coordinates), press Play in `Bootstrap` → Title → choose a slot →
**New** (a second tap confirms replacing an occupied slot) → the garage panel opens: accept
missions, research, load cargo, **Depart**. Drive with WASD/arrows or the on-screen pedals; hold
**E/Interact** (or the on-screen HOLD INTERACT button) at caches, camp sockets, the gauge, the fuse
site and the relay; stop near landmarks below 15 km/h for 2 s to discover them; stop inside the
garage arrival pad (< 1 m/s for 2 s) to bank and see the summary. **Pause → Save & quit** returns
to the title; **Continue** resumes. Saves live in `Application.persistentDataPath/saves/slot-N/`.
`Tools → Wayfinder → Refresh Campaign Content` re-copies the JSON after content edits.

## Exploration map (Batch D, 15 Oct 2026)

**VERIFIED (headless, no Unity):** `python3 Tools/Build/headless_cs.py all` → **143/143** EditMode
tests, including 21 map tests and 13 supplies/selling/research-wiring tests. To regenerate the map evidence PNGs:

```bash
WAYFINDER_MAP_SNAPSHOT_DIR=/tmp/map python3 Tools/Build/headless_cs.py test   # after `compile`
python3 Tools/Validation/rgba_to_png.py /tmp/map                               # raw RGBA → PNG (stdlib only)
```

**UNVERIFIED (needs Unity) — map controls in the VerticalSlice:**

| Action | Touch | Desktop / controller |
|---|---|---|
| Open the full map (pauses the drive) | Tap the minimap (HUD right edge) | **M** / gamepad **Select**; garage: **Map (M)** button |
| Close | **Close (M)** button | **M** or **Esc** / gamepad Select or East |
| Pan / zoom | Drag; pinch | Arrow keys; mouse wheel or **+ / −** |
| Select a marker (details, distance, bearing) | Tap | Click |
| Place a custom marker (max 10) | Long-press 0.6 s → **Place** | **Mark centre** button → **Place** |
| Route to a marker (dashed straight-line estimate) / clear | Select it → **Route here** / **Clear route** | same |
| Recentre on the vehicle | **Center on car** | **C** |
| Filter layers | **Hubs / Sites / Tasks / Marks** toggles | same |

**Field supplies and selling (same batch, UNVERIFIED in Unity; rules VERIFIED headless — 143/143):**
- During a drive, tap **Supplies**. The vehicle is held while the panel is open.
- **Use fuel can:** adds min(6 L, missing); needs ≥ 1 L of space; if part of the can would be wasted,
  tap twice to confirm.
- **Repair** a subsystem: +35, only when stopped and out of water.
- **Patch tire:** only with a puncture (tires < 30); it lifts the 25 km/h limit.
- In the garage, **Sell +N** then **Confirm?** sells one unit at 40 % (rounded down). Salvage bundles,
  mission items and the camp kit Q03 still needs cannot be sold.

The sheet covers the 1,024 m slice area at 2 m per pixel. Fog means "not explored yet"; contour lines
mean "surveyed from a vista". Unopened caches never appear on the map (§7.6).

## Latest setup/build workflow (15 Oct 2026)

Read **[UNITY_HANDOFF.md](UNITY_HANDOFF.md)** for the current source implementation,
actual executed checks, prerequisites and Windows/macOS/Linux commands.

```bash
python3 Tools/Build/unity_pipeline.py smoke --unity "PATH_TO_INSTALLED_UNITY_EDITOR"
```

This attempts real-editor pinning → generated URP/quality/input/scenes → Unity
EditMode tests → PlayMode tests → development APK, stopping on failures or stale
outputs. It requires a locally installed/licensed Unity 6.3 LTS editor and Android
modules. **It has not run successfully here: no Unity editor is installed.** The
runner's 23 mocked contract tests are not evidence of Unity success. The current
headless suite has **55 passing tests**; 49-test counts below describe the prior
2026-10-14 batch.

Android release source now explicitly emits `Builds/Android/Wayfinder.aab` and
requires owner upload-key signing. No signed AAB exists yet. Generated scenes are
an explicitly labeled driving prototype, not the planned campaign flow.

## 0. Environment reality check (read first)

The machine that produced this repository state has **no Unity editor, no Android SDK, no Xcode** (only Python 3.11 and Node). Therefore:

- The **non-Unity C# assemblies** (`Wayfinder.Core`, the pure part of `Wayfinder.Simulation`, `Wayfinder.World`, `Wayfinder.Progression`, `Wayfinder.Persistence`, `Wayfinder.Tests.EditMode`) **are compiled and tested here** with a real Roslyn C# 9 compiler + NUnit 3 that `Tools/Build/headless_cs.py` bootstraps from pinned sources — see §3a and `Docs/EVIDENCE_CS_HEADLESS.md` (49/49 EditMode tests pass).
- The **Unity-dependent C#** (`Scripts/Platform`, `Scripts/Presentation`, `Scripts/Editor`, `Simulation/VehicleController.cs`, `Simulation/ChaseCameraBehaviour.cs`, `Tests/PlayMode`) is **real source, parse-checked as C# 9, but not type-checked or executed** (KI-001/KI-003).
- Everything under `Tools/Validation` and `Tools/Balance` is **Python and was executed here** — see `Docs/EVIDENCE_M0.md` for captured output.
- Nothing in this repository is a fabricated build log, APK, IPA, or device measurement.

**Smallest missing tool to advance one step:** install **Unity 6.3 LTS (any `6000.3.x` patch)** via Unity Hub on a machine you control.

---

## 1. One-time project setup (owner machine)

```bash
git clone https://github.com/boss-of-python/Wayfinder.git
cd Wayfinder
# 1) Install Unity 6.3 LTS (6000.3.x) via Unity Hub, with modules:
#    - Android Build Support (SDK+NDK+OpenJDK)   [for Android smoke build]
#    - iOS Build Support                          [macOS only, for iOS smoke build]
# 2) Pin the real toolchain (writes ProjectSettings/ProjectVersion.txt and fixes package pins):
python3 Tools/Build/pin_toolchain.py --editor-version 6000.3.<patch>f1
# 3) Open the repo root as a Unity project (Unity Hub → Add → select this folder).
#    Unity resolves Packages/ and generates Packages/packages-lock.json.
# 4) In Unity: menu  Tools → Wayfinder → Setup Project
#    (creates Bootstrap/Title/PhysicsTestGround/VerticalSlice scenes, URP asset, quality tiers,
#     player settings: landscape, ARM64, product name "Wayfinder: The Quiet Frontier")
# 5) Commit the generated state so the repo is truly version-locked:
git add ProjectSettings/ProjectVersion.txt Packages/packages-lock.json Assets Packages
git commit -m "M0: pin editor/packages, generate scenes and .meta files"
git push
```

> D-004 / KI-002: package pins in `Packages/manifest.json` are **provisional** until step 2–5. If the Package Manager reports a missing version, pick the newest compatible version it offers for the `6000.3` stream and record the change in `Docs/DECISIONS.md`.

---

## 2. Run the game in the editor (**UNVERIFIED** until Unity is installed)

1. Open scene `Assets/Wayfinder/Scenes/Bootstrap.unity`.
2. Press Play. Implemented prototype flow: `Bootstrap → Title → Open proving ground → Driving`.
   Campaign/New Expedition/Continue/Garage integration remains unfinished.
3. Prototype controls: **WASD/arrows** drive, **mouse movement/right stick** camera,
   **Space** handbrake, **Esc/start** pause; on-screen touch steering/pedals, reset and Title.
   Hold brake while stationary to select reverse; throttle brakes in reverse.
   Mission interaction/map/recovery-menu wiring is not part of this prototype.
4. Touch controls render automatically in the Game view safe area (they are the primary target).

## 3. Run the automated tests (**UNVERIFIED** for C#; **VERIFIED** for Python)

C# (Unity Test Runner, after setup):
```bash
# EditMode (pure domain + content + save + economy):
Unity -batchmode -nographics -projectPath . \
  -runTests -testPlatform EditMode -testResults Logs/editmode.xml
# PlayMode (vehicle grounding/driving/fuel, touch/pause/release):
Unity -batchmode -projectPath . \
  -runTests -testPlatform PlayMode -testResults Logs/playmode.xml
```
(Replace `Unity` with the exact editor binary path, e.g. `~/Unity/Hub/Editor/6000.3.xf1/Editor/Unity`.)

Python data/formula/balance checks (**VERIFIED** — ran during implementation):
```bash
python3 Tools/Validation/validate_content.py     # content counts, IDs, gates, recipes
python3 Tools/Validation/formula_checks.py       # blueprint formulas & invariants
python3 Tools/Balance/economy_sim.py             # §11.5 headless balance simulation
```

## 3a. Headless C# compile + EditMode tests (**VERIFIED** — no Unity needed)

```bash
python3 Tools/Build/headless_cs.py all        # bootstrap (first run ≈1 min, cached in ~/.cache/wayfinder-headless-cs)
                                              # + compile (C# 9, warn:4) + EditMode tests (NUnitLite)
                                              # + .NET Standard 2.1 profile check + parse check of Unity files
python3 Tools/Build/headless_cs.py test -- --where "class =~ PersistenceTests"   # any NUnit filter
WAYFINDER_HEADLESS_CS_HOME=/tmp/wf-cs python3 Tools/Build/headless_cs.py all     # fresh cache
```

Needs Python 3.9+, Linux x86_64 and access to `github.com` / `api.github.com` / `codeload.github.com` /
`files.pythonhosted.org` for the one-time bootstrap. Exit code is non-zero on any compile error,
test failure, profile violation or syntax error. Outputs: `Logs/headless/compile-report.json`,
`Logs/headless/EditMode.headless.xml` (NUnit3 XML). This is **not** a Unity build (KI-016): Unity's
own Test Runner result (§3) remains the gate evidence and supersedes it.

## 4. Build a smoke build (**UNVERIFIED** — needs platform modules)

Android (ARM64, IL2CPP, development build for M0 smoke):
```bash
Unity -batchmode -quit -projectPath . \
  -executeMethod Wayfinder.Editor.BuildCommands.BuildAndroidDev \
  -logFile Logs/build_android.log
# Artifact: Builds/Android/Wayfinder-dev.apk
```

iOS (macOS + Xcode + owner signing identity):
```bash
Unity -batchmode -quit -projectPath . \
  -executeMethod Wayfinder.Editor.BuildCommands.BuildIOSDev \
  -logFile Logs/build_ios.log
# Artifact: Builds/iOS/Xcode project — sign in Xcode with YOUR credentials (never commit them)
```

Release variants: `BuildAndroidRelease` (signed AAB, `Builds/Android/Wayfinder.aab`) /
`BuildIOSRelease` (see `Assets/Wayfinder/Scripts/Editor/BuildCommands.cs` and `Docs/UNITY_HANDOFF.md`).
Signing credentials stay outside the repository (§23.2). Store publication is owner-authorized and out of scope for any AI step.

## 5. Content & world bake (editor menu, after Setup Project)

| Menu item | Effect |
|---|---|
| `Tools → Wayfinder → Import Content JSON` | Validates JSON and (re)generates ScriptableObject definitions + catalog hash |
| `Tools → Wayfinder → Validate Content` | In-editor validation (duplicate IDs, counts, prerequisites, recipes, route bands) |
| `Tools → Wayfinder → Bake World (Greybox)` | Deterministic heightfield → 24×24 chunk bake → seam report → `Logs/world_bake_report.json` |
| `Tools → Wayfinder → Run EditMode Tests` | Convenience wrapper around Test Runner |

## 6. Airplane-mode / offline audit (device, **UNVERIFIED**)

After any successful install: enable airplane mode, force-stop the app, relaunch, start a new expedition, play 10 minutes, background/foreground during banking, force-stop mid-drive, relaunch and confirm the last valid save revision loads with no currency duplication (SAVE-01/MOBILE-01). No network permission must be requested at install or runtime.

## 7. Repository map

```
Assets/Wayfinder/Scripts/Core            pure domain primitives (IDs, hash, PRNG, results, bitsets)
Assets/Wayfinder/Scripts/Simulation      vehicle/hazard math + WheelCollider runtime
Assets/Wayfinder/Scripts/World           world definition, noise, seams, streaming, routes
Assets/Wayfinder/Scripts/Progression     inventory/economy/transactions/missions/research/discovery
Assets/Wayfinder/Scripts/Persistence     save DTOs, canonical JSON, atomic save service, migrations
Assets/Wayfinder/Scripts/Presentation    HUD/garage/map controllers and view models
Assets/Wayfinder/Scripts/Platform        lifecycle, paths, quality tiers, local diagnostics
Assets/Wayfinder/Scripts/Editor          setup, scene builder, content import, validators, baker, builds
Assets/Wayfinder/Tests/EditMode          NUnit unit tests (pure domain + content + save)
Assets/Wayfinder/Tests/PlayMode          NUnit play tests (vehicle, hazards, recovery, streaming)
Assets/Wayfinder/Content                 authoritative JSON content + input actions
Tools/Validation                         headless validators (Python, runnable anywhere)
Tools/Balance                            economy simulation (Python)
Tools/Build                              pin_toolchain.py, headless_cs.py (C# compile+test without Unity), platform build scripts
Docs/evidence                            committed raw evidence artifacts (NUnit XML, compile reports, console logs)
Docs                                     status, decisions, issues, evidence, requirement map, assets
```
