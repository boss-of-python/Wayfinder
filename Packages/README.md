# Packages

`manifest.json` pins are **PROVISIONAL** (Decision D-004, Issue KI-002).

This repository was authored in an environment that cannot reach Unity's package
registry, and the blueprint forbids inventing exact package versions. The pins
above target the Unity `6000.3` (Unity 6.3 LTS) stream and must be verified
against the actually-installed editor:

```bash
python3 Tools/Build/pin_toolchain.py --editor-version 6000.3.<patch>f1
```

Then open the project once in Unity so the Package Manager resolves dependencies
and writes `packages-lock.json`, and commit both files.

Rules:
- Never add an advertising, IAP, analytics, tracking, or multiplayer package (PROD-01).
- Runtime packages stay minimal (§15.2). Editor-only tooling lives in `Assets/…/Editor`.
- `com.unity.nuget.newtonsoft-json` is the official free Unity distribution of
  Newtonsoft.Json, used for canonical save serialization (D-010).
