#!/usr/bin/env python3
"""Pin the Wayfinder toolchain after a Unity 6.3 LTS editor is installed.

Why this exists (D-004 / KI-002): the repository was authored in an environment with no
Unity editor and no access to Unity's package registry, and the blueprint forbids inventing
editor patch numbers or package versions. Run this ONCE on a machine with the editor:

    python3 Tools/Build/pin_toolchain.py --editor-version 6000.3.<patch>f1

It writes ProjectSettings/ProjectVersion.txt from the template and validates that
Packages/manifest.json exists. Then open the project in Unity so the Package Manager
resolves dependencies and writes Packages/packages-lock.json — commit both.
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
    args = parser.parse_args()

    version = args.editor_version.strip()
    if not re.match(r"^6000\.3\.\d+[abfp]\d+$", version):
        print(f"ERROR: '{version}' does not look like a Unity 6.3 LTS version (expected 6000.3.<n>[abfp]<n>).")
        print("Use the exact version string shown in Unity Hub for the editor you installed.")
        return 1

    os.makedirs(PROJECT_SETTINGS, exist_ok=True)
    with open(VERSION_TXT, "w", encoding="utf-8") as f:
        f.write(f"m_EditorVersion: {version}\n")
        # Unity writes the actual revision hash on first open; never fabricate it.
    print(f"Wrote {os.path.relpath(VERSION_TXT, ROOT)}: {version}")

    manifest = os.path.join(ROOT, "Packages", "manifest.json")
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
