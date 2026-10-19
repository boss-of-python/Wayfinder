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
- The **game systems** are implemented in C# and composed by a per-slot campaign session: vehicle physics (WheelCollider, powertrain,
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
  with zero warnings, pass a .NET Standard 2.1 API-profile check, and **all 188 EditMode NUnit tests
  pass** (59 before Batch C, 109 before Batch D, 143 before Batch G) (`python3 Tools/Build/headless_cs.py all`; raw artifacts in `Docs/evidence/headless-cs/`,
  write-ups in `Docs/EVIDENCE_CS_HEADLESS.md` and `Docs/UNITY_HANDOFF.md`). Running them found and fixed a save-checksum bug that
  would have blocked every save (KI-013), a water-stall re-drain (KI-014) and an inventory stack bug
  (KI-015).
- **Verified by a real Unity editor (15 Oct 2026):** Unity **6000.3.26f1** (`de8167e7440c`) on a
  GitHub-hosted runner activated a Personal license from the repository secrets, resolved every
  package pin and **compiled all six engine-free Wayfinder assemblies** — so `VehicleController`
  and `ChaseCameraBehaviour` are now Unity-compiled, not parse-checked. Evidence:
  `Docs/evidence/cloud-build-2026-10-15/`.
- **Not yet verified:** the four Input System-dependent assemblies (`Platform`, `Presentation`,
  `Editor`, PlayMode/EditMode test assemblies — KI-034), scene generation, PlayMode physics,
  device builds, touch play, thermal performance. **No APK exists yet** (KI-006). Nothing here
  claims a build log, APK, IPA, screenshot or device measurement that does not exist.

## Android development APK status (19 Oct 2026)

