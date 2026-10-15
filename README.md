# Wayfinder: The Quiet Frontier

A single-player, offline-first exploration-driving game for Android and iOS —
Unity 6.3 LTS · C# · URP · **no ads, no in-app purchases, no accounts, no backend**.

> “You can see a place on the horizon, work out how to reach it, and bring back something worth remembering.”

## What is in this repository

| Path | Contents |
|---|---|
| `Wayfinder_Production_Blueprint.txt` | Authoritative product specification (v1.0, 14 Oct 2026) |
| `AI_IMPLEMENTATION_HANDOFF.txt` | AI implementation protocol this repo follows |
| `Assets/Wayfinder/` | Unity project source: scripts, tests, content JSON, input actions |
| `Docs/` | Implementation status, decisions, known issues, evidence, build & run, requirement map, asset manifest |
| `Tools/` | Headless validators, economy simulator, content generator, build scripts, toolchain pin |

Read **`Docs/BUILD_AND_RUN.md`** first — it states exactly what is verified today versus what
requires a Unity editor on your machine.

For Android publication, see **[Google Play publishing & status](Docs/PLAY_STORE_PUBLISHING.md)**
(reviewed 15 Oct 2026), including the full Google Docs owner guide. **Not store-ready yet:**
runtime integration, Unity/device gates, final assets and signed AAB packaging remain unfinished.

## Current state (honest summary)

- The complete release-scope **content catalog** is authored and machine-validated:
  100 landmarks · 200 caches · 20 main missions (Q01–Q20) · 30 optional contracts + 5 job
  templates · 4 vehicles · 20 research tiers (Σ 32,200 Salvage) · 10 camps · 5 garages ·
  25 relics · 11 surfaces · crafting recipes · quality tiers.
- The **game systems** are implemented in C#: vehicle physics (WheelCollider, powertrain,
  surfaces, fuel, damage, water/mud, winch, recovery), world pipeline (deterministic hash/PRNG,
  heightfield, chunk seams, route graph, streaming state machine), progression (inventory,
  economy ledger, idempotent transactions, missions, discovery/map, research), atomic
  save/backup/migration, input (touch/joystick/tilt/controller/desktop), accessibility settings,
  UI view models, lifecycle safety, editor tooling and build scripts.
- **Verified evidence (ran in this repo):** content validation, 35 formula/invariant checks
  (including terrain seam agreement ≤ 1 mm and the §5.1 fuel example), economy simulation with
  no deadlocks and no arbitrage. See `Docs/EVIDENCE_M0.md`.
- **Verified without Unity (headless C#):** the non-Unity assemblies (Core, pure Simulation
  math, World, Progression, Persistence) compile with a real Roslyn C# 9 compiler at warning level 4
  with zero warnings, pass a .NET Standard 2.1 API-profile check, and **all 59 EditMode NUnit tests
  pass** (`python3 Tools/Build/headless_cs.py all`; raw artifacts in `Docs/evidence/headless-cs/`,
  write-ups in `Docs/EVIDENCE_CS_HEADLESS.md` and `Docs/UNITY_HANDOFF.md`). Running them found and fixed a save-checksum bug that
  would have blocked every save (KI-013), a water-stall re-drain (KI-014) and an inventory stack bug
  (KI-015).
- **Not yet verified:** Unity's own compile of the engine-facing code (`Platform`, `Presentation`,
  `Editor`, `VehicleController`, `ChaseCamera`, PlayMode tests — parse-checked only), PlayMode
  physics, device builds, touch play, thermal performance — the authoring environment had no
  Unity/Android/Xcode toolchain (`Docs/KNOWN_ISSUES.md` KI-001). Nothing here claims a build log,
  APK, IPA, screenshot or device measurement that does not exist.

## Unity / Android implementation update (15 Oct 2026)

A source-level driving prototype now connects Bootstrap → Title → a four-wheel
proving ground, touch input, chase camera and pause/reset UI. Setup generates URP,
quality settings and runtime input resources; Android release tooling now explicitly
builds a signed AAB. **These Unity-facing changes are syntax-checked only, not yet
compiled/executed by Unity.** The full campaign, saves/UI integration and streaming
remain unfinished.

See **[Unity handoff and one-command smoke pipeline](Docs/UNITY_HANDOFF.md)** for
Windows/macOS/Linux instructions, the 59 passing headless C# tests and 23 passing
Python runner tests. On a computer with an installed/licensed editor:

```bash
python3 Tools/Build/unity_pipeline.py smoke --unity "PATH_TO_INSTALLED_UNITY_EDITOR"
```

## Working from a Moto G45

Read **[Moto G45 build and test guide](Docs/MOTO_G45_BUILD_AND_TEST.md)**. The phone
can remotely control a licensed Unity desktop and install/test its APK, but cannot
run the supported desktop Unity Editor itself. A manual, dedicated-Windows-runner
APK workflow is prepared in `.github/workflows/android-phone-build.yml`; it has
not been run and requires secure owner setup, not a phone-only or free hosted build.

Latest continuation: tested pause/focus gate, authored-water stall wiring (Unity
unverified), mobile 30 FPS target with a 30/60 toggle. **59/59** headless C# tests and
**23/23** mocked Python runner tests pass; no real APK or device result exists.

## Quick start (owner machine)

```bash
# 1. Install Unity 6.3 LTS (6000.3.x) via Unity Hub (+ Android Build Support module)
# 2. Pin the toolchain and open the project:
python3 Tools/Build/pin_toolchain.py --editor-version 6000.3.<patch>f1
# 3. Open this folder in Unity → menu Tools → Wayfinder → Setup Project
# 4. Run headless checks any time (no Unity needed):
python3 Tools/Build/headless_cs.py all           # real C# 9 compile + 59 EditMode NUnit tests
python3 Tools/Validation/validate_content.py
python3 Tools/Validation/formula_checks.py
python3 Tools/Balance/economy_sim.py
```

Full commands (editor tests, Android/iOS smoke builds, airplane-mode audit):
see `Docs/BUILD_AND_RUN.md`.

## Design pillars

1. Curiosity leads. 2. Terrain is the puzzle. 3. Failure is recoverable.
4. Progress increases possibilities. 5. A complete offline experience. 6. Calm, not empty.

## License / monetization statement

No advertising. No in-app purchases. No premium currency. No tracking. No required network.
(Exactly the statement §11.4 requires in Settings/About — and enforced by the dependency scan
in `Tools/Validation/validate_content.py`.)
