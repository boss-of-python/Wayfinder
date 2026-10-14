# Wayfinder — Build & Run

Status legend used here: **VERIFIED** (ran in this repo/environment) vs **UNVERIFIED** (requires a tool this environment does not have; command is exact and ready to run).

---

## 0. Environment reality check (read first)

The machine that produced this repository state has **no Unity editor, no .NET/Mono compiler, no Android SDK, no Xcode** (only Python 3.11 and Node). Therefore:

- Everything under `Assets/` is **real source, but uncompiled/unverified** (KI-001/KI-003).
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
2. Press Play. Flow: `Boot → Title → (New Expedition | Continue) → Garage → Driving`.
3. Desktop development controls (§3.1): **WASD/arrows** drive, **mouse drag** camera, **Space** handbrake, **E** interact, **M** map, **R** recovery menu, **Esc** pause.
4. Touch controls render automatically in the Game view safe area (they are the primary target).

## 3. Run the automated tests (**UNVERIFIED** for C#; **VERIFIED** for Python)

C# (Unity Test Runner, after setup):
```bash
# EditMode (pure domain + content + save + economy):
Unity -batchmode -nographics -projectPath . \
  -runTests -testPlatform EditMode -testResults Logs/editmode.xml -quit
# PlayMode (vehicle grounding, hazards, recovery, streaming):
Unity -batchmode -projectPath . \
  -runTests -testPlatform PlayMode -testResults Logs/playmode.xml -quit
```
(Replace `Unity` with the exact editor binary path, e.g. `~/Unity/Hub/Editor/6000.3.xf1/Editor/Unity`.)

Python data/formula/balance checks (**VERIFIED** — ran during implementation):
```bash
python3 Tools/Validation/validate_content.py     # content counts, IDs, gates, recipes
python3 Tools/Validation/formula_checks.py       # blueprint formulas & invariants
python3 Tools/Balance/economy_sim.py             # §11.5 headless balance simulation
```

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

Release variants: `BuildAndroidRelease` / `BuildIOSRelease` (see `Assets/Wayfinder/Scripts/Editor/BuildCommands.cs`).
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
Tools/Build                              pin_toolchain.py, platform build scripts
Docs                                     status, decisions, issues, evidence, requirement map, assets
```
