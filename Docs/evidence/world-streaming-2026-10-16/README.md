# Evidence — scatter placement and chunk streaming planner (Batch G, 2026-10-16)

**Headless evidence only.** Nothing here is a Unity editor, PlayMode, device or build result. The
planner and scatter placer are engine-free C#, so they are compiled and executed with the real
Roslyn 3.9 (C# 9) compiler and NUnitLite, exactly as the earlier batches were (D-014).

| File | What it is |
|---|---|
| `headless-console.log` | `python3 Tools/Build/headless_cs.py all` on the final code: 7 runtime assemblies + tests compile with **0 errors, 0 warnings**; **164/164** EditMode tests pass (143 → 164: 10 `ScatterPlacerTests`, 11 `ChunkStreamPlannerTests`); .NET Standard 2.1 profile OK for all six runtime assemblies; 29 Unity-dependent files parse. `EXIT=0`. |
| `EditMode.headless.xml`, `compile-report.json` | Raw NUnit3 XML and compile report from that run. |
| `mutation_check.py`, `mutation-check.log` | 15 rule breaks or defect re-introductions, one at a time. **15/15 caught**; the unmutated baseline is green before and after. |
| `python-checks.log` *(see below)* | Content validator, formula checks and economy simulation re-run after the change: all pass. |

## What was built (files)

- `Assets/Wayfinder/Scripts/World/ScatterPlacer.cs` — §7.6 scatter placement. Deterministic per
  candidate (§7.3), chunk-independent, Poisson-style spacing that holds across chunk borders,
  road-shoulder / slope / reserved-footprint / density rules.
- `Assets/Wayfinder/Scripts/World/ChunkStreamPlanner.cs` — §18.3 streaming planner: request ring,
  prefetch ahead of travel, distance hysteresis, dormant data retention, pinning, activation budget,
  retry-then-defect, a handle ledger, and a loading hold.
- `Assets/Wayfinder/Scripts/World/WorldDefinition.cs` — `ChunkStateMachine` gains one legal
  transition, `Deactivating → DataReady` (dormant retention; D-034).
- `Assets/Wayfinder/Tests/EditMode/WorldStreamingTests.cs` — 21 tests plus a simulated Unity host
  (`StreamHostSim`) that counts asset handles independently of the planner.

## Defects found and fixed by this batch

| Defect | How it was found | Fix |
|---|---|---|
| Scatter cell size was **integer division** (`256 / 85 = 3`), so the candidate grid no longer tiled the chunk and candidates leaked into neighbouring chunks | `EveryAcceptedPoint_IsInsideItsOwnerChunk…` and `CellGrid_…` failed | `(float)` division; regression test kept |
| Distance-free chunk-index hysteresis **thrashed**: oscillating across one boundary deactivated ≈3 chunks per crossing (1218 deactivations in 400 crossings) | `Hysteresis_BoundaryOscillation_…` | Distance-based bands measured from the player (256 m enter, 384 m retain, 640 m unload) plus the 3 × 3 ring as an always-active union (D-034) |
| Euclidean-only activation dropped the diagonal corners of the 3 × 3 ring when standing near an edge | `InitialLoad_…`, `WorldEdge_…` | Activation is the union of the Chebyshev 3 × 3 ring and the distance bands |
| A chunk leaving the ring was deactivated **and** unloaded, so it reloaded on re-entry | `Dormant_ChunkReenteringTheRing_…` | Dormant state (`Deactivating → DataReady`) keeps data inside 640 m |
| The handle ledger could be wrong **between** steps without a post-tick check catching it | Mutation #11 (“handle ledger ignores Deactivating”) survived the first run | Mid-tick invariant: a chunk being deactivated still holds its handle until the host confirms |

## Bounds asserted by the tests (QA case 12 at planner level)

- Instantiated chunks ≤ 16 (4 × 4) at any moment, including during crossings.
- Retained (loaded) chunks ≤ 36 (6 × 6) inside the 640 m data zone.
- After 300 back-and-forth boundary crossings: host and planner handle counts agree on every tick,
  no chunk is activated twice, activation − deactivation equals the active count, and the steady-state
  ring is exactly 9.

## What is untested, and why

- **Unity integration.** No `WorldStreamer` MonoBehaviour exists yet; the planner has no Unity
  caller. Addressables/handle semantics, frame-budget behaviour and PlayMode crossing tests (QA 12
  in Unity) need the editor and a device (KI-037).
- **Scatter in a real baked world.** Tests use an analytic flat surface with one road. The baker's
  real height/slope/road/POI surface and visual density have not been checked (KI-038).
- **Performance.** Planner and placer costs were not measured on a phone (PERF-01, KI-001/006).
- **Caches and landmarks** are not scattered here: §7.6 places them at validated sockets (§7.2 step
  9), which is a separate, later batch.

## Reproduce

```
python3 Tools/Build/headless_cs.py all
python3 Docs/evidence/world-streaming-2026-10-16/mutation_check.py   # ~10 minutes; restores files
```