The three latest cloud-build attempts
([39139527909](https://github.com/boss-of-python/Wayfinder/actions/runs/39139527909),
[39141218150](https://github.com/boss-of-python/Wayfinder/actions/runs/39141218150),
[39141401948](https://github.com/boss-of-python/Wayfinder/actions/runs/39141401948)) all passed
preflight and all failed in *Unity 1 of 2: Setup Project* after 5–6 minutes with GameCI's generic
`Build failed with exit code 1`; no APK was produced. Each diagnose job then reported the same
refusal twelve times — `the response contains terminal escape sequences; pass
--allow-escape-sequences to output it anyway` — which is why the Unity failure was still unknown
after five dispatches: **`gh api` refuses a raw log body containing ANSI colour and `\r` redraws,
and redirecting stdout to a file does not bypass that guard**, so Unity's editor log never reached
the reporter (KI-041, fixed by D-036).

The fetcher now downloads through `curl` → `gh --allow-escape-sequences` → plain `gh`, **sanitises
the escape sequences out of whatever arrives** (verbatim bytes are not parsable — game-ci colourises
the compile-error line itself, so a raw log loses the `file=…,line=…,col=…` annotation while still
looking diagnosed), and stores a 400-line excerpt in the artefact. The reporter names a crash or OOM
kill and says when the editor breadcrumb is *absent*, which means no C# exception was thrown inside
the build method.

That absence is what pointed at the cause: Unity ran for minutes, packages resolved (four
shader-compiler logs survived), and `ProjectSetup.Setup` threw nothing — so the failure was earlier
than our entry point. Source review then found it (PR #15): `ContentImporter.cs` uses
`Newtonsoft.Json.Linq` while `Wayfinder.Editor.asmdef` did not reference `Unity.Newtonsoft.Json`,
and Unity asmdefs require direct references. A CS0234 in an Editor-only assembly aborts batchmode
before `-executeMethod` — exactly the observed shape. The reference is added; it is reasoned from
source, **not yet compiled by Unity**, and another error may hide behind it (KI-034). **One dispatch
of current `main` now yields either an APK or file/line/column annotations**; evidence in
`Docs/evidence/cloud-build-2026-10-19/`.

The earlier 15 Oct Unity run also exposed a package compile error: Input System 1.12.0 referenced
`BuildTarget.ReservedCFE`, removed in Unity 6000.3. The manifest is repinned to 1.20.1, but that
fix is still unverified (KI-036). The existing account-based secrets should be enough; no `.ulf`
file or `UNITY_LICENSE` secret needs to be created if `UNITY_EMAIL` and `UNITY_PASSWORD` are
configured. A new cloud run is needed to verify package resolution, compile Unity glue, and produce
the unsigned development APK. The agent cannot dispatch the workflow without `actions: write`
(KI-035). See [build instructions](Docs/BUILD_AND_RUN.md) for the route and remaining gates.

## Exploration map, field supplies and research wiring (15 Oct 2026, Batch D)

The slice now has an **exploration map that starts blank and fills in as you explore** (§8.1, §13.2,
§13.4):
- Driving reveals 64 m cells around the car twice a second; cells hidden behind major ridges stay fogged.
- Vistas survey 384 m and add contour lines.
- Every revealed cell is saved and survives rescue.
- A HUD **minimap** and a full **map screen**: region % mapped against the 60 % finale gate, legend,
  filters, tap-to-inspect, pinch/drag, up to ten long-press custom markers with a dashed route
  estimate, *Center on car*, and a fuel-aware return estimate to the nearest safe hub.
- Unopened caches are never shown.

**Verified headlessly:** 130/130 C# tests, including 21 map tests. The tests prove a new game's sheet is
fully fogged, an incremental redraw equals a full redraw byte-for-byte, and markers persist; an 11/11
mutation check confirms the tests catch regressions. PNG snapshots of a scripted exploration go from 0 %
to 68 % mapped (`Docs/evidence/exploration-map-2026-10-15/`).

**Implemented, syntax-checked only:** the Unity minimap and map screen (KI-031). Map terrain still
comes from the greybox slice (KI-029), and routes are straight-line estimates (KI-030).

Also in this batch:
- **Field supplies:** fuel can, repair kit and tire patch, with the blueprint's use conditions.
- **Selling** at 40 % in the garage.
- **Researched upgrades now reach the physics rig's stat resolver.**
- Two upgrades that previously did nothing were fixed: Trail Tires I grip and Engine Tune torque
  (KI-032).

Headless total: **143/143** C# tests; 13/13 mutation check for these rules.

## Campaign composition update (15 Oct 2026, Batch C)

The progression services are now composed into a per-save-slot **`CampaignSession`**
(new engine-free `Wayfinder.Campaign` assembly): new game → depart → collect → discover →
return → bank → research, full rescue, camp establishment/deposit, autosave and
suspend saves, all driven through atomic, idempotent transactions with verified saves.
The §21.3 forest slice is defined in `Content/vertical_slice.json` (6 landmarks, 12 caches,
Q01–Q04, 2 camp sockets, tiers 1–6). **Verified headlessly:** 109/109 C# tests (including
banking interrupted after every persistence step and a full Q01–Q04 slice playthrough with an
exact Salvage ledger); seven domain defects found while composing were fixed (KI-018…KI-024)
and an 8-case mutation check shows the tests catch them. **Implemented, syntax-checked only:**
three save slots on the Title screen, the `VerticalSliceDriver` HUD/garage/summary, and a
world-coordinate VerticalSlice greybox scene. Evidence: `Docs/evidence/campaign-session-2026-10-15/`.
The M2 gate (a playable 30-minute slice on a device) is **not** passed yet.

## Unity / Android implementation update (15 Oct 2026)

A source-level driving prototype now connects Bootstrap → Title → a four-wheel
proving ground, touch input, chase camera and pause/reset UI. Setup generates URP,
quality settings and runtime input resources; Android release tooling now explicitly
builds a signed AAB. **These Unity-facing changes are syntax-checked only, not yet
compiled/executed by Unity.** The full campaign, saves/UI integration and streaming
remain unfinished.

See **[Unity handoff and one-command smoke pipeline](Docs/UNITY_HANDOFF.md)** for
Windows/macOS/Linux instructions and the latest headless checks: 188 C# tests and 60
mocked Python build/pipeline tests pass. On a computer with an installed/licensed editor:

```bash
python3 Tools/Build/unity_pipeline.py smoke --unity "PATH_TO_INSTALLED_UNITY_EDITOR"
```

## Working from a Moto G45

Read **[Moto G45 build and test guide](Docs/MOTO_G45_BUILD_AND_TEST.md)**. The phone
can remotely control a licensed Unity desktop and install/test its APK, but cannot
run the supported desktop Unity Editor itself. A manual, dedicated-Windows-runner
APK workflow is prepared in `.github/workflows/android-phone-build.yml`; it has
not been run and requires secure owner setup, not a phone-only or free hosted build.

Latest local checks: **91/91** diagnostics tests, **23/23** mocked pipeline tests, **19/19**
mutations for the 19 Oct batch (**22/22** for Batch H) and a 20/20 dress rehearsal; the last
headless C# run (16 Oct) passed **188/188** tests, and the only C#-side change since is one added
asmdef reference (PR #15). The three latest
cloud runs failed in *Setup Project*, and their diagnose jobs could not fetch the editor log
because `gh api` refused a body containing terminal escape sequences — found, reproduced against
gh's own source and fixed on 19 Oct (KI-041, D-036). The workflow pre-creates
`error_logs/apk-build-errors.log` and publishes the sanitised job log, its excerpt tail, the fetch
transcript and the report as `Wayfinder-APK-error-logs`
(`Docs/evidence/cloud-build-2026-10-19/`). The Unity failure is now attributed to a missing asmdef
reference but is unverified by a real editor run and needs one dispatch; the mobile pause/focus and
authored-water wiring remain Unity-unverified, and no real APK or device result exists.

## Quick start (owner machine)

```bash
# 1. Install Unity 6.3 LTS (6000.3.x) via Unity Hub (+ Android Build Support module)
# 2. Pin the toolchain and open the project:
python3 Tools/Build/pin_toolchain.py --editor-version 6000.3.<patch>f1
# 3. Open this folder in Unity → menu Tools → Wayfinder → Setup Project
#    then play: Title → slot "New" → VerticalSlice (greybox campaign slice)
# 4. Run headless checks any time (no Unity needed):
python3 Tools/Build/headless_cs.py all           # real C# 9 compile + 188 EditMode NUnit tests
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
