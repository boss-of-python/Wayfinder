# Batch L evidence — §10.4 community logistics: free instant hub-to-hub transfer

Date: 2026-10-21. Branch `arena/00927a42-wayfinder`. Decision: **D-042**. Issue: **KI-028** (last
open piece; now closed).

§10.4:694 — "Items stored in any camp are part of a shared logistics inventory **only after** a
safe-hub transfer action, not magically accessible for field repairs. For implementation simplicity,
version 1 allows free instant transfer among activated hubs while the player is at a hub; no field
access. Explain this as community logistics."

## Result

| Check | Result |
| --- | --- |
| `headless_cs.py compile` | 7 assemblies / 60 files, **0 errors, 0 warnings** (`compile-report.json`) |
| `headless_cs.py test` | **254 / 254 passed**, 0 failed, 0 skipped (239 → 254) |
| `headless_cs.py profile` | .NET Standard 2.1 API profile **OK** |
| `headless_cs.py syntax` | 30 Unity-dependent files parse, **0 syntax errors** |
| `validate_content.py` | 0 errors, 0 warnings — **PASSED** |
| `formula_checks.py` | 0 failures — **PASSED** |
| Mutation check | **18 / 18 caught**, survivors: none, tree clean, exit 0 |

New fixture: `Assets/Wayfinder/Tests/EditMode/CampaignLogisticsTransferTests.cs` (15 tests).

## What the 15 tests execute

| # | Test | Rule |
| --- | --- | --- |
| 1 | `TransferToHub_MovesPropertyFromTheHubYouAreAt_ToAnotherActivatedHub` | property leaves the hub you are at and lands in **that camp's own** store (D-039) |
| 2 | `TransferFromHub_PullsPropertyIntoTheHubYouAreAt` | pull moves only the requested stack |
| 3 | `ATransferIsFreeAndInstant_NoFeeAndNoClockAdvance` | no fee, no escrow, **the clock does not move** |
| 4 | `TransfersSurviveSaveAndReload` | durable through schema-2 save + reload |
| 5 | `ATransferIsCritical_AFailedVerifiedSaveRollsItBack` | `SaveIoFailure` and a complete rollback (§12.4/§17.4) |
| 6 | `TransfersAnnounceThemselvesAsCommunityLogistics` | notice kind, receiving hub, amount, and the words "community logistics" (§13.2) |
| 7 | `NoFieldAccess_TransfersAreRefusedInTheOpenField` | §10.4's "not magically accessible for field repairs" |
| 8 | `StandingAtACamp_ThatCampIsTheCurrentHub_AndPropertyMovesFromIt` | a camp is a full logistics partner, both directions |
| 9 | `AnUnactivatedHubCannotSendOrReceive` | activation is the gate (camp and garage), detail says "not activated" |
| 10 | `TwoGaragesShareOneUnlimitedStore_SoGarageToGarageIsRefused` | one unlimited garage store ⇒ nothing to move |
| 11 | `TransferToTheHubYouAreAlreadyAt_IsRefused` | self-transfer refused with its **own** detail |
| 12 | `LogisticsHubs_AreDeterministicallyOrdered` | garages then camps, each sorted; an unactivated hub addresses no store |
| 13 | `UnknownItemsProtectedObjectsAndEmptyQuantities_AreRefused` | unknown/empty item, qty ≤ 0, protected ledger object |
| 14 | `ACampStoreIsMassLimited_AndAFailedTransferMovesNothing` | the 500 kg camp limit binds and the move is atomic |
| 15 | `TheGaragePanelListsEveryActivatedHubWithItsCapacity` | `CurrentHubId`, `IsCurrent`, `SharedStore`, `CanExchange`, capacity (§10.4 "show capacity"), mass, stacks, display names |

## Mutation check

`mutation_check.py` breaks one rule at a time in production source, recompiles, runs the whole
EditMode suite, and requires at least one failing test (or a compile failure). It refuses to start on
a dirty `Assets/` tree, restores every file from memory after each mutant, and verifies the tree is
clean at the end.

