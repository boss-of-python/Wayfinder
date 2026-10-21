# Wayfinder — Executed Evidence: headless C# compile + EditMode tests (2026-10-14)

> **Update 2026-10-21 (Batch J — hub travel + per-camp storage, KI-028):** 7 assemblies / 0 warnings,
> **216/216** EditMode tests (188 → 216), .NET Standard 2.1 profile OK, 30 Unity-dependent files parse,
> `validate_content.py` + `formula_checks.py` clean. New fixtures `CampaignHubTravelTests` (15) and
> `CampaignCampStorageTests` (9) plus 4 schema-migration tests in `PersistenceTests`. **27/27** mutation
> checks caught (15 hub travel, 12 per-camp storage/migration); two survivors were fixed by strengthening
> the tests. First executed `SaveMigrations` step (schema 1 → 2) — the JObject-based first draft silently
> lost the migrated store, which is why the delegate is typed on `SavePayload` (D-040).
>
> **Update 2026-10-15 (Batch D continuation — supplies/selling/research wiring):** 7 assemblies / 60
> files, 0 warnings, **143/143** EditMode tests, 13/13 mutation check; KI-032 found and fixed.
> Artifacts: `Docs/evidence/supplies-and-stats-2026-10-15/`.
>
> **Update 2026-10-15 (Batch D — exploration map):** 7 assemblies / 57 files, 0 warnings,
> **130/130** EditMode tests (21 new map tests), 29 Unity-dependent files parse-checked, 11/11 map
> mutation check. Artifacts: `Docs/evidence/exploration-map-2026-10-15/`. Caveat found this batch:
> `headless_cs.py test` runs the *last compiled* assemblies, so run `compile` first (or `all`).
>
> **Update 2026-10-15 (Batch C):** the harness now also compiles the new engine-free
> `Wayfinder.Campaign` assembly (11 files) and profile-checks it against .NET Standard 2.1.
> Current result: 7 assemblies / 47 files, 0 warnings, **109/109** EditMode tests, 24
> Unity-dependent files parse-checked. Artifacts: `Docs/evidence/campaign-session-2026-10-15/`
> (includes an 8-case mutation check). The sections below describe the original 14 Oct run.

This file records what `Tools/Build/headless_cs.py` actually did in the authoring environment, with
the raw artifacts committed next to it under `Docs/evidence/headless-cs/`. Nothing here is a Unity
result; see §5 for exactly what this evidence does **not** prove.

Environment: Linux x86_64 sandbox (Debian 12, Python 3.11.2). Still **no Unity editor, no Android
SDK, no Xcode**. Network egress limited to `github.com`, `api.github.com`, `codeload.github.com`,
`registry.npmjs.org`, `pypi.org`, `files.pythonhosted.org`.

---

## 1. What the harness is

`Tools/Build/headless_cs.py` bootstraps a **real C# 9 compiler** (Roslyn `csc` 3.9.0, the compiler
generation that introduced C# 9 — Unity 6 compiles C# 9) and a real NUnit 3 runner, with every
artifact pinned by commit or SHA-256 (D-014), then:

| Step | What runs | Output |
|---|---|---|
| `compile` | Compiles the 5 runtime assemblies + `Wayfinder.Tests.EditMode` with the real `.asmdef` reference graph, `-langversion:9.0 -warn:4 -deterministic`; files whose `using` lines pull `UnityEngine`/`UnityEditor`/`Unity.*`/`TMPro` are excluded (listed per assembly) | `Logs/headless/compile-report.json` |
| `test` | Runs the EditMode NUnit suites under NUnitLite 3.13.3 (`--workers=1`) | `Logs/headless/EditMode.headless.xml` (NUnit3 format) |
| `profile` | Recompiles the 5 runtime assemblies against **only** a .NET Standard 2.1 reference assembly built from `dotnet/standard` source — the API profile Unity 6 exposes — so no .NET-Core-only API can pass | console |
| `syntax` | Parses every Unity-dependent file (`Simulation/VehicleController.cs`, `Simulation/ChaseCameraBehaviour.cs`, all of `Platform/`, `Presentation/`, `Editor/`, `Tests/PlayMode/`) with the Roslyn C# 9 parser, with and without the `UNITY_*` symbols. **Parse only — not type-checked** | console |

