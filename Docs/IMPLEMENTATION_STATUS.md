# Implementation status

Updated 2026-10-14. **Current milestone: M0. Gate: Blocked.**
Source preparation is not a compiled build. No game, APK, IPA, device performance,
or full-loop completion is claimed. Status vocabulary: Not started / In progress /
Implemented / Verified / Blocked. Verified always names actual evidence.

## M0 requirement map (engineering IDs supplement Appendix E)

| ID | Blueprint / requirement | Status | Evidence / remaining work |
|---|---|---|---|
| M0-01 | §15.2 editor + package version lock | Implemented | ProjectVersion, manifest/lock, dependency-sources; Unity resolution/compatibility blocked |
| M0-02 | §15.3, §16 assembly boundaries/composition | In progress | Core, Platform, Presentation, Composition, Editor; compile gate pending |
| M0-03 | §2.5 Boot/Title state flow and pause ownership | In progress | Scenes + state commands + tests; actual playback pending |
| M0-04 | §3.2, §19.1 input actions/lifecycle safety foundation | In progress | Input maps and focus/rearm tests; no vehicle/touch driving claimed |
| M0-05 | §18.4 URP Forward/settings, fixed 60 Hz | In progress | Explicit editor preparation + serialized assets; render verification pending |
| M0-06 | §20 test assemblies + structural CI | In progress | Run evidence recorded at batch completion; Unity suite blocked |
| M0-07 | §19.2, §21.4 native smoke build/scripts | Blocked | Toolchain absent; owner Android/iOS commands supplied |
| M0-08 | §18.1, §20.5 device matrix | Implemented | QA/DEVICE_MATRIX.md; actual model nomination/testing pending |
| M0-09 | §22 persistent docs/provenance/Git checkpoints | In progress | This map, decisions, issues, manifest, build guide; push evidence at batch end |

## Appendix E acceptance map

| ID | Milestone | Requirement / proof | Status | Remaining work |
|---|---|---|---|---|
| PROD-01 | M0–M7 | No ads/IAP/premium; dependency + offline audit | In progress | Structural allowlist in M0, actual binary/offline audit later |
| FLOW-01 | M2 | Complete loop; playable recording/tester | Not started | Inventory through return/research |
| DRIVE-01 | M1 | Touch/tilt/stick; device/remapping tests | Not started | M0 action definitions alone do not satisfy |
| DRIVE-02 | M1 | Surface handling; telemetry + review | Not started | Scout and proving ground |
| HAZ-01 | M1 | Mud/water/slope; boundaries/readability | Not started | Physics hazards and warnings |
| REC-01 | M1–M2 | Zero-resource recovery | Not started | Safe poses, rescue, loss invariants |
| WORLD-01 | M3–M5 | Five biomes/6144 m; traversal | Not started | Authored full mainland |
| GEN-01 | M3 | Stable deterministic bake; hash/topology | Not started | Seed/hash/bake/route tools |
| STREAM-01 | M3 | Safe streaming; seam/leak stress | Not started | Local bundles and leases |
| MAP-01 | M2 | Persistent map/reward idempotency | Not started | Cells/mask/reward escrow |
| CONTENT-01 | M4–M5 | Exact content counts; catalog audit | Not started | Definitions/sockets/validated approaches |
| QUEST-01 | M4 | Twenty missions; fresh-save playthrough | Not started | M2 Q01–04 first |
| ITEM-01 | M2–M4 | Cargo/protected items; tests/recovery | Not started | Atomic inventory and reserved slots |
| UPG-01 | M2–M4 | Twenty tiers/exact costs/shared stats | Not started | M2 first six then full tree |
| ECON-01 | M2–M6 | No softlock/arbitrage; simulation/playtest | Not started | One ledger, recipes, balancing |
| CAMP-01 | M2 | Atomic establish/deposit; fault injection | Not started | Coupled persistence mutation |
| SAVE-01 | M2–M7 | Atomic backups/migrations; corruption suite | Not started | Three slots, immutable snapshots |
| UI-01 | M0–M6 | Safe/readable touch UI; physical checks | In progress | M0 placeholder shell only; on-device checks blocked |
| ACCESS-01 | M1–M6 | Non-tilt/reduced motion; human review | Not started | Full access settings and controls |
| PERF-01 | M2–M6 | Measured quality tier per device | Not started | Never claim performance from settings |
| MOBILE-01 | M0–M7 | Pause/resume/kill; lifecycle device tests | In progress | Input foundation only; save safety M2 |
| LEGAL-01 | M0–M7 | Licensed final assets; provenance | In progress | Placeholder manifest; final assets absent |
| BUILD-01 | M0–M7 | Reproducible Android/iOS artifacts | Blocked | Licensed editor, modules, Mac/Xcode, devices |
| FINISH-01 | M4–M7 | Finale/credits/postgame; end-to-end | Not started | All missions, five relays, 60% map |

## Dependency-ordered milestones
1. **M0** pin/tools -> assembly/state/input -> scenes/URP/title -> tests/build ->
   native smoke evidence (or explicit owner blocker). Report gate; do not silently pass.
2. **M1** Scout chassis -> wheels/gears/brakes -> collision camera -> actual touch ->
   surfaces/load -> hazards/winch/reset -> instrumented obstacle/device handling gate.
3. **M2** atomic inventory/wallet -> garage/pickups/discovery/map -> Q01–04/research ->
   camps -> durable saves/fault tests -> 1024 m forest slice -> device full loop/profile.
4. **M3** road graph -> authored height/biomes -> chunk/seam bake -> proxies/streaming ->
   sockets/validators -> full mainland traversal/leak report.
5. **M4** expand shared definitions: all vehicles/tiers/items/missions/hubs/relics/finale;
   feature freeze only after full greybox campaign works.
6. **M5** exact counts + licensed final art/audio; no critical placeholders.
7. **M6** actual thermal/accessibility/localization/economy/usability qualification.
8. **M7** clean native releases/migrations/full offline playthrough/legal/store audit;
   owner signing/publication separate. No marketing claims ahead of evidence.

Sections 1–25 and Appendices A–F are mapped in Specifications/REQUIREMENT_MAP.md.
