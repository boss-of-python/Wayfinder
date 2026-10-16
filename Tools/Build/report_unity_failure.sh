#!/usr/bin/env bash
# Turn Unity's undescriptive CI failure ("Build failed with exit code 1") into the real cause.
#
# Unity aborts batchmode with exit code 1 for many different reasons, and GameCI prints
# "Please note that the exit code is not very descriptive". This script reads a Unity log and
# emits GitHub Actions annotations, which are readable from the API
# (repos/{owner}/{repo}/check-runs/{id}/annotations) without having to download the raw run log.
#
# Which log? game-ci's build.sh runs `unity-editor -logfile /dev/stdout ... $CUSTOM_PARAMETERS`,
# so the editor log is the *job log*, not a file in the workspace: run 38875757162 (2026-10-16)
# proved a `-logFile` custom parameter does not deliver it. The workspace then only holds Unity's
# auxiliary worker logs (`Logs/AssetImportWorker*.log`, shader compiler), which match none of the
# patterns below — that run's diagnostics were therefore completely silent. Two rules follow:
#   1. the workflow's "Explain the Unity result" job downloads the finished job log and passes it
#      here explicitly, and
#   2. this script is never silent: it always says which files it inspected, says so loudly when
#      no editor log was among them, and prints a log tail when a failure has no recognised cause.
#
# Usage:  report_unity_failure.sh [workspace_dir] [log_file ...]
#   workspace_dir  defaults to $GITHUB_WORKSPACE, else the current directory
#   log_file       optional explicit logs; defaults to the paths GameCI/Unity write to
#
# Environment (optional): UNITY_SETUP_OUTCOME / UNITY_BUILD_OUTCOME / UNITY_JOB_RESULT carry the
# verdict of the Unity steps, so a failure with no recognised cause is still reported as one.
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

# Workflow commands are single-line: encode %, CR and LF so a multi-line excerpt survives.
escape() {
  printf '%s' "$1" | awk 'BEGIN { ORS = "" }
    { gsub(/%/, "%25"); gsub(/\r/, "%0D"); if (NR > 1) printf "%%0A"; printf "%s", $0 }'
}

errors_emitted=0
emit_error() { # emit_error <annotation-body>
  printf '::error %s\n' "$1"
  errors_emitted=$((errors_emitted + 1))
}

# A Unity step that was attempted and did not succeed, as reported by the workflow.
step_failed=no
for outcome in "${UNITY_SETUP_OUTCOME:-}" "${UNITY_BUILD_OUTCOME:-}" "${UNITY_JOB_RESULT:-}"; do
  case "$outcome" in
    failure | cancelled | timed_out) step_failed=yes ;;
  esac
done

if [ -z "${logs// /}" ]; then
  say "No Unity log survived in the workspace; read the Unity step output above."
  echo "::notice title=Unity diagnostics::No Editor.log left in the workspace - read the Unity step output for the failure."
  if [ "$step_failed" = yes ]; then
    emit_error "title=Unity log unavailable::A Unity step did not succeed and no log reached this script. The editor log is the job log (game-ci passes -logfile /dev/stdout); the \"Explain the Unity result\" job downloads it from the Actions API."
  fi
  exit 0
fi

say "Logs inspected:$logs"

# Say what was actually read, with sizes: run 38875757162 looked identical to "nothing ran".
inspected=""
for log in $logs; do
  if [ -f "$log" ]; then
    size="$(wc -c <"$log" 2>/dev/null | tr -d ' ') bytes"
  else
    size="missing"
  fi
  inspected="${inspected:+$inspected, }${log#"$workspace"/} ($size)"
done

# Is any of them a real editor log, or only Unity's auxiliary worker/shader logs?
editor_log_markers='Unity Editor version:|COMMAND LINE ARGUMENTS|Aborting batchmode|Scripts have compiler errors|Build failed, with exit code|Build succeeded|\[Licensing::Module\]'
if grep -qhE "$editor_log_markers" $logs 2>/dev/null; then
  editor_log=yes
else
  editor_log=no
fi
say "- Editor log recognised among them: **$editor_log**"
echo "::notice title=Unity diagnostics::Inspected $inspected; editor log recognised: $editor_log."

# Evidence of failure from the logs themselves, so this works without the workflow's outcomes.
if grep -qhE 'Build failed, with exit code|Aborting batchmode due to failure|Unity aborted before completing|Scripts have compiler errors|error CS[0-9]+:' $logs 2>/dev/null; then
  step_failed=yes
fi

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
    emit_error "file=$file,line=$lineno,col=$column,title=Unity compile error::$message"
    reported=$((reported + 1))
  done <<EOF
