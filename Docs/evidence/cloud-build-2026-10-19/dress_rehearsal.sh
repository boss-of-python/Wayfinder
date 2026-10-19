#!/usr/bin/env bash
# Dress rehearsal for the escape-sequence fix (2026-10-19).
#
# The real Unity job log cannot be downloaded from the implementation sandbox (the Actions log
# host and the artefact blob host are both outside its allow-list), so this rehearsal reproduces
# the failure mode instead: a `gh` stub that behaves exactly like gh >= 2.63, which refuses to
# print a raw non-JSON body containing terminal escape sequences
# (pkg/cmd/api/api.go: "the response contains terminal escape sequences; pass
# --allow-escape-sequences to output it anyway"), and answers a Unity-shaped job log — ANSI
# colour, \r redraws, GitHub timestamp prefixes — only when the flag is passed.
#
# It then runs the fetcher as it was at the base commit 8611c6c (must reproduce the reported
# failure) and as it is now (must download, sanitise and be parsable by the reporter).
#
# Usage: bash Docs/evidence/cloud-build-2026-10-19/dress_rehearsal.sh
set -u

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
base_commit="8611c6c"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
failures=0

check() { # check <description> <expected> <actual>
  if [ "$2" = "$3" ]; then
    echo "PASS  $1"
  else
    echo "FAIL  $1 (expected '$2', got '$3')"
    failures=$((failures + 1))
  fi
}

# ---- the gh >= 2.63 stub ------------------------------------------------------------------
cat >"$work/fake-gh" <<'STUB'
#!/usr/bin/env bash
case "$*" in
  *"api --help"*)
    cat <<'HELP'
Flags:
      --allow-escape-sequences   Allow printing terminal escape sequences
HELP
    ;;
  *"/jobs?per_page=100"*) printf '%s\n' "117718330570" ;;
  *"/actions/jobs/117718330570/logs"*)
    case "$*" in
      *"--allow-escape-sequences"*)
        printf '2026-10-18T15:52:01.1111111Z \033[36m[INFO]\033[0m Unity build\r\n'
        printf '2026-10-18T15:55:12.2222222Z \033[1mRefreshing native access tokens\033[0m\n'
        printf '2026-10-18T15:56:40.3333333Z \033[KAssets/Wayfinder/Scripts/Presentation/MapOverlayView.cs(212,31): \033[31merror CS1061: '"'"'RectTransform'"'"' does not contain a definition for '"'"'setSizeDelta'"'"'\033[0m\n'
        printf '2026-10-18T15:56:41.4444444Z Aborting batchmode due to failure:\n'
        printf '2026-10-18T15:56:41.5555555Z Scripts have compiler errors.\n'
        printf '2026-10-18T15:56:44.6666666Z \033[31m[ERROR]\033[0m Error: Unity aborted before completing: Scripts have compiler errors.\n'
        ;;
      *)
        echo 'the response contains terminal escape sequences; pass --allow-escape-sequences to output it anyway' >&2
        exit 1
        ;;
    esac
    ;;
  *) exit 1 ;;
esac
STUB
chmod +x "$work/fake-gh"

fetch_env() {
  env -i PATH=/usr/bin:/bin \
    WAYFINDER_GH="$work/fake-gh" \
    GITHUB_REPOSITORY=boss-of-python/Wayfinder \
    GITHUB_RUN_ID=39141401948 GITHUB_RUN_ATTEMPT=1 "$@"
}

echo "== 1. the fetcher at base commit $base_commit, against gh >= 2.63 =="
mkdir -p "$work/old/error_logs"
git -C "$repo_root" show "$base_commit:Tools/Build/fetch_job_log.sh" >"$work/old-fetch.sh"
old_out="$(cd "$work/old" && fetch_env bash "$work/old-fetch.sh" "Build Wayfinder-dev.apk" error_logs/unity-job-log.txt 2 0 2>&1)"
old_status=$?
echo "$old_out"
check "the old fetcher reproduces the reported refusal" 1 "$old_status"
case "$old_out" in
  *"the response contains terminal escape sequences"*) echo "PASS  the refusal text is the one from runs 39139527909/39141218150/39141401948" ;;
  *) echo "FAIL  the refusal text did not match"; failures=$((failures + 1)) ;;
