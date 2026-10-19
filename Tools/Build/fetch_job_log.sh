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
# Run 39141401948 (2026-10-18) exposed the third reason this diagnosis stayed blind: `gh api`
# refuses to print a response that contains terminal escape sequences, and a Unity job log is full
# of them (ANSI colours from game-ci/Docker, plus `\r` progress redraws). gh >= 2.63 therefore
# answered all twelve attempts with
#   "the response contains terminal escape sequences; pass --allow-escape-sequences to output it
#    anyway"
# and no log was ever written. So this script (a) asks for the raw bytes explicitly when the
# installed gh supports that flag and (b) always sanitises what it writes: escape sequences and
# carriage returns would otherwise split the very lines report_unity_failure.sh greps for
# (`error CS0117:`, `Aborting batchmode due to failure:`, ...) and would be rejected by the
# workflow-command parser, which forbids ESC in annotation text.
#
# Usage:  fetch_job_log.sh <job name> <output file> [attempts] [sleep seconds]
# Env:    GITHUB_REPOSITORY, GITHUB_RUN_ID, GITHUB_RUN_ATTEMPT, GH_TOKEN (or GITHUB_TOKEN),
#         WAYFINDER_GH to override the `gh` executable (used by the tests).
#
# Writes: <output>            sanitised plain-text job log (what the reporter parses)
#         <output>.excerpt    last lines of the log, so the uploaded error_logs/ artefact carries
#                             the end of Unity's output even if no annotation pattern matches
# The verbatim download is kept only in a temp file and reported as a byte delta: a failing Unity
# job log runs to tens of MB, and uploading it twice would not buy anything the sanitised log and
# the excerpt do not already carry.
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

errfile="$(mktemp 2>/dev/null || echo "${output}.err")"

# gh >= 2.63 needs an explicit opt-in before it prints a response containing escape sequences.
# Detect it once instead of guessing from a version string: an unsupported flag is a hard error.
allow_escapes=""
if "$gh_cli" api --help 2>&1 | grep -q -- '--allow-escape-sequences'; then
  allow_escapes="--allow-escape-sequences"
fi

# Plain text only: drop CSI/OSC sequences (colours, cursor moves), lone ESCs and the \r that
# Docker progress redraws leave behind. Note the intermediate-byte class is written `[ -/]`
# (space..slash), *not* `[ -\/]`: a backslash is not an escape inside a POSIX bracket expression,
# so `[ -\/]` silently means space..backslash and eats real log text — it turned
# "\033[KAborting batchmode" into "orting batchmode" before the fixture below caught it.
sanitise() {
  sed -e 's/\o033\[[0-9;?]*[ -/]*[@-~]//g' \
      -e 's/\o033\][^\a\o033]*\(\a\|\o033\\\)//g' \
      -e 's/\o033//g' \
      -e 's/\r//g'
}

last_error=""
rawfile="$(mktemp 2>/dev/null || echo "${output}.raw")"
for attempt in $(seq 1 "$attempts"); do
  # The API answers with a redirect to a short-lived signed blob URL, and the archive can lag a
  # few seconds behind the job's completion; retry before giving up. gh's own stderr is kept:
  # run 38880573866 reported only "could not download", which hid the actual cause.
  if "$gh_cli" api $allow_escapes "repos/$repository/actions/jobs/$job_id/logs" >"$rawfile" 2>"$errfile" \
    && [ -s "$rawfile" ]; then
    sanitise <"$rawfile" >"$output"
    if [ -s "$output" ]; then
      # Keep the end of Unity's own output in the uploaded artefact: annotations carry a few
      # hundred characters, and the cause of a hard editor stop is the last thing said.
      {
        printf 'Last 400 lines of job %s ("%s"), run %s attempt %s, sanitised:\n\n' \
          "$job_id" "$job_name" "$run_id" "$run_attempt"
        grep -v '^[[:space:]]*$' "$output" 2>/dev/null | tail -400 | cut -c1-400
      } >"${output}.excerpt" 2>/dev/null || true
      lines="$(wc -l <"$output" | tr -d ' ')"
      bytes="$(wc -c <"$output" | tr -d ' ')"
      raw_bytes="$(wc -c <"$rawfile" | tr -d ' ')"
      removed=$((raw_bytes - bytes))
      rm -f "$errfile" "$rawfile"
      echo "Downloaded job $job_id log: $lines lines, $bytes bytes -> $output"
      # Say how much sanitising removed, so a sanitiser that ever ate real content is visible.
      if [ "$removed" -gt 0 ]; then
        echo "Stripped $removed bytes of escape sequences/carriage returns from $raw_bytes downloaded."
      else
        echo "The download contained no escape sequences to strip ($raw_bytes bytes)."
      fi
      if [ -n "$allow_escapes" ]; then
        echo "gh accepted --allow-escape-sequences."
      else
        echo "This gh build has no --allow-escape-sequences flag; sanitising made the log parsable anyway."
      fi
      echo "::notice title=Unity job log::Job \"$job_name\" ($job_id): $lines lines, $bytes bytes downloaded for diagnosis."
      exit 0
    fi
    last_error="the download contained only control characters; nothing parsable survived sanitising"
  else
    # Keep the last non-empty line of gh's error, with the signed query string removed.
    last_error="$(grep -v '^[[:space:]]*$' "$errfile" 2>/dev/null | tail -1 | cut -c1-300 \
      | sed -E 's/\?[^" ]*/?<redacted>/g')"
  fi
  rm -f "$rawfile"
  [ "$attempt" -lt "$attempts" ] && sleep "$pause"
done

rm -f "$output" "$rawfile" "${output}.excerpt" "$errfile"
detail="${last_error:-no error text from gh}"
echo "gh api repos/$repository/actions/jobs/$job_id/logs failed $attempts times: $detail"

# Name the likely cause instead of always blaming the token. Runs 39139527909/39141218150/
# 39141401948 each printed "Needs actions: read" over an escape-sequence refusal, and the workflow
# had already granted that permission — the hint sent three investigations after a non-problem.
case "$detail" in
  *"escape sequences"*)
    hint="gh refused the body because it contains terminal escape sequences. This script passes --allow-escape-sequences when \`gh api --help\` advertises it, so either the runner's gh is older than 2.63 or it stopped advertising the flag: check \`gh --version\` in the job log." ;;
  *"403"* | *"404"* | *"resource not accessible"* | *"permission"* | *"scope"*)
    hint="Needs \`actions: read\` on the workflow token (and the job must be finished)." ;;
  *)
    hint="Open the run in the Actions tab to read the Unity log there." ;;
esac
echo "::notice title=Unity job log::Could not download the log of job $job_id after $attempts attempts: $detail. $hint"
exit 1
