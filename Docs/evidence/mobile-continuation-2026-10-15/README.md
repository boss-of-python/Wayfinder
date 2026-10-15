# Mobile continuation evidence — 15 October 2026

Actual sandbox execution, not Unity/device/store evidence:

- `headless-console.log`, `EditMode.headless.xml`, `compile-report.json`:
  `python3 Tools/Build/headless_cs.py all` passed with **59/59** C# domain tests,
  zero compile warnings/errors and passing .NET Standard 2.1 profile checks. Four
  new lifecycle-ordering/duplicate-callback tests are included.
- `latest-syntax.log`: `python3 Tools/Build/headless_cs.py syntax` passed for
  **21 Unity-dependent files**, rerun after the last HUD/FPS edits. This checks
  grammar only, not Unity types, physics, rendering or package compatibility.
- `python-runner-tests.log`: **23/23** Python contract tests passed with mocked
  Unity processes and temporary placeholder archives; none is a real APK/AAB.
- `unity-doctor.log`: actual editor discovery failed, exit 1, because Unity is
  not installed/found.
- `workflow-static-check.log`: PyYAML parsed the workflow and assertions checked
  its manual trigger, owner guard, dedicated runner labels, environment, read-only
  permissions, pinned action SHAs, safe input passing and success-only APK upload.
  This is NOT an executed GitHub Actions job. PyYAML 6.0.2 was a sandbox-only tool,
  installed under ignored `/home/user/.cache/wayfinder-yaml`, not an app dependency.

The public repository/default branch and action commit SHAs were read through
`gh`; no workflow dispatch, self-hosted runner registration, paid resource,
license activation, secret upload, commit, push, merge or publication was performed.

The preceding 55-test results remain under `../unity-prep-2026-10-15/`. This
continuation adds source functionality but does not close KI-001/002 or any real
Unity/Android/device gate. See `../../MOTO_G45_BUILD_AND_TEST.md`.
