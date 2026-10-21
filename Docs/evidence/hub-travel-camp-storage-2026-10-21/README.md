# Evidence — safe-hub travel + per-camp storage (Batch J, 2026-10-21)

Headless evidence only. **Nothing here is a Unity, device or build result.** Hub travel and the
camp/garage service split have no Unity UI yet (KI-026); the domain rules below are what the UI will
call. Closes the hub-travel and per-camp-storage halves of KI-028 (D-039, D-040).

| File | What it is |
|---|---|
| `headless-console.log` | `python3 Tools/Build/headless_cs.py all`: 7 assemblies compile with **0 errors, 0 warnings** at `-warn:4`; **216/216** EditMode tests (188 → 216: 15 `CampaignHubTravelTests`, 9 `CampaignCampStorageTests`, 4 new migration tests in `PersistenceTests`); the .NET Standard 2.1 API-profile check passes for all six runtime assemblies; 30 Unity-dependent files parse. `EXIT=0`. |
| `EditMode.headless.xml`, `compile-report.json` | Raw NUnit3 XML and the compile report from that exact run. |
| `mutation_check.py`, `mutation-check.log` | **27 rule breaks, 27/27 caught.** A1–A15 break §10.4 hub travel (content service gate, water/deep-mud refusal, mid-expedition extraction target, both `AtGarage` guard files, camp activation, socket radius, the notice, the safe pose, the progression gate, three variants of the open-field refusal, `SpawnFor`'s camp sockets, the verified save). B1–B12 break §12.2 per-camp storage (the store id, content capacity, `EstablishCamp`/`DepositAtCamp` wiring, both migration branches, merge-vs-append, pooled-row clearing, malformed rows, the checksum recompute, restore, pooled-row adoption, a capture double-write). The script refuses to start on a dirty `Assets/` tree, restores every source after its mutant, and asserts the tree is clean at the end — `EXIT=0`. |
| `python-checks.log` | `validate_content.py` (0 errors, 0 warnings), `formula_checks.py` (0 failures), `git diff --check` and a post-run `git status --porcelain Assets`: all clean. |

## What was executed

- **Where you are:** `CurrentHubId` returns the parked hub in garage mode, the nearest *activated*
  camp whose socket radius contains the pose in the field, and null in the open field — including the
  negative cases (outside the 8 m socket radius, an unactivated camp socket).
- **Travel:** garage → camp re-bases the pose and the safe pose and re-bases the next `Depart`;
  camp → garage mid-expedition keeps `IsActive` and the extraction target; a teleport does not inflate
  `Stats.DistanceMeters`; the `HubTravelled` notice is emitted only when the move commits; the wallet
  is unchanged (no fee); a `SaveStep.TempVerified` `IOException` rolls the whole move back with
  `SaveIoFailure` and the retry succeeds; the arrival survives a reload byte-identically.
- **Refusals:** water, deep mud, open field (with the pose, extraction target and notice queue
  asserted untouched, and full rescue then working as the way out), unactivated camp, unknown hub,
  null hub, self-travel, and a *real mutated catalog* with `hubTravel` stripped from `camps.json`.
- **Service split:** at a camp, research, crafting, loadout moves, the mission board, the contract
  board and selling all return `WrongState`, while save and hub travel still work; back at a garage
  the loadout gate passes immediately (it fails on the missing item, not on state). `AtGarage ==
  InGarage` for a save that predates hub travel.
- **Per-camp storage:** deposits land in that camp's own store and never in another's; the pooled v1
  container is neither configured nor filled; a store is sized by its camp's own
  `storageCapacityKg` (proved against a mutated catalog at 5 kg: 5 units stored, 3 left in cargo);
  stores survive save → reload separately and the capture stays byte-identical; hub travel leaves both
  stores intact; a failed verified deposit rolls the camp store back.
- **Migration:** two schema-1 files written to disk and reloaded (pooled rows → first activated camp's
  store, and pooled rows → garage storage when no camp exists), plus four step-level tests (merge by
  item, mission-scoped stacks kept apart, rows that carry nothing dropped, checksum recomputed,
  current-schema payloads never rewritten, a newer schema still refused).

## Defect found by this work

The first implementation of `SaveMigrations.Steps[1]` was a `JObject` transform, as the scaffolding's
comment suggested. It **silently lost the migrated store**: assigning an already-parented `JArray`
back onto the `JObject` that owned it detached the array, so the step emptied `campStorage` and
produced no `campStores` entry — no exception, no failing checksum, and a player's camp property gone.
Only the on-disk migration test caught it. The `Migration` delegate is now typed on `SavePayload`
(D-040), which removes the serialization boundary the field was lost in.