$error_lines
EOF
  if [ "$error_count" -gt "$reported" ]; then
    emit_error "title=Unity compile errors::$error_count unique compile errors; $reported reported here, all of them are in the Unity log."
  fi
fi

# ---- 2. why batchmode aborted ------------------------------------------------------------
abort_reasons="$(grep -hA1 'Aborting batchmode due to failure' $logs 2>/dev/null | detimestamp \
  | grep -v 'Aborting batchmode due to failure' | grep -vE '^(--)?$' | sort -u)"
if [ -n "$abort_reasons" ]; then
  while IFS= read -r reason; do
    [ -z "$reason" ] && continue
    say "- Unity aborted batchmode: $reason"
    emit_error "title=Unity aborted batchmode::$reason"
  done <<EOF
$abort_reasons
EOF
fi

aborted="$(grep -hoE 'Unity aborted before completing: .*' $logs 2>/dev/null | detimestamp | sort -u | head -1)"
if [ -n "$aborted" ]; then
  say "- GameCI: $aborted"
  emit_error "title=Unity aborted::$aborted"
fi

# An -executeMethod that Unity cannot resolve (renamed/uncompiled Editor assembly) aborts
# before any of our code runs, and says so in one line.
method_errors="$(grep -hE 'executeMethod' $logs 2>/dev/null \
  | grep -iE 'could not be found|not found|does not exist|no such method' \
  | detimestamp | sort -u | head -3)"
if [ -n "$method_errors" ]; then
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    say "- Build method unresolved: $line"
    emit_error "title=Unity build method not found::$line"
  done <<EOF
$method_errors
EOF
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
    emit_error "title=Unity package resolution::$line"
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
# Our own entry points write Logs/wayfinder-editor-failure.log before rethrowing, so the cause
# of a Setup/Build failure survives even when only workspace files can be read.
breadcrumbs="$(grep -hoE '\[Wayfinder\] Editor method failed: .*' $logs 2>/dev/null | detimestamp | sort -u | head -5)"
if [ -n "$breadcrumbs" ]; then
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    say "- $line"
    emit_error "title=Wayfinder editor method failed::$line"
  done <<EOF
$breadcrumbs
EOF
fi

exceptions="$(grep -hoE '[A-Za-z][A-Za-z0-9_.]*Exception: .*' $logs 2>/dev/null \
  | detimestamp | sort -u | head -10)"
if [ -n "$exceptions" ]; then
  say '```'
  printf '%s\n' "$exceptions" >>"$summary"
  say '```'
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    emit_error "title=Unity exception::$line"
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

# ---- 8. never be silent about a failure --------------------------------------------------
if [ "$step_failed" = yes ] && [ "$editor_log" = no ]; then
  say "- **No editor log was available.** game-ci runs \`unity-editor -logfile /dev/stdout\`, so Unity's log is the job log, not a workspace file."
  missing="A Unity step failed but no editor log was inspected ($inspected). game-ci runs unity-editor with -logfile /dev/stdout, so the editor log is the job log; the \"Explain the Unity result\" job downloads it from the Actions API and reruns this script over it."
  if [ "$errors_emitted" -gt 0 ]; then
    # A cause was already reported (e.g. our own breadcrumb file): context, not the headline.
    echo "::notice title=Unity editor log missing::$missing"
  else
    emit_error "title=Unity editor log missing::$missing"
  fi
fi

if [ "$step_failed" = yes ] && [ "$errors_emitted" -eq 0 ]; then
  # Unrecognised failure: publish the end of the biggest log so the cause is still readable
  # from the annotations API instead of leaving an empty "Build failed with exit code 1".
  biggest=""
  biggest_size=0
  for log in $logs; do
    [ -f "$log" ] || continue
    size="$(wc -c <"$log" 2>/dev/null | tr -d ' ')"
    if [ "${size:-0}" -ge "$biggest_size" ]; then
      biggest="$log"
      biggest_size="$size"
    fi
  done
  tail_text=""
  if [ -n "$biggest" ]; then
    tail_text="$(grep -v '^[[:space:]]*$' "$biggest" 2>/dev/null | detimestamp | tail -40 | cut -c1-200)"
  fi
  say "- **Failure with no recognised cause.** Tail of ${biggest#"$workspace"/}:"
  say '```'
  printf '%s\n' "$tail_text" >>"$summary"
  say '```'
  emit_error "title=Unity failure not explained::A Unity step failed but no compile error, abort reason, package failure or exception matched in: $inspected. The log tail is in the next annotation and in the job summary."
  echo "::notice title=Unity log tail::$(escape "$tail_text")"
fi

if [ "$error_count" -eq 0 ] && [ -z "$aborted" ]; then
  say "- No compile error or batchmode abort found in the logs inspected."
fi

exit 0
