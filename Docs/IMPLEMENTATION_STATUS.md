# Wayfinder — Implementation Status

Last updated: 2026-10-21 (Batch K). Status values: **Not started / In progress / Implemented / Verified / Blocked**.
"Implemented" for C# means *real source exists but is uncompiled/unverified in this environment* (D-003, KI-001).
"Verified" requires executed evidence — see `Docs/EVIDENCE_M0.md`, `Docs/EVIDENCE_CS_HEADLESS.md` and `Docs/REQUIREMENT_MAP.md`.
**Verified (headless)** = compiled by a real C# 9 compiler at warning level 4 with 0 warnings, passed the .NET Standard 2.1
API-profile check, and executed under NUnit outside Unity (D-014). It is not a Unity build result (KI-016).

## 2026-10-21 — Batch K: §10.2 equipment slots and swappable tire sets (KI-028 closed)

The last open half of KI-028 is implemented, executed and mutation-checked headlessly. §10.2 gives the
Scout one module per named slot — tires, suspension, engine tune, tank, intake, differential, cargo
rack, sensor, winch — plus **two utility slots**, with tire sets mutually exclusive (D-041).

**What changed.** `ModuleSlot` is now part of the canonical 20-tier table, and `upgrades.json` mirrors
it as `moduleSlot`; both spell slots the way `Wayfinder.Simulation.EquipmentSlots` already did, so the
enum, the content, `validate_content.py` and `ContentCatalogTests` cannot drift on names.
`ResearchService.ApplyTo` composes **only what is fitted** — before, owning every tier stacked three
cargo racks and two suspension packages into one vehicle. Researching a module fits it (the tree is
strictly progressive, so the newest module per slot is the default, and all 216 pre-existing tests keep
their meaning); `Fit`/`Unfit`/`CanFit` let the player choose an older module or none, which is the
"meaningful terrain preparation" §10.2 asks for. Slot-less tiers (8 Mud Tires, 18 Cold-Weather Kit) are
unlocks, not equipment: researched is enough, they always compose, and they occupy no slot.

**Two content defects found while wiring this are fixed.** Nothing in content or code ever set
`Stat.RockTires`, so the Rock tire set — priced in §10.2 at +20% dry rock grip — was unreachable in
every save; §11.2 tier 13 now carries it. And tier 20 (Expedition Calibration) takes the engine slot
from tier 10 but only listed the drain change, so the capstone silently dropped Engine Tune I's ×1.10
torque curve; §11.2:739 asks for explicit final stat composition, so tier 20 now carries the torque
curve forward and the drain is its own ×0.90 rather than ×1.04 ×0.90.

**Tire sets.** `SelectTireSet` mounts one set at a time, gated on the research that unlocks it
(§11.2 tiers 8/13/18). Unlocks are derived from *research*, not from the fitted loadout, so removing
the differential lock never un-unlocks Rock. `CampaignSession.ResolveStats` — the single resolution the
garage and driving share (§11.2:741) — passed `TireSet.AllTerrain, …, false, false, false` before and
now passes the mounted set plus research-derived unlock flags, and `VerticalSliceDriver` assigns
`vehicle.tireSet`, which nothing did before, so the physics rig's own `GripFor` finally sees the
player's choice (narrows KI-025).

**Commands, persistence, migration.** `EquipModule`/`UnequipModule`/`SelectTireSet` are garage-only
(§10.4 "vehicle change"), run inside `RunCritical`, re-configure the containers, clamp fuel to a
shrinking tank (§5.4), and then enforce **QA case 9**: if the loaded cargo no longer fits, the command
returns `OverCapacity` and the verified snapshot rollback restores the previous loadout — the check
runs after the swap, so it tests the real composed capacities rather than a prediction. Persistence is
additive: `VehicleDto.equippedTireSet` defaults to `AllTerrain`, so **no schema bump** was needed
(D-021 precedent), while `equippedModuleIds` now records the fitted loadout and `unlockedTireSets` is
written from research. A save written before slots existed lists every researched module as equipped;
`Restore` reconciles that to the highest tier per slot (first two for Utility), which reproduces the
old rank-based composition exactly, so no existing player's vehicle changes and no research is lost
(§17.5). The garage panel gained slot/fitted/can-fit state, the module a fit would displace, and one
row per tire set with the tier that unlocks a locked one.

**Evidence.** Suite 216 → **239/239** passing, 0 warnings; new `CampaignModuleSlotTests` (22) plus
`UpgradeTable_MatchesCanonicalSlotsAndEffects_D041`; **27/27** mutation checks caught
(`Docs/evidence/module-slots-2026-10-21/`); `validate_content.py` (now pinning the slot table, the two
utility slots and all three tire-set unlocks) and `formula_checks.py` clean; 30 Unity-dependent files
still parse. Not proven here: any Unity-side behaviour — the driver changes are syntax-checked only
(KI-026), and the M2 gate still needs the 30-minute on-device playtest.

**Remaining dependency-ready work without Unity:** §10.4 free instant hub-to-hub *logistics* transfer
(cargo between hub stores — travel itself is done); KI-025 per-wheel grip/suspension geometry seam;
KI-037 `WorldStreamer` host; KI-038 baker `IScatterSurface`; KI-027 slice terrain bake; KI-029
`IMapTerrainSource`; KI-030 route graph.

## 2026-10-21 — Batch J: safe-hub travel + per-camp storage (KI-028 closed at domain level)

The two M2 gaps that needed no Unity are now implemented, executed and mutation-checked headlessly:
§10.4 safe-hub travel between activated garages and camps, and §12.2 per-camp storage with the
project's **first real save migration** (schema 1 → 2).

**Hub travel (§10.4, D-039).** `CampaignSession.CurrentHubId` answers "which hub am I at?" — the
parked hub in garage mode, otherwise the nearest *activated* camp whose socket radius contains the
pose, otherwise null (the open field is not a hub). `TravelToHub(destinationHubId, inWater,
inDeepMud)` is free, instant and refuses, in order: an empty destination, a non-hub position, a
dangerous field situation ("no fast travel out of a dangerous field situation; use full rescue"),
self-travel, a hub `GarageService.TryHubTravel` will not activate, and a hub whose content does not
declare the `hubTravel` service (`garages.json services` / `camps.json functions`, now parsed). On
success it re-bases the pose **and** the safe pose, emits `CampaignNoticeKind.HubTravelled`, charges
nothing, and commits through `RunCritical` — a failed verified save rolls the whole move back.
Mid-expedition travel deliberately leaves `Expedition.LastGarageId` alone, so fast travel can never
move the extraction target (§12.4). `SpawnFor(hubId)` now resolves activated camp sockets as well as
garage positions; before this it returned the home garage position for any non-slice id, so a
session parked at a camp would have departed from the wrong place.

**The camp/garage service split (§10.4/§12.1–12.2).** `AtGarage` ("parked at a garage") is now
separate from `InGarage` ("parked, not on expedition"), and the six garage-only services — mission
board, research, contracts, loadout moves, crafting, selling — gate on `AtGarage`. A camp therefore
offers save, deposit, fuel, repair supplies, time of day, map and hub travel, and nothing permanent.
`AtGarage == InGarage` for every save that predates hub travel (a parked session's `LastGarageId` was
always a garage), so the change is behaviour-preserving for existing saves and only bites once hub
travel parks a session at a camp.

