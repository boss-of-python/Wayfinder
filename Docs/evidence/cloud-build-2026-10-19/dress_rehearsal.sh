#!/usr/bin/env bash
# Dress rehearsal for the Unity job-log fetch (2026-10-19, after merging PR #15).
#
# The real Unity job log cannot be downloaded from the implementation sandbox (the Actions log host
# and the artefact blob host are both outside its allow-list), so this rehearsal reproduces the
# failure modes instead, with stubs faithful to each downloader:
#
#   * `gh` >= 2.63 refuses a raw non-JSON body carrying terminal escape sequences
#     ("the response contains terminal escape sequences; pass --allow-escape-sequences to output it
#     anyway") and, per cli/cli pkg/cmd/api/api.go, redirecting stdout does NOT bypass the guard.
#   * `curl` writes bytes verbatim — which fixes the download and leaves the log unparsable, because
#     game-ci colourises the very lines that matter.
#
# It runs the fetcher as it was at the base commit 8611c6c (must reproduce the reported failure),
# as PR #15 left it on main (downloads, but see part 4), and as it is now.
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

# A Unity job log exactly as the Actions API serves it: ANSI colour from game-ci, \r redraws from
# Docker, GitHub timestamp prefixes, and the compile error PR #15 identified in ContentImporter.cs
# (`Newtonsoft.Json.Linq` with no `Unity.Newtonsoft.Json` reference in Wayfinder.Editor.asmdef).
emit_unity_log() {
  printf '2026-10-18T15:52:01.1111111Z \033[36m[INFO]\033[0m Unity build\r\n'
  printf '2026-10-18T15:56:40.3333333Z \033[KAssets/Wayfinder/Scripts/Editor/ContentImporter.cs(112,44): \033[31merror CS0234: The type or namespace name `Json'"'"' does not exist in the namespace `Newtonsoft'"'"'\033[0m\n'
  printf '2026-10-18T15:56:41.4444444Z Aborting batchmode due to failure:\n'
  printf '2026-10-18T15:56:41.5555555Z Scripts have compiler errors.\n'
  printf '2026-10-18T15:56:44.6666666Z \033[31m[ERROR]\033[0m Error: Unity aborted before completing: Scripts have compiler errors.\n'
}

# ---- the gh >= 2.63 stub ------------------------------------------------------------------
cat >"$work/fake-gh" <<'STUB'
#!/usr/bin/env bash
case "$*" in
  *"/jobs?per_page=100"*) printf '%s\n' "117718330570" ;;
  *"/actions/jobs/117718330570/logs"*)
    case "$*" in
      *"--allow-escape-sequences"*) "$GH_EMIT_LOG" ;;
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

# ---- the curl stub: verbatim bytes, honouring -o -----------------------------------------
cat >"$work/fake-curl" <<'STUB'
#!/usr/bin/env bash
out=""
while [ $# -gt 0 ]; do if [ "$1" = "-o" ]; then out="$2"; fi; shift; done
"$GH_EMIT_LOG" >"$out"
STUB
chmod +x "$work/fake-curl"

# The same bytes for both stubs, so the only variable under test is the fetcher.
{
  printf '#!/usr/bin/env bash\n'
  declare -f emit_unity_log
  printf 'emit_unity_log\n'
} >"$work/emit-log.sh"
chmod +x "$work/emit-log.sh"

fetch_env() { # fetch_env <dir> <script> [extra env assignments...]
  local dir="$1" script="$2"; shift 2
  mkdir -p "$dir/error_logs"
  ( cd "$dir" && env -i PATH=/usr/bin:/bin \
      WAYFINDER_GH="$work/fake-gh" WAYFINDER_CURL="$work/fake-curl" GH_EMIT_LOG="$work/emit-log.sh" \
      GITHUB_REPOSITORY=boss-of-python/Wayfinder \
      GITHUB_RUN_ID=39141401948 GITHUB_RUN_ATTEMPT=1 "$@" \
      bash "$script" "Build Wayfinder-dev.apk" error_logs/unity-job-log.txt 2 0 2>&1 )
}

echo "== 1. the fetcher at base commit $base_commit, against gh >= 2.63 =="
git -C "$repo_root" show "$base_commit:Tools/Build/fetch_job_log.sh" >"$work/old-fetch.sh"
old_out="$(fetch_env "$work/old" "$work/old-fetch.sh")"
old_status=$?
echo "$old_out"
check "the old fetcher reproduces the reported refusal" 1 "$old_status"
case "$old_out" in
  *"the response contains terminal escape sequences"*)
    echo "PASS  the refusal text is the one from runs 39139527909/39141218150/39141401948" ;;
  *) echo "FAIL  the refusal text did not match"; failures=$((failures + 1)) ;;
esac
[ -f "$work/old/error_logs/unity-job-log.txt" ] \
  && { echo "FAIL  the old fetcher left a log behind"; failures=$((failures + 1)); } \
  || echo "PASS  the old fetcher left no log, so the reporter had nothing to read"

