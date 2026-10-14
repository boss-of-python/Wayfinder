# Wayfinder: The Quiet Frontier

Native Android/iOS, single-player, offline exploration-driving game in development.
**M0 foundation source — uncompiled; no playable driving/campaign build yet.**
No advertising, in-app purchases, tracking, required account or backend.

## Open the project

Use **Unity 6000.3.0f1 (d1870ce95baf)** via Unity Hub; add this repository root.
This is an evidenced reproducibility baseline, not a claim of the newest patch.
Package versions and provenance are committed. First import needs Unity's package
registry; the eventual installed game must work offline.

Run **Wayfinder > Prepare M0**, open
`Assets/Wayfinder/Scenes/Bootstrap/Bootstrap.unity`, then press Play.
Expected M0 behavior: a development title, Controls/About panels and explicit
resume after focus loss. New Game/Continue and driving are not implemented yet.
This expected behavior has NOT been verified here: Unity is absent in the sandbox.

- [Exact build/test instructions](Docs/BUILD_AND_RUN.md)
- [Implementation and acceptance status](Docs/IMPLEMENTATION_STATUS.md)
- [Decisions](Docs/DECISIONS.md) · [Known issues](Docs/KNOWN_ISSUES.md)
- [Blueprint reference](Docs/Specifications/Wayfinder_Production_Blueprint.md)
- [Milestone handoff](Docs/Specifications/AI_IMPLEMENTATION_HANDOFF.md)
- [M0 evidence](Docs/Milestones/M0_REPORT.md)

## Checks that do not require Unity

```sh
python3 Tools/Validation/validate_repository.py
python3 -m unittest discover -s Tools/Validation/tests -v
```

These check repository contracts, not C# compilation or game correctness.
Unity EditMode/PlayMode and native device smoke tests are separate gates.

## GitHub checkpoints

Project source, metadata, settings, tests and docs are saved on
[`arena/26801a3c-wayfinder`](https://github.com/boss-of-python/Wayfinder/tree/arena/26801a3c-wayfinder).
Use this branch when cloning; it is not yet merged into `main`.

```sh
git clone --branch arena/26801a3c-wayfinder https://github.com/boss-of-python/Wayfinder.git
cd Wayfinder
```

Progress is committed/pushed at meaningful work checkpoints. Generated Library,
player builds and owner signing/license secrets are intentionally excluded. Build
artifacts belong in approved release/artifact storage, not source control. No game
license or commercial asset rights are inferred from public repository access.
