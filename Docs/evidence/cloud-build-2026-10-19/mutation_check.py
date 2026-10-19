#!/usr/bin/env python3
"""Mutation check for the escape-sequence fix (2026-10-19).

Runs 39139527909, 39141218150 and 39141401948 all failed in *Unity 1 of 2: Setup Project* and all
three diagnose jobs reported the same refusal twelve times over
(`escape-sequence-refusals.txt`, verbatim from the annotations API):

    the response contains terminal escape sequences; pass --allow-escape-sequences to output it
    anyway

`gh api` guards raw non-JSON bodies, and a Unity job log is full of ANSI colour and \\r progress
redraws, so the editor log was never downloaded and the real Unity cause stayed unknown for three
consecutive runs (KI-041). Each mutation below reverts one safeguard added by that fix;
Tools/Build/test_build_diagnostics.py must fail for every one of them.

Run from the repository root:
    python3 Docs/evidence/cloud-build-2026-10-19/mutation_check.py
"""
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
REPORT = ROOT / "Tools/Build/report_unity_failure.sh"
FETCH = ROOT / "Tools/Build/fetch_job_log.sh"
WORKFLOW = ROOT / ".github/workflows/android-cloud-build.yml"
TESTS = ROOT / "Tools/Build/test_build_diagnostics.py"

# (id, description, path, old, new)
MUTATIONS = [
    # Retargeted after merging PR #15 (curl-first download): the capability probe is gone, because
    # the chain now tries curl, then `gh --allow-escape-sequences`, then plain `gh`.
    ("P1", "never ask gh to opt out of the escape-sequence guard (the three-run blindness of 2026-10-18)",
     FETCH, 'if "$gh_cli" api "repos/$repository/actions/jobs/$job_id/logs" --allow-escape-sequences \\',
     'if "$gh_cli" api "repos/$repository/actions/jobs/$job_id/logs" \\'),
    ("P2", "drop the plain-gh fallback, so a gh too old for the flag gets nothing",
     FETCH, '''  if "$gh_cli" api "repos/$repository/actions/jobs/$job_id/logs" >"$rawfile" 2>"$errfile" \\
    && [ -s "$rawfile" ]; then
    downloader="gh"
    return 0
  fi
''', ""),
    ("P19", "stop preferring curl, so gh's escape-sequence guard can block the download again",
     FETCH, 'if [ -n "$token" ] && command -v "$curl_cli" >/dev/null 2>&1; then', "if false; then"),
    # The gap this batch closes on top of PR #15: curl fixes the *download* and writes bytes
    # verbatim, so without sanitising the reporter still cannot annotate a colourised compile error.
    ("P3", "save the downloaded job log without sanitising it",
     FETCH, 'sanitise <"$rawfile" >"$output"', 'cat <"$rawfile" >"$output"'),
    ("P4", "widen the CSI intermediate-byte class back to [ -\\/], which eats the text after ESC[K",
     FETCH, r"s/\o033\[[0-9;?]*[ -/]*[@-~]//g", r"s/\o033\[[0-9;?]*[ -\/]*[@-~]//g"),
    ("P5", "stop leaving carriage returns out of the parsed log",
     FETCH, r"      -e 's/\r//g'", r"      -e 's/\r/\r/g'"),
    ("P6", "stop writing the excerpt tail the artefact carries",
     FETCH, '} >"${output}.excerpt" 2>/dev/null || true', "} >/dev/null 2>/dev/null || true"),
    ("P7", "stop reporting how many bytes sanitising removed",
     FETCH, 'if [ "$removed" -gt 0 ]; then', "if false; then"),
    ("P8", "report a download that sanitises to nothing as a success",
     FETCH, '    if [ -s "$output" ]; then', '    if [ -f "$output" ]; then'),
    ("P9", "stop naming a crash/OOM kill, so a hard stop looks like 'nothing matched'",
     REPORT, 'if [ -n "$crash_lines" ]; then', "if false; then"),
    ("P10", "report any 'out of memory' text as a hard stop, including Unity's benign lines",
     REPORT, "|Out of memory!.*|", "|.*out of memory.*|"),
    ("P11", "stop treating a crash as evidence that a Unity step failed",
     REPORT, "|Fatal Error!|Crash!!!|SIG(SEGV|ABRT|BUS|ILL)'", "|Fatal Error!'"),
    ("P12", "claim the breadcrumb is missing even where Unity never ran (the diagnose job)",
     REPORT, '[ -d "$workspace/Logs" ] && [ ! -f "$breadcrumb_file" ]', '[ ! -f "$breadcrumb_file" ]'),
    ("P13", "stop inferring anything from an absent breadcrumb",
     REPORT, 'if [ "$step_failed" = yes ] && [ -d "$workspace/Logs" ] && [ ! -f "$breadcrumb_file" ]; then',
     "if false; then"),
    ("P14", "drop the crash/OOM alternatives from the workflow's raw error-line grep",
     WORKFLOW, "|Crash!!!|Native stacktrace|Fatal Error!|Out of memory|", "|"),
    # Removing the whole tail block, not just its `if`: mutating the condition alone is an
    # equivalent mutant, because the `else` branch recomputes the same tail from the full log.
    ("P15", "stop appending the end of the Unity log to the downloadable error report",
     WORKFLOW,
     "              printf '\\n--- Last 400 lines of the Unity job log ---\\n'\n"
     "              if [ -s error_logs/unity-job-log.txt.excerpt ]; then\n"
     "                cat error_logs/unity-job-log.txt.excerpt\n"
     "              else\n"
     "                grep -v '^[[:space:]]*$' error_logs/unity-job-log.txt | tail -400 | cut -c1-400\n"
     "              fi\n",
     ""),
    ("P16", "let the raw error-line grep flood the report file unbounded",
     WORKFLOW, "error_logs/unity-job-log.txt | head -400 | cut -c1-400 || true",
     "error_logs/unity-job-log.txt || true"),
    # The refusal notice blamed `actions: read` for three runs while the permission was granted;
    # the hint is now derived from the error text, so each branch needs its own mutation.
    ("P17", "stop recognising an escape-sequence refusal and blame the token again",
     FETCH, '  *"escape sequences"*)', '  *"zzz-escape sequences"*)'),
    ("P18", "stop recognising a permission error",
     FETCH, '  *"403"* | *"404"* | *"resource not accessible"* | *"permission"* | *"scope"*)',
     '  *"zzz-403"*)'),
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
