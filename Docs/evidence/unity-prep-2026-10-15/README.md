# Evidence scope — 15 October 2026 Unity preparation batch

These are actual results from the sandbox, **not a Unity build**.

- `EditMode.headless.xml`, `compile-report.json`, `headless-console.log`:
  `python3 Tools/Build/headless_cs.py all` — 55/55 C# domain tests pass, zero
  compile errors/warnings, .NET Standard 2.1 profile passes. Twenty-one engine
  source files are parsed for syntax only, not Unity API type-checked.
- `python-runner-tests.log`:
  `python3 -m unittest discover -s Tools/Build -p 'test_*.py' -v` — 23/23 tests
  pass using mocked Unity subprocesses and temporary placeholder ZIP/XML files.
  Any 'build artifact' messages in this test log refer to mock archives in
  temporary directories, not real Android builds or signed artifacts.
- `unity-doctor.log`:
  `python3 Tools/Build/unity_pipeline.py doctor` — expected failure, exit 1,
  Unity editor not installed/found.

No Unity import/setup/test/compile/build, Android APK/AAB, device result or store
submission is evidenced here. See ../../UNITY_HANDOFF.md for owner-machine steps
and remaining gates. Historical 14 October evidence is preserved separately.