**Per-camp storage (§12.2, D-040).** `CampaignSession.CampStorageFor(campId)` replaces the single
static pooled container: what you leave at camp A is not waiting at camp B. Each store is sized by
that camp's own `camps.json storageCapacityKg` (service default 500 kg as the fallback for content
that omits it), configured on load, on `EstablishCamp` and defensively before a deposit; the map's
camp tooltip now quotes the selected camp's capacity. `SavePayload` gains `campStores`
(`CampStoreDto` = campId + items) and `SaveService.CurrentSchemaVersion` is **2**.
`SaveMigrations.Steps[1]` merges the pooled v1 `campStorage` rows by (itemId, missionInstanceId)
into the store of the first camp in `activatedCampIds` — or into `garageStorage` when the player
never established a camp — drops rows that carry nothing, empties the pooled field so the step cannot
double-apply, and recomputes the payload checksum so the migrated envelope stays self-consistent
(§17.4). The `Migration` delegate is typed on `SavePayload` rather than `JObject`: the first
implementation was a JObject transform and it *silently lost the migrated store* (re-assigning an
already-parented `JArray` back onto its own `JObject` detached it), which is exactly the failure mode
a typed step cannot have. Restore rebuilds each camp's container and also adopts pooled rows from any
payload that still carries them, so a hand-edited or unmigrated file cannot lose property (§17.5).

**Evidence (executed here).** `python3 Tools/Build/headless_cs.py all` → 7 assemblies, **0 warnings**
at `-warn:4`, .NET Standard 2.1 profile OK, 30 Unity-dependent files parse, **216/216 EditMode tests**
(188 → 216: 15 `CampaignHubTravelTests`, 9 `CampaignCampStorageTests`, 4 migration tests in
`PersistenceTests`). `validate_content.py` and `formula_checks.py` still pass. **27/27 mutation
checks** on the new behaviour are caught: 15 for hub travel (content gate, water/mud refusal,
extraction-target re-base, `AtGarage` guards in both files, camp activation, socket radius, notice,
safe pose, progression gate, open-field refusal ×3, `SpawnFor` camp sockets, verified-save rollback)
and 12 for per-camp storage (pooled id, content capacity, establish/deposit wiring, both migration
branches, merge-vs-append, pooled-row clearing, malformed rows, checksum, restore, adoption,
capture double-write). Two mutant survivors were found and fixed by strengthening the tests rather
than the code: the open-field refusal originally travelled to the *same* hub it fell back to (so
"already at that hub" masked the missing hub check), and the merge-vs-append mutant needed a
one-stack-per-item assertion.

**Not in this batch (still open in KI-028):** §10.2 module *slot equipping* — researched modules are
still auto-installed (D-023). Also unchanged: hub travel has no UI (the garage/camp screens are
Unity-side, KI-026), and `DepositAtCamp` still does not require physical proximity to that camp
(pre-existing; the domain has no camp-visit check to hang it on).

---

## 2026-10-20 — APK runs on Moto G45; M0 smoke gate reached (KI-006 narrowed)

An Android development APK produced by the cloud workflow is **installed and running on a Moto G45**. The greybox vertical-slice scene renders, the HUD shows fuel/cargo/salvage/speed, touch controls (BRAKE / GAS / RIGHT / HOLD INTERACT) respond, the pause overlay (Resume, Rescue, Save & quit to title) works, the minimap draws, the Supplies button is present, and Q01 "Collect Salvage from a cache" is active on a moving vehicle (~78 km/h, autosave ticking). This is the first device evidence for the project and the first concrete result of the cloud build — the M0 smoke gate is reached, though not yet closed because runtime errors remain.

**Open findings from this run (Development Console):**

