# Unity implementation batch — 15 October 2026

## Campaign composition batch (Batch C)

New engine-free `Wayfinder.Campaign` assembly (per-slot `CampaignSession`, snapshot saves,
autosave, field controller, view models) — **109/109 headless tests**, evidence in
`Docs/evidence/campaign-session-2026-10-15/`. New Unity-facing files are **syntax-checked only**
(plus a scratch stub type-check that is not Unity evidence): `Platform/CampaignHost.cs`,
`Presentation/VerticalSliceDriver.cs`, the save-slot UI in `Presentation/TitleFlowHost.cs`,
`SceneBuilder.BuildVerticalSlice` (world coordinates, from `Content/vertical_slice.json`),
`ProjectSetup.CopyCampaignContent`, `Tests/PlayMode/CampaignPlayModeTests.cs` (2 tests; 9 PlayMode
tests total) and 2 new editor tests in `Tests/Editor/ProjectSetupTests.cs` (Resources content copy is
verbatim; the VerticalSlice scene has its driver/rig at the garage spawn and one marker per slice
cache/landmark). First-open checklist for this batch: Setup Project → confirm
`Assets/Wayfinder/Resources/Content/*.json` exist → Test Runner EditMode (expect 109) and PlayMode
(expect 9) → play Title → New → VerticalSlice → Depart → collect a cache → return → research.
Researched stats beyond tank size/cargo mass are not yet applied to physics (KI-025).

## Later same-day mobile continuation

See **[MOTO_G45_BUILD_AND_TEST.md](MOTO_G45_BUILD_AND_TEST.md)** for the latest
59-test headless result, mobile pause/focus and water-contact source changes,
phone-controlled desktop workflow and the source-ZIP route. The 55-test batch
below is retained as the preceding implementation snapshot; neither batch has
Unity/device/build evidence.

## What was done

This batch implements the **first driving-prototype / Android-build foundation**.
It does not complete the campaign or make the game ready for Google Play.

- Repeatable editor setup now creates/reuses URP pipeline and renderer assets,
  configures three quality tiers through serialized settings (the public names
  property is read-only), enables the new Input System and generates its runtime
  JSON resource from the authoritative `.inputactions` file.
- Setup preserves existing application identifiers, version numbers and signing
  configuration. The default ID still needs owner review before publication.
- Bootstrap loads a prototype title screen with an **Open proving ground** button.
  The generated test scene includes an unscaled Scout root, four configured
  WheelColliders, persistent greybox materials, input, camera and HUD wiring.
- Prototype UI supplies multi-pointer left/right/gas/brake buttons, safe-area
  layout, pause/resume, speed/fuel/gear display, reset and return to title. It is
  explicitly labeled a prototype; no fake campaign/Continue option is shown.
- MonoBehaviour types now live in matching filenames so generated scene script
  references can survive serialization/reimport.
- Fixed source-level issues: missing Input System and test runner assembly
  references, wrong tilt method name, invalid input-asset disposal, nonexistent
  `WheelHit.distance`, and static torque split called as an instance method.
- Fixed tested domain behavior: zero steering no longer introduces a phantom
  turn, left/right Ackermann angles are symmetric, reverse produces signed torque
  after the stationary guard, direction cannot flip at speed, the handbrake cuts
  propulsion, and negative AWD split preserves direction.
- Vehicle controller now samples surface labels, gates propulsion for an empty
  tank/stopped/disabled engine, updates mass for fuel/cargo, and keeps the chase
  camera from colliding with its own rig. Reverse uses the brake pedal after the
  guard; throttle brakes when in reverse. These engine integrations need Unity
  physics calibration and tests.
- Android development builds explicitly request APK; release builds explicitly
  request AAB, IL2CPP/ARM64 and custom signing. Failed build reports throw instead
  of merely logging success-looking completion. Signing secrets are restored after
  the build and are not written into source or printed by the build helper.
- Added a cross-platform Python runner that checks the actual installed editor,
  pins it only for setup/smoke, runs separate setup/EditMode/PlayMode/build
  processes, rejects missing/stale outputs and fails on unsuccessful/empty test
  reports. Test execution omits `-quit` so Unity Test Framework controls exit.

## What actually ran here

| Check | Executed result |
|---|---|
| `python3 -m unittest discover -s Tools/Build -p 'test_*.py' -v` | 23/23 Python runner contract tests passed; mocked Unity subprocesses/temporary archives, **not Unity evidence** |
| `python3 Tools/Build/headless_cs.py all` | 55/55 pure C# tests passed; zero compile warnings/errors; .NET Standard 2.1 profile passed |
| Same harness, Unity-dependent sources | 21 files syntax-parsed successfully, **not Unity API type-checked** |
| Content / formula / economy scripts | Passed; content had zero errors/warnings; no modeled deadlocks/arbitrage |
| `bash -n Tools/Build/build.sh` / `git diff --check` | Passed |
| `python3 Tools/Build/unity_pipeline.py doctor` | Failed honestly: Unity executable not installed/found |