| Mutant | Rule broken | Caught by |
| --- | --- | --- |
| L1 | transfers allowed away from a hub (field access) | `NoFieldAccess_TransfersAreRefusedInTheOpenField` |
| L2 | protected ledger objects can be transferred | `UnknownItemsProtectedObjectsAndEmptyQuantities_AreRefused` |
| L3 | a non-positive quantity is accepted | same |
| L4 | transferring to the hub you are already at is allowed | `TransferToTheHubYouAreAlreadyAt_IsRefused` |
| L5 | an unactivated hub can send or receive | `AnUnactivatedHubCannotSendOrReceive` |
| L6 | garage-to-garage "transfer" is accepted | `TwoGaragesShareOneUnlimitedStore_SoGarageToGarageIsRefused` |
| L7 | a camp's own store is replaced by the shared garage store | 9 tests (mass limit, rollback, free/instant, …) |
| L8 | an unlocked garage addresses no store | 10 tests |
| L9 | pulling from another hub sends to it instead | `TransferFromHub_…`, `StandingAtACamp_…` |
| L10 | the hub list puts camps before garages | `LogisticsHubs_AreDeterministicallyOrdered` |
| L11 | camps are missing from the hub list | `LogisticsHubs_…`, `TheGaragePanelLists…` |
| L12 | the transfer notice is dropped | `TransfersAnnounceThemselvesAsCommunityLogistics` |
| L13 | the notice names the hub property left, not the receiver | same |
| L14 | a failed verified save no longer rolls back (§17.4) | `ATransferIsCritical_…` + 2 pre-existing rollback tests |
| L15 | every hub row claims it can exchange | `TheGaragePanelListsEveryActivatedHubWithItsCapacity` |
| L16 | the panel never shows a store's capacity | same |
| L17 | the shared garage store is presented as per-hub | same |
| L18 | a hub's stored stacks are not listed | same |

### The first run found two survivors, and both were findings about the code

The first harness run reported **16/18**, with L4 and L5 surviving. Neither was a weak test — each
exposed something real, and both were fixed in the source before this run:

- **L5 was a duplicate guard.** The code checked `HubStore(other) == null` and then re-checked the
  resolved `from`/`to`. Since `other` is always one of `fromHubId`/`toHubId`, and the at-hub gate has
  already proven `here` resolves, the first check could never fire — removing it changed no behaviour,
  which is exactly why the mutant survived. The duplicate was **deleted**; the surviving guard now sits
  immediately before the `from.Value`/`to.Value` dereference it protects, and L5 was retargeted at it
  (removing it now makes an unactivated hub throw, which the test reports as a failure).
- **L4 was masked, not dead.** A self-transfer and a garage↔garage transfer both end at the
  same-store refusal with the same reason (`WrongState`), so a test asserting only the reason cannot
  tell them apart. The self-transfer check is *not* redundant, because the shared-store wording
  ("every garage shares one unlimited store") would be wrong for a camp. Tests 10 and 11 now assert
  the player-facing `Detail`, which is what makes the two paths distinguishable.

This is the point of running mutants: a survivor is a claim that some code is load-bearing, and in one
case that claim was false.

### Deliberately not mutation-covered

The two defensive `ConfigureCampStore` calls inside the transfer. `ConfigureContainers()` already
sizes every activated camp's store on load and on establishment, so removing them changes no
observable behaviour and no mutant can catch them. They are recorded in D-042 as belt-and-braces
rather than claimed as tested.

## Files

| File | What it is |
| --- | --- |
| `headless-console.log` | full `headless_cs.py all` console output for this batch |
| `EditMode.headless.xml` | NUnit3 result XML: 254 total / 254 passed / 0 failed |
| `compile-report.json` | per-assembly compile report, 0 warnings |
| `python-checks.log` | `validate_content.py` + `formula_checks.py` output |
| `mutation_check.py` | the 18-mutant harness (L1–L18) |
| `mutation-check.log` | its run: 18/18 caught, survivors none, tree clean, `EXIT 0` |

## Reproduce

```bash
python3 Tools/Build/headless_cs.py all          # compile + test + profile + syntax (~60 s)
python3 Docs/evidence/hub-logistics-2026-10-21/mutation_check.py   # ~13 min, needs a clean Assets/ tree
```

## What this evidence does not prove

Nothing here is a Unity result. The domain, progression, persistence and view-model code was compiled
and executed outside Unity (D-014); `VerticalSliceDriver`'s new logistics panel is **syntax-checked
only** and has never run in an editor or on a device (KI-003, KI-026). No PlayMode, physics, frame-time
or thermal evidence exists. The M2 gate still needs the 30-minute on-device playtest. §10.4's
automated arbitrage test ("crafting and reselling must never create currency") is a pricing rule, not a
logistics one, and remains unwritten.
