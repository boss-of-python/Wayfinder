#!/usr/bin/env python3
"""Mutation check for the self-reporting cloud build (Batch H, 2026-10-16).

Each mutation reverts one safeguard added after the cloud-build failures of 2026-10-16. Run
38875757162 exposed the silent reporter; rerun 38880573866 exposed that a missing-editor-log
error suppressed the surviving worker-log tail. Targets:
Tools/Build/report_unity_failure.sh, Tools/Build/fetch_job_log.sh,
.github/workflows/android-cloud-build.yml and Assets/Wayfinder/Scripts/Editor/EditorFailureLog.cs.

Tools/Build/test_build_diagnostics.py must fail for every mutation; a mutation that still
passes means the suite does not actually pin that behaviour.

Run from the repository root:
    python3 Docs/evidence/cloud-build-2026-10-16/mutation_check.py
"""
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
REPORT = ROOT / "Tools/Build/report_unity_failure.sh"
FETCH = ROOT / "Tools/Build/fetch_job_log.sh"
WORKFLOW = ROOT / ".github/workflows/android-cloud-build.yml"
FAILURE_LOG = ROOT / "Assets/Wayfinder/Scripts/Editor/EditorFailureLog.cs"
SETUP = ROOT / "Assets/Wayfinder/Scripts/Editor/ProjectSetup.cs"
TESTS = ROOT / "Tools/Build/test_build_diagnostics.py"

# (id, description, path, old, new)
MUTATIONS = [
    ("N1", "let an unexplained failure pass without an annotation (the 38875757162 silence)",
     REPORT, 'if [ "$step_failed" = yes ] && [ "$causes_emitted" -eq 0 ]; then',
     "if false; then"),
    ("N2", "stop reporting that no editor log was available",
     REPORT, 'if [ "$step_failed" = yes ] && [ "$editor_log" = no ]; then',
     "if false; then"),
    ("N3", "accept Unity's worker logs as if they were the editor log",
     REPORT, "  editor_log=no", "  editor_log=yes"),
    ("N4", "ignore the Unity step outcomes the workflow passes in",
     REPORT, 'for outcome in "${UNITY_SETUP_OUTCOME:-}" "${UNITY_BUILD_OUTCOME:-}" "${UNITY_JOB_RESULT:-}"; do',
     'for outcome in ""; do'),
    ("N5", "stop saying which log files were inspected",
     REPORT, 'echo "::notice title=Unity diagnostics::Inspected $inspected; editor log recognised: $editor_log."',
     "true"),
    ("N6", "emit the log tail without encoding its newlines (breaks the workflow command)",
     REPORT, 'echo "::notice title=Unity log tail::$(escape "$tail_text")"',
     'echo "::notice title=Unity log tail::$tail_text"'),
    ("N7", "stop grepping for the editor entry point's failure breadcrumb",
     REPORT, r"'\[Wayfinder\] Editor method failed: .*'", "'zzz-no-such-marker: .*'"),
    ("N8", "stop detecting an unresolvable -executeMethod build method",
     REPORT, "grep -hE 'executeMethod'", "grep -hE 'zzz-executeMethod'"),
    ("N9", "download whichever job happens to be first instead of the build job",
     FETCH, '--jq ".jobs[] | select(.name == \\"$job_name\\") | .id"', '--jq ".jobs[] | .id"'),
    # Retargeted 2026-10-19: the download now lands in a temp file and is sanitised before it is
    # judged, so the "is what we wrote usable?" guard moved inside the loop. Same intent as before
    # (an empty download must never be reported as success), new line.
    ("N10", "keep an empty downloaded log and call it a success",
     FETCH, '    if [ -s "$output" ]; then',
     '    if [ -f "$output" ]; then'),
    ("N11", "go back to the ubuntu-latest label that migrates to Ubuntu 26 on 2026-10-19",
     WORKFLOW, "    runs-on: ubuntu-24.04\n    timeout-minutes: 180",
     "    runs-on: ubuntu-latest\n    timeout-minutes: 180"),
    ("N12", "let the diagnosis job run without waiting for the build job",
     WORKFLOW, "    needs: apk\n", ""),
    ("N13", "reinstate the -logFile custom parameter that delivers no editor log",
     WORKFLOW, "          buildMethod: Wayfinder.Editor.ProjectSetup.Setup\n          versioning: 'None'",
     "          buildMethod: Wayfinder.Editor.ProjectSetup.Setup\n          versioning: 'None'\n"
     '          customParameters: "-logFile /github/workspace/Logs/Unity-Setup.log"'),
    ("N14", "stop telling the in-job reporter how the Unity steps ended",
     WORKFLOW, "          UNITY_SETUP_OUTCOME: ${{ steps.unity_setup.outcome }}\n", ""),
    ("N15", "rename the C# breadcrumb marker so the shell pattern no longer matches it",
     FAILURE_LOG, 'Marker = "[Wayfinder] Editor method failed: "',
     'Marker = "[Wayfinder] editor failure: "'),
    ("N16", "swallow the editor exception instead of rethrowing it (build would go green)",
     FAILURE_LOG, "Record(method, error);\n                throw;", "Record(method, error);"),
    ("N17", "run Setup without the failure recorder again",
     SETUP, 'public static void Setup() => EditorFailureLog.Run("Wayfinder.Editor.ProjectSetup.Setup", RunSetup);',
     "public static void Setup() => RunSetup();"),
    ("N18", "suppress the fallback tail when only Unity worker logs survive",
     REPORT, 'if [ "$step_failed" = yes ] && [ "$causes_emitted" -eq 0 ]; then',
     'if [ "$step_failed" = yes ] && [ "$editor_log" = yes ]; then'),
    ("N19", "skip pre-creating the APK error-log destination before preflight",
     WORKFLOW, "mkdir -p error_logs", "mkdir -p missing_error_logs"),
    ("N20", "stop publishing the dedicated APK error-log artifact",
     WORKFLOW, "name: Wayfinder-APK-error-logs", "name: Wayfinder-APK-log-disabled"),
    ("N21", "stop extracting raw Unity error lines into the report file",
     WORKFLOW, "grep -Ein", "grep -E"),
    ("N22", "allow the reporting-only diagnose job to fail the workflow",
     WORKFLOW, "    continue-on-error: true\n    runs-on: ubuntu-24.04",
     "    runs-on: ubuntu-24.04"),
]


def suite_passes():
    result = subprocess.run([sys.executable, str(TESTS)], capture_output=True, text=True, cwd=ROOT)
    return result.returncode == 0


def main():
    if not suite_passes():
        print("ABORT: the suite must be green before a mutation check.")
        return 1

    backups = {path: path.read_text() for path in {m[2] for m in MUTATIONS}}

    caught, missed = 0, []
    try:
        for mid, description, path, old, new in MUTATIONS:
            text = backups[path]
            if old not in text:
                print(f"{mid}: SKIP - pattern not found (mutation is stale, fix this script)")
                missed.append(f"{mid} (stale)")
                continue
            path.write_text(text.replace(old, new, 1))
            if suite_passes():
                print(f"{mid}: MISSED - suite still passed after: {description}")
                missed.append(mid)
            else:
                print(f"{mid}: caught - {description}")
                caught += 1
            path.write_text(text)
    finally:
        for path, text in backups.items():
            path.write_text(text)

    total = len(MUTATIONS)
    print(f"\n{caught}/{total} mutations caught.")
    if missed:
        print(f"Not caught: {', '.join(missed)}")
        return 1
    if not suite_passes():
        print("ABORT: files were not restored cleanly.")
        return 1
    print("Files restored; suite green again.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
