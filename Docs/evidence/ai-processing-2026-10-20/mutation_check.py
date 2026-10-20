#!/usr/bin/env python3
"""Mutation check for the AI-processing triage batch (2026-10-20).

Every safeguard this batch added is reverted here, one at a time, and the test that is supposed to
pin it must fail. A test that still passes after its safeguard is gone proves nothing, and this
repository's rule (D-036 and the batches before it) is that a fix is only "verified" when the check
can fail.

Run from the repository root:

    python3 Docs/evidence/ai-processing-2026-10-20/mutation_check.py

Each mutation is applied to the working tree, the selected test command is executed, and the file is
restored in a `finally` block, so an interrupted run never leaves a mutation behind.
"""
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
SIM = ROOT / "Assets/Wayfinder/Scripts/Simulation"
SURFACE = SIM / "SurfaceTable.cs"
FADE = SIM / "SurfaceTransition.cs"
IDLE = SIM / "IdleAutoStop.cs"
SAVE = ROOT / "Assets/Wayfinder/Scripts/Persistence/SaveService.cs"
MAP = ROOT / "Assets/Wayfinder/Scripts/Campaign/CampaignSession.Map.cs"
BUILD = ROOT / "Assets/Wayfinder/Scripts/Editor/BuildCommands.cs"
SETUP = ROOT / "Assets/Wayfinder/Scripts/Editor/ProjectSetup.cs"
DIAGNOSTICS_TESTS = "Tools/Build/test_build_diagnostics.py"


def headless(where):
    # `all`, not `test`: `test` runs the previously compiled assembly, so a source mutation would
    # silently pass (this was measured on the first run of this script: 6 of 8 mutations survived
    # because nothing was recompiled).
    return [sys.executable, "Tools/Build/headless_cs.py", "all", "--", "--where", where]


def python_tests():
    return [sys.executable, DIAGNOSTICS_TESTS]


# (id, what the mutation claims is unnecessary, path, old text, new text, command)
MUTATIONS = [
    ("M1", "blind array indexing on an undefined surface label (the reported SYS-SIM-001 crash)",
     SURFACE,
     "public static SurfaceCoefficients Get(SurfaceType surface) => Table[(int)Sanitize(surface)];",
     "public static SurfaceCoefficients Get(SurfaceType surface) => Table[(int)surface];",
     headless("class =~ SimulationTests")),

    ("M2", "apply the new surface coefficient on the same tick (no §4.4 cross-fade)",
     FADE,
     "                Progress = 0f;\n                if (immediate)",
     "                Progress = 1f;\n                if (immediate)",
     headless("class =~ SimulationTests")),

    ("M3", "never auto-stop a parked engine (the §5.1 idle drain the report calls a bug)",
     IDLE,
     "            if (_parkedSeconds < DelaySeconds) return false;",
     "            if (_parkedSeconds < DelaySeconds * 1000f) return false;",
     headless("class =~ SimulationTests")),

    ("M4", "relabel a save through a missing migration step instead of failing",
     SAVE,
     '''                if (step == null)
                    throw new InvalidOperationException(
                        $"no save migration from v{from} to v{from + 1}; this build cannot upgrade that save");''',
     '''                if (step == null)
                {
                    version = from + 1;
                    continue;
                }''',
     headless("class =~ PersistenceTests")),

    ("M5", "stop marking the map dirty when a marker is committed (the polling SYS-WRD-002 asks to remove)",
     MAP,
     '''            if (result.Success)
            {
                _autosave.MarkDirty();
                _mapRevision++;
            }
            return result;
        }

        /// <summary>Points the dashed route line''',
     '''            if (result.Success) _autosave.MarkDirty();
            return result;
        }

        /// <summary>Points the dashed route line''',
     headless("class =~ ExplorationMapTests")),

    ("M6", "let a failed BuildPlayer return without a non-zero batchmode exit (SYS-CICD-002)",
     BUILD,
     '''                Debug.LogError("[Wayfinder] batchmode build failed; exiting with code 1 so CI cannot pass.");
                EditorApplication.Exit(1);''',
     '''                Debug.LogError("[Wayfinder] batchmode build failed; exiting with code 1 so CI cannot pass.");''',
     python_tests()),

    ("M7", "leave the Android target API level on Auto (SYS-CICD-001 version drift)",
     SETUP,
     "            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)AndroidTargetSdkApiLevel;",
     "            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;",
     python_tests()),

    ("M8", "let the workflow and ProjectSetup declare different target API levels",
     ROOT / ".github/workflows/android-cloud-build.yml",
     "  ANDROID_TARGET_SDK: '36'",
     "  ANDROID_TARGET_SDK: '35'",
     python_tests()),
]


def run(command):
    return subprocess.run(command, cwd=ROOT, capture_output=True, text=True)


def main():
    failures = []
    for mutation_id, description, path, old, new, command in MUTATIONS:
        original = path.read_text()
        if original.count(old) != 1:
            print(f"[{mutation_id}] SKIP — anchor found {original.count(old)} times in {path.name}")
            failures.append(mutation_id)
            continue
        path.write_text(original.replace(old, new))
        try:
            result = run(command)
        finally:
            path.write_text(original)
        killed = result.returncode != 0
        print(f"[{mutation_id}] {'killed' if killed else 'SURVIVED'} — {description}")
        if not killed:
            failures.append(mutation_id)
            tail = (result.stdout + result.stderr).strip().splitlines()[-5:]
            for line in tail:
                print("        " + line)
    print()
    print(f"{len(MUTATIONS) - len(failures)}/{len(MUTATIONS)} mutations killed by the suite")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