Toolchain provenance (all hashes are asserted by the script before use):

| Artifact | Source (allowed host) | Pin |
|---|---|---|
| .NET Core 3.1.23 runtime | PyPI wheel `dotnetcore2-3.1.23` | sha256 `5f076ddc…67ab5` |
| OpenSSL 1.1 shared libs (only if the OS lacks them) | PyPI wheel `psycopg2-binary 2.9.5` | sha256 `bcda1c84…7882` |
| Roslyn 3.9.0 `csc` (13 files) | `github.com/mono/roslyn-binaries` | commit `1c648247…`, per-file sha256 |
| Newtonsoft.Json 13.0.3 (same major as Unity's `com.unity.nuget.newtonsoft-json`) | `github.com/JamesNK/Newtonsoft.Json` tag 13.0.3 | commit `0a2e291c…`, tree sha256 `2c4062e0…` |
| NUnit framework + NUnitLite 3.13.3 | `github.com/nunit/nunit` tag v3.13.3 | commit `af8ca8bc…`, tree sha256 `1bd3a5eb…` |
| .NET Standard 2.1 reference assembly (83 source files) | `github.com/dotnet/standard` `release/3.0` | commit `908070fe…`, tree sha256 `6e12b73c…` |

Full hex digests: `Tools/Build/headless_cs.py` (top of file) and `Docs/ASSET_MANIFEST.csv`.
The cache lives outside the repository (`~/.cache/wayfinder-headless-cs`, override with
`WAYFINDER_HEADLESS_CS_HOME`); the bootstrap is idempotent and was re-run from an empty cache
to confirm reproducibility (52 s wall clock including downloads).

---

## 2. Commands executed and results

```
$ python3 Tools/Build/headless_cs.py all          # from an EMPTY cache (reproducibility run)
[headless-cs] csc version 3.9.0-6.21124.20 (db94f4cc)
[headless-cs] toolchain ready in /home/user/.cache/wayfinder-headless-cs
[headless-cs] Wayfinder.Core: OK — 9 file(s) compiled, 0 Unity-dependent file(s) excluded, 0 error(s), 0 warning(s)
[headless-cs] Wayfinder.Simulation: OK — 7 file(s) compiled, 2 Unity-dependent file(s) excluded, 0 error(s), 0 warning(s)
[headless-cs] Wayfinder.World: OK — 2 file(s) compiled, 0 Unity-dependent file(s) excluded, 0 error(s), 0 warning(s)
[headless-cs] Wayfinder.Progression: OK — 6 file(s) compiled, 0 Unity-dependent file(s) excluded, 0 error(s), 0 warning(s)
[headless-cs] Wayfinder.Persistence: OK — 2 file(s) compiled, 0 Unity-dependent file(s) excluded, 0 error(s), 0 warning(s)
[headless-cs] Wayfinder.Tests.EditMode: OK — 5 file(s) compiled, 0 Unity-dependent file(s) excluded, 0 error(s), 0 warning(s)
NUnitLite 3.13.3 (.NET Standard 2.0)   CLR Version: 3.1.23
Test Run Summary
  Overall result: Passed
  Test Count: 49, Passed: 49, Failed: 0, Warnings: 0, Inconclusive: 0, Skipped: 0
[headless-cs] Wayfinder.Core vs .NET Standard 2.1 reference only: OK (9 file(s))
[headless-cs] Wayfinder.Simulation vs .NET Standard 2.1 reference only: OK (7 file(s))
[headless-cs] Wayfinder.World vs .NET Standard 2.1 reference only: OK (2 file(s))
[headless-cs] Wayfinder.Progression vs .NET Standard 2.1 reference only: OK (6 file(s))
[headless-cs] Wayfinder.Persistence vs .NET Standard 2.1 reference only: OK (2 file(s))
syntax-check: 12 file(s), 0 syntax error(s)
[headless-cs] SUMMARY compile=OK tests=OK netstandard2.1-profile=OK syntax=OK
real 0m52.077s
```

Committed raw artifacts (copied verbatim from `Logs/headless/` after the final run):

| File | Content |
|---|---|
| `Docs/evidence/headless-cs/console.log` | full console output of the final `all` run |
| `Docs/evidence/headless-cs/compile-report.json` | per-assembly compiled/excluded file lists, errors, warnings |
| `Docs/evidence/headless-cs/EditMode.headless.xml` | NUnit3 result XML, 49 test-cases with per-case result and duration |

Order-independence check (the suites share static `Global` services, so this matters):

```
$ python3 Tools/Build/headless_cs.py test -- --where "test =~ Research"        → 3 run, 3 passed
$ python3 Tools/Build/headless_cs.py test -- --where "class =~ ProgressionTests" → 14 run, 14 passed
```

Negative control for the profile check (not a repo test; run ad hoc to prove the check can fail):
a one-line file calling `System.Text.Json.JsonSerializer` compiles against the .NET Core 3.1
implementation assemblies but is rejected against the .NET Standard 2.1 reference (`CS0234`).

Python layer re-run after the source changes in this batch: `validate_content.py` PASSED (0 errors),
`formula_checks.py` PASSED (0 failures), `economy_sim.py` completed with no deadlocks.

Test history during this batch (same 49 tests, never disabled or filtered):

| Run | Result | What changed in between |
|---|---|---|
| first compile | 6 real compile errors across 4 assemblies | — |
| first green compile | 49 run, 38 passed, 11 failed | compile fixes (a)–(e) below |
| after SaveJson fix | 49 run, 44 passed, 5 failed | KI-013 |
| final | **49 run, 49 passed** | KI-014, KI-015, D-018, two corrected test expectations |

---

## 3. Defects the harness found (all real, all fixed in this batch)

Compile-time (would have been the "API drift" surfaced on first editor open; now fixed before it):

| # | Where | Problem | Fix |
|---|---|---|---|
| a | `Simulation/HazardModels.cs` `AckermannAngles` | `double` → `float` without cast (CS0266) | explicit casts |
| b | `World` → `Simulation` | `World` used `SurfaceType` from `Wayfinder.Simulation`, which its asmdef does not reference | `SurfaceType` moved to `Wayfinder.Core` (D-015); `using Wayfinder.Core;` added where needed |
| c | `Persistence/SaveService.cs` | `_atomicReplace` field typed `Action<string>` but assigned/invoked with three strings | typed `Action<string,string,string>` |
| d | `Progression/ResearchService.cs` | constructor private while tests (and composition) construct it | public, with injected gate predicate (D-018) |
| e | `Tests/EditMode/ContentCatalogTests.cs` | used `UnityEngine.Application.dataPath` in an EditMode suite that is otherwise pure | path resolution guarded by `UNITY_5_3_OR_NEWER`, fallback `WAYFINDER_CONTENT_DIR` / walk-up (D-016) |

Runtime (found by executing the suites):

| ID | Severity | Problem | Fix |
|---|---|---|---|
| KI-013 | **Blocker** (would have shipped) | Every `SaveService.Save()` returned `FailedVerify`: Newtonsoft's default `ObjectCreationHandling.Auto` *appends* arrays to DTO list fields that are initialised in field initialisers, so the parse-verify step saw doubled lists and a checksum mismatch | dedicated `SaveJson` settings (`ObjectCreationHandling.Replace`, `DateParseHandling.None`, invariant culture) used for every serialize/deserialize (D-017) |
| KI-014 | Major | Water stall drained −10 condition every 2 s while submerged instead of once per incident (§5.3): `VehicleController` cleared the water latch immediately after applying it | `DamageModel` owns an incident latch (`ApplyWaterStall()` returns `true` only once per incident, `EndWaterIncident()` on exit, `RepairFully()` clears); `VehicleController.RestartAfterWaterRecovery()` is the only exit path |
| KI-015 | Major | `InventoryService.Add` left a single stack above `MaxStack`, under-counting used slots; `CanAdd` and `Add` could disagree | all-or-nothing `PlacementPlan` that fills partial stacks then opens new slots, splitting at `MaxStack` |
| — | Minor | `ResearchService` read `MissionService.Global` directly, making research tests order-dependent | injected `Func<string,bool>` story-gate predicate (D-018) |

Two test expectations were wrong and were corrected against the blueprint rather than the code:
`Powertrain_TorqueCurveAnchors` expected 212.0 N·m at 1950 rpm — linear interpolation of the §4.2
anchors `(1500,190),(2500,240)` gives **212.5**; `FuelModel_EngineOffZero_AndBounded_QA11` compared the
theoretical maximum `0.047 L/s` with `Assert.Less` — the maximum is attained exactly, so the assertion
is now `AreEqual(0.047, worst, 1e-6)` (the bound itself is unchanged).

---

## 4. Status change this evidence justifies

Per D-003 the rule was "C# without a compiler stays Implemented (uncompiled/unverified)". For the
assemblies listed above the honest status is now **Verified (headless)**:

| Assembly | Compiled C# 9, 0 warnings | Profile check (.NET Standard 2.1) | Tests executed |
|---|---|---|---|
| Wayfinder.Core | yes (9/9 files) | yes | 7 |
| Wayfinder.Simulation | yes (7/9 files; `VehicleController.cs`, `ChaseCameraBehaviour.cs` parse-checked only) | yes | 14 |
| Wayfinder.World | yes (2/2) | yes | (covered via Progression/Core tests + Python mirror) |
| Wayfinder.Progression | yes (6/6) | yes | 14 |
| Wayfinder.Persistence | yes (2/2) | yes | 8 |
| Wayfinder.Tests.EditMode | yes (5/5) | n/a | 49 total (6 ContentCatalog) |
| Wayfinder.Platform / Presentation / Editor / Tests.PlayMode | **parse-checked only** | no | none |

---

## 5. What this evidence does NOT prove

- Nothing was compiled by Unity's own build pipeline (Bee/Roslyn as shipped with 6000.3.x), so
  Unity-specific analyzers, `.asmdef` option differences and package resolution are still unverified.
- The 12 Unity-dependent files are **syntax-valid C# 9** only; their use of `UnityEngine` /
  `UnityEditor` / Input System / URP APIs is **not type-checked** (KI-003 remains open for them).
- NUnit here is 3.13.3; Unity Test Framework embeds NUnit 3.5. The suites use only the common
  attribute/assert surface, but UTF's runner was not exercised.
- Newtonsoft is 13.0.3 built from source; the project pins Unity's `com.unity.nuget.newtonsoft-json`
  package (a 13.0.x build). Same major; the behaviour relied on (`JsonSerializerSettings`,
  `ObjectCreationHandling.Replace`, `DateParseHandling.None`) exists in every 13.0.x, but the
  package binary itself was not used.
- No PlayMode test, physics, device, frame-time, thermal, build or store evidence exists (KI-006,
  KI-009 unchanged).

## 6. Reproduce

```bash
python3 Tools/Build/headless_cs.py all         # bootstrap (once, ~1 min) + compile + test + profile + syntax
python3 Tools/Build/headless_cs.py test -- --where "class =~ PersistenceTests"
WAYFINDER_HEADLESS_CS_HOME=/tmp/wf-cs python3 Tools/Build/headless_cs.py all   # fresh cache
```

Requirements: Python 3.9+, Linux x86_64 (the pinned runtime wheel is manylinux x86_64), access to
the hosts listed at the top of this file. Outputs land in `Logs/headless/` (git-ignored); copy
curated results into `Docs/evidence/` when they are cited.