echo
echo "== 2. the current fetcher over the gh path (no token in the environment) =="
new_out="$(fetch_env "$work/new" "$repo_root/Tools/Build/fetch_job_log.sh")"
echo "$new_out"
log="$work/new/error_logs/unity-job-log.txt"
grep -q "downloaded with gh --allow-escape-sequences" <<<"$new_out" \
  && echo "PASS  gh was asked to opt out of the guard" \
  || { echo "FAIL  the opt-out flag was not used"; failures=$((failures + 1)); }

echo
echo "== 3. the current fetcher over the curl path (token present, as in the workflow) =="
curl_out="$(fetch_env "$work/curl" "$repo_root/Tools/Build/fetch_job_log.sh" GH_TOKEN=fake-token)"
echo "$curl_out"
curl_log="$work/curl/error_logs/unity-job-log.txt"
grep -q "downloaded with curl" <<<"$curl_out" \
  && echo "PASS  curl was preferred when a token was available" \
  || { echo "FAIL  curl was not used"; failures=$((failures + 1)); }

echo
echo "== 4. is what each path wrote actually parsable? =="
for pair in "gh:$log" "curl:$curl_log"; do
  name="${pair%%:*}"; path="${pair#*:}"
  if grep -q $'\033' "$path" 2>/dev/null; then
    echo "FAIL  $name path: escape sequences survived into the parsed log"; failures=$((failures + 1))
  else
    echo "PASS  $name path: no escape sequence survives"
  fi
  if grep -q $'\r' "$path" 2>/dev/null; then
    echo "FAIL  $name path: carriage returns survived"; failures=$((failures + 1))
  else
    echo "PASS  $name path: no carriage return survives"
  fi
  # ESC[K sits immediately before real text: a too-wide CSI class eats that text with the sequence.
  if grep -q '^2026-10-18T15:56:40\.3333333Z Assets/Wayfinder/Scripts/Editor/ContentImporter\.cs(112,44): error CS0234:' "$path"; then
    echo "PASS  $name path: the compile-error line survived intact, on one line"
  else
    echo "FAIL  $name path: the compile-error line was damaged"; failures=$((failures + 1))
  fi
  [ -s "${path}.excerpt" ] \
    && echo "PASS  $name path: the excerpt tail was written for the artefact" \
    || { echo "FAIL  $name path: no excerpt tail"; failures=$((failures + 1)); }
done

echo
echo "== 5. the control: the same bytes WITHOUT sanitising (what a verbatim download alone gives) =="
"$work/emit-log.sh" >"$work/verbatim.log"
control="$(cd "$work" && env -i PATH=/usr/bin:/bin UNITY_JOB_RESULT=failure GITHUB_STEP_SUMMARY=/dev/null \
  bash "$repo_root/Tools/Build/report_unity_failure.sh" "$work" "$work/verbatim.log" 2>&1)"
echo "$control"
case "$control" in
  *"::error file=Assets/Wayfinder/Scripts/Editor/ContentImporter.cs"*)
    echo "FAIL  the unsanitised log was annotated, so sanitising is not load-bearing"; failures=$((failures + 1)) ;;
  *) echo "PASS  the unsanitised log loses the compile-error annotation (this is the gap sanitising closes)" ;;
esac
# Precisely what is lost: the abort reason survives because that line carries no escapes, so an
# unsanitised download still looks diagnosed while the actual file/line/col cause is missing.
case "$control" in
  *"::error title=Unity aborted batchmode::Scripts have compiler errors."*)
    echo "PASS  the unsanitised log still reports the abort reason, so it looks diagnosed while the cause is missing" ;;
  *) echo "FAIL  unexpected control outcome: no abort reason either"; failures=$((failures + 1)) ;;
esac
# When nothing else matches, the escapes also end up inside the annotation text (the tail notice),
# which GitHub's workflow-command parser rejects. That variant needs a log with no abort line, and
# is pinned by test_a_raw_unsanitised_log_would_miss_the_compile_error instead of being restated
# here against a fixture that does have one.

echo
echo "== 6. the reporter over the sanitised log =="
report_out="$(cd "$work/new" && env -i PATH=/usr/bin:/bin UNITY_JOB_RESULT=failure \
  GITHUB_STEP_SUMMARY="$work/new/summary.md" \
  bash "$repo_root/Tools/Build/report_unity_failure.sh" "$PWD" error_logs/unity-job-log.txt 2>&1)"
echo "$report_out"
case "$report_out" in
  *"::error file=Assets/Wayfinder/Scripts/Editor/ContentImporter.cs,line=112,col=44"*)
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
if grep -q $'\033' <<<"$report_out"; then
  echo "FAIL  raw escape bytes leaked into annotation text"; failures=$((failures + 1))
else
  echo "PASS  no escape byte leaks into the annotation text"
fi

echo
if [ "$failures" -eq 0 ]; then
  echo "DRESS REHEARSAL: all checks passed."
else
  echo "DRESS REHEARSAL: $failures check(s) failed."
fi
exit "$failures"
