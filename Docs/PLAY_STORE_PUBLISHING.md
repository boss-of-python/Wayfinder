# Wayfinder — Google Play publishing and status

Reviewed **2026-10-15**, baseline `1c38da2`.

**Full owner guide in Google Docs:**
[Project Status & Google Play Publishing Guide](https://docs.google.com/document/d/1t1b6vOJWlvlR7anCU04WsFD8huosY4gNCb1S6WFVqZM/edit)

## Moto G45 route and later source fixes

See [MOTO_G45_BUILD_AND_TEST.md](MOTO_G45_BUILD_AND_TEST.md) for a complete
phone-controlled desktop build/install procedure. Latest domain suite: 59/59
passing C# tests. Pause/focus and water-trigger source changes do not close the
Unity/device gate. No APK/AAB has been built or published.

## Implementation update (same day, subsequent batch)

See [UNITY_HANDOFF.md](UNITY_HANDOFF.md): setup and a title-to-proving-ground
prototype are now implemented in source; the signed-AAB path is explicit. The
headless suite now passes **55/55 C# tests** and the Python runner passes **23/23
mocked tests**. Unity-facing code is syntax-checked only. No Unity build, signed
artifact or device evidence exists. The Google Doc's initial audit is historical;
its implementation addendum describes this later batch.

## Verdict: not ready for store submission

The repository contains substantial content and tested domain systems, not yet a
verified complete playable Android game. No completion percentage or launch date
is supported. Play Console/account/live listing status was not inspected.

### Checks rerun on 15 October 2026

All commands below exited successfully:

```bash
python3 Tools/Build/headless_cs.py all
python3 Tools/Validation/validate_content.py
python3 Tools/Validation/formula_checks.py
python3 Tools/Balance/economy_sim.py
```

- Headless C#: 49/49 tests passed; compilation had zero errors/warnings;
  .NET Standard 2.1 profile passed. Twelve Unity-dependent files passed syntax
  parsing only, **not** Unity API type-checking.
- Content validation: zero errors/warnings. Includes 100 landmarks, 200 caches,
  20 missions, 30 contracts plus 5 templates, 4 vehicles and 20 research tiers.
- Formula checks passed. Economy simulation found no modeled deadlocks/arbitrage.
- No Unity compilation, PlayMode tests, APK/AAB build or device test was performed.
  Current headless outputs are in ignored `Logs/headless/`; prior committed
  evidence is under `Docs/evidence/headless-cs/`.

### Remaining work

| Area | Actual state / next gate |
|---|---|
| Foundation | Install Unity, verify editor/package pins, resolve compile errors; generate and preserve scenes/settings/`.meta` files; configure URP |
| Runtime integration | `RuntimeHosts.cs` boot/title hosts are placeholders that log messages. Complete composition, transitions, vehicle rig and gameplay wiring |
| UI | Presentation models exist, not finished rendered touch screens |
| Vehicle | Pure math tested; WheelCollider driving/camera/device controls unverified |
| World | Scatter placement and `WorldStreamer` host missing (KI-017); validate routes and positions |
| Campaign | Data/services exist; full playable campaign unverified |
| Art/audio | M5 not started; tracked placeholders, including app icon |
| Performance/usability | M6 not started; requires physical-device measurements/playtests |
| Release | M7 not started; no verified signed Play-ready AAB |

## Publishing sequence

1. **Initialize Unity.** Install Unity 6.3 LTS (`6000.3.x`) with Android Build
   Support, bundled SDK/NDK and OpenJDK. Run `Tools/Build/pin_toolchain.py` with
   the actual installed editor version (see [BUILD_AND_RUN.md](BUILD_AND_RUN.md)).
   Resolve packages/compiler errors. Run **Tools → Wayfinder → Setup Project**;
   create/assign a mobile URP asset if warned. Preserve generated scenes, settings,
   package lockfile and `.meta` GUIDs in version control. Setup can overwrite app
   identifiers: apply final release settings afterwards.
2. **Finish and test the game.** Run Unity EditMode/PlayMode suites. First prove
   a device slice: New Expedition → driving/objective → banking/save → Continue.
   Complete streaming/scatter/world routes, campaign, final assets and accessibility.
   Test offline cold launch, touch/Back, process death/backgrounding, storage failure,
   save recovery, updates, frame time, memory, battery and sustained thermals.
3. **Set Android release identity.** Select the permanent package ID before first
   Play upload. Setup currently writes `com.wayfinder.quietfrontier`; availability
   and ownership are not established. Set a version name and increasing integer
   version code. Verify IL2CPP/ARM64, minimum/target APIs and native 16 KB page-size
   compatibility for the actual submission date. Audit the merged manifest and SDKs.
4. **Build a signed AAB.** Use Unity's Android Build Profile UI: enable **Build App
   Bundle (Google Play)**, disable Development Build/debugging, select release scenes
   and configure a private upload keystore in Publishing Settings. Keep keys/passwords
   outside Git and chat, with secure backups. Configure Play App Signing in Console.
   Build a `.aab` and test the Play-delivered install through internal testing.
5. **Set up Play Console.** Register/use an owner-controlled personal or organization
   account; complete identity/contact/device verification as requested. Organization
   accounts may require D-U-N-S documentation. Pay the enrollment amount displayed
   by Google (historically US$25 once; verify current amount/taxes). Configure
   payments/tax details if selling the download.
6. **Create the listing.** Choose Game, language, permanent identity, free/paid model,
   category/tags, countries and pricing. Supply final icon/feature graphic, genuine
   gameplay screenshots, descriptions and support contact. Common store graphic
   sizes are 512×512 icon and 1024×500 feature graphic; check current Console rules.
7. **Complete policy declarations.** Publish a public privacy-policy webpage and link
   it in Console and in-app. Complete Data safety, ads, app access, target audience,
   content rating and other required forms. Audit the real binary before declaring
   no data collection; local-only saves are different from transmitted data. Check
   asset licenses/credits. Children's audiences introduce additional obligations.
8. **Test, review, publish.** Upload to internal testing; fix bundle/device/pre-launch
   errors. Complete any account-specific closed-testing and production-access gates;
   internal testing is not a substitute. The current tester count/duration in Console
   is authoritative. Submit the production release for review only with owner approval.
   Use managed publishing if desired, then monitor vitals/reviews/support. Ship fixes
   with higher version codes; do not assume downgrade-by-upload is possible.

### AAB source path added; real Unity validation still required

`Assets/Wayfinder/Scripts/Editor/BuildCommands.cs` now explicitly requests an APK
for development and a signed `.aab` for release, restores temporary signing state,
and throws on build failures. This supersedes the initial audit's APK-only finding.
The new source is not proof of a working signed bundle. Run the owner pipeline in
[UNITY_HANDOFF.md](UNITY_HANDOFF.md), configure signing privately, then validate the
Play-delivered build. No submission or publication has occurred.

## Official policy references

These links are for owner verification, **not live-verified policy evidence**.
Network access in the audit environment did not permit checking Google/Unity
pages. Do not treat this guide as a claim about the exact October 2026 target API,
account testing threshold, fee or deadline. Follow current Console requirements.

- [Play Console](https://play.google.com/console)
- [Developer enrollment](https://support.google.com/googleplay/android-developer/answer/6112435)
- [Target API policy](https://support.google.com/googleplay/android-developer/answer/11926878)
- [Personal-account testing](https://support.google.com/googleplay/android-developer/answer/14151465)
- [Play App Signing](https://support.google.com/googleplay/android-developer/answer/9842756)
- [Data safety](https://support.google.com/googleplay/android-developer/answer/10787469)
- [User Data policy](https://support.google.com/googleplay/android-developer/answer/10144311)
- [16 KB native compatibility](https://developer.android.com/guide/practices/page-sizes)
- [Unity Android manual](https://docs.unity3d.com/Manual/android.html)

Further project evidence: [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md),
[KNOWN_ISSUES.md](KNOWN_ISSUES.md), [EVIDENCE_CS_HEADLESS.md](EVIDENCE_CS_HEADLESS.md),
[ASSET_MANIFEST.csv](ASSET_MANIFEST.csv). The full Google Doc includes the release
checklist, key-management instructions and step-by-step owner workflow.
