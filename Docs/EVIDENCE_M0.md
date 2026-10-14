# Wayfinder — Executed Evidence (2026-10-14)

This file records commands actually executed in the authoring environment and their real output.
Anything not in this file is **unverified** (see `Docs/KNOWN_ISSUES.md`).

Environment: Linux sandbox, Python 3.11.2, Node present. **No Unity editor, no .NET/Mono compiler,
no Android SDK, no Xcode** (verified with `which unity dotnet mono csc mcs` → empty).

---

## 1. Content validation — `python3 Tools/Validation/validate_content.py`

```
validate_content: 0 error(s), 0 warning(s)
CONTENT VALIDATION PASSED
EXIT=0
```

Covered and passing: exact release-scope counts (100 landmarks / 200 caches / 20 missions /
30 authored + 5 repeatable contracts / 20 upgrades / 4 vehicles / 10 camps / 5 garages /
25 relics / 4 recipes / 11 surfaces / 5 biomes), per-biome splits (20/40/6/2), unique stable IDs
across all content, upgrade cost table Σ = 32,200 with sequential tiers and valid story gates,
mission reward table exact per §9.2 with linear prerequisite chain and typed objective vocabulary,
recipe arbitrage impossibility (QA case 17), cache payout bands, contract reward formula (§9.3),
surface coefficients exact per §4.4, vehicle values exact per §4.2/§10.1, and the PROD-01
dependency scan (no ad/IAP/analytics/tracking tokens in `Packages/manifest.json`; no external
URLs in `Assets/**/Scripts` outside approved documentation links).

## 2. Formula and invariant checks — `python3 Tools/Validation/formula_checks.py`

```
35 checks executed, 0 failures — FORMULA CHECKS PASSED
```

Highlights (all asserted against the blueprint's own example values):

| Check | Result |
|---|---|
| fuel example rate (0.45/0.50/0.20) | 0.02255 L/s exactly (§5.1) |
| 24 L endurance at that rate | 17.74 min ≈ §5.1 "approximately 17.7 minutes" |
| engine-off consumption | 0 |
| damage D(5) / D(10) / D(3) | 5.6 / clamped 35 / 0 (§5.5) |
| research tree | 20 tiers, Σ incremental = 32,200 (§11.2) |
| steering limits 34/26/16/10° at 0/20/40/70 km/h | exact (§3.3) |
| slip ratio matched-roll / locked wheel | 0 / −1 (§4.4) |
| rescue 25% floor of 37 units | 9 (§5.6) |
| world arithmetic | 24×256 = 96×64 = 6144 m; 129×2 m grid (§6.1/§7.4/§8.1) |
| stable hash determinism (32/64-bit, chunk seeds) | pass (§7.3) |
| terrain seam agreement across adjacent chunk bakes | worst difference 0.000 m ≤ 1 mm (§7.4) |
| heightfield determinism + non-degenerate relief | identical repeat samples; 184 m spread across 24 probes |

Note: a first run exposed a bug in the *Python mirror* of the fBm octave amplitude (initialized
0.0 instead of 0.5), which made the seam check vacuous. The mirror was fixed and the heightfield
relief assertion added; the C# `NoiseField` implementation was correct (amp starts 0.5f).

## 3. Economy simulation — `python3 Tools/Balance/economy_sim.py`

```
Total research cost: 32200 Salvage

--- pattern: conservative ---  tiers by 1 h: 3 | by 5 h: 8 | by 12 h: 13 | full tree 34.8 h
--- pattern: typical ---       tiers by 1 h: 4 | by 5 h: 9 | by 12 h: 15 | full tree 22.0 h
--- pattern: thorough ---      tiers by 1 h: 4 | by 5 h: 10 | by 12 h: 16 | full tree 16.0 h

deadlock scan: OK (free garage services + no-cost repeatable jobs + Salvage-only research)
arbitrage scan: OK (no recipe creates currency)
ECONOMY SIMULATION COMPLETED WITH NO DEADLOCKS
EXIT=0
```

All three patterns meet the §11.3 pacing hypotheses (tiers 1–3 in the first hour; 5–7 by 5 h;
10–12 by 12 h). This is a model (KI-010), not playtest proof.

## 4. Dependency / authenticity audit

- `Packages/manifest.json` contains only URP, Input System, Addressables, Localization,
  Newtonsoft (official free distribution), Test Framework, uGUI and built-in `com.unity.modules.*`.
  The built-in `unityanalytics` module is deliberately absent (PROD-01).
- No third-party art/audio/font assets are imported (`Docs/ASSET_MANIFEST.csv` tracks placeholders).
- No signed builds, store approvals, device measurements or screenshots are claimed anywhere.

## 5. What was NOT executed here (and why)

| Missing evidence | Why | Smallest tool needed |
|---|---|---|
| C# compilation of `Assets/**` | no Unity/.NET compiler in environment | Unity 6.3 LTS editor |
| NUnit EditMode/PlayMode results | requires Unity Test Runner | Unity 6.3 LTS editor |
| Android smoke build (M0 gate) | no Android SDK/NDK | Unity + Android module |
| iOS smoke build (M0 gate) | no macOS/Xcode/signing | Mac + Xcode + owner signing identity |
| Device touch/thermal/lifecycle tests | no devices | physical Android/iOS devices |
