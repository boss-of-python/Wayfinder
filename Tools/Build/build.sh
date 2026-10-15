#!/usr/bin/env bash
# Wayfinder build helper. Requires a Unity 6.3 LTS editor (see Docs/BUILD_AND_RUN.md).
# Usage: Tools/Build/build.sh [doctor|setup|smoke|android-dev|android-release|ios-dev|ios-release|test-editmode|test-playmode|validate]
set -euo pipefail
cd "$(dirname "$0")/../.."

UNITY_BIN="${UNITY_BIN:-}"
if [[ -z "$UNITY_BIN" ]]; then
  for candidate in \
      "$HOME/Unity/Hub/Editor/6000.3."*/Editor/Unity \
      "/Applications/Unity/Hub/Editor/6000.3."*/Unity.app/Contents/MacOS/Unity; do
    if [[ -x "$candidate" ]]; then UNITY_BIN="$candidate"; break; fi
  done
fi

cmd="${1:-validate}"

case "$cmd" in
  validate)
    python3 Tools/Validation/validate_content.py
    python3 Tools/Validation/formula_checks.py
    python3 Tools/Balance/economy_sim.py
    ;;
  doctor|setup|smoke|android-dev|android-release)
    args=(); [[ -n "$UNITY_BIN" ]] && args+=(--unity "$UNITY_BIN")
    python3 Tools/Build/unity_pipeline.py "$cmd" "${args[@]}"
    ;;
  test-editmode|test-playmode)
    # Tests control editor exit; never pass -quit alongside -runTests.
    if [[ -z "$UNITY_BIN" ]]; then echo "ERROR: set UNITY_BIN to the Unity editor executable"; exit 1; fi
    platform="EditMode"; [[ "$cmd" == "test-playmode" ]] && platform="PlayMode"
    mkdir -p Logs
    rm -f "Logs/${platform}.xml"
    "$UNITY_BIN" -batchmode -projectPath . \
      -runTests -testPlatform "$platform" -testResults "Logs/${platform}.xml" -logFile "Logs/${platform}.log"
    python3 -c 'import sys; sys.path.insert(0, "Tools/Build"); from unity_pipeline import verify_tests; from pathlib import Path; verify_tests(Path(sys.argv[1]))' "Logs/${platform}.xml"
    ;;
  ios-dev|ios-release)
    if [[ -z "$UNITY_BIN" ]]; then echo "ERROR: set UNITY_BIN to the Unity editor executable"; exit 1; fi
    method="Wayfinder.Editor.BuildCommands.BuildIOSDev"
    [[ "$cmd" == "ios-release" ]] && method="Wayfinder.Editor.BuildCommands.BuildIOSRelease"
    mkdir -p Logs
    "$UNITY_BIN" -batchmode -quit -projectPath . \
      -executeMethod "$method" -logFile "Logs/build_${cmd}.log"
    echo "Build log: Logs/build_${cmd}.log"
    ;;
  *)
    echo "Unknown command: $cmd"; exit 1;;
esac
