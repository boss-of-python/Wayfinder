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
# The download itself goes through curl, not `gh api .../logs`: current gh refuses to emit a log
# that contains terminal escape sequences ("the response contains terminal escape sequences; pass
# --allow-escape-sequences to output it anyway") — and a Unity job log always contains them, so
# every download in runs 39139527909/39141218150/39141401948 (2026-10-18) failed 12 times and the
# real Unity failure stayed invisible. curl writes the bytes verbatim and follows the API's 302
# redirect to the signed log URL. `gh` (with and without the escape-sequence flag) remains as a
# fallback for environments without curl or a token in the environment.
#
# Usage:  fetch_job_log.sh <job name> <output file> [attempts] [sleep seconds]
# Env:    GITHUB_REPOSITORY, GITHUB_RUN_ID, GITHUB_RUN_ATTEMPT, GH_TOKEN (or GITHUB_TOKEN),
#         WAYFINDER_GH / WAYFINDER_CURL to override the `gh` / `curl` executables (used by tests).
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
curl_cli="${WAYFINDER_CURL:-curl}"
repository="${GITHUB_REPOSITORY:-}"
run_id="${GITHUB_RUN_ID:-}"
run_attempt="${GITHUB_RUN_ATTEMPT:-1}"
token="${GH_TOKEN:-${GITHUB_TOKEN:-}}"

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

# One download attempt: curl first (immune to gh's escape-sequence refusal), then gh with the
# flag that opts out of it, then plain gh for versions too old to know the flag. The API answers
# with a redirect to a short-lived signed blob URL, and the archive can lag a few seconds behind
# the job's completion; the caller retries the whole attempt before giving up.
download_log() {
  if [ -n "$token" ] && command -v "$curl_cli" >/dev/null 2>&1; then
    if "$curl_cli" -fsSL \
        -H "Authorization: Bearer $token" \
        -H "Accept: application/vnd.github+json" \
        "https://api.github.com/repos/$repository/actions/jobs/$job_id/logs" \
        -o "$output" 2>"$errfile" && [ -s "$output" ]; then
      return 0
    fi
  fi
  if "$gh_cli" api "repos/$repository/actions/jobs/$job_id/logs" --allow-escape-sequences \
      >"$output" 2>"$errfile" && [ -s "$output" ]; then
    return 0
  fi
  "$gh_cli" api "repos/$repository/actions/jobs/$job_id/logs" >"$output" 2>"$errfile" && [ -s "$output" ]
}

errfile="$(mktemp 2>/dev/null || echo "${output}.err")"
last_error=""
for attempt in $(seq 1 "$attempts"); do
  # gh's/curl's own stderr is kept: run 38880573866 reported only "could not download", which hid
  # the actual cause.
  if download_log; then
    lines="$(wc -l <"$output" | tr -d ' ')"
    bytes="$(wc -c <"$output" | tr -d ' ')"
    rm -f "$errfile"
    echo "Downloaded job $job_id log: $lines lines, $bytes bytes -> $output"
    echo "::notice title=Unity job log::Job \"$job_name\" ($job_id): $lines lines, $bytes bytes downloaded for diagnosis."
    exit 0
  fi
  # Keep the last non-empty line of the downloader's error, with the signed query string removed.
  last_error="$(grep -v '^[[:space:]]*$' "$errfile" 2>/dev/null | tail -1 | cut -c1-300 \
    | sed -E 's/\?[^" ]*/?<redacted>/g')"
  [ "$attempt" -lt "$attempts" ] && sleep "$pause"
done

rm -f "$output" "$errfile"
detail="${last_error:-no error text from the downloader}"
echo "Downloading repos/$repository/actions/jobs/$job_id/logs failed $attempts times: $detail"
echo "::notice title=Unity job log::Could not download the log of job $job_id after $attempts attempts: $detail. Needs actions: read; open the run in the Actions tab instead."
exit 1