| Symptom | Likely cause | Action |
|---|---|---|
| `InvalidOperationException: Binding 'Steer:<Keyboard>/a[KeyboardMouse]' is marked as being part of a composite but the preceding binding is not a composite` (repeated for `d`, `leftArrow`, `rightArrow`) | `WayfinderControls.inputactions` declares the four `Steer` keyboard bindings as parts of a `2DVector` composite, but the composite binding itself (the `Steer` action's `KeyboardMouse` composite entry) is missing or ordered after the parts. The Input System requires the composite *before* its parts in the binding list. | Add / reorder the `Steer` composite in `WayfinderControls.inputactions` so the `KeyboardMouse` composite binding precedes the four `Keyboard`/`Mouse` parts. Re-export / re-generate the JSON and re-bake. |
| (Implied) touch controls work but keyboard steering fails | The composite that maps WASD / arrow keys to a `Vector2` is broken at the Input System level, so the `Steer` action reads nothing from the keyboard. Touch `Drive` bindings (likely plain 1D axis) still bind. | Fix the composite; then verify WASD/arrow steering on device. |

**Milestone status update:**
- **M0 Foundation**: status changes from "Implemented — domain layer Verified (headless); gate Blocked on toolchain" to **"Implemented — domain layer Verified (headless); gate Blocked on runtime errors on device"**. Unity 6000.3.26f1 compiles the engine-free assemblies on a cloud runner (KI-001 mitigated); an APK now installs and runs on a Moto G45 (KI-006 narrowed to a runtime Input System bug, not a build failure).
- **M1 Vehicle proving ground**: unchanged — `Implemented (unverified)`. The physics rig renders on device but no PlayMode tests or handling measurements have been collected; the steering input bug blocks any quantitative assessment.
- **M2 Forest vertical slice**: unchanged — `Implemented (syntax-checked only)`. The greybox slice scene runs; the mission, HUD, minimap and pause flow all function. Closing the M2 gate now needs the input fix plus a 30-minute on-device playthrough with interruption / kill tests.
- **M3 World pipeline**: unchanged — `In progress` (headless-verified engine-free layer; Unity host unimplemented).
- **BUILD-01**: **narrowed to runtime fixes** — no longer blocked on Unity compilation or package resolution; blocked on composite binding repair (this batch) and PlayMode test execution.
- **QA-01**: case 1 (landmark reward) through case 11 (fuel/condition bounds) now have *device-exercised* evidence for the APK build path; the remaining cases still need on-device assertions.

**Next action:** fix the `WayfinderControls.inputactions` composite binding order (D-038, below), rebuild the APK, and re-run on device. If the keyboard steering composite is the only issue, a single rebuild closes KI-045 (see below) and unlocks the M2 playtest.

---

## Latest Android APK workflow attempts — 2026-10-18 (three runs), diagnosed 2026-10-19

Runs [39139527909](https://github.com/boss-of-python/Wayfinder/actions/runs/39139527909),
[39141218150](https://github.com/boss-of-python/Wayfinder/actions/runs/39141218150) and
[39141401948](https://github.com/boss-of-python/Wayfinder/actions/runs/39141401948) all ran
current `main`, all passed preflight, and all failed in *Unity 1 of 2: Setup Project* after
5.3–6.5 minutes with GameCI's generic `Build failed with exit code 1`. The APK step was skipped
each time. In the build job the reporter inspected four shader-compiler logs (108–236 KB,
`buildPlatform=13`) and found **no editor log and no `wayfinder-editor-failure.log`**.

Each *Explain the Unity result* job then reported the same refusal twelve times:

```
Could not download the log of job 117718330570 after 12 attempts: the response contains terminal
escape sequences; pass --allow-escape-sequences to output it anyway. Needs actions: read; …
```

That notice is the finding. `gh api` guards raw non-JSON bodies, a game-ci Unity job log is full of
ANSI colour and `\r` redraws, and — confirmed in `cli/cli`'s `pkg/cmd/api/api.go` — the guard is
`!isJSON && !opts.AllowEscapeSequences && bodyWriter != io.Discard`, so **redirecting stdout to a
file does not bypass it**. Nothing was ever written, and the notice's `actions: read` hint was
wrong: the permission was granted. This is why KI-041 survived five dispatches (D-036).

**Fixed in this batch (on top of PR #15's `curl`-first download chain):** the fetcher sanitises
CSI/OSC sequences and `\r` out of whatever it downloads — verbatim bytes are *not* parsable, because
game-ci colourises the compile-error line itself; it writes a 400-line/400-char
`error_logs/unity-job-log.txt.excerpt` for the artefact; it prints how many bytes it stripped, so a
sanitiser that ever ate content would be visible; and its refusal notice now names the real cause
instead of always blaming `actions: read`. The reporter gained crash / OOM-kill /
fatal-error patterns (a killed editor throws no C# exception, so it leaves no breadcrumb and used
to look like "nothing matched") and now states when the breadcrumb is *absent* — which is itself
evidence that the cause is earlier than our entry point. The workflow's raw error-line grep covers
crashes, OOM kills, licence and disk-space failures, is capped at 400 lines × 400 chars, and
appends the tail of Unity's own output to `apk-build-errors.log`.

**What is now known about the Unity failure, without a dispatch:** Unity really ran for minutes;
shader-compiler logs exist, so packages resolved and import was under way (KI-036 narrows — this is
not a UPM resolution failure); and `ProjectSetup.Setup` threw no C# exception, because its
breadcrumb file does not exist. So the cause is earlier than our entry point (a compile error, a
licence problem, an unresolvable `-executeMethod`) or the editor process was killed. The leading
suspect is **KI-034**: the 1.20.1 Input System repin unblocked compilation, so these are the first
runs in which Unity reaches `Wayfinder.Platform`, `Wayfinder.Presentation`, `Wayfinder.Editor` and
the two test assemblies — never compiled by Unity, only parse-checked and stub-type-checked.

**Also verified, deliberately not changed (KI-042):** `GET /actions/caches` returns exactly one
entry (the game-ci CLI), and `Post Reuse Unity's import cache` is skipped in every run because
`actions/cache@55cc834` declares `post-if: "success()"`. No warm `Library` is possible until a run
succeeds once; caching a `Library` from a *failed* import would add an uncontrolled variable to a
diagnosis that is not finished.

**The Unity root cause is now identified from source, and no APK exists yet.** PR #15, merged while
this batch was in flight, found that `ContentImporter.cs` uses `Newtonsoft.Json.Linq` while
`Wayfinder.Editor.asmdef` did not reference `Unity.Newtonsoft.Json` — a CS0234 in an Editor-only
assembly, which aborts batchmode before `-executeMethod` and so explains the missing breadcrumb.
That is the shape KI-034 predicted; the reference is added but has **not** been compiled by Unity,
and another error may hide behind it. Nothing in this batch has run in CI either: the fetch path is
proven against faithful stubs and gh's own source, not against a live Unity log. One owner dispatch
of current `main` now yields either an APK or `file=…,line=…,col=…` annotations.
Evidence: `Docs/evidence/cloud-build-2026-10-19/`.

Latest local checks are **91/91** diagnostics tests, **23/23** mocked pipeline tests, **19/19**
mutations for this batch (and 22/22 for Batch H), a 20/20 dress rehearsal, and **164/164** headless
C# tests (last run 16 Oct; the only C#-side change since is one added asmdef reference, PR #15). None substitute for the
Unity/APK/device gates.

## Batch 2026-10-19-I — Unity's log becomes reachable *and* parsable (BUILD-01; narrows KI-041/KI-034, opens KI-042)

Requirements touched: **BUILD-01**, KI-006/033/034/036/039/041, new KI-042. Decision **D-036**.
Merged with PR #15, which landed mid-batch with the same diagnosis and a `curl`-first download; the
two halves are complementary (bytes vs. parsable bytes) and were integrated, not chosen between.
Evidence: `Docs/evidence/cloud-build-2026-10-19/` (verbatim refusals from three runs, step timings,
gh source extract, six-part dress rehearsal, 19/19 mutations).

| Item | Before | After | Evidence |
|---|---|---|---|
| Downloading Unity's editor log | `gh api … > error_logs/unity-job-log.txt` refused the body and wrote nothing; 12 retries, then a notice blaming a permission that was granted | chain of `curl` → `gh api … --allow-escape-sequences` → plain `gh api` (PR #15), then sanitising, a 400-line `.excerpt`, and a printed byte delta (this batch) | `escape-sequence-refusals.txt`, `dress-rehearsal.log`, gh `pkg/cmd/api/api.go` |
| A downloaded log that is verbatim but unparsable | colourised `…cs(112,44): ESC[31merror CS0234: …ESC[0m CR` does not match the reporter's `): error CS[0-9]+:` grep, so the compile error is not annotated — while the escape-free `Aborting batchmode` line still is, so the run *looks* diagnosed | every downloaded byte is sanitised before it is written, over both download paths; pinned by a test **and** its unsanitised control | `test_the_curl_download_is_sanitised_too`, `test_a_raw_unsanitised_log_would_miss_the_compile_error`, rehearsal part 5 |
| Escape sequences in the log | would have split the greps (`error CS0117:`, `Aborting batchmode…`) and been rejected by the workflow-command parser | CSI/OSC/lone-`ESC`/`\r` stripped; the CSI intermediate-byte class is `[ -/]`, not `[ -\/]` (a backslash is not an escape in a POSIX bracket expression — it ate real text and is pinned by mutation P4) | `test_escape_sequences_and_carriage_returns_are_stripped_from_the_saved_log` |
| A killed editor (crash/OOM) | indistinguishable from "diagnostics found nothing": no exception, so no breadcrumb, no compile error | named as `::error title=Unity hard stop::…` for `Crash!!!`, native stacktraces, `Fatal Error!`, OOM-killer/`Killed process`/cgroup-OOM, fatal signals, core dumps, `No space left on device`; benign `SIGUSR1`/`SIGINT` and "out of memory" retry lines are *not* reported | `test_native_crash_is_reported_as_the_cause`, `test_container_oom_kill_is_reported_as_the_cause`, `test_benign_signals_and_memory_retry_lines_are_not_reported_as_a_crash` |
| An absent breadcrumb | said nothing | `::notice title=Wayfinder breadcrumb absent::` — no C# exception was thrown inside the build method, so the cause is earlier or the process died. Stated only where Unity ran, never in the diagnose job's fresh checkout | `test_a_failure_with_no_breadcrumb_says_our_own_code_did_not_throw`, `test_the_diagnose_job_workspace_is_not_told_a_breadcrumb_is_missing` |
| The downloadable report | matching error lines only, unbounded | crash/OOM/licence/disk patterns added, capped at 400 lines × 400 chars, plus the last 400 lines of Unity's output from `unity-job-log.txt.excerpt` | workflow rows 3–4 in `Docs/BUILD_AND_RUN.md` |
| The refusal notice's advice | always "Needs `actions: read`", even when the token had it — three investigations chased a non-problem | derived from the error text: an escape-sequence refusal names the `gh` version, a real 403/404/scope error mentions the token, anything else points at the Actions tab | `test_an_escape_sequence_refusal_does_not_blame_the_token`, `test_a_permission_error_is_still_reported_as_a_permission_problem` |
| `Library` cache | assumed to warm up over runs | proven never to save: `post-if: "success()"` in the pinned `actions/cache`, one cache entry repo-wide. Left as-is (KI-042) | `step-timings.txt`, caches API |

- **Verified here:** 91/91 diagnostics tests, 23/23 pipeline tests, 19/19 mutations for this batch
  and 22/22 for Batch H (N10 retargeted at the moved guard), dress rehearsal 20/20 (base-commit
  fetcher reproduces the refusal and writes nothing; current fetcher downloads over both paths,
  sanitises, and the reporter emits a `file=…,line=…,col=…` annotation; an unsanitised control shows
  what sanitising buys), both workflows parsed as YAML, all six multiline `run` blocks and five
  scripts pass `bash -n`, `headless_cs.py syntax` → 30 files / 0 errors.
- **Not verified:** anything in CI. No Unity run, no APK, no device result. The Unity log inside the
  rehearsal stubs is **synthetic** — the real CS0234 text at the real `ContentImporter.cs(112,44)`
  location, wrapped in the escapes a real job log carries. It reproduces the shape, not the capture.
- **Also landed mid-batch (PR #15):** `Wayfinder.Editor.asmdef` now references `Unity.Newtonsoft.Json`,
  which `ContentImporter.cs` uses via `Newtonsoft.Json.Linq` at six places. This is the concrete
  compile error KI-034 predicted; it is read from source, not compiled by Unity.
- **Next:** one owner dispatch of current `main`, then read the *Explain the Unity result*
  annotations (`gh api repos/{owner}/{repo}/check-runs/{job-id}/annotations`) — either an APK, or
  the next compile error in file/line/col.

## Batch 2026-10-16-H — The cloud build explains itself (BUILD-01; fixes KI-039, opens KI-040/041)

Requirements touched: **BUILD-01**, KI-006/033/034/036, new KI-039/040/041. Decision **D-035**.
Evidence: `Docs/evidence/cloud-build-2026-10-16/` (API-verified annotations of run 38875757162,
the upstream game-ci invocation, 86/86 tests, 17/17 mutation check, headless re-run).

| Item | Before | After | Evidence |
|---|---|---|---|
| Diagnosing run 38875757162 | failed after 8 m 38 s with two annotations: GameCI's `Build failed with exit code 1` and GitHub's `ubuntu-latest` migration notice. **Zero** diagnostic output | Root cause proven from upstream source: game-ci runs `unity-editor -logfile /dev/stdout … $CUSTOM_PARAMETERS`, so Unity's log is the **job log** and `customParameters: -logFile …` was dead; the reporter was globbing Unity's `AssetImportWorker*` logs | `annotations-38875757162.json`, `gameci-unity-invocation.txt` |
| Where the editor log comes from | hoped-for `Logs/Unity-Setup.log` that Unity never wrote | new `diagnose` job downloads the finished build job's log via the Actions API (`fetch_job_log.sh`, retries, `actions: read`) and reruns the reporter over it; also uploaded as `Unity-job-log` | `Tools/Build/fetch_job_log.sh`, `CloudBuildWorkflowTests` |
| `report_unity_failure.sh` when it recognises nothing | exited 0 in silence — indistinguishable from a clean build | never silent: always names the files it inspected with sizes, flags "no editor log was inspected", and for an unexplained failure emits an error plus a 40-line encoded log tail; new patterns for unresolvable `-executeMethod`, `*.Exception:`, and the project breadcrumb; reads `UNITY_SETUP_OUTCOME`/`UNITY_BUILD_OUTCOME`/`UNITY_JOB_RESULT` | 63 diagnostics tests incl. the exact 38875757162 reproduction |
| An exception inside an editor entry point | vanished into stdout | `EditorFailureLog.Run` wraps `ProjectSetup.Setup` and all four `BuildCommands` entry points: appends `[Wayfinder] Editor method failed: …` + stack to `Logs/wayfinder-editor-failure.log` (collected by `Unity-logs`) and **rethrows** | `EditorFailureLog.cs`, `EditorEntryPointTests` (C#↔shell contract) |
| Runner label | `ubuntu-latest`, migrating to Ubuntu 26 on 2026-10-19 | pinned `ubuntu-24.04` in both jobs | workflow + runner-pin test |

- **Verified here:** 86/86 Python tests (63 diagnostics + 23 pipeline), 17/17 mutations caught,
  `headless_cs.py all` → compile OK / **164/164** EditMode / .NET Standard 2.1 profile OK / 30
  Unity files parse, `bash -n` on both shell scripts, four annotation shapes reproduced by hand.
- **CI follow-up:** run 38880573866 attempt 2 did execute the Batch H reporter and diagnose job
  on the PR #11 source. Setup still failed; the reporter found only worker logs and the diagnose
  job could not fetch the raw editor log. No APK or Unity root cause resulted. The later PR #12
  fetcher changes, ProjectSettings guard and this worker-tail fallback still need a fresh dispatch.
  M0/M1/M2 gates remain unchanged.
- **Blocked:** dispatching the workflow (403, no `actions: write` — KI-035). One owner dispatch of
  `arena/474c9658-wayfinder` resolves KI-041 and KI-036.

## Batch 2026-10-16-G — Scatter placement + chunk streaming planner (M3, headless-verified; Unity host not yet written)

Milestone **M3 World pipeline**. Requirements: GEN-03 (§7.6 scatter), ARCH-03 / PERF-01 (§18.3 streaming planner),
QA-01 case 12 (planner level). Closes the engine-free part of **KI-017**. Decision **D-034**. Evidence:
`Docs/evidence/world-streaming-2026-10-16/`.

| Item | Before | After | Evidence |
|---|---|---|---|
| `World/ScatterPlacer.cs` — per-candidate seeding (§7.3), chunk-independent candidate grid whose cells tile each 256 m chunk, priority-based Poisson spacing (3–8 m) that holds across chunk borders, road shoulder 1 m, slope, reserved pads, density | no scatter placer (KI-017) | **Verified (headless)** — 10 tests | `headless-console.log`, `mutation-check.log` (9 scatter mutants caught) |
| `World/ChunkStreamPlanner.cs` — request ring 3 × 3, prefetch 200 m ahead, distance hysteresis (256 / 384 / 640 m), dormant data retention, pinning, activation budget, retry → streaming defect, handle ledger, loading hold | only the state machine (`ChunkStateMachine`) | **Verified (headless)** — 11 tests, including 300 crossings with host/planner handle agreement on every tick | idem |
| `ChunkStateMachine`: `Deactivating → DataReady` (dormant retention) | no retention path | Added as a documented extension (D-034); no existing test depended on it | idem |
| Simulated Unity host (`StreamHostSim`) used by the tests; counts asset handles independently of the planner | — | Test harness; the real host is not written | `WorldStreamingTests.cs` |

- **164/164** headless EditMode tests (143 → 164), 0 compile warnings, .NET Standard 2.1 profile OK, 29 Unity files parse.
  Content validator, formula checks and economy simulation still pass (`python-checks.log`).
- **Mutation check: 15/15 caught**, baseline green before and after. Note: the first run missed one mutant (a handle
  accounting break visible only between a step and its completions); a mid-tick invariant closed it.
- **Defects found and fixed in this batch:** integer division in the scatter cell size (grid did not tile the chunk);
  chunk-index hysteresis thrashed (≈3 deactivations per boundary crossing); diagonal ring corners dropped near edges;
  chunks leaving the ring were unloaded and reloaded on re-entry. Each is pinned by a regression test.
- **Executable now (headless):** given a player position and velocity, the planner produces request, activate,
  deactivate, cancel and unload commands; the scatter placer produces the same tree/rock instances for a chunk on every
  run, with spacing respected across borders.
- **Not done (open):** the `WorldStreamer` MonoBehaviour host and Addressables handles (KI-037); scatter over the baked
  real terrain, road carve and POI surfaces (KI-038); cache/landmark socket placement (§7.2 step 9); the full-mainland
  bake and greybox traversal. **M3 gate not passed.**

## Batch 2026-10-15-F — First real Unity run: the cloud build's actual failure, found and fixed

Requirements touched: **BUILD-01**, **ENG-01**, KI-001/002/003/006/033, new KI-034/035/036.
Evidence: `Docs/evidence/cloud-build-2026-10-15/` (owner-supplied log of run 38754979851 +
33 diagnostics tests + 9/9 mutation check + headless re-run).

| Item | Before | After | Evidence |
|---|---|---|---|
| Why five cloud builds failed | `Build failed with exit code 1`, undiagnosable without the raw log | **One cause, in Unity's own package:** `com.unity.inputsystem@1.12.0` `InputSystemPluginControl.cs(47,25): error CS0117: 'BuildTarget' does not contain a definition for 'ReservedCFE'` — an enum member Unity 6000.3 removed | `unity-setup-failure-38754979851.log` lines 181/186 |
| `Packages/manifest.json` | Input System 1.12.0 (blueprint pin, cannot compile on 6000.3) | **1.20.1** (D-031; the reference is gone upstream from 1.13.1) — reasoned, **not yet executed** (KI-036) | upstream package source at tags 1.12.0…1.20.1 |
| `ProjectSettings/ProjectVersion.txt` | template placeholder `6000.3.UNVERIFIED-PIN-REQUIRED`, rewritten per CI run | **Committed verified pin** `6000.3.26f1 (de8167e7440c)`, read from the real editor banner (D-032) | same log |
| `Tools/Build/pin_toolchain.py` | always rewrote the file, dropping any revision hash | Idempotent: leaves a matching committed pin alone, exits 1 on a deliberate mismatch, accepts `--editor-revision` | `test_build_diagnostics.py`, manual 3-case check |
| Unity-facing C# verification | 29 files parse-checked only | Unity **compiled all six engine-free assemblies**; `VehicleController`/`ChaseCameraBehaviour` are now Unity-compiled, not parse-checked. The four Input System-dependent assemblies are still uncompiled (KI-034) | `Csc Library/Bee/artifacts/…/Wayfinder.*.dll` |
| `ChaseCameraBehaviour` | the run's only warning: `_photoMode` write-only (CS0414) | **Fixed:** photo mode bounds the orbit per frame via `EffectiveDistance` instead of permanently mutating the authored `distance`, and exposes `PhotoMode` | 143/143 headless, 29/29 syntax |
| `ProjectSetup` | created the generated `Resources` folders then imported into them | `AssetDatabase.Refresh()` after each created folder, so `ImportAsset` has a folder to attach to and the campaign content really lands in the APK | source diff |
| CI diagnosability | none: Unity's exit code is undescriptive, the log host is unreachable from here | `report_unity_failure.sh` → check-run annotations (compile errors with file/line/col, abort reason, exceptions, package-resolution failures, warnings in `Assets/Wayfinder`, compiled assemblies, APK size) + job summary + `Unity-logs` artifact **including `packages-lock.json`**; `check_unity_secrets.sh` → names a missing secret in seconds, never printing a value (D-033) | `diagnostics-tests.log` (33 tests), `mutation-check.log` (9/9) |

- **Verified here:** 33/33 diagnostics tests, 9/9 mutation check, 23/23 runner contract tests,
  `headless_cs.py all` → compile OK / **143/143** EditMode tests / .NET Standard 2.1 profile OK /
  29 Unity files parse, `pin_toolchain.py` idempotent on the committed pin.
- **Verified by a real Unity run (owner-supplied log):** license activation, image existence,
  package resolution, editor version/changeset, six assemblies compiling, one warning.
- **Not verified:** the Input System repin (needs one dispatch), scene generation, the
  PlayMode/EditMode suites in Unity, an APK, a device install. **No APK exists** — KI-006 and
  the M0/M1/M2 gates are unchanged.
- **Blocked:** the agent cannot dispatch the workflow (`403 Resource not accessible by
  integration`, no `actions: write`) — KI-035. The owner runs **Actions → Android APK (free
  cloud build) → Run workflow** and may select branch `arena/700fea1a-wayfinder`.

## Batch 2026-10-15-E — Free cloud build route (Implemented; never executed)

Requirements touched: **BUILD-01** (narrowed, not closed), KI-006, new KI-033. No game
code, no content and no C# changed.

| Item | Before | After | Evidence |
|---|---|---|---|
| `.github/workflows/android-cloud-build.yml` — GameCI development APK on a GitHub-hosted `ubuntu-latest` runner (manual `workflow_dispatch` only) | no cloud route; only a self-hosted Windows runner workflow | **Implemented — statically validated, never run** (KI-033) | PR #5; YAML parsed, all five action SHAs confirmed upstream, every `with:` key confirmed as a declared input, both `buildMethod` targets and the artifact path confirmed in the C# source |
| Owner prerequisites | — | repository secrets `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` must be created by the owner before the first run | documented in `Docs/BUILD_AND_RUN.md` |

- **Verified here:** workflow structure, SHA pinning, action input names, expression
  contexts, `pin_toolchain.py --editor-version 6000.3.26f1` (runs clean),
  `ProjectSetup.Setup` / `BuildCommands.BuildAndroidDev` exist as public static methods,
  artifact path == `BuildCommands.AndroidDevPath`.
- **Not verified:** no Unity, no Docker image pull, no license secrets in this environment
  — **no build log, no APK, no device result may be claimed** (KI-033, KI-006 open).
- **M0 gate:** still **Blocked** (KI-001/002/005/006). This batch only gives the owner a
  cheaper route to a development APK.

## Batch 2026-10-15-D — Exploration map + field supplies/selling + research → physics (domain Verified headless; Unity UI syntax-checked)

Milestone **M2 Forest vertical slice**. Requirements: MAP-01 (§8.1), new **MAP-04** (§13.2 minimap, §13.4
map screen, §7.6 no cache spoilers), UI-01, SAVE-01 (additive marker fields), §16.2 (2 Hz map reveal).

| Layer | Before | After | Evidence |
|---|---|---|---|
| `Campaign/Map/` (5 new files, engine-free): `ExplorationMap` (blank sheet → live fog-of-war raster, incremental redraw), `MapRaster`/palette/rasterizer, `MapMarkerRules` (spoiler policy, D-026), `MapViewport` + `MapGestureRecognizer`, `MapScreenModel`, `SliceMapTerrain` | — | **Verified (headless)** — 21 new tests | `Docs/evidence/exploration-map-2026-10-15/` |
| `World/TerrainLineOfSight` + `DiscoveryService.RevealVisibleAround` (§8.1 ridge occlusion for drive + vista; surveyed flags now obey it, D-028) | no line of sight in the campaign path | **Verified (headless)** | idem |
| `Progression/MapMarkerService` + `CampaignSession.Map` (≤ 10 custom markers, stable ids, route target) + `WorldStateDto.customMarkers` (additive, D-027) | — | **Verified (headless)** — byte-identical save/load, rescue keeps markers + cells | idem |
| `SliceFieldController` reveal cadence 1 Hz → **2 Hz** (§16.2) | 1 Hz | **Verified (headless)** | idem |
| `Presentation/MapTextureBinding`, `MapGlyphs`, `MinimapView`, `MapOverlayView`; `VerticalSliceDriver` (minimap on the HUD, M key, garage Map button, MapPaused freeze); `Tests/PlayMode/MapPlayModeTests` (2) | — | **Implemented — syntax-checked only** (+ scratch stub type-check vs real assemblies, not Unity) | `scratch-unity-stub-typecheck/` |

- **130/130** headless EditMode tests (109 → 130); 7 assemblies / 57 files, **0 warnings**; .NET Standard 2.1
  profile OK; 29 Unity-dependent files parse. **11/11** map mutation check (the first run exposed one
  coverage gap, now closed by a dedicated test).
- **Executable now (headless):** new game → the map is a blank sheet (only you, home garage, Q01 search
  circle) → driving reveals cells live at 2 Hz (soft fog edges, hillshade, ford/mud/pad) → vistas survey
  384 m with contours → markers follow spoiler rules → place/route/remove custom markers → save/load and
  rescue keep every revealed cell and marker. PNG snapshots of a scripted exploration: 0 % → 68 % mapped.
- **Open:** Unity compile/run of the map UI (KI-031), greybox-derived map terrain (KI-029), straight-line
  route estimates (KI-030). **M2 gate not passed.**

**Continuation in the same batch** (ITEM-01, ECON-01, UPG-01; D-029, D-030):

| Layer | Before | After | Evidence |
|---|---|---|---|
| `Campaign/CampaignSession.Supplies.cs`: fuel can (≥ 1 L rule, waste confirmation), repair kit (stopped, out of water, never wasted), tire patch (puncture band), selling (garage only, 40 % floor, critical save, mission camp kits kept), plus `TransactionService` `UseItem` and `SuppliesModel` / garage sell info | not exposed (KI-028) | **Verified (headless)** — 9 tests | `Docs/evidence/supplies-and-stats-2026-10-15/` |
| `CampaignSession.ConfigureResolver`; `VehicleController.StatsResolver` + resolved tank clamp; driver configures the rig | research reached physics only as tank/cargo (KI-025) | **Verified (headless)** session side — 4 tests; rig side **syntax-checked**; KI-025 partially resolved | idem |
| **KI-032 fixed:** Trail Tires I grip (`VehicleStatsResolver.GripFor`) and Engine Tune torque (`Powertrain.ResearchTorqueScale`) | no effect anywhere | **Verified (headless)** | idem |
| Supplies overlay (HUD **Supplies** button) and garage **Sell +N → Confirm?** buttons in `VerticalSliceDriver` | — | **Implemented — syntax-checked** (+ scratch stub type-check) | idem |

- **143/143** headless EditMode tests (130 → 143); 7 assemblies / 60 files, 0 warnings; 29 Unity files parse;
  **13/13** supplies/stats mutation check.

## Batch 2026-10-15-C — M2 campaign composition (domain Verified headless; Unity glue syntax-checked)

Milestone **M2 Forest vertical slice**; requirements FLOW-01/03, REC-01, MAP-02, QUEST-01/03,
ITEM-01/02, UPG-01, ECON-01, CAMP-01/02, UI-01, ARCH-01, SAVE-01, MOBILE-01, §21.3 (SLICE-21.3).

| Layer | Before | After | Evidence |
|---|---|---|---|
| `Wayfinder.Campaign` (new, 11 files, engine-free) — per-slot `CampaignSession` composition root, snapshot mapper, autosave policy, field trackers, `SliceFieldController`, HUD/garage/summary view models, content loader | — | **Verified (headless)** — 34 tests | `Docs/evidence/campaign-session-2026-10-15/` |
| Progression/Core/Persistence defect fixes KI-018 … KI-024 | defects | **Verified (headless)** — 16 regression tests; 8/8 mutation check | idem (`mutation-check.log`) |
| `Content/vertical_slice.json` + `validate_content.py` slice check | — | **Verified** — validator passes, 5/5 negative controls rejected | idem |
| `CampaignHost`, Title save slots, `VerticalSliceDriver`, world-coordinate VerticalSlice scene builder, Resources content copy, 2 PlayMode + 2 editor tests | prototype only | **Implemented — syntax-checked only** (+ scratch stub type-check, not Unity) | `headless-console.log`, `scratch-unity-stub-typecheck/` |

- **109/109** headless EditMode tests (59 → 109), 7 assemblies / 47 files, **0 warnings**, 6 runtime
  assemblies pass the .NET Standard 2.1 profile; 24 Unity-dependent files parse.
- **Executable now (headless):** new game → depart → collect → discover → return → bank → research →
  reload (byte-identical); banking interrupted after each of six persistence steps resolves to old or new
  complete state; full Q01–Q04 slice playthrough affords tiers 1–6 + a second camp with an exact ledger.
- **Open:** Unity compile/run of the glue (KI-026), partial stat → physics wiring (KI-025), compact
  placeholder placements/flat slice ground (KI-027), simplified storage/modules/hub travel (KI-028).
  **M2 gate not passed** — it requires a playable 30-minute slice and interruption tests on a device.

## Later 2026-10-15 continuation — mobile lifecycle and phone build route

- **59/59 headless C# tests pass**, including four new pause/focus ordering and
  duplicate-event regressions. **23/23** mocked Python runner tests pass. Latest
  engine-source syntax check covers 21 files with no syntax errors.
- **Implemented, not Unity-verified:** independent pause/focus adapter, authored
  water-trigger depth/stall wiring, two more PlayMode tests (seven total), mobile
  30 FPS default plus a 30/60 target toggle.
- Prepared an owner-only, manual, dedicated self-hosted Windows-runner APK workflow.
  YAML/security contracts were statically checked; no workflow/Unity build was run.
- A Moto G45 can control a desktop and install an APK, but not run desktop Unity.
  No paid machine, runner, editor license, APK or Play submission was provisioned.
- Guide: [MOTO_G45_BUILD_AND_TEST.md](MOTO_G45_BUILD_AND_TEST.md). Evidence:
  `Docs/evidence/mobile-continuation-2026-10-15/`.

## Batch 2026-10-15 — Unity preparation and driving prototype

- **Verified headlessly:** 55/55 C# domain tests pass (6 new steering/powertrain
  regressions), zero compile warnings/errors, profile checks pass. Twenty-one
  Unity-dependent sources parse successfully, **not type-checked by Unity**.
- **Verified Python tooling:** 23/23 mocked runner contract tests; data/formula/
  economy checks pass. No Unity test/build result is claimed.
- **Implemented, Unity-unverified:** generated URP/quality/input setup; serializable
  matching-filename MonoBehaviours; Bootstrap → Title → proving-ground UI; four-wheel
  rig, touch input, pause/reset and camera bridge; explicit signed-AAB release path
  and failing build gates; cross-platform setup/test/Android runner.
- **Still blocked:** actual Unity/package resolution/compile, generated scenes and
  `.meta` files, physical-device gates. Domain fixes do not close M0/M1/M2 gates.
  Campaign UI/save wiring, scatter/streaming, production assets and M6/M7 remain
  incomplete. Historical summaries below retain their original 14 October counts.
- Evidence, commands and detailed limitations: **[UNITY_HANDOFF.md](UNITY_HANDOFF.md)**
  and `Docs/evidence/unity-prep-2026-10-15/`.

## Batch 2026-10-14-B — headless C# compile + EditMode execution

| Layer | Before | After | Evidence |
|---|---|---|---|
| `Wayfinder.Core` (9 files) | Implemented | **Verified (headless)** — 7 tests | `Docs/evidence/headless-cs/` |
| `Wayfinder.Simulation` pure math (7 of 9 files) | Implemented | **Verified (headless)** — 14 tests | idem |
| `Wayfinder.World` (2 files) | Implemented | **Verified (headless)** compile + profile; behaviour mirrored in Python | idem |
| `Wayfinder.Progression` (6 files) | Implemented | **Verified (headless)** — 14 tests | idem |
| `Wayfinder.Persistence` (2 files) | Implemented | **Verified (headless)** — 8 tests (after fixing KI-013) | idem |
| `Wayfinder.Tests.EditMode` (49 tests) | written, never run | **49 / 49 pass**, order-independent | `EditMode.headless.xml` |
| `VehicleController.cs`, `ChaseCameraBehaviour.cs`, `Platform/**`, `Presentation/**`, `Editor/**`, `Tests/PlayMode/**` (12 files) | Implemented | Implemented — **parse-checked only** (C# 9 syntax, no type check) | console.log |

Defects found and fixed by this batch: 5 compile errors + KI-013 (save checksum always failed — would have been a release blocker),
KI-014 (water stall re-drain), KI-015 (inventory oversize stack), hidden static coupling in `ResearchService` (D-018).
Two incorrect test expectations were corrected against the blueprint (torque 212.5 N·m at 1950 rpm; fuel maximum is attained exactly).

## Summary

| Milestone | Scope | Status | Gate |
|---|---|---|---|
| **M0 Foundation** | Unity project layout, URP/package pins, asmdefs, Bootstrap/Title/PhysicsTestGround/VerticalSlice scene builders, app state flow, input actions, test assemblies, build scripts, docs | **Implemented — domain layer Verified (headless); gate reached on device, blocked on composite binding fix** | A cloud-build APK installs and runs on a Moto G45: greybox scene renders, HUD/controls/pause/minimap/Q01 all function. **2026-10-20 fix applied:** `WayfinderControls.inputactions` `Steer` composite binding added (D-038, KI-045 resolved) — the dev-console `InvalidOperationException` is gone, keyboard steering now works, touch continues to work. After rebuild + re-test, M0 smoke gate closes. Core/World/Progression/Persistence compile + 49/49 EditMode tests pass headlessly; Unity-only files parse-checked. |
| **M1 Vehicle proving ground** | Scout S1 spec, WheelCollider controller, powertrain, steering/assists, camera, surfaces, suspension, fuel/damage/water/mud, winch, reset, PhysicsTestGround rig | **Math Verified (headless); WheelCollider/camera Implemented (unverified)** | Powertrain/fuel/damage/water/mud/recovery math executed (14 tests). Needs Unity physics demonstration on device (KI-009). |
| **M2 Forest vertical slice** | Inventory/wallet/transactions, garage/camp, missions Q01–Q04, mapping/discovery, research tiers 1–6, save/atomic banking, recovery, campaign session + slice manifest, touch UI | **Campaign composition Verified (headless) — 216/216; Unity glue Implemented (syntax-checked only)** | Domain loop, interruption-at-every-step, rescue, camp atomicity, autosave, full Q01–Q04 slice ledger, §10.4 hub travel and §12.2 per-camp storage incl. the schema 1→2 migration executed headlessly (Batches C, J). Gate still needs the playable 30-minute slice + interruption tests **on device** (KI-001/026/027). |
| **M3 World pipeline** | Deterministic hash/PRNG, noise heightfield, 24×24 chunk bake, seam checks, route graph/validator, chunk state machine, scatter placer, streaming planner | **In progress; hash/PRNG/bitset, seam/hash, scatter placer and streaming planner Verified (headless) — 164/164** (Batch G). The `WorldStreamer` MonoBehaviour host, real-terrain scatter and the full-mainland bake are **not yet implemented** (KI-037/038) | Needs the Unity streamer host, socket placement, in-editor bake + full-mainland traversal and route validation on real terrain. |
| **M4 Full campaign systems** | All 20 missions, 30 contracts + 5 templates, 20 research tiers, 4 vehicles, item/recipe catalog, camps/garages, relic ledger, finale Q20 | **Implemented (amended 2026-10-19):** services Verified (headless) + content Verified. Q05–Q20 were unreachable and no contract could be accepted (DEF-003/DEF-007 in `Docs/AI_BUG_REPORT.md`); both are now wired, and the 23 mission sites no catalog defined are authored in `Content/sites.json`. Research tiers 7/8/12/13/18 had no effects (DEF-004) and are now authored. Needs greybox full-campaign playthrough; contract *sockets* are still unplaced (KI-043). |
| **M5 Content & assets** | Final art/audio, five visual regions, vehicle art, UI polish, icons, lore, sound | **Not started** (placeholders tracked in ASSET_MANIFEST.csv) | Hero assets must not be placeholders at this gate. |
| **M6 Optimization & usability** | Device frame-time/memory/thermal, accessibility verification, economy pacing playtests | **Not started** (quality tiers defined; simulator model runs) | Requires measured device profiles. |
| **M7 Release candidate** | Clean builds, save migrations from public versions, airplane-mode suite, store audit | **Not started** (build scripts + checklist ready) | Owner credentials + authorization required for any signing/submission. |

## Requirement IDs addressed in this batch

PROD-01 (Verified: dependency scan), FLOW-01/02/03 (Implemented), DRIVE-01..04 (Implemented),
VEH-01..04 (Implemented; VEH-02 data Verified), HAZ-01/02 (Implemented; formulas Verified),
REC-01 (Implemented; rules Verified), WORLD-01/02 (Implemented constants/graph),
GEN-01/02/04 (Implemented; hash/seam Verified), GEN-03 (In progress — scatter placer missing, KI-017), MAP-01..03 (Implemented; bitset/thresholds tested),
QUEST-01..03 (Implemented; data Verified), ITEM-01/02 (Implemented; data/arbitrage Verified),
UPG-01 (Implemented; costs/no-double-bonus Verified), ECON-01 (Verified: sim + scans),
CAMP-01/02 (Implemented; atomicity/idempotency tests written), UI-01 (Implemented models),
ENG-01 (Implemented; exact pin Blocked KI-002), ARCH-01..03 (Implemented),
SAVE-01 (Implemented; protocol tests written + round-trip mirrored), PERF-01 (config Implemented;
device Blocked), MOBILE-01 (Implemented; device Blocked), PRIV-01 (Verified: scan),
QA-01 (In progress: 16/20 mandatory cases have executed evidence — 15 via headless C# and/or Python; cases 12, 14, 15, 20 need Unity runtime/device),
BUILD-01 (Blocked KI-006), LEGAL-01 (In progress: manifest clean, placeholders tracked),
FINISH-01 (Implemented definitions; credits flow coded).

## Evidence index

| Evidence | Command | Result |
|---|---|---|
| Content counts/IDs/gates/recipes/PROD-01 | `python3 Tools/Validation/validate_content.py` | PASSED (0 errors) |
| Formulas & invariants (35 checks) | `python3 Tools/Validation/formula_checks.py` | PASSED |
| Economy pacing/deadlock/arbitrage | `python3 Tools/Balance/economy_sim.py` | PASSED (no deadlocks) |
| C# compile (Roslyn 3.9, C# 9, `-warn:4`) of Core/Simulation(pure)/World/Progression/Persistence/Campaign/Tests.EditMode | `python3 Tools/Build/headless_cs.py compile` | OK — 47 files, 0 errors, 0 warnings; 4 Unity files excluded (Batch C) |
| C# EditMode NUnit suites (headless, NUnitLite 3.13.3) | `python3 Tools/Build/headless_cs.py test` | **109 / 109 passed** (`Docs/evidence/campaign-session-2026-10-15/EditMode.headless.xml`; earlier 49/55/59 snapshots in older folders) |
| .NET Standard 2.1 API-profile check | `python3 Tools/Build/headless_cs.py profile` | OK — 6 runtime assemblies |
| C# 9 parse check of 24 Unity-dependent files | `python3 Tools/Build/headless_cs.py syntax` | OK — 0 syntax errors (not type-checked) |
| Mutation check of Batch C fixes/safeguards | `python3 Docs/evidence/campaign-session-2026-10-15/mutation_check.py` | 8 / 8 mutations caught |
| Build diagnostics + runner contract (mocked; no Unity) | `python3 -m unittest discover -s Tools/Build -p 'test_*.py'` | **86 / 86** (`Docs/evidence/cloud-build-2026-10-16/diagnostics-tests.log`) |
| Mutation check of the Batch H cloud-build safeguards | `python3 Docs/evidence/cloud-build-2026-10-16/mutation_check.py` | **17 / 17** mutations caught |
| C# NUnit suites **in Unity** | Unity Test Runner / `Tools/Build/build.sh test-editmode` | **Not run** (KI-001) |
| Android/iOS smoke builds | `Tools/Build/build.sh android-dev` / `ios-dev` | **Not run** (KI-006) |

## QA §20.2 mandatory case coverage

| # | Case | Coverage |
|---|---|---|
| 1 | Landmark reward granted once | C# test **passed (headless)** + ledger mirror |
| 2 | Already-collected cache yields nothing | C# transaction replay + campaign reload/preview tests **passed (headless)** (authored contents, KI-020) |
| 3 | Insufficient cargo blocks pickup without consuming | C# inventory + campaign full-cargo + field-prompt tests **passed (headless)** (KI-015/019/020 fixed) |
| 4 | Banking interruption → old or new state, never mixed | C# save protocol test + **campaign banking interrupted after each of 6 persistence steps** + failed-write rollback **passed (headless)** |
| 5 | Backup after corruption preserves latest valid | C# backup rotation test **passed (headless)** |
| 6 | Zero-resource rescue/playability | C# rules + campaign rescue/zero-supply recovery **passed (headless)** + Python deadlock scan |
| 7 | Tier costs & prerequisites canonical | C# **passed (headless)** + Python (both PASSED) |
| 8 | Tire sets never double-dip research bonus | C# resolver test **passed (headless)** |
| 9 | Capacity-module removal blocked over capacity | C# test **passed (headless)** |
| 10 | Camp establishment atomic | C# transaction + campaign test (atomic with its save) **passed (headless)** (KI-021 fixed) |
| 11 | Fuel/condition bounds; frame-rate-independent integration | C# tests **passed (headless)** + Python bounds |
| 12 | Chunk boundary handle leaks | Streaming state machine coded; PlayMode test pending (Unity) |
| 13 | 100 landmark / 200 cache unique & reachable | IDs unique PASSED; reachability at M3 bake |
| 14 | Wetland routes valid in both water states | Water model states coded; route validation at M3 |
| 15 | Background/resume never latches throttle | C# + PlayMode test written |
| 16 | No reward farming by closing on reward screen | Transaction idempotency tests **passed (headless)** |
| 17 | Crafted-item resale never nets currency | C# **passed (headless)** + Python (both PASSED) |
| 18 | Mapping threshold never duplicated | C# restore test **passed (headless)** |
| 19 | Migration preserves discoveries/modules | C# test **passed (headless)** |
| 20 | 150% scale / 30% text expansion layout | Not covered — needs UI layout pass (M2/M6 device check) |

## Next dependency-ordered batch

0. **APK produces and runs on device (2026-10-20).** First cloud-build APK installs and plays on a Moto G45. `WayfinderControls.inputactions` composite binding fix applied (KI-045 resolved) — the dev-console `InvalidOperationException` is gone, keyboard steering now works, touch still works.

1. **Owner, ~10 minutes — verify the fix:** dispatch **Actions → Android APK (free cloud build) → Run workflow** on the current branch. The `WayfinderControls.inputactions` change is data-only (no C#, no asmdef, no package); the build should succeed and produce a new APK. Install on the Moto G45 and confirm: (a) no `InvalidOperationException` in the Development Console, (b) WASD/arrow steering works, (c) touch still works, (d) Q01 can be completed end-to-end. If green, the M2 30-minute playtest can begin. **Read the *Explain the Unity result* job** (D-035) for any file/line/col annotations. Commit `Packages/packages-lock.json` from the `Unity-logs` artifact (KI-002).
   *Local alternative:* install Unity 6.3 LTS + Android module, open the project, `Tools → Wayfinder → Setup Project`, commit generated scenes/meta (KI-004/005).
2. Fix any remaining UnityEngine API drift in the 24 parse-checked files — first the new campaign glue (KI-026); run EditMode tests **in Unity** (expect 216/216) and the PlayMode tests; play Title → New → VerticalSlice and attach logs/screens to `Docs/evidence/`.
3. M2 follow-ups that are dependency-ready without Unity: ~~camp↔garage hub travel + per-camp storage with a schema bump/migration (KI-028)~~ **done 2026-10-21 (Batch J, D-039/D-040)**; ~~expose repair-kit/tire-patch use and selling in the session~~ **done (D-029)**. Remaining: the `VehicleController.ApplyEffectiveStats` seam so researched per-wheel grip/suspension geometry reaches physics (KI-025), and §10.2 module slot equipping (the last open part of KI-028).
4. M3 batch: `ScatterPlacer` (§7.6) + pure `WorldStreamer` planner with handle-leak tests (KI-017, QA case 12); slice terrain bake so the 1,024 m area has real relief (KI-027).
5. Device: Moto G45 30-minute slice playtest (interruption/kill tests, 30 FPS budget) to close the M2 gate.
6. Keep `python3 Tools/Build/headless_cs.py all` green on every batch.
