#!/usr/bin/env python3
"""Pin the Wayfinder toolchain to a real Unity 6.3 LTS editor.

Why this exists (D-004 / KI-002): the repository was authored in an environment with no
Unity editor and no access to Unity's package registry, and the blueprint forbids inventing
editor patch numbers or package versions. Run this ONCE on a machine with the editor:

    python3 Tools/Build/pin_toolchain.py --editor-version 6000.3.<patch>f1

It writes ProjectSettings/ProjectVersion.txt from the template and validates that
Packages/manifest.json exists. Then open the project in Unity so the Package Manager
resolves dependencies and writes Packages/packages-lock.json — commit both.

The committed ProjectSettings/ProjectVersion.txt is authoritative, so this script is
idempotent: it leaves a file that already pins the requested version untouched (this is
what the cloud build workflow relies on — it calls this before every Unity step and must
not drop the verified m_EditorVersionWithRevision hash). It refuses to silently change a
committed pin to a different version. Pass --editor-revision with the changeset hash Unity
prints ("Unity Editor version: 6000.3.26f1 (de8167e7440c)") to record the full pin.
"""

import argparse
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
PROJECT_SETTINGS = os.path.join(ROOT, "ProjectSettings")
VERSION_TXT = os.path.join(PROJECT_SETTINGS, "ProjectVersion.txt")
TEMPLATE = os.path.join(PROJECT_SETTINGS, "ProjectVersion.template.txt")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--editor-version", required=True,
                        help="Full Unity editor version, e.g. 6000.3.2f1 (must be a real 6000.3.x patch)")
    parser.add_argument("--editor-revision", default=None,
                        help="Optional Unity changeset hash, e.g. de8167e7440c (Unity prints it as "
                             "'Unity Editor version: 6000.3.26f1 (de8167e7440c)'). Never invent it.")
    args = parser.parse_args()

    version = args.editor_version.strip()
    if not re.match(r"^6000\.3\.\d+[abfp]\d+$", version):
        print(f"ERROR: '{version}' does not look like a Unity 6.3 LTS version (expected 6000.3.<n>[abfp]<n>).")
        print("Use the exact version string shown in Unity Hub for the editor you installed.")
        return 1

    revision = args.editor_revision.strip() if args.editor_revision else ""
    if revision and not re.match(r"^[0-9a-f]{12}$", revision):
        print(f"ERROR: '{revision}' does not look like a Unity changeset hash (expected 12 hex characters).")
        return 1

    os.makedirs(PROJECT_SETTINGS, exist_ok=True)

    # Idempotent: a committed ProjectVersion.txt is the authoritative pin (KI-002 resolved from a
    # real editor log). Do not rewrite it — that would drop the verified m_EditorVersionWithRevision.
    if os.path.exists(VERSION_TXT):
        with open(VERSION_TXT, encoding="utf-8") as f:
            existing = f.read()
        pinned = re.search(r"^m_EditorVersion:\s*(\S+)\s*$", existing, re.MULTILINE)
        if pinned and pinned.group(1) == version:
            print(f"{os.path.relpath(VERSION_TXT, ROOT)} already pins {version} — left untouched.")
            return check_manifest(ROOT)
        if pinned:
            print(f"ERROR: {os.path.relpath(VERSION_TXT, ROOT)} pins {pinned.group(1)} but "
                  f"{version} was requested. Change one of them deliberately (D-004).")
            return 1

    with open(VERSION_TXT, "w", encoding="utf-8") as f:
        f.write(f"m_EditorVersion: {version}\n")
        if revision:
            f.write(f"m_EditorVersionWithRevision: {version} ({revision})\n")
        # Without --editor-revision the hash is deliberately omitted: Unity writes the real one on
        # first open, and fabricating it is forbidden (Blueprint §15.2).
    print(f"Wrote {os.path.relpath(VERSION_TXT, ROOT)}: {version}"
          + (f" ({revision})" if revision else ""))

    return check_manifest(ROOT)


def check_manifest(root):
    manifest = os.path.join(root, "Packages", "manifest.json")
    if not os.path.exists(manifest):
        print("ERROR: Packages/manifest.json missing — restore it from git before opening the project.")
        return 1

    print("""
NEXT STEPS:
  1. Open the repository root in Unity Hub with this editor (install Android/iOS modules as needed).
  2. Let the Package Manager resolve; if a provisional pin is unavailable, accept the nearest
     compatible version for the 6000.3 stream and record the change in Docs/DECISIONS.md (D-004).
  3. Run  Tools → Wayfinder → Setup Project  in the editor.
  4. Commit: ProjectSettings/ProjectVersion.txt, Packages/packages-lock.json, generated Scenes + .meta files.
  5. Update Docs/BUILD_AND_RUN.md §1 with the pinned version, and set KI-002 to Resolved.""")
    return 0


if __name__ == "__main__":
    sys.exit(main())
