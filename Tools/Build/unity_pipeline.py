#!/usr/bin/env python3
"""Owner-machine Unity setup/test/Android pipeline (Windows, macOS, Linux).

No downloads, activation, publication or credentials on the command line. Set
UNITY_BIN or pass --unity. First install/license Unity 6.3 LTS with Android modules.
Signing uses local Publishing Settings or the four WAYFINDER_* signing env vars.
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
VERSION = re.compile(r"(?<!\d)(6000\.3\.\d+f\d+)(?!\w)")
METHODS = {
    "setup": "Wayfinder.Editor.ProjectSetup.Setup",
    "android-dev": "Wayfinder.Editor.BuildCommands.BuildAndroidDev",
    "android-release": "Wayfinder.Editor.BuildCommands.BuildAndroidRelease",
}
ARTIFACTS = {
    "android-dev": "Builds/Android/Wayfinder-dev.apk",
    "android-release": "Builds/Android/Wayfinder.aab",
}


class PipelineError(RuntimeError):
    pass


def find_unity(explicit=None):
    value = explicit or os.environ.get("UNITY_BIN")
    if value:
        path = Path(value).expanduser()
        if not path.is_file():
            raise PipelineError("Unity executable not found. Pass --unity with the editor executable, not Unity Hub.")
        return path.resolve()
    for name in ("Unity", "unity", "Unity.exe"):
        value = shutil.which(name)
        if value:
            return Path(value).resolve()
    bases = [Path.home() / "Unity/Hub/Editor", Path("/Applications/Unity/Hub/Editor")]
    if os.environ.get("ProgramFiles"):
        bases.append(Path(os.environ["ProgramFiles"]) / "Unity/Hub/Editor")
    candidates = []
    for base in bases:
        for version in base.glob("6000.3.*f*"):
            for suffix in ("Editor/Unity", "Editor/Unity.exe", "Unity.app/Contents/MacOS/Unity"):
                candidate = version / suffix
                if candidate.is_file():
                    candidates.append(candidate)
    if len(candidates) == 1:
        return candidates[0].resolve()
    if len(candidates) > 1:
        raise PipelineError("Multiple Unity editors found. Choose the pinned editor with --unity or UNITY_BIN.")
    raise PipelineError("Unity is not installed/found. Install and activate Unity 6.3 LTS with Android Build Support, SDK/NDK and OpenJDK, then set UNITY_BIN.")


def editor_version(unity):
    try:
        result = subprocess.run([str(unity), "-version"], capture_output=True, text=True, timeout=60)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise PipelineError("Could not query Unity editor version.") from exc
    match = VERSION.search(result.stdout + result.stderr)
    if result.returncode or not match:
        raise PipelineError("Expected an installed stable Unity 6.3 LTS editor (6000.3.<patch>f<n>).")
    return match.group(1)


def check_project(root, version, pin=False):
    manifest = root / "Packages/manifest.json"
    if not manifest.is_file():
        raise PipelineError("Packages/manifest.json missing: select the Wayfinder repository root.")
    try:
        json.loads(manifest.read_text(encoding="utf-8"))
    except (ValueError, OSError) as exc:
        raise PipelineError("Packages/manifest.json cannot be read as JSON.") from exc
    path = root / "ProjectSettings/ProjectVersion.txt"
    if path.exists():
        match = re.search(r"^m_EditorVersion:\s*(\S+)", path.read_text(), re.MULTILINE)
        if not match or match.group(1) != version:
            raise PipelineError("Installed editor differs from ProjectVersion.txt. Select the pinned editor; do not silently upgrade the project.")
    elif pin:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(f"m_EditorVersion: {version}\n", encoding="utf-8")
        print(f"Pinned installed editor: {version}")
    else:
        print("Editor pin missing; setup/smoke will pin the verified installed version.")


def command(unity, root, log, method=None, test_platform=None):
    args = [str(unity), "-batchmode", "-projectPath", str(root), "-buildTarget", "Android", "-logFile", str(log)]
    if test_platform:
        # Test Framework controls editor exit. -quit can stop the editor before tests run.
        args += ["-runTests", "-testPlatform", test_platform, "-testResults", str(log.with_suffix(".xml"))]
    else:
        args += ["-quit", "-executeMethod", method]
    return args


def verify_tests(path):
    try:
        node = ET.parse(path).getroot()
        total = int(node.get("total", "0"))
        failed = int(node.get("failed", "0"))
        if node.get("result") != "Passed" or failed or total <= 0:
            raise PipelineError(f"Unity tests did not pass with nonzero test count: {path}")
    except (OSError, ET.ParseError, ValueError) as exc:
        raise PipelineError(f"Missing or invalid Unity test results: {path}") from exc
    print(f"Unity tests passed: {total} ({path.name})")


def verify_artifact(path, bundle):
    try:
        with zipfile.ZipFile(path) as archive:
            required = "base/manifest/AndroidManifest.xml" if bundle else "AndroidManifest.xml"
            if required not in archive.namelist():
                raise PipelineError(f"Build lacks the expected Android manifest: {path}")
            if archive.testzip() is not None:
                raise PipelineError(f"Corrupt build archive: {path}")
    except (OSError, zipfile.BadZipFile) as exc:
        raise PipelineError(f"Missing/invalid build artifact: {path}") from exc


def run_step(unity, root, step, timeout):
    logs = root / "Logs/unity-pipeline"
    logs.mkdir(parents=True, exist_ok=True)
    log = logs / f"{step}.log"
    test = step in ("EditMode", "PlayMode")
    result_path = log.with_suffix(".xml") if test else root / ARTIFACTS[step] if step in ARTIFACTS else logs / "setup-complete.json"
    # Remove only test reports (not owner build artifacts) to prevent stale-pass reuse.
    if test and result_path.exists():
        result_path.unlink()
    before = result_path.stat().st_mtime_ns if result_path and result_path.exists() else None
    args = command(unity, root, log, test_platform=step) if test else command(unity, root, log, method=METHODS[step])
    print(f"Running {step}; log: {log}", flush=True)
    try:
        result = subprocess.run(args, cwd=root, timeout=timeout)
    except subprocess.TimeoutExpired as exc:
        raise PipelineError(f"{step} timed out; inspect {log} and close any remaining Unity/Gradle child processes before retrying.") from exc
    except OSError as exc:
        raise PipelineError(f"Could not start Unity for {step}.") from exc
    if result.returncode:
        raise PipelineError(f"{step} failed (exit {result.returncode}). Inspect {log} for licensing/package/compiler/build errors.")
    if test:
        verify_tests(result_path)
    elif step in ARTIFACTS:
        if not result_path.exists() or result_path.stat().st_mtime_ns == before:
            raise PipelineError(f"No new build artifact produced by {step}; inspect {log}.")
        verify_artifact(result_path, bundle=step == "android-release")
        print(f"Build artifact: {result_path}")
    else:
        if not result_path.exists() or result_path.stat().st_mtime_ns == before:
            raise PipelineError(f"Setup did not complete; inspect {log}.")
        for relative in ("Assets/Wayfinder/Scenes/Bootstrap.unity", "Assets/Wayfinder/Resources/WayfinderControls.json", "Packages/packages-lock.json"):
            if not (root / relative).is_file():
                raise PipelineError(f"Setup output missing: {relative}; inspect {log}.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["doctor", "setup", "test", "android-dev", "android-release", "smoke"])
    parser.add_argument("--unity", help="Full Unity editor executable path (or set UNITY_BIN)")
    parser.add_argument("--project-path", type=Path, default=ROOT)
    parser.add_argument("--timeout", type=int, default=1800, help="Maximum seconds per Unity step")
    args = parser.parse_args(argv)
    if args.timeout <= 0:
        parser.error("--timeout must be positive")
    try:
        unity = find_unity(args.unity)
        version = editor_version(unity)
        root = args.project_path.expanduser().resolve()
        check_project(root, version, pin=args.action in ("setup", "smoke"))
        print(f"Unity: {unity}\nVersion: {version}\nProject: {root}")
        if args.action not in ("doctor", "setup", "smoke") and not (root / "ProjectSettings/ProjectVersion.txt").is_file():
            raise PipelineError("Project is not pinned. Run setup or smoke first.")
        if args.action == "doctor":
            print("Executable/version/manifest checked only. License, packages and Android modules are verified by the actual setup/build, not by this doctor check.")
            return 0
        steps = {
            "setup": ["setup"], "test": ["EditMode", "PlayMode"],
            "smoke": ["setup", "EditMode", "PlayMode", "android-dev"],
            "android-dev": ["android-dev"], "android-release": ["android-release"],
        }[args.action]
        for step in steps:
            run_step(unity, root, step, args.timeout)
        print("Requested Unity steps completed. No publication performed; device/store gates remain separate.")
        return 0
    except PipelineError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
