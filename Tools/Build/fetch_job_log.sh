#!/usr/bin/env bash
# Download the raw log of a finished job of the *current* workflow run.
#
# Why this exists: game-ci's build.sh launches `unity-editor -logfile /dev/stdout ...`, so Unity's
# editor log is the job log, not a file in the workspace. Run 38875757162 (2026-10-16) proved that
# a `-logFile` custom parameter does not change this: the workspace held only Unity's auxiliary
# worker logs, so `report_unity_failure.sh` had nothing to read and the only annotation on an
# 8-minute failed build was GameCI's "Build failed with exit code 1".
#
# A second job (needs: <build job>, if: always()) runs this: by then the build job is finished, so
# GET /repos/{owner}/{repo}/actions/jobs/{job_id}/logs returns its complete plain-text log, which
# is then parsed into check-run annotations. Needs `actions: read` on the workflow token.
#
# Usage:  fetch_job_log.sh <job name> <output file> [attempts] [sleep seconds]
# Env:    GITHUB_REPOSITORY, GITHUB_RUN_ID, GITHUB_RUN_ATTEMPT, GH_TOKEN (or GITHUB_TOKEN),
#         WAYFINDER_GH to override the `gh` executable (used by the tests).
#
# Exit 0 only when a non-empty log was written; the caller keeps `continue-on-error: true`, so a
# diagnostic download can never change the build's verdict.
set -u

job_name="${1:-}"
output="${2:-}"
attempts="${3:-6}"
pause="${4:-5}"

if [ -z "$job_name" ] || [ -z "$output" ]; then
  echo "usage: fetch_job_log.sh <job name> <output file> [attempts] [sleep seconds]" >&2
  exit 2
fi

gh_cli="${WAYFINDER_GH:-gh}"
repository="${GITHUB_REPOSITORY:-}"
run_id="${GITHUB_RUN_ID:-}"
run_attempt="${GITHUB_RUN_ATTEMPT:-1}"

if [ -z "$repository" ] || [ -z "$run_id" ]; then
  echo "::notice title=Unity job log::GITHUB_REPOSITORY/GITHUB_RUN_ID are unset; cannot download the build job log."
  exit 1
fi

mkdir -p "$(dirname "$output")" 2>/dev/null || true

job_id=""
for attempt in $(seq 1 "$attempts"); do
  # Attempt-scoped so a re-run reads its own job, not the first attempt's.
  job_id="$(
    "$gh_cli" api \
      "repos/$repository/actions/runs/$run_id/attempts/$run_attempt/jobs?per_page=100" \
      --jq ".jobs[] | select(.name == \"$job_name\") | .id" 2>/dev/null | head -1
  )"
  [ -n "$job_id" ] && break
  [ "$attempt" -lt "$attempts" ] && sleep "$pause"
done

if [ -z "$job_id" ]; then
  echo "::notice title=Unity job log::No job named \"$job_name\" in run $run_id attempt $run_attempt; the Unity log could not be downloaded."
  exit 1
fi

for attempt in $(seq 1 "$attempts"); do
  # The archive can lag a few seconds behind the job's completion; retry before giving up.
  if "$gh_cli" api "repos/$repository/actions/jobs/$job_id/logs" >"$output" 2>/dev/null && [ -s "$output" ]; then
    lines="$(wc -l <"$output" | tr -d ' ')"
    bytes="$(wc -c <"$output" | tr -d ' ')"
    echo "Downloaded job $job_id log: $lines lines, $bytes bytes -> $output"
    echo "::notice title=Unity job log::Job \"$job_name\" ($job_id): $lines lines, $bytes bytes downloaded for diagnosis."
    exit 0
  fi
  [ "$attempt" -lt "$attempts" ] && sleep "$pause"
done

rm -f "$output"
echo "::notice title=Unity job log::Could not download the log of job $job_id (needs actions: read). Open the run in the Actions tab instead."
exit 1
