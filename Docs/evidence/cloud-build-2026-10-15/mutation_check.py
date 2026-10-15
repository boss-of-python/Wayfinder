#!/usr/bin/env python3
"""Mutation check for the cloud-build diagnostics (Batch F, 2026-10-15).

Each mutation breaks one safeguard in Tools/Build/report_unity_failure.sh,
Tools/Build/check_unity_secrets.sh or .github/workflows/android-cloud-build.yml.
Tools/Build/test_build_diagnostics.py must fail for every mutation; a mutation that
still passes means the test suite does not actually pin that behaviour.

Run from the repository root:
    python3 Docs/evidence/cloud-build-2026-10-15/mutation_check.py
"""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
REPORT = ROOT / "Tools/Build/report_unity_failure.sh"
SECRETS = ROOT / "Tools/Build/check_unity_secrets.sh"
WORKFLOW = ROOT / ".github/workflows/android-cloud-build.yml"
TESTS = ROOT / "Tools/Build/test_build_diagnostics.py"

# (id, description, path, old, new[, replace_all])
# M1 must hit every call site: the compile-error regex is anchored on "[^ ]+", so a timestamp
# prefix can never reach that annotation anyway - only the whole-line patterns (abort reason,
# exceptions, package resolution) depend on detimestamp.
MUTATIONS = [
    ("M1", "stop stripping the GitHub Actions timestamp prefix from reported paths",
     REPORT, "| detimestamp", "| cat", True),
    ("M2", "raise the annotation cap from 10 to 50",
     REPORT, '$reported" -ge 10', '$reported" -ge 50'),
    ("M3", "count warnings from anywhere under Assets/, not just Assets/Wayfinder",
     REPORT, "'Assets/Wayfinder[^ ]*\\([0-9]+,[0-9]+\\): warning CS[0-9]+: .*'",
     "'Assets/[^ ]*\\([0-9]+,[0-9]+\\): warning CS[0-9]+: .*'"),
    ("M4", "print the license secret value",
     SECRETS, 'echo "All three Unity license secrets are present (values not printed)."',
     'echo "license=$UNITY_LICENSE"'),
    ("M5", "revert the Input System pin to the version that cannot compile on 6000.3.26f1",
     ROOT / "Packages/manifest.json", '"com.unity.inputsystem": "1.20.1"',
     '"com.unity.inputsystem": "1.12.0"'),
    ("M6", "let the diagnostics step run only on failure() instead of always()",
     WORKFLOW, "- name: Report the real Unity failure as annotations\n        if: always()",
     "- name: Report the real Unity failure as annotations\n        if: failure()"),
    ("M7", "stop passing UNITY_PASSWORD to the secret checker",
     WORKFLOW, "          UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}\n        run: bash Tools/Build/check_unity_secrets.sh",
     "        run: bash Tools/Build/check_unity_secrets.sh"),
    ("M8", "stop detecting package-resolution failures (an unpinnable version)",
     REPORT, "grep -hoiE '(cannot perform upm operation",
     "grep -hoiE '(zzz-cannot perform upm operation"),
    ("M9", "stop cross-checking resolved packages against Packages/manifest.json",
     REPORT, 'if [ -f "$manifest" ] && [ -n "$resolved_paths" ]; then',
     'if false; then'),
]


def suite_passes():
    result = subprocess.run([sys.executable, str(TESTS)], capture_output=True, text=True, cwd=ROOT)
    return result.returncode == 0


def main():
    if not suite_passes():
        print("ABORT: the suite must be green before a mutation check.")
        return 1

    backups = {}
    for path in {m[2] for m in MUTATIONS}:
        backups[path] = path.read_text()

    caught, missed = 0, []
    try:
        for mutation in MUTATIONS:
            mid, description, path, old, new = mutation[:5]
            replace_all = mutation[5] if len(mutation) > 5 else False
            text = backups[path]
            if old not in text:
                print(f"{mid}: SKIP - pattern not found (mutation is stale, fix this script)")
                missed.append(f"{mid} (stale)")
                continue
            path.write_text(text.replace(old, new) if replace_all else text.replace(old, new, 1))
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
