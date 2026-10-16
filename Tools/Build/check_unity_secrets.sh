#!/usr/bin/env bash
# Fail fast unless a supported Unity authentication method is configured, before the ~9 GB
# editor image is pulled. GameCI accepts either a license-file secret, or Unity account
# credentials for Personal-license activation. The 2026-10-15 successful run used the latter
# with UNITY_LICENSE empty; see Docs/evidence/cloud-build-2026-10-15/README.md.
#
# Never prints a secret value. Reads UNITY_LICENSE / UNITY_EMAIL / UNITY_PASSWORD from the
# environment and emits GitHub Actions annotations:
#   ::error::   neither authentication method is configured -> exit 1
#   ::warning:: a configured secret has leading/trailing whitespace
#
# Outside CI (no GITHUB_STEP_SUMMARY needed) it works the same way; annotations are just stdout
# lines that only GitHub interprets.
set -u

license="${UNITY_LICENSE-}"
email="${UNITY_EMAIL-}"
password="${UNITY_PASSWORD-}"

if [ -z "$license" ] && { [ -z "$email" ] || [ -z "$password" ]; }; then
  missing=""
  [ -n "$email" ] || missing="$missing UNITY_EMAIL"
  [ -n "$password" ] || missing="$missing UNITY_PASSWORD"
  echo "::error title=Unity authentication missing::Provide either UNITY_LICENSE or both UNITY_EMAIL and UNITY_PASSWORD. Missing account secret(s):$missing. Add them under Settings -> Secrets and variables -> Actions before running this workflow."
  exit 1
fi

padded=""
for name in UNITY_LICENSE UNITY_EMAIL UNITY_PASSWORD; do
  value="${!name-}"
  if [ -n "$value" ]; then
    trimmed="$(printf '%s' "$value" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
    if [ "$value" != "$trimmed" ]; then
      padded="$padded $name"
    fi
  fi
done

if [ -n "$padded" ]; then
  echo "::warning title=Unity secrets::Leading/trailing whitespace in:$padded - Unity activation can reject padded values."
fi

if [ -n "$license" ]; then
  echo "Unity license-file authentication is configured (value not printed)."
else
  echo "Unity account credentials are configured for Personal-license activation (values not printed)."
fi
exit 0
