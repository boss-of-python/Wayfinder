#!/usr/bin/env bash
# Fail fast when the Unity license secrets are missing, before the ~9 GB editor image is pulled.
#
# Never prints a secret value. Reads UNITY_LICENSE / UNITY_EMAIL / UNITY_PASSWORD from the
# environment and emits GitHub Actions annotations:
#   ::error::   a secret is absent  -> exit 1
#   ::warning:: a secret has leading/trailing whitespace (Unity activation rejects that)
#
# Outside CI (no GITHUB_STEP_SUMMARY needed) it works the same way; annotations are just stdout
# lines that only GitHub interprets.
set -u

missing=""
padded=""
for name in UNITY_LICENSE UNITY_EMAIL UNITY_PASSWORD; do
  value="${!name-}"
  if [ -z "$value" ]; then
    missing="$missing $name"
  else
    trimmed="$(printf '%s' "$value" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
    if [ "$value" != "$trimmed" ]; then
      padded="$padded $name"
    fi
  fi
done

if [ -n "$padded" ]; then
  echo "::warning title=Unity secrets::Leading/trailing whitespace in:$padded - Unity activation can reject a padded license file."
fi

if [ -n "$missing" ]; then
  echo "::error title=Unity secrets missing::Missing repository secret(s):$missing. Add them under Settings -> Secrets and variables -> Actions before running this workflow."
  exit 1
fi

echo "All three Unity license secrets are present (values not printed)."
exit 0
