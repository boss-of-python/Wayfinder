# Known issues / blockers

All entries affect M0 source batch; no player build exists yet.

| ID | Severity | Reproduction / evidence | Status / resolution |
|---|---|---|---|
| TOOL-001 | Gate blocker | `command -v Unity dotnet mcs adb xcodebuild` finds none on this Linux sandbox | Blocked: install licensed Unity 6000.3.0f1 with build modules on owner machine; iOS requires Mac/Xcode |
| PKG-001 | Gate blocker | Package versions have source evidence, but no Unity UPM resolution ran | Blocked: resolve on unrestricted workstation; review lock diff and compile |
| BUILD-001 | Gate blocker | No Android APK/AAB, iOS Xcode export, EditMode/PlayMode results or device capture | Blocked: run BUILD_AND_RUN.md, attach actual artifacts/evidence |
| UI-001 | High before release | Open Title: development-only text/panels; no final artwork/audio | Placeholder: replace/verify during M2/M5; 150% scale and physical dp targets not certified |
| FLOW-001 | Planned M2 | New Game/Continue are unavailable; no campaign or saved settings | Not started: implement durable new-game creation and full loop in M2 |
| MOBILE-001 | Gate verification | Lifecycle/input reset code and tests exist but no device execution | Blocked: test focus + pause callback ordering, held controls, Android Back, safe areas |
| LEGAL-001 | Release blocker | Owner has not supplied commercial art/audio or trademark review | No rights certification; asset manifest tracks source placeholders |

Potential import/API defects remain risks, not confirmed working behavior. Never
mark these resolved based solely on Python checks or generated YAML. Save integrity,
vehicle behavior, world streaming, localization integration and performance have
not been implemented or certified in M0. No known failing Unity test is suppressed.
