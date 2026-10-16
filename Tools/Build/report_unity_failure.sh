#!/usr/bin/env bash
# Turn Unity's undescriptive CI failure ("Build failed with exit code 1") into the real cause.
#
# Unity aborts batchmode with exit code 1 for many different reasons, and GameCI prints
# "Please note that the exit code is not very descriptive". This script reads the Unity
# Editor.log that survives in the workspace and emits GitHub Actions annotations, which are
# readable from the API (repos/{owner}/{repo}/check-runs/{id}/annotations) without having to
# download the raw run log.
#
# Usage:  report_unity_failure.sh [workspace_dir] [log_file ...]
#   workspace_dir  defaults to $GITHUB_WORKSPACE, else the current directory
#   log_file       optional explicit logs; defaults to the paths GameCI/Unity write to
#
# Always exits 0: it reports a failure, it must never become one (the Unity step's own exit
# code is the job's verdict).
set -u

workspace="${1:-${GITHUB_WORKSPACE:-$PWD}}"
shift 2>/dev/null || true
summary="${GITHUB_STEP_SUMMARY:-/dev/null}"

# ---- locate the logs ---------------------------------------------------------------------
logs=""
if [ "$#" -gt 0 ]; then
  logs="$*"
else
  # The explicit Editor.log path and the *.log globs can match the same file; list each once.
  logs="$(
    for candidate in \
      "$workspace/Builds/Logs/Editor.log" \
      "$workspace"/Builds/Logs/*.log \
      "$workspace"/Logs/*.log
    do
      [ -f "$candidate" ] && printf '%s\n' "$candidate"
    done | sort -u | tr '\n' ' '
  )"
fi

say() { printf '%s\n' "$1" >>"$summary"; }
say "## Unity build diagnostics"

if [ -z "${logs// /}" ]; then
  say "No Unity log survived in the workspace; read the Unity step output above."
  echo "::notice title=Unity diagnostics::No Editor.log left in the workspace - read the Unity step output for the failure."
  exit 0
fi

say "Logs inspected:$logs"

# Every Library/PackageCache path Unity mentioned, i.e. the packages it really unpacked.
resolved_paths="$(grep -hoE 'Library/PackageCache/[A-Za-z0-9.@+-]+' $logs 2>/dev/null | sort -u)"

# Strip the "2026-10-15T14:56:44.9611349Z " prefix GitHub Actions adds when it echoes a log.
detimestamp() { sed -E 's/^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]+Z //'; }

# ---- 1. compile errors: "path(line,col): error CS1234: message" --------------------------
error_lines="$(grep -hoE '[^ ]+\([0-9]+,[0-9]+\): error CS[0-9]+: .*' $logs 2>/dev/null | detimestamp | sort -u)"
error_count="$(printf '%s\n' "$error_lines" | grep -c . || true)"
say "- Unity compile errors (unique): **$error_count**"

if [ "$error_count" -gt 0 ]; then
  say '```'
  printf '%s\n' "$error_lines" | head -40 >>"$summary"
  say '```'
  # GitHub caps annotations per level per run, so report ten and say how many are hidden.
  reported=0
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    if [ "$reported" -ge 10 ]; then break; fi
    file="$(printf '%s' "$line" | sed -E 's/^(.*)\(([0-9]+),([0-9]+)\): error CS[0-9]+:.*$/\1/')"
    lineno="$(printf '%s' "$line" | sed -E 's/^(.*)\(([0-9]+),([0-9]+)\): error CS[0-9]+:.*$/\2/')"
    column="$(printf '%s' "$line" | sed -E 's/^(.*)\(([0-9]+),([0-9]+)\): error CS[0-9]+:.*$/\3/')"
    message="$(printf '%s' "$line" | sed -E 's/^[^(]*\([0-9]+,[0-9]+\): //')"
    echo "::error file=$file,line=$lineno,col=$column,title=Unity compile error::$message"
    reported=$((reported + 1))
  done <<EOF
$error_lines
EOF
  if [ "$error_count" -gt "$reported" ]; then
    echo "::error title=Unity compile errors::$error_count unique compile errors; $reported reported here, all of them are in the Unity log."
  fi
fi

# ---- 2. why batchmode aborted ------------------------------------------------------------
abort_reasons="$(grep -hA1 'Aborting batchmode due to failure' $logs 2>/dev/null | detimestamp \
  | grep -v 'Aborting batchmode due to failure' | grep -vE '^(--)?$' | sort -u)"
if [ -n "$abort_reasons" ]; then
  while IFS= read -r reason; do
    [ -z "$reason" ] && continue
    say "- Unity aborted batchmode: $reason"
    echo "::error title=Unity aborted batchmode::$reason"
  done <<EOF
$abort_reasons
EOF
fi

aborted="$(grep -hoE 'Unity aborted before completing: .*' $logs 2>/dev/null | detimestamp | sort -u | head -1)"
if [ -n "$aborted" ]; then
  say "- GameCI: $aborted"
  echo "::error title=Unity aborted::$aborted"
fi

# ---- 3. package resolution (an unpinnable version fails here, before any compile) ---------
resolution="$(grep -hoiE '(cannot perform upm operation|unable to find package|package [^ ]+ cannot be found|no matching version|cannot find package|failed to resolve packages|package has invalid dependencies)[:]?.*' $logs 2>/dev/null \
  | detimestamp | sort -u | head -10)"
if [ -n "$resolution" ]; then
  say "- Package resolution failures:"
  say '```'
  printf '%s\n' "$resolution" >>"$summary"
  say '```'
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    echo "::error title=Unity package resolution::$line"
  done <<EOF
$resolution
EOF
fi

# Which pinned packages Unity actually unpacked, so a silent downgrade is visible.
manifest="$workspace/Packages/manifest.json"
if [ -f "$manifest" ] && [ -n "$resolved_paths" ]; then
  say "- Pinned packages seen in Library/PackageCache:"
  for package in $(grep -oE '"com\.unity\.[a-z0-9.-]+"' "$manifest" | tr -d '"' | sort -u); do
    escaped="$(printf '%s' "$package" | sed 's/\./\\./g')"
    if printf '%s\n' "$resolved_paths" | grep -qE "PackageCache/${escaped}(@|$|[[:space:]/])"; then
      say "  - $package"
    fi
  done
fi

# ---- 4. editor exceptions (a buildMethod that throws, e.g. Setup Project) -----------------
exceptions="$(grep -hoE '(BuildFailedException|UnityException|InvalidOperationException|NullReferenceException|ArgumentException|MissingMethodException|TypeLoadException|IOException|UnauthorizedAccessException): .*' $logs 2>/dev/null \
  | detimestamp | sort -u | head -10)"
if [ -n "$exceptions" ]; then
  say '```'
  printf '%s\n' "$exceptions" >>"$summary"
  say '```'
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    echo "::error title=Unity exception::$line"
  done <<EOF
$exceptions
EOF
fi

# ---- 5. warnings in our own code (this repository holds itself to zero, D-014) ------------
warning_lines="$(grep -hoE 'Assets/Wayfinder[^ ]*\([0-9]+,[0-9]+\): warning CS[0-9]+: .*' $logs 2>/dev/null | detimestamp | sort -u)"
warning_count="$(printf '%s\n' "$warning_lines" | grep -c . || true)"
say "- Compiler warnings in Assets/Wayfinder (unique): **$warning_count**"
if [ "$warning_count" -gt 0 ]; then
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    file="$(printf '%s' "$line" | sed -E 's/^(.*)\(([0-9]+),([0-9]+)\): warning CS[0-9]+:.*$/\1/')"
    lineno="$(printf '%s' "$line" | sed -E 's/^(.*)\(([0-9]+),([0-9]+)\): warning CS[0-9]+:.*$/\2/')"
    message="$(printf '%s' "$line" | sed -E 's/^[^(]*\([0-9]+,[0-9]+\): //')"
    echo "::warning file=$file,line=$lineno,title=Unity warning::$message"
  done <<EOF
$warning_lines
EOF
fi

# ---- 6. what Unity actually compiled (proves which assemblies are Unity-verified) ---------
compiled="$(grep -hoE 'Csc Library/Bee/artifacts/[^ ]*/Wayfinder\.[A-Za-z.]+\.dll' $logs 2>/dev/null \
  | sed -E 's#.*/(Wayfinder\.[A-Za-z.]+)\.dll#\1#' | sort -u)"
if [ -n "$compiled" ]; then
  say "- Wayfinder assemblies Unity compiled:"
  while IFS= read -r asm; do
    [ -z "$asm" ] && continue
    say "  - $asm"
    echo "::notice title=Unity compiled::$asm"
  done <<EOF
$compiled
EOF
fi

# ---- 7. did the APK appear? --------------------------------------------------------------
for apk in "$workspace/Builds/Android/Wayfinder-dev.apk" "$workspace"/Builds/Android/*.apk "$workspace"/Builds/Android/*.aab; do
  if [ -f "$apk" ]; then
    size="$(stat -c %s "$apk" 2>/dev/null || stat -f %z "$apk" 2>/dev/null || echo '?')"
    say "- Artefact: ${apk#"$workspace"/} ($size bytes)"
    echo "::notice title=Unity artefact::${apk#"$workspace"/}, $size bytes"
  fi
done

if [ "$error_count" -eq 0 ] && [ -z "$aborted" ]; then
  say "- No compile error or batchmode abort found in the logs inspected."
fi

exit 0