Curated raw results are under `Docs/evidence/unity-prep-2026-10-15/`. They are
headless/Python results only. No Unity activation, package resolution, generated
Unity scene/URP assets, Unity test run, APK, signed AAB, screenshot or device result
has been produced by this batch. Unity downloads and package services are not
available from this sandbox; no license/signing credentials were requested.

## Run on your development computer

Prerequisites: Python 3.9+, installed and activated Unity **6.3 LTS / 6000.3.x**,
Android Build Support with SDK/NDK/OpenJDK, and internet for Unity Package Manager.
Close any editor instance using this checkout before batch commands.

Use the exact installed executable, not Unity Hub. The paths below contain
`YOUR_VERSION` placeholders: replace them with your actual version folder.

Windows PowerShell, from the repository root:

```powershell
py -3 Tools/Build/unity_pipeline.py smoke --unity "C:\Program Files\Unity\Hub\Editor\YOUR_VERSION\Editor\Unity.exe"
```

macOS:

```bash
python3 Tools/Build/unity_pipeline.py smoke --unity "/Applications/Unity/Hub/Editor/YOUR_VERSION/Unity.app/Contents/MacOS/Unity"
```

Linux:

```bash
python3 Tools/Build/unity_pipeline.py smoke --unity "$HOME/Unity/Hub/Editor/YOUR_VERSION/Editor/Unity"
```

`smoke` attempts, in order:

1. Read the real editor version and pin it if no pin exists. A mismatched existing
   pin fails rather than silently upgrading the project.
2. Resolve/import packages and execute setup, writing scenes/resources/settings.
3. Run Unity EditMode tests (including four new editor setup/serialization checks).
4. Run five PlayMode tests for four-wheel grounding, forward drive/empty tank,
   idle/stopped-engine fuel use, held touch input, and safe touch release/pause.
5. Build `Builds/Android/Wayfinder-dev.apk`.

Logs and Unity NUnit XML: `Logs/unity-pipeline/`. The runner stops at the first
failed gate. Tests and editor setup code are written but unverified until this
command actually completes on an installed editor. The first import may expose
remaining Unity/package API drift; do not dismiss compiler errors as harmless.
If activation, package selection or an input-backend restart needs interaction,
open once in the editor, resolve it, close the editor and rerun. Package pins are
still provisional until the actual editor resolves them.

Individual stages, after setting `UNITY_BIN` to that editor executable:

```bash
python3 Tools/Build/unity_pipeline.py doctor
python3 Tools/Build/unity_pipeline.py setup
python3 Tools/Build/unity_pipeline.py test
python3 Tools/Build/unity_pipeline.py android-dev
python3 Tools/Build/unity_pipeline.py android-release
```

The last command builds an AAB only after private signing is configured; it does
not upload/publish it. Use local Publishing Settings or inject **all four**
`WAYFINDER_KEYSTORE_PATH`, `WAYFINDER_KEYSTORE_PASS`, `WAYFINDER_KEY_ALIAS` and
`WAYFINDER_KEY_ALIAS_PASS` through a secret manager/process environment. Do not
put passwords on command lines, in Git, in this document or in chat. Keep the
keystore outside the checkout and back it up securely. Environment-injected
signing settings are applied temporarily and restored in `finally`.

## After a successful Unity run

- Preserve generated `ProjectSettings`, `Packages/packages-lock.json`, scenes,
  input resource, URP/material assets and all `Assets/**/*.meta` in version control.
  Builds/Logs/Library and keystores stay excluded. Review changes before committing.
- Open `Bootstrap.unity` and press Play. Intended prototype flow is
  **Bootstrap → Title → Open proving ground → drive/pause/reset → Title**.
- Install the development APK on physical Android phones. Verify controls,
  backgrounding, safe areas, frame pacing, battery and thermal behavior. Then test
  a signed non-development Play-delivered build separately.
- Share the relevant compiler/test/build logs if the pipeline fails. Logs can
  contain local paths; review/redact sensitive information before sharing. Never
  share private keys or passwords.

## Still not done

- No campaign composition, inventory/mission UI, banking, save/Continue or full
  vertical slice wired into the prototype.
- No world scatter placer or runtime streamer; no validated full-world traversal.
- Water-depth hazard probes remain placeholders; the test scene is not proof of
  mud/water/winch/recovery gameplay. Real driving calibration remains outstanding.
- No final art/audio/localization, performance evidence, current target API/page-size
  certification, privacy/store assets, closed-testing eligibility or submission.
- Setup rebuilds the four generated greybox scenes. It prompts to save unsaved
  editor work, but it intentionally replaces their generated layout; keep custom
  authored scenes separate or back them up first.

Read [PLAY_STORE_PUBLISHING.md](PLAY_STORE_PUBLISHING.md) for the remaining owner
release workflow. AAB support in source is **not** a signed release artifact or
Play approval.