esac
[ -f "$work/old/error_logs/unity-job-log.txt" ] \
  && { echo "FAIL  the old fetcher left a log behind"; failures=$((failures + 1)); } \
  || echo "PASS  the old fetcher left no log, so the reporter had nothing to read"

echo
echo "== 2. the current fetcher =="
mkdir -p "$work/new/error_logs"
new_out="$(cd "$work/new" && fetch_env bash "$repo_root/Tools/Build/fetch_job_log.sh" "Build Wayfinder-dev.apk" error_logs/unity-job-log.txt 2 0 2>&1)"
new_status=$?
echo "$new_out"
check "the current fetcher downloads the log" 0 "$new_status"
log="$work/new/error_logs/unity-job-log.txt"
if grep -q $'\033' "$log" 2>/dev/null; then
  echo "FAIL  escape sequences survived into the parsed log"; failures=$((failures + 1))
else
  echo "PASS  no escape sequence survives into the parsed log"
fi
if grep -q $'\r' "$log" 2>/dev/null; then
  echo "FAIL  carriage returns survived"; failures=$((failures + 1))
else
  echo "PASS  no carriage return survives"
fi
# ESC[K sits immediately before real text: a too-wide CSI class eats that text with the sequence.
if grep -q '^2026-10-18T15:56:40\.3333333Z Assets/Wayfinder/Scripts/Presentation/MapOverlayView\.cs(212,31): error CS1061:' "$log"; then
  echo "PASS  the compile-error line survived sanitising intact, on one line"
else
  echo "FAIL  the compile-error line was damaged by sanitising"; failures=$((failures + 1))
fi
[ -s "$work/new/error_logs/unity-job-log.txt.excerpt" ] \
  && echo "PASS  the excerpt tail was written for the artefact" \
  || { echo "FAIL  no excerpt tail"; failures=$((failures + 1)); }

echo
echo "== 3. the reporter over the downloaded log =="
report_out="$(cd "$work/new" && env -i PATH=/usr/bin:/bin UNITY_JOB_RESULT=failure \
  GITHUB_STEP_SUMMARY="$work/new/summary.md" \
  bash "$repo_root/Tools/Build/report_unity_failure.sh" "$PWD" error_logs/unity-job-log.txt 2>&1)"
echo "$report_out"
case "$report_out" in
  *"::error file=Assets/Wayfinder/Scripts/Presentation/MapOverlayView.cs,line=212,col=31"*)
    echo "PASS  the compile error became a file/line/col annotation, readable from the API" ;;
  *) echo "FAIL  no file/line/col annotation"; failures=$((failures + 1)) ;;
esac
case "$report_out" in
  *"editor log recognised: yes"*) echo "PASS  the downloaded log is recognised as an editor log" ;;
  *) echo "FAIL  the downloaded log was not recognised"; failures=$((failures + 1)) ;;
esac
case "$report_out" in
  *"::error title=Unity aborted batchmode::Scripts have compiler errors."*)
    echo "PASS  the batchmode abort reason is annotated" ;;
  *) echo "FAIL  no abort reason"; failures=$((failures + 1)) ;;
esac
case "$report_out" in
  *"Unity failure not explained"*)
    echo "FAIL  a cause was found, so 'not explained' must not be reported"; failures=$((failures + 1)) ;;
  *) echo "PASS  no 'failure not explained' fallback when a cause was matched" ;;
esac

echo
if [ "$failures" -eq 0 ]; then
  echo "DRESS REHEARSAL: all checks passed."
else
  echo "DRESS REHEARSAL: $failures check(s) failed."
fi
exit "$failures"
